using UnityEngine;

namespace VoiceHorror.Audio
{
    /// <summary>
    /// Synthesized placeholder sounds so the prototype has atmosphere without any audio assets.
    /// Every clip is generated once and cached.
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        static AudioClip _drone, _heartbeat, _breath, _wail, _hit, _fail, _step, _pickup, _tape, _waves;

        static AudioClip Make(string name, float seconds, System.Func<int, float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = sample(i, i / (float)Rate);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0x7FFFFF - 1f;
        }

        /// <summary>Low, slowly beating drone (loops seamlessly: 8 s with integer cycles).</summary>
        public static AudioClip Drone => _drone ? _drone : _drone = Make("drone", 8f, (i, t) =>
        {
            float a = Mathf.Sin(2 * Mathf.PI * 55f * t) * 0.35f;
            float b = Mathf.Sin(2 * Mathf.PI * 55.5f * t) * 0.3f;
            float c = Mathf.Sin(2 * Mathf.PI * 82.5f * t) * 0.12f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 0.25f * t));
            float d = Mathf.Sin(2 * Mathf.PI * 110.125f * t) * 0.05f;
            return (a + b + c + d) * 0.6f;
        });

        public static AudioClip Heartbeat => _heartbeat ? _heartbeat : _heartbeat = Make("heartbeat", 1f, (i, t) =>
        {
            float Beat(float t0) { float x = t - t0; return x < 0 ? 0 : Mathf.Exp(-x * 28f) * Mathf.Sin(2 * Mathf.PI * 48f * x); }
            return (Beat(0f) + 0.7f * Beat(0.22f)) * 0.9f;
        });

        public static AudioClip Waves
        {
            get
            {
                if (_waves) return _waves;
                uint s = 0x1234567u; float lp = 0f;
                _waves = Make("waves", 10f, (i, t) =>
                {
                    lp += (Noise(ref s) - lp) * 0.02f;
                    float swell = 0.4f + 0.6f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 0.1f * t), 2f);
                    return lp * swell * 3f;
                });
                return _waves;
            }
        }

        /// <summary>Creature breathing: filtered noise with a slow in/out envelope.</summary>
        public static AudioClip Breath
        {
            get
            {
                if (_breath) return _breath;
                uint s = 0xBEEFu; float lp = 0f;
                _breath = Make("breath", 3f, (i, t) =>
                {
                    lp += (Noise(ref s) - lp) * 0.08f;
                    float env = Mathf.Pow(Mathf.Sin(Mathf.PI * (t % 1.5f) / 1.5f), 2f);
                    return lp * env * 1.6f;
                });
                return _breath;
            }
        }

        /// <summary>Sentinel wail: falling, wavering tone with noise.</summary>
        public static AudioClip Wail
        {
            get
            {
                if (_wail) return _wail;
                uint s = 0xC0FFEEu; float phase = 0f;
                _wail = Make("wail", 2f, (i, t) =>
                {
                    float f = Mathf.Lerp(720f, 260f, t / 2f) * (1f + 0.03f * Mathf.Sin(2 * Mathf.PI * 7f * t));
                    phase += 2 * Mathf.PI * f / Rate;
                    float env = Mathf.Min(1f, t * 8f) * Mathf.Clamp01((2f - t) * 2f);
                    return (Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 1.5f) * 0.2f + Noise(ref s) * 0.12f) * env;
                });
                return _wail;
            }
        }

        /// <summary>A bright consonant chord: what a successful song sounds like.</summary>
        public static AudioClip Hit => _hit ? _hit : _hit = Make("hit", 1.4f, (i, t) =>
        {
            float env = Mathf.Exp(-t * 2.5f) * Mathf.Min(1f, t * 60f);
            float s = Mathf.Sin(2 * Mathf.PI * 220f * t) + 0.7f * Mathf.Sin(2 * Mathf.PI * 277.18f * t)
                    + 0.6f * Mathf.Sin(2 * Mathf.PI * 329.63f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * 440f * t);
            return s * env * 0.25f;
        });

        /// <summary>A sour, detuned cluster: the song failed.</summary>
        public static AudioClip Fail => _fail ? _fail : _fail = Make("fail", 1.0f, (i, t) =>
        {
            float env = Mathf.Exp(-t * 3.5f) * Mathf.Min(1f, t * 40f);
            float s = Mathf.Sin(2 * Mathf.PI * 196f * t) + Mathf.Sin(2 * Mathf.PI * 207.6f * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 293.7f * t);
            return s * env * 0.25f;
        });

        public static AudioClip Step
        {
            get
            {
                if (_step) return _step;
                uint s = 0xABCDu; float lp = 0f;
                _step = Make("step", 0.18f, (i, t) =>
                {
                    lp += (Noise(ref s) - lp) * 0.3f;
                    return lp * Mathf.Exp(-t * 30f) * 0.8f;
                });
                return _step;
            }
        }

        public static AudioClip Pickup => _pickup ? _pickup : _pickup = Make("pickup", 0.6f, (i, t) =>
        {
            float f = t < 0.15f ? 660f : 880f;
            return Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 5f) * 0.3f;
        });

        /// <summary>Cassette click + hiss (tape logs).</summary>
        public static AudioClip Tape
        {
            get
            {
                if (_tape) return _tape;
                uint s = 0x7A7Eu;
                _tape = Make("tape", 1.2f, (i, t) =>
                {
                    float click = t < 0.02f ? Noise(ref s) * 0.9f : 0f;
                    return click + Noise(ref s) * 0.04f;
                });
                return _tape;
            }
        }

        public static AudioSource MakeSource(GameObject host, AudioClip clip, bool loop, float volume, float spatial, float maxDistance = 30f)
        {
            var src = host.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.volume = volume;
            src.spatialBlend = spatial;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = maxDistance;
            src.playOnAwake = false;
            return src;
        }
    }
}
