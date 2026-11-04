using System;
using System.Collections.Generic;

namespace VoiceBattle.Session.Online
{
    /// <summary>
    /// Where to join a friend: server address + room code. Travels through Steam as the rich-presence
    /// "connect" string ("+vb_server host:port +vb_room CODE"), which Steam passes on the command line when a
    /// friend clicks "Join game", and as lobby data (keys <see cref="LobbyServerKey"/>, <see cref="LobbyRoomKey"/>).
    /// </summary>
    public sealed record JoinInfo(string Host, int Port, string RoomCode)
    {
        public const string LobbyServerKey = "vb_server";
        public const string LobbyRoomKey = "vb_room";

        public string ToConnectString() => $"+vb_server {Host}:{Port} +vb_room {RoomCode}";
        public string ServerAddress => $"{Host}:{Port}";

        /// <summary>Parses a rich-presence connect string or a launch command line; null when it has no join info.</summary>
        public static JoinInfo? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var tokens = text!.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string? server = null, room = null;
            for (int i = 0; i + 1 < tokens.Length; i++)
            {
                if (tokens[i] == "+vb_server") server = tokens[i + 1];
                if (tokens[i] == "+vb_room") room = tokens[i + 1];
            }
            return server == null || room == null ? null : FromParts(server, room);
        }

        /// <summary>From lobby data values; null when they are missing or malformed.</summary>
        public static JoinInfo? FromParts(string? server, string? room)
        {
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(room)) return null;
            int colon = server!.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(server.Substring(colon + 1), out int port) || port < 1 || port > 65535) return null;
            string host = server.Substring(0, colon);
            foreach (var ch in host) if (!(char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_' || ch == ':' || ch == '[' || ch == ']')) return null;
            string code = room!.Trim().ToUpperInvariant();
            if (code.Length < 3 || code.Length > 12) return null;
            foreach (var ch in code) if (!char.IsLetterOrDigit(ch)) return null;
            return new JoinInfo(host, port, code);
        }

        /// <summary>"+connect_lobby 1234…" — Steam's argument when the game is started from a lobby invite.</summary>
        public static ulong? LobbyFromCommandLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < t.Length; i++)
                if (t[i] == "+connect_lobby" && ulong.TryParse(t[i + 1], out var id)) return id;
            return null;
        }
    }
}
