using UnityEngine;

namespace VoiceHorror.Config
{
    /// <summary>Non-voice tunables: threat meter, director, player.</summary>
    [CreateAssetMenu(menuName = "VoiceHorror/Game Balance", fileName = "GameBalance")]
    public sealed class GameBalance : ScriptableObject
    {
        [Header("Threat (0-100)")]
        [Tooltip("Threat gained per second at Loud (loudness01 = 1). Scales linearly with loudness.")]
        public float threatRisePerSec = 22f;
        [Tooltip("Extra multiplier above the Loud boundary (screaming).")]
        public float screamThreatMultiplier = 2.5f;
        public float threatDecayPerSec = 6f;
        [Tooltip("Seconds of silence before threat starts to decay.")]
        public float threatDecayDelay = 2f;

        [Header("Director (threat -> spawns)")]
        public float spawnThreatThreshold = 55f;
        public float panicThreatThreshold = 85f;
        public float spawnCooldownSec = 25f;
        public float panicSpawnCooldownSec = 12f;
        public float minSpawnDistance = 18f;
        public int maxDirectorCreatures = 3;

        [Header("Player")]
        public float maxHealth = 100f;
        public float walkSpeed = 2.6f;
        public float sprintSpeed = 4.6f;
        public float crouchSpeed = 1.4f;
        public float mouseSensitivity = 0.12f;
        [Tooltip("Noise radius of sprinting footsteps (walking and crouching are silent).")]
        public float sprintFootstepRadius = 5f;
        [Tooltip("Hiding in a closet multiplies your voice radius by this.")]
        public float hidingRadiusMultiplier = 0.5f;
        public float healthRegenPerSec = 2f;
        public float healthRegenDelay = 8f;

        [Header("Noise occlusion")]
        [Tooltip("Radius multiplier per wall between the source and the listener.")]
        public float wallOcclusion = 0.6f;
        public int maxOccluders = 3;
    }
}
