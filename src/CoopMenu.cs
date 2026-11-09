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
        private bool _showAch, _showRules, _showGive;
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

        // Gamepad: which button has the focus, how many buttons the last frame drew, A pressed this frame.
        private int _focus, _btnCount, _lastCount = 1, _slot;
        private bool _padA;

        private void Update()
        {
            PadNav.Poll();
            _padA = false;
            if (Input.GetKeyDown(KeyCode.F2) || PadNav.Combo && !ColorMenu.Open) { SetOpen(!Open); _focus = 0; return; }
            if (!Open) return;
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
            if (PadNav.Up) _focus = (_focus + _lastCount - 1) % _lastCount;
            if (PadNav.Down) _focus = (_focus + 1) % _lastCount;
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
            _btnCount = 0;
            float w = 640 * k, h = 470 * k;
            var p = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.4f));
            Fill(p, new Color(0.09f, 0.07f, 0.06f, 0.96f));
            Frame(p, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            Frame(new Rect(p.x + 6 * k, p.y + 6 * k, p.width - 12 * k, p.height - 12 * k), new Color(0.3f, 0.22f, 0.12f), 1 * k);
            GUI.Label(new Rect(p.x + 24 * k, p.y + 14 * k, w, 36 * k), "Cooperativo", _title);
            GUI.Label(new Rect(p.xMax - 190 * k, p.y + 22 * k, 160 * k, 22 * k), "versión " + Plugin.Version, _small);
            if (Versions.UpdateAvailable && Btn(new Rect(p.x + 190 * k, p.y + 18 * k, 260 * k, 28 * k), "¡Versión " + Versions.Latest + " disponible! Descargar")) Application.OpenURL(Versions.ReleasesUrl);
            var inner = new Rect(p.x + 30 * k, p.y + 60 * k, w - 60 * k, h - 110 * k);

            bool inGame = Plugin.Role != "" && (Net.Connected || Session.IsHost || SteamCoop.Lobby != Steamworks.CSteamID.Nil);
            if (inGame) DrawInGame(inner, k);
            else if (_page == Page.Code) DrawJoinCode(inner, k);
            else if (_page == Page.Ip) DrawJoinIp(inner, k);
            else DrawMain(inner, k);

            if (!string.IsNullOrEmpty(SteamCoop.Status))
                GUI.Label(new Rect(p.x + 20 * k, p.yMax - 64 * k, w - 40 * k, 26 * k), SteamCoop.Status, _small);
            GUI.Label(new Rect(p.x + 20 * k, p.yMax - 36 * k, w - 40 * k, 24 * k), PadNav.Active ? "Cruceta: moverse · A: aceptar · B: volver · L3+R3: cerrar" : "F2 o Esc para cerrar este menú", _small);
            if (Event.current.type == EventType.Repaint) { _lastCount = Mathf.Max(1, _btnCount); if (_focus >= _lastCount) _focus = 0; }
        }

        private bool Btn(Rect r, string text)
        {
            int idx = _btnCount++;
            bool focused = PadNav.Active && idx == _focus;
            if (focused) Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.85f, 0.45f), 3);
            bool pressed = GUI.Button(r, text, _button);
            if (focused && _padA && Event.current.type == EventType.Repaint) { _padA = false; pressed = true; }
            return pressed;
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
            // Options of this player, and the co-op achievements.
            float oy = r.yMax - 86 * k, q4 = (r.width - 30 * k) / 4f;
            if (Btn(new Rect(r.x, oy, q4, 30 * k), "Ver vida: " + (EnemyBars.On ? "sí" : "no"))) EnemyBars.Toggle();
            if (Btn(new Rect(r.x + (q4 + 10 * k), oy, q4, 30 * k), $"Logros ({Achievements.Unlocked}/{Achievements.All.Length})")) { _showAch = !_showAch; _showRules = false; }
            if (Btn(new Rect(r.x + 2 * (q4 + 10 * k), oy, q4, 30 * k), "Reglas")) { _showRules = !_showRules; _showAch = false; _showGive = false; }
            if (Btn(new Rect(r.x + 3 * (q4 + 10 * k), oy, q4, 30 * k), "Dar equipo")) { _showGive = !_showGive; _showAch = false; _showRules = false; }
            if (_showGive)
            {
                var ar = new Rect(r.x, r.y + 40 * k, r.width, oy - r.y - 46 * k);
                var o = GUI.color; GUI.color = new Color(0.07f, 0.05f, 0.04f, 0.97f); GUI.DrawTexture(ar, Texture2D.whiteTexture); GUI.color = o;
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
                var ar = new Rect(r.x, r.y + 40 * k, r.width, oy - r.y - 46 * k);
                var o = GUI.color; GUI.color = new Color(0.07f, 0.05f, 0.04f, 0.97f); GUI.DrawTexture(ar, Texture2D.whiteTexture); GUI.color = o;
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
                    GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 22 * k), "Tu rol: " + Roles.Names[Roles.Mine] + (Roles.Mine > 0 ? " — " + Roles.Texts[Roles.Mine] : "") + " (se elige al entrar en la mazmorra)", _small);
                }
            }
            if (_showAch)
            {
                var ar = new Rect(r.x, r.y + 40 * k, r.width, oy - r.y - 46 * k);
                var o = GUI.color; GUI.color = new Color(0.07f, 0.05f, 0.04f, 0.97f); GUI.DrawTexture(ar, Texture2D.whiteTexture); GUI.color = o;
                float ay = ar.y + 6 * k;
                foreach (var a in Achievements.All)
                {
                    bool got = Achievements.Has(a.Id);
                    GUI.Label(new Rect(ar.x + 10 * k, ay, ar.width - 20 * k, 22 * k), (got ? "✔ " : "· ") + a.Name + " — " + a.Text, got ? _head : _small);
                    ay += 23 * k;
                }
            }
            if (Btn(new Rect(r.x, r.yMax - 46 * k, 200 * k, 40 * k), "Color de tu Will (F3)")) { SetOpen(false); ColorMenu.Instance?.SetOpen(true); }
            if (Btn(new Rect(r.x + 210 * k, r.yMax - 46 * k, 170 * k, 40 * k), "Desatascarme")) { SetOpen(false); Arreglos.Desatascar(); }
            if (Btn(new Rect(r.xMax - 200 * k, r.yMax - 46 * k, 200 * k, 40 * k), host ? "Cerrar la partida" : "Salir de la partida")) LeaveGame();
        }
    }
}
