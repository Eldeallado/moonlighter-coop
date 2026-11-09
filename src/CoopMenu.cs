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
        private bool _showAch, _showRules, _showGive, _showNews;
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
        private int NavGroup => _showRules || _showGive ? 1 : 0;

        /// Moves the focus to the closest button in that direction (as it looks on screen).
        private void MoveFocus(float dx, float dy)
        {
            if (_lastRects.Count == 0) return;
            if (_focus >= _lastRects.Count) { _focus = 0; return; }
            var fr = _lastRects[_focus];
            Vector2 from = fr.center;
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < _lastRects.Count; i++)
            {
                if (i == _focus) continue;
                var o = _lastRects[i];
                Vector2 d = o.center - from;
                float along = d.x * dx + d.y * dy, across = Mathf.Abs(d.x * dy) + Mathf.Abs(d.y * dx);
                if (along <= 1f) continue;
                // Buttons in line with this one (sharing some of its width for up/down, of its height for
                // left/right) come first, the nearest row or column; then anything within 45 degrees.
                bool inLine = dy != 0 ? o.xMax > fr.xMin && o.xMin < fr.xMax : o.yMax > fr.yMin && o.yMin < fr.yMax;
                float score;
                if (inLine) score = along + across * 0.01f;
                else if (along > across) score = 100000f + along + across * 2f;
                else continue;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) _focus = best;
        }

        private void OpenPanel(ref bool flag)
        {
            bool open = !flag;
            _showAch = _showRules = _showGive = false;
            flag = open;
            if (open) { _tabFocus = _focus; _focus = 0; _listSel = 0; _listSelDrawn = -1; _listScroll = Vector2.zero; }
        }

        private void ClosePanels() { _showAch = _showRules = _showGive = false; _focus = _tabFocus; }

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
            if (_showAch)
            {
                // A list without buttons: up/down go through the achievements, B (or A) goes back.
                if (Input.GetKeyDown(KeyCode.Escape) || PadNav.B || PadNav.A) { ClosePanels(); return; }
                if (PadNav.Up || Input.GetKeyDown(KeyCode.UpArrow)) _listSel = Mathf.Max(0, _listSel - 1);
                if (PadNav.Down || Input.GetKeyDown(KeyCode.DownArrow)) _listSel = Mathf.Min(Achievements.All.Length - 1, _listSel + 1);
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
            Plugin.Role = "host";
            // Steam only by default: opening a port for joining by IP is the host's choice (menu button).
            if (SteamCoop.Available) SteamCoop.Host();
            else Net.StartHost(Plugin.Port);
            Plugin.Log.LogInfo("[menu] create game");
        }

        private void JoinIp()
        {
            Plugin.Role = "join";
            Plugin.TcpMode = true;
            Plugin.JoinAddress = _ip.Trim();
            SteamCoop.Status = "Conectando con " + Plugin.JoinAddress + "…";
            if (HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
            Plugin.Log.LogInfo("[menu] join by IP " + Plugin.JoinAddress);
        }

        private void JoinCode()
        {
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
            float w = 680 * k, h = 520 * k;
            var p = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Ui.Dim();
            Ui.Sheet(p);
            L(new Rect(p.x + 34 * k, p.y + 22 * k, w, 38 * k), "Cooperativo", STitle, Ui.Title);
            L(new Rect(p.xMax - 140 * k, p.y + 26 * k, 106 * k, 26 * k), "versión " + Plugin.Version, SSmall, Ui.InkSoft, TextAnchor.MiddleRight);
            if (Versions.UpdateAvailable && Btn(new Rect(p.x + 200 * k, p.y + 24 * k, 220 * k, 30 * k), "¡Hay versión " + Versions.Latest + "! Descargar")) Application.OpenURL(Versions.ReleasesUrl);
            if (Btn(new Rect(p.xMax - 262 * k, p.y + 24 * k, 116 * k, 30 * k), _showNews || _showAch ? "Volver" : "Novedades")) { if (_showAch) ClosePanels(); else { _showNews = !_showNews; _newsScroll = Vector2.zero; } }
            Ui.Divider(p.x + 34 * k, p.y + 64 * k, w - 68 * k);
            var inner = new Rect(p.x + 34 * k, p.y + 76 * k, w - 68 * k, h - 152 * k);

            bool inGame = Plugin.Role != "" && (Net.Connected || Session.IsHost || SteamCoop.Lobby != Steamworks.CSteamID.Nil);
            if (_showNews) DrawNews(inner, k);
            else if (_showAch) DrawAchievements(inner, k);
            else if (inGame) DrawInGame(inner, k);
            else if (_page == Page.Code) DrawJoinCode(inner, k);
            else if (_page == Page.Ip) DrawJoinIp(inner, k);
            else DrawMain(inner, k);

            if (!string.IsNullOrEmpty(SteamCoop.Status))
                L(new Rect(p.x + 24 * k, p.yMax - 72 * k, w - 48 * k, 22 * k), SteamCoop.Status, SSmall, Ui.Ink, TextAnchor.MiddleCenter);
            string hint = _showAch ? (PadNav.Active ? "Arriba y abajo: ver los logros  ·  B: volver" : "Rueda o flechas para ver los logros  ·  Esc: volver")
                : PadNav.Active ? "Cruceta: moverse  ·  A: aceptar  ·  B: volver  ·  L3+R3: cerrar" : "F2 o Esc para cerrar este menú";
            L(new Rect(p.x + 24 * k, p.yMax - 50 * k, w - 48 * k, 20 * k), hint, SSmall, Ui.InkSoft, TextAnchor.MiddleCenter);
            if (Event.current.type == EventType.Repaint) { _lastRects = new List<Rect>(_rects); if (_focus >= _lastRects.Count) _focus = 0; }
        }

        private bool Btn(Rect r, string text, bool on = false, float size = 16f)
        {
            _btnCount++;
            // Only the buttons of the group the pad is in can take the focus.
            int idx = -1;
            if (_grp == NavGroup) { idx = _rects.Count; _rects.Add(r); }
            bool focused = PadNav.Active && idx >= 0 && idx == _focus && !_showAch && !_showNews;   // those pages scroll instead
            bool pressed = Ui.Button(r, text, focused, on, GUI.enabled, size);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; }
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
            Ui.Bar(new Rect(r.x, r.y + 26 * k, r.width, 10 * k), got / (float)Mathf.Max(1, all.Length), Ui.Gold);

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
                if (done)
                {
                    Ui.Box(b, Ui.Outline);
                    Ui.Box(new Rect(b.x + Ui.U, b.y + Ui.U, b.width - 2 * Ui.U, b.height - 2 * Ui.U), Ui.Gold);
                    Ui.Box(new Rect(b.x + Ui.U, b.y + Ui.U, b.width - 2 * Ui.U, Ui.U), new Color(1f, 1f, 1f, 0.5f));
                    Ui.Check(new Rect(b.x + b.width * 0.18f, b.y + b.height * 0.18f, b.width * 0.64f, b.height * 0.64f), true);
                }
                else
                {
                    Ui.Slot(b);
                    Ui.Label(b, $"{Mathf.FloorToInt(100f * a.Count / a.Goal)}%", SSmall, Ui.Cream, TextAnchor.MiddleCenter);
                }
                // Name and what to do.
                float tx = b.xMax + 12 * k, tw = c.xMax - tx - 156 * k;
                L(new Rect(tx, c.y + 7 * k, tw, 24 * k), a.Name, SBody + 1f, done ? Ui.Title : Ui.Ink);
                L(new Rect(tx, c.y + 30 * k, tw, 20 * k), a.Text, SSmall, Ui.InkSoft);
                // Progress on the right.
                var pb = new Rect(c.xMax - 146 * k, c.y + 14 * k, 132 * k, 10 * k);
                Ui.Bar(pb, a.Count / (float)a.Goal, done ? Ui.Gold : Ui.WoodLight);
                L(new Rect(pb.x, pb.yMax + 3 * k, pb.width, 18 * k), done ? "¡Conseguido!" : a.Count.ToString("N0", Es) + " / " + a.Goal.ToString("N0", Es), SSmall, done ? Ui.Good : Ui.InkSoft, TextAnchor.MiddleCenter);
            }
            Ui.EndScroll();
        }

        private void DrawMain(Rect r, float k)
        {
            float bh = 50 * k, gap = 14 * k, y = r.y + 4 * k;
            L(new Rect(r.x, y, r.width, 26 * k), SteamCoop.Available ? $"Steam: {SteamCoop.MyName}" : "Steam no está abierto: solo se puede jugar por IP", SBody, Ui.Ink, TextAnchor.MiddleCenter);
            y += 36 * k;
            if (Btn(new Rect(r.x + r.width * 0.1f, y, r.width * 0.8f, bh), "Crear partida", false, 20f)) CreateGame();
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
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 48 * k), "Unirse", false, 20f) || enter) JoinCode();
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
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 48 * k), "Conectar", false, 20f) || enter) JoinIp();
            y += 62 * k;
            if (Btn(new Rect(r.x + r.width * 0.35f, y, r.width * 0.3f, 36 * k), "Atrás")) _page = Page.Main;
        }

        private void DrawInGame(Rect r, float k)
        {
            float y = r.y;
            bool host = Session.IsHost;
            L(new Rect(r.x, y, r.width, 26 * k), host ? "Tu partida está abierta" : "Estás en la partida de tu amigo", SHead, Ui.Ink);
            y += 32 * k;
            // With Reglas or Dar equipo open, the panel takes this space (and the pad stays inside it).
            bool panel = _showRules || _showGive;
            if (!panel)
            {
                if (!string.IsNullOrEmpty(SteamCoop.Code))
                {
                    L(new Rect(r.x, y, r.width, 20 * k), "Código para unirse:", SSmall, Ui.InkSoft);
                    var cr = new Rect(r.x, y + 20 * k, r.width * 0.6f, 62 * k);
                    Ui.Slot(cr);
                    Ui.Shadowed(cr, SteamCoop.Code, 40f, Ui.Gold, new Color(0f, 0f, 0f, 0.6f));
                    float bx = cr.xMax + 12 * k, bw = r.width - cr.width - 12 * k;
                    if (Btn(new Rect(bx, cr.y, bw, 28 * k), _copied == SteamCoop.Code && Time.unscaledTime - _copiedAt < 2f ? "¡Copiado!" : "Copiar código"))
                    { GUIUtility.systemCopyBuffer = SteamCoop.Code; _copied = SteamCoop.Code; _copiedAt = Time.unscaledTime; }
                    if (Btn(new Rect(bx, cr.y + 34 * k, bw, 28 * k), "Invitar amigos de Steam")) SteamCoop.Invite();
                    y += 94 * k;
                }
                if (host)
                {
                    // Joining by IP opens a port on this PC: only while the host wants it.
                    if (Btn(new Rect(r.x, y, 250 * k, 28 * k), Net.Listening ? "Unirse por IP: abierto" : "Permitir unirse por IP", Net.Listening)) { if (Net.Listening) Net.StopHost(); else Net.StartHost(Plugin.Port); }
                    if (Net.Listening) L(new Rect(r.x + 262 * k, y, r.width - 262 * k, 28 * k), $"Tu IP en la red: {LocalIp()}", SBody, Ui.Ink);
                    y += 36 * k;
                    if (Btn(new Rect(r.x, y, r.width, 28 * k), "Dificultad: " + Difficulty.Describe(Session.Players))) { Difficulty.Mode = (Difficulty.Mode + 1) % 3; Plugin.SaveDifficulty(Difficulty.Mode); }
                }
                else L(new Rect(r.x, y, r.width, 28 * k), "Dificultad: " + Difficulty.Describe(Session.Players), SBody, Ui.Ink);
                y += 38 * k;
                L(new Rect(r.x, y, r.width, 22 * k), $"Jugadores ({Session.Players}/5)", SHead, Ui.Ink);
                y += 26 * k;
                string me = (SteamCoop.Available ? SteamCoop.MyName : "Tú") + (host ? " (anfitrión, tú)" : " (tú)");
                Player(r, ref y, me, k);
                if (host)
                    foreach (var peer in Net.Live)
                    {
                        float py = y;
                        Player(r, ref y, $"{SteamCoop.PeerName(peer)} (jugador {peer.Id + 1})", k);
                        if (Btn(new Rect(r.xMax - 110 * k, py, 110 * k, 22 * k), "Expulsar")) Kick(peer);
                    }
                else if (Net.Connected) Player(r, ref y, $"{SteamCoop.PeerName(Net.Peer)} (anfitrión)", k);
                if (host && Net.ConnectedCount == 0) L(new Rect(r.x + 18 * k, y, r.width, 22 * k), "Esperando a que se una alguien…", SSmall, Ui.InkFaint);
                // Other mods that differ between players (see Mods.cs).
                foreach (var kv in Mods.Differences)
                {
                    y += 6 * k;
                    L(new Rect(r.x, y, r.width, 40 * k), $"Mods distintos ({(kv.Key == 0 ? "anfitrión" : "jugador " + (kv.Key + 1))}): {kv.Value}", SSmall, Ui.Bad, TextAnchor.UpperLeft, true);
                    y += 40 * k;
                }
            }
            // Options of this player, and the co-op achievements: a row of tabs (left/right on the pad).
            float oy = r.yMax - 84 * k, q4 = (r.width - 30 * k) / 4f;
            if (Btn(new Rect(r.x, oy, q4, 32 * k), "Ver vida: " + (EnemyBars.On ? "sí" : "no"), EnemyBars.On)) EnemyBars.Toggle();
            if (Btn(new Rect(r.x + (q4 + 10 * k), oy, q4, 32 * k), $"Logros ({Achievements.Unlocked}/{Achievements.All.Length})")) OpenPanel(ref _showAch);
            if (Btn(new Rect(r.x + 2 * (q4 + 10 * k), oy, q4, 32 * k), "Reglas", _showRules)) OpenPanel(ref _showRules);
            if (Btn(new Rect(r.x + 3 * (q4 + 10 * k), oy, q4, 32 * k), "Dar equipo", _showGive)) OpenPanel(ref _showGive);
            _grp = 1;
            if (_showGive)
            {
                var ar = new Rect(r.x, r.y + 32 * k, r.width, oy - r.y - 40 * k);
                Ui.Card(ar);
                float ay = ar.y + 10 * k;
                L(new Rect(ar.x + 12 * k, ay, ar.width - 24 * k, 24 * k), "Deja una pieza en la mochila compartida para que la coja otro:", SBody, Ui.Ink);
                ay += 32 * k;
                for (int i = 0; i < Give.Slots.Length; i++)
                {
                    var st = Give.Equipped(i);
                    string label = Give.SlotNames[i] + ": " + (st != null ? Stats.ItemName(st.master) : "—");
                    float gw = (ar.width - 34 * k) / 2f, gx = ar.x + 12 * k + (i % 2) * (gw + 10 * k), gy = ay + (i / 2) * 34 * k;
                    if (st != null) { if (Btn(new Rect(gx, gy, gw, 28 * k), label, false, 14f)) Give.ToBag(i); }
                    else L(new Rect(gx, gy, gw, 28 * k), label, SSmall, Ui.InkFaint, TextAnchor.MiddleCenter);
                }
            }
            if (_showRules)
            {
                var ar = new Rect(r.x, r.y + 32 * k, r.width, oy - r.y - 40 * k);
                Ui.Card(ar);
                float ay = ar.y + 10 * k;
                L(new Rect(ar.x + 12 * k, ay, ar.width - 24 * k, 24 * k), host ? "Reglas (las eliges tú, valen para todos):" : "Reglas que ha elegido el anfitrión:", SBody, Ui.Ink);
                ay += 30 * k;
                for (int i = 0; i < HostSettings.Count; i++)
                {
                    var row = new Rect(ar.x + 12 * k, ay, ar.width - 24 * k, 26 * k);
                    if (host) { if (Btn(new Rect(row.x + 32 * k, row.y, row.width - 32 * k, row.height), HostSettings.Names[i])) HostSettings.Toggle(i); }
                    else L(new Rect(row.x + 32 * k, row.y, row.width - 32 * k, row.height), HostSettings.Names[i], SBody, Ui.Ink);
                    Ui.Check(new Rect(row.x, row.y + 2 * k, 22 * k, 22 * k), HostSettings.Get(i));
                    ay += 30 * k;
                }
                if (HostSettings.Roles)
                {
                    ay += 4 * k;
                    L(new Rect(ar.x + 12 * k, ay, ar.width - 24 * k, 40 * k), "Tu rol: " + Roles.Names[Roles.Mine] + (Roles.Mine > 0 ? " — " + Roles.Texts[Roles.Mine] : "") + " (se elige al entrar en la mazmorra)", SSmall, Ui.InkSoft, TextAnchor.UpperLeft, true);
                }
            }
            _grp = 0;
            float by = r.yMax - 42 * k;
            if (Btn(new Rect(r.x, by, 200 * k, 40 * k), "Color de tu Will (F3)")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
            if (Btn(new Rect(r.x + 210 * k, by, 170 * k, 40 * k), "Desatascarme")) { SetOpen(false); Arreglos.Desatascar(); }
            if (Btn(new Rect(r.xMax - 200 * k, by, 200 * k, 40 * k), host ? "Cerrar la partida" : "Salir de la partida")) LeaveGame();
        }

        /// One line of the player list: a small wax-seal dot and the name.
        private static void Player(Rect r, ref float y, string name, float k)
        {
            Ui.Box(new Rect(r.x + 4 * k, y + 8 * k, 7 * k, 7 * k), Ui.Title);
            L(new Rect(r.x + 18 * k, y, r.width - 140 * k, 22 * k), name, SBody, Ui.Ink);
            y += 25 * k;
        }
    }
}
