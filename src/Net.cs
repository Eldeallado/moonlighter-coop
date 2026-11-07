using System;
using System.Collections.Concurrent;
using System.IO;
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
    }

    /// One framed TCP connection: [int32 length][byte type][payload]. Reads on a thread, handled on Unity's thread.
    public class Peer
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _sendLock = new object();
        public readonly ConcurrentQueue<(Msg type, byte[] data)> Inbox = new ConcurrentQueue<(Msg, byte[])>();
        public volatile bool Closed;
        public string Remote;

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
                    if (len < 1 || len > 64 * 1024 * 1024) throw new IOException("bad frame length " + len);
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
        public static Peer Peer;
        private static TcpListener _listener;
        private static float _nextConnectTry;
        private static bool _connecting;

        public static bool Connected => Peer != null && !Peer.Closed;

        public static void StartHost(int port)
        {
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
                if (Connected) { c.Close(); }
                else
                {
                    Peer = new Peer(c);
                    Plugin.Log.LogInfo("[net] guest connected from " + Peer.Remote);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[net] accept: " + e.Message); }
            try { _listener.BeginAcceptTcpClient(OnAccept, null); } catch { }
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
                    Peer = new Peer(c);
                    Plugin.Log.LogInfo($"[net] connected to host {address}:{port}");
                }
                catch { c.Close(); }
                _connecting = false;
            }, null);
        }

        public static void Send(Msg type, byte[] data) => Peer?.Send(type, data);
    }
}




