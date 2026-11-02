using System;
using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Dsp;
using VoiceHorror.Persistence;

namespace VoiceHorror.Gameplay.Weapons
{
    /// <summary>
    /// Spec §2.6 "الإطلاق". Click to open a performance window, sing/say the weapon's reference,
    /// the window closes on 400 ms of silence (or max length), the performance is scored on a worker
    /// thread, and the score becomes damage. All the while the voice is ordinary noise for the creatures.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        public enum Phase { Ready, Performing, Scoring, Cooldown }

        public sealed class WeaponSlot
        {
            public WeaponDefinition def;
            public bool unlocked;
            public float cooldownLeft;
            public bool HasReference => ReferenceStore.Has(def.id);
        }

        public readonly List<WeaponSlot> Slots = new List<WeaponSlot>();
        public int SelectedIndex { get; private set; }
        public WeaponSlot Selected => SelectedIndex >= 0 && SelectedIndex < Slots.Count ? Slots[SelectedIndex] : null;
        public Phase Current { get; private set; } = Phase.Ready;
        public float PerformElapsed => _detector.Elapsed;
        public float PerformMax => _detector.MaxDurationSec;
        public bool VoiceStarted => _detector.VoiceStarted;

        public ScoreResult LastScore { get; private set; }
        public float LastDamage { get; private set; }
        public string LastWeaponId { get; private set; }
        public float LastResultTime { get; private set; } = -100f;
        public bool LastWasNoVoice { get; private set; }

        /// <summary>Raised when the player tries to use a weapon that has no recorded reference.</summary>
        public event Action<WeaponDefinition> NeedsRecording;
        public event Action<WeaponDefinition, ScoreResult, float> Fired;
        public event Action<WeaponDefinition> Unlocked;

        public Transform aimOrigin;

        readonly EndOfPerformanceDetector _detector = new EndOfPerformanceDetector();
        VoiceRecorder _recorder;
        ScoringService.Job<(ScoreResult score, TakeFeatures perf)> _job;
        WeaponSlot _performingSlot;
        readonly Collider[] _overlap = new Collider[32];
        readonly RaycastHit[] _rayHits = new RaycastHit[16];

        void Awake()
        {
            _recorder = GetComponent<VoiceRecorder>();
            if (_recorder == null) _recorder = gameObject.AddComponent<VoiceRecorder>();
            if (aimOrigin == null && Camera.main != null) aimOrigin = Camera.main.transform;
            foreach (var w in GameConfig.Instance.weapons)
            {
                if (w == null) continue;
                Slots.Add(new WeaponSlot { def = w, unlocked = w.unlockedAtStart });
            }
            float longest = 4f;
            foreach (var s in Slots) longest = Mathf.Max(longest, s.def.maxDurationSec * 1.5f);
            _recorder.capacitySec = longest + 2f;
            SelectedIndex = Slots.FindIndex(s => s.unlocked);
        }

        void OnEnable()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel += OnChunkLevel;
        }

        void OnDisable()
        {
            if (VoiceLevelMeter.Instance != null) VoiceLevelMeter.Instance.ChunkLevel -= OnChunkLevel;
        }

        void Start()
        {
            // VoiceLevelMeter may have been created after our OnEnable.
            if (VoiceLevelMeter.Instance != null)
            {
                VoiceLevelMeter.Instance.ChunkLevel -= OnChunkLevel;
                VoiceLevelMeter.Instance.ChunkLevel += OnChunkLevel;
            }
        }

        public void Unlock(string weaponId)
        {
            var slot = Slots.Find(s => s.def.id == weaponId);
            if (slot == null || slot.unlocked) return;
            slot.unlocked = true;
            SelectedIndex = Slots.IndexOf(slot);
            Unlocked?.Invoke(slot.def);
        }

        public bool AllUnlockedHaveReferences()
        {
            foreach (var s in Slots) if (s.unlocked && !s.HasReference) return false;
            return true;
        }

        public WeaponDefinition FirstUnlockedWithoutReference()
        {
            foreach (var s in Slots) if (s.unlocked && !s.HasReference) return s.def;
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var s in Slots) s.cooldownLeft = Mathf.Max(0f, s.cooldownLeft - dt);

            if (Current == Phase.Scoring && _job != null && _job.IsDone) ResolveScore();
            if (Current == Phase.Cooldown && (_performingSlot == null || _performingSlot.cooldownLeft <= 0f)) Current = Phase.Ready;

            if (GameState.Paused || GameState.InputBlocked) return;

            if (Current == Phase.Ready || Current == Phase.Cooldown)
            {
                if (InputReader.WasPressed(InputReader.Action.Weapon1)) Select(0);
                if (InputReader.WasPressed(InputReader.Action.Weapon2)) Select(1);
                if (InputReader.WasPressed(InputReader.Action.Weapon3)) Select(2);
            }

            if (Current == Phase.Ready && InputReader.WasPressed(InputReader.Action.Fire)) TryBeginPerformance();
            else if (Current == Phase.Performing && InputReader.WasPressed(InputReader.Action.Cancel)) CancelPerformance();
        }

        void Select(int index)
        {
            if (index < 0 || index >= Slots.Count || !Slots[index].unlocked) return;
            SelectedIndex = index;
        }

        public bool TryBeginPerformance()
        {
            var slot = Selected;
            if (slot == null || !slot.unlocked || slot.cooldownLeft > 0f) return false;
            if (!slot.HasReference) { NeedsRecording?.Invoke(slot.def); return false; }
            var mic = MicCapture.Instance;
            if (mic == null || !mic.IsHealthy) return false;

            var t = GameConfig.Tuning;
            _detector.EndSilenceSec = t.endSilenceMs / 1000f;
            _detector.MaxDurationSec = slot.def.maxDurationSec * 1.5f;
            _detector.NoVoiceTimeoutSec = t.noVoiceTimeoutSec;
            _detector.VoiceThresholdDb = VoiceLevelMeter.Instance != null ? VoiceLevelMeter.Instance.VoiceStartDb : -50f;
            _detector.Reset();
            _performingSlot = slot;
            _recorder.Begin();
            Current = Phase.Performing;
            return true;
        }

        public void CancelPerformance()
        {
            if (Current != Phase.Performing) return;
            _recorder.Cancel();
            Current = Phase.Ready;
        }

        void OnChunkLevel(float db, float dt)
        {
            if (Current != Phase.Performing) return;
            var state = _detector.Feed(db, dt);
            if (state == EndOfPerformanceDetector.State.Running) return;

            var audio = _recorder.End();
            if (state == EndOfPerformanceDetector.State.NoVoice)
            {
                LastWasNoVoice = true;
                LastResultTime = Time.time;
                LastWeaponId = _performingSlot.def.id;
                Current = Phase.Ready;
                return;
            }
            LastWasNoVoice = false;
            var reference = ReferenceStore.Get(_performingSlot.def.id);
            _job = ScoringService.ScoreAsync(reference, audio, _recorder.SampleRate);
            Current = Phase.Scoring;
        }

        void ResolveScore()
        {
            ScoringService.LogIfFailed(_job, "performance scoring");
            var slot = _performingSlot;
            var def = slot.def;
            ScoreResult score = _job.Failed ? ScoreResult.Empty : _job.Result.score;
            _job = null;

            float damage = def.DamageFor(score.Final);
            LastScore = score;
            LastDamage = damage;
            LastWeaponId = def.id;
            LastResultTime = Time.time;

            Vector3 origin = aimOrigin != null ? aimOrigin.position : transform.position + Vector3.up * 1.6f;
            Vector3 dir = aimOrigin != null ? aimOrigin.forward : transform.forward;
            float quality = Mathf.Clamp01(score.Final / 100f);
            if (damage > 0f) Deliver(def, origin, dir, damage, quality);
            WeaponEffect.Spawn(def, origin, dir, quality, damage > 0f, GameConfig.Instance.effectMaterial);

            slot.cooldownLeft = damage > 0f ? def.cooldownSec : def.cooldownSec * 0.5f;
            Current = Phase.Cooldown;
            Fired?.Invoke(def, score, damage);
        }

        void Deliver(WeaponDefinition def, Vector3 origin, Vector3 dir, float damage, float quality)
        {
            float stagger = def.staggerSec * Mathf.Lerp(0.5f, 1.2f, quality);
            switch (def.delivery)
            {
                case WeaponDelivery.Beam:
                {
                    int n = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.1f, def.spread), dir, _rayHits, def.range, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(_rayHits, 0, n, HitDistanceComparer.Instance);
                    for (int i = 0; i < n; i++)
                    {
                        var h = _rayHits[i];
                        if (h.collider.transform.IsChildOf(transform)) continue;
                        var dmg = h.collider.GetComponentInParent<IDamageable>();
                        if (dmg != null && dmg.IsAlive)
                        {
                            dmg.TakeDamage(damage, origin, def.tier, stagger);
                            break;
                        }
                        if (h.collider.GetComponentInParent<Noise.Occluder>() != null) break; // walls stop the beam
                    }
                    break;
                }
                case WeaponDelivery.Cone:
                case WeaponDelivery.Shockwave:
                {
                    Vector3 center = def.delivery == WeaponDelivery.Shockwave ? transform.position + Vector3.up : origin;
                    int n = Physics.OverlapSphereNonAlloc(center, def.range, _overlap, ~0, QueryTriggerInteraction.Ignore);
                    var done = new HashSet<IDamageable>();
                    for (int i = 0; i < n; i++)
                    {
                        var c = _overlap[i];
                        if (c.transform.IsChildOf(transform)) continue;
                        var dmg = c.GetComponentInParent<IDamageable>();
                        if (dmg == null || !dmg.IsAlive || done.Contains(dmg)) continue;
                        Vector3 to = c.bounds.center - center;
                        if (def.delivery == WeaponDelivery.Cone && Vector3.Angle(dir, to) > def.spread) continue;
                        if (NoiseBlocked(center, c.bounds.center)) continue;
                        done.Add(dmg);
                        float falloff = def.delivery == WeaponDelivery.Shockwave ? Mathf.Lerp(1f, 0.5f, to.magnitude / def.range) : 1f;
                        dmg.TakeDamage(damage * falloff, center, def.tier, stagger);
                    }
                    break;
                }
            }
        }

        bool NoiseBlocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            int n = Physics.RaycastNonAlloc(a, d.normalized, _rayHits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_rayHits[i].collider.GetComponentInParent<Noise.Occluder>() != null) return true;
            return false;
        }

        sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
