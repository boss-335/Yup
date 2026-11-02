using VoiceMatch.Core.Dsp;
using Xunit;

namespace VoiceMatch.Core.Tests;

public class FftTests
{
    [Fact]
    public void ForwardThenInverse_RestoresSignal()
    {
        var r = new Random(3);
        var re = Enumerable.Range(0, 256).Select(_ => r.NextDouble() - 0.5).ToArray();
        var im = new double[256];
        var orig = (double[])re.Clone();
        Fft.Transform(re, im, false);
        Fft.Transform(re, im, true);
        for (int i = 0; i < 256; i++) { Assert.Equal(orig[i], re[i], 9); Assert.Equal(0, im[i], 9); }
    }

    [Fact]
    public void Sine_PeaksInExpectedBin()
    {
        int n = 1024;
        var re = Enumerable.Range(0, n).Select(i => Math.Cos(2 * Math.PI * 37 * i / n)).ToArray();
        var im = new double[n];
        Fft.Transform(re, im, false);
        int peak = Enumerable.Range(0, n / 2).OrderByDescending(k => re[k] * re[k] + im[k] * im[k]).First();
        Assert.Equal(37, peak);
    }

    [Fact]
    public void RejectsNonPowerOfTwo() =>
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[100], new double[100], false));
}

public class YinPitchTrackerTests
{
    private static readonly ContourConfig Cfg = new();

    private static float MedianVoicedHz(PitchFrame[] frames)
    {
        var v = frames.Where(f => f.Voiced).Select(f => f.F0Hz).OrderBy(x => x).ToArray();
        Assert.NotEmpty(v);
        return v[v.Length / 2];
    }

    [Theory]
    [InlineData(82.41)]   // E2 — low male
    [InlineData(110.0)]
    [InlineData(220.0)]
    [InlineData(440.0)]
    [InlineData(880.0)]   // A5 — child / high female
    public void Sine_TrackedWithin10Cents(double hz)
    {
        var frames = new YinPitchTracker(Cfg).Track(TestSignals.Sine(hz, 0.5), TestSignals.Rate);
        Assert.True(frames.Count(f => f.Voiced) > frames.Length * 0.9);
        Assert.InRange(TestSignals.Cents(MedianVoicedHz(frames), hz), -10, 10);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(24000)]
    [InlineData(16000)]
    public void WorksAtOtherSampleRates(int rate)
    {
        var frames = new YinPitchTracker(Cfg).Track(TestSignals.Sine(196, 0.5, rate: rate), rate);
        Assert.InRange(TestSignals.Cents(MedianVoicedHz(frames), 196), -10, 10);
    }

    [Fact]
    public void HarmonicToneWithWeakFundamental_NoOctaveError()
    {
        var frames = new YinPitchTracker(Cfg).Track(TestSignals.Harmonic(110, 0.5, fundamental: 0.2), TestSignals.Rate);
        Assert.InRange(TestSignals.Cents(MedianVoicedHz(frames), 110), -15, 15);
    }

    [Fact]
    public void Silence_IsUnvoiced()
    {
        var frames = new YinPitchTracker(Cfg).Track(new float[24000], TestSignals.Rate);
        Assert.All(frames, f => Assert.False(f.Voiced));
    }

    [Fact]
    public void WhiteNoise_MostlyUnvoiced()
    {
        var frames = new YinPitchTracker(Cfg with { YinFallbackThreshold = Cfg.YinThreshold }).Track(TestSignals.Noise(0.5, 0.3), TestSignals.Rate);
        Assert.True(frames.Count(f => f.Voiced) < frames.Length * 0.1);
    }

    [Fact]
    public void FrameTiming_MatchesHop()
    {
        var frames = new YinPitchTracker(Cfg).Track(TestSignals.Sine(220, 1.0), TestSignals.Rate);
        Assert.Equal(1 + (48000 - 2048) / 512, frames.Length);
        Assert.Equal(512.0 / 48000, frames[1].TimeSec - frames[0].TimeSec, 5);
    }
}

public class OnsetDetectorTests
{
    [Fact]
    public void ToneBursts_DetectedNearTrueStarts()
    {
        // Four notes separated by short silences, starting at 0.2, 0.7, 1.2, 1.7 s.
        var silence = new float[(int)(0.2 * 48000)];
        var gap = new float[(int)(0.1 * 48000)];
        var audio = TestSignals.Concat(silence,
            TestSignals.Harmonic(220, 0.4), gap, TestSignals.Harmonic(247, 0.4), gap,
            TestSignals.Harmonic(262, 0.4), gap, TestSignals.Harmonic(294, 0.4), silence);
        var onsets = new SpectralFluxOnsetDetector(new ContourConfig()).Detect(audio, 48000);
        foreach (var expected in new[] { 0.2, 0.7, 1.2, 1.7 })
            Assert.Contains(onsets, t => Math.Abs(t - expected) < 0.03);
        Assert.InRange(onsets.Length, 4, 5);
    }

    [Fact]
    public void SteadyTone_HasAtMostOneOnset()
    {
        var onsets = new SpectralFluxOnsetDetector(new ContourConfig()).Detect(TestSignals.Harmonic(220, 1.5), 48000);
        Assert.True(onsets.Length <= 1);
    }
}

public class AudioPrepTests
{
    [Fact]
    public void Normalize_HitsTargetLoudness()
    {
        var y = AudioPrep.Normalize(TestSignals.Sine(220, 1, amp: 0.01), 48000, -20, 40, out _);
        Assert.InRange(AudioPrep.LoudnessDb(y, 48000), -20.5, -19.5);
    }

    [Fact]
    public void Normalize_RespectsMaxGain()
    {
        AudioPrep.Normalize(TestSignals.Sine(220, 1, amp: 0.0001), 48000, -20, 24, out float gain);
        Assert.Equal(24f, gain);
    }

    [Fact]
    public void Decimate2_KeepsVoiceBand_RemovesAliasBand()
    {
        var pass = AudioPrep.Decimate2(TestSignals.Sine(1000, 0.5));
        var stop = AudioPrep.Decimate2(TestSignals.Sine(20000, 0.5)); // would alias to 4 kHz
        Assert.InRange(AudioPrep.LoudnessDb(pass, 24000), -10, -8);    // 0.5 amp sine ≈ -9 dB
        Assert.True(AudioPrep.LoudnessDb(stop, 24000) < -50);
    }

    [Fact]
    public void Resample_PreservesPitch()
    {
        var y = AudioPrep.Resample(TestSignals.Sine(300, 0.5, rate: 44100), 44100, 48000);
        var frames = new YinPitchTracker(new ContourConfig()).Track(y, 48000);
        var hz = frames.Where(f => f.Voiced).Select(f => f.F0Hz).OrderBy(x => x).ElementAt(frames.Count(f => f.Voiced) / 2);
        Assert.InRange(TestSignals.Cents(hz, 300), -10, 10);
    }
}

public class PerformanceEndpointerTests
{
    [Fact]
    public void DetectsStartAndEndAfterSilence()
    {
        var cfg = new ContourConfig();
        var audio = TestSignals.Concat(new float[24000], TestSignals.Sine(220, 1.0, 0.3), new float[24000]);
        var ep = new PerformanceEndpointer(cfg, 48000);
        var events = new List<EndpointEvent>();
        for (int i = 0; i < audio.Length; i += 480) // 10 ms mic callbacks
            events.Add(ep.Process(audio.AsSpan(i, Math.Min(480, audio.Length - i))));

        Assert.Equal(new[] { EndpointEvent.Started, EndpointEvent.Ended }, events.Where(e => e != EndpointEvent.None));
        Assert.InRange(ep.StartSample, 24000 - 1024, 24000 + 1024);
        Assert.InRange(ep.EndSample, 72000 - 1024, 72000 + 1024);
    }

    [Fact]
    public void ShortPause_DoesNotEndPerformance()
    {
        var cfg = new ContourConfig();
        var audio = TestSignals.Concat(TestSignals.Sine(220, 0.5, 0.3), new float[(int)(0.15 * 48000)], TestSignals.Sine(220, 0.5, 0.3));
        var ep = new PerformanceEndpointer(cfg, 48000);
        Assert.Equal(EndpointEvent.Started, ep.Process(audio));
        Assert.True(ep.Active);
    }
}
