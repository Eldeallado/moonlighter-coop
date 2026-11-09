using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace MoonlighterCoop
{
    /// F2: the co-op menu. Create a game (you get a code to pass on, and can invite Steam friends), join one
    /// with a friend's code, or join by IP on the same network. Once in a game it shows the code, who is in,
    /// and lets you leave.
    public class CoopMenu : MonoBehaviour
    {
        private bool _showAch, _showRules, _showGive, _showNews, _showHist;
        private Vector2 _newsScroll;
        public static bool Open;
        public static CoopMenu Instance;
        private enum Page { Main, Code, Ip }
        private Page _page;
        private string _code = "", _ip = "192.168.1.";
        private bool _cursorWas;
        private string _copied = "";
        private float _copiedAt;

        private void Awake() { Instance = this; }

        public void SetOpen(bool open)
        {
            if (open == Open) return;
            Open = open;
            var hero = HeroMerchant.Instance;
            var gin = GUIManager.Instance != null ? GUIManager.Instance.input : null;
            if (open)
            {
                _page = Page.Main;
                _openedAt = Time.unscaledTime; Ui.Sound(Ui.SndOpen);
                // First time after updating: start on what is new.
                if (Changelog.Unseen) { _showNews = true; _newsScroll = Vector2.zero; Changelog.MarkSeen(); }
                _cursorWas = Cursor.visible; Cursor.visible = true;
                if (hero != null) { hero.heroMerchantController.DisableMoveInput(); hero.heroMerchantController.DisableAttackInput(); }
                if (gin != null) gin.Enabled = false;   // typing a code must not drive the game's menus
            }
            else
            {
                Cursor.visible = _cursorWas;
                if (gin != null) gin.Enabled = true;
                if (hero != null) { hero.heroMerchantController.EnableMoveInput(); if (!Downed.Me) hero.heroMerchantController.EnableAttackInput(); }
            }
        }

        // Gamepad: which button has the focus, where the focusable buttons were last drawn, A pressed this frame.
        // Buttons belong to a group: 0 = the menu itself, 1 = the panel open on top (Reglas, Dar equipo). While a
        // panel is open the pad only moves inside it; B closes it and puts the focus back on its tab.
        private int _focus, _btnCount, _slot, _grp, _tabFocus;
        private bool _padA;
        private readonly List<Rect> _rects = new List<Rect>();
        private List<Rect> _lastRects = new List<Rect>();
        private float _openedAt;
        private int NavGroup => _showRules || _showGive ? 1 : 0;

        /// Moves the focus to the closest button in that direction (as it looks on screen).
        private void MoveFocus(float dx, float dy)
        {
            int was = _focus;
            _focus = Ui.Nearest(_lastRects, _focus, dx, dy);
            if (_focus != was) Ui.Sound(Ui.SndMove);
        }

        private void OpenPanel(ref bool flag)
        {
            bool open = !flag;
            _showAch = _showRules = _showGive = _showHist = false;
            flag = open;
            if (open) { _tabFocus = _focus; _focus = 0; _listSel = 0; _listSelDrawn = -1; _listScroll = Vector2.zero; }
        }

        private void ClosePanels() { _showAch = _showRules = _showGive = _showHist = false; _focus = _tabFocus; }

        private void Update()
        {
            PadNav.Poll();
            _padA = false;
            if (Input.GetKeyDown(KeyCode.F2) || PadNav.Combo && !ColorMenu.Open) { SetOpen(!Open); _focus = 0; return; }
            if (!Open) return;
            if (_showNews)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || PadNav.B || PadNav.A) _showNews = false;
                if (PadNav.Up) _newsScroll.y -= 80f;
                if (PadNav.Down) _newsScroll.y += 80f;
                _newsScroll.y = Mathf.Max(0f, _newsScroll.y);
                return;
            }
            if (_showAch || _showHist)
            {
                // A list without buttons: up/down go through the achievements, B (or A) goes back.
                if (Input.GetKeyDown(KeyCode.Escape) || PadNav.B || PadNav.A) { ClosePanels(); return; }
                if (PadNav.Up || Input.GetKeyDown(KeyCode.UpArrow)) _listSel = Mathf.Max(0, _listSel - 1);
                if (PadNav.Down || Input.GetKeyDown(KeyCode.DownArrow)) _listSel = Mathf.Min((_showHist ? History.Runs.Count : Achievements.All.Length) - 1, _listSel + 1);
                return;
            }
            if ((_showRules || _showGive) && (Input.GetKeyDown(KeyCode.Escape) || PadNav.B)) { ClosePanels(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) || PadNav.B) { if (_page != Page.Main) { _page = Page.Main; _focus = 0; } else SetOpen(false); return; }
            if (_page == Page.Code && PadNav.Active)
            {
                // Pick the code with the D-pad: left/right choose the letter, up/down change it, A joins.
                if (_code.Length != 7) _code = "AAAAAAA";
                if (PadNav.Left) _slot = (_slot + 6) % 7;
                if (PadNav.Right) _slot = (_slot + 1) % 7;
                if (PadNav.Up || PadNav.Down)
                {
                    var cs = SteamCoop.CodeAlphabet; int i = Mathf.Max(0, cs.IndexOf(_code[_slot]));
                    i = (i + (PadNav.Up ? 1 : cs.Length - 1)) % cs.Length;
                    _code = _code.Substring(0, _slot) + cs[i] + _code.Substring(_slot + 1);
                }
                if (PadNav.A) JoinCode();
                return;
            }
            if (PadNav.Up) MoveFocus(0, -1);
            if (PadNav.Down) MoveFocus(0, 1);
            if (PadNav.Left) MoveFocus(-1, 0);
            if (PadNav.Right) MoveFocus(1, 0);
            if (PadNav.A) _padA = true;
        }

        private static string LocalIp()
        {
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "?";
        }

        // ---------- actions ----------

        // For the QA remote.
        public static void Do(string what, string arg)
        {
            var m = Instance; if (m == null) return;
            if (what == "create") m.CreateGame();
            else if (what == "code") { m._code = arg; m.JoinCode(); }
            else if (what == "ip") { m._ip = arg; m.JoinIp(); }
            else if (what == "leave") m.LeaveGame();
            else if (what == "pad") PadNav.Simulate(arg);
            else if (what == "diff") Difficulty.Mode = int.Parse(arg);
            else if (what == "listen") Net.StartHost(Plugin.Port);
            else if (what == "kick") { var p = Net.Live.FirstOrDefault(x => x.Id.ToString() == arg); if (p != null) m.Kick(p); }
        }

        private void CreateGame()
        {
            Backup.Now("al crear partida");
            Plugin.Role = "host";
            // Steam only by default: opening a port for joining by IP is the host's choice (menu button).
            if (SteamCoop.Available) SteamCoop.Host();
            else Net.StartHost(Plugin.Port);
            Plugin.Log.LogInfo("[menu] create game");
        }

        private void JoinIp()
        {
            Backup.Now("al unirte por IP");
            Plugin.Role = "join";
            Plugin.TcpMode = true;
            Plugin.JoinAddress = _ip.Trim();
            SteamCoop.Status = "Conectando con " + Plugin.JoinAddress + "…";
            if (HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
            Plugin.Log.LogInfo("[menu] join by IP " + Plugin.JoinAddress);
        }

        private void JoinCode()
        {
            Backup.Now("al unirte con código");
            SteamCoop.JoinByCode(_code);
            Plugin.Log.LogInfo("[menu] join by code " + _code);
        }

        private void LeaveGame()
        {
            // Say goodbye first (Steam drops what is still queued when the session closes).
            if (Net.Connected) Net.Send(Msg.Bye, new byte[0]);
            StartCoroutine(FinishLeaving(Session.IsGuest, 0.4f));
            _page = Page.Main;
        }

        /// Host: send a player away, and do not let them back in this session.
        private void Kick(IPeer p)
        {
            SteamCoop.Banned.Add(SteamCoop.BanKey(p));
            p.Send(Msg.Bye, new byte[] { 1 });
            StartCoroutine(CloseLater(p));
            SteamCoop.Status = "Has expulsado a " + SteamCoop.PeerName(p);
            Plugin.Log.LogInfo("[menu] kicked player " + p.Id);
        }
        public static void CloseSoon(IPeer p) { if (Instance != null) Instance.StartCoroutine(Instance.CloseLater(p)); else p.Close(); }
        private System.Collections.IEnumerator CloseLater(IPeer p) { yield return new WaitForSecondsRealtime(0.4f); p.Close(); }

        /// The host closed the game: a guest goes back to its own menu.
        public static void HostClosed() { if (Instance != null) Instance.StartCoroutine(Instance.FinishLeaving(true, 0f)); }

        private System.Collections.IEnumerator FinishLeaving(bool wasGuest, float wait)
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            string status = SteamCoop.Status;
            Net.CloseAll();
            Net.StopHost();
            SteamCoop.Leave();
            Session.Reset();
            Plugin.Role = "";
            Plugin.TcpMode = false;
            if (status.StartsWith("El anfitrión")) SteamCoop.Status = status;   // keep why we left on screen
            Plugin.Log.LogInfo("[menu] left the game");
            // A guest was playing on the host's save: back to the main menu to load its own.
            if (wasGuest && HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
        }
        // ---------- drawing (parchment and wood, see Ui) ----------

        // Text sizes (720p points) and inks.
        private const float STitle = 32f, SHead = 20f, SBody = 17f, SSmall = 14.5f;
        private static void L(Rect r, string t, float size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool wrap = false) => Ui.Label(r, t, size, c, a, wrap);

        private void OnGUI()
        {
            if (!Open) return;
            Ui.Load();
            float k = Ui.K;
            GUI.depth = -1001;
            _btnCount = 0; _grp = 0; _rects.Clear();
            float w = 760 * k, h = 540 * k;
            var p = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Ui.Dim();
            Ui.BeginOpen(_openedAt, p);
            Ui.Sheet(p);
            L(new Rect(p.x + 34 * k, p.y + 22 * k, w, 38 * k), "Cooperativo", STitle, Ui.Ink);
            L(new Rect(p.xMax - 140 * k, p.y + 26 * k, 106 * k, 26 * k), "versión " + Plugin.Version, SSmall, Ui.InkSoft, TextAnchor.MiddleRight);
            if (Versions.UpdateAvailable && Btn(new Rect(p.x + 200 * k, p.y + 24 * k, 220 * k, 30 * k), "¡Hay versión " + Versions.Latest + "! Descargar")) Application.OpenURL(Versions.ReleasesUrl);
            if (Btn(new Rect(p.xMax - 262 * k, p.y + 24 * k, 116 * k, 30 * k), _showNews || _showAch || _showHist ? "Volver" : "Novedades")) { if (_showAch || _showHist) ClosePanels(); else { _showNews = !_showNews; _newsScroll = Vector2.zero; } }
            Ui.Divider(p.x + 34 * k, p.y + 64 * k, w - 68 * k);
            var inner = new Rect(p.x + 34 * k, p.y + 76 * k, w - 68 * k, h - 152 * k);

            bool inGame = Plugin.Role != "" && (Net.Connected || Session.IsHost || SteamCoop.Lobby != Steamworks.CSteamID.Nil);
            if (_showNews) DrawNews(inner, k);
            else if (_showAch) DrawAchievements(inner, k);
            else if (_showHist) DrawHistory(inner, k);
            else if (inGame) DrawInGame(inner, k);
            else if (_page == Page.Code) DrawJoinCode(inner, k);
            else if (_page == Page.Ip) DrawJoinIp(inner, k);
            else DrawMain(inner, k);

            if (!string.IsNullOrEmpty(SteamCoop.Status))
                L(new Rect(p.x + 24 * k, p.yMax - 72 * k, w - 48 * k, 22 * k), SteamCoop.Status, SSmall, Ui.Ink, TextAnchor.MiddleCenter);
            string hint = _showAch || _showHist ? (PadNav.Active ? "Arriba y abajo: ver la lista  ·  B: volver" : "Rueda o flechas para ver la lista  ·  Esc: volver")
                : PadNav.Active ? "Cruceta: moverse  ·  A: aceptar  ·  B: volver  ·  L3+R3: cerrar" : "F2 o Esc para cerrar este menú";
            L(new Rect(p.x + 24 * k, p.yMax - 50 * k, w - 48 * k, 20 * k), hint, SSmall, Ui.InkSoft, TextAnchor.MiddleCenter);
            Ui.EndOpen();
            if (Event.current.type == EventType.Repaint) { _lastRects = new List<Rect>(_rects); if (_focus >= _lastRects.Count) _focus = 0; }
        }

        private bool Btn(Rect r, string text, bool on = false, float size = 16f, Ui.Tone tone = Ui.Tone.Wood, string icon = null)
        {
            _btnCount++;
            // Only the buttons of the group the pad is in can take the focus.
            int idx = -1;
            if (_grp == NavGroup) { idx = _rects.Count; _rects.Add(r); }
            bool focused = PadNav.Active && idx >= 0 && idx == _focus && !_showAch && !_showNews && !_showHist;   // those pages scroll instead
            bool pressed = Ui.Button(r, text, focused, on, GUI.enabled, size, tone, icon);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; Ui.Sound(Ui.SndPress); }
            return pressed;
        }

        /// F2 → Novedades: every version, newest first, with what it brought (CHANGELOG.md inside the DLL).
        private void DrawNews(Rect r, float k)
        {
            float iw = r.width - 20 * k;
            // Measure first so the scroll view knows how tall the list is.
            float total = 0f;
            foreach (var e in Changelog.Entries)
            {
                total += 36 * k;
                foreach (var l in e.Lines) total += Ui.Height(l, SBody, iw, true) + 4 * k;
                total += 12 * k;
            }
            _newsScroll.y = Mathf.Min(_newsScroll.y, Mathf.Max(0f, total - r.height));
            _newsScroll = Ui.BeginScroll(r, _newsScroll, new Rect(0, 0, iw, total));
            float y = 0f;
            foreach (var e in Changelog.Entries)
            {
                Ui.Label(new Rect(0, y, iw, 28 * k), "Versión " + e.Title, SHead, Ui.Title);
                Ui.Box(new Rect(0, y + 29 * k, iw, Ui.U * 0.5f + 0.5f), Ui.InkFaint);
                y += 36 * k;
                foreach (var l in e.Lines)
                {
                    float lh = Ui.Height(l, SBody, iw, true);
                    Ui.Label(new Rect(0, y, iw, lh), l, SBody, Ui.Ink, TextAnchor.UpperLeft, true, true);
                    y += lh + 4 * k;
                }
                y += 12 * k;
            }
            Ui.EndScroll();
        }

        /// F2 → Logros: a page of cards, one per achievement, with its progress bar.
        private static readonly System.Globalization.NumberFormatInfo Es = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." };   // 50.000, as in Spain
        private int _listSel, _listSelDrawn = -1;
        private Vector2 _listScroll;
        private void DrawAchievements(Rect r, float k)
        {
            var all = Achievements.All;
            int got = Achievements.Unlocked;

            // Header: how many, and a bar for the whole set.
            L(new Rect(r.x, r.y - 4 * k, r.width * 0.6f, 26 * k), "Logros del cooperativo", SHead, Ui.Ink);
            L(new Rect(r.xMax - 140 * k, r.y - 4 * k, 140 * k, 26 * k), $"{got} de {all.Length}", SHead, Ui.Title, TextAnchor.MiddleRight);
            Ui.Bar(new Rect(r.x, r.y + 24 * k, r.width, 16 * k), got / (float)Mathf.Max(1, all.Length), Ui.Green);

            // The list.
            float cardH = 58 * k, gap = 7 * k;
            var view = new Rect(r.x, r.y + 46 * k, r.width, r.height - 46 * k);
            float iw = view.width - 14 * k, total = all.Length * (cardH + gap);
            if (_listSel != _listSelDrawn)
            {
                // Keep the chosen card in sight.
                float top = _listSel * (cardH + gap), bottom = top + cardH;
                if (top < _listScroll.y) _listScroll.y = top;
                if (bottom > _listScroll.y + view.height) _listScroll.y = bottom - view.height;
                _listSelDrawn = _listSel;
            }
            _listScroll = Ui.BeginScroll(view, _listScroll, new Rect(0, 0, iw, total));
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                bool done = Achievements.Has(a.Id);
                var c = new Rect(0, i * (cardH + gap), iw, cardH);
                Ui.Card(c, done, PadNav.Active && i == _listSel);
                // Badge: a gold medal with a tick when done, a dark slot with the percentage when not.
                var b = new Rect(c.x + 9 * k, c.y + 9 * k, cardH - 18 * k, cardH - 18 * k);
                // Badge: a green rune mark with a tick when done, an empty ring with the percentage when not.
                Ui.Check(b, done);
                if (!done) Ui.Label(b, $"{Mathf.FloorToInt(100f * a.Count / a.Goal)}%", 13f, Ui.InkSoft, TextAnchor.MiddleCenter);
                // Name and what to do.
                float tx = b.xMax + 12 * k, tw = c.xMax - tx - 156 * k;
                L(new Rect(tx, c.y + 7 * k, tw, 24 * k), a.Name, SBody + 1f, done ? Ui.Title : Ui.Ink);
                L(new Rect(tx, c.y + 30 * k, tw, 20 * k), a.Text, SSmall, Ui.InkSoft);
                // Progress on the right.
                var pb = new Rect(c.xMax - 156 * k, c.y + 8 * k, 142 * k, 18 * k);
                Ui.Bar(pb, a.Count / (float)a.Goal, done ? Ui.Green : Ui.Tan);
                L(new Rect(pb.x, pb.yMax + 3 * k, pb.width, 18 * k), done ? "¡Conseguido!" : a.Count.ToString("N0", Es) + " / " + a.Goal.ToString("N0", Es), SSmall, done ? Ui.Good : Ui.InkSoft, TextAnchor.MiddleCenter);
            }
            Ui.EndScroll();
        }

        /// F2 → Historial: the last runs, newest first.
        private void DrawHistory(Rect r, float k)
        {
            var runs = History.Runs;
            L(new Rect(r.x, r.y - 4 * k, r.width * 0.6f, 26 * k), "Últimas expediciones", SHead, Ui.Ink);
            L(new Rect(r.xMax - 200 * k, r.y - 4 * k, 200 * k, 26 * k), runs.Count == 0 ? "" : $"{runs.Count} guardadas", SSmall, Ui.InkFaint, TextAnchor.MiddleRight);
            if (runs.Count == 0) { L(new Rect(r.x, r.y + 40 * k, r.width, 40 * k), "Aún no hay ninguna. Cuando volváis de una mazmorra aparecerá aquí.", SBody, Ui.InkSoft, TextAnchor.UpperLeft, true); return; }
            float cardH = 58 * k, gap = 7 * k;
            var view = new Rect(r.x, r.y + 30 * k, r.width, r.height - 30 * k);
            float iw = view.width - 14 * k, total = runs.Count * (cardH + gap);
            if (_listSel != _listSelDrawn)
            {
                float top = _listSel * (cardH + gap), bottom = top + cardH;
                if (top < _listScroll.y) _listScroll.y = top;
                if (bottom > _listScroll.y + view.height) _listScroll.y = bottom - view.height;
                _listSelDrawn = _listSel;
            }
            _listScroll = Ui.BeginScroll(view, _listScroll, new Rect(0, 0, iw, total));
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var c = new Rect(0, i * (cardH + gap), iw, cardH);
                Ui.Card(c, i == 0, PadNav.Active && i == _listSel);
                // The floor reached, big, on the left (like the game's floor numbers).
                var fl = new Rect(c.x + 8 * k, c.y + 8 * k, 64 * k, cardH - 16 * k);
                Ui.Slot(fl);
                L(new Rect(fl.x, fl.y + 2 * k, fl.width, 16 * k), "piso", 12f, Ui.InkFaint, TextAnchor.MiddleCenter);
                L(new Rect(fl.x, fl.y + 14 * k, fl.width, fl.height - 14 * k), run.Floor.ToString(), 22f, Ui.Green, TextAnchor.MiddleCenter);
                float tx = fl.xMax + 12 * k;
                L(new Rect(tx, c.y + 6 * k, iw - tx - 150 * k, 22 * k), "Mazmorra de " + run.Dungeon, SBody, Ui.Ink);
                L(new Rect(c.xMax - 160 * k, c.y + 6 * k, 150 * k, 22 * k), run.When, SSmall, Ui.InkFaint, TextAnchor.MiddleRight);
                string fell = string.IsNullOrEmpty(run.Fell) ? "nadie cayó" : "cayó: " + run.Fell;
                L(new Rect(tx, c.y + 30 * k, iw - tx - 10 * k, 20 * k), $"{run.Gold.ToString("N0", Es)} de oro  ·  {run.Kills} enemigos  ·  {run.Players} jugadores  ·  {fell}", SSmall, Ui.InkSoft);
            }
            Ui.EndScroll();
        }

        private void DrawMain(Rect r, float k)
        {
            float bh = 50 * k, gap = 14 * k, y = r.y + 4 * k;
            L(new Rect(r.x, y, r.width, 26 * k), SteamCoop.Available ? $"Steam: {SteamCoop.MyName}" : "Steam no está abierto: solo se puede jugar por IP", SBody, Ui.Ink, TextAnchor.MiddleCenter);
            y += 36 * k;
            if (Btn(new Rect(r.x + r.width * 0.1f, y, r.width * 0.8f, bh), "Crear partida", false, 20f, Ui.Tone.Gold)) CreateGame();
            L(new Rect(r.x, y + bh + 2 * k, r.width, 22 * k), "Te da un código para que tus amigos se unan (hasta 5 jugadores).", SSmall, Ui.InkSoft, TextAnchor.MiddleCenter);
            y += bh + gap + 24 * k;
            GUI.enabled = SteamCoop.Available;
            if (Btn(new Rect(r.x + r.width * 0.1f, y, r.width * 0.8f, bh), "Unirse con un código", false, 20f)) { _page = Page.Code; _code = ""; }
            GUI.enabled = true;
            y += bh + gap;
            if (Btn(new Rect(r.x + r.width * 0.1f, y, r.width * 0.8f, bh), "Unirse por IP (misma red)", false, 20f)) _page = Page.Ip;
            y += bh + gap + 6 * k;
            if (Btn(new Rect(r.x + r.width * 0.25f, y, r.width * 0.5f, 38 * k), "Color de tu Will (F3)")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
        }

        private void DrawJoinCode(Rect r, float k)
        {
            float y = r.y + 6 * k;
            L(new Rect(r.x, y, r.width, 26 * k), "Escribe el código que te ha dado tu amigo:", SHead, Ui.Ink, TextAnchor.MiddleCenter);
            y += 44 * k;
            if (PadNav.Active)
            {
                float bw = 48 * k, gap = 8 * k, x0 = r.x + (r.width - (7 * bw + 6 * gap)) / 2f;
                for (int i = 0; i < 7; i++)
                {
                    var br = new Rect(x0 + i * (bw + gap), y, bw, 58 * k);
                    if (i == _slot) Ui.Box(new Rect(br.x - 2 * Ui.U, br.y - 2 * Ui.U, br.width + 4 * Ui.U, br.height + 4 * Ui.U), Ui.Gold);
                    Ui.Slot(br);
                    L(br, i < _code.Length ? _code[i].ToString() : "", 30f, Ui.Cream, TextAnchor.MiddleCenter);
                }
                L(new Rect(r.x, y + 62 * k, r.width, 20 * k), "Izquierda y derecha: letra  ·  arriba y abajo: cambiarla  ·  A: unirse", SSmall, Ui.InkSoft, TextAnchor.MiddleCenter);
            }
            else
            {
                _code = Ui.TextField(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 58 * k), _code.ToUpperInvariant(), 8, 30f, TextAnchor.MiddleCenter, "code");
                GUI.FocusControl("code");
            }
            y += 88 * k;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 48 * k), "Unirse", false, 20f, Ui.Tone.Gold) || enter) JoinCode();
            y += 62 * k;
            if (Btn(new Rect(r.x + r.width * 0.35f, y, r.width * 0.3f, 36 * k), "Atrás")) _page = Page.Main;
            L(new Rect(r.x, y + 44 * k, r.width, 40 * k), "Al unirte vuelves al menú principal y entras en la partida de tu amigo.", SSmall, Ui.InkSoft, TextAnchor.MiddleCenter, true);
        }

        private void DrawJoinIp(Rect r, float k)
        {
            float y = r.y + 6 * k;
            L(new Rect(r.x, y, r.width, 26 * k), "IP del anfitrión (la ve en su menú F2):", SHead, Ui.Ink, TextAnchor.MiddleCenter);
            y += 44 * k;
            _ip = Ui.TextField(new Rect(r.x + r.width * 0.15f, y, r.width * 0.7f, 58 * k), _ip, 40, 28f, TextAnchor.MiddleCenter, "ip");
            GUI.FocusControl("ip");
            y += 88 * k;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 48 * k), "Conectar", false, 20f, Ui.Tone.Gold) || enter) JoinIp();
            y += 62 * k;
            if (Btn(new Rect(r.x + r.width * 0.35f, y, r.width * 0.3f, 36 * k), "Atrás")) _page = Page.Main;
        }

        /// In a game: two columns. Left, the game itself (code, settings, players) — or the open panel (Reglas,
        /// Dar equipo). Right, the options as a list, like the game's own pause menu, and leaving at the bottom.
        private void DrawInGame(Rect r, float k)
        {
            bool host = Session.IsHost;
            float lw = 420 * k, gap = 24 * k;
            var left = new Rect(r.x, r.y, lw, r.height);
            var right = new Rect(r.x + lw + gap, r.y, r.width - lw - gap, r.height);
            Ui.Box(new Rect(r.x + lw + gap / 2f, r.y + 4 * k, Mathf.Max(1f, Ui.U * 0.5f), r.height - 8 * k), Ui.InkFaint);

            // ----- right: options -----
            float y = right.y, bh = 32 * k, step = 37 * k;
            Ui.Section(new Rect(right.x, y, right.width, 20 * k), "Opciones");
            y += 28 * k;
            if (Btn(new Rect(right.x, y, right.width, bh), $"Logros   {Achievements.Unlocked}/{Achievements.All.Length}", false, 16f, Ui.Tone.Wood, "gold_chest_opening_1")) OpenPanel(ref _showAch);
            y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Historial", false, 16f, Ui.Tone.Wood, "MiniCalendar")) OpenPanel(ref _showHist);
            y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Reglas de la partida", _showRules, 16f, Ui.Tone.Wood, "Calendar_CompletedIcon")) OpenPanel(ref _showRules);
            y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Dar o pedir equipo", _showGive, 16f, Ui.Tone.Wood, "Bag_Emblem")) OpenPanel(ref _showGive);
            y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Aspecto de tu Will", false, 16f, Ui.Tone.Wood, "Bag_Mirror")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
            y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Vida de enemigos: " + (EnemyBars.On ? "sí" : "no"), EnemyBars.On, 16f, Ui.Tone.Wood, "HealthBar_Heart")) EnemyBars.Toggle();
            y += step;
            if (Net.Connected && Btn(new Rect(right.x, y, right.width, bh), "Escribir en el chat", false, 16f, Ui.Tone.Wood, "talk")) { SetOpen(false); Chat.OpenTyping(); }
            if (Net.Connected) y += step;
            if (Btn(new Rect(right.x, y, right.width, bh), "Desatascarme", false, 16f, Ui.Tone.Wood, "GUI_Effect_Icon_Confuse")) { SetOpen(false); Arreglos.Desatascar(); }
            if (Btn(new Rect(right.x, right.yMax - 38 * k, right.width, 38 * k), host ? "Cerrar la partida" : "Salir de la partida", false, 15f, Ui.Tone.Red, "Bag_Pendant")) LeaveGame();

            // ----- left: the open panel, or the game -----
            _grp = 1;
            if (_showRules) { DrawRules(left, k, host); _grp = 0; return; }
            if (_showGive) { DrawGive(left, k); _grp = 0; return; }
            _grp = 0;

            y = left.y;
            L(new Rect(left.x, y, left.width, 26 * k), host ? "Tu partida está abierta" : "Estás en la partida de tu amigo", SHead, Ui.Ink);
            y += 32 * k;
            if (!string.IsNullOrEmpty(SteamCoop.Code))
            {
                Ui.Section(new Rect(left.x, y, left.width, 20 * k), "Código para unirse");
                y += 24 * k;
                var cr = new Rect(left.x, y, 236 * k, 60 * k);
                Ui.Slot(cr);
                Ui.Shadowed(cr, SteamCoop.Code, 38f, Ui.Gold, new Color(0f, 0f, 0f, 0.6f));
                float bx = cr.xMax + 12 * k, bw = left.xMax - bx;
                if (Btn(new Rect(bx, y, bw, 28 * k), _copied == SteamCoop.Code && Time.unscaledTime - _copiedAt < 2f ? "¡Copiado!" : "Copiar código", false, 15f))
                { GUIUtility.systemCopyBuffer = SteamCoop.Code; _copied = SteamCoop.Code; _copiedAt = Time.unscaledTime; }
                if (Btn(new Rect(bx, y + 32 * k, bw, 28 * k), "Invitar de Steam", false, 15f)) SteamCoop.Invite();
                y += 70 * k;
            }
            Ui.Section(new Rect(left.x, y, left.width, 20 * k), "Ajustes");
            y += 26 * k;
            float cw = 230 * k, cx = left.xMax - cw;
            L(new Rect(left.x, y, cx - left.x, 28 * k), "Dificultad", SBody, Ui.Ink);
            if (host) { if (Choose(new Rect(cx, y, cw, 28 * k), Difficulty.Describe(Session.Players))) { Difficulty.Mode = (Difficulty.Mode + 1) % 3; Plugin.SaveDifficulty(Difficulty.Mode); } }
            else L(new Rect(cx, y, cw, 28 * k), Difficulty.Describe(Session.Players), SBody, Ui.InkSoft, TextAnchor.MiddleCenter);
            y += 34 * k;
            if (host)
            {
                // Joining by IP opens a port on this PC: only while the host wants it.
                L(new Rect(left.x, y, cx - left.x, 28 * k), "Unirse por IP", SBody, Ui.Ink);
                if (Choose(new Rect(cx, y, cw, 28 * k), Net.Listening ? "Abierto: " + LocalIp() : "Cerrado")) { if (Net.Listening) Net.StopHost(); else Net.StartHost(Plugin.Port); }
                y += 34 * k;
            }
            y += 4 * k;
            Ui.Section(new Rect(left.x, y, left.width, 20 * k), $"Jugadores  {Session.Players}/5");
            y += 26 * k;
            string me = SteamCoop.Available ? SteamCoop.MyName : "Tú";
            PlayerRow(left, ref y, me, host ? "anfitrión · tú" : "tú", Looks.Mine, k);
            if (host)
                foreach (var peer in Net.Live)
                {
                    float py = y;
                    PlayerRow(left, ref y, SteamCoop.PeerName(peer), "jugador " + (peer.Id + 1), LookOf(peer.Id), k);
                    if (Btn(new Rect(left.xMax - 74 * k, py + 3 * k, 68 * k, 22 * k), "Echar", false, 13f, Ui.Tone.Red)) Kick(peer);
                }
            else if (Net.Connected) PlayerRow(left, ref y, SteamCoop.PeerName(Net.Peer), "anfitrión", LookOf(0), k);
            if (host && Net.ConnectedCount == 0) L(new Rect(left.x + 10 * k, y, left.width, 22 * k), "Esperando a que se una alguien…", SSmall, Ui.InkFaint);
            // Other mods that differ between players (see Mods.cs).
            foreach (var kv in Mods.Differences)
            {
                L(new Rect(left.x, y, left.width, 34 * k), $"Mods distintos ({(kv.Key == 0 ? "anfitrión" : "jugador " + (kv.Key + 1))}): {kv.Value}", SSmall, Ui.Bad, TextAnchor.UpperLeft, true);
                y += 34 * k;
            }
        }

        private static int LookOf(int playerId) => Puppet.All.TryGetValue(playerId, out var p) && p != null ? p.Look : Looks.Default(playerId);

        /// A player: a small card with their vest colour, name and what they are.
        private static void PlayerRow(Rect col, ref float y, string name, string tag, int look, float k)
        {
            var row = new Rect(col.x, y, col.width, 28 * k);
            Ui.Card(row);
            Ui.Swatch(new Rect(row.x + 6 * k, row.y + 5 * k, 18 * k, 18 * k), Looks.Swatch(Looks.Unpack(look)[Looks.Chaleco], Looks.Chaleco));
            L(new Rect(row.x + 32 * k, row.y, row.width - 200 * k, row.height), name, SBody, Ui.Ink);
            L(new Rect(row.xMax - 200 * k, row.y, 116 * k, row.height), tag, SSmall, Ui.InkSoft, TextAnchor.MiddleRight);
            y += 31 * k;
        }

        /// A chooser (value between arrows) that the pad can focus like a button.
        private bool Choose(Rect r, string value)
        {
            _btnCount++;
            int idx = -1;
            if (_grp == NavGroup) { idx = _rects.Count; _rects.Add(r); }
            bool focused = PadNav.Active && idx >= 0 && idx == _focus && !_showAch && !_showNews;
            bool pressed = Ui.Chooser(r, value, focused);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; }
            return pressed;
        }

        /// A whole row you can click (or A): a card, lit when on, with a tick box or a dimmed look when off.
        private bool Row(Rect r, bool lit, bool enabled = true)
        {
            _btnCount++;
            int idx = -1;
            if (enabled && _grp == NavGroup) { idx = _rects.Count; _rects.Add(r); }
            bool focused = PadNav.Active && idx >= 0 && idx == _focus;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Ui.Card(r, lit || hover, focused);
            bool pressed = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; }
            return pressed;
        }

        private void DrawRules(Rect r, float k, bool host)
        {
            L(new Rect(r.x, r.y, r.width, 26 * k), "Reglas de la partida", SHead, Ui.Ink);
            L(new Rect(r.x, r.y + 26 * k, r.width, 20 * k), host ? "Las eliges tú y valen para todos." : "Las ha elegido el anfitrión.", SSmall, Ui.InkSoft);
            float y = r.y + 54 * k;
            for (int i = 0; i < HostSettings.Count; i++)
            {
                var row = new Rect(r.x, y, r.width, 32 * k);
                bool on = HostSettings.Get(i);
                if (Row(row, on, host)) HostSettings.Toggle(i);
                Ui.Check(new Rect(row.x + 8 * k, row.y + 6 * k, 20 * k, 20 * k), on);
                L(new Rect(row.x + 38 * k, row.y, row.width - 44 * k, row.height), HostSettings.Names[i], 14f, on ? Ui.Ink : Ui.InkSoft);
                y += 37 * k;
            }
            if (HostSettings.Roles)
                L(new Rect(r.x, y + 4 * k, r.width, 54 * k), "Tu rol: " + Roles.Names[Roles.Mine] + (Roles.Mine > 0 ? " — " + Roles.Texts[Roles.Mine] : "") + " (se elige al entrar en la mazmorra)", SSmall, Ui.InkSoft, TextAnchor.UpperLeft, true);
        }

        private bool _asking; private int _askTo = -1;

        /// Dar equipo: leave a piece of yours in the shared bag, or (Pedir) ask a partner for one of theirs.
        private void DrawGive(Rect r, float k)
        {
            L(new Rect(r.x, r.y, 160 * k, 26 * k), "Equipo", SHead, Ui.Ink);
            if (Choose(new Rect(r.xMax - 230 * k, r.y, 230 * k, 26 * k), _asking ? "Pedir a un compañero" : "Dar lo mío")) _asking = !_asking;
            var ids = new List<int>(Puppet.All.Keys); ids.Sort();
            if (!_asking)
            {
                L(new Rect(r.x, r.y + 28 * k, r.width, 20 * k), "Deja una pieza en la mochila compartida para que la coja otro.", SSmall, Ui.InkSoft);
                float y = r.y + 54 * k;
                for (int i = 0; i < Give.Slots.Length; i++)
                {
                    var st = Give.Equipped(i);
                    var row = new Rect(r.x, y, r.width, 30 * k);
                    var o = GUI.color; if (st == null) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    if (Row(row, false, st != null)) Give.ToBag(i);
                    L(new Rect(row.x + 10 * k, row.y, 100 * k, row.height), Give.SlotNames[i], SSmall, Ui.InkSoft);
                    L(new Rect(row.x + 110 * k, row.y, row.width - 180 * k, row.height), st != null ? Stats.ItemName(st.master) : "—", 15.5f, Ui.Ink);
                    if (st != null) L(new Rect(row.xMax - 70 * k, row.y, 62 * k, row.height), "Dejar", SSmall, Ui.Title, TextAnchor.MiddleRight);
                    GUI.color = o;
                    y += 33 * k;
                }
                return;
            }
            if (ids.Count == 0) { L(new Rect(r.x, r.y + 40 * k, r.width, 22 * k), "No hay nadie más en la partida.", SBody, Ui.InkSoft); return; }
            if (!ids.Contains(_askTo)) _askTo = ids[0];
            L(new Rect(r.x, r.y + 30 * k, 110 * k, 26 * k), "A quién:", SBody, Ui.Ink);
            if (Choose(new Rect(r.x + 110 * k, r.y + 30 * k, r.width - 110 * k, 26 * k), Give.PlayerName(_askTo))) _askTo = ids[(ids.IndexOf(_askTo) + 1) % ids.Count];
            L(new Rect(r.x, r.y + 58 * k, r.width, 20 * k), "Le saldrá una pregunta y, si dice que sí, lo deja en la mochila compartida.", SSmall, Ui.InkSoft);
            float ay = r.y + 82 * k;
            for (int i = 0; i < Give.Slots.Length; i++)
            {
                var row = new Rect(r.x + (i % 2) * (r.width / 2f + 3 * k), ay + (i / 2) * 33 * k, r.width / 2f - 3 * k, 30 * k);
                if (Row(row, false)) Give.Ask(_askTo, i);
                L(new Rect(row.x + 10 * k, row.y, row.width - 20 * k, row.height), "Pedir " + Give.SlotNames[i].ToLowerInvariant(), 15f, Ui.Ink);
            }
        }
    }
}
