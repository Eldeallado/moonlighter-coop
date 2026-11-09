using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The look of every co-op window, drawn like the game's own art: parchment sheets with wooden rods, the
    /// game's font (Fairfax) in brown ink, wooden buttons, inset paper cards, and bars and scrollbars to match.
    /// Sizes are in 720p points times k; one "art pixel" (U) is the game's own pixel size.
    public static class Ui
    {
        public static Font Font;

        // Palette, from the game's scrolls and wooden UI.
        public static readonly Color Outline = new Color(0.17f, 0.1f, 0.07f);
        public static readonly Color Paper = new Color(0.93f, 0.86f, 0.68f);
        public static readonly Color PaperDark = new Color(0.82f, 0.71f, 0.5f);
        public static readonly Color PaperShade = new Color(0.74f, 0.62f, 0.42f);
        public static readonly Color Wood = new Color(0.55f, 0.33f, 0.18f);
        public static readonly Color WoodLight = new Color(0.72f, 0.47f, 0.26f);
        public static readonly Color WoodDark = new Color(0.36f, 0.2f, 0.11f);
        public static readonly Color Ink = new Color(0.27f, 0.17f, 0.1f);
        public static readonly Color InkSoft = new Color(0.45f, 0.33f, 0.22f);
        public static readonly Color InkFaint = new Color(0.58f, 0.46f, 0.32f);
        public static readonly Color Title = new Color(0.45f, 0.16f, 0.07f);
        public static readonly Color Cream = new Color(1f, 0.93f, 0.75f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.36f);
        public static readonly Color Good = new Color(0.33f, 0.5f, 0.18f);
        public static readonly Color Bad = new Color(0.66f, 0.2f, 0.12f);

        public static float K => Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
        public static float U => Mathf.Max(1f, Mathf.Round(Screen.height / 360f));

        private static Texture2D _px;
        private static Texture2D Px { get { if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); } return _px; } }

        /// The game's font, from its dungeon welcome scroll (or by name). False until the game has loaded it.
        public static bool Load()
        {
            if (Font != null) return true;
            var gui = GUIManager.Instance;
            var ws = gui != null ? HarmonyLib.Traverse.Create(gui).Field("dungeonWelcomeScroll").GetValue<DungeonWelcomeScroll>() : null;
            if (ws != null && ws.textDungeon != null) Font = ws.textDungeon.font;
            if (Font == null) Font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "Fairfax");
            return Font != null;
        }

        // ---------- shapes ----------

        public static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * o.a); GUI.DrawTexture(r, Px); GUI.color = o; }
        public static void Frame(Rect r, Color c, float w) { Box(new Rect(r.x, r.y, r.width, w), c); Box(new Rect(r.x, r.yMax - w, r.width, w), c); Box(new Rect(r.x, r.y, w, r.height), c); Box(new Rect(r.xMax - w, r.y, w, r.height), c); }

        /// Darkens the game behind a window.
        public static void Dim(float a = 0.45f) => Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.03f, 0.02f, a));

        /// A big window: a parchment sheet hanging from a wooden rod, with another rod at the bottom.
        public static void Sheet(Rect r)
        {
            float u = U;
            var paper = new Rect(r.x, r.y + 5 * u, r.width, r.height - 10 * u);
            Box(new Rect(paper.x + 2 * u, paper.y + 2 * u, paper.width, paper.height), new Color(0f, 0f, 0f, 0.35f));   // drop shadow
            Box(paper, Outline);
            Box(new Rect(paper.x + u, paper.y + u, paper.width - 2 * u, paper.height - 2 * u), PaperDark);
            Box(new Rect(paper.x + 3 * u, paper.y + 3 * u, paper.width - 6 * u, paper.height - 6 * u), Paper);
            Frame(new Rect(paper.x + 6 * u, paper.y + 6 * u, paper.width - 12 * u, paper.height - 12 * u), new Color(PaperDark.r, PaperDark.g, PaperDark.b, 0.8f), u);
            // Torn-ish corners: a notch of the darker paper.
            foreach (var c in new[] { new Vector2(paper.x + 3 * u, paper.y + 3 * u), new Vector2(paper.xMax - 6 * u, paper.y + 3 * u), new Vector2(paper.x + 3 * u, paper.yMax - 6 * u), new Vector2(paper.xMax - 6 * u, paper.yMax - 6 * u) })
                Box(new Rect(c.x, c.y, 3 * u, 3 * u), PaperDark);
            Rod(new Rect(r.x - 8 * u, r.y, r.width + 16 * u, 10 * u));
            Rod(new Rect(r.x - 8 * u, r.yMax - 10 * u, r.width + 16 * u, 10 * u));
        }

        /// A wooden rod with knobs at both ends.
        private static void Rod(Rect r)
        {
            float u = U;
            var bar = new Rect(r.x + 4 * u, r.y + 2 * u, r.width - 8 * u, r.height - 4 * u);
            Box(bar, Outline);
            Box(new Rect(bar.x + u, bar.y + u, bar.width - 2 * u, bar.height - 2 * u), Wood);
            Box(new Rect(bar.x + u, bar.y + u, bar.width - 2 * u, u), WoodLight);
            Box(new Rect(bar.x + u, bar.yMax - 2 * u, bar.width - 2 * u, u), WoodDark);
            foreach (float x in new[] { r.x, r.xMax - 7 * u })
            {
                var knob = new Rect(x, r.y, 7 * u, r.height);
                Box(knob, Outline);
                Box(new Rect(knob.x + u, knob.y + u, knob.width - 2 * u, knob.height - 2 * u), WoodDark);
                Box(new Rect(knob.x + 2 * u, knob.y + 2 * u, 2 * u, knob.height - 4 * u), Wood);
            }
        }

        /// A small horizontal scroll with rolled wooden ends (the game's dungeon-welcome scroll); for notices.
        public static void Scroll(Rect r, float a = 1f)
        {
            var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            float u = U * 0.75f;
            var paperR = new Rect(r.x + 10 * u, r.y + 2 * u, r.width - 20 * u, r.height - 4 * u);
            Box(new Rect(paperR.x - u, paperR.y - u, paperR.width + 2 * u, paperR.height + 2 * u), Outline);
            Box(paperR, PaperDark);
            Box(new Rect(paperR.x + u, paperR.y + u, paperR.width - 2 * u, paperR.height - 3 * u), Paper);
            Box(new Rect(paperR.x + 3 * u, paperR.y + 3 * u, paperR.width - 6 * u, u), new Color(PaperDark.r, PaperDark.g, PaperDark.b, 0.6f));
            Box(new Rect(paperR.x + 3 * u, paperR.yMax - 5 * u, paperR.width - 6 * u, u), new Color(PaperDark.r, PaperDark.g, PaperDark.b, 0.6f));
            foreach (float x in new[] { r.x, r.xMax - 12 * u })
            {
                var roll = new Rect(x, r.y, 12 * u, r.height);
                Box(roll, Outline);
                Box(new Rect(roll.x + u, roll.y + u, roll.width - 2 * u, roll.height - 2 * u), Wood);
                Box(new Rect(roll.x + 3 * u, roll.y + u, 3 * u, roll.height - 2 * u), WoodLight);
                Box(new Rect(roll.x + u, roll.y + 3 * u, roll.width - 2 * u, u), new Color(Outline.r, Outline.g, Outline.b, 0.5f));
                Box(new Rect(roll.x + u, roll.yMax - 4 * u, roll.width - 2 * u, u), new Color(Outline.r, Outline.g, Outline.b, 0.5f));
            }
            GUI.color = o;
        }

        /// A small parchment plate (HUD rows, tooltips): paper with a dark rim.
        public static void Plate(Rect r, float a = 1f)
        {
            float u = U;
            var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            Box(r, Outline);
            Box(new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 2 * u), PaperDark);
            Box(new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 3 * u), Paper);
            GUI.color = o;
        }

        /// An area inset in the parchment: a slightly darker panel with a thin ink line.
        public static void Card(Rect r, bool lit = false, bool selected = false)
        {
            float u = U;
            Box(r, lit ? new Color(0.98f, 0.9f, 0.66f) : new Color(PaperDark.r, PaperDark.g, PaperDark.b, 0.55f));
            Box(new Rect(r.x, r.y, r.width, u), new Color(PaperShade.r, PaperShade.g, PaperShade.b, 0.9f));   // inner shadow on top
            Frame(r, selected ? Title : lit ? WoodLight : InkFaint, selected ? 2 * u : u);
        }

        /// A dark wooden inset (for big values like the join code).
        public static void Slot(Rect r)
        {
            float u = U;
            Box(r, Outline);
            Box(new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 2 * u), WoodDark);
            Box(new Rect(r.x + u, r.y + u, r.width - 2 * u, 2 * u), new Color(0f, 0f, 0f, 0.3f));
        }

        /// Horizontal divider with a small diamond in the middle.
        public static void Divider(float x, float y, float w)
        {
            float u = U;
            Box(new Rect(x, y, w, u), InkFaint);
            Box(new Rect(x + w / 2f - 2 * u, y - 2 * u, 4 * u, 5 * u), InkSoft);
        }

        /// A progress bar: dark track, coloured fill with a light top edge.
        public static void Bar(Rect r, float frac, Color fill)
        {
            float u = U;
            Box(r, Outline);
            var inner = new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 2 * u);
            Box(inner, new Color(0.3f, 0.2f, 0.13f));
            var f = new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(frac), inner.height);
            Box(f, fill);
            if (f.width > 0f) Box(new Rect(f.x, f.y, f.width, Mathf.Max(1f, u * 0.5f)), new Color(1f, 1f, 1f, 0.35f));
        }

        /// A drawn tick box (the font has no ✔).
        public static void Check(Rect r, bool on)
        {
            float u = U;
            Box(r, Outline);
            Box(new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 2 * u), on ? Paper : PaperDark);
            if (!on) return;
            // A tick made of small squares.
            float s = Mathf.Max(u, Mathf.Round(r.width / 8f));
            float x0 = r.x + r.width * 0.2f, y0 = r.y + r.height * 0.5f;
            for (int i = 0; i < 2; i++) Box(new Rect(x0 + i * s, y0 + i * s, s * 1.4f, s * 1.4f), Good);
            for (int i = 0; i < 4; i++) Box(new Rect(x0 + (2 + i) * s, y0 + (1 - i) * s, s * 1.4f, s * 1.4f), Good);
        }

        // ---------- text ----------

        private static readonly Dictionary<int, GUIStyle> _styles = new Dictionary<int, GUIStyle>();

        /// A style in the game's font. Cached by layout; size and colour are set every call.
        public static GUIStyle Style(float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false, bool rich = false)
        {
            int key = (int)anchor * 4 + (wrap ? 1 : 0) + (rich ? 2 : 0);
            if (!_styles.TryGetValue(key, out var s) || s.font != Font)
            {
                s = new GUIStyle { font = Font, alignment = anchor, wordWrap = wrap, richText = rich, clipping = TextClipping.Overflow };
                _styles[key] = s;
            }
            s.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * K));
            s.normal.textColor = color;
            return s;
        }

        public static void Label(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false, bool rich = false)
            => GUI.Label(r, text, Style(size, color, anchor, wrap, rich));

        /// Text with a 1-pixel shadow (on wood).
        public static void Shadowed(Rect r, string text, float size, Color color, Color shadow, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            float u = Mathf.Max(1f, U * 0.5f);
            GUI.Label(new Rect(r.x + u, r.y + u, r.width, r.height), text, Style(size, shadow, anchor));
            GUI.Label(r, text, Style(size, color, anchor));
        }

        public static float Height(string text, float size, float width, bool rich = false) => Style(size, Ink, TextAnchor.UpperLeft, true, rich).CalcHeight(new GUIContent(text), width);
        public static float Width(string text, float size) => Style(size, Ink).CalcSize(new GUIContent(text)).x;

        // ---------- controls ----------

        /// A wooden button. focused = has the pad focus (gold frame); on = chosen/toggled (pressed in).
        public static bool Button(Rect r, string text, bool focused = false, bool on = false, bool enabled = true, float size = 16f)
        {
            float u = U;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            if (focused) { Box(new Rect(r.x - 2 * u, r.y - 2 * u, r.width + 4 * u, r.height + 4 * u), Gold); }
            Box(r, Outline);
            var inner = new Rect(r.x + u, r.y + u, r.width - 2 * u, r.height - 2 * u);
            var face = !enabled ? new Color(0.5f, 0.42f, 0.34f) : on ? WoodDark : hover || focused ? WoodLight : Wood;
            Box(inner, face);
            if (!on) Box(new Rect(inner.x, inner.y, inner.width, u), enabled ? new Color(1f, 0.85f, 0.6f, 0.45f) : new Color(1f, 1f, 1f, 0.15f));
            Box(new Rect(inner.x, inner.yMax - 2 * u, inner.width, 2 * u), on ? new Color(0f, 0f, 0f, 0.25f) : WoodDark);
            var tr = new Rect(inner.x + 3 * u, inner.y + (on ? u : 0f), inner.width - 6 * u, inner.height - 2 * u);
            Shadowed(tr, text, size, !enabled ? new Color(0.85f, 0.8f, 0.72f) : on ? Gold : Cream, new Color(0.15f, 0.08f, 0.04f, 0.8f));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static GUIStyle _field;
        private static Texture2D _fieldTex;
        /// A text box: pale paper sunk into the sheet, ink text.
        public static string TextField(Rect r, string text, int max, float size, TextAnchor anchor = TextAnchor.MiddleCenter, string controlName = null)
        {
            if (_fieldTex == null)
            {
                _fieldTex = new Texture2D(6, 6) { filterMode = FilterMode.Point };
                for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++)
                    _fieldTex.SetPixel(x, y, x == 0 || y == 0 || x == 5 || y == 5 ? Outline : y == 4 ? PaperShade : new Color(0.98f, 0.94f, 0.82f));
                _fieldTex.Apply();
            }
            if (_field == null || _field.font != Font)
                _field = new GUIStyle { font = Font, border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(6, 6, 2, 2), clipping = TextClipping.Clip };
            _field.normal.background = _field.focused.background = _field.hover.background = _fieldTex;
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Ink;
            _field.alignment = anchor;
            _field.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * K));
            var cur = GUI.skin.settings.cursorColor; var sel = GUI.skin.settings.selectionColor;
            GUI.skin.settings.cursorColor = Ink; GUI.skin.settings.selectionColor = new Color(0.72f, 0.47f, 0.26f, 0.5f);
            if (controlName != null) GUI.SetNextControlName(controlName);
            text = GUI.TextField(r, text ?? "", max, _field);
            GUI.skin.settings.cursorColor = cur; GUI.skin.settings.selectionColor = sel;
            return text;
        }

        // Scroll views with a wooden scrollbar: the skin's bar is swapped while drawing, then put back.
        private static GUIStyle _vbar, _vthumb, _none, _oldBar, _oldThumb, _oldUp, _oldDown;
        private static Texture2D _trackTex, _thumbTex;
        public static Vector2 BeginScroll(Rect view, Vector2 pos, Rect content)
        {
            float u = U;
            if (_trackTex == null)
            {
                _trackTex = new Texture2D(3, 3) { filterMode = FilterMode.Point };
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) _trackTex.SetPixel(x, y, x == 1 ? PaperShade : PaperDark);
                _trackTex.Apply();
                _thumbTex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) _thumbTex.SetPixel(x, y, x == 0 || x == 3 || y == 0 || y == 3 ? Outline : x == 1 ? WoodLight : Wood);
                _thumbTex.Apply();
                _none = new GUIStyle { fixedHeight = 0.001f, fixedWidth = 0.001f };
            }
            _vbar = new GUIStyle { fixedWidth = 5 * u, margin = new RectOffset((int)(3 * u), 0, 0, 0) };
            _vbar.normal.background = _trackTex;
            _vthumb = new GUIStyle { fixedWidth = 5 * u, border = new RectOffset(1, 1, 1, 1) };
            _vthumb.normal.background = _thumbTex;
            var skin = GUI.skin;
            _oldBar = skin.verticalScrollbar; _oldThumb = skin.verticalScrollbarThumb; _oldUp = skin.verticalScrollbarUpButton; _oldDown = skin.verticalScrollbarDownButton;
            skin.verticalScrollbar = _vbar; skin.verticalScrollbarThumb = _vthumb; skin.verticalScrollbarUpButton = _none; skin.verticalScrollbarDownButton = _none;
            return GUI.BeginScrollView(view, pos, content, GUIStyle.none, _vbar);
        }

        public static void EndScroll()
        {
            GUI.EndScrollView();
            var skin = GUI.skin;
            skin.verticalScrollbar = _oldBar; skin.verticalScrollbarThumb = _oldThumb; skin.verticalScrollbarUpButton = _oldUp; skin.verticalScrollbarDownButton = _oldDown;
        }
    }
}
