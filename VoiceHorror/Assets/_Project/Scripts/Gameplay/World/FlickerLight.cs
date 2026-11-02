using UnityEngine;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>Oil lamp / bulb that flickers more as threat rises.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class FlickerLight : MonoBehaviour
    {
        public static float GlobalInstability;
        public float baseIntensity = 1.2f;
        Light _light;
        float _seed;

        void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.Range(0f, 100f);
            if (baseIntensity <= 0f) baseIntensity = _light.intensity;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * (2f + GlobalInstability * 14f));
            float dropout = GlobalInstability > 0.6f && n < 0.25f ? 0.1f : 1f;
            _light.intensity = baseIntensity * Mathf.Lerp(0.85f, 1.1f, n) * dropout;
        }
    }
}
