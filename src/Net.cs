using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MoonlighterCoop
{
    public enum Msg : byte
    {
        Hello = 1,
        Save = 2,
        Pose = 3,
        Ping = 4,
        Pong = 5,
        Travel = 6,
        DungeonInfo = 7,
        DoorRequest = 8,
        Enemies = 9,
        EnemyHit = 10,
        EnemyDeath = 11,
        BreakAt = 12,
        ItemDrop = 13,
        ItemTaken = 14,
        Downed = 15,
        Revive = 16,
        EndRun = 17,
        FountainHeal = 18,
        BreakDetailAt = 19,
        RoomDoors = 20,
        TownArea = 21,
        Vote = 22,
        VoteResult = 23,
        Gold = 24,
        Bag = 25,
        Welcome = 26,
        PlayerCount = 27,
        ShopState = 28,
        Shelves = 29,
        Customers = 30,
        ShopSell = 31,
        DayTime = 32,
        CatchThief = 33,
        ShopResults = 34,
        Bye = 35,
        PlayerLeft = 36,
        Projectiles = 37,
        GoldSet = 38,
        DeadEnemies = 39, Tutorial = 40, LookData = 41, Mods = 42, Stats = 43, Call = 44, CallReply = 45, PendantRequest = 46, Chat = 47, Settings = 48, Role = 49, RoleHeal = 50, ShopOwner = 51, Spectate = 52, Give = 53, Lag = 54, AskItem = 55, AskReply = 56,
    }

    public interface IPeer
    {
        ConcurrentQueue<(Msg type, byte[] data)> Inbox { get; }
        bool Closed { get; }
        string Remote { get; }
        int Id { get; set; }
        void Send(Msg type, byte[] data);
        void Close();
    }

    /// One framed TCP connection: [int32 length][byte type][payload]. Reads on a thread, handled on Unity's thread.
    public class Peer : IPeer
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _sendLock = new object();
        public ConcurrentQueue<(Msg type, byte[] data)> Inbox { get; } = new ConcurrentQueue<(Msg, byte[])>();
        private volatile bool _closed;
        public bool Closed { get => _closed; private set => _closed = value; }
        public string Remote { get; }
        public int Id { get; set; }

        public Peer(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            Remote = client.Client.RemoteEndPoint?.ToString();
            new Thread(ReadLoop) { IsBackground = true, Name = "coop-read" }.Start();
        }

        private void ReadLoop()
        {
            try
            {
                var r = new BinaryReader(_stream);
                while (!Closed)
                {
                    int len = r.ReadInt32();
                    if (len < 1 || len > 8 * 1024 * 1024) throw new IOException("bad frame length " + len);
                    var type = (Msg)r.ReadByte();
                    var data = r.ReadBytes(len - 1);
                    if (data.Length != len - 1) throw new EndOfStreamException();
                    Inbox.Enqueue((type, data));
                }
            }
            catch (Exception e)
            {
                if (!Closed) Plugin.Log.LogWarning("[net] connection lost: " + e.Message);
            }
            Close();
        }

        public void Send(Msg type, byte[] data)
        {
            if (Closed) return;
            try
            {
                lock (_sendLock)
                {
                    var head = BitConverter.GetBytes(data.Length + 1);
                    _stream.Write(head, 0, 4);
                    _stream.WriteByte((byte)type);
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[net] send failed: " + e.Message);
                Close();
            }
        }

        public void Close()
        {
            Closed = true;
            try { _client.Close(); } catch { }
        }
    }

    public static class Net
    {
        /// Host: every connected guest. Guest: just the host. Each guest gets a player number (1, 2, ...).
        public static readonly List<IPeer> Peers = new List<IPeer>();
        private static readonly object _lock = new object();
        private static int _nextId = 1;
        private static TcpListener _listener;
        private static float _nextConnectTry;
        private static bool _connecting;

        /// Connected AND playing the shared game (not each in its own tutorial): what gameplay sync checks.
        public static bool Juego => Connected && !Session.SoloTutorial;

        public static bool Connected { get { lock (_lock) return Peers.Any(p => !p.Closed); } }
        public static int ConnectedCount { get { lock (_lock) return Peers.Count(p => !p.Closed); } }
        public static List<IPeer> Live { get { lock (_lock) return Peers.Where(p => !p.Closed).ToList(); } }

        /// The first live peer (the host, on a guest). Setting it adds a peer.
        public static IPeer Peer
        {
            get { lock (_lock) return Peers.FirstOrDefault(p => !p.Closed); }
            set { if (value != null) Add(value); }
        }

        public static void Add(IPeer p)
        {
            lock (_lock)
            {
                Peers.RemoveAll(x => x.Closed);
                if (Peers.Contains(p)) return;
                // Lowest free number, so someone who leaves and comes back is not player 7.
                if (Session.IsHost) { int id = 1; while (Peers.Any(x => !x.Closed && x.Id == id)) id++; p.Id = id; }
                Peers.Add(p);
            }
        }

        public static bool Listening => _listener != null;
        public static void StopHost() { try { _listener?.Stop(); } catch { } _listener = null; Plugin.Log.LogInfo("[net] stopped listening on TCP"); }

        public static void StartHost(int port)
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Plugin.Log.LogInfo($"[net] hosting on TCP {port}");
            _listener.BeginAcceptTcpClient(OnAccept, null);
        }

        private static void OnAccept(IAsyncResult ar)
        {
            try
            {
                var c = _listener.EndAcceptTcpClient(ar);
                var peer = new Peer(c);
                if (SteamCoop.Banned.Contains(SteamCoop.BanKey(peer))) { peer.Close(); Plugin.Log.LogInfo("[net] kicked player tried to come back: refused"); }
                else Add(peer);
                Plugin.Log.LogInfo($"[net] guest {peer.Id} connected from {peer.Remote}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[net] accept: " + e.Message); }
            try { _listener?.BeginAcceptTcpClient(OnAccept, null); } catch { }
        }

        /// Called every frame on the guest until connected.
        public static void TickJoin(string address, int port, float now)
        {
            if (Connected || _connecting || now < _nextConnectTry) return;
            _connecting = true;
            _nextConnectTry = now + 2f;
            var c = new TcpClient();
            c.BeginConnect(address, port, ar =>
            {
                try
                {
                    c.EndConnect(ar);
                    Add(new Peer(c));
                    Plugin.Log.LogInfo($"[net] connected to host {address}:{port}");
                }
                catch { c.Close(); }
                _connecting = false;
            }, null);
        }

        /// Leave the game: drop every connection (the TCP listener stays for a new game).
        public static void CloseAll() { lock (_lock) { foreach (var p in Peers) p.Close(); Peers.Clear(); _nextId = 1; } }

        /// Host: to every guest. Guest: to the host.
        public static void Send(Msg type, byte[] data) { foreach (var p in Live) p.Send(type, data); }

        /// Host: pass a guest's message on to the other guests.
        public static void SendExcept(IPeer except, Msg type, byte[] data) { foreach (var p in Live) if (p != except) p.Send(type, data); }
    }
}






