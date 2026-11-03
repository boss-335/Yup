using System;
using System.Collections.Generic;
using System.IO;
using VoiceMatch.Core.Audio;

namespace VoiceBattle.Sounds
{
    /// <summary>WAV / OGG Vorbis / MP3 → mono float samples (GDD §5.4 step 1).</summary>
    public static class AudioDecoder
    {
        public static float[] Decode(byte[] data, string fileName, out int sampleRate)
        {
            string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "wav": return WavFile.Read(data, out sampleRate);
                case "ogg":
                {
                    using var r = new NVorbis.VorbisReader(new MemoryStream(data), true);
                    sampleRate = r.SampleRate;
                    return Mix(r.Channels, (buf, off, n) => r.ReadSamples(buf, off, n));
                }
                case "mp3":
                {
                    using var r = new NLayer.MpegFile(new MemoryStream(data));
                    sampleRate = r.SampleRate;
                    return Mix(r.Channels, (buf, off, n) => r.ReadSamples(buf, off, n));
                }
                default: throw new NotSupportedException($"unsupported audio format '{ext}'");
            }
        }

        private static float[] Mix(int channels, Func<float[], int, int, int> read)
        {
            var mono = new List<float>();
            var buf = new float[4096 * Math.Max(1, channels)];
            int n;
            while ((n = read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i + channels <= n; i += channels)
                {
                    float s = 0;
                    for (int c = 0; c < channels; c++) s += buf[i + c];
                    mono.Add(s / channels);
                }
            return mono.ToArray();
        }
    }
}
