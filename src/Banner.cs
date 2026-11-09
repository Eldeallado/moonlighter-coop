using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Co-op notices drawn like the game's own: the parchment scroll it shows when you enter a dungeon, with its
    /// font (Fairfax) and ink colour.
    /// - You are a ghost: a small scroll stays at the bottom while you are one.
    /// - Someone else falls / comes back / a vote is on: a scroll pops up for a few seconds and fades away.
    public class Banner : MonoBehaviour
    {
        public static Banner Instance;
        private Sprite _scroll;
        private Font _font;
        private Color _ink = new Color(0.27f, 0.17f, 0.1f);
        private GUIStyle _style, _shadow;
        private string _popup; private float _popupAt = -100f;
        private const float PopupTime = 4f;
        private readonly HashSet<int> _lastGhosts = new HashSet<int>();

        private void Awake() { Instance = this; }

        public static void Show(string text) { if (Instance != null) { Instance._popup = text; Instance._popupAt = Time.unscaledTime; } }

        private bool Load()
        {
            if (_font != null) return true;
            var gui = GUIManager.Instance;
            var ws = gui != null ? HarmonyLib.Traverse.Create(gui).Field("dungeonWelcomeScroll").GetValue<DungeonWelcomeScroll>() : null;
            if (ws != null)
            {
                var frames = ws.golemScrollAnimation;
                if (frames != null && frames.Count > 0) _scroll = frames[frames.Count - 1];   // fully unrolled
                if (ws.textDungeon != null) _font = ws.textDungeon.font;
            }
            if (_font == null) _font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "Fairfax");
            return _font != null;
        }

        private void Update()
        {
            // Watch the others: someone fell, or came back.
            var now = new HashSet<int>(Downed.Ghosts);
            foreach (var id in now) if (!_lastGhosts.Contains(id)) Show($"{Name(id)} ha caído — llega a una fuente o baja de piso para revivirlo");
            foreach (var id in _lastGhosts) if (!now.Contains(id) && Puppet.All.ContainsKey(id)) Show($"¡{Name(id)} ha vuelto a la vida!");
            _lastGhosts.Clear(); foreach (var id in now) _lastGhosts.Add(id);
        }

        private static string Name(int id) =>
            Puppet.All.TryGetValue(id, out var p) && p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "Tu compañero";

        private string _lastStatus = "";
        private float _hintsUntil = -1f;
        private bool _wasInGame, _wasConnected;
        private GUIStyle _keyStyle, _hintStyle;

        private void OnGUI()
        {
            if (ColorMenu.Open || CoopMenu.Open) return;
            if (!Load()) return;
            // Connection news (created, joined, someone left...) pop up as a scroll too.
            var st = SteamCoop.Status ?? "";
            if (st != _lastStatus) { _lastStatus = st; if (st != "") Show(st); }
            // Key hints: always in the main menu; in game, for a few seconds after arriving or connecting.
            bool inGame = HeroMerchant.Instance != null;
            if (inGame && !_wasInGame) _hintsUntil = Time.unscaledTime + 8f;
            if (Net.Connected && !_wasConnected) _hintsUntil = Time.unscaledTime + 8f;
            _wasInGame = inGame; _wasConnected = Net.Connected;
            bool menu = !inGame && UnityEngine.Object.FindObjectOfType<MainMenuPanel>() != null;
            float hintAge = _hintsUntil - Time.unscaledTime;
            if (menu) KeyHints(1f);
            else if (inGame && hintAge > 0f && (GUIManager.Instance == null || GUIManager.Instance.currentInventory == null)) KeyHints(Mathf.Clamp01(hintAge / 0.8f));

            string vote = Net.Connected ? Vote.Hint() : null;
            string fixedText = vote ?? (Net.Connected && Downed.Me ? (HostSettings.HardRevive ? "Eres un fantasma — solo revives si un compañero entra en una fuente o gasta una poción a tu lado" : "Eres un fantasma — revives si un compañero entra en una fuente o bajáis de piso") : null);
            float age = Time.unscaledTime - _popupAt;
            bool popup = _popup != null && age < PopupTime && !Downed.Me && vote == null;
            if (fixedText == null && !popup) return;
            float alpha = popup && fixedText == null ? Mathf.Clamp01(Mathf.Min(age / 0.25f, (PopupTime - age) / 0.6f)) : 1f;
            Draw(fixedText ?? _popup, alpha);
        }

        /// Bottom right: small scroll with the mod's two keys, drawn like key caps.
        private void KeyHints(float a)
        {
            float k = Screen.height / 531f;
            if (_keyStyle == null)
            {
                _keyStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, font = _font };
                _hintStyle = new GUIStyle { alignment = TextAnchor.MiddleLeft, font = _font };
            }
            _keyStyle.fontSize = _hintStyle.fontSize = Mathf.Max(9, (int)(12 * k));
            string right = Changelog.Unseen ? "Cooperativo · ¡novedades!" : Net.Connected ? $"Cooperativo · {Session.Players} jugadores" : "Cooperativo";
            float w = 290 * k, h = 30 * k;
            var r = new Rect(Screen.width - w - 14 * k, Screen.height - h - 14 * k, w, h);
            Parchment(r, k * 0.75f, a);
            float x = r.x + 26 * k, cy = r.y + h / 2f;
            x = KeyCap(x, cy, PadNav.Active ? "L3+R3" : "F2", right, k, a) + 14 * k;
            if (!PadNav.Active) KeyCap(x, cy, "F3", "Color", k, a);
        }

        private float KeyCap(float x, float cy, string key, string label, float k, float a)
        {
            var cap = new Rect(x, cy - 9 * k, (key.Length > 2 ? 48 : 24) * k, 18 * k);
            Box(cap, new Color(0.17f, 0.1f, 0.07f), a);
            Box(new Rect(cap.x + k, cap.y + k, cap.width - 2 * k, cap.height - 3 * k), new Color(0.45f, 0.3f, 0.18f), a);
            _keyStyle.normal.textColor = new Color(1f, 0.93f, 0.75f, a);
            GUI.Label(cap, key, _keyStyle);
            _hintStyle.normal.textColor = new Color(0.27f, 0.17f, 0.1f, a);
            var size = _hintStyle.CalcSize(new GUIContent(label));
            GUI.Label(new Rect(cap.xMax + 5 * k, cy - 9 * k, size.x + 2, 18 * k), label, _hintStyle);
            return cap.xMax + 5 * k + size.x;
        }
        private Texture2D _px;
        private void Box(Rect r, Color c, float a) { var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * a); GUI.DrawTexture(r, _px); GUI.color = o; }

        /// A pixel-art scroll like the game's: cream paper with a darker rim and a faint inner line, rolled wooden
        /// ends on both sides, all with a dark outline.
        private void Parchment(Rect r, float k, float a)
        {
            if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
            float u = Mathf.Max(1f, Mathf.Round(2f * k));             // one "pixel" of the game's art
            var outline = new Color(0.17f, 0.1f, 0.07f, 0.95f);
            var paper = new Color(0.93f, 0.86f, 0.68f, 0.97f);
            var paperDark = new Color(0.82f, 0.71f, 0.5f, 0.97f);
            var wood = new Color(0.55f, 0.33f, 0.18f, 1f);
            var woodLight = new Color(0.72f, 0.47f, 0.26f, 1f);
            var paperR = new Rect(r.x + 10 * u, r.y + 2 * u, r.width - 20 * u, r.height - 4 * u);
            Box(new Rect(paperR.x - u, paperR.y - u, paperR.width + 2 * u, paperR.height + 2 * u), outline, a);
            Box(paperR, paperDark, a);
            Box(new Rect(paperR.x + u, paperR.y + u, paperR.width - 2 * u, paperR.height - 3 * u), paper, a);
            Box(new Rect(paperR.x + 3 * u, paperR.y + 3 * u, paperR.width - 6 * u, u), paperDark, a * 0.6f);
            Box(new Rect(paperR.x + 3 * u, paperR.yMax - 5 * u, paperR.width - 6 * u, u), paperDark, a * 0.6f);
            foreach (float x in new[] { r.x, r.xMax - 12 * u })
            {
                var roll = new Rect(x, r.y, 12 * u, r.height);
                Box(roll, outline, a);
                Box(new Rect(roll.x + u, roll.y + u, roll.width - 2 * u, roll.height - 2 * u), wood, a);
                Box(new Rect(roll.x + 3 * u, roll.y + u, 3 * u, roll.height - 2 * u), woodLight, a);
                Box(new Rect(roll.x + u, roll.y + 3 * u, roll.width - 2 * u, u), outline, a * 0.5f);
                Box(new Rect(roll.x + u, roll.yMax - 4 * u, roll.width - 2 * u, u), outline, a * 0.5f);
            }
        }

        private void Draw(string text, float alpha)
        {
            float k = Screen.height / 531f;
            if (_style == null)
            {
                _style = new GUIStyle { alignment = TextAnchor.MiddleCenter, wordWrap = true, font = _font };
                _shadow = new GUIStyle(_style);
            }
            _style.fontSize = Mathf.Max(10, (int)(15 * k));
            _shadow.fontSize = _style.fontSize;
            float w = 470 * k, h = 62 * k;
            var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.78f - h / 2f, w, h);
            Parchment(r, k, alpha);
            var old = GUI.color;            GUI.color = old;
            var tx = new Rect(r.x + 34 * k, r.y + 6 * k, w - 68 * k, h - 12 * k);
            _shadow.normal.textColor = new Color(1f, 0.95f, 0.8f, 0.35f * alpha);
            GUI.Label(new Rect(tx.x + 1, tx.y + 1, tx.width, tx.height), text, _shadow);
            _style.normal.textColor = new Color(_ink.r, _ink.g, _ink.b, alpha);
            GUI.Label(tx, text, _style);
        }
    }
}
