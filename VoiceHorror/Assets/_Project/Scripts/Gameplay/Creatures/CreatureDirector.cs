using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.Creatures
{
    /// <summary>
    /// Spec §2.4: threat controls how quickly creatures appear. Above the spawn threshold the director
    /// wakes a creature at a spawn point out of earshot and sends it toward the player's last noise.
    /// Also serves summon requests from the Heart.
    /// </summary>
    public sealed class CreatureDirector : MonoBehaviour
    {
        public static CreatureDirector Instance { get; private set; }

        [Tooltip("Inactive creature used as the spawn template (built by the scene builder).")]
        public GameObject stalkerTemplate;
        public GameObject hunterTemplate;
        public Transform[] spawnPoints;
        [Tooltip("Hunter spawns only once threat passes the panic threshold.")]
        public bool allowHunterSpawns = true;
        public bool directorEnabled = true;

        readonly List<CreatureBrain> _spawned = new List<CreatureBrain>();
        float _cooldown = 10f;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            _spawned.RemoveAll(c => c == null || !c.IsAlive);
            if (!directorEnabled || GameState.Paused) return;
            var threat = ThreatMeter.Instance;
            if (threat == null) return;
            var b = GameConfig.Balance;

            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f || _spawned.Count >= b.maxDirectorCreatures) return;

            if (threat.threat >= b.panicThreatThreshold)
            {
                bool hunter = allowHunterSpawns && hunterTemplate != null && Random.value < 0.4f;
                if (Spawn(hunter ? hunterTemplate : stalkerTemplate, true) != null) _cooldown = b.panicSpawnCooldownSec;
            }
            else if (threat.threat >= b.spawnThreatThreshold)
            {
                if (Spawn(stalkerTemplate, false) != null) _cooldown = b.spawnCooldownSec;
            }
        }

        /// <summary>Spawns a creature at the best spawn point (far enough from the player, preferring out of sight).</summary>
        public CreatureBrain Spawn(GameObject template, bool hunt, Vector3? near = null)
        {
            if (template == null || spawnPoints == null || spawnPoints.Length == 0) return null;
            var pc = PlayerController.Instance;
            Vector3 playerPos = pc != null ? pc.transform.position : Vector3.zero;
            Vector3 origin = near ?? playerPos;
            float minDist = near.HasValue ? 3f : GameConfig.Balance.minSpawnDistance;

            Transform best = null;
            float bestScore = float.MaxValue;
            foreach (var sp in spawnPoints)
            {
                if (sp == null) continue;
                float dPlayer = Vector3.Distance(sp.position, playerPos);
                if (dPlayer < minDist) continue;
                float score = Vector3.Distance(sp.position, origin);
                if (score < bestScore) { bestScore = score; best = sp; }
            }
            if (best == null) return null;

            var go = Instantiate(template, best.position, best.rotation);
            go.name = template.name.Replace("Template_", "") + "_spawn";
            go.SetActive(true);
            var brain = go.GetComponent<CreatureBrain>();
            if (brain != null)
            {
                _spawned.Add(brain);
                var target = NoiseSystem.LastPlayerNoise.time > 0f ? NoiseSystem.LastPlayerNoise.position : playerPos;
                StartCoroutine(SendNextFrame(brain, target, hunt));
            }
            return brain;
        }

        System.Collections.IEnumerator SendNextFrame(CreatureBrain brain, Vector3 target, bool hunt)
        {
            yield return null; // let Start() place the agent on the NavMesh first
            if (brain != null && brain.IsAlive) brain.SendTo(target, hunt);
        }

        public int AliveSpawned => _spawned.Count;
    }
}
