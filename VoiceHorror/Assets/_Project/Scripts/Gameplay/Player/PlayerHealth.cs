using System;
using UnityEngine;
using VoiceHorror.Core;

namespace VoiceHorror.Gameplay.Player
{
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        public float Current { get; private set; }
        public float Max => GameConfig.Balance.maxHealth;
        public bool IsAlive => Current > 0f;
        public float Health01 => Max > 0 ? Current / Max : 0f;
        /// <summary>0..1 flash intensity after taking damage, for the HUD vignette.</summary>
        public float HurtFlash { get; private set; }

        public event Action Died;
        public event Action<float> Damaged;

        float _sinceHit;

        void Awake() => Current = GameConfig.Balance.maxHealth;

        public void TakeDamage(float amount, Vector3 from, int tier, float staggerSec)
        {
            if (!IsAlive || amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            _sinceHit = 0f;
            HurtFlash = 1f;
            Damaged?.Invoke(amount);
            if (Current <= 0f) Died?.Invoke();
        }

        public void Restore()
        {
            Current = Max;
            HurtFlash = 0f;
        }

        void Update()
        {
            HurtFlash = Mathf.MoveTowards(HurtFlash, 0f, Time.deltaTime * 1.5f);
            if (!IsAlive) return;
            _sinceHit += Time.deltaTime;
            var b = GameConfig.Balance;
            if (_sinceHit > b.healthRegenDelay && Current < Max)
                Current = Mathf.Min(Max, Current + b.healthRegenPerSec * Time.deltaTime);
        }
    }
}
