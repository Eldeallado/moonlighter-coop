using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A short chat: T to write, Enter to send, Esc to cancel. The last lines show at the bottom left for a while.
    /// Plain text only (no rich-text tags), 120 characters at most.
    public class Chat : MonoBehaviour
    {
        public static Chat Instance;
        public static bool Typing;
        private string _text = "";
        private bool _focus;
        private readonly List<(string who, string text, float at)> _lines = new List<(string, string, float)>();
        private const float Show = 12f;

        private void Awake() { Instance = this; }

        public static void Send(string text)
        {
            text = Clean(text);
            if (text == "" || !Net.Connected) return;
            var name = Nombres.Mio(Session.IsHost ? "Anfitrión" : "Jugador " + (Session.MyId + 1));
            var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
            w.Write((byte)Session.MyId); w.Write(Clean(name)); w.Write(text);
            Net.Send(Msg.Chat, ms.ToArray());
            Instance?.Add(name, text);
        }

        public static void OnChat(byte[] data)
        {
            try
            {
                var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
                int id = r.ReadByte(); if (id == Session.MyId) return;
                Instance?.Add(Clean(r.ReadString()), Clean(r.ReadString()));
            }
            catch { }
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var ch in s) if (!char.IsControl(ch) && ch != '<' && ch != '>') sb.Append(ch);
            var t = sb.ToString().Trim();
            return t.Length > 120 ? t.Substring(0, 120) : t;
        }

        private void Add(string who, string text)
        {
            _lines.Add((who, text, Time.unscaledTime));
            if (_lines.Count > 6) _lines.RemoveAt(0);
            Plugin.Log.LogInfo($"[chat] {who}: {text}");
        }

        /// QA: show the chat without a partner (some lines and the input box).
        public static bool Preview;
        public void DemoLines() { _lines.Clear(); Add("Elde al lado", "¡Vamos a por el Gólem!"); Add("Jugador 2", "Espera, que vendo esto primero"); _text = "Ya voy"; }

        private void Update()
        {
            if (!Net.Connected && !Preview) { if (Typing) Close(); return; }
            bool menus = ColorMenu.Open || CoopMenu.Open || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null);
            if (!Typing && !menus && HeroMerchant.Instance != null && Net.Connected && Teclas.Movil) Teclas.Ofrecer(KeyCode.T, "Chat");
            if (!Typing && !menus && Teclas.Abajo(KeyCode.T) && HeroMerchant.Instance != null) OpenTyping();
            if (Typing && PadNav.Active) PadKeys();
        }

        /// Start writing (T, or F2 → Escribir en el chat). With a pad, an on-screen keyboard comes up.
        public static void OpenTyping()
        {
            if (Instance == null || HeroMerchant.Instance == null) return;
            Typing = true; Instance._text = ""; Instance._focus = true; Instance._kx = 0; Instance._ky = 1; Instance._openedAt = Time.unscaledTime;
            var c = HeroMerchant.Instance.heroMerchantController; c.DisableMoveInput(); c.DisableAttackInput();
            if (GUIManager.Instance != null) GUIManager.Instance.input.Enabled = false;
            Ui.Sound(Ui.SndOpen);
        }

        // ---------- on-screen keyboard (pad) ----------

        private static readonly string[] Rows = { "1234567890", "qwertyuiop", "asdfghjklñ", "zxcvbnm,.?", "áéíóú¡!¿:)" };
        private static readonly string[] Specials = { "Mayús", "Espacio", "Borrar", "Enviar" };
        private int _kx, _ky; private bool _caps; private float _openedAt;

        private void PadKeys()
        {
            PadNav.Poll();
            int rows = Rows.Length + 1;
            if (PadNav.Up) { _ky = (_ky + rows - 1) % rows; Ui.Sound(Ui.SndMove); }
            if (PadNav.Down) { _ky = (_ky + 1) % rows; Ui.Sound(Ui.SndMove); }
            int cols = _ky < Rows.Length ? 10 : Specials.Length;
            if (_ky == Rows.Length && _kx >= cols) _kx = cols - 1;
            if (PadNav.Left) { _kx = (_kx + cols - 1) % cols; Ui.Sound(Ui.SndMove); }
            if (PadNav.Right) { _kx = (_kx + 1) % cols; Ui.Sound(Ui.SndMove); }
            if (PadNav.B) { if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1); else Close(); Ui.Sound(Ui.SndPress); }
            if (PadNav.A) { Ui.Sound(Ui.SndPress); PressKey(_kx, _ky); }
        }

        private void PressKey(int x, int y)
        {
            if (y < Rows.Length)
            {
                if (_text.Length >= 120) return;
                char c = Rows[y][x];
                _text += _caps ? char.ToUpperInvariant(c) : c;
                if (_caps && char.IsLetter(c)) _caps = false;   // one capital, like a phone
                return;
            }
            switch (x)
            {
                case 0: _caps = !_caps; break;
                case 1: if (_text.Length < 120) _text += " "; break;
                case 2: if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1); break;
                case 3: Send(_text); Close(); break;
            }
        }

        private void DrawKeyboard(float k)
        {
            float kw = 44 * k, kh = 36 * k, gap = 6 * k;
            float w = 10 * kw + 9 * gap + 40 * k, h = (Rows.Length + 1) * (kh + gap) + 70 * k;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 150 * k, w, h);
            Ui.BeginOpen(_openedAt, r);
            Ui.Scroll(r);
            float x0 = r.x + 20 * k, y0 = r.y + 28 * k;
            for (int y = 0; y < Rows.Length; y++)
                for (int x = 0; x < 10; x++)
                {
                    var kr = new Rect(x0 + x * (kw + gap), y0 + y * (kh + gap), kw, kh);
                    char c = Rows[y][x];
                    if (Ui.Button(kr, (_caps ? char.ToUpperInvariant(c) : c).ToString(), x == _kx && y == _ky, false, true, 18f)) PressKey(x, y);
                }
            float sy = y0 + Rows.Length * (kh + gap), total = 10 * kw + 9 * gap;
            float[] widths = { 0.2f, 0.4f, 0.2f, 0.2f };
            float sx = x0;
            for (int i = 0; i < Specials.Length; i++)
            {
                float sw = total * widths[i] - (i < Specials.Length - 1 ? gap : 0f);
                var kr = new Rect(sx, sy, sw, kh);
                bool on = i == 0 && _caps;
                if (Ui.Button(kr, Specials[i], i == _kx && _ky == Rows.Length, on, true, 16f, i == 3 ? Ui.Tone.Gold : Ui.Tone.Wood)) PressKey(i, Rows.Length);
                sx += sw + gap;
            }
            Ui.Label(new Rect(r.x, r.yMax - 32 * k, r.width, 20 * k), "Cruceta: tecla  ·  A: escribir  ·  B: borrar (o cerrar si está vacío)", 14f, Ui.InkFaint, TextAnchor.MiddleCenter);
            Ui.EndOpen();
        }

        private void Close()
        {
            Typing = false;
            var hero = HeroMerchant.Instance;
            if (hero != null) { hero.heroMerchantController.EnableMoveInput(); if (!Downed.Me) hero.heroMerchantController.EnableAttackInput(); }
            if (GUIManager.Instance != null) GUIManager.Instance.input.Enabled = true;
        }

        private void OnGUI()
        {
            Ui.Load();
            float k = Ui.K;
            float x = 20 * k, w = 470 * k, y = Screen.height - 124 * k;
            if (Typing)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { Send(_text); Close(); e.Use(); return; }
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
                // A small scroll with the box to write in.
                var sr = new Rect(x - 10 * k, y + 34 * k, w + 20 * k, 66 * k);
                Ui.Scroll(sr);
                _text = Ui.TextField(new Rect(x + 22 * k, y + 42 * k, w - 44 * k, 28 * k), _text, 120, 17f, TextAnchor.MiddleLeft, "coopchat");
                if (_focus) { GUI.FocusControl("coopchat"); _focus = false; }
                Ui.Label(new Rect(x + 22 * k, y + 72 * k, w - 44 * k, 20 * k), PadNav.Active ? "Escribe con el teclado de la pantalla" : "Enter: enviar  ·  Esc: cancelar", 14f, Ui.InkSoft, TextAnchor.MiddleCenter);
                if (PadNav.Active) DrawKeyboard(k);
            }
            float now = Time.unscaledTime;
            float ly = y + 28 * k;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                float age = now - l.at;
                if (!Typing && age > Show) continue;
                float a = Typing ? 1f : Mathf.Clamp01((Show - age) / 2f);
                float nw = Ui.Width(l.who + ": ", 16f);
                var h = Mathf.Max(24 * k, Ui.Height(l.text, 16f, w - nw - 16 * k) + 6 * k);
                ly -= h + 3 * k;
                // Each line on a little parchment plate: name in red ink, then the message.
                Ui.Plate(new Rect(x - 4 * k, ly, w + 8 * k, h), 0.92f * a);
                var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                Ui.Label(new Rect(x + 6 * k, ly + 2 * k, nw, h - 4 * k), l.who + ":", 16f, Ui.Title, TextAnchor.UpperLeft);
                Ui.Label(new Rect(x + 6 * k + nw, ly + 2 * k, w - nw - 16 * k, h - 4 * k), l.text, 16f, Ui.Ink, TextAnchor.UpperLeft, true);
                GUI.color = o;
            }
        }
    }
}
