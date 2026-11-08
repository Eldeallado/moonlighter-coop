using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Fixes from playing with a friend (0.4.1).
    public static class Arreglos
    {
        /// F2 → "Desatascarme": close whatever panel is open, unpause, give Will his controls back and put him
        /// next to the host. For any situation where the game left a player stuck.
        public static void Desatascar()
        {
            var gui = GUIManager.Instance; var gm = GameManager.Instance; var hero = HeroMerchant.Instance;
            try { if (gui != null && gui.currentInventory != null) { gui.AllowPanelChange(); gui.DisableCurrentPanel(); } } catch { }
            try { if (gui != null) { gui.canOpenPanels = true; gui.canOpenPause = true; gui.input.Enabled = true; } } catch { }
            try { if (gm != null) { gm.UnpauseGame(); gm.SetDefaultGameSpeed(); } } catch { }
            try { if (gui != null && gui.dungeonLoadingPanel != null && gui.dungeonLoadingPanel.IsActive() && gm != null && gm.NotLoadingScene) gui.dungeonLoadingPanel.Disable(); } catch { }
            if (hero != null)
            {
                var c = hero.heroMerchantController;
                try { c.AllowMovement(); c.EnableMoveInput(); if (!Downed.Me) { c.AllowAttack(); c.EnableAttackInput(); } } catch { }
                try { GUIManager.Instance.FadeIn(null); } catch { }
                // Next to the host (or the first partner) if they are in the same place.
                var p = Puppet.Instance;
                if (p != null && p.Visible)
                {
                    c.TeleportTo(p.transform.position + new Vector3(20f, 0f));
                    try { CameraManager.Instance.TeleportCameraToPoint(hero.gameObject); CameraManager.Instance.SetFollowTarget(); } catch { }
                }
            }
            SteamCoop.Status = "Desatascado";
            Plugin.Log.LogInfo("[fix] unstuck by hand");
        }
    }

    // The tutorial is scripted for one player (its own customers, dialogues that wait for "the" player):
    // it only runs on the host; guests see it through the host and are never held by it.
    [HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.Start), typeof(int))]
    static class TutorialOnlyOnHost
    {
        static bool Prefix()
        {
            if (!Session.IsGuest || !Net.Juego) return true;
            Plugin.Log.LogInfo("[fix] guest: the tutorial runs on the host only");
            return false;
        }
    }

    // A guest never keeps customers of its own (tutorial or quest ones would block its counter): the host's are shown.
    [HarmonyPatch(typeof(VisitorGenerator), nameof(VisitorGenerator.SpawnVisitor))]
    static class GuestNoOwnVisitors
    {
        static void Postfix(VisitorGenerator __instance, Agent __result)
        {
            if (!Session.IsGuest || !Net.Juego || __result == null) return;
            __instance.generatedVisitors.Remove(__result);
            __instance.generatedGameObjects.Remove(__result.gameObject);
            Object.Destroy(__result.gameObject);
            Plugin.Log.LogInfo("[fix] guest: removed a customer of its own");
        }
    }
}
