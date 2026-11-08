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
            Shared.Init();
        }

        /// Leaving a game: forget everyone, so a new game (or a new host) starts clean.
        public static void Reset()
        {
            GuestLoaded = false; SaveSent = false; Welcomed = false; MyId = 0; _knownPlayers = 0;
            _pendingSave = null; _menuSeenAt = -1f; LastPing = -1f;
            _helloSentTo.Clear(); _helloFrom.Clear(); _saveSentTo.Clear(); _liveIds.Clear();
            foreach (var p in Puppet.All.Values) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            Puppet.All.Clear();
            Looks.Of.Clear();
            Downed.Ghosts.Clear();
            ShopSync.ClearCopies();
        }

        /// Someone left: their Will disappears for everyone, and they no longer count as a ghost or a vote.
        public static void RemovePlayer(int id)
        {
            if (Puppet.All.TryGetValue(id, out var p) && p != null) UnityEngine.Object.Destroy(p.gameObject);
            Puppet.All.Remove(id);
            Looks.Of.Remove(id);
            Downed.Ghosts.Remove(id);
            Plugin.Log.LogInfo($"[session] player {id} is gone");
        }

        // A guest the host never welcomes (kicked before, full, or not answering) gives up after 20 s.
        private static float _connectedAt = -1f;
        private static void GuestWaitWelcome(float now)
        {
            if (!IsGuest || !Net.Connected || Welcomed) { _connectedAt = -1f; return; }
            if (_connectedAt < 0f) { _connectedAt = now; return; }
            if (now - _connectedAt < 20f) return;
            _connectedAt = -1f;
            Plugin.Log.LogWarning("[session] the host never answered: leaving");
            SteamCoop.Status = "El anfitrión no te deja entrar o no responde";
            CoopMenu.HostClosed();
        }

        private static readonly System.Collections.Generic.HashSet<int> _liveIds = new System.Collections.Generic.HashSet<int>();
        private static void HostWatchLeavers()
        {
            if (!IsHost) return;
            var now = new System.Collections.Generic.HashSet<int>();
            foreach (var p in Net.Live) now.Add(p.Id);
            foreach (var id in _liveIds)
                if (!now.Contains(id)) { RemovePlayer(id); Net.Send(Msg.PlayerLeft, new[] { (byte)id }); SteamCoop.Status = "Un jugador ha salido"; }
            _liveIds.Clear(); foreach (var id in now) _liveIds.Add(id);
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
            SteamCoop.Tick();
            if (IsGuest && Plugin.TcpMode) Net.TickJoin(Plugin.JoinAddress, Plugin.Port, now);
            foreach (var peer in Net.Live)
            {
                while (peer.Inbox.TryDequeue(out var m)) Handle(peer, m.type, m.data);
                if (peer.Closed) continue;
                if (_helloSentTo.Add(peer)) peer.Send(Msg.Hello, Encoding.UTF8.GetBytes(Plugin.Version));
                // Host: each new guest gets its player number and the host's save.
                if (IsHost && _helloFrom.Contains(peer) && !_saveSentTo.Contains(peer) && HeroMerchant.Instance != null && GameManager.Instance.currentGameSlot != null)
                {
                    _saveSentTo.Add(peer);
                    peer.Send(Msg.Welcome, new[] { (byte)peer.Id });
                    SendSave(peer);
                }
            }
            if (IsHost && Net.Connected && now >= _nextCount)
            {
                _nextCount = now + 1f;
                Net.Send(Msg.PlayerCount, new[] { (byte)Players });
            }
            if (Net.Connected)
            {
                if (now >= _nextPose && (IsHost || Welcomed))
                {
                    _nextPose = now + 0.05f;
                    Net.Send(Msg.Pose, PoseCapture.Capture(LocalZone(), MyId));
                }
                if (now >= _nextPing)
                {
                    _nextPing = now + 2f;
                    Net.Send(Msg.Ping, BitConverter.GetBytes(now));
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
            HostWatchLeavers();
            GuestWaitWelcome(now);
            LoadingWatchdog(now);
            Shared.Tick();
            ShopSync.Tick();
            Regroup();
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

        // After arriving somewhere new in town, the guest appears next to the host (the game places each Will
        // by where he came from, so they could end up far apart).
        private static string _regroupedIn = "";
        private static float _zoneSince;
        private static string _lastZone = "";
        public static int Regroups;
        private static void Regroup()
        {
            if (!IsGuest || !Net.Connected) return;
            var hero = HeroMerchant.Instance; var p = Puppet.Instance; var gm = GameManager.Instance;
            string zone = LocalZone();
            if (zone != _lastZone) { _lastZone = zone; _zoneSince = Time.unscaledTime; _regroupedIn = ""; }
            if (hero == null || p == null || gm == null || !zone.StartsWith("town") || !p.Visible || !gm.NotLoadingScene) return;
            if (_regroupedIn == zone || Time.unscaledTime - _zoneSince < 1.5f) return;
            _regroupedIn = zone;
            if (((Vector2)(p.transform.position - hero.transform.position)).magnitude < 120f) return;
            hero.heroMerchantController.TeleportTo(p.transform.position + new Vector3(20f, 0f));
            CameraManager.Instance.TeleportCameraToPoint(hero.gameObject);
            Regroups++;
            Plugin.Log.LogInfo($"[session] regrouped next to the host in {zone}");
        }
        public static void ZoneLeft() { _regroupedIn = ""; }

        private static byte[] _pendingSave;
        private static float _menuSeenAt = -1f;

        private static readonly System.Collections.Generic.HashSet<IPeer> _helloSentTo = new System.Collections.Generic.HashSet<IPeer>();
        private static readonly System.Collections.Generic.HashSet<IPeer> _helloFrom = new System.Collections.Generic.HashSet<IPeer>();
        private static readonly System.Collections.Generic.HashSet<IPeer> _saveSentTo = new System.Collections.Generic.HashSet<IPeer>();

        /// My player number: 0 = host, 1.. = guests (the host tells each guest its number).
        public static int MyId;
        public static bool Welcomed;
        public static int Players => IsHost ? 1 + Net.ConnectedCount : !Net.Connected ? 1 : Math.Max(2, _knownPlayers);
        private static int _knownPlayers;
        private static float _nextCount;

        // Things one player does that every other player must see: the host passes them on.
        private static readonly System.Collections.Generic.HashSet<Msg> Relayed = new System.Collections.Generic.HashSet<Msg>
        { Msg.Pose, Msg.ItemTaken, Msg.Downed, Msg.FountainHeal, Msg.Gold, Msg.Bag, Msg.TownArea, Msg.BreakAt, Msg.BreakDetailAt, Msg.EndRun, Msg.ShopState, Msg.Shelves, Msg.DayTime };

        private static void Handle(IPeer from, Msg type, byte[] data)
        {
            if (IsHost && Relayed.Contains(type)) Net.SendExcept(from, type, data);
            switch (type)
            {
                case Msg.Welcome:
                    if (IsGuest) { MyId = data[0]; Welcomed = true; SteamCoop.Status = "Conectado: eres el jugador " + (MyId + 1); Plugin.Log.LogInfo($"[session] I am player {MyId}"); }
                    break;
                case Msg.PlayerCount:
                    _knownPlayers = data[0];
                    break;
                case Msg.Hello:
                    var v = Encoding.UTF8.GetString(data);
                    _helloReceived = true;
                    _helloFrom.Add(from);
                    if (IsHost) SteamCoop.Status = "Se ha unido " + SteamCoop.PeerName(from);
                    Plugin.Log.LogInfo($"[session] hello from peer, version {v}");
                    if (v != Plugin.Version) Plugin.Log.LogWarning($"[session] version mismatch: mine {Plugin.Version}, theirs {v}");
                    break;
                case Msg.Save:
                    if (IsGuest) _pendingSave = data;
                    break;
                case Msg.Pose:
                    // A guest with no number yet says it is 0 (the host's number): ignore it until welcomed.
                    if (data[0] == MyId) break;
                    PosesReceived++;
                    Puppet.Ensure(data[0]).Apply(data, LocalZone());
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
                case Msg.Gold: Shared.OnGold(data); break;
                case Msg.ShopState: ShopSync.OnShopState(data); break;
                case Msg.Shelves: ShopSync.OnShelves(data); break;
                case Msg.Customers: if (IsGuest) ShopSync.OnCustomers(data); break;
                case Msg.ShopSell: if (IsHost) ShopSync.OnShopSell(); break;
                case Msg.DayTime: DaySync.OnDayTime(data); break;
                case Msg.CatchThief: if (IsHost) ShopSync.OnCatchThief(data); break;
                case Msg.ShopResults: if (IsGuest) ShopResults.OnResults(data); break;
                case Msg.Bye:
                    if (IsHost) { Plugin.Log.LogInfo($"[session] player {from.Id} left"); from.Close(); }
                    else { SteamCoop.Status = data.Length > 0 && data[0] == 1 ? "El anfitrión te ha expulsado de la partida" : "El anfitrión ha cerrado la partida"; CoopMenu.HostClosed(); }
                    break;
                case Msg.PlayerLeft: if (IsGuest) RemovePlayer(data[0]); break;
                case Msg.Bag: Shared.OnBag(data); break;
                case Msg.Vote: Vote.OnVote(from.Id, data); break;
                case Msg.VoteResult: if (IsGuest) Vote.OnResult(data); break;
                case Msg.TownArea: TownSync.OnTownArea(data); break;
                case Msg.RoomDoors: if (IsGuest) DoorSync.OnRoomDoors(data); break;
                case Msg.BreakAt:
                    BreakableSync.OnBreakAt(data);
                    break;
                case Msg.DoorRequest:
                    if (IsHost) Dungeon.OnDoorRequest(data);
                    break;
                case Msg.Ping:
                    from.Send(Msg.Pong, data);   // only to whoever asked (their clock)
                    break;
                case Msg.Pong:
                    LastPing = (Time.unscaledTime - BitConverter.ToSingle(data, 0)) * 1000f;
                    break;
            }
        }

        private static void SendSave(IPeer to)
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
            to.Send(Msg.Save, ms.ToArray());
            Plugin.Log.LogInfo($"[session] sent save to guest {to.Id}: slot {gm.currentGameSlot.seed}, {json.Length} bytes");
        }

        private static void LoadHostSave(byte[] payload)
        {
            if (GuestLoaded) return;
            var r = new BinaryReader(new MemoryStream(payload));
            int seed = r.ReadInt32();
            int len = r.ReadInt32();
            if (len < 0 || len > SaveGuard.MaxSaveBytes) { Plugin.Log.LogWarning("[session] host save refused: size " + len); SteamCoop.Status = "La partida recibida no es válida: no se carga"; return; }
            var json = r.ReadBytes(len);
            if (!SaveGuard.IsSafe(json, out var why)) { Plugin.Log.LogWarning("[session] host save refused: " + why); SteamCoop.Status = "La partida recibida no es válida: no se carga"; return; }
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
            sb.AppendLine($"shop shelf_sent={ShopSync.ShelfSent} shelf_applied={ShopSync.ShelfApplied} customers_sent={ShopSync.CustomersSent} customers_applied={ShopSync.CustomersApplied} sales_asked={ShopSync.SalesAsked} sales_done={ShopSync.SalesDone}");
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









