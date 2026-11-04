#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoiceBattle.Session.Online;
using VoiceBattle.UnityLayer;
using Debug = UnityEngine.Debug;

namespace VoiceBattle.EditorTools
{
    /// <summary>"Voice Battle" menu: create the main scene, apply player settings, build Windows / macOS / Linux players.
    /// Engine + data sync is <see cref="EngineSync"/> (separate assembly).</summary>
    public static class VoiceBattleEditor
    {
        private const string ScenePath = "Assets/VoiceBattle/Scenes/Main.unity";
        private static string ProjectRoot => EngineSync.ProjectRoot;
        private static string DataOut => EngineSync.DataOut;

        // ---------------------------------------------------------------- scene + player settings

        [MenuItem("Voice Battle/Create main scene", priority = 20)]
        public static void CreateMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.07f, 0.05f, 0.12f);
            c.orthographic = true;
            cam.AddComponent<AudioListener>();
            new GameObject("Voice Battle").AddComponent<MainMenu>();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(ProjectRoot, ScenePath))!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyPlayerSettings();
            Debug.Log("[Voice Battle] " + ScenePath + " created and set as the only build scene. Press Play.");
        }

        /// <summary>Name, microphone permission text, windowed by default, keeps running when unfocused (voice chat).</summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Voice Battle";
            PlayerSettings.productName = "Voice Battle";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            // macOS asks for microphone permission with this text (property name differs between Unity versions).
            var mac = typeof(PlayerSettings).GetNestedType("macOS");
            var mic = mac?.GetProperty("microphoneUsageDescription");
            if (mic != null && mic.CanWrite) mic.SetValue(null, "Voice Battle listens to your singing to attack, and carries your voice to your friends.");
        }

        // ---------------------------------------------------------------- builds

        [MenuItem("Voice Battle/Build/Windows (64-bit)", priority = 40)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", "VoiceBattle.exe");

        [MenuItem("Voice Battle/Build/macOS", priority = 41)]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "macOS", "VoiceBattle.app");

        [MenuItem("Voice Battle/Build/Linux (64-bit)", priority = 42)]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Linux", "VoiceBattle.x86_64");

        /// <summary>Builds into unity/Builds/&lt;platform&gt; — the content folder tools/steam/depot_*.vdf uploads.</summary>
        public static void Build(BuildTarget target, string folder, string exe)
        {
            if (!EngineSync.Sync(interactive: !Application.isBatchMode)) return;
            if (!File.Exists(Path.Combine(ProjectRoot, ScenePath))) CreateMainScene();
            ApplyPlayerSettings();
            string outDir = Path.Combine(ProjectRoot, "Builds", folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, exe),
                target = target,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                EngineSync.Fail(!Application.isBatchMode, $"Build failed: {report.summary.result} ({report.summary.totalErrors} errors) — see the Console.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            // With the test AppID (480) Steam needs steam_appid.txt next to the game; a build for your own AppID
            // that is launched through Steam does not, and the Steam upload must not contain it.
            string appIdFile = Path.Combine(outDir, "steam_appid.txt");
            var cfg = SteamConfig.Load(Path.Combine(DataOut, "steam.json"));
            if (cfg.IsTestApp) File.WriteAllText(appIdFile, cfg.AppId.ToString());
            else if (File.Exists(appIdFile)) File.Delete(appIdFile);
            Debug.Log($"[Voice Battle] built {Path.Combine(outDir, exe)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(Path.Combine(outDir, exe));
        }

        /// <summary>For CI / the command line: Unity -batchmode -quit -projectPath unity -executeMethod VoiceBattle.EditorTools.VoiceBattleEditor.BuildAllBatch</summary>
        public static void BuildAllBatch()
        {
            BuildWindows();
            BuildMac();
            BuildLinux();
        }
    }
}
