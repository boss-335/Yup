using VoiceMatch.Core.Synthesis;
using Xunit;

namespace VoiceMatch.Core.Tests;

public class ContourCodecTests
{
    private static Contour Sung(string id) =>
        ContourBuilder.FromAudio(VoiceSynth.Render(TestSignals.Melody(id).Notes, new VoiceStyle(), 48000, 3), 48000, new ContourConfig());

    [Fact]
    public void RoundTrip_PreservesShape()
    {
        var c = Sung("ode_to_joy_a");
        var d = ContourCodec.Decode(ContourCodec.Encode(c));
        Assert.Equal(c.DurationMs, d.DurationMs);
        Assert.Equal(c.Voiced, d.Voiced);
        for (int i = 0; i < c.Length; i++) Assert.InRange(d.RelativeCents[i] - c.RelativeCents[i], -0.5f, 0.5f);
        Assert.Equal(c.Onsets.Length, d.Onsets.Length);
        Assert.True(new VoiceMatch.Core.Matching.ContourMatcher().Match(c, d, new MatchConfig()).Accuracy > 0.99f);
        Assert.Equal(d, ContourCodec.FromBase64(ContourCodec.ToBase64(c)) with { }, new ContourEq());
    }

    private sealed class ContourEq : IEqualityComparer<Contour>
    {
        public bool Equals(Contour? a, Contour? b) => a!.DurationMs == b!.DurationMs && a.RelativeCents.SequenceEqual(b.RelativeCents) && a.Voiced.SequenceEqual(b.Voiced);
        public int GetHashCode(Contour c) => c.DurationMs;
    }

    [Fact]
    public void Hash_StableAndContentSensitive()
    {
        var a = Sung("ode_to_joy_a");
        Assert.Equal(ContourCodec.Hash(a), ContourCodec.Hash(ContourCodec.Decode(ContourCodec.Encode(a))));
        Assert.NotEqual(ContourCodec.Hash(a), ContourCodec.Hash(Sung("twinkle_a")));
    }

    [Fact]
    public void CorruptData_Rejected()
    {
        Assert.Throws<FormatException>(() => ContourCodec.Decode(new byte[3]));
        var bytes = ContourCodec.Encode(Sung("twinkle_a"));
        Assert.Throws<FormatException>(() => ContourCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
        bytes[0] = (byte)'X';
        Assert.Throws<FormatException>(() => ContourCodec.Decode(bytes));
    }

    [Fact]
    public void Difficulty_FlatToneIsEasiest_BusyTuneIsHarder()
    {
        var flat = ContourBuilder.FromAudio(TestSignals.Harmonic(220, 2), 48000, new ContourConfig());
        Assert.Equal(1, ContourCodec.Difficulty(flat));
        Assert.True(ContourCodec.Difficulty(Sung("fur_elise_a")) > ContourCodec.Difficulty(flat));
    }

    [Fact]
    public void HummedPreview_MatchesItsOwnContour()
    {
        var c = Sung("eine_kleine_a");
        var hum = ContourVoice.Render(c, 48000);
        Assert.InRange(hum.Length, c.DurationMs * 48 - 100, c.DurationMs * 48 + 100);
        var back = ContourBuilder.FromAudio(hum, 48000, new ContourConfig());
        Assert.True(new VoiceMatch.Core.Matching.ContourMatcher().Match(c, back, new MatchConfig()).Accuracy > 0.8f);
    }
}
