using UnityEngine;
using VoiceHorror.Dsp;

namespace VoiceHorror.Config
{
    public enum WeaponDelivery
    {
        /// <summary>Narrow ray along the view direction; hits the first creature.</summary>
        Beam = 0,
        /// <summary>Cone in front of the player; hits everything inside.</summary>
        Cone = 1,
        /// <summary>Sphere around the player; hits everything inside.</summary>
        Shockwave = 2,
    }

    /// <summary>Spec §2.6 weapon definition. Weapons are relics of the island's last nahham.</summary>
    [CreateAssetMenu(menuName = "VoiceHorror/Weapon", fileName = "Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id = "w_new";
        public string displayNameAr = "سلاح";
        public string displayNameEn = "Weapon";
        [TextArea] public string loreAr = "";
        [TextArea] public string loreEn = "";
        [TextArea] public string recordingHintAr = "";
        [TextArea] public string recordingHintEn = "";

        [Header("Voice")]
        public ReferenceType referenceType = ReferenceType.Melody;
        public float minDurationSec = 1.5f;
        public float maxDurationSec = 4f;

        [Header("Damage")]
        public float baseDamage = 40f;
        [Tooltip("Score (0-100) -> damage multiplier. Below minScoreToFire the weapon fails regardless.")]
        public AnimationCurve damageCurve = DefaultCurve(35f);
        [Range(0f, 100f)] public float minScoreToFire = 35f;
        public float cooldownSec = 5f;
        [Tooltip("Seconds the target creature is staggered on a successful hit (scaled by score).")]
        public float staggerSec = 1.2f;

        [Header("Delivery")]
        public WeaponDelivery delivery = WeaponDelivery.Beam;
        public float range = 20f;
        [Tooltip("Cone half-angle (Cone) or beam radius in metres (Beam).")]
        public float spread = 1f;
        public Color color = new Color(0.4f, 0.9f, 1f);
        [Tooltip("Optional prefab spawned at the impact/origin; a procedural effect is used when empty.")]
        public GameObject effectPrefab;

        [Header("Unlock")]
        public bool unlockedAtStart = true;
        [Tooltip("1 = short, 2 = medium, 3 = long. Some creatures resist low tiers.")]
        public int tier = 1;

        public string DisplayName => UI.Loc.IsArabic ? displayNameAr : displayNameEn;
        public string Lore => UI.Loc.IsArabic ? loreAr : loreEn;
        public string RecordingHint => UI.Loc.IsArabic ? recordingHintAr : recordingHintEn;

        /// <summary>Damage for a score; 0 when the performance failed.</summary>
        public float DamageFor(float score)
        {
            if (score < minScoreToFire) return 0f;
            return baseDamage * Mathf.Max(0f, damageCurve.Evaluate(score));
        }

        public static AnimationCurve DefaultCurve(float minScore)
        {
            // Hard minimum, then partial damage that grows to 1.0 at 70 and 1.5 at a perfect 100.
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(minScore, 0.4f),
                new Keyframe(70f, 1f),
                new Keyframe(100f, 1.5f));
        }
    }
}
