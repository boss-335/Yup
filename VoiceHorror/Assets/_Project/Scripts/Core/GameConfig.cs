using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Config;

namespace VoiceHorror.Core
{
    /// <summary>
    /// Scene-level holder for config assets (assigned by the scene builders).
    /// Anything missing falls back to code defaults so a half-built scene still runs.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameConfig : MonoBehaviour
    {
        public VoiceTuning voiceTuning;
        public GameBalance balance;
        public List<WeaponDefinition> weapons = new List<WeaponDefinition>();
        public List<CreatureDefinition> creatures = new List<CreatureDefinition>();
        [Tooltip("Materials used by runtime effects (assigned by the builder so they ship in builds).")]
        public Material effectMaterial;
        public Material creatureGlowMaterial;

        static GameConfig _instance;

        public static GameConfig Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<GameConfig>();
                if (_instance == null)
                {
                    var go = new GameObject("GameConfig (defaults)");
                    _instance = go.AddComponent<GameConfig>();
                }
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (voiceTuning == null) voiceTuning = ScriptableObject.CreateInstance<VoiceTuning>();
            if (balance == null) balance = ScriptableObject.CreateInstance<GameBalance>();
        }

        public static VoiceTuning Tuning
        {
            get
            {
                var i = Instance;
                if (i.voiceTuning == null) i.voiceTuning = ScriptableObject.CreateInstance<VoiceTuning>();
                return i.voiceTuning;
            }
        }

        public static GameBalance Balance
        {
            get
            {
                var i = Instance;
                if (i.balance == null) i.balance = ScriptableObject.CreateInstance<GameBalance>();
                return i.balance;
            }
        }

        public WeaponDefinition FindWeapon(string id)
        {
            foreach (var w in weapons) if (w != null && w.id == id) return w;
            return null;
        }
    }
}
