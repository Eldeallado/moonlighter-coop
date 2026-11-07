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
            Dungeon.Init();
        }

        public static string LocalZone()
        {
            var gm = GameManager.Instance;
            if (gm == null || HeroMerchant.Instance == null) return "";
            if (gm.IsDungeonSceneLoaded()) return Dungeon.RoomZone();
            if (gm.IsTownSceneLoaded()) return "town:" + TownSync.Area();
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
            LoadingWatchdog(now);
            EnemySync.HostTick();
            DoorSync.HostTick();
            Downed.Tick();
            if (Net.Connected && IsGuest && Puppet.Instance != null) Dungeon.GuestFollowTick(Puppet.Instance.Zone);
            if (now >= _nextStatus) { _nextStatus = now + 0.5f; WriteStatus(); }
        }

        // Loading the save from the menu shows a "dummy" loading panel; on the guest nothing hid it again.
        private static float _loadingStuckSince = -1f;
        private static void LoadingWatchdog(float now)
        {
            var gui = GUIManager.Instance;
            var gm = GameManager.Instance;
            var panel = gui != null ? gui.dungeonLoadingPanel : null;
            // Only the "dummy" panel from loading a save: the real dungeon loading panel must never be touched.
            bool stuck = panel != null && panel.IsActive() && gm != null && HeroMerchant.Instance != null
                         && gm.IsTownSceneLoaded() && !gm.IsDungeonSceneLoaded() && gm.NotLoadingScene
                         && Traverse.Create(panel).Field("_dummy").GetValue<bool>();
            if (!stuck) { _loadingStuckSince = -1f; return; }
            if (_loadingStuckSince < 0f) { _loadingStuckSince = now; return; }
            if (now - _loadingStuckSince < 4f) return;
            _loadingStuckSince = -1f;
            Plugin.Log.LogInfo("[session] loading panel still up in town: hiding it");
            if (gui.currentInventory == panel) gui.DisableCurrentPanel();
            else panel.Disable();
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
                case Msg.Travel:
                    if (IsGuest) Dungeon.OnTravel(data);
                    break;
                case Msg.DungeonInfo:
                    if (IsGuest) Dungeon.OnInfo(data);
                    break;
                case Msg.Enemies:
                    if (IsGuest) EnemySync.OnStates(data);
                    break;
                case Msg.EnemyHit:
                    if (IsHost) EnemySync.OnHit(data);
                    break;
                case Msg.EnemyDeath:
                    if (IsGuest) EnemySync.OnDeath(data);
                    break;
                case Msg.ItemDrop: if (IsGuest) Loot.OnItemDrop(data); break;
                case Msg.ItemTaken: Loot.OnItemTaken(data); break;
                case Msg.Downed: Downed.OnPartnerDowned(data); break;
                case Msg.Revive: Downed.OnRevive(data); break;
                case Msg.EndRun: Downed.OnEndRun(); break;
                case Msg.FountainHeal: Downed.OnFountainHeal(); break;
                case Msg.BreakDetailAt: BreakableSync.OnBreakDetailAt(data); break;
                case Msg.TownArea: TownSync.OnTownArea(data); break;
                case Msg.RoomDoors: if (IsGuest) DoorSync.OnRoomDoors(data); break;
                case Msg.BreakAt:
                    BreakableSync.OnBreakAt(data);
                    break;
                case Msg.DoorRequest:
                    if (IsHost) Dungeon.OnDoorRequest(data);
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

        private static int RoomEnemiesAlive()
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return -1;
            int n = 0; foreach (var e in dm.currentDungeonRoom.roomEnemies) if (e != null && e.enemyStats != null && e.enemyStats.isAlive) n++;
            return n;
        }

        private static float _hpMin = 99999f;

        private static void WriteStatus()
        {
            var hero = HeroMerchant.Instance;
            var p = Puppet.Instance;
            if (hero != null && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded()) _hpMin = Mathf.Min(_hpMin, (float)hero.heroMerchantStats.currentHealth.Value);
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
            if (p != null)
                foreach (var sr in p.GetComponentsInChildren<SpriteRenderer>(false))
                    sb.AppendLine($"puppet_part={sr.name} sprite={(sr.sprite ? sr.sprite.name : "NULL")} layer={sr.sortingLayerName}/{sr.sortingOrder} mat={(sr.sharedMaterial ? sr.sharedMaterial.name : "NULL")} col={sr.color} z={sr.transform.position.z} vis={sr.isVisible}");
            if (hero != null)
            {
                var will = hero.transform.Find("HeroMerchantArt/Will")?.GetComponent<SpriteRenderer>();
                if (will != null) sb.AppendLine($"hero_will sprite={(will.sprite ? will.sprite.name : "NULL")} layer={will.sortingLayerName}/{will.sortingOrder} mat={(will.sharedMaterial ? will.sharedMaterial.name : "NULL")} z={will.transform.position.z}");
            }
            sb.AppendLine($"ping_ms={LastPing:F0}");
            sb.AppendLine($"dungeon_sig={(GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded() ? Dungeon.Signature() : "")}");
            sb.AppendLine($"content_sig={(GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded() && GameManager.Instance.NotLoadingScene ? Dungeon.ContentSignature() : "")}");
            sb.AppendLine($"floor_seed={Dungeon.FloorSeed}");
            sb.AppendLine($"breaks_sent={BreakableSync.Sent}");
            sb.AppendLine($"loot_sent={Loot.DropsSent}");
            sb.AppendLine($"loot_shown={Loot.DropsShown}");
            sb.AppendLine($"loot_taken={Loot.Taken}");
            sb.AppendLine($"loot_removed={Loot.Removed}");
            sb.AppendLine($"loot_received={Loot.Received}");
            sb.AppendLine($"downed_me={Downed.Me}");
            sb.AppendLine($"downed_partner={Downed.Partner}");
            sb.AppendLine($"revives={Downed.Revives}");
            sb.AppendLine($"target_swaps={TargetSwap.Swaps}");
            sb.AppendLine($"hp={(hero != null ? (float)hero.heroMerchantStats.currentHealth.Value : -1f):F0}");
            sb.AppendLine($"hp_min={_hpMin:F0}");
            sb.AppendLine($"breaks_applied={BreakableSync.Applied}");
            sb.AppendLine($"infos_sent={Dungeon.InfosSent}");
            sb.AppendLine($"infos_received={Dungeon.InfosReceived}");
            sb.AppendLine($"loading_panel={(GUIManager.Instance != null && GUIManager.Instance.dungeonLoadingPanel != null && GUIManager.Instance.dungeonLoadingPanel.IsActive())}");
            sb.AppendLine($"scenario_step={Plugin.ScenarioStep}");
            sb.AppendLine($"enemy_hits_sent={EnemySync.HitsSent}");
            sb.AppendLine($"enemy_hits_applied={EnemySync.HitsApplied}");
            sb.AppendLine($"enemy_deaths_sent={EnemySync.DeathsSent}");
            sb.AppendLine($"enemy_deaths_applied={EnemySync.DeathsApplied}");
            sb.AppendLine($"enemy_states_applied={EnemySync.StatesApplied}");
            sb.AppendLine($"room_enemies_alive={RoomEnemiesAlive()}");
            sb.AppendLine($"combat_seen={Plugin.CombatRoomSeen}");
            sb.AppendLine($"combat_cleared={Plugin.CombatRoomCleared}");
            sb.AppendLine($"door_requests_sent={Dungeon.DoorRequestsSent}");
            sb.AppendLine($"door_requests_done={Dungeon.DoorRequestsDone}");
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








