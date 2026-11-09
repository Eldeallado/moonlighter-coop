using System.IO;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Giving gear to a partner (F2 → "Dar equipo"): the piece leaves your equipment and goes into the shared bag,
    /// where everybody can take it and equip it. The others are told what is waiting for them.
    public static class Give
    {
        public static readonly HeroMerchantInventory.EquipmentSlot[] Slots = {
            HeroMerchantInventory.EquipmentSlot.Weapon1, HeroMerchantInventory.EquipmentSlot.Weapon2,
            HeroMerchantInventory.EquipmentSlot.Head, HeroMerchantInventory.EquipmentSlot.Body, HeroMerchantInventory.EquipmentSlot.Boots,
            HeroMerchantInventory.EquipmentSlot.Potion, HeroMerchantInventory.EquipmentSlot.Amulet1, HeroMerchantInventory.EquipmentSlot.Amulet2 };
        public static readonly string[] SlotNames = { "Arma 1", "Arma 2", "Cabeza", "Cuerpo", "Botas", "Poción", "Amuleto 1", "Amuleto 2" };

        public static ItemStack Equipped(int i)
        {
            var hero = HeroMerchant.Instance;
            try { return hero != null ? hero.heroMerchantInventory.GetEquippedItemByType(Slots[i]) : null; } catch { return null; }
        }

        public static void ToBag(int i)
        {
            var hero = HeroMerchant.Instance;
            var stack = Equipped(i);
            if (hero == null || stack == null) return;
            var inv = hero.heroMerchantInventory;
            string name = Stats.ItemName(stack.master);
            inv.SetEquippedItemByType(null, Slots[i]);
            if (!inv.TryAddItem(stack))
            {
                inv.SetEquippedItemByType(stack, Slots[i]);   // no room: put it back on
                Banner.Show("La mochila está llena: haz sitio para dejar " + name);
                return;
            }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(SteamCoop.Available ? SteamCoop.MyName : "Tu compañero"); w.Write(name);
            Net.Send(Msg.Give, ms.ToArray());
            Banner.Show("Has dejado " + name + " en la mochila compartida");
            Plugin.Log.LogInfo("[give] " + name + " to the shared bag");
        }

        public static void OnGive(byte[] data)
        {
            try { var r = new BinaryReader(new MemoryStream(data)); Banner.Show(r.ReadString() + " te ha dejado " + r.ReadString() + " en la mochila (ábrela y equípalo)"); }
            catch { }
        }
    }
}
