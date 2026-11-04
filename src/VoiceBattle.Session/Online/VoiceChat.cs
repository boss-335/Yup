using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Concentus;
using Concentus.Enums;
using VoiceMatch.Core.Dsp;

namespace VoiceBattle.Session.Online
{
    public sealed record VoiceConfig
    {
        public int SampleRate { get; init; } = 16000;
        public int FrameMs { get; init; } = 20;
        public int Bitrate { get; init; } = 16000;
        public int JitterMs { get; init; } = 60;
        public int MaxBufferMs { get; init; } = 400;
        /// <summary>Frames quieter than this are not sent (saves bandwidth; nothing to hear anyway).</summary>
        public float SendGateDb { get; init; } = -50f;
        /// <summary>Keep sending this long after the voice drops, so word endings are not cut.</summary>
        public int HangoverMs { get; init; } = 300;
        /// <summary>GDD §9: voice of strangers (public matches) is muted until the player unmutes them.</summary>
        public bool MuteStrangersByDefault { get; init; } = true;

        public int FrameSamples => SampleRate * FrameMs / 1000;

        public static VoiceConfig Load(string path)
        {
            using var d = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var r = d.RootElement;
            int I(string n, int v) => r.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : v;
            var c = new VoiceConfig();
            c = c with
            {
                SampleRate = I("sample_rate", c.SampleRate), FrameMs = I("frame_ms", c.FrameMs), Bitrate = I("bitrate", c.Bitrate),
                JitterMs = I("jitter_ms", c.JitterMs), MaxBufferMs = I("max_buffer_ms", c.MaxBufferMs), HangoverMs = I("hangover_ms", c.HangoverMs),
                SendGateDb = r.TryGetProperty("send_gate_db", out var g) ? (float)g.GetDouble() : c.SendGateDb,
                MuteStrangersByDefault = !r.TryGetProperty("mute_strangers_by_default", out var m) || m.GetBoolean(),
            };
            if (c.SampleRate != 8000 && c.SampleRate != 12000 && c.SampleRate != 16000 && c.SampleRate != 24000 && c.SampleRate != 48000)
                throw new FormatException("voice.json: sample_rate must be an Opus rate (8/12/16/24/48 kHz)");
            if (c.FrameMs != 10 && c.FrameMs != 20 && c.FrameMs != 40 && c.FrameMs != 60) throw new FormatException("voice.json: frame_ms must be 10/20/40/60");
            return c;
        }
    }

    /// <summary>Mic samples (any rate) → Opus packets, skipping silence (with hangover).</summary>
    public sealed class VoiceEncoder
    {
        private readonly VoiceConfig _cfg;
        private readonly IOpusEncoder _enc;
        private readonly List<float> _pending = new();
        private readonly byte[] _out = new byte[1500];
        private int _hangoverFrames;

        public VoiceEncoder(VoiceConfig cfg)
        {
            _cfg = cfg;
            _enc = OpusCodecFactory.CreateEncoder(cfg.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _enc.Bitrate = cfg.Bitrate;
        }

        public int FramesSent { get; private set; }
        public int FramesSkipped { get; private set; }

        /// <summary>Returns the packets ready to send (zero or more).</summary>
        public List<byte[]> Push(ReadOnlySpan<float> mic, int micRate)
        {
            var x = AudioPrep.ResampleTo(mic.ToArray(), micRate, _cfg.SampleRate);
            _pending.AddRange(x);
            var packets = new List<byte[]>();
            int n = _cfg.FrameSamples;
            while (_pending.Count >= n)
            {
                var frame = _pending.GetRange(0, n).ToArray();
                _pending.RemoveRange(0, n);
                double sq = 0;
                foreach (var v in frame) sq += v * (double)v;
                bool loud = 10 * Math.Log10(sq / n + 1e-24) >= _cfg.SendGateDb;
                if (loud) _hangoverFrames = _cfg.HangoverMs / _cfg.FrameMs;
                else if (_hangoverFrames > 0) _hangoverFrames--;
                else { FramesSkipped++; continue; }
                int len = _enc.Encode(frame, n, _out, _out.Length);
                if (len > 0) { packets.Add(_out.AsSpan(0, len).ToArray()); FramesSent++; }
            }
            return packets;
        }
    }

    /// <summary>
    /// Receives everyone's voice packets, orders them in a small jitter buffer per sender, conceals single
    /// lost packets, and mixes unmuted senders into one mono stream at <see cref="VoiceConfig.SampleRate"/>.
    /// The audio thread calls <see cref="Read"/>; the network thread calls <see cref="Receive"/>.
    /// </summary>
    public sealed class VoiceMixer
    {
        private sealed class Stream
        {
            public IOpusDecoder Dec = null!;
            public readonly SortedDictionary<int, byte[]> Packets = new();
            public int? Next;                 // next sequence to play (unwrapped)
            public int LastSeqRaw = -1, Wraps;
            public bool Started;
            public readonly Queue<float> Pcm = new();
            public float Level;
        }

        private readonly VoiceConfig _cfg;
        private readonly Dictionary<int, Stream> _streams = new();
        private readonly HashSet<int> _muted = new();
        private readonly object _lock = new();

        public VoiceMixer(VoiceConfig cfg) => _cfg = cfg;

        public void Mute(int sender, bool muted) { lock (_lock) { if (muted) _muted.Add(sender); else _muted.Remove(sender); } }
        public bool IsMuted(int sender) { lock (_lock) return _muted.Contains(sender); }

        /// <summary>Recent loudness 0..1 of a sender (to animate their character's mouth).</summary>
        public float LevelOf(int sender) { lock (_lock) return _streams.TryGetValue(sender, out var s) ? s.Level : 0; }

        public void Receive(int sender, ushort seq, byte[] packet)
        {
            lock (_lock)
            {
                if (!_streams.TryGetValue(sender, out var s))
                    _streams[sender] = s = new Stream { Dec = OpusCodecFactory.CreateDecoder(_cfg.SampleRate, 1) };
                // Unwrap the 16-bit sequence.
                if (s.LastSeqRaw >= 0 && seq < 1000 && s.LastSeqRaw > 64000) s.Wraps++;
                s.LastSeqRaw = seq;
                int full = s.Wraps * 65536 + seq;
                if (s.Next != null && full < s.Next) return; // too late
                s.Packets[full] = packet;
                int maxPackets = _cfg.MaxBufferMs / _cfg.FrameMs;
                while (s.Packets.Count > maxPackets) s.Packets.Remove(s.Packets.Keys.First());
            }
        }

        /// <summary>Fills <paramref name="output"/> with the mix (silence where nobody talks).</summary>
        public void Read(Span<float> output)
        {
            output.Clear();
            lock (_lock)
            {
                foreach (var kv in _streams)
                {
                    var s = kv.Value;
                    Fill(s, output.Length);
                    bool muted = _muted.Contains(kv.Key);
                    double sq = 0;
                    for (int i = 0; i < output.Length && s.Pcm.Count > 0; i++)
                    {
                        float v = s.Pcm.Dequeue();
                        sq += v * v;
                        if (!muted) output[i] += v;
                    }
                    s.Level = s.Level * .7f + .3f * (float)Math.Min(1, Math.Sqrt(sq / Math.Max(1, output.Length)) * 6);
                }
            }
            for (int i = 0; i < output.Length; i++) output[i] = Math.Max(-1f, Math.Min(1f, output[i]));
        }

        private void Fill(Stream s, int needed)
        {
            int n = _cfg.FrameSamples;
            var frame = new float[n];
            // Wait for the jitter buffer before starting a stream (or after it ran dry).
            if (!s.Started)
            {
                if (s.Packets.Count * _cfg.FrameMs < _cfg.JitterMs) return;
                s.Started = true;
                s.Next = s.Packets.Keys.First();
            }
            while (s.Pcm.Count < needed)
            {
                if (s.Packets.Count == 0) { s.Started = false; s.Next = null; return; }
                if (s.Packets.TryGetValue(s.Next!.Value, out var pkt))
                {
                    s.Packets.Remove(s.Next.Value);
                    s.Dec.Decode(pkt, frame, n, false);
                }
                else
                {
                    // Lost or late: let Opus conceal it, then move on.
                    s.Dec.Decode(ReadOnlySpan<byte>.Empty, frame, n, false);
                }
                foreach (var v in frame) s.Pcm.Enqueue(v);
                s.Next++;
            }
        }
    }
}
