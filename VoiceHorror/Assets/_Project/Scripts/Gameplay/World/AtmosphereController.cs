using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// Threat drives the mood (spec §2.4): drone volume and pitch, heartbeat, fog density,
    /// and flicker of the lamps marked with <see cref="FlickerLight"/>.
    /// </summary>
    public sealed class AtmosphereController : MonoBehaviour
    {
        public Color calmFog = new Color(0.05f, 0.07f, 0.09f);
        public Color dangerFog = new Color(0.12f, 0.04f, 0.04f);
        public float calmFogDensity = 0.035f;
        public float dangerFogDensity = 0.06f;
        public bool playWaves = true;

        AudioSource _drone, _heart, _waves;

        void Start()
        {
            _drone = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Drone, true, 0.15f, 0f);
            _heart = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Heartbeat, true, 0f, 0f);
            _drone.Play();
            _heart.Play();
            if (playWaves)
            {
                _waves = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Waves, true, 0.12f, 0f);
                _waves.Play();
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
        }

        void Update()
        {
            float th = ThreatMeter.Instance != null ? ThreatMeter.Instance.Threat01 : 0f;
            var hp = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<PlayerHealth>() : null;
            float hurt = hp != null ? 1f - hp.Health01 : 0f;

            _drone.volume = Mathf.Lerp(0.12f, 0.45f, th);
            _drone.pitch = Mathf.Lerp(1f, 1.18f, th);
            float heart = Mathf.Max(Mathf.InverseLerp(0.45f, 1f, th), hurt * 0.8f);
            _heart.volume = Mathf.Lerp(0f, 0.8f, heart);
            _heart.pitch = Mathf.Lerp(0.9f, 1.6f, heart);

            RenderSettings.fogColor = Color.Lerp(calmFog, dangerFog, th);
            RenderSettings.fogDensity = Mathf.Lerp(calmFogDensity, dangerFogDensity, th);
            FlickerLight.GlobalInstability = th;
        }
    }
}
