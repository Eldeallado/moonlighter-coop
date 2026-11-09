using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// What each player did: enemies beaten, things picked up, falls, revives, bosses. Every player counts its own
    /// and sends them (Msg.Stats). Shown as a summary when a run ends (back in town) and when a new day starts;
    /// lifetime totals give co-op achievements (kept on this PC).
    public static class Stats
    {
        public const int Kills = 0, Items = 1, Deaths = 2, Revive = 3, Bosses = 4, Gold = 5, N = 6;
        public static readonly string[] Names = { "Enemigos", "Objetos", "Caídas", "Revividos", "Jefes", "Oro" };

        private static readonly int[] _run = new int[N], _day = new int[N];
        public static readonly Dictionary<string, int> RunItems = new Dictionary<string, int>();
        private static bool _dirty;

        /// Other players' numbers: id -> (run, day, items of the run).
        public class Of { public string Name = ""; public int[] Run = new int[N], Day = new int[N]; public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>(); }
        public static readonly Dictionary<int, Of> Others = new Dictionary<int, Of>();

        public static void Count(int what, int n = 1)
        {
            if (!Net.Juego || n <= 0) return;
            _run[what] += n; _day[what] += n;
            Achievements.Add(what, n);
            _dirty = true;
        }

        public static void Picked(ItemMaster m, int qty)
        {
            if (!Net.Juego || m == null || qty <= 0 || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            Count(Items, qty);
            string name = ItemName(m);
            RunItems.TryGetValue(name, out var q); RunItems[name] = q + qty;
        }

        public static string ItemName(ItemMaster m)
        {
            try { var n = DigitalSunGames.Languages.I2.Localization.Get(m.nameKey); if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return m.name;
        }

        // ---------- network ----------

        public static byte[] Bytes()
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Session.MyId);
            w.Write(SteamCoop.Available ? SteamCoop.MyName : (Session.IsHost ? "Anfitrión" : "Jugador " + (Session.MyId + 1)));
            foreach (var x in _run) w.Write(x);
            foreach (var x in _day) w.Write(x);
            var top = RunItems.OrderByDescending(kv => kv.Value).Take(6).ToList();
            w.Write((byte)top.Count);
            foreach (var kv in top) { w.Write(kv.Key.Length > 40 ? kv.Key.Substring(0, 40) : kv.Key); w.Write(kv.Value); }
            return ms.ToArray();
        }

        public static void OnStats(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                int id = r.ReadByte(); if (id == Session.MyId) return;
                var o = new Of { Name = r.ReadString() };
                for (int i = 0; i < N; i++) o.Run[i] = r.ReadInt32();
                for (int i = 0; i < N; i++) o.Day[i] = r.ReadInt32();
                int n = r.ReadByte();
                for (int i = 0; i < n && i < 6; i++) o.Items.Add(new KeyValuePair<string, int>(r.ReadString(), r.ReadInt32()));
                Others[id] = o;
            }
            catch { }
        }

        private static float _nextSend;
        private static string _lastZone = "";
        private static int _lastDay = -1;

        public static void Tick()
        {
            if (!Net.Connected) { Others.Clear(); return; }
            float now = Time.unscaledTime;
            if ((_dirty && now >= _nextSend) || now >= _nextSend + 5f)
            {
                _nextSend = now + 1f; _dirty = false;
                Net.Send(Msg.Stats, Bytes());
            }
            var gm = GameManager.Instance; var hero = HeroMerchant.Instance;
            if (gm == null || hero == null || !gm.NotLoadingScene || gm.currentGameSlot == null) return;
            string zone = Session.LocalZone();
            bool wasDungeon = _lastZone.StartsWith("dungeon"), isTown = zone.StartsWith("town");
            if (zone != "" && zone != _lastZone)
            {
                if (wasDungeon && isTown && !Session.TutorialActivo) { Summary.Show(Summary.Kind.Run); Achievements.RunDone(); }
                if (!wasDungeon && zone.StartsWith("dungeon")) { for (int i = 0; i < N; i++) _run[i] = 0; RunItems.Clear(); _dirty = true; }
                _lastZone = zone;
            }
            int day = gm.currentGameSlot.daysPassed;
            if (_lastDay >= 0 && day > _lastDay && !Session.TutorialActivo) { Summary.Show(Summary.Kind.Day); Achievements.DayDone(); }
            if (day != _lastDay && _lastDay >= 0) { Summary.KeepDay(); for (int i = 0; i < N; i++) _day[i] = 0; _dirty = true; }
            _lastDay = day;
        }


        public static int[] MyRun => _run;
        public static int[] MyDay => _day;

        // ---------- kills: the player whose hit came last (within 4 s) gets the enemy ----------

        private static readonly Dictionary<int, float> _hitAt = new Dictionary<int, float>();
        public static void LocalHit(Enemy e) { if (e != null && !EnemySync.ApplyingRemoteHit) _hitAt[e.GetInstanceID()] = Time.unscaledTime; }
        public static void Died(Enemy e)
        {
            if (e == null || !_hitAt.TryGetValue(e.GetInstanceID(), out var t)) return;
            _hitAt.Remove(e.GetInstanceID());
            if (Time.unscaledTime - t > 4f) return;
            Count(Kills);
            var id = EnemySync.IdOf(e);
            if (id != null && id.StartsWith("boss:")) { Count(Bosses); Achievements.BossDown(Downed.Ghosts.Count == 0 && !Downed.Me); }
        }
    }

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class StatsHits
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Enemy __instance) => Stats.LocalHit(__instance);
    }

    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.Die))]
    static class StatsDeaths
    {
        static void Postfix(EnemyStats __instance) => Stats.Died(__instance.GetComponent<Enemy>());
    }

    /// The summary on screen: per player, after a run (with what each one picked up) or after a day.
    public class Summary : MonoBehaviour
    {
        public enum Kind { Run, Day }
        private static Summary _i;
        private Kind _kind; private float _until;
        private int[] _mine; private Dictionary<string, int> _myItems; private Dictionary<int, Stats.Of> _others;
        private static int[] _dayKept; private static Dictionary<int, Stats.Of> _othersDayKept;
        private GUIStyle _t, _h, _l, _s;

        private void Awake() { _i = this; }

        /// The day counters reset when the day changes: keep a copy for the summary first.
        public static void KeepDay()
        {
            _dayKept = (int[])Stats.MyDay.Clone();
            _othersDayKept = Stats.Others.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static void Show(Kind k)
        {
            if (_i == null) return;
            _i._kind = k; _i._until = Time.unscaledTime + 14f;
            _i._mine = k == Kind.Run ? (int[])Stats.MyRun.Clone() : (_dayKept ?? (int[])Stats.MyDay.Clone());
            _i._myItems = new Dictionary<string, int>(Stats.RunItems);
            _i._others = k == Kind.Run ? Stats.Others.ToDictionary(kv => kv.Key, kv => kv.Value) : (_othersDayKept ?? Stats.Others.ToDictionary(kv => kv.Key, kv => kv.Value));
            Plugin.Log.LogInfo($"[stats] summary {k}: mine {string.Join(",", _i._mine.Select(x => x.ToString()).ToArray())}");
        }

        public static bool Open => _i != null && Time.unscaledTime < _i._until;

        private void Update()
        {
            if (!Open) return;
            PadNav.Poll();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || PadNav.B) _until = 0f;
        }

        private void OnGUI()
        {
            if (!Open) return;
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            if (_t == null)
            {
                _t = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _h = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _l = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                _s = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            }
            _t.fontSize = (int)(24 * k); _h.fontSize = (int)(16 * k); _l.fontSize = (int)(15 * k); _s.fontSize = (int)(12 * k);
            _t.normal.textColor = new Color(1f, 0.85f, 0.45f); _h.normal.textColor = Color.white;
            _l.normal.textColor = new Color(0.9f, 0.9f, 0.9f); _s.normal.textColor = new Color(0.75f, 0.75f, 0.7f);

            var cols = new List<(string name, int[] v, IEnumerable<KeyValuePair<string, int>> items)>();
            cols.Add(((SteamCoop.Available ? SteamCoop.MyName : "Tú") + " (tú)", _mine, _myItems.OrderByDescending(kv => kv.Value).Take(6)));
            foreach (var kv in _others.OrderBy(kv => kv.Key))
                cols.Add((kv.Value.Name, _kind == Kind.Run ? kv.Value.Run : kv.Value.Day, kv.Value.Items));
            bool items = _kind == Kind.Run;
            float colW = 170 * k, w = Mathf.Min(Screen.width - 20, 150 * k + colW * cols.Count), h = (items ? 420 : 300) * k;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(r, new Color(0.09f, 0.07f, 0.06f, 0.94f)); Frame(r, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            GUI.Label(new Rect(r.x, r.y + 10 * k, r.width, 34 * k), _kind == Kind.Run ? "Resumen de la expedición" : "Resumen del día", _t);
            float x0 = r.x + 140 * k, y = r.y + 54 * k;
            for (int c = 0; c < cols.Count; c++) GUI.Label(new Rect(x0 + c * colW, y, colW, 26 * k), cols[c].name, _h);
            y += 32 * k;
            for (int s = 0; s < Stats.N; s++)
            {
                GUI.Label(new Rect(r.x + 10 * k, y, 130 * k, 24 * k), Stats.Names[s], _l);
                int best = 0; foreach (var cc in cols) best = Mathf.Max(best, cc.v[s]);
                for (int c = 0; c < cols.Count; c++)
                {
                    int v = cols[c].v[s];
                    var st = new GUIStyle(_l); if (v > 0 && v == best && cols.Count > 1 && s != Stats.Deaths) st.normal.textColor = new Color(1f, 0.85f, 0.45f);
                    GUI.Label(new Rect(x0 + c * colW, y, colW, 24 * k), v.ToString(), st);
                }
                y += 26 * k;
            }
            if (items)
            {
                y += 6 * k;
                GUI.Label(new Rect(r.x + 10 * k, y, 130 * k, 24 * k), "Ha recogido", _l);
                for (int c = 0; c < cols.Count; c++)
                {
                    var txt = string.Join("\n", cols[c].items.Select(kv => $"{kv.Value} × {kv.Key}").ToArray());
                    GUI.Label(new Rect(x0 + c * colW + 4 * k, y, colW - 8 * k, 150 * k), txt == "" ? "—" : txt, _s);
                }
            }
            GUI.Label(new Rect(r.x, r.yMax - 28 * k, r.width, 22 * k), $"Enter o B para cerrar · se cierra solo en {Mathf.CeilToInt(_until - Time.unscaledTime)} s", _s);
        }

        private static Texture2D _white;
        private static void Fill(Rect r, Color c) { if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); } var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        private static void Frame(Rect r, Color c, float w) { Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c); Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c); }
    }
}
