#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VoiceBattle.EditorTools
{
    /// <summary>
    /// Makes the unity/ folder open-and-play: on load it builds the engine (dotnet publish) and copies the DLLs and
    /// data/ in when they are missing or older than src/ and data/, and keeps the VB_STEAMWORKS define in step with
    /// the Steamworks.NET package. Lives in its own assembly (VoiceBattle.Sync.Editor.asmdef) with no reference to the
    /// game scripts, so it still runs when they cannot compile yet because the DLLs are missing.
    /// Same file lists as unity/sync.sh and unity/sync.ps1 — change all three together.
    /// </summary>
    [InitializeOnLoad]
    public static class EngineSync
    {
        public static readonly string[] Dlls =
        {
            "VoiceMatch.Core", "VoiceBattle.Session", "VoiceBattle.Sounds", "VoiceBattle.Net", "BattleRules.Core",
            "NVorbis", "NLayer", "Concentus", "System.Text.Json", "System.Text.Encodings.Web", "Microsoft.Bcl.AsyncInterfaces",
        };

        public static readonly string[] DataFiles =
        {
            "match_config.json", "moves.json", "battle.json", "characters.json", "visuals.json", "sounds.json", "modes.json", "voice.json", "steam.json",
        };

        private const string SteamDefine = "VB_STEAMWORKS";
        private const string SteamAssembly = "com.rlabrecque.steamworks.net";
        private const string AutoSyncPref = "VoiceBattle.AutoSync";

        /// <summary>unity/ is the project; the repository (src/, data/, tools/) is its parent.</summary>
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string PluginsDir => Path.Combine(Application.dataPath, "Plugins", "VoiceBattle");
        public static string DataOut => Path.Combine(Application.dataPath, "StreamingAssets", "data");
        // Written by this class and by unity/sync.sh / sync.ps1 (Unity ignores dot-files; the folder is git-ignored).
        private static string Stamp => Path.Combine(PluginsDir, ".sync-stamp");

        static EngineSync()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                UpdateSteamDefine();
                if (EditorPrefs.GetBool(AutoSyncPref, true) && NeedsSync(out string why))
                {
                    Debug.Log("[Voice Battle] syncing engine and data: " + why);
                    Sync(interactive: false);
                }
            };
        }

        // ---------------------------------------------------------------- sync

        [MenuItem("Voice Battle/Sync engine and data", priority = 1)]
        public static void SyncMenu() => Sync(interactive: true);

        [MenuItem("Voice Battle/Auto-sync on load", priority = 2)]
        private static void ToggleAutoSync() => EditorPrefs.SetBool(AutoSyncPref, !EditorPrefs.GetBool(AutoSyncPref, true));

        [MenuItem("Voice Battle/Auto-sync on load", true)]
        private static bool ToggleAutoSyncCheck()
        {
            Menu.SetChecked("Voice Battle/Auto-sync on load", EditorPrefs.GetBool(AutoSyncPref, true));
            return true;
        }

        /// <summary>True when a DLL or data file is missing, or src/ or data/ changed since the last sync.</summary>
        public static bool NeedsSync(out string why)
        {
            if (!Directory.Exists(Path.Combine(RepoRoot, "src"))) { why = ""; return false; }   // project copied without the repo
            var missingDll = Dlls.FirstOrDefault(d => !File.Exists(Path.Combine(PluginsDir, d + ".dll")));
            if (missingDll != null) { why = missingDll + ".dll missing"; return true; }
            var missingData = DataFiles.FirstOrDefault(f => !File.Exists(Path.Combine(DataOut, f)));
            if (missingData != null) { why = missingData + " missing"; return true; }
            if (!File.Exists(Stamp)) { why = "never synced"; return true; }
            var synced = File.GetLastWriteTimeUtc(Stamp);
            var newer = SourceFiles().FirstOrDefault(f => File.GetLastWriteTimeUtc(f) > synced);
            why = newer != null ? Path.GetFileName(newer) + " changed" : "";
            return newer != null;
        }

        private static IEnumerable<string> SourceFiles()
        {
            string src = Path.Combine(RepoRoot, "src"), data = Path.Combine(RepoRoot, "data");
            foreach (var f in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
            {
                if (f.Contains(".Tests") || f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                    || f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (f.EndsWith(".cs") || f.EndsWith(".csproj")) yield return f;
            }
            foreach (var f in DataFiles) yield return Path.Combine(data, f);
            foreach (var f in Directory.EnumerateFiles(Path.Combine(data, "Packs"), "*.json")) yield return f;
            foreach (var f in Directory.EnumerateFiles(Path.Combine(data, "midi"), "*.mid")) yield return f;
        }

        /// <summary>dotnet publish src/VoiceBattle.Session, copy the engine DLLs to Plugins/VoiceBattle and data/ to StreamingAssets/data.</summary>
        public static bool Sync(bool interactive)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "vb_sync_" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyData();
                string? dotnet = FindDotnet();
                if (dotnet == null)
                {
                    Fail(interactive, "The .NET SDK 8 was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0, restart Unity, then run Voice Battle → Sync engine and data. (Data was copied; the engine DLLs were not.)");
                    return false;
                }
                EditorUtility.DisplayProgressBar("Voice Battle", "Building the engine (dotnet publish)… the first time downloads NuGet packages", .3f);
                var (code, output) = Run(dotnet, $"publish \"{Path.Combine(RepoRoot, "src", "VoiceBattle.Session")}\" -c Release -o \"{tmp}\" --nologo", RepoRoot, 600_000);
                if (code != 0) { Fail(interactive, "dotnet publish failed (exit " + code + "):\n" + Tail(output, 30)); return false; }
                Directory.CreateDirectory(PluginsDir);
                foreach (var dll in Dlls) File.Copy(Path.Combine(tmp, dll + ".dll"), Path.Combine(PluginsDir, dll + ".dll"), true);
                Directory.CreateDirectory(Path.GetDirectoryName(Stamp)!);
                File.WriteAllText(Stamp, DateTime.UtcNow.ToString("o"));
                Debug.Log($"[Voice Battle] engine DLLs → {PluginsDir}, data → {DataOut}");
                return true;
            }
            catch (Exception ex) { Fail(interactive, "Sync failed: " + ex.Message); return false; }
            finally
            {
                EditorUtility.ClearProgressBar();
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { }
                AssetDatabase.Refresh();
            }
        }

        private static void CopyData()
        {
            string data = Path.Combine(RepoRoot, "data");
            Directory.CreateDirectory(Path.Combine(DataOut, "Packs"));
            Directory.CreateDirectory(Path.Combine(DataOut, "midi"));
            foreach (var f in DataFiles) File.Copy(Path.Combine(data, f), Path.Combine(DataOut, f), true);
            foreach (var f in Directory.GetFiles(Path.Combine(data, "Packs"), "*.json")) File.Copy(f, Path.Combine(DataOut, "Packs", Path.GetFileName(f)), true);
            foreach (var f in Directory.GetFiles(Path.Combine(data, "midi"), "*.mid")) File.Copy(f, Path.Combine(DataOut, "midi", Path.GetFileName(f)), true);
        }

        /// <summary>Unity started from the Dock/Hub often has a shorter PATH than a terminal, so look in the usual places too.</summary>
        public static string? FindDotnet()
        {
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
            var dirs = new List<string>();
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(root)) dirs.Add(root!);
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.AddRange(new[]
            {
                Path.Combine(home, ".dotnet"), @"C:\Program Files\dotnet", "/usr/local/share/dotnet", "/opt/homebrew/bin",
                "/usr/local/bin", "/usr/share/dotnet", "/usr/lib/dotnet", "/snap/bin",
            });
            foreach (var d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string p = Path.Combine(d.Trim(), exe);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static (int code, string output) Run(string file, string args, string cwd, int timeoutMs)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            psi.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            var sb = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (InvalidOperationException) { } return (-1, "timed out"); }
            p.WaitForExit();
            lock (sb) return (p.ExitCode, sb.ToString());
        }

        private static string Tail(string s, int lines) => string.Join("\n", s.Split('\n').Reverse().Take(lines).Reverse());

        public static void Fail(bool interactive, string message)
        {
            Debug.LogError("[Voice Battle] " + message);
            if (interactive) EditorUtility.DisplayDialog("Voice Battle", message, "OK");
        }

        // ---------------------------------------------------------------- Steam define

        /// <summary>SteamBridge uses Steamworks only when the package is installed, so the project compiles either way.</summary>
        public static void UpdateSteamDefine()
        {
            bool installed = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == SteamAssembly);
            var target = NamedBuildTarget.Standalone;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
            bool has = defines.Contains(SteamDefine);
            if (installed == has) return;
            if (installed) defines.Add(SteamDefine); else defines.Remove(SteamDefine);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log(installed ? "[Voice Battle] Steamworks.NET found — Steam enabled" : "[Voice Battle] Steamworks.NET not installed — Steam disabled");
        }

        // ---------------------------------------------------------------- server

        [MenuItem("Voice Battle/Run local server (port 7777)", priority = 60)]
        public static void RunServer()
        {
            string? dotnet = FindDotnet();
            if (dotnet == null) { Fail(true, "The .NET SDK 8 was not found."); return; }
            string args = $"run --project \"{Path.Combine(RepoRoot, "tools", "voice-battle-server")}\" -c Release";
            ProcessStartInfo psi;
            if (Application.platform == RuntimePlatform.WindowsEditor) psi = new ProcessStartInfo("cmd.exe", $"/k \"\"{dotnet}\" {args}\"");
            else if (Application.platform == RuntimePlatform.OSXEditor)
                psi = new ProcessStartInfo("osascript", $"-e 'tell application \"Terminal\" to do script \"cd \\\"{RepoRoot}\\\" && \\\"{dotnet}\\\" {args.Replace("\"", "\\\"")}\"'");
            else psi = new ProcessStartInfo("x-terminal-emulator", $"-e \"{dotnet}\" {args}");
            psi.WorkingDirectory = RepoRoot;
            psi.UseShellExecute = true;
            try { Process.Start(psi); Debug.Log("[Voice Battle] server starting in a terminal window; connect to 127.0.0.1:7777"); }
            catch (Exception ex) { Fail(true, "Could not open a terminal: " + ex.Message + "\nRun it yourself: dotnet run --project tools/voice-battle-server -c Release"); }
        }

        [MenuItem("Voice Battle/Open docs (steam.md)", priority = 80)]
        private static void OpenDocs() => EditorUtility.RevealInFinder(Path.Combine(RepoRoot, "docs", "steam.md"));
    }
}
