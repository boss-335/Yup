using System;
using System.Collections.Generic;
using VoiceMatch.Core;
using VoiceMatch.Core.Dsp;

namespace VoiceBattle.Session
{
    /// <summary>One sung take cut out of the live mic stream. Times are ms since capture started.</summary>
    public sealed record Performance(float[] Samples, int SampleRate, long StartMs, long EndMs)
    {
        public int DurationMs => (int)(EndMs - StartMs);
    }

    /// <summary>
    /// Turns a continuous mic stream into takes (GDD §6.2): voice-activity start, 250 ms of silence ends
    /// the take, with pre-roll so the first consonant is kept. Also supports push-to-sing. The Unity mic
    /// script only has to call <see cref="Push"/> with whatever the device delivered.
    /// </summary>
    public sealed class PerformanceCapture
    {
        private readonly ContourConfig _cfg;
        private readonly SessionConfig _session;
        private readonly int _rate;
        private readonly List<float> _buf = new();
        private PerformanceEndpointer _ep;
        private float _thresholdDb;
        private long _bufStart;     // absolute sample index of _buf[0]
        private long _epBase;       // absolute sample index where the endpointer was last reset
        private long _total;        // samples pushed so far
        private long _perfStart = -1;
        private bool _manual;

        public PerformanceCapture(ContourConfig cfg, SessionConfig session, int sampleRate)
        {
            _cfg = cfg; _session = session; _rate = sampleRate;
            _thresholdDb = cfg.MinRmsDb;
            _ep = NewEndpointer();
        }

        public bool InPerformance => _perfStart >= 0;
        public float ThresholdDb => _thresholdDb;
        public long NowMs => _total * 1000 / _rate;

        /// <summary>
        /// Sets the voice threshold from a stretch of room noise (ask the player to stay quiet for a second):
        /// 12 dB above the noise floor, never below the configured minimum.
        /// </summary>
        public void Calibrate(ReadOnlySpan<float> roomNoise)
        {
            double s = 0;
            foreach (var v in roomNoise) s += v * (double)v;
            float floorDb = roomNoise.Length == 0 ? -120f : (float)(10 * Math.Log10(s / roomNoise.Length + 1e-24));
            _thresholdDb = Math.Max(_cfg.MinRmsDb, floorDb + 12f);
            ResetEndpointer();
        }

        /// <summary>Push-to-sing: start a take now regardless of level.</summary>
        public void BeginManual()
        {
            if (InPerformance) return;
            _manual = true;
            _perfStart = Math.Max(_bufStart, _total - Ms(_session.PreRollMs));
        }

        /// <summary>Push-to-sing: end the take now.</summary>
        public Performance? EndManual()
        {
            if (!InPerformance || !_manual) return null;
            return Cut(_total);
        }

        /// <summary>Feeds mic samples; returns a finished take when one ended inside this buffer.</summary>
        public Performance? Push(ReadOnlySpan<float> samples)
        {
            for (int i = 0; i < samples.Length; i++) _buf.Add(samples[i]);
            _total += samples.Length;
            if (_manual) return LimitLength();

            _ep.Process(samples);
            if (!InPerformance && _ep.StartSample >= 0)
                _perfStart = Math.Max(_bufStart, _epBase + _ep.StartSample - Ms(_session.PreRollMs));

            if (InPerformance && _ep.EndSample >= 0)
                return Cut(Math.Min(_total, _epBase + _ep.EndSample + Ms(50)));

            if (!InPerformance) TrimIdle();
            return LimitLength();
        }

        private Performance? LimitLength()
        {
            if (InPerformance && _total - _perfStart > Ms(_session.MaxPerformanceMs)) return Cut(_total);
            return null;
        }

        private Performance Cut(long end)
        {
            int from = (int)(_perfStart - _bufStart), to = (int)(end - _bufStart);
            var take = _buf.GetRange(from, Math.Max(0, to - from)).ToArray();
            var p = new Performance(take, _rate, _perfStart * 1000 / _rate, end * 1000 / _rate);
            _perfStart = -1;
            _manual = false;
            ResetEndpointer();
            TrimIdle();
            return p;
        }

        private void TrimIdle()
        {
            long keep = Ms(_session.PreRollMs) + Ms(50);
            long excess = _buf.Count - keep;
            if (excess > 0) { _buf.RemoveRange(0, (int)excess); _bufStart += excess; }
        }

        private void ResetEndpointer()
        {
            _ep = NewEndpointer();
            _epBase = _total;
        }

        private PerformanceEndpointer NewEndpointer() => new PerformanceEndpointer(_cfg, _rate, _thresholdDb);

        private long Ms(int ms) => (long)ms * _rate / 1000;
    }
}
