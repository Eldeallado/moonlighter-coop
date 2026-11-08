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
            _bagDirty = false;
            var bag = BagBytes();
            if (bag == null) return;
            Net.Send(Msg.Bag, bag);
            BagsSent++;
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
            var bag = BagBytes();
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

        public static void OnBag(byte[] data)
        {
            var inv = HeroMerchant.Instance?.heroMerchantInventory;
            if (inv == null || inv.slots == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            ApplyingBag = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    bool has = r.ReadBoolean();
                    string name = ""; int plus = 0, qty = 0, enchLevel = 0, enchType = 0; string enchEffect = "";
                    if (has) { name = r.ReadString(); plus = r.ReadInt32(); qty = r.ReadInt32(); enchLevel = r.ReadInt32(); enchType = r.ReadInt32(); enchEffect = r.ReadString(); }
                    if (i >= inv.slots.Length) continue;
                    var cur = inv.slots[i].item;
                    bool same = has
                        ? cur != null && cur.master != null && cur.master.name == name && cur.Quantity == qty && new ItemSaveInfo(cur).enchantmentLevel == enchLevel
                        : cur == null;
                    if (same) continue;
                    inv.SetItem(null, i);
                    if (cur != null) Object.Destroy(cur.gameObject);
                    if (!has) continue;
                    var master = ItemDatabase.GetItemByName(name, plus);
                    if (master == null) continue;
                    var stack = ItemStack.Create(master, qty);
                    if (enchLevel > 0)
                    {
                        var eq = stack.GetComponent<Equipment>();
                        var ench = enchType != 2
                            ? RecipeManager.Instance.GetEnchantment(master.name, 0, master.plusLevel)
                            : RecipeManager.Instance.GetEnchantmentRecipeToItemAndEffect(master.name, enchEffect, master.plusLevel);
                        if (eq != null && ench != null) for (int k = 0; k < enchLevel; k++) eq.EnchantWith(ench);
                    }
                    inv.SetItem(stack, i);
                }
            }
            finally { ApplyingBag = false; }
            BagsApplied++;
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.addGold))]
    static class SharedGold
    {
        static void Postfix(int amount) => Shared.GoldChanged(amount);
    }
}
