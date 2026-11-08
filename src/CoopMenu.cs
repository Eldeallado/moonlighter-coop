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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2)) SetOpen(!Open);
            if (Open && Input.GetKeyDown(KeyCode.Escape)) { if (_page != Page.Main) _page = Page.Main; else SetOpen(false); }
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
        }

        private void CreateGame()
        {
            Plugin.Role = "host";
            Net.StartHost(Plugin.Port);           // same network / VPN by IP
            if (SteamCoop.Available) SteamCoop.Host();
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

        /// The host closed the game: a guest goes back to its own menu.
        public static void HostClosed() { if (Instance != null) Instance.StartCoroutine(Instance.FinishLeaving(true, 0f)); }

        private System.Collections.IEnumerator FinishLeaving(bool wasGuest, float wait)
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            string status = SteamCoop.Status;
            Net.CloseAll();
            SteamCoop.Leave();
            Session.Reset();
            Plugin.Role = "";
            Plugin.TcpMode = false;
            if (status == "El anfitrión ha cerrado la partida") SteamCoop.Status = status;
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
            float w = 640 * k, h = 470 * k;
            var p = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.4f));
            Fill(p, new Color(0.09f, 0.07f, 0.06f, 0.96f));
            Frame(p, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            Frame(new Rect(p.x + 6 * k, p.y + 6 * k, p.width - 12 * k, p.height - 12 * k), new Color(0.3f, 0.22f, 0.12f), 1 * k);
            GUI.Label(new Rect(p.x + 24 * k, p.y + 14 * k, w, 36 * k), "Cooperativo", _title);
            var inner = new Rect(p.x + 30 * k, p.y + 60 * k, w - 60 * k, h - 110 * k);

            bool inGame = Plugin.Role != "" && (Net.Connected || Session.IsHost || SteamCoop.Lobby != Steamworks.CSteamID.Nil);
            if (inGame) DrawInGame(inner, k);
            else if (_page == Page.Code) DrawJoinCode(inner, k);
            else if (_page == Page.Ip) DrawJoinIp(inner, k);
            else DrawMain(inner, k);

            if (!string.IsNullOrEmpty(SteamCoop.Status))
                GUI.Label(new Rect(p.x + 20 * k, p.yMax - 64 * k, w - 40 * k, 26 * k), SteamCoop.Status, _small);
            GUI.Label(new Rect(p.x + 20 * k, p.yMax - 36 * k, w - 40 * k, 24 * k), "F2 o Esc para cerrar este menú", _small);
        }

        private bool Btn(Rect r, string text) => GUI.Button(r, text, _button);

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
        }

        private void DrawJoinCode(Rect r, float k)
        {
            float y = r.y + 10 * k;
            GUI.Label(new Rect(r.x, y, r.width, 26 * k), "Escribe el código que te ha dado tu amigo:", _head);
            y += 44 * k;
            GUI.SetNextControlName("code");
            _code = GUI.TextField(new Rect(r.x + r.width * 0.2f, y, r.width * 0.6f, 56 * k), _code.ToUpperInvariant(), 8, _field);
            GUI.FocusControl("code");
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
            if (host) { GUI.Label(new Rect(r.x, y, r.width, 22 * k), $"Por IP en la misma red: {LocalIp()}", _label); y += 28 * k; }
            GUI.Label(new Rect(r.x, y, r.width, 22 * k), $"Jugadores ({Session.Players}/5):", _head);
            y += 28 * k;
            string me = (SteamCoop.Available ? SteamCoop.MyName : "Tú") + (host ? " (anfitrión, tú)" : " (tú)");
            GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), "• " + me, _label); y += 24 * k;
            if (host)
                foreach (var peer in Net.Live) { GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), $"• {SteamCoop.PeerName(peer)} (jugador {peer.Id})", _label); y += 24 * k; }
            else if (Net.Connected)
            { GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), $"• {SteamCoop.PeerName(Net.Peer)} (anfitrión)", _label); y += 24 * k; }
            if (host && Net.ConnectedCount == 0) { GUI.Label(new Rect(r.x + 14 * k, y, r.width, 22 * k), "Esperando a que se una alguien…", _small); }
            if (Btn(new Rect(r.xMax - 200 * k, r.yMax - 46 * k, 200 * k, 40 * k), host ? "Cerrar la partida" : "Salir de la partida")) LeaveGame();
        }
    }
}
