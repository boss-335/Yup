using System;

namespace VoiceHorror.Dsp
{
    /// <summary>In-place iterative radix-2 complex FFT with precomputed twiddles. Not thread-safe per instance.</summary>
    public sealed class Fft
    {
        public readonly int Size;
        readonly float[] _cos, _sin;
        readonly int[] _rev;
        readonly float[] _re, _im;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two", nameof(size));
            Size = size;
            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                _cos[i] = (float)Math.Cos(-2 * Math.PI * i / size);
                _sin[i] = (float)Math.Sin(-2 * Math.PI * i / size);
            }
            _rev = new int[size];
            int bits = 0;
            while ((1 << bits) < size) bits++;
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _rev[i] = r;
            }
            _re = new float[size];
            _im = new float[size];
        }

        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        /// <summary>In-place complex transform of (re, im), both of length Size. Inverse is scaled by 1/Size.</summary>
        public void Transform(float[] re, float[] im, bool inverse)
        {
            for (int i = 0; i < Size; i++)
            {
                int r = _rev[i];
                if (r > i)
                {
                    float t = re[i]; re[i] = re[r]; re[r] = t;
                    t = im[i]; im[i] = im[r]; im[r] = t;
                }
            }
            float sign = inverse ? -1f : 1f;
            for (int len = 2; len <= Size; len <<= 1)
            {
                int halfLen = len >> 1;
                int step = Size / len;
                for (int start = 0; start < Size; start += len)
                {
                    for (int k = 0; k < halfLen; k++)
                    {
                        float wr = _cos[k * step], wi = sign * _sin[k * step];
                        int a = start + k, b = a + halfLen;
                        float tr = re[b] * wr - im[b] * wi;
                        float ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
                }
            }
            if (inverse)
            {
                float inv = 1f / Size;
                for (int i = 0; i < Size; i++) { re[i] *= inv; im[i] *= inv; }
            }
        }

        /// <summary>Power spectrum (|X|^2) of a real signal for bins 0..Size/2. Input shorter than Size is zero-padded.</summary>
        public void PowerSpectrum(float[] input, int count, float[] powerOut)
        {
            for (int i = 0; i < Size; i++)
            {
                _re[i] = i < count ? input[i] : 0f;
                _im[i] = 0f;
            }
            Transform(_re, _im, false);
            int bins = Size / 2 + 1;
            for (int i = 0; i < bins; i++) powerOut[i] = _re[i] * _re[i] + _im[i] * _im[i];
        }
    }
}
