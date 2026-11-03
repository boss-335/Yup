using System;
using System.IO;
using System.Security.Cryptography;

namespace VoiceMatch.Core
{
    /// <summary>
    /// Compact binary form of a <see cref="Contour"/> (≈3 bytes per 10.7 ms frame) for storing taught
    /// moves and user sounds, and for <c>contour_hash</c> (GDD §7.1). Stores pitch shape only — no audio.
    /// Layout: "VMC1" · durationMs:int32 · frames:int32 · onsets:int32 · cents:int16[frames]
    ///         · voiced bits (frames/8 bytes) · onset ms:int32[onsets].
    /// </summary>
    public static class ContourCodec
    {
        private static readonly byte[] Magic = { (byte)'V', (byte)'M', (byte)'C', (byte)'1' };

        public static byte[] Encode(Contour c)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Magic);
            w.Write(c.DurationMs);
            w.Write(c.Length);
            w.Write(c.Onsets.Length);
            foreach (var v in c.RelativeCents) w.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v))));
            var bits = new byte[(c.Length + 7) / 8];
            for (int i = 0; i < c.Length; i++) if (c.Voiced[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
            w.Write(bits);
            foreach (var t in c.Onsets) w.Write((int)Math.Round(t * 1000));
            w.Flush();
            return ms.ToArray();
        }

        public static Contour Decode(byte[] data)
        {
            if (data == null || data.Length < 16) throw new FormatException("contour data too short");
            using var r = new BinaryReader(new MemoryStream(data));
            var magic = r.ReadBytes(4);
            for (int i = 0; i < 4; i++) if (magic[i] != Magic[i]) throw new FormatException("not a VMC1 contour");
            int duration = r.ReadInt32(), frames = r.ReadInt32(), onsets = r.ReadInt32();
            if (frames < 0 || onsets < 0 || duration < 0 || frames > 100000 || onsets > 10000) throw new FormatException("corrupt contour header");
            long need = 16L + frames * 2L + (frames + 7) / 8 + onsets * 4L;
            if (data.Length != need) throw new FormatException("contour data length mismatch");
            var cents = new float[frames];
            for (int i = 0; i < frames; i++) cents[i] = r.ReadInt16();
            var bits = r.ReadBytes((frames + 7) / 8);
            var voiced = new bool[frames];
            for (int i = 0; i < frames; i++) voiced[i] = (bits[i >> 3] & (1 << (i & 7))) != 0;
            var on = new float[onsets];
            for (int i = 0; i < onsets; i++) on[i] = r.ReadInt32() / 1000f;
            return new Contour(cents, voiced, on, duration);
        }

        public static string ToBase64(Contour c) => Convert.ToBase64String(Encode(c));

        public static Contour FromBase64(string s) => Decode(Convert.FromBase64String(s));

        /// <summary>Short stable id of a contour's content (first 8 bytes of SHA-256, hex).</summary>
        public static string Hash(Contour c)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encode(c));
            return BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 1 (easy) … 5 (hard) from the shape: pitch range, how often the pitch moves, onsets per second.
        /// Used to label taught moves and uploaded sounds (GDD §5.4 "estimates difficulty").
        /// </summary>
        public static int Difficulty(Contour c)
        {
            if (c.VoicedCount < 4) return 1;
            float min = float.MaxValue, max = float.MinValue, moves = 0, prev = float.NaN;
            for (int i = 0; i < c.Length; i++)
            {
                if (!c.Voiced[i]) continue;
                float v = c.RelativeCents[i];
                min = Math.Min(min, v); max = Math.Max(max, v);
                if (!float.IsNaN(prev)) moves += Math.Abs(v - prev);
                prev = v;
            }
            float sec = Math.Max(0.1f, c.DurationMs / 1000f);
            float range = (max - min) / 100f;                 // semitones
            float motion = moves / 100f / sec;                // semitones travelled per second
            float density = c.Onsets.Length / sec;            // notes per second
            float score = range / 6f + motion / 12f + density / 3f;
            return Math.Max(1, Math.Min(5, 1 + (int)Math.Floor(score * 1.6f)));
        }
    }
}
