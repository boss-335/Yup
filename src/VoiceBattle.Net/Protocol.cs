using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VoiceBattle.Net
{
    // ======================= client → server =======================
    /// <summary>
    /// First message. <see cref="SteamTicket"/> = hex of a Steam auth ticket from
    /// SteamUser.GetAuthTicketForWebApi(identity); required when the server runs with "auth": "steam".
    /// </summary>
    public sealed record Hello(int Protocol, string Name, string Character, string? SteamTicket = null);
    public sealed record CreateRoom(string Mode, bool Bots = false, bool Public = false);
    public sealed record JoinRoom(string Code);
    public sealed record QuickMatch(string Mode);
    public sealed record LeaveRoom();
    /// <summary>A loadout entry. Pack snippets are checked by the server against its packs; taught moves
    /// (Custom = true) only send their slot and length — their contour never leaves the device.</summary>
    public sealed record MoveSpec(string Slot, string SnippetId, string Title, int DurationMs, bool Custom);
    public sealed record SetLoadout(List<MoveSpec> Moves);
    public sealed record SetTeam(string Team);
    public sealed record SetReady(bool Ready);
    public sealed record StartMatch();
    /// <summary>
    /// One sung take, analysed on the client (GDD §7.1: move id, accuracy, tempo ratio, contour hash).
    /// <see cref="DurationMs"/> is how long the take lasted, used to anchor finisher windows and for plausibility.
    /// </summary>
    public sealed record Perform(string Slot, float Accuracy, float TempoRatio, string ContourHash, int DurationMs);
    public sealed record LockTarget(string? Target);
    public sealed record Ping(long ClientMs);

    // ======================= server → client =======================
    /// <summary><see cref="SteamId"/> is set when the server verified the player's Steam ticket.</summary>
    public sealed record Welcome(string PlayerId, long ServerMs, int Protocol, string? SteamId = null);
    public sealed record Error(string Code, string Message);
    /// <summary><see cref="Voice"/> = the sender index stamped on this player's relayed voice packets.</summary>
    public sealed record PlayerInfo(string Id, string Name, string Character, string Team, bool Ready, bool Bot, bool Host, List<MoveSpec> Moves, int Voice, string? SteamId = null);
    public sealed record RoomState(string Code, string Mode, string Phase, List<PlayerInfo> Players, int MinPlayers, int MaxPlayers, long? StartsAtMs);
    public sealed record FighterInfo(string Id, string Name, string Character, string Team, bool Bot, List<MoveSpec> Moves, int Voice);
    public sealed record MatchStarted(long ServerMs, int DurationMs, string Mode, List<FighterInfo> Fighters);
    public sealed record EventMsg(long AtMs, string Kind, string ActorId, string? TargetId, string? Slot, float Accuracy, float Damage,
                                  float ChargeGain, int Rep, string? Detail);
    public sealed record FighterState(string Id, float Hp, float Charge, int ChainReps, bool Alive, string? LockedTarget,
                                      Dictionary<string, long> ReadyAtMs);
    public sealed record Snapshot(long ServerMs, long RemainingMs, List<FighterState> Fighters);
    public sealed record MatchEnded(string? WinnerId, string? WinnerTeam, bool Draw, string Reason, List<string> Placements);
    public sealed record Pong(long ClientMs, long ServerMs);

    /// <summary>
    /// Wire format: frame = length:int32 big-endian · kind:byte · payload. Kind 0 = JSON message
    /// {"t": type, …fields}; kind 1 = voice packet (sender index:uint16 · sequence:uint16 · codec bytes),
    /// relayed by the server to the rest of the room so players hear each other (GDD §7.1).
    /// </summary>
    public static class Wire
    {
        public const int ProtocolVersion = 1;
        public const int MaxFrameBytes = 64 * 1024;
        public const byte KindJson = 0, KindVoice = 1;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static readonly Dictionary<string, Type> Types = new();
        private static readonly Dictionary<Type, string> Names = new();

        static Wire()
        {
            foreach (var t in new[]
            {
                typeof(Hello), typeof(CreateRoom), typeof(JoinRoom), typeof(QuickMatch), typeof(LeaveRoom), typeof(SetLoadout),
                typeof(SetTeam), typeof(SetReady), typeof(StartMatch), typeof(Perform), typeof(LockTarget), typeof(Ping),
                typeof(Welcome), typeof(Error), typeof(RoomState), typeof(MatchStarted), typeof(EventMsg), typeof(Snapshot),
                typeof(MatchEnded), typeof(Pong),
            })
            {
                string name = char.ToLowerInvariant(t.Name[0]) + t.Name.Substring(1);
                Types[name] = t; Names[t] = name;
            }
        }

        public static byte[] Encode(object message)
        {
            if (!Names.TryGetValue(message.GetType(), out var name)) throw new ArgumentException($"not a protocol message: {message.GetType().Name}");
            var node = JsonSerializer.SerializeToNode(message, message.GetType(), Json)!.AsObject();
            node["t"] = name;
            return Frame(KindJson, Encoding.UTF8.GetBytes(node.ToJsonString()));
        }

        /// <summary>Parses a JSON payload (without the frame header) into its message record.</summary>
        public static object Decode(byte[] payload)
        {
            if (payload.Length > MaxFrameBytes) throw new FormatException("message too large");
            var node = JsonNode.Parse(payload) as JsonObject ?? throw new FormatException("message is not an object");
            string t = node["t"]?.GetValue<string>() ?? throw new FormatException("message without type");
            if (!Types.TryGetValue(t, out var type)) throw new FormatException($"unknown message type '{t}'");
            node.Remove("t");
            return JsonSerializer.Deserialize(node.ToJsonString(), type, Json) ?? throw new FormatException($"empty '{t}'");
        }

        public static byte[] Voice(ushort sender, ushort sequence, ReadOnlySpan<byte> codec)
        {
            var payload = new byte[4 + codec.Length];
            payload[0] = (byte)(sender >> 8); payload[1] = (byte)sender;
            payload[2] = (byte)(sequence >> 8); payload[3] = (byte)sequence;
            codec.CopyTo(payload.AsSpan(4));
            return Frame(KindVoice, payload);
        }

        public static (ushort sender, ushort sequence, byte[] codec) ParseVoice(byte[] payload)
        {
            if (payload.Length < 4) throw new FormatException("voice packet too short");
            return ((ushort)(payload[0] << 8 | payload[1]), (ushort)(payload[2] << 8 | payload[3]), payload.AsSpan(4).ToArray());
        }

        public static byte[] Frame(byte kind, byte[] payload)
        {
            if (payload.Length + 1 > MaxFrameBytes) throw new ArgumentException("frame too large");
            var f = new byte[5 + payload.Length];
            int len = payload.Length + 1;
            f[0] = (byte)(len >> 24); f[1] = (byte)(len >> 16); f[2] = (byte)(len >> 8); f[3] = (byte)len;
            f[4] = kind;
            Buffer.BlockCopy(payload, 0, f, 5, payload.Length);
            return f;
        }
    }

    /// <summary>Incremental frame parser for a byte stream (TCP). Feed bytes, take complete frames.</summary>
    public sealed class FrameReader
    {
        private readonly List<byte> _buf = new();

        public void Feed(ReadOnlySpan<byte> data) { foreach (var b in data) _buf.Add(b); }

        /// <summary>Next complete frame (kind, payload) or null. Throws on a malformed length.</summary>
        public (byte kind, byte[] payload)? Next()
        {
            if (_buf.Count < 4) return null;
            int len = _buf[0] << 24 | _buf[1] << 16 | _buf[2] << 8 | _buf[3];
            if (len < 1 || len > Wire.MaxFrameBytes) throw new FormatException($"bad frame length {len}");
            if (_buf.Count < 4 + len) return null;
            byte kind = _buf[4];
            var payload = _buf.GetRange(5, len - 1).ToArray();
            _buf.RemoveRange(0, 4 + len);
            return (kind, payload);
        }
    }
}
