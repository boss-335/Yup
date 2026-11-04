// VB_STEAMWORKS is set by Editor/VoiceBattleEditor.cs when the Steamworks.NET package is installed.
#if VB_STEAMWORKS && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX)
#define VB_STEAM
#endif
#nullable enable
using System;
using System.IO;
using UnityEngine;
#if VB_STEAM
using Steamworks;
#endif
using VoiceBattle.Session.Online;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// Steam (Steamworks.NET 2025.164.1, see unity/Packages/manifest.json): player name, login ticket for the
    /// server, a friends-only lobby that carries server + room code so friends can join from the Steam friends
    /// list or an overlay invite, and rich presence. Without Steam running the game still works (no login).
    /// One instance lives for the whole run (<see cref="Get"/>): Steam is initialised once, and an invite that
    /// arrives in the menu or in training waits in <see cref="TakePendingJoin"/> until the online screen opens.
    /// Settings: StreamingAssets/data/steam.json (AppID 480 = Valve's test app until Voice Battle has its own).
    /// </summary>
    public sealed partial class SteamBridge : MonoBehaviour
    {
        private static SteamBridge? _instance;

        /// <summary>The single bridge, created on first use and kept across scene loads.</summary>
        public static SteamBridge Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SteamBridge>();
            return _instance;
        }

        public SteamConfig Config { get; private set; } = SteamConfig.Default;
        public bool Available { get; private set; }
        public string? Problem { get; private set; }

        /// <summary>A friend wants us in their match (invite accepted, "Join game", or started from an invite).</summary>
        public event Action<JoinInfo>? JoinRequested;

        private JoinInfo? _pendingJoin;
        public bool HasPendingJoin => _pendingJoin != null;

        /// <summary>The join that arrived while nobody listened (or came on the command line); null when none.</summary>
        public JoinInfo? TakePendingJoin()
        {
            var j = _pendingJoin;
            _pendingJoin = null;
            return j;
        }

        private void RaiseJoin(JoinInfo info)
        {
            if (JoinRequested != null) JoinRequested(info);
            else _pendingJoin = info;
        }

        private void LoadConfig()
        {
            try { Config = SteamConfig.Load(Path.Combine(Application.streamingAssetsPath, "data", "steam.json")); }
            catch (Exception ex) { Debug.LogWarning("steam.json: " + ex.Message); }
            // "+vb_server h:p +vb_room CODE" also works without Steam (LAN tests, shortcuts).
            _pendingJoin = SteamConfig.JoinFromCommandLine(Environment.GetCommandLineArgs());
        }

        private void OnApplicationQuit() => Shutdown();
    }

#if VB_STEAM
    public sealed partial class SteamBridge
    {
        public string PersonaName => Available ? SteamFriends.GetPersonaName() : "";
        public ulong SteamId => Available ? SteamUser.GetSteamID().m_SteamID : 0;
        public bool InParty => _lobby.m_SteamID != 0;

        private Callback<GetTicketForWebApiResponse_t>? _ticketCb;
        private Callback<GameLobbyJoinRequested_t>? _lobbyJoinCb;
        private Callback<GameRichPresenceJoinRequested_t>? _presenceJoinCb;
        private CallResult<LobbyCreated_t>? _created;
        private CallResult<LobbyEnter_t>? _entered;
        private Action<string?>? _pendingTicket;
        private HAuthTicket _ticket = HAuthTicket.Invalid;
        private CSteamID _lobby;
        private JoinInfo? _partyInfo;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            LoadConfig();
            try
            {
                if (!Packsize.Test()) { Problem = "Steamworks.NET was built for the wrong platform"; return; }
                if (!DllCheck.Test()) { Problem = "steam_api library missing or wrong version"; return; }
#if !UNITY_EDITOR
                // Outside the Editor a build started without Steam relaunches itself through Steam.
                if (Config.RelaunchThroughSteam && !Config.IsTestApp && SteamAPI.RestartAppIfNecessary(new AppId_t(Config.AppId))) { Application.Quit(); return; }
#endif
                Available = SteamAPI.Init();
                if (!Available) { Problem = "Steam is not running (or no steam_appid.txt)"; return; }
            }
            catch (DllNotFoundException ex) { Problem = "steam_api not found: " + ex.Message; return; }

            _ticketCb = Callback<GetTicketForWebApiResponse_t>.Create(OnTicket);
            _lobbyJoinCb = Callback<GameLobbyJoinRequested_t>.Create(r => JoinLobby(r.m_steamIDLobby));
            _presenceJoinCb = Callback<GameRichPresenceJoinRequested_t>.Create(r =>
            {
                var info = JoinInfo.Parse(r.m_rgchConnect);
                if (info != null) RaiseJoin(info);
            });
            _created = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _entered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);

            // Started from an invite while the game was closed: Steam passes it on the command line.
            SteamApps.GetLaunchCommandLine(out string cmd, 1024);
            string all = cmd + " " + string.Join(" ", Environment.GetCommandLineArgs());
            var direct = JoinInfo.Parse(all);
            if (direct != null) _pendingJoin = direct;
            else if (JoinInfo.LobbyFromCommandLine(all) is ulong lobby) JoinLobby(new CSteamID(lobby));
        }

        private void Update() { if (Available) SteamAPI.RunCallbacks(); }

        private void Shutdown()
        {
            if (!Available) return;
            LeaveParty();
            if (_ticket != HAuthTicket.Invalid) SteamUser.CancelAuthTicket(_ticket);
            SteamAPI.Shutdown();
            Available = false;
        }

        /// <summary>Asks Steam for a Web API ticket; <paramref name="done"/> gets its hex (null without Steam).</summary>
        public void RequestTicket(Action<string?> done)
        {
            if (!Available) { done(null); return; }
            if (_ticket != HAuthTicket.Invalid) SteamUser.CancelAuthTicket(_ticket);
            _pendingTicket = done;
            _ticket = SteamUser.GetAuthTicketForWebApi(Config.TicketIdentity);
        }

        private void OnTicket(GetTicketForWebApiResponse_t r)
        {
            var cb = _pendingTicket;
            _pendingTicket = null;
            if (cb == null || r.m_hAuthTicket != _ticket) return;
            if (r.m_eResult != EResult.k_EResultOK) { Problem = "Steam ticket: " + r.m_eResult; cb(null); return; }
            cb(BitConverter.ToString(r.m_rgubTicket, 0, r.m_cubTicket).Replace("-", ""));
        }

        /// <summary>Opens a friends-only Steam lobby for this room so friends can join from Steam. Call when in a private room.</summary>
        public void SetParty(JoinInfo info, int maxMembers)
        {
            if (!Available) return;
            _partyInfo = info;
            SteamFriends.SetRichPresence("connect", info.ToConnectString());
            SteamFriends.SetRichPresence("status", "Voice Battle — room " + info.RoomCode);
            if (_lobby.m_SteamID != 0) { WriteLobbyData(); return; }
            _created!.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Mathf.Clamp(maxMembers, 2, Config.MaxPartyMembers)));
        }

        /// <summary>Opens the Steam overlay's invite dialog for the current party.</summary>
        public void InviteFriends()
        {
            if (Available && _lobby.m_SteamID != 0) SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
        }

        public void LeaveParty()
        {
            if (!Available) return;
            if (_lobby.m_SteamID != 0) SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = default;
            _partyInfo = null;
            SteamFriends.ClearRichPresence();
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Problem = "Steam lobby: " + r.m_eResult; return; }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            WriteLobbyData();
        }

        private void WriteLobbyData()
        {
            if (_partyInfo == null || _lobby.m_SteamID == 0) return;
            SteamMatchmaking.SetLobbyData(_lobby, JoinInfo.LobbyServerKey, _partyInfo.ServerAddress);
            SteamMatchmaking.SetLobbyData(_lobby, JoinInfo.LobbyRoomKey, _partyInfo.RoomCode);
        }

        private void JoinLobby(CSteamID lobby)
        {
            if (!Available) return;
            _entered!.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure) return;
            var id = new CSteamID(r.m_ulSteamIDLobby);
            var info = JoinInfo.FromParts(SteamMatchmaking.GetLobbyData(id, JoinInfo.LobbyServerKey), SteamMatchmaking.GetLobbyData(id, JoinInfo.LobbyRoomKey));
            if (info == null) { Problem = "that Steam lobby has no Voice Battle room"; return; }
            _lobby = id;
            _partyInfo = info;
            RaiseJoin(info);
        }
    }
#else
    /// <summary>Steamworks.NET not installed, or a platform without Steam: everything still works, without Steam.</summary>
    public sealed partial class SteamBridge
    {
        public string PersonaName => "";
        public ulong SteamId => 0;
        public bool InParty => false;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            LoadConfig();
            Problem = "Steamworks.NET is not in this build";
        }

        public void RequestTicket(Action<string?> done) => done(null);
        public void SetParty(JoinInfo info, int maxMembers) { }
        public void InviteFriends() { }
        public void LeaveParty() { }
        private void Shutdown() { }
    }
#endif
}
