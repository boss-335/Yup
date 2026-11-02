using System;

namespace VoiceHorror.Dsp
{
    /// <summary>
    /// Turns raw mono audio into per-frame features: RMS (dBFS), YIN voicing/f0,
    /// spectral-flux onset strength and MFCC (spec §3.1–3.3).
    /// Allocates per call — intended for end-of-performance analysis on a worker thread,
    /// not for the per-frame audio path. Create one instance per thread.
    /// </summary>
    public sealed class FeatureExtractor : IFeatureExtractor
    {
        readonly DspConfig _cfg;
        readonly YinPitchDetector _yin;
        readonly Fft _fft;
        readonly float[] _window;
        readonly float[][] _melFilters;
        readonly double[,] _dct;
        readonly int _frame, _hop, _sr;
        readonly Fft _mfccFft;
        readonly float[] _mfccWindow;
        readonly float[][] _mfccMel;
        readonly int _mfccLen;

        public bool ComputeMfcc = true;

        public FeatureExtractor(DspConfig cfg = null)
        {
            _cfg = cfg ?? new DspConfig();
            _sr = _cfg.AnalysisSampleRate;
            _frame = _cfg.AnalysisFrameSize;
            _hop = _cfg.AnalysisHop;
            _yin = new YinPitchDetector(_cfg);
            _fft = new Fft(Fft.NextPow2(_frame));
            _window = DspMath.Hann(_frame);
            _melFilters = BuildMelFilters(_cfg.MelBands, _fft.Size, _sr, _cfg.MelLowHz, Math.Min(_cfg.MelHighHz, _sr / 2f - 1f));
            // MFCC uses a shorter centred window (~25 ms) so individual harmonics are not resolved:
            // the cepstrum then follows the vocal-tract envelope rather than the voice's pitch.
            _mfccLen = Math.Min(_frame, Math.Max(64, (int)Math.Round(_cfg.MfccWindowSec * _sr)));
            _mfccFft = new Fft(Fft.NextPow2(_mfccLen));
            _mfccWindow = DspMath.Hann(_mfccLen);
            _mfccMel = BuildMelFilters(_cfg.MelBands, _mfccFft.Size, _sr, _cfg.MelLowHz, Math.Min(_cfg.MelHighHz, _sr / 2f - 1f));
            _dct = BuildDct(_cfg.MfccCount + 1, _cfg.MelBands);
        }

        public DspConfig Config => _cfg;

        public FrameFeatures[] Extract(float[] mono, int sampleRate)
        {
            if (mono == null || mono.Length == 0) return Array.Empty<FrameFeatures>();
            float[] x = sampleRate == _sr ? mono : Resampler.Resample(mono, sampleRate, _sr);
            if (x.Length < _frame) Array.Resize(ref x, _frame);

            int frames = 1 + (x.Length - _frame) / _hop;
            var result = new FrameFeatures[frames];
            var windowed = new float[_frame];
            int bins = _fft.Size / 2 + 1;
            var power = new float[bins];
            var logMag = new float[bins];
            var prevLogMag = new float[bins];
            var mel = new double[_cfg.MelBands];
            var mfccIn = new float[_mfccLen];
            var mfccPower = new float[_mfccFft.Size / 2 + 1];
            bool havePrev = false;

            for (int f = 0; f < frames; f++)
            {
                int off = f * _hop;
                float rmsDb = DspMath.RmsDb(x, off, _frame);
                var (voiced, f0) = _yin.Detect(x, off, _frame, _sr);

                for (int i = 0; i < _frame; i++) windowed[i] = x[off + i] * _window[i];
                _fft.PowerSpectrum(windowed, _frame, power);

                // Onset: half-wave rectified spectral flux of log magnitude.
                double flux = 0;
                for (int k = 0; k < bins; k++)
                {
                    logMag[k] = (float)Math.Log(1e-6 + Math.Sqrt(power[k]));
                    if (havePrev)
                    {
                        double dlt = logMag[k] - prevLogMag[k];
                        if (dlt > 0) flux += dlt;
                    }
                }
                Array.Copy(logMag, prevLogMag, bins);
                havePrev = true;

                float[] mfcc = null;
                if (ComputeMfcc)
                {
                    int mOff = off + (_frame - _mfccLen) / 2;
                    for (int i = 0; i < _mfccLen; i++) mfccIn[i] = x[mOff + i] * _mfccWindow[i];
                    _mfccFft.PowerSpectrum(mfccIn, _mfccLen, mfccPower);
                    double melMax = 0;
                    for (int m = 0; m < mel.Length; m++)
                    {
                        double s = 0;
                        var filt = _mfccMel[m];
                        for (int k = 0; k < mfccPower.Length; k++) if (filt[k] != 0f) s += filt[k] * mfccPower[k];
                        mel[m] = s;
                        if (s > melMax) melMax = s;
                    }
                    // Relative floor: valleys below (peak - MelFloorDb) are clamped, so low-level noise
                    // filling spectral valleys changes the cepstrum much less.
                    double floor = Math.Max(1e-10, melMax * Math.Pow(10, -_cfg.MelFloorDb / 10.0));
                    for (int m = 0; m < mel.Length; m++) mel[m] = Math.Log(Math.Max(mel[m], floor));
                    mfcc = new float[_cfg.MfccCount];
                    for (int c = 1; c <= _cfg.MfccCount; c++) // skip c0 (energy)
                    {
                        double s = 0;
                        for (int m = 0; m < mel.Length; m++) s += _dct[c, m] * mel[m];
                        mfcc[c - 1] = (float)s;
                    }
                }

                result[f] = new FrameFeatures(rmsDb, voiced, voiced ? f0 : 0f, (float)(flux / bins), mfcc);
            }
            return result;
        }

        static float HzToMel(float hz) => 2595f * (float)Math.Log10(1 + hz / 700f);
        static float MelToHz(float mel) => 700f * ((float)Math.Pow(10, mel / 2595f) - 1);

        static float[][] BuildMelFilters(int bands, int fftSize, int sr, float lowHz, float highHz)
        {
            int bins = fftSize / 2 + 1;
            float lowMel = HzToMel(lowHz), highMel = HzToMel(highHz);
            var centers = new float[bands + 2];
            for (int i = 0; i < centers.Length; i++)
                centers[i] = MelToHz(lowMel + (highMel - lowMel) * i / (bands + 1)) * fftSize / sr;
            var filters = new float[bands][];
            for (int m = 0; m < bands; m++)
            {
                var f = new float[bins];
                float l = centers[m], c = centers[m + 1], r = centers[m + 2];
                for (int k = 0; k < bins; k++)
                {
                    if (k > l && k <= c) f[k] = (k - l) / Math.Max(1e-6f, c - l);
                    else if (k > c && k < r) f[k] = (r - k) / Math.Max(1e-6f, r - c);
                }
                filters[m] = f;
            }
            return filters;
        }

        static double[,] BuildDct(int coeffs, int bands)
        {
            var d = new double[coeffs, bands];
            for (int c = 0; c < coeffs; c++)
                for (int m = 0; m < bands; m++)
                    d[c, m] = Math.Sqrt(2.0 / bands) * Math.Cos(Math.PI * c * (m + 0.5) / bands);
            return d;
        }
    }
}
