using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoiceHorror.Dsp;

namespace VoiceHorror.Persistence
{
    /// <summary>
    /// Everything we write lives under Application.persistentDataPath/VoiceHorror (spec §0.6, §9).
    /// Nothing is ever uploaded.
    /// </summary>
    public static class Storage
    {
        public static string Root
        {
            get
            {
                var p = Path.Combine(Application.persistentDataPath, "VoiceHorror");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string ReferencesDir
        {
            get
            {
                var p = Path.Combine(Root, "references");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string RecordingsDir
        {
            get
            {
                var p = Path.Combine(Root, "recordings");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static bool TryReadText(string path, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(path)) return false;
                text = File.ReadAllText(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Storage] read failed {path}: {e.Message}");
                return false;
            }
        }

        public static void WriteTextAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>"احذف كل تسجيلاتي": removes references and any raw recordings.</summary>
        public static void DeleteAllRecordings()
        {
            TryDeleteDir(Path.Combine(Root, "references"));
            TryDeleteDir(Path.Combine(Root, "recordings"));
            ReferenceStore.ClearCache();
        }

        public static void DeleteEverything()
        {
            TryDeleteDir(Root);
            ReferenceStore.ClearCache();
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { Debug.LogWarning($"[Storage] delete failed {dir}: {e.Message}"); }
        }
    }

    /// <summary>Calibration result (spec §2.2), saved as JSON.</summary>
    [Serializable]
    public sealed class CalibrationProfile
    {
        public int version = 1;
        public string deviceName = "";
        public float noiseFloorDb = -60f;
        public float talkDb = -30f;
        public float loudDb = -18f;
        public string createdUtc = "";

        static string PathFor => System.IO.Path.Combine(Storage.Root, "calibration.json");

        public CalibrationValues ToValues() => new CalibrationValues
        {
            NoiseFloorDb = noiseFloorDb,
            TalkDb = talkDb,
            LoudDb = loudDb,
            Valid = true,
        };

        public static CalibrationProfile From(CalibrationValues v, string device) => new CalibrationProfile
        {
            deviceName = device ?? "",
            noiseFloorDb = v.NoiseFloorDb,
            talkDb = v.TalkDb,
            loudDb = v.LoudDb,
            createdUtc = DateTime.UtcNow.ToString("o"),
        };

        public static bool Exists => File.Exists(PathFor);

        public static CalibrationProfile Load()
        {
            if (!Storage.TryReadText(PathFor, out var json)) return null;
            try { return JsonUtility.FromJson<CalibrationProfile>(json); }
            catch { return null; }
        }

        public void Save() => Storage.WriteTextAtomic(PathFor, JsonUtility.ToJson(this, true));

        public static void Delete()
        {
            if (File.Exists(PathFor)) File.Delete(PathFor);
        }
    }

    /// <summary>Player-facing settings (spec §5), saved as JSON.</summary>
    [Serializable]
    public sealed class PlayerSettingsData
    {
        public int version = 1;
        public string micDevice = "";
        [Tooltip("Extra sensitivity in dB, clamped to ±maxSensitivityDb.")]
        public float sensitivityDb = 0f;
        public bool streamerMode = false;
        public bool keepRawRecordings = false;
        public bool privacyNoticeAccepted = false;
        public string language = "ar";
        public float masterVolume = 0.9f;
        public float mouseSensitivity = 1f;
        public bool showDebugOverlay = false;

        static PlayerSettingsData _current;
        static string PathFor => Path.Combine(Storage.Root, "settings.json");

        public static event Action Changed;

        public static PlayerSettingsData Current
        {
            get
            {
                if (_current != null) return _current;
                if (Storage.TryReadText(PathFor, out var json))
                {
                    try { _current = JsonUtility.FromJson<PlayerSettingsData>(json); } catch { _current = null; }
                }
                if (_current == null) _current = new PlayerSettingsData();
                return _current;
            }
        }

        public static void Save()
        {
            Storage.WriteTextAtomic(PathFor, JsonUtility.ToJson(Current, true));
            Changed?.Invoke();
        }
    }

    /// <summary>Feature-only reference storage per weapon (spec §2.6, §3.4).</summary>
    public static class ReferenceStore
    {
        static readonly Dictionary<string, VoiceReference> Cache = new Dictionary<string, VoiceReference>();

        static string PathFor(string weaponId) => Path.Combine(Storage.ReferencesDir, Sanitize(weaponId) + ".json");

        static string Sanitize(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id;
        }

        public static bool Has(string weaponId) => Get(weaponId) != null;

        public static VoiceReference Get(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            if (Cache.TryGetValue(weaponId, out var r)) return r;
            if (!Storage.TryReadText(PathFor(weaponId), out var json)) return null;
            try
            {
                r = VoiceReference.FromJson(json);
                Cache[weaponId] = r;
                return r;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReferenceStore] bad reference for {weaponId}: {e.Message}");
                return null;
            }
        }

        public static void Save(VoiceReference reference)
        {
            Storage.WriteTextAtomic(PathFor(reference.WeaponId), reference.ToJson());
            Cache[reference.WeaponId] = reference;
        }

        public static void Delete(string weaponId)
        {
            var p = PathFor(weaponId);
            if (File.Exists(p)) File.Delete(p);
            Cache.Remove(weaponId);
        }

        public static void ClearCache() => Cache.Clear();
    }

    /// <summary>16-bit PCM WAV writer, only used when the player opts in to keeping raw takes.</summary>
    public static class WavWriter
    {
        public static void Write(string path, float[] samples, int sampleRate)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(sampleRate);
                w.Write(sampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
        }
    }
}
