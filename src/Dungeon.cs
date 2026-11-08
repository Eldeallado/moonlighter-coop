using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The host leads: entering a dungeon, the next floor, the way home and every room change.
    /// The guest builds the host's exact floor from a DungeonSaveInfo instead of generating its own.
    public static class Dungeon
    {
        public enum Travel : byte { NextLevel = 1, Town = 2 }

        public static bool ApplyingRemote;      // a travel the host asked for is running on the guest
        public static bool InHeroTravel;        // inside HeroMerchant's own travel (it already told the guest)
        public static bool Following;
        public static bool Leading;             // the host walks through a door because a guest asked (even as a ghost)           // the guest is walking through a door to follow the host
        public static DungeonSaveInfo PendingInfo;
        public static int FloorSeed;            // host picks one per floor; both generate with it
        public static int InfosSent, InfosReceived;
        private static float _roomMismatchSince = -1f;
        private static float _followCooldown;

        public static void Init()
        {
            DungeonGenerator.OnGeneratedDungeon += OnGenerated;
        }

        public static string RoomZone()
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null) return "dungeon";
            var r = dm.currentDungeonRoom;
            return $"dungeon:{dm.culture}:{dm.level}:{r.x},{r.y}{(r.isSecret ? "s" : "")}{(r is DungeonBossRoom ? "b" : "")}";
        }

        public static string Signature()
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.dungeonRooms == null) return "";
            var parts = dm.dungeonRooms.Where(r => r != null && !(r is DungeonBossRoom))
                .Select(r => $"{r.roomInfo?.name}@{r.x},{r.y}").OrderBy(s => s);
            return string.Join("|", parts).GetHashCode().ToString("X8") + "/" + dm.dungeonRooms.Count;
        }

        /// Same rooms AND same things inside them (details, breakables, chests, enemies).
        public static string ContentSignature()
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.dungeonRooms == null) return "";
            var parts = dm.dungeonRooms.Where(r => r != null).OrderBy(r => r.x).ThenBy(r => r.y).ThenBy(r => r.isSecret)
                .Select(r => $"{r.x},{r.y}:{r.GetComponentsInChildren<SpriteRenderer>(true).Length}:{r.roomEnemies.Count}");
            return string.Join("|", parts).GetHashCode().ToString("X8");
        }

        // ---------- host side ----------

        public static void SendTravel(Travel kind, ItemMaster.Culture culture, int level)
        {
            if (!Session.IsHost || !Net.Connected) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)kind); w.Write((int)culture); w.Write(level); w.Write(FloorSeed);
            Net.Send(Msg.Travel, ms.ToArray());
            Plugin.Log.LogInfo($"[dungeon] host travel {kind} {culture} {level} seed {FloorSeed}");
        }

        /// Send one guest (reconnecting, or joining late) to the host's floor, starting in the host's room.
        public static void CatchUp(IPeer p)
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Travel.NextLevel); w.Write((int)dm.culture); w.Write(dm.level); w.Write(FloorSeed);
            p.Send(Msg.Travel, ms.ToArray());
            var info = new DungeonSaveInfo(dm);
            var cur = dm.currentDungeonRoom;
            info.portalRoomIndex = info.rooms.FindIndex(r => r.x == cur.x && r.y == cur.y && r.isSecret == cur.isSecret);
            info.portalPosition = HeroMerchant.Instance != null ? HeroMerchant.Instance.transform.position : cur.transform.position;
            FsJSONManager.Serialize(info, out fsData data);
            p.Send(Msg.DungeonInfo, Encoding.UTF8.GetBytes(fsJsonPrinter.CompressedJson(data)));
            EnemySync.SendDeadTo(p);
            Reconnect.CatchUps++;
            Plugin.Log.LogInfo($"[dungeon] catch-up for player {p.Id}: {dm.culture} floor {dm.level}, room {cur.x},{cur.y}, {EnemySync.Dead.Count} dead");
        }

        private static void OnGenerated()
        {
            if (!Session.IsHost || !Net.Connected) return;
            Plugin.Instance.StartCoroutine(SendInfoWhenReady());
        }

        private static IEnumerator SendInfoWhenReady()
        {
            yield return null;
            var dm = DungeonManager.Instance;
            if (dm == null) yield break;
            var info = new DungeonSaveInfo(dm);
            // Make the guest start where the host starts, not in the boss room (portalRoomIndex -1 means boss).
            var init = dm.initialDungeonRoom;
            info.portalRoomIndex = info.rooms.FindIndex(r => init != null && r.x == init.x && r.y == init.y && !r.isSecret);
            if (init != null) info.portalPosition = init.transform.position;
            FsJSONManager.Serialize(info, out fsData data);
            var json = Encoding.UTF8.GetBytes(fsJsonPrinter.CompressedJson(data));
            Net.Send(Msg.DungeonInfo, json);
            InfosSent++;
            Plugin.Log.LogInfo($"[dungeon] sent floor {info.culture} {info.floor}: {info.rooms.Count} rooms, start {info.portalRoomIndex}, {json.Length} bytes, sig {Signature()}");
        }

        // ---------- guest side ----------

        public static void OnTravel(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var kind = (Travel)r.ReadByte();
            var culture = (ItemMaster.Culture)r.ReadInt32();
            int level = r.ReadInt32();
            FloorSeed = r.ReadInt32();
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            Plugin.Log.LogInfo($"[dungeon] guest follows travel {kind} {culture} {level} seed {FloorSeed}");
            // The run may have ended (results screen up on both): close ours, the host chose what comes next.
            var gui = GUIManager.Instance;
            if (gui != null && gui.currentInventory != null && gui.currentInventory.GetType().Name.Contains("Result"))
            {
                gui.AllowPanelChange();
                gui.DisableCurrentPanel();
                GameManager.Instance.UnpauseGame();
            }
            ApplyingRemote = true;
            try
            {
                switch (kind)
                {
                    case Travel.NextLevel:
                        PendingInfo = null;
                        if (level == 0) hero.LoadDungeonScene(culture);
                        else
                        {
                            Traverse.Create(hero).Field("_currentDungeonLevel").SetValue(level - 1);
                            Traverse.Create(hero).Field("_lastDungeonCulture").SetValue(culture);
                            hero.LoadNextLevelDungeonScene(culture);
                        }
                        break;
                    case Travel.Town:
                        if (GameManager.Instance.IsDungeonSceneLoaded()) hero.LoadTownScene();
                        break;
                }
            }
            finally { ApplyingRemote = false; }
        }

        public static void OnInfo(byte[] data)
        {
            // Same check as for the save: only the game's own types, sane size.
            if (!SaveGuard.IsSafe(data, out var why)) { Plugin.Log.LogWarning("[dungeon] floor from host refused: " + why); return; }
            var text = Encoding.UTF8.GetString(data);
            DungeonSaveInfo info = null;
            FsJSONManager.Deserialize(text, ref info, false);
            PendingInfo = info;
            InfosReceived++;
            Plugin.Log.LogInfo($"[dungeon] got floor {info?.culture} {info?.floor}: {info?.rooms?.Count} rooms");
        }

        public static IEnumerator GuestGenerate(ItemMaster.Culture culture, int level)
        {
            float start = Time.unscaledTime;
            while (PendingInfo == null || PendingInfo.culture != culture || PendingInfo.floor != level)
            {
                if (Time.unscaledTime - start > 60f)
                {
                    Plugin.Log.LogError($"[dungeon] no floor from host for {culture} {level}; generating locally");
                    _bypass = true;
                    var e = DungeonGenerator.GenerateDungeon(culture, level);
                    _bypass = false;
                    yield return e;
                    yield break;
                }
                yield return null;
            }
            var info = PendingInfo;
            PendingInfo = null;
            Plugin.Log.LogInfo($"[dungeon] guest builds host floor {culture} {level}");
            yield return DungeonGenerator.GenerateSavedDungeon(info);
        }

        internal static bool _bypass;
        private static string _lastFollowKey = "";
        private static int _followTries;
        private static float _nextDoorRequest;
        public static int DoorRequestsSent, DoorRequestsDone;

        public static void RequestDoor(DungeonDoor door)
        {
            var to = door.destinationRoom;
            if (to == null || Time.unscaledTime < _nextDoorRequest) return;
            _nextDoorRequest = Time.unscaledTime + 1.5f;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(to.x); w.Write(to.y); w.Write(to.isSecret); w.Write(to is DungeonBossRoom);
            Net.Send(Msg.DoorRequest, ms.ToArray());
            DoorRequestsSent++;
            Plugin.Log.LogInfo($"[dungeon] guest asks to go to room {to.x},{to.y}");
        }

        public static void OnDoorRequest(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int x = r.ReadInt32(), y = r.ReadInt32(); bool secret = r.ReadBoolean(), boss = r.ReadBoolean();
            var dm = DungeonManager.Instance;
            var gm = GameManager.Instance;
            var hero = HeroMerchant.Instance;
            if (dm == null || dm.currentDungeonRoom == null || hero == null || !gm.IsDungeonSceneLoaded() || !gm.NotLoadingScene) return;
            var door = dm.currentDungeonRoom.roomDoors.FirstOrDefault(d => d != null && d.destinationRoom != null
                && (boss ? d.destinationRoom is DungeonBossRoom : d.destinationRoom.x == x && d.destinationRoom.y == y && d.destinationRoom.isSecret == secret));
            if (door == null) { Plugin.Log.LogWarning($"[dungeon] guest door request to {x},{y}: no such door from here"); return; }
            if (!door.isOpened) { Plugin.Log.LogInfo("[dungeon] guest door request: door is closed (fight first)"); return; }
            // Never drag the host to another room while it has a chest, the bag or any menu open (black screen).
            if (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null) { Plugin.Log.LogInfo("[dungeon] guest door request ignored: host has a panel open"); return; }
            Plugin.Log.LogInfo($"[dungeon] host goes through {door.name} because the guest did");
            DoorRequestsDone++;
            Leading = true;
            try { door.OnPlayerEnter(hero.gameObject); } finally { Leading = false; }
        }

        /// Every frame on the guest: if the host is in another room of this floor, walk through the door to it.
        public static void GuestFollowTick(string hostZone)
        {
            if (!Session.IsGuest || HeroMerchant.Instance == null) return;
            var dm = DungeonManager.Instance;
            var gm = GameManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || !gm.IsDungeonSceneLoaded() || !gm.NotLoadingScene) { _roomMismatchSince = -1f; return; }
            string mine = RoomZone();
            if (string.IsNullOrEmpty(hostZone) || !hostZone.StartsWith("dungeon:") || hostZone == mine) { _roomMismatchSince = -1f; return; }
            // Same floor?
            var hp = hostZone.Split(':'); var mp = mine.Split(':');
            if (hp.Length < 4 || mp.Length < 4 || hp[1] != mp[1] || hp[2] != mp[2]) return;
            float now = Time.unscaledTime;
            if (_roomMismatchSince < 0f) { _roomMismatchSince = now; return; }
            if (now - _roomMismatchSince < 0.4f || now < _followCooldown) return;
            _followCooldown = now + 2.5f;

            string key = hp[3];
            bool secret = key.Contains("s"), boss = key.Contains("b");
            var xy = key.TrimEnd('s', 'b').Split(',');
            int x = int.Parse(xy[0]), y = int.Parse(xy[1]);
            DungeonRoom target = boss ? dm.bossRoom : dm.dungeonRooms.FirstOrDefault(r => r != null && r.x == x && r.y == y && r.isSecret == secret && !(r is DungeonBossRoom));
            if (target == null) { Plugin.Log.LogWarning($"[dungeon] host room {key} not found here"); return; }
            var door = dm.currentDungeonRoom.roomDoors.FirstOrDefault(d => d != null && d.destinationRoom == target);
            // If walking through the door already failed for this room, move there for sure.
            if (_lastFollowKey == key) _followTries++; else { _lastFollowKey = key; _followTries = 1; }
            if (_followTries > 1) door = null;
            Plugin.Log.LogInfo($"[dungeon] guest follows host to room {key} via {(door != null ? door.name : "teleport")}");
            Following = true;
            try
            {
                if (door != null) door.OnPlayerEnter(HeroMerchant.Instance.gameObject);
                else
                {
                    dm.SpecialChangeRoomTo(target);
                    var p = Puppet.Instance;
                    if (p != null) HeroMerchant.Instance.heroMerchantController.TeleportTo(p.transform.position);
                }
            }
            finally { Following = false; }
        }
    }

    // Host: tell the guest when Will goes down a floor (also the first floor) or home.
    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.LoadNextLevelDungeonScene))]
    static class TravelNextLevel
    {
        static bool Prefix()
        {
            if (Session.IsGuest && Net.Connected && !Dungeon.ApplyingRemote)
            {
                Plugin.Log.LogInfo("[dungeon] guest tried to change floor on its own: blocked (the host leads)");
                return false;
            }
            // A new floor: the host picks the dice both sides will generate it with.
            if (Session.IsHost) Dungeon.FloorSeed = Net.Connected ? new System.Random().Next(1, int.MaxValue) : 0;
            Dungeon.InHeroTravel = true;
            return true;
        }
        static void Postfix(HeroMerchant __instance, ItemMaster.Culture culture)
        {
            Dungeon.InHeroTravel = false;
            if (Session.IsHost)
                Dungeon.SendTravel(Dungeon.Travel.NextLevel, culture, Traverse.Create(__instance).Field("_currentDungeonLevel").GetValue<int>());
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.LoadTownScene))]
    static class TravelTown
    {
        static bool Prefix()
        {
            if (Session.IsGuest && Net.Connected && !Dungeon.ApplyingRemote)
            {
                Plugin.Log.LogInfo("[dungeon] guest tried to go home on its own: blocked (the host leads)");
                return false;
            }
            Dungeon.InHeroTravel = true;
            return true;
        }
        static void Postfix() { Dungeon.InHeroTravel = false; if (Session.IsHost) Dungeon.SendTravel(Dungeon.Travel.Town, ItemMaster.Culture.Merchant, 0); }
    }

    // Guest: build the host's floor instead of a random one.
    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateDungeon), typeof(ItemMaster.Culture), typeof(int), typeof(bool))]
    static class GuestUsesHostFloor
    {
        static bool Prefix(ItemMaster.Culture culture, int level, bool test, ref IEnumerator __result)
        {
            if (!Session.IsGuest || !Net.Connected || Dungeon._bypass || test) return true;
            if (Dungeon.FloorSeed != 0) return true;   // same seed as the host: generate normally
            __result = Dungeon.GuestGenerate(culture, level);
            return false;
        }
    }

    // Isaac-style: whoever walks through a door takes both players. The guest asks the host to go;
    // the host walks through the same door and the guest then follows it as usual.
    [HarmonyPatch(typeof(DungeonDoor), nameof(DungeonDoor.OnPlayerEnter))]
    static class GuestDoorsGoThroughHost
    {
        static bool Prefix(DungeonDoor __instance)
        {
            // A ghost cannot change rooms (it would drag everyone else along).
            if (Downed.Me && Net.Connected && !Dungeon.Following && !Dungeon.Leading) return false;
            if (!Session.IsGuest || !Net.Connected || Dungeon.Following) return true;
            Dungeon.RequestDoor(__instance);
            return false;
        }
    }
}



