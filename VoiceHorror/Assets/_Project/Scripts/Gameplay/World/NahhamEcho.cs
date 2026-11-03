using UnityEngine;
using VoiceHorror.Art;
using VoiceHorror.Audio;
using VoiceHorror.Characters;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// The grandfather's echo in his house: a translucent nahham sitting cross-legged, hand cupped
    /// behind his ear, swaying as he sings a maqam bayati phrase. He dissolves when you come close
    /// (a memory, not a ghost you can touch) and returns when you step back.
    /// </summary>
    [RequireComponent(typeof(ProceduralCharacter))]
    public sealed class NahhamEcho : MonoBehaviour
    {
        public float fadeDistance = 2.2f;
        public float visibleDistance = 4.5f;
        public float maxAlpha = 0.38f;
        public float humVolume = 0.35f;
        [Tooltip("Gallery: ignore the player's distance and stay visible.")]
        public bool alwaysVisible;

        ProceduralCharacter _ch;
        Transform _spine, _chest, _head, _jaw, _armR;
        AudioSource _hum;
        Light _glow;
        float _alpha;
        Material _ghostInstance;

        void Start()
        {
            _ch = GetComponent<ProceduralCharacter>();
            _ch.Build();
            _spine = _ch.Role("spine"); _chest = _ch.Role("chest"); _head = _ch.Role("head");
            _jaw = _ch.Role("jaw"); _armR = _ch.Role("armR");

            // Own material instance so the fade does not touch other ghosts.
            var src = _ch.palette.For(Slot.Ghost);
            _ghostInstance = new Material(src);
            foreach (var r in _ch.Renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == src) mats[i] = _ghostInstance;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _hum = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.NahhamHum, true, 0f, 1f, 9f);
            _hum.Play();
            var lg = new GameObject("Echo Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.9f, 0.3f);
            _glow = lg.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(0.55f, 0.85f, 0.95f);
            _glow.range = 3.5f;
        }

        void LateUpdate()
        {
            if (_ch == null || !_ch.Built) return;
            float t = Time.time;
            var player = PlayerController.Instance;
            float d = player != null ? Vector3.Distance(player.transform.position, transform.position) : 99f;
            float target = d < fadeDistance ? 0f : Mathf.Clamp01(Mathf.InverseLerp(visibleDistance + 3f, visibleDistance, d) + 0.25f);
            if (d < fadeDistance) target = 0f;
            if (alwaysVisible) target = 1f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime * 0.6f);

            float shimmer = 0.85f + 0.15f * Mathf.PerlinNoise(t * 2f, 0.4f);
            var c = _ghostInstance.color;
            c.a = maxAlpha * _alpha * shimmer;
            _ghostInstance.color = c;
            _glow.intensity = 0.8f * _alpha * shimmer;
            _hum.volume = humVolume * _alpha;

            // Singing: slow sway, head tilting into the cupped hand, the jaw following the melody.
            float sway = Mathf.Sin(t * 0.8f);
            float phrase = Mathf.Max(0f, Mathf.Sin(t * 2.4f)) * (0.6f + 0.4f * Mathf.Sin(t * 0.37f));
            Rotate(_spine, new Vector3(2f * Mathf.Sin(t * 0.4f), 0f, sway * 4f));
            Rotate(_chest, new Vector3(-2f * phrase, sway * 3f, sway * 2f));
            Rotate(_head, new Vector3(-6f * phrase + 3f * Mathf.Sin(t * 0.6f), -sway * 6f, 6f + sway * 5f));
            Rotate(_jaw, new Vector3(phrase * 14f, 0f, 0f));
            Rotate(_armR, new Vector3(Mathf.Sin(t * 0.8f + 0.5f) * 3f, 0f, 0f));
        }

        void Rotate(Transform bone, Vector3 euler)
        {
            if (bone != null) bone.localRotation = _ch.RestLocal(bone) * Quaternion.Euler(euler);
        }
    }
}
