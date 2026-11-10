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
            w.Write(Nombres.Mio("Tu compañero")); w.Write(name);
            Net.Send(Msg.Give, ms.ToArray());
            Banner.Show("Has dejado " + name + " en la mochila compartida");
            Plugin.Log.LogInfo("[give] " + name + " to the shared bag");
        }

        public static void OnGive(byte[] data)
        {
            try { var r = new BinaryReader(new MemoryStream(data)); Banner.Show(r.ReadString() + " te ha dejado " + r.ReadString() + " en la mochila (ábrela y equípalo)"); }
            catch { }
        }

        // ---------- asking a partner for a piece (F2 → Dar equipo → Pedir) ----------

        private static string MyName => Nombres.Mio("Jugador " + (Session.MyId + 1));

        public static void Ask(int to, int slot)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Session.MyId); w.Write((byte)to); w.Write((byte)slot); w.Write(MyName);
            Net.Send(Msg.AskItem, ms.ToArray());
            Banner.Show("Le has pedido " + SlotNames[slot].ToLowerInvariant() + " a " + PlayerName(to));
        }

        public static string PlayerName(int id) => Puppet.All.TryGetValue(id, out var p) && p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "Jugador " + (id + 1);

        /// Someone asks: if it is for me, show the question (or say no straight away if I have nothing there).
        public static void OnAsk(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                int from = r.ReadByte(), to = r.ReadByte(), slot = r.ReadByte(); string name = r.ReadString();
                if (to != Session.MyId || slot >= Slots.Length) return;
                if (Equipped(slot) == null) { Reply(from, slot, 2); return; }
                AskPanel.Open(from, name, slot);
            }
            catch { }
        }

        public static void Reply(int to, int slot, int answer)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Session.MyId); w.Write((byte)to); w.Write((byte)slot); w.Write((byte)answer); w.Write(MyName);
            Net.Send(Msg.AskReply, ms.ToArray());
            if (answer == 1) ToBag(slot);
        }

        public static void OnAskReply(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                r.ReadByte(); int to = r.ReadByte(), slot = r.ReadByte(), answer = r.ReadByte(); string name = r.ReadString();
                if (to != Session.MyId) return;
                string what = SlotNames[slot].ToLowerInvariant();
                if (answer == 0) Banner.Show(name + " prefiere quedarse su " + what);
                else if (answer == 2) Banner.Show(name + " no lleva nada en " + what);
                // answer 1: the "te ha dejado … en la mochila" notice comes with the piece.
            }
            catch { }
        }
    }

    /// The question on the asked player's screen: give it (H / pad up) or keep it (G / pad down), 15 s to answer.
    public class AskPanel : MonoBehaviour
    {
        private static AskPanel _i;
        private int _from, _slot; private string _name; private float _until, _openedAt; private bool _pending;
        private void Awake() { _i = this; }

        public static void Open(int from, string name, int slot)
        {
            if (_i == null) return;
            _i._from = from; _i._name = name; _i._slot = slot; _i._until = Time.unscaledTime + 15f; _i._openedAt = Time.unscaledTime; _i._pending = true;
            Ui.Sound(Ui.SndOpen);
        }

        private bool Showing => _pending;

        private void Update()
        {
            if (!Showing) return;
            if (Time.unscaledTime >= _until) { Answer(0); return; }   // no answer in time: keep it
            PadNav.Poll();
            if (Teclas.Movil) { Teclas.Ofrecer(KeyCode.H, "Dárselo"); Teclas.Ofrecer(KeyCode.G, "No"); }
            if (Teclas.Abajo(KeyCode.H) || PadNav.Up) Answer(1);
            else if (Teclas.Abajo(KeyCode.G) || PadNav.Down) Answer(0);
        }

        private void Answer(int a) { _pending = false; Give.Reply(_from, _slot, a); Ui.Sound(Ui.SndPress); }

        private void OnGUI()
        {
            if (!Showing) return;
            Ui.Load();
            float k = Ui.K;
            float w = 600 * k, h = 118 * k;
            var r = new Rect((Screen.width - w) / 2f, 190 * k, w, h);
            Ui.BeginOpen(_openedAt, r);
            Ui.Scroll(r);
            var st = Give.Equipped(_slot);
            string item = st != null ? Stats.ItemName(st.master) : Give.SlotNames[_slot];
            Ui.Label(new Rect(r.x + 30 * k, r.y + 24 * k, r.width - 60 * k, 46 * k), $"{_name} te pide {item} ({Give.SlotNames[_slot].ToLowerInvariant()})", 18f, Ui.Ink, TextAnchor.MiddleCenter, true);
            Ui.Divider(r.x + 120 * k, r.y + 74 * k, r.width - 240 * k);
            var yes = new Rect(r.x + 90 * k, r.y + 80 * k, 190 * k, 28 * k);
            var no = new Rect(r.xMax - 280 * k, r.y + 80 * k, 190 * k, 28 * k);
            bool pad = PadNav.Active;
            if (Ui.Button(yes, pad ? "Arriba: dárselo" : Teclas.Nombre("H: dárselo", "Dárselo"), false, false, true, 15f, Ui.Tone.Gold)) Answer(1);
            if (Ui.Button(no, pad ? "Abajo: no" : Teclas.Nombre("G: no", "No"), false, false, true, 15f)) Answer(0);
            Ui.Label(new Rect(r.xMax - 60 * k, r.y + 4 * k, 50 * k, 20 * k), Mathf.CeilToInt(_until - Time.unscaledTime) + " s", 13f, Ui.InkFaint, TextAnchor.MiddleRight);
            Ui.EndOpen();
        }
    }
}
