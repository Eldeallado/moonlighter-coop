using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoonlighterCoop
{
    /// Other BepInEx mods: every player sends the list of the ones it runs (and the size of its bag). Mods that
    /// change stacks or the bag (StackIt, Stack 999, Bigger Item Stacks, More Inventory...) must be the same for
    /// everyone, because the bag is shared: if they differ, everybody is told who has what.
    public static class Mods
    {
        // Mods that change nothing the others can see.
        private static readonly string[] Harmless = { "configurationmanager", "unityexplorer", "scriptengine" };
        // Words that give away a mod touching items, stacks or the bag.
        private static readonly string[] BagWords = { "stack", "inventory", "inventario", "bag", "slot", "item", "chest" };

        /// "guid|name|version" of every plugin but this one, plus "#bag=N" (slots in Will's bag).
        public static List<string> Mine()
        {
            var l = new List<string>();
            try
            {
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    var m = kv.Value.Metadata;
                    if (m.GUID == Plugin.Guid || Harmless.Any(h => m.GUID.ToLowerInvariant().Contains(h))) continue;
                    l.Add($"{m.GUID}|{m.Name}|{m.Version}");
                }
            }
            catch { }
            var inv = HeroMerchant.Instance != null ? HeroMerchant.Instance.heroMerchantInventory : null;
            if (inv != null && inv.slots != null) l.Add("#bag=" + inv.slots.Length);
            return l;
        }

        public static byte[] ListBytes()
        {
            var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
            w.Write((byte)Session.MyId);
            var l = Mine(); w.Write((short)l.Count);
            foreach (var s in l) w.Write(s.Length > 200 ? s.Substring(0, 200) : s);
            return ms.ToArray();
        }

        /// What differs, per player number (shown in the F2 menu).
        public static readonly Dictionary<int, string> Differences = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _lastShown = new Dictionary<int, string>();

        public static void OnList(IPeer from, byte[] data)
        {
            List<string> theirs; int id;
            try
            {
                var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
                id = r.ReadByte(); int n = r.ReadInt16();
                if (n < 0 || n > 200) return;
                theirs = new List<string>();
                for (int i = 0; i < n; i++) theirs.Add(r.ReadString());
            }
            catch { return; }
            if (id == Session.MyId) return;
            var mine = Mine();
            string Name(string s) => s.StartsWith("#") ? s : s.Split('|')[1] + " " + s.Split('|')[2];
            string Guid(string s) => s.Split('|')[0];
            var onlyThem = theirs.Where(t => !t.StartsWith("#") && !mine.Any(m => Guid(m) == Guid(t))).ToList();
            var onlyMe = mine.Where(m => !m.StartsWith("#") && !theirs.Any(t => Guid(t) == Guid(m))).ToList();
            var otherVersion = theirs.Where(t => !t.StartsWith("#") && mine.Any(m => Guid(m) == Guid(t) && m != t)).ToList();
            string myBag = mine.FirstOrDefault(m => m.StartsWith("#bag=")), theirBag = theirs.FirstOrDefault(t => t.StartsWith("#bag="));

            var sb = new StringBuilder();
            if (onlyThem.Count > 0) sb.Append("Solo él tiene: " + string.Join(", ", onlyThem.Select(Name).ToArray()) + ". ");
            if (onlyMe.Count > 0) sb.Append("Solo tú tienes: " + string.Join(", ", onlyMe.Select(Name).ToArray()) + ". ");
            if (otherVersion.Count > 0) sb.Append("Otra versión: " + string.Join(", ", otherVersion.Select(Name).ToArray()) + ". ");
            bool bagDiffers = myBag != null && theirBag != null && myBag != theirBag;
            if (bagDiffers) sb.Append($"Mochila de distinto tamaño ({myBag.Substring(5)} tú, {theirBag.Substring(5)} él). ");
            string text = sb.ToString().Trim();
            if (text == "") Differences.Remove(id); else Differences[id] = text;

            // Tell once per change; louder if it touches the shared bag.
            _lastShown.TryGetValue(id, out var last);
            if (text == "" || text == last) return;
            _lastShown[id] = text;
            bool risky = bagDiffers || onlyThem.Concat(onlyMe).Concat(otherVersion).Any(s => BagWords.Any(w => s.ToLowerInvariant().Contains(w)));
            string who = id == 0 ? "El anfitrión" : "El jugador " + (id + 1);   // (relayed lists come through the host)
            Banner.Show(risky
                ? $"{who} tiene otros mods de objetos o mochila: usad los mismos o la mochila compartida puede perder cosas (mira " + Teclas.Nombre("F2", "el menú Coop") + ")"
                : $"{who} usa otros mods (mira " + Teclas.Nombre("F2", "el menú Coop") + ")");
            Plugin.Log.LogWarning($"[mods] player {id}: {text}");
        }
    }
}
