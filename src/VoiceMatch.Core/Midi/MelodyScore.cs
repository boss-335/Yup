using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VoiceMatch.Core.Midi
{
    /// <summary>
    /// Human-editable melody source (data/melodies/*.json): note names with lengths in beats.
    /// This is the in-house authored score; the .mid reference is generated from it.
    /// </summary>
    public sealed record MelodyScore(string Id, string Title, int Bpm, MidiNote[] Notes)
    {
        public double DurationSec => Notes.Length == 0 ? 0 : Notes[Notes.Length - 1].EndSec;

        public static MelodyScore Load(string path) => Parse(File.ReadAllText(path));

        public static MelodyScore Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string id = root.GetProperty("id").GetString() ?? throw new FormatException("missing id");
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? id : id;
            int bpm = root.TryGetProperty("bpm", out var b) ? b.GetInt32() : 120;
            double beat = 60.0 / bpm;
            double pos = 0;
            var notes = new List<MidiNote>();
            foreach (var n in root.GetProperty("notes").EnumerateArray())
            {
                string name = n[0].GetString() ?? "";
                double beats = n[1].GetDouble();
                if (!name.Equals("R", StringComparison.OrdinalIgnoreCase)) // "R" = rest
                    notes.Add(new MidiNote(NoteNumber(name), pos * beat, beats * beat));
                pos += beats;
            }
            return new MelodyScore(id, title, bpm, notes.ToArray());
        }

        /// <summary>"C4" = 60, "D#5" = 75, "Bb3" = 58.</summary>
        public static int NoteNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("empty note name");
            int[] semis = { 9, 11, 0, 2, 4, 5, 7 }; // A..G
            char letter = char.ToUpperInvariant(name[0]);
            if (letter < 'A' || letter > 'G') throw new FormatException($"bad note name '{name}'");
            int pc = semis[letter - 'A'];
            int i = 1;
            while (i < name.Length && (name[i] == '#' || name[i] == 'b'))
            {
                pc += name[i] == '#' ? 1 : -1;
                i++;
            }
            if (!int.TryParse(name.Substring(i), out int octave)) throw new FormatException($"bad octave in '{name}'");
            return (octave + 1) * 12 + pc;
        }
    }
}
