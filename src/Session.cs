using System;
using System.IO;
using System.Text;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Who is connected, the save hand-off, and the 20 Hz pose exchange.
    public static class Session
    {
        public static bool IsHost => Plugin.Role == "host";
        public static bool IsGuest => Plugin.Role == "join";
        public static bool GuestLoaded;
        public static bool SaveSent;
        private static bool _helloReceived;
        private static float _nextPose;
        private static float _nextStatus;
        public static int PosesReceived;
        public static float LastPing = -1f;
        private static float _nextPing;

        public static void Start()
        {
            if (IsHost) Net.StartHost(Plugin.Port);
        }

        public static string LocalZone()
        {
            var gm = GameManager.Instance;
            if (gm == null || HeroMerchant.Instance == null) return "";
            if (gm.IsDungeonSceneLoaded()) return "dungeon";
            if (gm.IsTownSceneLoaded()) return "town";
            return "";
        }

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (IsGuest) Net.TickJoin(Plugin.JoinAddress, Plugin.Port, now);
            var peer = Net.Peer;
            if (peer != null)
            {
                while (peer.Inbox.TryDequeue(out var m)) Handle(m.type, m.data);
                if (!peer.Closed)
                {
                    if (!_helloSent) { _helloSent = true; Net.Send(Msg.Hello, Encoding.UTF8.GetBytes(Plugin.Version)); }
                    if (IsHost && _helloReceived && !SaveSent && HeroMerchant.Instance != null && GameManager.Instance.currentGameSlot != null)
                        SendSave();
                    if (now >= _nextPose)
                    {
                        _nextPose = now + 0.05f;
                        Net.Send(Msg.Pose, PoseCapture.Capture(LocalZone()));
                    }
                    if (now >= _nextPing)
                    {
                        _nextPing = now + 2f;
                        Net.Send(Msg.Ping, BitConverter.GetBytes(now));
                    }
                }
            }
            // The guest keeps the host's save until its own main menu is up (it can arrive before).
            if (_pendingSave != null && !GuestLoaded && GameManager.Instance != null && HeroMerchant.Instance == null)
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainMenuPanel>();
                if (menu != null)
                {
                    if (_menuSeenAt < 0f) _menuSeenAt = now;
                    if (now - _menuSeenAt > 6f) { var d = _pendingSave; _pendingSave = null; LoadHostSave(d); }
                }
            }
            if (now >= _nextStatus) { _nextStatus = now + 0.5f; WriteStatus(); }
        }

        private static byte[] _pendingSave;
        private static float _menuSeenAt = -1f;

        private static bool _helloSent;

        private static void Handle(Msg type, byte[] data)
        {
            switch (type)
            {
                case Msg.Hello:
                    var v = Encoding.UTF8.GetString(data);
                    _helloReceived = true;
                    Plugin.Log.LogInfo($"[session] hello from peer, version {v}");
                    if (v != Plugin.Version) Plugin.Log.LogWarning($"[session] version mismatch: mine {Plugin.Version}, theirs {v}");
                    break;
                case Msg.Save:
                    if (IsGuest) _pendingSave = data;
                    break;
                case Msg.Pose:
                    PosesReceived++;
                    Puppet.Ensure().Apply(data, LocalZone());
                    break;
                case Msg.Ping:
                    Net.Send(Msg.Pong, data);
                    break;
                case Msg.Pong:
                    LastPing = (Time.unscaledTime - BitConverter.ToSingle(data, 0)) * 1000f;
                    break;
            }
        }

        private static void SendSave()
        {
            SaveSent = true;
            var gm = GameManager.Instance;
            FsJSONManager.Serialize(gm.gameSaveData, out fsData data);
            var json = Encoding.UTF8.GetBytes(fsJsonPrinter.CompressedJson(data));
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(gm.currentGameSlot.seed);
            w.Write(json.Length);
            w.Write(json);
            Net.Send(Msg.Save, ms.ToArray());
            Plugin.Log.LogInfo($"[session] sent save to guest: slot {gm.currentGameSlot.seed}, {json.Length} bytes");
        }

        private static void LoadHostSave(byte[] payload)
        {
            if (GuestLoaded) return;
            var r = new BinaryReader(new MemoryStream(payload));
            int seed = r.ReadInt32();
            var json = r.ReadBytes(r.ReadInt32());
            var gm = GameManager.Instance;
            if (gm == null || UnityEngine.Object.FindObjectOfType<MainMenuPanel>() == null)
            {
                Plugin.Log.LogWarning("[session] got host save but not in main menu; ignoring");
                return;
            }
            GuestLoaded = true;
            gm.LoadGameFromBytes(json);
            GameSlot slot = null;
            foreach (var s in gm.gameSaveData.gameSlots)
                if (s != null && s.seed == seed) slot = s;
            if (slot == null) slot = gm.currentGameSlot;
            Plugin.Log.LogInfo($"[session] loading host save, slot {seed} found={slot != null && slot.seed == seed}");
            Plugin.LoadSlotFromMenu(slot);
        }

        private static void WriteStatus()
        {
            var hero = HeroMerchant.Instance;
            var p = Puppet.Instance;
            var sb = new StringBuilder();
            sb.AppendLine($"role={Plugin.Role}");
            sb.AppendLine($"connected={Net.Connected}");
            sb.AppendLine($"zone={LocalZone()}");
            sb.AppendLine($"hero={(hero ? $"{hero.transform.position.x:F1},{hero.transform.position.y:F1}" : "none")}");
            sb.AppendLine($"poses_received={PosesReceived}");
            sb.AppendLine($"puppet_zone={(p ? p.Zone : "")}");
            sb.AppendLine($"puppet_visible={(p ? p.Visible : false)}");
            sb.AppendLine($"puppet={(p ? $"{p.transform.position.x:F1},{p.transform.position.y:F1}" : "none")}");
            sb.AppendLine($"puppet_parts={(p ? p.GetComponentsInChildren<SpriteRenderer>(false).Length : 0)}");
            sb.AppendLine($"sprite_misses={SpriteCache.Misses}");
            sb.AppendLine($"ping_ms={LastPing:F0}");
            sb.AppendLine($"guest_loaded={GuestLoaded}");
            sb.AppendLine($"save_sent={SaveSent}");
            sb.AppendLine($"slot={(GameManager.Instance != null && GameManager.Instance.currentGameSlot != null ? GameManager.Instance.currentGameSlot.seed : 0)}");
            try { File.WriteAllText(Plugin.StatusFile("status.txt"), sb.ToString()); } catch { }
        }
    }

    /// The guest plays on the host's save: never write it to the guest's Steam Cloud.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SerializeGameSlot))]
    static class NoGuestSave
    {
        static bool Prefix()
        {
            if (!Session.IsGuest) return true;
            Plugin.Log.LogInfo("[session] guest save blocked");
            return false;
        }
    }
}
