using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoiceMatch.Core.Midi
{
    /// <summary>
    /// Minimal Standard MIDI File reader/writer (formats 0 and 1): note on/off, tempo map,
    /// running status. Enough for in-house public-domain melody references (GDD §5.2.2).
    /// </summary>
    public static class MidiFile
    {
        public static MidiNote[] Read(string path) => Read(File.ReadAllBytes(path));

        public static MidiNote[] Read(byte[] data)
        {
            int pos = 0;
            if (ReadTag(data, ref pos) != "MThd") throw new FormatException("Not a MIDI file (missing MThd)");
            int hdrLen = (int)ReadU32(data, ref pos);
            int hdrEnd = pos + hdrLen;
            int format = ReadU16(data, ref pos);
            int ntrks = ReadU16(data, ref pos);
            int division = ReadU16(data, ref pos);
            pos = hdrEnd;
            if (format > 1) throw new NotSupportedException("MIDI format 2 is not supported");
            if ((division & 0x8000) != 0) throw new NotSupportedException("SMPTE time division is not supported");

            var tempos = new List<(long tick, int usPerQuarter)>();
            var raw = new List<(long onTick, long offTick, int pitch, int vel)>();

            for (int t = 0; t < ntrks && pos < data.Length; t++)
            {
                string tag = ReadTag(data, ref pos);
                int len = (int)ReadU32(data, ref pos);
                int end = pos + len;
                if (tag != "MTrk") { pos = end; continue; }

                long tick = 0;
                int status = 0;
                var open = new Dictionary<(int ch, int pitch), Stack<(long tick, int vel)>>();
                while (pos < end)
                {
                    tick += ReadVlq(data, ref pos);
                    int b = data[pos];
                    if (b >= 0x80) { status = b; pos++; }
                    else if (status == 0) throw new FormatException("Running status without prior status byte");

                    if (status == 0xFF)
                    {
                        int type = data[pos++];
                        int mlen = (int)ReadVlq(data, ref pos);
                        if (type == 0x51 && mlen == 3)
                            tempos.Add((tick, (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2]));
                        pos += mlen;
                        status = 0;
                        if (type == 0x2F) break;
                        continue;
                    }
                    if (status == 0xF0 || status == 0xF7)
                    {
                        int slen = (int)ReadVlq(data, ref pos);
                        pos += slen;
                        status = 0;
                        continue;
                    }

                    int kind = status & 0xF0, ch = status & 0x0F;
                    int d1 = data[pos++];
                    int d2 = (kind == 0xC0 || kind == 0xD0) ? 0 : data[pos++];
                    if (kind == 0x90 && d2 > 0)
                    {
                        if (!open.TryGetValue((ch, d1), out var st)) open[(ch, d1)] = st = new Stack<(long, int)>();
                        st.Push((tick, d2));
                    }
                    else if (kind == 0x80 || (kind == 0x90 && d2 == 0))
                    {
                        if (open.TryGetValue((ch, d1), out var st) && st.Count > 0)
                        {
                            var (on, vel) = st.Pop();
                            raw.Add((on, tick, d1, vel));
                        }
                    }
                }
                pos = end;
            }

            tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
            if (tempos.Count == 0 || tempos[0].tick > 0) tempos.Insert(0, (0, 500000));

            double ToSec(long tk)
            {
                double sec = 0;
                for (int i = 0; i < tempos.Count; i++)
                {
                    long segStart = tempos[i].tick;
                    if (segStart >= tk) break;
                    long segEnd = i + 1 < tempos.Count ? Math.Min(tempos[i + 1].tick, tk) : tk;
                    sec += (segEnd - segStart) * tempos[i].usPerQuarter / 1e6 / division;
                }
                return sec;
            }

            return raw.Select(r =>
            {
                double s = ToSec(r.onTick);
                return new MidiNote(r.pitch, s, ToSec(r.offTick) - s, r.vel);
            }).OrderBy(n => n.StartSec).ThenBy(n => n.Pitch).ToArray();
        }

        public static void Write(string path, IEnumerable<MidiNote> notes, int bpm = 120)
            => File.WriteAllBytes(path, Write(notes, bpm));

        /// <summary>Writes a format-0 file at the given tempo, 480 ticks per quarter.</summary>
        public static byte[] Write(IEnumerable<MidiNote> notes, int bpm = 120)
        {
            const int division = 480;
            int usPerQuarter = 60000000 / bpm;
            double ticksPerSec = division * 1e6 / usPerQuarter;

            var events = new List<(long tick, int order, byte[] bytes)>();
            foreach (var n in notes)
            {
                long on = (long)Math.Round(n.StartSec * ticksPerSec);
                long off = (long)Math.Round(n.EndSec * ticksPerSec);
                events.Add((on, 1, new byte[] { 0x90, (byte)n.Pitch, (byte)Math.Max(1, Math.Min(127, n.Velocity)) }));
                events.Add((off, 0, new byte[] { 0x80, (byte)n.Pitch, 0 })); // offs sort before ons at the same tick
            }
            events.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.order.CompareTo(b.order));

            var track = new List<byte>();
            WriteVlq(track, 0);
            track.AddRange(new byte[] { 0xFF, 0x51, 0x03, (byte)(usPerQuarter >> 16), (byte)(usPerQuarter >> 8), (byte)usPerQuarter });
            long last = 0;
            foreach (var e in events)
            {
                WriteVlq(track, e.tick - last);
                track.AddRange(e.bytes);
                last = e.tick;
            }
            WriteVlq(track, 0);
            track.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });

            var file = new List<byte>();
            file.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, division >> 8, division & 0xFF });
            file.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            int tl = track.Count;
            file.AddRange(new byte[] { (byte)(tl >> 24), (byte)(tl >> 16), (byte)(tl >> 8), (byte)tl });
            file.AddRange(track);
            return file.ToArray();
        }

        private static string ReadTag(byte[] d, ref int p)
        {
            if (p + 4 > d.Length) throw new FormatException("Unexpected end of MIDI data");
            string s = new string(new[] { (char)d[p], (char)d[p + 1], (char)d[p + 2], (char)d[p + 3] });
            p += 4;
            return s;
        }

        private static uint ReadU32(byte[] d, ref int p) { uint v = (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]); p += 4; return v; }
        private static int ReadU16(byte[] d, ref int p) { int v = d[p] << 8 | d[p + 1]; p += 2; return v; }

        private static long ReadVlq(byte[] d, ref int p)
        {
            long v = 0;
            for (int i = 0; i < 4; i++)
            {
                byte b = d[p++];
                v = (v << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return v;
            }
            throw new FormatException("Variable-length quantity too long");
        }

        private static void WriteVlq(List<byte> o, long v)
        {
            var stack = new Stack<byte>();
            stack.Push((byte)(v & 0x7F));
            while ((v >>= 7) > 0) stack.Push((byte)((v & 0x7F) | 0x80));
            o.AddRange(stack);
        }
    }
}
