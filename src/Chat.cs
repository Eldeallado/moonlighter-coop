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
            var name = SteamCoop.Available ? SteamCoop.MyName : (Session.IsHost ? "Anfitrión" : "Jugador " + (Session.MyId + 1));
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
            if (!Typing && !menus && Input.GetKeyDown(KeyCode.T) && HeroMerchant.Instance != null)
            {
                Typing = true; _text = ""; _focus = true;
                var c = HeroMerchant.Instance.heroMerchantController; c.DisableMoveInput(); c.DisableAttackInput();
                if (GUIManager.Instance != null) GUIManager.Instance.input.Enabled = false;
            }
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
                Ui.Label(new Rect(x + 22 * k, y + 72 * k, w - 44 * k, 20 * k), "Enter: enviar  ·  Esc: cancelar", 14f, Ui.InkSoft, TextAnchor.MiddleCenter);
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
