using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Competitive shop (host setting): every sale counts for whoever put that item on the shelf. When the shop
    /// closes, everybody sees the ranking of the day. Gold is still shared; this is only for bragging rights.
    public static class ShopContest
    {
        private static readonly Dictionary<int, int> _owner = new Dictionary<int, int>();         // shelf slot -> player
        private static readonly List<(string item, int owner, float at)> _taken = new List<(string, int, float)>();
        private static readonly Dictionary<int, int> _sold = new Dictionary<int, int>();          // player -> gold today

        public static void Init() { CashPoint.OnItemSold += OnItemSold; ShopManager.OnShopClose += OnClose; }

        /// A shelf slot changed on this PC (by this player's hands, or a customer on the host).
        public static void SlotChanged(int slot, string before, string now)
        {
            if (!HostSettings.ShopContest || !Net.Juego) return;
            string Item(string key) => string.IsNullOrEmpty(key) ? "" : key.Split('|')[0];
            if (Item(now) != "" && Item(now) != Item(before))
            {
                // Something new on the shelf: it is mine.
                _owner[slot] = Session.MyId;
                var ms = new MemoryStream(); var w = new BinaryWriter(ms);
                w.Write((byte)0); w.Write(slot); w.Write((byte)Session.MyId);
                Net.Send(Msg.ShopOwner, ms.ToArray());
            }
            else if (Item(now) == "" && Item(before) != "" && Session.IsHost && _owner.TryGetValue(slot, out var o))
                _taken.Add((Item(before), o, Time.unscaledTime));   // taken (by a customer, usually)
        }

        public static void OnOwner(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            byte kind = r.ReadByte();
            if (kind == 0) { int slot = r.ReadInt32(); _owner[slot] = r.ReadByte(); }
            else if (kind == 1) { var text = r.ReadString(); Banner.Show(text); Plugin.Log.LogInfo("[contest] " + text); }
        }

        private static void OnItemSold(ItemMaster m, int gold, ItemPriceValoration v)
        {
            if (!HostSettings.ShopContest || !Net.Juego || !Session.IsHost || m == null) return;
            _taken.RemoveAll(t => Time.unscaledTime - t.at > 120f);
            int i = _taken.FindIndex(t => t.item == m.name);
            int owner = i >= 0 ? _taken[i].owner : 0;
            if (i >= 0) _taken.RemoveAt(i);
            _sold.TryGetValue(owner, out var g); _sold[owner] = g + gold;
        }

        private static void OnClose()
        {
            if (!HostSettings.ShopContest || !Net.Juego || !Session.IsHost || _sold.Count == 0) return;
            string Name(int id) => id == 0 ? "Anfitrión" : (Stats.Others.TryGetValue(id, out var o) && o.Name != "" ? o.Name : "Jugador " + (id + 1));
            var rank = _sold.OrderByDescending(kv => kv.Value).ToList();
            string text = "Tienda competitiva — gana " + Name(rank[0].Key) + ": " + string.Join(" · ", rank.Select(kv => $"{Name(kv.Key)} {kv.Value} de oro").ToArray());
            var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write((byte)1); w.Write(text);
            Net.Send(Msg.ShopOwner, ms.ToArray());
            Banner.Show(text);
            Plugin.Log.LogInfo("[contest] " + text);
            _sold.Clear(); _taken.Clear();
        }
    }
}
