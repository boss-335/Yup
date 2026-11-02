using System;
using UnityEngine;
using VoiceHorror.Core;

namespace VoiceHorror.Gameplay.Noise
{
    /// <summary>
    /// Spec §2.4 threat 0–100: rises with the player's noise, decays with silence.
    /// Drives music, lighting and the creature director.
    /// </summary>
    public sealed class ThreatMeter : MonoBehaviour
    {
        public static ThreatMeter Instance { get; private set; }

        [Range(0f, 100f)] public float threat;
        public float Threat01 => threat / 100f;
        public event Action<float> CrossedUp;

        static readonly float[] Thresholds = { 25f, 50f, 75f };
        float _silentFor;
        float _lastThreshold;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called every frame by the player noise emitter.</summary>
        public void Feed(float loudness01, float radius, float loudRadius, float dt)
        {
            var b = GameConfig.Balance;
            if (loudness01 <= 0.001f)
            {
                _silentFor += dt;
                if (_silentFor > b.threatDecayDelay) threat -= b.threatDecayPerSec * dt;
            }
            else
            {
                _silentFor = 0f;
                float mult = radius > loudRadius ? b.screamThreatMultiplier : 1f;
                threat += b.threatRisePerSec * loudness01 * mult * dt;
            }
            threat = Mathf.Clamp(threat, 0f, 100f);

            foreach (var t in Thresholds)
            {
                if (threat >= t && _lastThreshold < t) CrossedUp?.Invoke(t);
            }
            _lastThreshold = threat;
        }

        public void Add(float amount) => threat = Mathf.Clamp(threat + amount, 0f, 100f);
    }
}
