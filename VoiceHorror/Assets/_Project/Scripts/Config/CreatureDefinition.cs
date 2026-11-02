using UnityEngine;

namespace VoiceHorror.Config
{
    public enum CreatureArchetype
    {
        /// <summary>المسكوت — slow, blind wanderer. The baseline creature.</summary>
        Stalker = 0,
        /// <summary>السعلوة — fast, lunges at close range, flees when badly hurt.</summary>
        Hunter = 1,
        /// <summary>بو درياه — immobile, hears very far, wails to send others to the noise.</summary>
        Sentinel = 2,
        /// <summary>الصمّة — the heart of silence. Immobile, summons, resists short songs.</summary>
        Heart = 3,
    }

    [CreateAssetMenu(menuName = "VoiceHorror/Creature", fileName = "Creature")]
    public sealed class CreatureDefinition : ScriptableObject
    {
        public string id = "c_new";
        public string displayNameAr = "مخلوق";
        public string displayNameEn = "Creature";
        [TextArea] public string loreAr = "";
        [TextArea] public string loreEn = "";
        public CreatureArchetype archetype = CreatureArchetype.Stalker;

        [Header("Body")]
        public float maxHealth = 100f;
        public float height = 2.1f;
        public float radius = 0.4f;
        public Color skinColor = new Color(0.55f, 0.52f, 0.48f);
        public Color glowColor = new Color(0.9f, 0.85f, 0.6f);

        [Header("Hearing")]
        [Tooltip("Multiplies the player's noise radius for this creature (1 = hears exactly the radius).")]
        public float hearingMultiplier = 1f;
        [Tooltip("Seconds of accumulated hearing while investigating before it commits to a hunt.")]
        public float huntConfirmSec = 0.8f;
        [Tooltip("Accumulated hearing decays at this rate per second while quiet.")]
        public float hearingDecayPerSec = 0.5f;
        [Tooltip("Within this distance it senses the player even in silence (breath, touch). 0 = never.")]
        public float closeSenseRadius = 1.4f;

        [Header("Movement")]
        public float wanderSpeed = 1.2f;
        public float investigateSpeed = 2.2f;
        public float huntSpeed = 3.6f;
        public float wanderRadius = 10f;
        public float searchRadius = 6f;
        [Tooltip("Seconds of silence while searching before it goes back to idle.")]
        public float searchTimeout = 8f;

        [Header("Attack")]
        public float attackRange = 1.6f;
        public float attackDamage = 35f;
        public float attackCooldown = 1.6f;

        [Header("Hunter")]
        public float lungeRange = 6f;
        public float lungeSpeedMultiplier = 2.2f;
        public float lungeDuration = 0.6f;
        public float lungeCooldown = 4f;
        [Range(0f, 1f)] public float fleeHealthFraction = 0.35f;
        public float fleeDuration = 4f;

        [Header("Sentinel / Heart")]
        public float alertRadius = 45f;
        public float wailDuration = 1.8f;
        public float alertCooldown = 8f;
        public float summonInterval = 14f;
        public int maxSummons = 3;

        [Header("Resistances")]
        [Tooltip("Damage multiplier for weapon tiers 1, 2, 3.")]
        public Vector3 tierDamageMultiplier = Vector3.one;
        [Tooltip("How much staggers affect it (0 = immune).")]
        [Range(0f, 1f)] public float staggerFactor = 1f;

        public string DisplayName => UI.Loc.IsArabic ? displayNameAr : displayNameEn;

        public float TierMultiplier(int tier)
        {
            switch (tier)
            {
                case 1: return tierDamageMultiplier.x;
                case 2: return tierDamageMultiplier.y;
                default: return tierDamageMultiplier.z;
            }
        }
    }
}
