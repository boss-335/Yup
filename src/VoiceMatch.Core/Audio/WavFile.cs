using System;
using System.IO;

namespace VoiceMatch.Core.Audio
{
    /// <summary>WAV reader (PCM 8/16/24/32, float32, extensible) mixing down to mono, and a PCM16 writer.</summary>
    public static class WavFile
    {
        public static float[] Read(string path, out int sampleRate) => Read(File.ReadAllBytes(path), out sampleRate);

        public static float[] Read(byte[] data, out int sampleRate)
        {
            using var br = new BinaryReader(new MemoryStream(data));
            if (new string(br.ReadChars(4)) != "RIFF") throw new FormatException("Not a RIFF file");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new FormatException("Not a WAVE file");

            int format = 0, channels = 0, bits = 0;
            sampleRate = 0;
            byte[]? pcm = null;
            while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
            {
                string id = new string(br.ReadChars(4));
                uint size = br.ReadUInt32();
                long next = br.BaseStream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    format = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    sampleRate = (int)br.ReadUInt32();
                    br.ReadUInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (format == 0xFFFE && size >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        format = br.ReadUInt16(); // first two bytes of the SubFormat GUID
                    }
                }
                else if (id == "data")
                {
                    long avail = br.BaseStream.Length - br.BaseStream.Position;
                    pcm = br.ReadBytes((int)Math.Min(size, avail));
                }
                if (next > br.BaseStream.Length) break;
                br.BaseStream.Position = next;
            }

            if (pcm == null || channels == 0) throw new FormatException("WAV is missing fmt or data chunk");
            int bytesPer = bits / 8;
            int frames = pcm.Length / (bytesPer * channels);
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += Sample(pcm, (f * channels + c) * bytesPer, format, bits);
                mono[f] = (float)(sum / channels);
            }
            return mono;
        }

        private static double Sample(byte[] b, int o, int format, int bits)
        {
            if (format == 3 && bits == 32) return BitConverter.ToSingle(b, o);
            if (format != 1) throw new NotSupportedException($"WAV format {format}/{bits}-bit not supported");
            switch (bits)
            {
                case 8: return (b[o] - 128) / 128.0;
                case 16: return BitConverter.ToInt16(b, o) / 32768.0;
                case 24: return ((b[o] | b[o + 1] << 8 | (sbyte)b[o + 2] << 16)) / 8388608.0;
                case 32: return BitConverter.ToInt32(b, o) / 2147483648.0;
                default: throw new NotSupportedException($"{bits}-bit PCM not supported");
            }
        }

        public static void WritePcm16(string path, float[] samples, int sampleRate)
            => File.WriteAllBytes(path, WritePcm16(samples, sampleRate));

        public static byte[] WritePcm16(float[] samples, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            int dataLen = samples.Length * 2;
            bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + dataLen);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' }); bw.Write(16);
            bw.Write((short)1); bw.Write((short)1); bw.Write(sampleRate); bw.Write(sampleRate * 2);
            bw.Write((short)2); bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(dataLen);
            foreach (var s in samples)
            {
                float c = s > 1f ? 1f : (s < -1f ? -1f : s);
                bw.Write((short)Math.Round(c * 32767));
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
}
