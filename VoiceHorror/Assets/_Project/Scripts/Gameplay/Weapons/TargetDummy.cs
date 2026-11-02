using UnityEngine;
using VoiceHorror.Core;

namespace VoiceHorror.Gameplay.Weapons
{
    /// <summary>M2 test target: records damage, flashes, resets after a few seconds.</summary>
    public sealed class TargetDummy : MonoBehaviour, IDamageable
    {
        public float maxHealth = 200f;
        public float resetAfterSec = 4f;

        public float Health { get; private set; }
        public float LastDamage { get; private set; }
        public float TotalDamage { get; private set; }
        public int Hits { get; private set; }
        public bool IsAlive => Health > 0f;

        Renderer _renderer;
        Color _baseColor;
        float _flash, _sinceHit;

        void Awake()
        {
            Health = maxHealth;
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null) _baseColor = _renderer.material.color;
        }

        public void TakeDamage(float amount, Vector3 from, int tier, float staggerSec)
        {
            LastDamage = amount;
            TotalDamage += amount;
            Hits++;
            Health = Mathf.Max(0f, Health - amount);
            _flash = 1f;
            _sinceHit = 0f;
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
            if (sp.z <= 0f) return;
            UI.Ui.Begin();
            float s = UI.Ui.Scale;
            var r = new Rect(sp.x / s - 150f, (Screen.height - sp.y) / s - 40f, 300f, 60f);
            UI.Ui.Text(new Rect(r.x, r.y, r.width, 26), Mathf.RoundToInt(Vector3.Distance(cam.transform.position, transform.position)) + " m", 20, TextAnchor.UpperCenter, UI.Ui.Gold, true, false, false);
            UI.Ui.Text(new Rect(r.x, r.y + 26, r.width, 26), UI.Loc.F("range.dummy", LastDamage, TotalDamage, Hits), 16, TextAnchor.UpperCenter, UI.Ui.Ink, false, false);
        }

        void Update()
        {
            _sinceHit += Time.deltaTime;
            if (_sinceHit > resetAfterSec && Health < maxHealth) Health = maxHealth;
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 2f);
            if (_renderer != null) _renderer.material.color = Color.Lerp(_baseColor, new Color(1f, 0.9f, 0.5f), _flash);
            float s = 1f + 0.15f * _flash;
            transform.localScale = new Vector3(s, 1f, s);
        }
    }
}
