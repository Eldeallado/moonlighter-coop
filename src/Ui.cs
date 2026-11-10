using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The look of every co-op window, taken from the game's own menus (pause and options): a warm grey-brown
    /// panel with the options' grain, rows of runic triangles, the pause menu's eye and tail, cream text in the
    /// game's font (Fairfax), the chosen thing in rune green between the game's spirals, ◂ ▸ arrows and
    /// bracketed step bars. The ornaments are the game's sprites, read at runtime (drawn shapes if missing).
    /// Sizes are in 720p points times k; one "art pixel" (U) is about the game's own pixel size.
    public static class Ui
    {
        public static Font Font;

        // Palette, measured on the game's options and pause menus.
        public static readonly Color Bg = new Color32(0x43, 0x3C, 0x31, 0xFF);
        public static readonly Color BgDark = new Color32(0x35, 0x2F, 0x27, 0xFF);
        public static readonly Color BgLight = new Color32(0x50, 0x48, 0x3B, 0xFF);
        public static readonly Color Line = new Color32(0x5E, 0x54, 0x45, 0xFF);
        public static readonly Color Tan = new Color32(0x7D, 0x70, 0x5C, 0xFF);
        public static readonly Color Green = new Color32(0x14, 0x9D, 0x72, 0xFF);
        public static readonly Color GreenLight = new Color32(0x2C, 0xC2, 0x8E, 0xFF);

        // Text inks (names kept from the first look, so every window follows the palette).
        public static readonly Color Ink = new Color32(0xEC, 0xDD, 0xC0, 0xFF);       // cream: normal text
        public static readonly Color InkSoft = new Color32(0xB3, 0xA6, 0x8C, 0xFF);   // secondary text
        public static readonly Color InkFaint = new Color32(0x79, 0x70, 0x5D, 0xFF);  // hints, lines, off values
        public static readonly Color Title = Green;                                    // what is chosen / important
        public static readonly Color Cream = Ink;
        public static readonly Color Gold = new Color32(0xE8, 0xC3, 0x5E, 0xFF);
        public static readonly Color Good = Green;
        public static readonly Color Bad = new Color32(0xDB, 0x66, 0x4C, 0xFF);
        public static readonly Color Outline = new Color32(0x24, 0x1F, 0x19, 0xFF);
        public static readonly Color Paper = Ink, PaperDark = BgLight, PaperShade = InkSoft;
        public static readonly Color Wood = Tan, WoodLight = new Color32(0x9A, 0x8B, 0x70, 0xFF), WoodDark = BgDark;

        // En el móvil la pantalla es pequeña: los menús ocupan casi todo el alto (el del Coop mide 540 de alto)
        public static float K => Teclas.Movil ? Mathf.Clamp(Screen.height / 560f, 0.5f, 3f) : Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
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

        // ---------- the game's sprites ----------

        private static readonly Dictionary<string, Texture2D> _sprites = new Dictionary<string, Texture2D>();
        private static float _nextLook;

        /// A sprite of the game as a small point-filtered texture (cut out of its atlas), or null if not loaded yet.
        public static Texture2D Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out var t) && t != null) return t;
            if (_sprites.ContainsKey(name) && Time.unscaledTime < _nextLook) return null;
            _nextLook = Time.unscaledTime + 2f;
            _sprites[name] = null;
            try
            {
                var sp = Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>().FirstOrDefault(s => s.name == name);
                if (sp == null) return null;
                var full = Recolor.ReadableCopy(sp.texture);
                var tr = sp.textureRect;
                t = new Texture2D((int)tr.width, (int)tr.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(full.GetPixels((int)tr.x, (int)tr.y, (int)tr.width, (int)tr.height));
                t.Apply();
                _sprites[name] = t;
                return t;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[ui] sprite " + name + ": " + e.Message); return null; }
        }

        /// Draws a sprite at the game's pixel scale (scale = screen pixels per sprite pixel), tinted, maybe mirrored.
        public static void Draw(Texture2D t, Rect r, Color tint, bool flipX = false, bool flipY = false)
        {
            if (t == null) return;
            var o = GUI.color; GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * o.a);
            GUI.DrawTextureWithTexCoords(r, t, new Rect(flipX ? 1f : 0f, flipY ? 1f : 0f, flipX ? -1f : 1f, flipY ? -1f : 1f));
            GUI.color = o;
        }

        /// A sprite repeated along a strip (the rows of triangles).
        public static void Tile(Texture2D t, Rect r, Color tint, bool flipY = false)
        {
            if (t == null) return;
            float w = t.width * U, h = r.height;
            int n = Mathf.Max(1, Mathf.FloorToInt(r.width / w));
            float start = r.x + (r.width - n * w) / 2f;
            for (int i = 0; i < n; i++) Draw(t, new Rect(start + i * w, r.y, w, h), tint, false, flipY);
        }

        // ---------- sounds (the game's own menu sounds) ----------

        public const string SndOpen = "gui_inventory_open", SndMove = "gui_selector_movement", SndPress = "gui_press_button", SndError = "gui_selector_error";
        public static void Sound(string name)
        {
            try { DarkTonic.MasterAudio.MasterAudio.PlaySoundAndForget(name); } catch { }
        }

        // ---------- opening animation ----------

        private static Matrix4x4 _openOld;
        private static Color _openColor;
        /// Starts drawing a window that opened at openedAt: it unfolds from a thin strip (like the pause menu)
        /// and fades in, in about a fifth of a second. Pair with EndOpen.
        public static void BeginOpen(float openedAt, Rect r)
        {
            _openOld = GUI.matrix; _openColor = GUI.color;
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.2f);
            if (t >= 1f) return;
            float e = 1f - Mathf.Pow(1f - t, 3f);                       // ease out
            float sy = Mathf.Lerp(0.08f, 1f, e) + Mathf.Sin(t * Mathf.PI) * 0.04f, sx = Mathf.Lerp(0.92f, 1f, e);
            GUIUtility.ScaleAroundPivot(new Vector2(sx, sy), r.center);
            GUI.color = new Color(_openColor.r, _openColor.g, _openColor.b, _openColor.a * Mathf.Clamp01(t * 2.5f));
        }
        public static void EndOpen() { GUI.matrix = _openOld; GUI.color = _openColor; }

        // ---------- shapes ----------

        public static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * o.a); GUI.DrawTexture(r, Px); GUI.color = o; }
        public static void Frame(Rect r, Color c, float w) { Box(new Rect(r.x, r.y, r.width, w), c); Box(new Rect(r.x, r.yMax - w, r.width, w), c); Box(new Rect(r.x, r.y, w, r.height), c); Box(new Rect(r.xMax - w, r.y, w, r.height), c); }

        /// A rectangle with pixel-art rounded corners (two steps of s).
        public static void Round(Rect r, Color c, float s)
        {
            if (s <= 0f || r.width < 4 * s || r.height < 4 * s) { Box(r, c); return; }
            Box(new Rect(r.x + 2 * s, r.y, r.width - 4 * s, r.height), c);
            Box(new Rect(r.x + s, r.y + s, r.width - 2 * s, r.height - 2 * s), c);
            Box(new Rect(r.x, r.y + 2 * s, r.width, r.height - 4 * s), c);
        }
        public static Rect Shrink(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

        /// Darkens the game behind a window (as the pause menu does).
        public static void Dim(float a = 0.6f) => Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.04f, 0.03f, a));

        /// A big window like the game's option screens: grey-brown with its grain, a row of tan triangles along the
        /// top and bottom, and the pause menu's green eye on top and tail underneath.
        public static void Sheet(Rect r)
        {
            float u = U;
            Round(new Rect(r.x + 2 * u, r.y + 3 * u, r.width, r.height), new Color(0f, 0f, 0f, 0.4f), 2 * u);   // shadow
            Round(r, Outline, 2 * u);
            Round(Shrink(r, u), Bg, 2 * u);
            // The grain of the options screen, rising from the bottom.
            var noise = Sprite("GUI_Options_bgNoise");
            if (noise != null)
            {
                float nh = Mathf.Min(r.height * 0.45f, noise.height * u * 1.5f);
                var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.55f * o.a);
                GUI.DrawTextureWithTexCoords(new Rect(r.x + 2 * u, r.yMax - nh - 2 * u, r.width - 4 * u, nh), noise, new Rect(0, 0, (r.width - 4 * u) / (noise.width * u * 1.5f), 1f));
                GUI.color = o;
            }
            // Triangle rows.
            var tri = Sprite("GUI_PopUp_Detail") ?? Sprite("GUI_Options_UpTrianglesBar_Tiled");
            float th = 15 * u * 0.75f;
            if (tri != null)
            {
                Tile(tri, new Rect(r.x + 14 * u, r.y + 5 * u, r.width - 28 * u, th), Color.white);
                Tile(tri, new Rect(r.x + 14 * u, r.yMax - 5 * u - th, r.width - 28 * u, th), Color.white, true);
            }
            else
            {
                Box(new Rect(r.x + 14 * u, r.y + 6 * u, r.width - 28 * u, u), Tan);
                Box(new Rect(r.x + 14 * u, r.yMax - 7 * u, r.width - 28 * u, u), Tan);
            }
            // The pause menu's eye on the top edge, and its tail hanging from the bottom.
            var eye = Sprite("GUI_Pause_Eye");
            if (eye != null) { float s = u; var er = new Rect(r.center.x - eye.width * s / 2f, r.y - eye.height * s / 2f, eye.width * s, eye.height * s); Round(Shrink(er, -u), Bg, u); Draw(eye, er, Color.white); }
            var tail = Sprite("GUI_Pause_Tail");
            if (tail != null) { float s = u; Draw(tail, new Rect(r.center.x - tail.width * s / 2f, r.yMax - 3 * u, tail.width * s, tail.height * s), Color.white); }
        }

        /// A small notice panel (the "are you coming?" box, the chat box): same look, smaller, no ornaments.
        public static void Scroll(Rect r, float a = 1f)
        {
            float u = U;
            var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a * o.a);
            Round(new Rect(r.x + u, r.y + 2 * u, r.width, r.height), new Color(0f, 0f, 0f, 0.35f), 2 * u);
            Round(r, Outline, 2 * u);
            Round(Shrink(r, u), Bg, 2 * u);
            var tri = Sprite("GUI_Pause_Triangles");
            float th = 15 * u * 0.5f;
            if (tri != null) { Tile(tri, new Rect(r.x + 10 * u, r.y + 4 * u, r.width - 20 * u, th), new Color(1f, 1f, 1f, 0.9f)); }
            else Box(new Rect(r.x + 10 * u, r.y + 4 * u, r.width - 20 * u, u), Green);
            GUI.color = o;
        }

        /// A small dark plate (HUD rows, tags over enemies).
        public static void Plate(Rect r, float a = 1f)
        {
            float u = U;
            var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a * o.a);
            Round(r, new Color(Outline.r, Outline.g, Outline.b, 0.9f), u);
            Round(Shrink(r, u), new Color(Bg.r, Bg.g, Bg.b, 0.92f), u);
            GUI.color = o;
        }

        /// A row or area: slightly darker than the window, with a thin line; lit = lighter, selected = green line.
        public static void Card(Rect r, bool lit = false, bool selected = false)
        {
            float u = U;
            Round(r, selected ? Green : Line, u);
            if (selected) Round(Shrink(r, u), Green, u);
            Round(Shrink(r, selected ? 2 * u : u), lit ? BgLight : BgDark, u);
        }

        /// A dark inset (the join code, the preview).
        public static void Slot(Rect r)
        {
            float u = U;
            Round(r, Line, u);
            Round(Shrink(r, u), new Color32(0x2A, 0x25, 0x1F, 0xFF), u);
        }

        /// A colour sample: rounded, outlined, with a little shine.
        public static void Swatch(Rect r, Color c)
        {
            float u = U;
            Round(r, Outline, u);
            Round(Shrink(r, u), c, u);
            Box(new Rect(r.x + 2 * u, r.y + 2 * u, 2 * u, u), new Color(1f, 1f, 1f, 0.45f));
        }

        /// The game's option arrow pointing left (-1) or right (+1); r is where it goes (centred inside).
        public static void Arrow(Rect r, int dir, Color c)
        {
            var t = Sprite("GUI_Options_Arrow");
            if (t != null)
            {
                float s = U, w = t.width * s, h = t.height * s;
                Draw(t, new Rect(r.center.x - w / 2f, r.center.y - h / 2f, w, h), c, dir < 0);
                return;
            }
            int rows = Mathf.Max(3, Mathf.RoundToInt(r.height / Mathf.Max(1f, U * 0.5f)));
            float rh = r.height / rows;
            for (int i = 0; i < rows; i++)
            {
                float tt = 1f - Mathf.Abs((i + 0.5f) / rows * 2f - 1f);
                float w = r.width * tt;
                Box(new Rect(dir > 0 ? r.x : r.xMax - w, r.y + i * rh, w, rh + 0.5f), c);
            }
        }

        /// The game's selection spirals on both sides of a piece of text centred in r (or of the whole rect).
        public static void Spirals(Rect r, float textWidth, Color c)
        {
            var t = Sprite("Character_Base_SpiralSet");
            float sw0 = t != null ? t.width : 8, sh0 = t != null ? t.height : 8;
            // Inside the rect: smaller spirals when the text leaves little room (packed rows of buttons).
            float s = U;
            if (textWidth > 0f && textWidth / 2f + 3 * s + sw0 * s > r.width / 2f) s = Mathf.Max(U * 0.5f, (r.width / 2f - textWidth / 2f - 2f) / (sw0 + 3f));
            float w = sw0 * s, h = sh0 * s;
            float half = textWidth > 0f ? Mathf.Min(r.width / 2f - w, textWidth / 2f + 3 * s) : r.width / 2f - w;
            float cy = r.center.y - h / 2f;
            // They breathe in and out a little, like the game's selector.
            float wob = Mathf.Round(Mathf.Sin(Time.unscaledTime * 5f) * 1.5f) * Mathf.Max(1f, s * 0.5f);
            var lr = new Rect(r.center.x - half - w - wob, cy, w, h);
            var rr = new Rect(r.center.x + half + wob, cy, w, h);
            if (t != null) { Draw(t, lr, c); Draw(t, rr, c, true); }
            else { Box(lr, c); Box(rr, c); }
        }

        /// A section heading: dim label with a line running to the right.
        public static void Section(Rect r, string text)
        {
            float u = U;
            float tw = Width(text, 15f);
            Label(new Rect(r.x, r.y, tw + 4, r.height), text, 15f, InkFaint);
            Box(new Rect(r.x + tw + 10 * K, r.y + r.height / 2f, r.width - tw - 10 * K, Mathf.Max(1f, u * 0.5f)), Line);
        }

        /// Horizontal divider: a line with the green underline of the options' tabs in the middle.
        public static void Divider(float x, float y, float w)
        {
            float u = U;
            Box(new Rect(x, y, w, Mathf.Max(1f, u * 0.5f)), Line);
            Box(new Rect(x + w / 2f - 40 * K, y - u * 0.5f, 80 * K, u), Green);
        }

        /// A progress bar. Tall ones (≥ 10 points) are the options' step bar: green squares between brackets;
        /// thin ones are a plain line (health).
        public static void Bar(Rect r, float frac, Color fill) => Bar(r, frac, fill, r.height >= 10 * K);
        public static void Bar(Rect r, float frac, Color fill, bool steps)
        {
            float u = U;
            frac = Mathf.Clamp01(frac);
            if (!steps)
            {
                Round(r, Outline, r.height >= 4 * u ? u * 0.5f : 0f);
                var inner = Shrink(r, Mathf.Max(1f, u * 0.5f));
                Box(inner, BgDark);
                if (frac > 0f) Box(new Rect(inner.x, inner.y, inner.width * frac, inner.height), fill);
                return;
            }
            Steps(r, frac, fill);
        }

        /// The options' step bar: [■■■■■- - - -], squares filled up to frac.
        public static void Steps(Rect r, float frac, Color fill)
        {
            float u = U;
            var corner = Sprite("GUI_Options_Bar_Corner");
            float cw = corner != null ? corner.width * u * (r.height / (corner.height * u)) : 3 * u;
            if (corner != null)
            {
                Draw(corner, new Rect(r.x, r.y, cw, r.height), Ink);
                Draw(corner, new Rect(r.xMax - cw, r.y, cw, r.height), Ink, true);
            }
            else { Box(new Rect(r.x, r.y, u, r.height), Ink); Box(new Rect(r.xMax - u, r.y, u, r.height), Ink); }
            var inner = new Rect(r.x + cw + u, r.y + r.height * 0.18f, r.width - 2 * cw - 2 * u, r.height * 0.64f);
            int n = Mathf.Clamp(Mathf.RoundToInt(inner.width / (inner.height * 1.15f)), 3, 30);
            float step = inner.width / n, sw = step - Mathf.Max(1f, u);
            int full = Mathf.RoundToInt(frac * n);
            if (frac > 0f && full == 0) full = 1;
            for (int i = 0; i < n; i++)
            {
                var cell = new Rect(inner.x + i * step, inner.y, sw, inner.height);
                if (i < full) Round(cell, fill, u * 0.5f);
                else Box(new Rect(cell.x, cell.center.y - Mathf.Max(1f, u * 0.5f) / 2f, sw * 0.7f, Mathf.Max(1f, u * 0.5f)), fill);   // the dashes
            }
        }

        /// A tick in a round mark (done) or an empty ring (not done).
        public static void Check(Rect r, bool on)
        {
            float u = U;
            var t = Sprite("actionable_base");
            if (t != null) Draw(t, r, on ? Green : Line);
            else Round(r, on ? Green : Line, u);
            if (!on) { if (t != null) Draw(t, Shrink(r, u), BgDark); return; }
            float s = Mathf.Max(u * 0.5f, Mathf.Round(r.width / 9f));
            float x0 = r.x + r.width * 0.22f, y0 = r.y + r.height * 0.48f;
            for (int i = 0; i < 2; i++) Box(new Rect(x0 + i * s, y0 + i * s, s * 1.4f, s * 1.4f), Ink);
            for (int i = 0; i < 4; i++) Box(new Rect(x0 + (2 + i) * s, y0 + (1 - i) * s, s * 1.4f, s * 1.4f), Ink);
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

        /// Text with a 1-pixel dark shadow.
        public static void Shadowed(Rect r, string text, float size, Color color, Color shadow, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            float u = Mathf.Max(1f, U * 0.5f);
            GUI.Label(new Rect(r.x + u, r.y + u, r.width, r.height), text, Style(size, shadow, anchor));
            GUI.Label(r, text, Style(size, color, anchor));
        }

        public static float Height(string text, float size, float width, bool rich = false) => Style(size, Ink, TextAnchor.UpperLeft, true, rich).CalcHeight(new GUIContent(text), width);
        public static float Width(string text, float size) => Style(size, Ink).CalcSize(new GUIContent(text)).x;

        // ---------- pad focus ----------

        /// Which of these rects comes next from rects[from] in a direction, as it looks on screen: the nearest one
        /// in line (sharing some width for up/down, some height for left/right), else anything within 45 degrees.
        /// Returns from when there is nothing that way.
        public static int Nearest(IList<Rect> rects, int from, float dx, float dy)
        {
            if (rects.Count == 0) return 0;
            if (from < 0 || from >= rects.Count) return 0;
            var fr = rects[from];
            int best = from; float bestScore = float.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                if (i == from) continue;
                var o = rects[i];
                Vector2 d = o.center - fr.center;
                float along = d.x * dx + d.y * dy, across = Mathf.Abs(d.x * dy) + Mathf.Abs(d.y * dx);
                if (along <= 1f) continue;
                bool inLine = dy != 0 ? o.xMax > fr.xMin && o.xMin < fr.xMax : o.yMax > fr.yMin && o.yMin < fr.yMax;
                float score;
                if (inLine) score = along + across * 0.01f;
                else if (along > across) score = 100000f + along + across * 2f;
                else continue;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // ---------- controls ----------

        /// Wood = a normal option, Gold = the main action of a window (green, bigger), Red = leaving.
        public enum Tone { Wood, Gold, Red }

        /// An option as the game draws them: just its text. With the pointer on it or the pad focus, the game's
        /// green spirals appear on both sides; on = chosen/toggled (green text); a faint plate shows where to click.
        public static bool Button(Rect r, string text, bool focused = false, bool on = false, bool enabled = true, float size = 16f, Tone tone = Tone.Wood, string icon = null)
        {
            float u = U;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            bool lit = focused || hover;
            // A very soft plate, so a column of options still reads as buttons.
            Round(r, new Color(BgLight.r, BgLight.g, BgLight.b, lit ? 0.9f : on ? 0.7f : 0.45f), u);
            if (on) Box(new Rect(r.x + 3 * u, r.yMax - u, r.width - 6 * u, u), Green);
            Color c = !enabled ? InkFaint : tone == Tone.Red ? Bad : tone == Tone.Gold || on ? (lit ? GreenLight : Green) : Ink;
            float s = tone == Tone.Gold ? size + 2f : size;
            // An icon from the game's art on the left, as tall as the text allows.
            float left = 0f;
            Texture2D ic = null;
            try { ic = icon != null ? Sprite(icon) : null; } catch { }
            if (ic != null && ic.width > 0 && ic.height > 0)
            {
                float ih = Mathf.Min(r.height - 6 * u, 13 * u), iw = ih * ic.width / ic.height;
                if (iw > ih * 1.6f) { iw = ih * 1.6f; ih = iw * ic.height / ic.width; }
                try { Draw(ic, new Rect(r.x + 6 * u, r.center.y - ih / 2f, iw, ih), enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f)); left = iw + 4 * u; } catch { }
            }
            var tr = new Rect(r.x + 4 * u + left, r.y, r.width - 8 * u - left * 2f, r.height);
            Shadowed(tr, text, s, c, new Color(0f, 0f, 0f, 0.45f));
            if (lit && enabled)
            {
                float tw = string.IsNullOrEmpty(text) ? 0f : Width(text, s);
                // Small keys (the on-screen keyboard): a green ring instead of spirals, which would hide the letter.
                if (tw > 0f && tw + 24 * u > tr.width) { Frame(r, Green, Mathf.Max(1f, u)); }
                else Spirals(tr, tw, tone == Tone.Red ? Bad : Green);
            }
            bool pressed = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (pressed) Sound(SndPress);
            return pressed;
        }

        /// A setting shown as ◂ value ▸ (like the game's options); clicking (or A) moves to the next value.
        public static bool Chooser(Rect r, string value, bool focused, bool enabled = true)
        {
            float k = K;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            bool lit = focused || hover;
            if (lit) Round(r, new Color(BgLight.r, BgLight.g, BgLight.b, 0.8f), U);
            var ac = lit ? Green : InkFaint;
            if (enabled)
            {
                Arrow(new Rect(r.x + 4 * k, r.y, 10 * k, r.height), -1, ac);
                Arrow(new Rect(r.xMax - 14 * k, r.y, 10 * k, r.height), 1, ac);
            }
            Label(new Rect(r.x + 16 * k, r.y, r.width - 32 * k, r.height), value, 16f, lit ? Green : Ink, TextAnchor.MiddleCenter);
            bool pressed = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (pressed) Sound(SndMove);
            return pressed;
        }

        private static GUIStyle _field;
        private static Texture2D _fieldTex;
        /// A text box: the game's cream option base, dark text.
        public static string TextField(Rect r, string text, int max, float size, TextAnchor anchor = TextAnchor.MiddleCenter, string controlName = null)
        {
            if (_fieldTex == null)
            {
                _fieldTex = new Texture2D(6, 6) { filterMode = FilterMode.Point };
                Color cream = new Color32(0xE1, 0xCE, 0xA9, 0xFF), rim = new Color32(0xBB, 0xA4, 0x77, 0xFF);
                for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++)
                    _fieldTex.SetPixel(x, y, (x == 0 || x == 5) && (y == 0 || y == 5) ? Color.clear : x == 0 || y == 0 || x == 5 || y == 5 ? rim : cream);
                _fieldTex.Apply();
            }
            if (_field == null || _field.font != Font)
                _field = new GUIStyle { font = Font, border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 2, 2), clipping = TextClipping.Clip };
            _field.normal.background = _field.focused.background = _field.hover.background = _fieldTex;
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Bg;
            _field.alignment = anchor;
            _field.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * K));
            var cur = GUI.skin.settings.cursorColor; var sel = GUI.skin.settings.selectionColor;
            GUI.skin.settings.cursorColor = Bg; GUI.skin.settings.selectionColor = new Color(Green.r, Green.g, Green.b, 0.45f);
            if (controlName != null) GUI.SetNextControlName(controlName);
            text = GUI.TextField(r, text ?? "", max, _field);
            GUI.skin.settings.cursorColor = cur; GUI.skin.settings.selectionColor = sel;
            return text;
        }

        // Scroll views with the options' scrollbar (a thin tan line, a green thumb): the skin's bar is swapped
        // while drawing, then put back.
        private static GUIStyle _vbar, _vthumb, _none, _oldBar, _oldThumb, _oldUp, _oldDown;
        private static Texture2D _trackTex, _thumbTex;
        public static Vector2 BeginScroll(Rect view, Vector2 pos, Rect content)
        {
            float u = U;
            if (_trackTex == null)
            {
                _trackTex = new Texture2D(3, 1) { filterMode = FilterMode.Point };
                _trackTex.SetPixel(0, 0, Color.clear); _trackTex.SetPixel(1, 0, Tan); _trackTex.SetPixel(2, 0, Color.clear); _trackTex.Apply();
                _thumbTex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                _thumbTex.SetPixel(0, 0, Green); _thumbTex.Apply();
                _none = new GUIStyle { fixedHeight = 0.001f, fixedWidth = 0.001f };
            }
            _vbar = new GUIStyle { fixedWidth = 3 * u, margin = new RectOffset((int)(4 * u), 0, 0, 0) };
            _vbar.normal.background = _trackTex;
            _vthumb = new GUIStyle { fixedWidth = 3 * u };
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
