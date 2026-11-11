using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// One purse and one bag for both players. Gold travels as the change (+/-) so both purses stay equal.
    /// The bag travels as a whole snapshot whenever it changes; the other side makes its bag the same.
    /// What each Will has equipped stays personal.
    public static class Shared
    {
        public static bool ApplyingGold, ApplyingBag;
        public static int GoldSent, GoldApplied, BagsSent, BagsApplied;
        private static bool _bagDirty;
        private static float _bagDirtyAt;

        public static void Init()
        {
            HeroMerchantInventory.OnItemAddedToInventory += (s, i) => BagChanged();
            HeroMerchantInventory.OnItemRemovedFromInventory += (s, i) => BagChanged();
            HeroMerchantInventory.OnItemChanged += i => BagChanged();
        }

        // ---------- gold ----------

        public static void GoldChanged(int amount)
        {
            if (ApplyingGold || !Net.Juego || amount == 0) return;
            if (amount > 0) Stats.Count(Stats.Gold, amount);   // gold this player earned
            var ms = new MemoryStream(); new BinaryWriter(ms).Write(amount);
            Net.Send(Msg.Gold, ms.ToArray());
            GoldSent++;
        }

        public static void OnGold(byte[] data)
        {
            int amount = new BinaryReader(new MemoryStream(data)).ReadInt32();
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            ApplyingGold = true;
            try { hero.addGold(amount); } finally { ApplyingGold = false; }
            GoldApplied++;
        }

        // ---------- bag ----------

        // The host owns the bag. Each snapshot carries a number at the end (old versions ignore it):
        // from the host, the bag version; from a guest, the host version it started from. If a guest's
        // change crosses another one on the way (lag, two pick-ups at once), the host adds what that guest
        // gained or lost to its own bag instead of copying it, and sends the result to everyone.
        private static int _version, _baseVersion;
        private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, int>> _history =
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, int>>();
        private static readonly System.Collections.Generic.Dictionary<int, (int baseV, System.Collections.Generic.Dictionary<string, int> counts)> _lastFrom =
            new System.Collections.Generic.Dictionary<int, (int, System.Collections.Generic.Dictionary<string, int>)>();
        public static int BagsMerged;

        private static void BagChanged()
        {
            if (ApplyingBag || !Net.Juego) return;
            _bagDirty = true;
            _bagDirtyAt = Time.unscaledTime;
        }

        public static void Tick()
        {
            // Wait a moment so a move (take from one slot, drop in another) goes as one snapshot.
            if (!_bagDirty || Time.unscaledTime - _bagDirtyAt < 0.25f || !Net.Juego) return;
            SendBag();
        }

        private static void SendBag()
        {
            _bagDirty = false;
            if (Session.IsHost) { _version++; Remember(); }
            var bag = BagBytes(Session.IsHost ? _version : _baseVersion);
            if (bag == null) return;
            Net.Send(Msg.Bag, bag);
            BagsSent++;
        }

        private static void Remember()
        {
            var c = Counts(HeroMerchant.Instance?.heroMerchantInventory);
            if (c == null) return;
            _history[_version] = c;
            _history.Remove(_version - 64);
        }

        private static string Key(string name, int plus, int enchLevel, int enchType, string enchEffect) =>
            name + "|" + plus + "|" + enchLevel + "|" + enchType + "|" + enchEffect;

        private static System.Collections.Generic.Dictionary<string, int> Counts(HeroMerchantInventory inv)
        {
            if (inv == null || inv.slots == null) return null;
            var c = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var slot in inv.slots)
            {
                var it = slot?.item;
                if (it == null || it.master == null) continue;
                var si = new ItemSaveInfo(it);
                string k = Key(si.name ?? "", si.plusLevel, si.enchantmentLevel, si.enchantmentType, si.enchantmentEffectName ?? "");
                c.TryGetValue(k, out int q); c[k] = q + si.quantity;
            }
            return c;
        }

        private static ItemStack MakeStack(string name, int plus, int qty, int enchLevel, int enchType, string enchEffect)
        {
            var master = ItemDatabase.GetItemByName(name, plus);
            if (master == null) return null;
            var stack = ItemStack.Create(master, qty);
            if (enchLevel > 0)
            {
                var eq = stack.GetComponent<Equipment>();
                var ench = enchType != 2
                    ? RecipeManager.Instance.GetEnchantment(master.name, 0, master.plusLevel)
                    : RecipeManager.Instance.GetEnchantmentRecipeToItemAndEffect(master.name, enchEffect, master.plusLevel);
                if (eq != null && ench != null) for (int k = 0; k < enchLevel; k++) eq.EnchantWith(ench);
            }
            return stack;
        }

        private static byte[] BagBytes(int version)
        {
            var b = BagBytes();
            if (b == null) return null;
            var ms = new MemoryStream(); ms.Write(b, 0, b.Length);
            new BinaryWriter(ms).Write(version);
            return ms.ToArray();
        }

        private static byte[] BagBytes()
        {
            var inv = HeroMerchant.Instance?.heroMerchantInventory;
            if (inv == null || inv.slots == null) return null;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(inv.slots.Length);
            foreach (var slot in inv.slots)
            {
                var it = slot?.item;
                w.Write(it != null && it.master != null);
                if (it == null || it.master == null) continue;
                var si = new ItemSaveInfo(it);
                w.Write(si.name ?? ""); w.Write(si.plusLevel); w.Write(si.quantity);
                w.Write(si.enchantmentLevel); w.Write(si.enchantmentType); w.Write(si.enchantmentEffectName ?? "");
            }
            return ms.ToArray();
        }

        /// A guest who just (re)joined: the shared bag and purse as they are right now.
        public static void SendStateTo(IPeer p)
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            if (!_history.ContainsKey(_version)) Remember();
            var bag = BagBytes(_version);
            if (bag != null) p.Send(Msg.Bag, bag);
            var ms = new MemoryStream(); new BinaryWriter(ms).Write(hero.GetCurrentGold());
            p.Send(Msg.GoldSet, ms.ToArray());
            Plugin.Log.LogInfo($"[shared] sent bag and gold to player {p.Id}");
        }

        public static void OnGoldSet(byte[] data)
        {
            int target = new BinaryReader(new MemoryStream(data)).ReadInt32();
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            int diff = target - hero.GetCurrentGold();
            if (diff == 0) return;
            ApplyingGold = true;
            try { hero.addGold(diff); } finally { ApplyingGold = false; }
        }

        private struct Slot { public bool has; public string name, enchEffect; public int plus, qty, enchLevel, enchType; }

        public static void OnBag(IPeer from, byte[] data)
        {
            var inv = HeroMerchant.Instance?.heroMerchantInventory;
            if (inv == null || inv.slots == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            var slots = new Slot[n];
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                var s = new Slot { has = r.ReadBoolean(), name = "", enchEffect = "" };
                if (s.has)
                {
                    s.name = r.ReadString(); s.plus = r.ReadInt32(); s.qty = r.ReadInt32(); s.enchLevel = r.ReadInt32(); s.enchType = r.ReadInt32(); s.enchEffect = r.ReadString();
                    string k = Key(s.name, s.plus, s.enchLevel, s.enchType, s.enchEffect);
                    counts.TryGetValue(k, out int q); counts[k] = q + s.qty;
                }
                slots[i] = s;
            }
            // Old versions send no number: their snapshot is copied as it is, like before
            int? stamp = r.BaseStream.Length - r.BaseStream.Position >= 4 ? r.ReadInt32() : (int?)null;

            if (Session.IsGuest)
            {
                // A change of ours not sent yet would be lost under the host's snapshot: send it first
                if (_bagDirty) SendBag();
                Apply(inv, slots);
                if (stamp.HasValue) _baseVersion = stamp.Value;
                return;
            }

            // Host: a guest's bag changed
            System.Collections.Generic.Dictionary<string, int> reference = null;
            if (stamp.HasValue && stamp.Value != _version)
            {
                // Its change crossed another: compare with what that guest started from
                reference = from != null && _lastFrom.TryGetValue(from.Id, out var last) && last.baseV == stamp.Value
                    ? last.counts
                    : (_history.TryGetValue(stamp.Value, out var h) ? h : null);
            }
            if (stamp.HasValue && from != null) _lastFrom[from.Id] = (stamp.Value, counts);
            if (reference == null) Apply(inv, slots);
            else Merge(inv, reference, counts, slots);
            SendBag();   // everyone ends with the host's bag (also the one who sent it)
        }

        private static void Apply(HeroMerchantInventory inv, Slot[] slots)
        {
            ApplyingBag = true;
            try
            {
                for (int i = 0; i < slots.Length && i < inv.slots.Length; i++)
                {
                    var s = slots[i];
                    var cur = inv.slots[i].item;
                    bool same = s.has
                        ? cur != null && cur.master != null && cur.master.name == s.name && cur.Quantity == s.qty && new ItemSaveInfo(cur).enchantmentLevel == s.enchLevel
                        : cur == null;
                    if (same) continue;
                    inv.SetItem(null, i);
                    if (cur != null) Object.Destroy(cur.gameObject);
                    if (!s.has) continue;
                    var stack = MakeStack(s.name, s.plus, s.qty, s.enchLevel, s.enchType, s.enchEffect);
                    if (stack != null) inv.SetItem(stack, i);
                }
            }
            finally { ApplyingBag = false; }
            BagsApplied++;
        }

        // What the guest gained or lost since its starting point, added to the host's bag
        private static void Merge(HeroMerchantInventory inv, System.Collections.Generic.Dictionary<string, int> before,
            System.Collections.Generic.Dictionary<string, int> now, Slot[] slots)
        {
            var keys = new System.Collections.Generic.HashSet<string>(before.Keys);
            keys.UnionWith(now.Keys);
            ApplyingBag = true;
            try
            {
                foreach (var k in keys)
                {
                    before.TryGetValue(k, out int a); now.TryGetValue(k, out int b);
                    int d = b - a;
                    if (d == 0) continue;
                    var p = k.Split('|');
                    if (d > 0)
                    {
                        var stack = MakeStack(p[0], int.Parse(p[1]), d, int.Parse(p[2]), int.Parse(p[3]), p[4]);
                        if (stack != null && !inv.TryAddItem(stack)) { Plugin.Log.LogWarning($"[shared] bag full, {d}x {p[0]} lost"); Object.Destroy(stack.gameObject); }
                    }
                    else
                    {
                        var master = ItemDatabase.GetItemByName(p[0], int.Parse(p[1]));
                        if (master != null) inv.RemoveItem(master, -d);
                    }
                }
            }
            finally { ApplyingBag = false; }
            BagsMerged++;
            Plugin.Log.LogInfo($"[shared] bag change merged ({keys.Count} kinds)");
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.addGold))]
    static class SharedGold
    {
        static void Postfix(int amount) => Shared.GoldChanged(amount);
    }
}
