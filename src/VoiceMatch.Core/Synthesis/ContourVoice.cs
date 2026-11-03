using System;

namespace VoiceMatch.Core.Synthesis
{
    /// <summary>
    /// Hums a <see cref="Contour"/> back (soft harmonic tone following the relative pitch) — the preview for
    /// taught moves, which keep no audio.
    /// </summary>
    public static class ContourVoice
    {
        public static float[] Render(Contour c, int sampleRate, float centreMidi = 62f, float gain = 0.25f)
        {
            int frames = c.Length;
            if (frames == 0) return Array.Empty<float>();
            double frameSec = c.FrameMs / 1000.0;
            int len = (int)(c.DurationMs / 1000.0 * sampleRate);
            var y = new float[len];
            double phase = 0, env = 0;
            for (int i = 0; i < len; i++)
            {
                double pos = i / (double)sampleRate / frameSec;
                int f = Math.Min(frames - 1, (int)pos);
                bool voiced = c.Voiced[f];
                env += ((voiced ? 1 : 0) - env) * 0.004; // ~5 ms smoothing, no clicks
                if (env < 1e-4) continue;
                // interpolate pitch between voiced neighbours
                int g = Math.Min(frames - 1, f + 1);
                double cents = voiced && c.Voiced[g] ? c.RelativeCents[f] + (c.RelativeCents[g] - c.RelativeCents[f]) * (pos - f) : c.RelativeCents[f];
                double hz = 440 * Math.Pow(2, (centreMidi + cents / 100.0 - 69) / 12);
                phase += 2 * Math.PI * hz / sampleRate;
                double v = Math.Sin(phase) + 0.35 * Math.Sin(2 * phase) + 0.12 * Math.Sin(3 * phase);
                y[i] = (float)(gain * env * v);
            }
            return y;
        }
    }
}
