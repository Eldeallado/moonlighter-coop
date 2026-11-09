using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Shared loot in dungeons: only the host drops items (from enemies, boxes, quests); the guest gets the same
    /// item at the same spot. Whoever picks it up keeps it, and it disappears for the other.
    public static class Loot
    {
        public static bool Applying;
        private static int _nextId = 1;
        private static readonly Dictionary<int, ItemStack> _items = new Dictionary<int, ItemStack>();
        private static readonly Dictionary<ItemStack, int> _ids = new Dictionary<ItemStack, int>();
        public static int DropsSent, DropsShown, Taken, Removed;

        public static bool InDungeon => Net.Juego && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded();

        private static void Track(int id, ItemStack s) { _items[id] = s; _ids[s] = id; }
        public static int IdOf(ItemStack s) => s != null && _ids.TryGetValue(s, out var id) ? id : 0;

        // Host: an item was dropped; tell the guest where it lands once it has landed.
        public static void HostDropped(ItemStack s)
        {
            if (!Session.IsHost || !InDungeon || Applying || s == null || s.master == null) return;
            int id = _nextId++;
            Track(id, s);
            Plugin.Instance.StartCoroutine(SendWhenLanded(id, s));
        }

        private static IEnumerator SendWhenLanded(int id, ItemStack s)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            if (s == null) yield break;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(id); w.Write(s.master.name); w.Write(s.Quantity);
            w.Write(s.transform.position.x); w.Write(s.transform.position.y);
            Net.Send(Msg.ItemDrop, ms.ToArray());
            DropsSent++;
        }

        public static void OnItemDrop(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int id = r.ReadInt32(); string name = r.ReadString(); int qty = r.ReadInt32();
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            if (!InDungeon) return;
            var master = ItemDatabase.GetItemByName(name, GameManager.Instance.GetCurrentGamePlusLevel());
            if (master == null) { Plugin.Log.LogWarning($"[loot] unknown item {name}"); return; }
            var stack = ItemStack.Create(master, qty);
            var dm = DungeonManager.Instance;
            Applying = true;
            try { stack.Drop(pos, 0.01f, dm != null && dm.currentDungeonRoom != null ? dm.currentDungeonRoom.transform : null); }
            finally { Applying = false; }
            Track(id, stack);
            DropsShown++;
        }

        // Either side picked something up: it leaves the floor on both, and BOTH players get it (shared bag).
        public static void PickedUp(int id, ItemMaster master, int qty)
        {
            if (!Net.Juego || master == null || qty <= 0) return;
            if (id != 0) _items.Remove(id);
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(id); w.Write(master.name); w.Write(qty);
            Net.Send(Msg.ItemTaken, ms.ToArray());
            Taken++;
        }

        public static int Received;
        public static void OnItemTaken(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int id = r.ReadInt32(); string name = r.ReadString(); int qty = r.ReadInt32();
            if (id != 0 && _items.TryGetValue(id, out var s) && s != null)
            {
                _ids.Remove(s);
                Object.Destroy(s.gameObject);
                Removed++;
            }
            _items.Remove(id);
            // Both bags are one shared bag now (Shared.cs): the picker's bag change reaches us as a snapshot.
        }
    }

    [HarmonyPatch(typeof(ItemStack), nameof(ItemStack.Drop), typeof(Vector2), typeof(float), typeof(Transform))]
    static class LootDropRadius
    {
        static bool Prefix(ItemStack __instance) => LootPatches.BeforeDrop(__instance);
        static void Postfix(ItemStack __instance) => LootPatches.AfterDrop(__instance);
    }

    [HarmonyPatch(typeof(ItemStack), nameof(ItemStack.Drop), typeof(Vector2), typeof(Vector2), typeof(float), typeof(Transform), typeof(float), typeof(float))]
    static class LootDropDirectional
    {
        static bool Prefix(ItemStack __instance) => LootPatches.BeforeDrop(__instance);
        static void Postfix(ItemStack __instance) => LootPatches.AfterDrop(__instance);
    }

    static class LootPatches
    {
        // Guest: drops in the dungeon come from the host, not from the guest's own rolls.
        public static bool BeforeDrop(ItemStack s)
        {
            if (Session.IsGuest && Loot.InDungeon && !Loot.Applying)
            {
                if (s != null) Object.Destroy(s.gameObject);
                return false;
            }
            return true;
        }

        public static void AfterDrop(ItemStack s) => Loot.HostDropped(s);
    }

    [HarmonyPatch(typeof(PickOnWalk), nameof(PickOnWalk.TryToAdd))]
    static class LootPickup
    {
        static void Prefix(PickOnWalk __instance, out (int id, ItemStack s, int qty, ItemMaster m) __state)
        {
            var s = Traverse.Create(__instance).Field("_stack").GetValue<ItemStack>();
            __state = (Loot.IdOf(s), s, s != null ? s.Quantity : 0, s != null ? s.master : null);
        }

        static void Postfix((int id, ItemStack s, int qty, ItemMaster m) __state)
        {
            if (__state.m == null || Loot.Applying) return;
            var hero = HeroMerchant.Instance;
            // The game either empties the floor stack or moves the stack object itself into the bag.
            bool inBag = __state.s != null && hero != null && __state.s.transform.IsChildOf(hero.transform);
            int left = (__state.s == null || inBag) ? 0 : __state.s.Quantity;
            int picked = __state.qty - left;
            if (picked > 0) { Loot.PickedUp(__state.id, __state.m, picked); Stats.Picked(__state.m, picked); }
        }
    }
}
