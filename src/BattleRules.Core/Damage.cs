using System;

namespace BattleRules.Core
{
    /// <summary>GDD §3.3: quality = clamp((acc − thr)/(1 − thr), 0, 1); damage = base × quality^1.5.</summary>
    public static class DamageModel
    {
        public static float Quality(float accuracy, float threshold)
        {
            if (accuracy < threshold) return 0f;
            float q = (accuracy - threshold) / (1f - threshold);
            return q < 0 ? 0 : (q > 1 ? 1 : q);
        }

        public static float Damage(float baseDamage, float quality, float exponent) => baseDamage * (float)Math.Pow(quality, exponent);

        public static bool IsPerfect(float accuracy, float perfectAccuracy) => accuracy >= perfectAccuracy;
    }
}
