using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceBattle.Net
{
    /// <summary>Hosts a <see cref="GameServer"/> on a TCP port: accepts clients, frames bytes, ticks the server.</summary>
    public sealed class TcpServerHost : IDisposable
    {
        private readonly GameServer _server;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private int _nextId;

        public TcpServerHost(GameServer server, int port, IPAddress? bind = null)
        {
            _server = server;
            _listener = new TcpListener(bind ?? IPAddress.Any, port);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Start(int tickMs = 20)
        {
            _listener.Start();
            _ = Task.Run(AcceptLoop);
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try { _server.Tick(); } catch (Exception ex) { Console.Error.WriteLine("tick: " + ex); }
                    try { await Task.Delay(tickMs, _stop.Token).ConfigureAwait(false); } catch (TaskCanceledException) { }
                }
            });
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) when (_stop.IsCancellationRequested) { return; }
                catch (SocketException) { continue; }
                client.NoDelay = true;
                var conn = new TcpConnection("c" + Interlocked.Increment(ref _nextId), client);
                _server.OnConnect(conn);
                _ = Task.Run(() => ReadLoop(conn));
            }
        }

        private async Task ReadLoop(TcpConnection conn)
        {
            var reader = new FrameReader();
            var buf = new byte[8192];
            try
            {
                var stream = conn.Stream;
                while (!_stop.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length, _stop.Token).ConfigureAwait(false);
                    if (n <= 0) break;
                    reader.Feed(buf.AsSpan(0, n));
                    while (reader.Next() is { } f) _server.OnFrame(conn, f.kind, f.payload);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or FormatException or OperationCanceledException) { }
            finally
            {
                _server.OnDisconnect(conn);
                conn.Close("disconnected");
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch (SocketException) { }
        }

        private sealed class TcpConnection : IConnection
        {
            private readonly TcpClient _client;
            private readonly object _sendLock = new();
            public TcpConnection(string id, TcpClient client) { Id = id; _client = client; Stream = client.GetStream(); }
            public string Id { get; }
            public NetworkStream Stream { get; }

            public void Send(byte[] frame)
            {
                try { lock (_sendLock) Stream.Write(frame, 0, frame.Length); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            }

            public void Close(string reason)
            {
                try { _client.Close(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            }
        }
    }

    /// <summary>
    /// Game client connection. Reads on a background thread; the game polls <see cref="TryReceive"/> on its
    /// main thread (Unity Update). Also keeps a server-clock estimate from ping/pong.
    /// </summary>
    public sealed class NetClient : IDisposable
    {
        private readonly TcpClient _tcp = new() { NoDelay = true };
        private NetworkStream? _stream;
        private readonly ConcurrentQueue<object> _messages = new();
        private readonly ConcurrentQueue<(ushort sender, ushort seq, byte[] codec)> _voice = new();
        private readonly object _sendLock = new();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long _offsetMs; private long _bestRtt = long.MaxValue;
        private ushort _voiceSeq;

        public bool Connected => _tcp.Connected;
        public string? Disconnected { get; private set; }
        public long LocalMs => _clock.ElapsedMilliseconds;
        /// <summary>Estimated server clock (ms), from the lowest-latency ping so far.</summary>
        public long ServerMs => LocalMs + _offsetMs;
        public long RttMs => _bestRtt == long.MaxValue ? -1 : _bestRtt;

        public async Task ConnectAsync(string host, int port, string name, string character)
        {
            await _tcp.ConnectAsync(host, port).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            _ = Task.Run(ReadLoop);
            Send(new Hello(Wire.ProtocolVersion, name, character));
            Send(new Ping(LocalMs));
        }

        public void Send(object message) => Write(Wire.Encode(message));

        /// <summary>Sends an encoded voice packet (e.g. 20 ms of Opus); the server relays it to the room.</summary>
        public void SendVoice(ReadOnlySpan<byte> codec) => Write(Wire.Voice(0, _voiceSeq++, codec));

        public bool TryReceive(out object message) => _messages.TryDequeue(out message!);

        public bool TryReceiveVoice(out (ushort sender, ushort seq, byte[] codec) packet) => _voice.TryDequeue(out packet);

        public void PingNow() => Send(new Ping(LocalMs));

        private void Write(byte[] frame)
        {
            if (_stream == null) throw new InvalidOperationException("not connected");
            try { lock (_sendLock) _stream.Write(frame, 0, frame.Length); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Disconnected ??= ex.Message; }
        }

        private async Task ReadLoop()
        {
            var reader = new FrameReader();
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    int n = await _stream!.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                    if (n <= 0) break;
                    reader.Feed(buf.AsSpan(0, n));
                    while (reader.Next() is { } f)
                    {
                        if (f.kind == Wire.KindVoice) { _voice.Enqueue(Wire.ParseVoice(f.payload)); continue; }
                        var msg = Wire.Decode(f.payload);
                        if (msg is Pong pong)
                        {
                            long now = LocalMs, rtt = now - pong.ClientMs;
                            if (rtt >= 0 && rtt <= _bestRtt) { _bestRtt = rtt; _offsetMs = pong.ServerMs - (pong.ClientMs + rtt / 2); }
                        }
                        _messages.Enqueue(msg);
                    }
                }
                Disconnected ??= "server closed the connection";
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or FormatException or System.Text.Json.JsonException)
            {
                Disconnected ??= ex.Message;
            }
        }

        public void Dispose()
        {
            try { _tcp.Close(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
    }
}
