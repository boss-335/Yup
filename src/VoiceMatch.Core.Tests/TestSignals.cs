using VoiceMatch.Core;
using VoiceMatch.Core.Midi;

namespace VoiceMatch.Core.Tests;

/// <summary>Synthetic signal generator with known pitches (GDD §11: "sine tones at known pitches").</summary>
internal static class TestSignals
{
    public const int Rate = 48000;

    public static float[] Sine(double hz, double seconds, double amp = 0.5, int rate = Rate)
    {
        var y = new float[(int)(seconds * rate)];
        for (int i = 0; i < y.Length; i++) y[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate));
        return y;
    }

    /// <summary>Harmonic tone; <paramref name="fundamental"/> scales the first partial (0 = missing fundamental).</summary>
    public static float[] Harmonic(double hz, double seconds, int harmonics = 10, double fundamental = 1, int rate = Rate)
    {
        var y = new float[(int)(seconds * rate)];
        for (int i = 0; i < y.Length; i++)
        {
            double v = 0;
            for (int h = 1; h <= harmonics; h++)
            {
                if (h * hz > 0.45 * rate) break;
                v += (h == 1 ? fundamental : 1) / h * Math.Sin(2 * Math.PI * h * hz * i / rate);
            }
            y[i] = (float)(0.2 * v);
        }
        return y;
    }

    public static float[] Noise(double seconds, double amp, int seed = 1, int rate = Rate)
    {
        var r = new Random(seed);
        var y = new float[(int)(seconds * rate)];
        for (int i = 0; i < y.Length; i++) y[i] = (float)(amp * (2 * r.NextDouble() - 1));
        return y;
    }

    public static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    public static double Cents(double hz, double refHz) => 1200 * Math.Log2(hz / refHz);

    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    public static VoiceMatchSettings Settings => VoiceMatchSettings.Load(Path.Combine(DataDir, "match_config.json"));

    public static MelodyScore Melody(string id) => MelodyScore.Load(Path.Combine(DataDir, "melodies", id + ".json"));

    public static string[] MelodyIds => Directory.GetFiles(Path.Combine(DataDir, "melodies"), "*.json")
        .Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray()!;
}
