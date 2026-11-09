using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The shop is everyone's. The host runs it (customers, their choices, what they pay); the guests:
    /// - see the host's customers as copies drawn from their sprites, and the item waiting on the counter;
    /// - can charge a customer at the counter (the sale happens on the host, the gold is shared);
    /// - share every shelf: any change to a slot (item, quantity, price) goes to everyone, slot by slot.
    /// Opening and closing the shop by anyone does it for everyone.
    public static class ShopSync
    {
        public static bool Applying;
        public static int ShelfSent, ShelfApplied, CustomersSent, CustomersApplied, SalesAsked, SalesDone;
        public static bool VisitorAtCash;      // guest: the host has a customer waiting at the counter
        public static string CashItem = "";    // guest: what that customer is buying

        private static bool InTown => Net.Juego && GameManager.Instance != null && GameManager.Instance.IsTownSceneLoaded() && ShopManager.Instance != null;

        // ---------- open / close ----------

        public static void LocalOpenClose(bool open)
        {
            if (Applying || !InTown) return;
            Net.Send(Msg.ShopState, new[] { (byte)(open ? 1 : 0) });
            Plugin.Log.LogInfo($"[shop] I {(open ? "opened" : "closed")} the shop: everyone does");
        }

        public static void OnShopState(byte[] data)
        {
            if (!InTown) return;
            bool open = data[0] == 1;
            var door = ShopManager.Instance.references != null ? ShopManager.Instance.references.shopEntranceDoor : null;
            if (door == null) door = Object.FindObjectOfType<ShopEntranceDoor>();
            if (door == null || door.opened == open) return;
            Applying = true;
            try { if (open) door.OpenShop(); else door.CloseShop(); }
            finally { Applying = false; }
            Plugin.Log.LogInfo($"[shop] partner {(open ? "opened" : "closed")} the shop");
        }

        // ---------- shelves ----------

        private static readonly List<(Showcase sc, int i)> _slots = new List<(Showcase, int)>();
        private static readonly List<string> _known = new List<string>();
        private static float _nextShelfScan, _nextSlotsRefresh;

        private static void RefreshSlots()
        {
            var cases = Object.FindObjectsOfType<Showcase>()
                .OrderBy(s => Mathf.RoundToInt(s.transform.position.x)).ThenBy(s => Mathf.RoundToInt(s.transform.position.y)).ToList();
            int n = 0; foreach (var c in cases) if (c.itemPositions != null) n += c.itemPositions.Count;
            if (n == _slots.Count && _slots.All(s => s.sc != null)) return;
            _slots.Clear(); _known.Clear();
            foreach (var c in cases)
                if (c.itemPositions != null)
                    for (int i = 0; i < c.itemPositions.Count; i++) { _slots.Add((c, i)); _known.Add(null); }
        }

        private static string Key(ItemStack s) =>
            s == null || s.master == null ? "" : $"{s.master.name}|{s.master.plusLevel}|{s.Quantity}|{s.unitSellingPrice}";

        private static bool ShowcasePanelOpen() =>
            GUIManager.Instance != null && GUIManager.Instance.currentInventory != null && GUIManager.Instance.currentInventory is ShowcaseInventoryPanel;

        private static readonly List<byte[]> _pendingShelves = new List<byte[]>();

        public static void Tick()
        {
            if (!InTown) { _slots.Clear(); _known.Clear(); if (_copies.Count > 0 || VisitorAtCash) ClearCopies(); return; }
            float now = Time.unscaledTime;
            if (now >= _nextSlotsRefresh) { _nextSlotsRefresh = now + 2f; RefreshSlots(); }
            // Someone else's shelf changes wait while my own showcase panel is open (it holds those slots).
            if (_pendingShelves.Count > 0 && !ShowcasePanelOpen())
            {
                foreach (var d in _pendingShelves) ApplyShelves(d);
                _pendingShelves.Clear();
            }
            if (now >= _nextShelfScan) { _nextShelfScan = now + 0.25f; ScanShelves(); }
            if (Session.IsHost) HostCustomersTick(now);
            else GuestCashTick();
        }

        private static void ScanShelves()
        {
            if (_slots.Count == 0) return;
            List<(int i, string key, ItemStack s)> changed = null;
            for (int k = 0; k < _slots.Count; k++)
            {
                var (sc, i) = _slots[k];
                if (sc == null) return;
                var st = sc.itemPositions[i].itemStack;
                string key = Key(st);
                if (_known[k] == null) { _known[k] = key; continue; }   // first look: just learn
                if (_known[k] == key) continue;
                string before = _known[k];
                _known[k] = key;
                ShopContest.SlotChanged(k, before, key);
                (changed ??= new List<(int, string, ItemStack)>()).Add((k, key, st));
            }
            if (changed == null) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(_slots.Count);
            w.Write(changed.Count);
            foreach (var c in changed)
            {
                w.Write(c.i);
                bool has = c.key != "";
                w.Write(has);
                if (!has) continue;
                w.Write(c.s.master.name); w.Write(c.s.master.plusLevel); w.Write(c.s.Quantity); w.Write(c.s.unitSellingPrice);
            }
            Net.Send(Msg.Shelves, ms.ToArray());
            ShelfSent++;
            Plugin.Log.LogInfo($"[shop] sent {changed.Count} shelf slot(s)");
        }

        public static void OnShelves(byte[] data)
        {
            if (!InTown) return;
            if (ShowcasePanelOpen()) { _pendingShelves.Add(data); return; }
            ApplyShelves(data);
        }

        private static void ApplyShelves(byte[] data)
        {
            RefreshSlots();
            var r = new BinaryReader(new MemoryStream(data));
            int total = r.ReadInt32();
            if (total != _slots.Count) { Plugin.Log.LogWarning($"[shop] shelf layout differs ({total} vs {_slots.Count})"); }
            int n = r.ReadInt32();
            Applying = true;
            try
            {
                for (int k = 0; k < n; k++)
                {
                    int idx = r.ReadInt32();
                    bool has = r.ReadBoolean();
                    string name = ""; int plus = 0, qty = 0, price = 0;
                    if (has) { name = r.ReadString(); plus = r.ReadInt32(); qty = r.ReadInt32(); price = r.ReadInt32(); }
                    if (idx < 0 || idx >= _slots.Count) continue;
                    var (sc, i) = _slots[idx];
                    var slot = sc.itemPositions[i];
                    var cur = slot.itemStack;
                    if (has && cur != null && cur.master != null && cur.master.name == name && cur.Quantity == qty)
                    {
                        if (cur.unitSellingPrice != price) { cur.unitSellingPrice = price; slot.SetPrice(price * qty); }
                    }
                    else
                    {
                        // (SetItemInPosition(null) leaves the old price tag up: clear it too.)
                        if (cur != null) { sc.SetItemInPosition(null, i); slot.SetPrice(0); Object.Destroy(cur.gameObject); }
                        if (has)
                        {
                            var master = ItemDatabase.GetItemByName(name, plus);
                            if (master != null)
                            {
                                var st = ItemStack.Create(master, qty);
                                st.unitSellingPrice = price;
                                sc.SetItemInPosition(st, i);
                            }
                        }
                    }
                    _known[idx] = Key(slot.itemStack);
                    ShelfApplied++;
                }
            }
            finally { Applying = false; }
        }

        // ---------- customers (host -> guests) ----------

        private static float _nextCustomers;
        private static bool _sentEmpty;
        private static readonly List<SpriteRenderer> _tmp = new List<SpriteRenderer>();

        private static void HostCustomersTick(float now)
        {
            if (now < _nextCustomers) return;
            _nextCustomers = now + 0.12f;
            var gen = ShopManager.Instance.visitorGenerator;
            var visitors = gen != null ? gen.generatedVisitors.Where(v => v != null && v.gameObject.activeInHierarchy).ToList() : new List<Agent>();
            if (visitors.Count == 0 && _sentEmpty) return;
            _sentEmpty = visitors.Count == 0;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            var cash = ShopManager.Instance.references != null ? ShopManager.Instance.references.shopCash : null;
            w.Write(cash != null && cash.visitorBuying != null);
            var cashItem = cash != null ? cash.transform.parent.Find("Item_Cash_Point")?.GetComponentInChildren<ItemStack>() : null;
            w.Write(cashItem != null && cashItem.master != null ? cashItem.master.name : "");
            w.Write(cashItem != null && cashItem.master != null ? cashItem.master.plusLevel : 0);
            w.Write(visitors.Count);
            foreach (var v in visitors)
            {
                var root = v.transform;
                w.Write(v.GetInstanceID());
                w.Write(root.position.x); w.Write(root.position.y);
                // A thief that has stolen something can be caught by rolling into it.
                var catcher = v.GetComponentInChildren<ThiefCatcher>(false);
                var cc = catcher != null ? catcher.GetComponent<Collider2D>() : null;
                w.Write(cc != null && cc.enabled);
                // What the customer carries (its quantity label is UI text, not a sprite).
                var carried = v.GetComponentInChildren<ItemStack>(false);
                w.Write(carried != null && carried.master != null ? carried.master.name : "");
                if (carried != null && carried.master != null)
                {
                    w.Write(carried.master.plusLevel); w.Write(carried.Quantity);
                    var cr = carried.transform.position - root.position; w.Write(cr.x); w.Write(cr.y);
                }
                _tmp.Clear();
                root.GetComponentsInChildren(false, _tmp);
                var parts = _tmp.Where(sr => sr.enabled && sr.sprite != null && sr.color.a > 0.01f).ToList();
                w.Write((short)parts.Count);
                foreach (var sr in parts)
                {
                    var t = sr.transform;
                    var spr = sr.sprite;
                    w.Write(spr.texture != null ? spr.texture.name + "/" + spr.name : spr.name);
                    var rel = t.position - root.position;
                    w.Write(rel.x); w.Write(rel.y);
                    var s = t.lossyScale; w.Write(s.x); w.Write(s.y);
                    w.Write((byte)((sr.flipX ? 1 : 0) | (sr.flipY ? 2 : 0)));
                    Color32 c = sr.color; w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                    w.Write(sr.sortingLayerName); w.Write(sr.sortingOrder);
                    w.Write(sr.sharedMaterial != null ? sr.sharedMaterial.name.Replace(" (Instance)", "") : "");
                }
            }
            Net.Send(Msg.Customers, ms.ToArray());
            CustomersSent++;
        }

        private class Copy { public GameObject go; public List<SpriteRenderer> parts = new List<SpriteRenderer>(); public Vector3 target; public float seen; public bool catchable; public ItemStack label; public string labelKey = ""; }
        private static readonly Dictionary<int, Copy> _copies = new Dictionary<int, Copy>();
        private static GameObject _root;
        private static SpriteRenderer _cashCopy;

        public static void OnCustomers(byte[] data)
        {
            if (!InTown) return;
            if (_root == null) { _root = new GameObject("CoopCustomers"); _root.AddComponent<CustomerMover>(); }
            bool inShop = Session.LocalZone() == "town:shop";
            _root.SetActive(inShop);
            var r = new BinaryReader(new MemoryStream(data));
            VisitorAtCash = r.ReadBoolean();
            CashItem = r.ReadString();
            int cashPlus = r.ReadInt32();
            int n = r.ReadInt32();
            float now = Time.unscaledTime;
            for (int k = 0; k < n; k++)
            {
                int id = r.ReadInt32();
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                if (!_copies.TryGetValue(id, out var cp) || cp.go == null)
                {
                    cp = new Copy { go = new GameObject("CoopCustomer " + id) };
                    cp.go.transform.SetParent(_root.transform, false);
                    cp.go.transform.position = pos;
                    _copies[id] = cp;
                }
                cp.target = pos; cp.seen = now;
                cp.catchable = r.ReadBoolean();
                string carried = r.ReadString();
                int cPlus = 0, cQty = 0; Vector3 cRel = Vector3.zero;
                if (carried != "") { cPlus = r.ReadInt32(); cQty = r.ReadInt32(); cRel = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f); }
                CarriedLabel(cp, carried, cPlus, cQty, cRel);
                int m = r.ReadInt16();
                for (int j = 0; j < m; j++)
                {
                    string sprite = r.ReadString();
                    var rel = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                    var scale = new Vector3(r.ReadSingle(), r.ReadSingle(), 1f);
                    byte flags = r.ReadByte();
                    var col = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    string layer = r.ReadString(); int order = r.ReadInt32(); string mat = r.ReadString();
                    if (j >= cp.parts.Count)
                    {
                        var go = new GameObject("part");
                        go.transform.SetParent(cp.go.transform, false);
                        cp.parts.Add(go.AddComponent<SpriteRenderer>());
                    }
                    var sr = cp.parts[j];
                    sr.gameObject.SetActive(true);
                    sr.sprite = SpriteCache.Get(sprite);
                    var mm = CleanMaterial();
                    if (mm != null && sr.sharedMaterial != mm) sr.sharedMaterial = mm;
                    sr.transform.localPosition = rel;
                    sr.transform.localScale = scale;
                    sr.flipX = (flags & 1) != 0; sr.flipY = (flags & 2) != 0;
                    sr.color = col; sr.sortingLayerName = layer; sr.sortingOrder = order;
                }
                for (int j = m; j < cp.parts.Count; j++) cp.parts[j].gameObject.SetActive(false);
            }
            // Customers that left.
            foreach (var id in _copies.Where(kv => kv.Value.seen < now).Select(kv => kv.Key).ToList())
            {
                if (_copies[id].go != null) Object.Destroy(_copies[id].go);
                _copies.Remove(id);
            }
            ShowCashItem(cashPlus);
            CustomersApplied++;
        }

        // Customers use 'Sprites/Diffuse Flash' (a lookup by name finds some other object's copy, often mid
        // flash: they came out teal) or a 'FakeIdle' shader that needs its own data. Draw them all with our own
        // clean copy of Diffuse Flash, flash off.
        private static Material _clean;
        public static Material CleanMaterial()
        {
            if (_clean != null) return _clean;
            Material src = SpriteCache.GetMaterial("Sprites/Diffuse Flash");
            if (src == null) { var sh = Shader.Find("Sprites/Diffuse Flash"); if (sh != null) src = new Material(sh); }
            if (src == null) return null;
            _clean = new Material(src) { name = "CoopCustomer clean" };
            // That shader's _Color is the flash colour (a teal one was left on the shared asset).
            if (_clean.HasProperty("_FlashAmount")) _clean.SetFloat("_FlashAmount", 0f);
            if (_clean.HasProperty("_Color")) _clean.SetColor("_Color", Color.white);
            return _clean;
        }

        // Only the quantity label of the carried item: its sprite already comes with the customer's sprites.
        private static void CarriedLabel(Copy cp, string name, int plus, int qty, Vector3 rel)
        {
            string key = name == "" || qty <= 1 ? "" : name + "|" + plus + "|" + qty;
            if (key != cp.labelKey) Plugin.Log.LogInfo($"[shop] carried '{name}' x{qty} (was '{cp.labelKey}')");
            if (key != cp.labelKey)
            {
                cp.labelKey = key;
                if (cp.label != null) { Object.Destroy(cp.label.gameObject); cp.label = null; }
                var master = key != "" ? ItemDatabase.GetItemByName(name, plus) : null;
                if (master != null)
                {
                    var st = ItemStack.Create(master, qty);
                    st.DisableCollision();
                    if (st.pickOnWalk != null) st.pickOnWalk.CanPick = false;
                    st.transform.SetParent(cp.go.transform, false);
                    st.Show();
                    // The label hangs under the item's art: hide only the sprites (the customer's copy already draws them).
                    foreach (var sr in st.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
                    if (st.itemShadow != null) st.itemShadow.enabled = false;
                    st.itemQuantityLabel.enabled = true;
                    cp.label = st;
                    Plugin.Log.LogInfo($"[shop] label {name} x{qty}: active={st.gameObject.activeInHierarchy} text='{st.itemQuantityLabel.text}' enabled={st.itemQuantityLabel.enabled}");
                }
            }
            if (cp.label != null) cp.label.transform.localPosition = rel;
        }

        // ---------- thieves: any player can catch them ----------

        private static float _nextCatch;
        public static int CatchesSent, CatchesDone;

        private static void GuestCatchTick()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || Time.unscaledTime < _nextCatch || _copies.Count == 0) return;
            var inv = hero.heroMerchantInventory;
            bool rolling = false;
            try
            {
                var a1 = inv.GetEquippedItemByType(HeroMerchantInventory.EquipmentSlot.Ability1);
                var a2 = inv.GetEquippedItemByType(HeroMerchantInventory.EquipmentSlot.Ability2);
                rolling = (a1 != null && a1.Ability != null && a1.Ability.isInUse) || (a2 != null && a2.Ability != null && a2.Ability.isInUse);
            }
            catch { }
            if (!rolling) return;
            foreach (var kv in _copies)
            {
                if (!kv.Value.catchable || kv.Value.go == null) continue;
                if (((Vector2)(kv.Value.go.transform.position - hero.transform.position)).magnitude > 26f) continue;
                _nextCatch = Time.unscaledTime + 1f;
                var ms = new MemoryStream(); new BinaryWriter(ms).Write(kv.Key);
                Net.Send(Msg.CatchThief, ms.ToArray());
                CatchesSent++;
                Plugin.Log.LogInfo($"[shop] I caught thief {kv.Key} (on the host)");
                return;
            }
        }

        public static void OnCatchThief(byte[] data)
        {
            if (!InTown || !Session.IsHost) return;
            int id = new BinaryReader(new MemoryStream(data)).ReadInt32();
            var v = ShopManager.Instance.visitorGenerator.generatedVisitors.FirstOrDefault(x => x != null && x.GetInstanceID() == id);
            var catcher = v != null ? v.GetComponentInChildren<ThiefCatcher>(false) : null;
            var cc = catcher != null ? catcher.GetComponent<Collider2D>() : null;
            if (catcher == null || cc == null || !cc.enabled) return;
            catcher.CatchThief();
            CatchesDone++;
            Plugin.Log.LogInfo("[shop] a guest caught the thief");
        }

        private static void ShowCashItem(int plus)
        {
            var cash = ShopManager.Instance.references != null ? ShopManager.Instance.references.shopCash : null;
            var at = cash != null ? cash.transform.parent.Find("Item_Cash_Point") : null;
            if (at == null) return;
            if (_cashCopy == null)
            {
                var go = new GameObject("CoopCashItem");
                _cashCopy = go.AddComponent<SpriteRenderer>();
                _cashCopy.sortingLayerName = "UpperDefault";
            }
            _cashCopy.transform.position = at.position + Vector3.up * 2f;
            var master = CashItem != "" ? ItemDatabase.GetItemByName(CashItem, plus) : null;
            _cashCopy.sprite = master != null ? ItemDatabase.GetSprite(master) : null;
            _cashCopy.gameObject.SetActive(master != null && Session.LocalZone() == "town:shop");
        }

        public static void ClearCopies()
        {
            foreach (var cp in _copies.Values) if (cp.go != null) Object.Destroy(cp.go);
            _copies.Clear();
            if (_cashCopy != null) _cashCopy.gameObject.SetActive(false);
            VisitorAtCash = false; CashItem = "";
        }

        private static void GuestCashTick()
        {
            if (_root != null) _root.SetActive(Session.LocalZone() == "town:shop");
            GuestCatchTick();
        }

        public static void MoveCopies(float dt)
        {
            float a = 1f - Mathf.Exp(-15f * dt);
            foreach (var cp in _copies.Values)
                if (cp.go != null) cp.go.transform.position = Vector3.Lerp(cp.go.transform.position, cp.target, a);
        }

        // ---------- charging at the counter ----------

        public static void GuestCharge()
        {
            Net.Send(Msg.ShopSell, new byte[0]);
            SalesAsked++;
            Plugin.Log.LogInfo("[shop] I charge the customer at the counter (on the host)");
        }

        public static void OnShopSell()
        {
            if (!InTown || !Session.IsHost) return;
            var cash = ShopManager.Instance.references.shopCash;
            if (cash == null || cash.visitorBuying == null) return;
            cash.TryToSellItem();
            SalesDone++;
            Plugin.Log.LogInfo("[shop] a guest charged the customer at the counter");
        }
    }

    public class CustomerMover : MonoBehaviour
    {
        private void Update() => ShopSync.MoveCopies(Time.unscaledDeltaTime);
    }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.OpenShop))]
    static class ShopOpenSync { static void Postfix() => ShopSync.LocalOpenClose(true); }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.CloseShop))]
    static class ShopCloseSync { static void Postfix() => ShopSync.LocalOpenClose(false); }

    // Guests never spawn their own customers: they see the host's.
    [HarmonyPatch(typeof(VisitorGenerator), nameof(VisitorGenerator.EnableSpawn))]
    static class GuestNoCustomers
    {
        static void Postfix(VisitorGenerator __instance)
        {
            if (Session.IsGuest && Net.Juego) { __instance.DisableSpawn(); Plugin.Log.LogInfo("[shop] guest: customers come from the host"); }
        }
    }

    // Guests can charge the host's customer waiting at the counter.
    [HarmonyPatch(typeof(CashPoint), nameof(CashPoint.IsInstantActionBlocked))]
    static class GuestCashBlocked
    {
        static void Postfix(ref bool __result)
        {
            if (Session.IsGuest && Net.Juego) __result = !ShopSync.VisitorAtCash;
        }
    }

    [HarmonyPatch(typeof(CashPoint), nameof(CashPoint.TryToSellItem))]
    static class GuestCashSell
    {
        static bool Prefix()
        {
            if (!Session.IsGuest || !Net.Juego) return true;
            if (ShopSync.VisitorAtCash) ShopSync.GuestCharge();
            return false;
        }
    }
    // Day and night are the same for everyone: sleeping (morning) or the day running out (afternoon) on one PC
    // changes it on all of them.
    [HarmonyPatch(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToMorning))]
    static class MorningSync { static void Postfix() => DaySync.Changed(DayTime.Morning); }

    [HarmonyPatch(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToAfternoon))]
    static class AfternoonSync { static void Postfix() => DaySync.Changed(DayTime.Afternoon); }

    public static class DaySync
    {
        public static bool Applying;
        public static void Changed(DayTime t)
        {
            if (Applying || !Net.Juego || GameManager.Instance == null || GameManager.Instance.currentGameSlot == null) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)t); w.Write(GameManager.Instance.currentGameSlot.daysPassed);
            Net.Send(Msg.DayTime, ms.ToArray());
            Plugin.Log.LogInfo($"[day] now {t}: everyone");
        }

        public static void OnDayTime(byte[] data)
        {
            var tm = TimeManagerV2.Instance;
            if (tm == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            var t = (DayTime)r.ReadByte(); int days = r.ReadInt32();
            if (tm.CurrrentDayTime == t) return;
            Applying = true;
            try { if (t == DayTime.Morning) tm.ChangeToMorning(); else tm.ChangeToAfternoon(); }
            finally { Applying = false; }
            Plugin.Log.LogInfo($"[day] partner: now {t} (day {days}, mine {GameManager.Instance.currentGameSlot.daysPassed})");
        }
    }
    /// The end-of-day summary (what was sold, for how much, customers, thieves) is counted where the sales
    /// happen: on the host. Guests wait for the host's summary and show the same one.
    public static class ShopResults
    {
        public static bool Allow;
        public static int Sent, Shown;
        private static readonly string[] Ints = { "totalSalesBoxGoldThisRun", "totalHelperGoldThisRun", "customersThisRun", "customersBoughtThisRun", "thievesThisRun", "questRewardsThisRun" };
        private static readonly string[] IntLists = { "itemsSoldQuantityThisRun", "itemsSoldPriceThisRun" };
        private static readonly string[] ItemLists = { "itemsSoldThisRun", "salesBoxItemsSoldThisRun", "helperItemsSoldThisRun", "robbedItemsThisRun" };

        public static void HostSend()
        {
            var hs = HeroStatistics.Instance;
            if (hs == null || !Net.Juego) return;
            var tr = Traverse.Create(hs);
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            foreach (var n in Ints) w.Write(tr.Field(n).GetValue<int>());
            foreach (var n in IntLists) { var l = tr.Field(n).GetValue<List<int>>() ?? new List<int>(); w.Write(l.Count); foreach (var x in l) w.Write(x); }
            foreach (var n in ItemLists)
            {
                var l = tr.Field(n).GetValue<List<ItemMaster>>() ?? new List<ItemMaster>();
                w.Write(l.Count); foreach (var m in l) { w.Write(m != null ? m.name : ""); w.Write(m != null ? m.plusLevel : 0); }
            }
            var q = hs.questRewardItem; w.Write(q != null ? q.name : ""); w.Write(q != null ? q.plusLevel : 0);
            Net.Send(Msg.ShopResults, ms.ToArray());
            Sent++;
            Plugin.Log.LogInfo("[shop] sent the day's summary");
        }

        public static void OnResults(byte[] data)
        {
            var hs = HeroStatistics.Instance;
            if (hs == null || ShopManager.Instance == null) return;
            var tr = Traverse.Create(hs);
            var r = new BinaryReader(new MemoryStream(data));
            foreach (var n in Ints) tr.Field(n).SetValue(r.ReadInt32());
            foreach (var n in IntLists) { int c = r.ReadInt32(); var l = new List<int>(); for (int i = 0; i < c; i++) l.Add(r.ReadInt32()); tr.Field(n).SetValue(l); }
            foreach (var n in ItemLists)
            {
                int c = r.ReadInt32(); var l = new List<ItemMaster>();
                for (int i = 0; i < c; i++) { string name = r.ReadString(); int plus = r.ReadInt32(); var m = name != "" ? ItemDatabase.GetItemByName(name, plus) : null; if (m != null) l.Add(m); }
                tr.Field(n).SetValue(l);
            }
            string qn = r.ReadString(); int qp = r.ReadInt32();
            hs.questRewardItem = qn != "" ? ItemDatabase.GetItemByName(qn, qp) : null;
            Allow = true;
            try { Traverse.Create(ShopManager.Instance).Method("ShowShopResultsAndClose").GetValue(); }
            finally { Allow = false; }
            Shown++;
            Plugin.Log.LogInfo("[shop] showing the host's summary of the day");
        }
    }

    [HarmonyPatch(typeof(ShopManager), "ShowShopResultsAndClose")]
    static class ShopResultsSync
    {
        static bool Prefix()
        {
            if (Session.IsGuest && Net.Juego && !ShopResults.Allow)
            {
                Plugin.Log.LogInfo("[shop] day over here: waiting for the host's summary");
                return false;
            }
            return true;
        }
        static void Postfix() { if (Session.IsHost) ShopResults.HostSend(); }
    }
}