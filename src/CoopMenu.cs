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
        private Texture2D _white;
        private GUIStyle _title, _head, _label, _small, _big, _button, _field;

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
        // ---------- drawing ----------

        private void Styles(float k)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _label = new GUIStyle(GUI.skin.label) { wordWrap = true };
                _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _button = new GUIStyle(GUI.skin.button);
                _field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter };
            }
            _title.fontSize = (int)(26 * k); _head.fontSize = (int)(18 * k); _label.fontSize = (int)(15 * k);
            _small.fontSize = (int)(13 * k); _big.fontSize = (int)(46 * k); _button.fontSize = (int)(16 * k); _field.fontSize = (int)(24 * k);
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _head.normal.textColor = Color.white; _label.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
            _small.normal.textColor = new Color(0.7f, 0.7f, 0.7f); _big.normal.textColor = new Color(1f, 0.9f, 0.55f);
        }

        private void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        private void Frame(Rect r, Color c, float w) { Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c); Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c); }

        private void OnGUI()
        {
            if (!Open) return;
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            Styles(k);
            GUI.depth = -1001;
            _btnCount = 0; _grp = 0; _rects.Clear();
            float w = 640 * k, h = 470 * k;
            var p = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.4f));
            Fill(p, new Color(0.09f, 0.07f, 0.06f, 0.96f));
            Frame(p, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            Frame(new Rect(p.x + 6 * k, p.y + 6 * k, p.width - 12 * k, p.height - 12 * k), new Color(0.3f, 0.22f, 0.12f), 1 * k);
            GUI.Label(new Rect(p.x + 24 * k, p.y + 14 * k, w, 36 * k), "Cooperativo", _title);
            GUI.Label(new Rect(p.xMax - 125 * k, p.y + 22 * k, 105 * k, 22 * k), "versión " + Plugin.Version, _small);
            if (Versions.UpdateAvailable && Btn(new Rect(p.x + 180 * k, p.y + 18 * k, 212 * k, 28 * k), "¡Hay versión " + Versions.Latest + "! Descargar")) Application.OpenURL(Versions.ReleasesUrl);
            if (Btn(new Rect(p.xMax - 240 * k, p.y + 18 * k, 110 * k, 28 * k), _showNews || _showAch ? "Volver" : "Novedades")) { if (_showAch) ClosePanels(); else { _showNews = !_showNews; _newsScroll = Vector2.zero; } }
            var inner = new Rect(p.x + 30 * k, p.y + 60 * k, w - 60 * k, h - 110 * k);

            bool inGame = Plugin.Role != "" && (Net.Connected || Session.IsHost || SteamCoop.Lobby != Steamworks.CSteamID.Nil);
            if (_showNews) DrawNews(inner, k);
            else if (_showAch) DrawAchievements(inner, k);
            else if (inGame) DrawInGame(inner, k);
            else if (_page == Page.Code) DrawJoinCode(inner, k);
            else if (_page == Page.Ip) DrawJoinIp(inner, k);
            else DrawMain(inner, k);

            if (!string.IsNullOrEmpty(SteamCoop.Status))
                GUI.Label(new Rect(p.x + 20 * k, p.yMax - 64 * k, w - 40 * k, 26 * k), SteamCoop.Status, _small);
            string hint = _showAch ? (PadNav.Active ? "↑ ↓: ver los logros · B: volver" : "Rueda o ↑ ↓ para ver los logros · Esc: volver")
                : PadNav.Active ? "Cruceta: moverse · A: aceptar · B: volver · L3+R3: cerrar" : "F2 o Esc para cerrar este menú";
            GUI.Label(new Rect(p.x + 20 * k, p.yMax - 36 * k, w - 40 * k, 24 * k), hint, _small);
            if (Event.current.type == EventType.Repaint) { _lastRects = new List<Rect>(_rects); if (_focus >= _lastRects.Count) _focus = 0; }
        }

        private bool Btn(Rect r, string text)
        {
            _btnCount++;
            // Only the buttons of the group the pad is in can take the focus.
            int idx = -1;
            if (_grp == NavGroup) { idx = _rects.Count; _rects.Add(r); }
            bool focused = PadNav.Active && idx >= 0 && idx == _focus && !_showAch && !_showNews;   // those pages scroll instead
            if (focused) Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.85f, 0.45f), 3);
            bool pressed = GUI.Button(r, text, _button);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; }
            return pressed;
        }

        /// F2 → Novedades: every version, newest first, with what it brought (CHANGELOG.md inside the DLL).
        private void DrawNews(Rect r, float k)
        {
            if (_newsText == null) { _newsText = new GUIStyle(_label) { richText = true }; _newsHead = new GUIStyle(_head) { richText = true }; }
            _newsText.fontSize = _label.fontSize; _newsHead.fontSize = _head.fontSize;
            _newsHead.normal.textColor = new Color(1f, 0.85f, 0.45f);
            float iw = r.width - 24 * k;
            // Measure first so the scroll view knows how tall the list is.
            float total = 0f;
            foreach (var e in Changelog.Entries)
            {
                total += 30 * k;
                foreach (var l in e.Lines) total += _newsText.CalcHeight(new GUIContent(l), iw) + 3 * k;
                total += 10 * k;
            }
            _newsScroll.y = Mathf.Min(_newsScroll.y, Mathf.Max(0f, total - r.height));
            _newsScroll = GUI.BeginScrollView(r, _newsScroll, new Rect(0, 0, iw, total));
            float y = 0f;
            foreach (var e in Changelog.Entries)
            {
                GUI.Label(new Rect(0, y, iw, 28 * k), "Versión " + e.Title, _newsHead);
                y += 30 * k;
                foreach (var l in e.Lines)
                {
                    float lh = _newsText.CalcHeight(new GUIContent(l), iw);
                    GUI.Label(new Rect(0, y, iw, lh), l, _newsText);
                    y += lh + 3 * k;
                }
                y += 10 * k;
            }
            GUI.EndScrollView();
        }
        private GUIStyle _newsText, _newsHead;

        /// F2 → Logros: a page of cards, one per achievement, with its progress bar.
        private static readonly System.Globalization.NumberFormatInfo Es = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." };   // 50.000, as in Spain
        private int _listSel, _listSelDrawn = -1;
        private Vector2 _listScroll;
        private GUIStyle _achName, _achText, _achCount, _badge;
        private void DrawAchievements(Rect r, float k)
        {
            if (_achName == null)
            {
                _achName = new GUIStyle(_head) { alignment = TextAnchor.MiddleLeft };
                _achText = new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
                _achCount = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _badge = new GUIStyle(_head) { alignment = TextAnchor.MiddleCenter };
            }
            _achName.fontSize = (int)(16 * k); _achText.fontSize = (int)(13 * k); _achCount.fontSize = (int)(11 * k);
            var gold = new Color(1f, 0.85f, 0.45f);
            var all = Achievements.All;
            int got = Achievements.Unlocked;

            // Header: how many, and a bar for the whole set.
            GUI.Label(new Rect(r.x, r.y - 4 * k, r.width * 0.6f, 26 * k), "Logros del cooperativo", _head);
            _achCount.normal.textColor = gold; _achCount.fontSize = (int)(15 * k);
            GUI.Label(new Rect(r.xMax - 120 * k, r.y - 4 * k, 120 * k, 26 * k), $"{got} de {all.Length}", _achCount);
            _achCount.fontSize = (int)(11 * k);
            var hb = new Rect(r.x, r.y + 24 * k, r.width, 6 * k);
            Fill(hb, new Color(0.2f, 0.15f, 0.1f));
            Fill(new Rect(hb.x, hb.y, hb.width * got / Mathf.Max(1, all.Length), hb.height), gold);

            // The list.
            float cardH = 54 * k, gap = 6 * k;
            var view = new Rect(r.x, r.y + 38 * k, r.width, r.height - 54 * k);
            float iw = view.width - 16 * k, total = all.Length * (cardH + gap);
            if (_listSel != _listSelDrawn)
            {
                // Keep the chosen card in sight.
                float top = _listSel * (cardH + gap), bottom = top + cardH;
                if (top < _listScroll.y) _listScroll.y = top;
                if (bottom > _listScroll.y + view.height) _listScroll.y = bottom - view.height;
                _listSelDrawn = _listSel;
            }
            _listScroll = GUI.BeginScrollView(view, _listScroll, new Rect(0, 0, iw, total));
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                bool done = Achievements.Has(a.Id);
                var c = new Rect(0, i * (cardH + gap), iw, cardH);
                Fill(c, done ? new Color(0.22f, 0.16f, 0.08f) : new Color(0.12f, 0.1f, 0.08f));
                bool sel = PadNav.Active && i == _listSel;
                Frame(c, sel ? gold : done ? new Color(0.62f, 0.45f, 0.22f) : new Color(0.25f, 0.2f, 0.15f), (sel ? 2f : 1f) * k);
                // Badge: gold with a tick when done, dark with the percentage when not.
                var b = new Rect(c.x + 9 * k, c.y + 9 * k, cardH - 18 * k, cardH - 18 * k);
                Fill(b, done ? gold : new Color(0.2f, 0.16f, 0.12f));
                Frame(b, done ? new Color(0.5f, 0.33f, 0.1f) : new Color(0.35f, 0.28f, 0.2f), 2 * k);
                _badge.normal.textColor = done ? new Color(0.3f, 0.18f, 0.05f) : new Color(0.6f, 0.53f, 0.45f);
                _badge.fontSize = (int)((done ? 20 : 11) * k);
                GUI.Label(b, done ? "✔" : $"{Mathf.FloorToInt(100f * a.Count / a.Goal)}%", _badge);
                // Name and what to do.
                float tx = b.xMax + 12 * k, tw = c.xMax - tx - 150 * k;
                _achName.normal.textColor = done ? gold : new Color(0.85f, 0.82f, 0.78f);
                _achText.normal.textColor = done ? new Color(0.85f, 0.78f, 0.62f) : new Color(0.6f, 0.57f, 0.53f);
                GUI.Label(new Rect(tx, c.y + 6 * k, tw, 22 * k), a.Name, _achName);
                GUI.Label(new Rect(tx, c.y + 27 * k, tw, 20 * k), a.Text, _achText);
                // Progress on the right.
                var pb = new Rect(c.xMax - 140 * k, c.y + 14 * k, 128 * k, 8 * k);
                Fill(pb, new Color(0.07f, 0.06f, 0.05f));
                Fill(new Rect(pb.x, pb.y, pb.width * a.Count / a.Goal, pb.height), done ? gold : new Color(0.55f, 0.42f, 0.22f));
                _achCount.normal.textColor = done ? gold : new Color(0.65f, 0.6f, 0.55f);
                GUI.Label(new Rect(pb.x, pb.yMax + 2 * k, pb.width, 16 * k), done ? "¡Conseguido!" : a.Count.ToString("N0", Es) + " / " + a.Goal.ToString("N0", Es), _achCount);
            }
            GUI.EndScrollView();
        }

        private void DrawMain(Rect r, float k)
        {
            float bh = 54 * k, gap = 16 * k, y = r.y + 10 * k;
            GUI.Label(new Rect(r.x, y, r.width, 26 * k), SteamCoop.Available ? $"Steam: {SteamCoop.MyName}" : "Steam no está abierto: solo se puede jugar por IP", _label);
            y += 40 * k;
            if (Btn(new Rect(r.x, y, r.width, bh), "Crear partida")) CreateGame();
            GUI.Label(new Rect(r.x, y + bh, r.width, 22 * k), "Te da un código para que tus amigos se unan (hasta 5 jugadores).", _small);
            y += bh + gap + 22 * k;
            GUI.enabled = SteamCoop.Available;
            if (Btn(new Rect(r.x, y, r.width, bh), "Unirse con un código")) { _page = Page.Code; _code = ""; }
            GUI.enabled = true;
            y += bh + gap;
            if (Btn(new Rect(r.x, y, r.width, bh), "Unirse por IP (misma red)")) _page = Page.Ip;
            y += bh + gap;
            if (Btn(new Rect(r.x, y, r.width, 38 * k), "Color de tu Will (F3)")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
        }

        private void DrawJoinCode(Rect r, float k)
        {
            float y = r.y + 10 * k;
            GUI.Label(new Rect(r.x, y, r.width, 26 * k), "Escribe el código que te ha dado tu amigo:", _head);
            y += 44 * k;
            if (PadNav.Active)
            {
                float bw = 44 * k, gap = 8 * k, x0 = r.x + (r.width - (7 * bw + 6 * gap)) / 2f;
                for (int i = 0; i < 7; i++)
                {
                    var br = new Rect(x0 + i * (bw + gap), y, bw, 56 * k);
                    Fill(br, new Color(0.03f, 0.03f, 0.03f));
                    Frame(br, i == _slot ? new Color(1f, 0.85f, 0.45f) : new Color(0.4f, 0.32f, 0.2f), (i == _slot ? 3 : 1) * k);
                    GUI.Label(br, i < _code.Length ? _code[i].ToString() : "", _field);
                }
                GUI.Label(new Rect(r.x, y + 58 * k, r.width, 18 * k), "← → letra · ↑ ↓ cambiar · A unirse", _small);
            }
            else
            {
                GUI.SetNextControlName("code");
                _code = GUI.TextField(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 56 * k), _code.ToUpperInvariant(), 8, _field);
                GUI.FocusControl("code");
            }
            y += 76 * k;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 50 * k), "Unirse") || enter) JoinCode();
            y += 66 * k;
            if (Btn(new Rect(r.x + r.width * 0.35f, y, r.width * 0.3f, 38 * k), "Atrás")) _page = Page.Main;
            GUI.Label(new Rect(r.x, y + 46 * k, r.width, 40 * k), "Al unirte vuelves al menú principal y entras en la partida de tu amigo.", _small);
        }

        private void DrawJoinIp(Rect r, float k)
        {
            float y = r.y + 10 * k;
            GUI.Label(new Rect(r.x, y, r.width, 26 * k), "IP del anfitrión (la ve en su menú F2):", _head);
            y += 44 * k;
            GUI.SetNextControlName("ip");
            _ip = GUI.TextField(new Rect(r.x + r.width * 0.15f, y, r.width * 0.7f, 56 * k), _ip, 40, _field);
            GUI.FocusControl("ip");
            y += 76 * k;
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (Btn(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 50 * k), "Conectar") || enter) JoinIp();
            y += 66 * k;
            if (Btn(new Rect(r.x + r.width * 0.35f, y, r.width * 0.3f, 38 * k), "Atrás")) _page = Page.Main;
        }

        private void DrawInGame(Rect r, float k)
        {
            float y = r.y;
            bool host = Session.IsHost;
            GUI.Label(new Rect(r.x, y, r.width, 26 * k), host ? "Tu partida está abierta" : "Estás en la partida de tu amigo", _head);
            y += 32 * k;
            // With Reglas or Dar equipo open, the panel takes this space (and the pad stays inside it).
            bool panel = _showRules || _showGive;
            if (!panel)
            {
            if (!string.IsNullOrEmpty(SteamCoop.Code))
            {
                GUI.Label(new Rect(r.x, y, r.width, 22 * k), "Código para unirse:", _label);
                var cr = new Rect(r.x, y + 18 * k, r.width * 0.62f, 64 * k);
                Fill(cr, new Color(0.03f, 0.03f, 0.03f));
                Frame(cr, new Color(0.62f, 0.45f, 0.22f), 2 * k);
                GUI.Label(cr, SteamCoop.Code, _big);
                if (Btn(new Rect(cr.xMax + 12 * k, cr.y, r.width - cr.width - 12 * k, 28 * k), _copied == SteamCoop.Code && Time.unscaledTime - _copiedAt < 2f ? "¡Copiado!" : "Copiar código"))
                { GUIUtility.systemCopyBuffer = SteamCoop.Code; _copied = SteamCoop.Code; _copiedAt = Time.unscaledTime; }
                if (Btn(new Rect(cr.xMax + 12 * k, cr.y + 36 * k, r.width - cr.width - 12 * k, 28 * k), "Invitar amigos de Steam")) SteamCoop.Invite();
                y += 92 * k;
            }
            if (host)
            {
                // Joining by IP opens a port on this PC: only while the host wants it.
                if (Btn(new Rect(r.x, y, 250 * k, 26 * k), Net.Listening ? "Unirse por IP: abierto" : "Permitir unirse por IP")) { if (Net.Listening) Net.StopHost(); else Net.StartHost(Plugin.Port); }
                if (Net.Listening) GUI.Label(new Rect(r.x + 262 * k, y + 2 * k, r.width - 262 * k, 22 * k), $"Tu IP en la red: {LocalIp()}", _label);
                y += 34 * k;
            }
            if (host)
            {
                if (Btn(new Rect(r.x, y, r.width, 26 * k), "Dificultad: " + Difficulty.Describe(Session.Players))) { Difficulty.Mode = (Difficulty.Mode + 1) % 3; Plugin.SaveDifficulty(Difficulty.Mode); }
            }
            else GUI.Label(new Rect(r.x, y, r.width, 22 * k), "Dificultad: " + Difficulty.Describe(Session.Players), _label);
            y += 32 * k;
            GUI.Label(new Rect(r.x, y, r.width, 22 * k), $"Jugadores ({Session.Players}/5):", _head);
            y += 28 * k;
            string me = (SteamCoop.Available ? SteamCoop.MyName : "Tú") + (host ? " (anfitrión, tú)" : " (tú)");
            GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), "• " + me, _label); y += 24 * k;
            if (host)
                foreach (var peer in Net.Live)
                {
                    GUI.Label(new Rect(r.x + 14 * k, y, r.width - 140 * k, 22 * k), $"• {SteamCoop.PeerName(peer)} (jugador {peer.Id + 1})", _label);
                    if (Btn(new Rect(r.xMax - 120 * k, y, 120 * k, 22 * k), "Expulsar")) Kick(peer);
                    y += 26 * k;
                }
            else if (Net.Connected)
            { GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), $"• {SteamCoop.PeerName(Net.Peer)} (anfitrión)", _label); y += 24 * k; }
            if (host && Net.ConnectedCount == 0) { GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), "Esperando a que se una alguien…", _small); }
            // Other mods that differ between players (see Mods.cs).
            foreach (var kv in Mods.Differences)
            {
                y += 6 * k;
                GUI.Label(new Rect(r.x, y, r.width, 40 * k), $"Mods distintos ({(kv.Key == 0 ? "anfitrión" : "jugador " + (kv.Key + 1))}): {kv.Value}", _small);
                y += 40 * k;
            }
            }
            // Options of this player, and the co-op achievements: a row of tabs (left/right on the pad).
            float oy = r.yMax - 86 * k, q4 = (r.width - 30 * k) / 4f;
            if (Btn(new Rect(r.x, oy, q4, 30 * k), "Ver vida: " + (EnemyBars.On ? "sí" : "no"))) EnemyBars.Toggle();
            if (Btn(new Rect(r.x + (q4 + 10 * k), oy, q4, 30 * k), $"Logros ({Achievements.Unlocked}/{Achievements.All.Length})")) OpenPanel(ref _showAch);
            if (Btn(new Rect(r.x + 2 * (q4 + 10 * k), oy, q4, 30 * k), "Reglas")) OpenPanel(ref _showRules);
            if (Btn(new Rect(r.x + 3 * (q4 + 10 * k), oy, q4, 30 * k), "Dar equipo")) OpenPanel(ref _showGive);
            var gold = new Color(1f, 0.85f, 0.45f);
            if (_showRules) Fill(new Rect(r.x + 2 * (q4 + 10 * k), oy + 32 * k, q4, 3 * k), gold);
            if (_showGive) Fill(new Rect(r.x + 3 * (q4 + 10 * k), oy + 32 * k, q4, 3 * k), gold);
            _grp = 1;
            if (_showGive)
            {
                var ar = new Rect(r.x, r.y + 34 * k, r.width, oy - r.y - 40 * k);
                Fill(ar, new Color(0.13f, 0.1f, 0.07f, 0.97f)); Frame(ar, new Color(0.45f, 0.33f, 0.17f), 1 * k);
                float ay = ar.y + 8 * k;
                GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 24 * k), "Deja una pieza en la mochila compartida para que la coja otro:", _head);
                ay += 30 * k;
                for (int i = 0; i < Give.Slots.Length; i++)
                {
                    var st = Give.Equipped(i);
                    string label = Give.SlotNames[i] + ": " + (st != null ? Stats.ItemName(st.master) : "—");
                    float gw = (ar.width - 30 * k) / 2f, gx = ar.x + 10 * k + (i % 2) * (gw + 10 * k), gy = ay + (i / 2) * 30 * k;
                    if (st != null) { if (Btn(new Rect(gx, gy, gw, 26 * k), label)) Give.ToBag(i); }
                    else GUI.Label(new Rect(gx, gy, gw, 24 * k), label, _small);
                }
            }
            if (_showRules)
            {
                var ar = new Rect(r.x, r.y + 34 * k, r.width, oy - r.y - 40 * k);
                Fill(ar, new Color(0.13f, 0.1f, 0.07f, 0.97f)); Frame(ar, new Color(0.45f, 0.33f, 0.17f), 1 * k);
                float ay = ar.y + 8 * k;
                GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 24 * k), host ? "Reglas (las eliges tú, valen para todos):" : "Reglas que ha elegido el anfitrión:", _head);
                ay += 30 * k;
                for (int i = 0; i < HostSettings.Count; i++)
                {
                    string label = (HostSettings.Get(i) ? "✔ " : "✗ ") + HostSettings.Names[i];
                    if (host) { if (Btn(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 24 * k), label)) HostSettings.Toggle(i); }
                    else GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 26 * k), label, _label);
                    ay += 27 * k;
                }
                if (HostSettings.Roles)
                {
                    ay += 6 * k;
                    GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 44 * k), "Tu rol: " + Roles.Names[Roles.Mine] + (Roles.Mine > 0 ? " — " + Roles.Texts[Roles.Mine] : "") + " (se elige al entrar en la mazmorra)", _small);
                }
            }
            _grp = 0;
            if (Btn(new Rect(r.x, r.yMax - 46 * k, 200 * k, 40 * k), "Color de tu Will (F3)")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
            if (Btn(new Rect(r.x + 210 * k, r.yMax - 46 * k, 170 * k, 40 * k), "Desatascarme")) { SetOpen(false); Arreglos.Desatascar(); }
            if (Btn(new Rect(r.xMax - 200 * k, r.yMax - 46 * k, 200 * k, 40 * k), host ? "Cerrar la partida" : "Salir de la partida")) LeaveGame();
        }
    }
}
