#nullable enable
using System.Linq;
using UnityEngine;
using VoiceBattle.Net;
using VoiceBattle.Session.Text;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Developer overlay for online play (IMGUI): connect, quick match / private room by code per mode,
    /// teams, ready/start, then live HP, target lock, mute per player, and the final placements.
    /// </summary>
    [RequireComponent(typeof(NetworkMatch))]
    public sealed class OnlineHud : MonoBehaviour
    {
        private NetworkMatch? _m;
        private string _code = "";
        private bool _bots = true;
        private GUIStyle? _big;

        private void Awake() => _m = GetComponent<NetworkMatch>();

        private static string Ar(string s) => ArabicShaper.ForDisplay(s);

        private void OnGUI()
        {
            if (_m == null) return;
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(760, Screen.width - 32), Screen.height - 32));
            if (_m.Error != null) { GUILayout.Label("ERROR: " + _m.Error, _big); GUILayout.EndArea(); return; }
            var s = _m.Session;
            GUILayout.Label($"Server {_m.serverHost}:{_m.serverPort} — {_m.Status}" + (s != null && s.Net.RttMs >= 0 ? $"   ping {s.Net.RttMs} ms" : ""));
            if (_m.Steam != null) GUILayout.Label(_m.Steam.Available ? $"Steam: {Ar(_m.Steam.PersonaName)}" : $"Steam: off ({_m.Steam.Problem})");

            if (s == null || !s.Connected)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Server:"); _m.serverHost = GUILayout.TextField(_m.serverHost, 64);
                GUILayout.Label("Name:"); _m.playerName = GUILayout.TextField(_m.playerName, 20);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Connect", GUILayout.Height(36))) _m.Connect();
                GUILayout.EndArea();
                return;
            }

            var st = s.State;
            if (st.LastError != null) GUILayout.Label("  ⚠ " + st.LastError.Message);
            var modes = _m.Data!.Modes.All;

            if (st.Room == null)
            {
                GUILayout.Label("Quick match (with strangers — their voice starts muted):", _big);
                GUILayout.BeginHorizontal();
                foreach (var md in modes) if (GUILayout.Button(Ar(md.NameAr) + "\n" + md.NameEn, GUILayout.Height(48))) s.QuickMatch(md.Id);
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                GUILayout.Label("Private room with friends:", _big);
                _bots = GUILayout.Toggle(_bots, " fill empty places with bots");
                GUILayout.BeginHorizontal();
                foreach (var md in modes) if (GUILayout.Button("Create " + md.NameEn)) s.CreateRoom(md.Id, _bots);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Room code:"); _code = GUILayout.TextField(_code, 8).ToUpperInvariant();
                if (GUILayout.Button("Join", GUILayout.Width(80))) s.JoinRoom(_code);
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            var room = st.Room;
            var mode = _m.Data.Modes.Get(room.Mode);
            if (!st.InMatch && st.Result == null)
            {
                GUILayout.Label($"Room {room.Code} — {Ar(mode.NameAr)} ({mode.NameEn})   {room.Players.Count}/{room.MaxPlayers}", _big);
                if (room.StartsAtMs is long at) GUILayout.Label($"Starting in {Mathf.Max(0, (at - s.ServerMs) / 1000f):F0} s…", _big);
                foreach (var p in room.Players.OrderBy(p => p.Team))
                    GUILayout.Label($"  {(mode.IsTeamMode ? "[" + p.Team + "] " : "")}{Ar(p.Name)}{(p.Host ? " ★host" : "")}{(p.Bot ? " (bot)" : "")}{(p.Ready ? " ✓ ready" : "")}  {string.Join(" · ", p.Moves.Select(mv => Ar(mv.Title)))}");
                GUILayout.BeginHorizontal();
                if (mode.IsTeamMode) foreach (var t in new[] { "A", "B" }) if (GUILayout.Button("Join team " + t)) s.SetTeam(t);
                var me = room.Players.FirstOrDefault(p => p.Id == st.PlayerId);
                if (me != null && GUILayout.Button(me.Ready ? "Not ready" : "Ready")) s.SetReady(!me.Ready);
                if (st.IsHost && GUILayout.Button("Start match")) s.StartMatch();
                if (_m.Steam != null && _m.Steam.Available && _m.Steam.InParty && GUILayout.Button("Invite Steam friends")) _m.InviteSteamFriends();
                if (GUILayout.Button("Leave")) s.Leave();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            if (st.Result != null)
            {
                var r = st.Result;
                string win = r.Draw ? "DRAW" : r.WinnerTeam != null && mode.IsTeamMode ? $"Team {r.WinnerTeam} wins" : $"{Name(st, r.WinnerId)} wins";
                GUILayout.Label(win + $"  ({r.Reason})", _big);
                for (int i = 0; i < r.Placements.Count; i++) GUILayout.Label($"  {i + 1}. {Name(st, r.Placements[i])}{(r.Placements[i] == st.PlayerId ? "  ← you" : "")}");
                GUILayout.Label("Back to the room in a few seconds…");
                GUILayout.EndArea();
                return;
            }

            // In the match.
            var snap = st.Last;
            GUILayout.Label($"{Ar(mode.NameAr)}   time left {(snap?.RemainingMs ?? 0) / 1000 / 60}:{(snap?.RemainingMs ?? 0) / 1000 % 60:00}", _big);
            var my = st.MyState;
            if (my != null) GUILayout.Label($"You: HP {my.Hp:F0}  charge {my.Charge:F0}{(my.ChainReps > 0 ? $"  FINISHER {my.ChainReps}/4" : "")}{(my.Alive ? "" : "  — out")}");
            GUILayout.Label("Target: " + (my?.LockedTarget == null ? "automatic (weakest)" : Name(st, my.LockedTarget)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto", GUILayout.Width(60))) s.LockTarget(null);
            foreach (var o in st.Opponents().Take(8))
            {
                var os = snap?.Fighters.FirstOrDefault(f => f.Id == o.Id);
                if (os != null && os.Alive && GUILayout.Button($"{Ar(o.Name)} {os.Hp:F0}")) s.LockTarget(o.Id);
            }
            GUILayout.EndHorizontal();
            foreach (var f in st.Match!.Fighters.OrderBy(f => f.Team))
            {
                var fs = snap?.Fighters.FirstOrDefault(x => x.Id == f.Id);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {(mode.IsTeamMode ? "[" + f.Team + "] " : "")}{Ar(f.Name)}{(f.Bot ? " (bot)" : "")}  {(fs == null ? "" : fs.Alive ? $"HP {fs.Hp:F0}" : "out")}", GUILayout.Width(360));
                if (!f.Bot && f.Id != st.PlayerId && GUILayout.Button(s.Voice.IsMuted(f.Voice) ? "🔇 unmute" : "🔊 mute", GUILayout.Width(100))) _m.ToggleMute(f.Voice);
                GUILayout.EndHorizontal();
            }
            if (_m.LastTake != null)
            {
                var rec = _m.LastTake.Recognition;
                GUILayout.Label(rec.Recognized ? $"Last take: {rec.Slot} {rec.Result!.Accuracy:F2}" : $"Last take: not recognised ({rec.Reason})");
            }
            GUILayout.EndArea();
        }

        private static string Name(OnlineState st, string? id) =>
            id == null ? "—" : ArabicShaper.ForDisplay(st.Match?.Fighters.FirstOrDefault(f => f.Id == id)?.Name ?? id);
    }
}
