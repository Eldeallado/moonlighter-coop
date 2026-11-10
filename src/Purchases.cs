using System.IO;
using HarmonyLib;

namespace MoonlighterCoop
{
    /// Spending in town (the forge, the witch, the hawker, the town board, the bank): gold and the bag are
    /// already shared, so anyone can use those menus; this just tells the others what was bought and for how much.
    public static class Purchases
    {
        public static void OnPurchase(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                string who = r.ReadString(), what = r.ReadString(), where = r.ReadString(); int gold = r.ReadInt32();
                Banner.Show($"{who} {what} {where} (−{gold.ToString("N0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." })} de oro)");
            }
            catch { }
        }

        /// Which menu the gold went to, and what for (from the panel that is open).
        public static bool Describe(object panel, out string what, out string where)
        {
            what = "ha gastado oro"; where = "";
            if (panel == null) return false;
            var t = Traverse.Create(panel);
            switch (panel.GetType().Name)
            {
                case "BlacksmithPanel":
                    where = "en la forja";
                    var rec = t.Field("selectedRecipe").GetValue<Recipe>();
                    what = rec != null ? "ha forjado " + rec.craftedItemNameDisplay : "ha pagado";
                    return true;
                case "WitchEnchantmentsPanel":
                    where = "en la bruja";
                    var ench = t.Field("_enchantment").GetValue<EnchantmentRecipe>();
                    var m = ench != null && GameManager.Instance != null ? ItemDatabase.GetItemByName(ench.itemName, GameManager.Instance.GetCurrentGamePlusLevel()) : null;
                    what = m != null ? "ha encantado " + Stats.ItemName(m) : "ha encantado una pieza";
                    return true;
                case "HawkerPanel": where = "en el puesto ambulante"; what = "ha comprado algo"; return true;
                case "TownBoardShopPanel": case "TownBoardStaffPanel": where = "en el tablón del pueblo"; what = "ha mejorado el pueblo"; return true;
                case "BankerPanel": where = "en el banco"; what = "ha invertido"; return true;
                default: return false;
            }
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.addGold))]
    static class PurchaseNotice
    {
        static void Prefix(int amount)
        {
            if (amount >= 0 || Shared.ApplyingGold || !Net.Juego) return;
            var gui = GUIManager.Instance;
            if (gui == null || !Purchases.Describe(gui.currentInventory, out var what, out var where)) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Nombres.Mio("Jugador " + (Session.MyId + 1))); w.Write(what); w.Write(where); w.Write(-amount);
            Net.Send(Msg.Purchase, ms.ToArray());
        }
    }
}
