using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Noise;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>
    /// Every frame, turns the player's real voice level into a NoiseEvent and feeds the threat meter
    /// (spec §2.4). Performances are not special-cased: singing a weapon song is just noise like any other.
    /// </summary>
    public sealed class PlayerNoiseEmitter : MonoBehaviour
    {
        public float CurrentRadius { get; private set; }

        PlayerController _player;

        void Awake() => _player = GetComponent<PlayerController>();

        void Update()
        {
            var meter = VoiceLevelMeter.Instance;
            if (meter == null || GameState.Paused) return;

            float radius = meter.Radius;
            if (_player != null && _player.IsHidden) radius *= GameConfig.Balance.hidingRadiusMultiplier;
            if (World.SafeRoom.PlayerInside) radius = 0f; // the nahham's coral walls swallow every sound
            CurrentRadius = radius;

            NoiseSystem.WallOcclusion = GameConfig.Balance.wallOcclusion;
            NoiseSystem.MaxOccluders = GameConfig.Balance.maxOccluders;
            NoiseSystem.Emit(new NoiseEvent
            {
                position = transform.position,
                target = transform.position,
                radius = radius,
                loudness01 = meter.Loudness01,
                time = Time.time,
                source = NoiseSource.PlayerVoice,
            });

            if (ThreatMeter.Instance != null)
                ThreatMeter.Instance.Feed(radius > 0f ? meter.Loudness01 : 0f, radius, GameConfig.Tuning.loudRadius, Time.deltaTime);
        }
    }
}
