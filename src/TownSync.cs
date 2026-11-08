using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// In town, both players are always in the same place: if one leaves the shop, both leave;
    /// if one goes into the shop, both go in.
    public static class TownSync
    {
        public static bool Applying;
        public static int Sent, Applied;

        /// "shop" inside Will's shop, "village" outside (Will wears his armor out there).
        public static string Area()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return "";
            var an = hero.heroMerchantController.characterAnimator;
            return an != null && an.GetBool("IsShopMode") ? "shop" : "village";
        }

        public static void Moved(bool toShop)
        {
            if (Applying || !Net.Connected || Session.TutorialActivo) return;
            Net.Send(Msg.TownArea, new[] { (byte)(toShop ? 1 : 0) });
            Sent++;
            Plugin.Log.LogInfo($"[town] I went {(toShop ? "into the shop" : "out to the village")}: taking my partner");
        }

        public static void OnTownArea(byte[] data)
        {
            if (Session.TutorialActivo) return;   // during the host's tutorial each one stays where it is
            bool toShop = data.Length > 0 && data[0] == 1;
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded() || HeroMerchant.Instance == null) return;
            if ((Area() == "shop") == toShop) return;
            var door = Object.FindObjectOfType<ShopEntranceDoor>();
            if (door == null) return;
            // Close whatever we had open (inventory, a shop panel) before being moved.
            var gui = GUIManager.Instance;
            if (gui != null && gui.currentInventory != null) { gui.AllowPanelChange(); gui.DisableCurrentPanel(); }
            Applying = true;
            try { if (toShop) door.GoToShop(false); else door.GoToVillage(); }
            finally { Applying = false; }
            Applied++;
            Plugin.Log.LogInfo($"[town] my partner went {(toShop ? "into the shop" : "out")}: following");
        }
    }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.GoToVillage))]
    static class TownGoToVillage { static void Postfix() => TownSync.Moved(false); }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.GoToShop))]
    static class TownGoToShop { static void Postfix() => TownSync.Moved(true); }
}
