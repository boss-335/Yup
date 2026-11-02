using System;

namespace VoiceMatch.Core.Dsp
{
    public enum EndpointEvent { None, Started, Ended }

    /// <summary>
    /// Streaming voice-activity endpointer for live mic input (GDD §6.2): a performance starts after
    /// <see cref="StartFrames"/> consecutive loud hops and ends after <c>EndSilenceMs</c> of silence.
    /// Feed raw mic buffers of any size; positions are reported in samples since <see cref="Reset"/>.
    /// The Unity layer calls this from the mic callback and hands the captured span to ContourBuilder.
    /// </summary>
    public sealed class PerformanceEndpointer
    {
        private readonly int _hop;
        private readonly float _thresholdDb;
        private readonly int _endSilenceHops;
        private double _acc;
        private int _accCount;
        private long _pos;
        private int _loudRun;
        private int _silentRun;

        public PerformanceEndpointer(ContourConfig cfg, int sampleRate, float? thresholdDb = null)
        {
            _hop = Math.Max(1, (int)Math.Round(cfg.Hop * sampleRate / (double)cfg.SampleRate));
            _thresholdDb = thresholdDb ?? cfg.MinRmsDb;
            _endSilenceHops = Math.Max(1, (int)Math.Ceiling(cfg.EndSilenceMs / 1000.0 * sampleRate / _hop));
        }

        public int StartFrames { get; init; } = 2;
        public bool Active { get; private set; }
        public long StartSample { get; private set; } = -1;
        public long EndSample { get; private set; } = -1;

        public void Reset()
        {
            _acc = 0; _accCount = 0; _pos = 0; _loudRun = 0; _silentRun = 0;
            Active = false; StartSample = -1; EndSample = -1;
        }

        /// <summary>Processes a buffer and returns the first event it produced (if any).</summary>
        public EndpointEvent Process(ReadOnlySpan<float> buffer)
        {
            var evt = EndpointEvent.None;
            foreach (var s in buffer)
            {
                _acc += s * (double)s;
                _accCount++;
                _pos++;
                if (_accCount < _hop) continue;

                double db = 10 * Math.Log10(_acc / _accCount + 1e-24);
                _acc = 0; _accCount = 0;
                bool loud = db >= _thresholdDb;

                if (!Active)
                {
                    _loudRun = loud ? _loudRun + 1 : 0;
                    if (_loudRun >= StartFrames && EndSample < 0)
                    {
                        Active = true;
                        _silentRun = 0;
                        StartSample = Math.Max(0, _pos - (long)_loudRun * _hop);
                        if (evt == EndpointEvent.None) evt = EndpointEvent.Started;
                    }
                }
                else
                {
                    _silentRun = loud ? 0 : _silentRun + 1;
                    if (_silentRun >= _endSilenceHops)
                    {
                        Active = false;
                        EndSample = _pos - (long)_silentRun * _hop;
                        if (evt == EndpointEvent.None) evt = EndpointEvent.Ended;
                    }
                }
            }
            return evt;
        }
    }
}
