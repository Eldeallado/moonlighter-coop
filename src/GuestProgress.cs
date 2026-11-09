using System.Collections.Generic;
using System.IO;
using System.Text;
using FullSerializer;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A guest's own gear. A guest plays on the host's save (and never saves it), so it used to start every time
    /// with a copy of the host's gear. Now its equipped items (weapons, armour, potion, amulets) are kept on its
    /// own PC, one file per host game, and put back when it joins that game again.
    public static class GuestProgress
    {
        private static string FileFor(int seed) => Path.Combine(BepInEx.Paths.ConfigPath, $"MoonlighterCoop-invitado-{seed}.json");
        private static float _next;

        public static void Tick()
        {
            if (!Session.IsGuest || !Net.Juego || !Session.GuestLoaded || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 20f;
            Save();
        }

        public static void Save()
        {
            var hero = HeroMerchant.Instance; var gm = GameManager.Instance;
            if (hero == null || gm == null || gm.currentGameSlot == null || !gm.NotLoadingScene || Session.TutorialActivo) return;
            try
            {
                var inv = hero.heroMerchantInventory;
                var list = new List<ItemSaveInfo>();
                foreach (var it in inv.equippedItems) list.Add(new ItemSaveInfo(it));
                FsJSONManager.Serialize(list, out fsData data);
                File.WriteAllText(FileFor(gm.currentGameSlot.seed), fsJsonPrinter.CompressedJson(data), Encoding.UTF8);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[guest gear] save: " + e.Message); }
        }

        /// Before the guest loads the host's game: its own gear instead of the host's.
        public static void Apply(GameSlot slot)
        {
            if (slot == null) return;
            var f = FileFor(slot.seed);
            if (!File.Exists(f)) return;
            try
            {
                var json = File.ReadAllText(f, Encoding.UTF8);
                if (!SaveGuard.IsSafe(Encoding.UTF8.GetBytes(json), out var why)) { Plugin.Log.LogWarning("[guest gear] refused: " + why); return; }
                var list = new List<ItemSaveInfo>();
                FsJSONManager.Deserialize(json, ref list, assert: false);
                if (list == null || list.Count == 0) return;
                slot.willEquippedItems = list;
                Banner.Show("Recuperado tu equipo de la última vez en esta partida");
                Plugin.Log.LogInfo($"[guest gear] my own gear back: {list.Count} slots");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[guest gear] load: " + e.Message); }
        }
    }
}
