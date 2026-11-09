using System;
using System.Collections.Concurrent;
using Steamworks;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A peer over Steam P2P (relayed by Steam, no ports to open). Frames are [type][payload], reliable.
    public class SteamPeer : IPeer
    {
        public readonly CSteamID SteamId;
        public int Id { get; set; }
        public ConcurrentQueue<(Msg type, byte[] data)> Inbox { get; } = new ConcurrentQueue<(Msg, byte[])>();
        public bool Closed { get; private set; }
        public string Remote { get; }

        public SteamPeer(CSteamID id)
        {
            SteamId = id;
            Remote = "steam:" + SteamFriends.GetFriendPersonaName(id);
        }

        public void Send(Msg type, byte[] data)
        {
            if (Closed) return;
            var buf = new byte[data.Length + 1];
            buf[0] = (byte)type;
            Buffer.BlockCopy(data, 0, buf, 1, data.Length);
            // Poses and enemy states can go unreliable (newest wins); everything else must arrive.
            var mode = type == Msg.Pose || type == Msg.Enemies ? EP2PSend.k_EP2PSendUnreliableNoDelay : EP2PSend.k_EP2PSendReliable;
            if (!SteamNetworking.SendP2PPacket(SteamId, buf, (uint)buf.Length, mode, 0))
                Plugin.Log.LogWarning($"[steam] send {type} ({buf.Length} bytes) failed");
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            SteamNetworking.CloseP2PSessionWithUser(SteamId);
        }

        public void MarkClosed() => Closed = true;
    }

    /// Steam lobbies, opened from the F2 menu. Creating one gives a short code (stored in the lobby) that a
    /// friend types to join; friends can also be invited through Steam. Up to 4 guests.
    public static class SteamCoop
    {
        public static bool Available;
        public static CSteamID Lobby = CSteamID.Nil;
        public static string Status = "";
        public static string Code = "";
        private static Callback<LobbyCreated_t> _created;
        private static Callback<GameLobbyJoinRequested_t> _joinRequested;
        private static Callback<LobbyEnter_t> _entered;
        private static Callback<P2PSessionRequest_t> _sessionRequest;
        private static Callback<P2PSessionConnectFail_t> _connectFail;
        private static Callback<LobbyChatUpdate_t> _members;
        private static ulong _pendingLobbyArg;
        private static byte[] _buf = new byte[1 << 20];
        private static bool _inited;
        public static string CodeAlphabet => CodeChars;
        private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static void TryInit()
        {
            if (_inited) return;
            try
            {
                if (!SteamAPI.IsSteamRunning()) return;
                SteamUser.GetSteamID();   // throws if the API is not up yet
            }
            catch { return; }
            _inited = true;
            Available = true;
            _created = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => { Backup.Now("al aceptar una invitación"); Join(r.m_steamIDLobby); });
            _entered = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _sessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            // Someone left the lobby (closed the game, lost connection): drop them.
            _members = Callback<LobbyChatUpdate_t>.Create(u =>
            {
                if ((u.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0) return;
                PeerOf(new CSteamID(u.m_ulSteamIDUserChanged))?.Close();
            });
            _connectFail = Callback<P2PSessionConnectFail_t>.Create(r =>
            {
                Plugin.Log.LogWarning($"[steam] P2P with {r.m_steamIDRemote} failed: {r.m_eP2PSessionError}");
                PeerOf(r.m_steamIDRemote)?.MarkClosed();
            });
            // Started from an invite while the game was closed.
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "+connect_lobby" && ulong.TryParse(a[i + 1], out var id)) _pendingLobbyArg = id;
            Plugin.Log.LogInfo($"[steam] ready as {SteamFriends.GetPersonaName()}");
        }

        /// Players the host kicked out this session.
        public static readonly System.Collections.Generic.HashSet<string> Banned = new System.Collections.Generic.HashSet<string>();
        public static string BanKey(IPeer p) => p is SteamPeer sp ? "steam:" + sp.SteamId.m_SteamID : "ip:" + (p.Remote ?? "").Split(':')[0];

        public static string MyName => Available ? SteamFriends.GetPersonaName() : "";

        private static SteamPeer PeerOf(CSteamID id)
        {
            foreach (var p in Net.Live) if (p is SteamPeer sp && sp.SteamId == id) return sp;
            return null;
        }

        public static void Host()
        {
            if (!Available) { Status = "Steam no está disponible"; return; }
            if (Lobby != CSteamID.Nil) return;
            Plugin.Role = "host";
            Code = "";
            Status = "Creando la partida…";
            // Invisible: it never shows up in any lobby list. The code IS the lobby's id, so only whoever has it
            // can find it (Steam limits how fast anyone can try ids).
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeInvisible, 5);
        }

        public static void Invite() { if (Available && Lobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(Lobby); }

        private static void OnLobbyCreated(LobbyCreated_t r)
        {
            if (r.m_eResult != EResult.k_EResultOK) { Status = "No se pudo crear la partida: " + r.m_eResult; Plugin.Log.LogWarning("[steam] " + Status); Plugin.Role = ""; return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, "moonlighter_coop", Plugin.Version);
            Code = Encode(Lobby.GetAccountID().m_AccountID);
            if (Lobby != FromCode(Code)) Plugin.Log.LogWarning($"[steam] lobby id {Lobby.m_SteamID:X} does not round-trip through its code (instance {Lobby.GetUnAccountInstance():X})");
            Status = "Partida creada. Código: " + Code;
            Plugin.Log.LogInfo($"[steam] lobby {Lobby} created, code {Code}");
        }

        // 7 letters from a 32-letter alphabet without look-alikes (no I, O, 0, 1) = the lobby's 32-bit account id.
        public static string Encode(uint acc)
        {
            var c = new char[7];
            for (int i = 6; i >= 0; i--) { c[i] = CodeChars[(int)(acc % 32)]; acc /= 32; }
            return new string(c);
        }

        public static bool TryDecode(string code, out uint acc)
        {
            acc = 0; ulong v = 0;
            if (code.Length != 7) return false;
            foreach (char ch in code) { int i = CodeChars.IndexOf(ch); if (i < 0) return false; v = v * 32 + (ulong)i; }
            if (v > uint.MaxValue) return false;
            acc = (uint)v; return true;
        }

        private static CSteamID FromCode(string code) =>
            TryDecode(code, out var acc)
                ? new CSteamID(new AccountID_t(acc), (uint)(EChatSteamIDInstanceFlags.k_EChatInstanceFlagLobby | EChatSteamIDInstanceFlags.k_EChatInstanceFlagMMSLobby), EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeChat)
                : CSteamID.Nil;

        public static void JoinByCode(string code)
        {
            if (!Available) { Status = "Steam no está disponible"; return; }
            code = (code ?? "").Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            var id = FromCode(code);
            if (id == CSteamID.Nil) { Status = "Ese código no es válido (son 7 letras y números)"; return; }
            Status = "Buscando la partida " + code + "…";
            Join(id);
        }
        public static void Join(CSteamID lobby)
        {
            if (Session.IsHost && Net.Connected) return;
            Plugin.Role = "join";
            Status = "Uniéndose a la partida…";
            Plugin.Log.LogInfo($"[steam] joining lobby {lobby}");
            SteamMatchmaking.JoinLobby(lobby);
        }

        public static void Leave()
        {
            if (Lobby != CSteamID.Nil && Available) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil; Code = ""; Status = "";
        }

        private static void OnLobbyEnter(LobbyEnter_t r)
        {
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (!Session.IsGuest) return;   // the host enters its own lobby too
            if (r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "No hay ninguna partida con ese código (o está llena)"; Plugin.Role = ""; Lobby = CSteamID.Nil; return;
            }
            var owner = SteamMatchmaking.GetLobbyOwner(Lobby);
            var theirs = SteamMatchmaking.GetLobbyData(Lobby, "moonlighter_coop");
            if (string.IsNullOrEmpty(theirs))
            {
                Status = "Ese código no es de una partida de Moonlighter Coop"; SteamMatchmaking.LeaveLobby(Lobby); Lobby = CSteamID.Nil; Plugin.Role = ""; return;
            }
            Code = Encode(Lobby.GetAccountID().m_AccountID);
            if (!string.IsNullOrEmpty(theirs) && theirs != Plugin.Version)
                Plugin.Log.LogWarning($"[steam] host has mod {theirs}, I have {Plugin.Version}");
            Net.Add(new SteamPeer(owner));
            Status = "Conectado con " + SteamFriends.GetFriendPersonaName(owner);
            Plugin.Log.LogInfo($"[steam] in lobby, host {owner}");
            // The guest plays on the host's save: be at the main menu to receive it.
            if (HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
            Net.Send(Msg.Hello, System.Text.Encoding.UTF8.GetBytes(Versions.Hello));
        }

        private static void AcceptGuest(CSteamID id)
        {
            if (!Session.IsHost || PeerOf(id) != null) return;
            if (Banned.Contains("steam:" + id.m_SteamID)) { Plugin.Log.LogInfo("[steam] kicked player tried to come back: refused"); return; }
            if (Net.ConnectedCount >= 4) { Plugin.Log.LogWarning("[steam] already 5 players"); return; }
            SteamNetworking.AcceptP2PSessionWithUser(id);
            var p = new SteamPeer(id);
            Net.Add(p);
            Status = "Se ha unido " + SteamFriends.GetFriendPersonaName(id);
            Plugin.Log.LogInfo($"[steam] guest {p.Id} {id} connected");
        }

        private static void OnSessionRequest(P2PSessionRequest_t r) => AcceptGuest(r.m_steamIDRemote);

        public static string PeerName(IPeer p) => p is SteamPeer sp && Available ? SteamFriends.GetFriendPersonaName(sp.SteamId) : p.Remote;

        /// Every frame: deliver P2P packets to each peer's inbox; join a lobby from the command line once ready.
        public static void Tick()
        {
            if (!_inited) { TryInit(); return; }
            if (_pendingLobbyArg != 0 && UnityEngine.Object.FindObjectOfType<MainMenuPanel>() != null)
            {
                var id = new CSteamID(_pendingLobbyArg); _pendingLobbyArg = 0; Join(id);
            }
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, 0))
            {
                if (size > _buf.Length) _buf = new byte[size];
                if (!SteamNetworking.ReadP2PPacket(_buf, size, out uint got, out CSteamID from, 0) || got < 1) continue;
                var sp = PeerOf(from);
                if (sp == null && Session.IsHost) { AcceptGuest(from); sp = PeerOf(from); }   // first packet from a new guest
                if (sp == null) continue;
                var data = new byte[got - 1];
                Buffer.BlockCopy(_buf, 1, data, 0, (int)got - 1);
                sp.Inbox.Enqueue(((Msg)_buf[0], data));
            }
        }
    }
}