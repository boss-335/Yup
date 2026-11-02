using VoiceMatch.Core.Audio;
using VoiceMatch.Core.Midi;
using Xunit;

namespace VoiceMatch.Core.Tests;

public class MidiFileTests
{
    [Fact]
    public void WriteThenRead_RoundTripsNotes()
    {
        var notes = new[] { new MidiNote(64, 0, 0.5), new MidiNote(65, 0.5, 0.25), new MidiNote(67, 1.0, 1.0, 80) };
        var back = MidiFile.Read(MidiFile.Write(notes, 120));
        Assert.Equal(notes.Length, back.Length);
        for (int i = 0; i < notes.Length; i++)
        {
            Assert.Equal(notes[i].Pitch, back[i].Pitch);
            Assert.Equal(notes[i].StartSec, back[i].StartSec, 3);
            Assert.Equal(notes[i].DurationSec, back[i].DurationSec, 3);
        }
        Assert.Equal(80, back[2].Velocity);
    }

    [Fact]
    public void Read_HandlesRunningStatus_NoteOnZeroVelocity_AndTempoChange()
    {
        // Format 0, division 96. Tempo 120 bpm, note C4 for 1 beat, tempo → 60 bpm, note D4 for 1 beat.
        byte[] track =
        {
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,  // tempo 500000 µs
            0x00, 0x90, 60, 100,                       // note on C4
            0x60, 60, 0,                               // running status: note on vel 0 = off, after 96 ticks
            0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40,  // tempo 1000000 µs
            0x00, 0x90, 62, 100,
            0x60, 0x80, 62, 0,
            0x00, 0xFF, 0x2F, 0x00,
        };
        var file = new List<byte> { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
                                    (byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, (byte)track.Length };
        file.AddRange(track);
        var notes = MidiFile.Read(file.ToArray());
        Assert.Equal(2, notes.Length);
        Assert.Equal(60, notes[0].Pitch); Assert.Equal(0.5, notes[0].DurationSec, 6);
        Assert.Equal(62, notes[1].Pitch); Assert.Equal(0.5, notes[1].StartSec, 6); Assert.Equal(1.0, notes[1].DurationSec, 6);
    }

    [Fact]
    public void Read_RejectsGarbage() => Assert.Throws<FormatException>(() => MidiFile.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

    [Fact]
    public void CommittedReferences_MatchTheirMelodySources()
    {
        foreach (var id in TestSignals.MelodyIds)
        {
            var score = TestSignals.Melody(id);
            var mid = MidiFile.Read(Path.Combine(TestSignals.DataDir, "midi", id + ".mid"));
            Assert.Equal(score.Notes.Select(n => n.Pitch), mid.Select(n => n.Pitch));
            Assert.Equal(score.DurationSec, mid[^1].EndSec, 2);
        }
    }
}

public class MelodyScoreTests
{
    [Theory]
    [InlineData("C4", 60)]
    [InlineData("A4", 69)]
    [InlineData("D#5", 75)]
    [InlineData("Bb3", 58)]
    [InlineData("C-1", 0)]
    public void NoteNames(string name, int midi) => Assert.Equal(midi, MelodyScore.NoteNumber(name));

    [Fact]
    public void Parse_BeatsAndRests()
    {
        var s = MelodyScore.Parse("{\"id\":\"x\",\"bpm\":60,\"notes\":[[\"C4\",1],[\"R\",1],[\"E4\",2]]}");
        Assert.Equal(2, s.Notes.Length);
        Assert.Equal(2.0, s.Notes[1].StartSec, 6);
        Assert.Equal(4.0, s.DurationSec, 6);
    }
}

public class WavFileTests
{
    [Fact]
    public void Pcm16_RoundTrip()
    {
        var x = TestSignals.Sine(440, 0.1);
        var back = WavFile.Read(WavFile.WritePcm16(x, 48000), out int sr);
        Assert.Equal(48000, sr);
        Assert.Equal(x.Length, back.Length);
        for (int i = 0; i < x.Length; i++) Assert.InRange(back[i] - x[i], -1.0f / 32767, 1.0f / 32767);
    }

    [Fact]
    public void Stereo_IsMixedToMono()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
        {
            bw.Write("RIFF".ToCharArray()); bw.Write(36 + 8); bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)2);
            bw.Write(44100); bw.Write(44100 * 4); bw.Write((short)4); bw.Write((short)16);
            bw.Write("data".ToCharArray()); bw.Write(8);
            bw.Write((short)16384); bw.Write((short)0); bw.Write((short)-16384); bw.Write((short)-16384);
        }
        var y = WavFile.Read(ms.ToArray(), out int sr);
        Assert.Equal(44100, sr);
        Assert.Equal(new[] { 0.25f, -0.5f }, y);
    }
}

public class ConfigTests
{
    [Fact]
    public void LoadsCommittedConfig()
    {
        var s = TestSignals.Settings;
        Assert.Equal(48000, s.Contour.SampleRate);
        Assert.Equal(2048, s.Contour.Frame);
        Assert.Equal(512, s.Contour.Hop);
        Assert.Equal(0.6f, s.Match.WeightPitch);
        Assert.Equal(0.25f, s.Match.WeightRhythm);
        Assert.Equal(0.15f, s.Match.WeightCoverage);
        Assert.Equal(-45f, s.Contour.MinRmsDb);
    }

    [Fact]
    public void MissingKeysKeepDefaults()
    {
        var s = VoiceMatchSettings.FromJson("{ \"hop\": 256 }");
        Assert.Equal(256, s.Contour.Hop);
        Assert.Equal(new MatchConfig().DtwBand, s.Match.DtwBand);
    }

    [Fact]
    public void InvalidValuesRejected() =>
        Assert.Throws<FormatException>(() => VoiceMatchSettings.FromJson("{ \"tempo_min\": 1.5 }"));

    [Fact]
    public void FinisherSpeedOverride_RelaxesDurationGate()
    {
        var m = new MatchConfig();
        Assert.Equal(1f / 0.7f, m.EffectiveMaxSpeed, 3);
        Assert.Equal(1.6f, m.WithMaxSpeed(1.6f).EffectiveMaxSpeed, 3);
    }
}
