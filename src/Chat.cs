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
        private GUIStyle _style, _input;
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

        private void Update()
        {
            if (!Net.Connected) { if (Typing) Close(); return; }
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
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
                _input = new GUIStyle(GUI.skin.textField);
            }
            _style.fontSize = (int)(15 * k); _input.fontSize = (int)(16 * k);
            float x = 20 * k, w = 460 * k, y = Screen.height - 120 * k;
            if (Typing)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { Send(_text); Close(); e.Use(); return; }
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
                GUI.SetNextControlName("coopchat");
                _text = GUI.TextField(new Rect(x, y + 40 * k, w, 30 * k), _text, 120, _input);
                if (_focus) { GUI.FocusControl("coopchat"); _focus = false; }
                GUI.Label(new Rect(x, y + 72 * k, w, 20 * k), "Enter enviar · Esc cancelar", _style);
            }
            float now = Time.unscaledTime;
            float ly = y + 30 * k;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                float age = now - l.at;
                if (!Typing && age > Show) continue;
                float a = Typing ? 1f : Mathf.Clamp01((Show - age) / 2f);
                var h = _style.CalcHeight(new GUIContent(l.who + ": " + l.text), w) + 2 * k;
                ly -= h;
                var o = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f * a); GUI.DrawTexture(new Rect(x - 4 * k, ly, w + 8 * k, h), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.45f, a); GUI.Label(new Rect(x, ly, w, h), l.who + ":", _style);
                var nw = _style.CalcSize(new GUIContent(l.who + ": ")).x;
                GUI.color = new Color(1f, 1f, 1f, a); GUI.Label(new Rect(x + nw, ly, w - nw, h), l.text, _style);
                GUI.color = o;
            }
        }
    }
}
