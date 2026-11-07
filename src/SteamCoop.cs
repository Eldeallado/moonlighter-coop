using System;
using System.Collections.Concurrent;
using Steamworks;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A peer over Steam P2P (relayed by Steam, no ports to open). Frames are [type][payload], reliable.
    public class SteamPeer : IPeer
    {
        public readonly CSteamID Id;
        public ConcurrentQueue<(Msg type, byte[] data)> Inbox { get; } = new ConcurrentQueue<(Msg, byte[])>();
        public bool Closed { get; private set; }
        public string Remote { get; }

        public SteamPeer(CSteamID id)
        {
            Id = id;
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
            if (!SteamNetworking.SendP2PPacket(Id, buf, (uint)buf.Length, mode, 0))
                Plugin.Log.LogWarning($"[steam] send {type} ({buf.Length} bytes) failed");
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            SteamNetworking.CloseP2PSessionWithUser(Id);
        }

        public void MarkClosed() => Closed = true;
    }

    /// Steam lobbies: F2 hosts a friends-only lobby and opens the invite dialog; accepting an invite (or
    /// starting the game from one, +connect_lobby) joins it as the guest.
    public static class SteamCoop
    {
        public static bool Available;
        public static CSteamID Lobby = CSteamID.Nil;
        public static string Status = "";
        private static Callback<LobbyCreated_t> _created;
        private static Callback<GameLobbyJoinRequested_t> _joinRequested;
        private static Callback<LobbyEnter_t> _entered;
        private static Callback<P2PSessionRequest_t> _sessionRequest;
        private static Callback<P2PSessionConnectFail_t> _connectFail;
        private static ulong _pendingLobbyArg;
        private static byte[] _buf = new byte[1 << 20];
        private static bool _inited;

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
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            _entered = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _sessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _connectFail = Callback<P2PSessionConnectFail_t>.Create(r =>
            {
                Plugin.Log.LogWarning($"[steam] P2P with {r.m_steamIDRemote} failed: {r.m_eP2PSessionError}");
                if (Net.Peer is SteamPeer sp && sp.Id == r.m_steamIDRemote) sp.MarkClosed();
            });
            // Started from an invite while the game was closed.
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "+connect_lobby" && ulong.TryParse(a[i + 1], out var id)) _pendingLobbyArg = id;
            Plugin.Log.LogInfo($"[steam] ready as {SteamFriends.GetPersonaName()}");
        }

        public static void Host()
        {
            if (!Available) { Status = "Steam no está disponible"; return; }
            if (Lobby != CSteamID.Nil) { SteamFriends.ActivateGameOverlayInviteDialog(Lobby); return; }
            Plugin.Role = "host";
            Status = "Creando sala de Steam…";
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4);
        }

        private static void OnLobbyCreated(LobbyCreated_t r)
        {
            if (r.m_eResult != EResult.k_EResultOK) { Status = "No se pudo crear la sala: " + r.m_eResult; Plugin.Log.LogWarning("[steam] " + Status); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, "moonlighter_coop", Plugin.Version);
            Status = "Sala creada: invita a un amigo (se ha abierto la ventana de Steam)";
            Plugin.Log.LogInfo($"[steam] lobby {Lobby} created");
            SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        public static void Join(CSteamID lobby)
        {
            if (Session.IsHost && Net.Connected) return;
            Plugin.Role = "join";
            Status = "Uniéndose a la partida de tu amigo…";
            Plugin.Log.LogInfo($"[steam] joining lobby {lobby}");
            SteamMatchmaking.JoinLobby(lobby);
        }

        private static void OnLobbyEnter(LobbyEnter_t r)
        {
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (!Session.IsGuest) return;   // the host enters its own lobby too
            var owner = SteamMatchmaking.GetLobbyOwner(Lobby);
            var theirs = SteamMatchmaking.GetLobbyData(Lobby, "moonlighter_coop");
            if (!string.IsNullOrEmpty(theirs) && theirs != Plugin.Version)
                Plugin.Log.LogWarning($"[steam] host has mod {theirs}, I have {Plugin.Version}");
            Net.Peer = new SteamPeer(owner);
            Status = "Conectado con " + SteamFriends.GetFriendPersonaName(owner);
            Plugin.Log.LogInfo($"[steam] in lobby, host {owner}");
            // The guest plays on the host's save: be at the main menu to receive it.
            if (HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
            Net.Send(Msg.Hello, System.Text.Encoding.UTF8.GetBytes(Plugin.Version));
        }

        private static void OnSessionRequest(P2PSessionRequest_t r)
        {
            // Only the host accepts, and only one partner for now.
            if (!Session.IsHost || (Net.Connected && !(Net.Peer is SteamPeer sp && sp.Id == r.m_steamIDRemote))) return;
            SteamNetworking.AcceptP2PSessionWithUser(r.m_steamIDRemote);
            if (!(Net.Peer is SteamPeer p2 && p2.Id == r.m_steamIDRemote && !p2.Closed))
            {
                Net.Peer = new SteamPeer(r.m_steamIDRemote);
                Status = "Conectado con " + SteamFriends.GetFriendPersonaName(r.m_steamIDRemote);
                Plugin.Log.LogInfo($"[steam] guest {r.m_steamIDRemote} connected");
            }
        }

        /// Every frame: deliver P2P packets to the peer's inbox; join a lobby from the command line once ready.
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
                if (Session.IsHost && !(Net.Peer is SteamPeer))
                {
                    // First packet from a guest that joined our lobby.
                    SteamNetworking.AcceptP2PSessionWithUser(from);
                    Net.Peer = new SteamPeer(from);
                }
                if (Net.Peer is SteamPeer sp && sp.Id == from)
                {
                    var data = new byte[got - 1];
                    Buffer.BlockCopy(_buf, 1, data, 0, (int)got - 1);
                    sp.Inbox.Enqueue(((Msg)_buf[0], data));
                }
            }
        }
    }
}
