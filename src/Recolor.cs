using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A player's look: a colour for each part of Will (vest, shirt, trousers, backpack, shoes, the gem on his
    /// neck, hair and skin), each picked with three bars (tone, intensity, brightness) or left as Will's own.
    /// part code = 0 (Will's own) or 1 + (tone*16 + intensity)*16 + brightness, tone 0..71 (5 degrees), others 0..15.
    /// A look travels as one int: old looks (0.4 and before) are "vest | hair << 15"; new ones are an id
    /// (bit 30 set) whose eight codes every player learns from Msg.LookData.
    public static class Looks
    {
        public struct Paint
        {
            public float Hue; public float Sat; public float Light; public float Add;
            public Paint(float hue, float sat, float light, float add = 0f) { Hue = hue; Sat = sat; Light = light; Add = add; }
        }

        public struct Part { public bool Own; public int Tone, Intensity, Bright; }

        public const int Tones = 72, Steps = 16;

        // The parts, in menu order. "Pale" parts (hair, shirt) are painted keeping their light shading.
        public const int Chaleco = 0, Camiseta = 1, Pantalon = 2, Mochila = 3, Zapatos = 4, Gema = 5, Pelo = 6, Piel = 7, N = 8;
        public static readonly string[] Names = { "Chaleco", "Camiseta", "Pantalón", "Mochila", "Zapatos", "Gema", "Pelo", "Piel" };
        public static bool Pale(int part) => part == Pelo || part == Camiseta;
        /// Each part's own colour in Will's art (swatch for "as Will", and the centre its shades turn around).
        public static readonly Color[] OwnColor = {
            new Color32(0x0E, 0x56, 0x5D, 255), new Color32(0xBB, 0xB5, 0x9D, 255), new Color32(0xAD, 0x58, 0x17, 255), new Color32(0x84, 0x59, 0x2A, 255),
            new Color32(0x57, 0x25, 0x21, 255), new Color32(0x30, 0xFB, 0x9B, 255), new Color32(0xDA, 0xD6, 0xB6, 255), new Color32(0xEE, 0xBD, 0x87, 255) };
        public static readonly float[] CenterHue = { 150f, 47f, 23f, 29f, 3f, 152f, 54f, 32f };
        /// A starting point when a part stops being "as Will" (close to Will's own colour).
        public static readonly int[,] Start = { { 34, 13, 6 }, { 9, 3, 11 }, { 5, 14, 9 }, { 6, 11, 6 }, { 1, 10, 5 }, { 30, 13, 12 }, { 11, 3, 14 }, { 6, 7, 13 } };

        public static int Code(Part p) => p.Own ? 0 : 1 + (Mathf.Clamp(p.Tone, 0, Tones - 1) * Steps + Mathf.Clamp(p.Intensity, 0, Steps - 1)) * Steps + Mathf.Clamp(p.Bright, 0, Steps - 1);
        public static Part Decode(int code)
        {
            if (code <= 0) return new Part { Own = true, Tone = 0, Intensity = 12, Bright = 8 };
            code -= 1;
            return new Part { Tone = code / (Steps * Steps), Intensity = code / Steps % Steps, Bright = code % Steps };
        }
        public static Part P(int tone, int intensity, int bright) => new Part { Tone = tone, Intensity = intensity, Bright = bright };

        // ---------- looks as ints ----------

        private const int NewFlag = 1 << 30;
        private static readonly Dictionary<int, int[]> _data = new Dictionary<int, int[]>();

        public static bool IsNew(int look) => (look & NewFlag) != 0;
        /// The look is ready to paint: an old-style one, or a new one whose codes have arrived.
        public static bool Known(int look) => look <= 0 || !IsNew(look) || _data.ContainsKey(look);

        /// The eight part codes of a look (old looks: vest and gem share the body colour, as they always did).
        public static int[] Codes(int look)
        {
            var c = new int[N];
            if (look <= 0) return c;
            if (IsNew(look)) { if (_data.TryGetValue(look, out var d)) d.CopyTo(c, 0); return c; }
            c[Chaleco] = c[Gema] = look & 0x7FFF;
            c[Pelo] = look >> 15 & 0x7FFF;
            return c;
        }

        /// The int that stands for these codes (all "as Will" = 0).
        public static int Register(int[] codes)
        {
            bool any = false; foreach (var x in codes) if (x != 0) any = true;
            if (!any) return 0;
            unchecked
            {
                int h = 17; foreach (var x in codes) h = h * 31 + x;
                int id = (h & 0x3FFFFFFF) | NewFlag;
                _data[id] = (int[])codes.Clone();
                return id;
            }
        }
        public static int Pack(Part[] parts) { var c = new int[N]; for (int i = 0; i < N; i++) c[i] = Code(parts[i]); return Register(c); }
        public static Part[] Unpack(int look) { var c = Codes(look); var p = new Part[N]; for (int i = 0; i < N; i++) p[i] = Decode(c[i]); return p; }

        public static string ToText(int look) => string.Join(",", System.Array.ConvertAll(Codes(look), x => x.ToString()));
        public static int FromText(string s)
        {
            var parts = (s ?? "").Split(',');
            if (parts.Length != N) return -1;
            var c = new int[N];
            for (int i = 0; i < N; i++) if (!int.TryParse(parts[i], out c[i]) || c[i] < 0 || c[i] > Tones * Steps * Steps) return -1;
            return Register(c);
        }

        /// Msg.LookData: a look id and its eight codes, so the others can paint it.
        public static byte[] DataBytes(int look)
        {
            var ms = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(ms);
            w.Write(look); foreach (var x in Codes(look)) w.Write((short)x);
            return ms.ToArray();
        }
        public static void Learn(byte[] data)
        {
            if (data == null || data.Length != 4 + 2 * N) return;
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
            int id = r.ReadInt32(); var c = new int[N];
            for (int i = 0; i < N; i++) { c[i] = r.ReadInt16(); if (c[i] < 0 || c[i] > Tones * Steps * Steps) return; }
            if (IsNew(id) && Register(c) == id) return;   // only ids that really match their codes
        }

        // ---------- colours ----------

        /// The colour a part's bars describe (also used to paint the bars and the swatches).
        public static Color Swatch(Part p, int part)
        {
            if (p.Own) return OwnColor[part];
            return Color.HSVToRGB(p.Tone * 5f / 360f, p.Intensity / 15f, 0.12f + 0.88f * p.Bright / 15f);
        }

        public static Paint PaintOf(int code, int part)
        {
            if (code <= 0) return new Paint(-1f, 1f, 1f);
            var p = Decode(code);
            if (Pale(part)) return new Paint(p.Tone * 5f, p.Intensity / 15f, 0.28f + 0.72f * p.Bright / 15f);
            float s = p.Intensity / 15f, v = p.Bright / 15f;
            return new Paint(p.Tone * 5f, s * 1.45f, 0.4f + 1.6f * v, Mathf.Max(0f, v - 0.6f) * 0.5f);
        }

        // ---------- styles: whole looks inspired by people of Rynoka and the dungeons ----------

        public struct Style { public string Name; public Part[] Parts; }
        private static Part O => new Part { Own = true };
        public static readonly Style[] Styles = {
            new Style { Name = "Will", Parts = new[] { O, O, O, O, O, O, O, O } },
            new Style { Name = "Zenón", Parts = new[] { P(24, 11, 6), P(10, 2, 13), P(6, 11, 4), P(6, 10, 5), P(5, 9, 3), P(24, 12, 12), P(0, 0, 15), O } },
            new Style { Name = "Herrero", Parts = new[] { P(5, 3, 3), P(6, 3, 8), P(5, 12, 4), P(4, 9, 3), P(0, 3, 2), P(2, 15, 12), P(4, 6, 2), P(5, 8, 10) } },
            new Style { Name = "Bruja", Parts = new[] { P(55, 12, 5), P(58, 4, 12), P(52, 10, 3), P(60, 8, 4), P(52, 8, 2), P(62, 14, 13), P(56, 7, 6), O } },
            new Style { Name = "Pete", Parts = new[] { P(1, 14, 6), P(10, 7, 14), P(42, 9, 4), P(6, 12, 6), P(4, 10, 3), P(10, 15, 14), P(6, 14, 10), O } },
            new Style { Name = "Gólem", Parts = new[] { P(36, 2, 6), P(36, 1, 10), P(36, 2, 4), P(8, 4, 5), P(36, 2, 2), P(36, 14, 14), P(0, 0, 12), P(36, 2, 12) } },
            new Style { Name = "Bosque", Parts = new[] { P(22, 13, 5), P(16, 6, 12), P(8, 11, 4), P(18, 12, 4), P(7, 10, 2), P(26, 15, 13), P(12, 9, 7), O } },
            new Style { Name = "Desierto", Parts = new[] { P(8, 9, 9), P(9, 4, 14), P(6, 9, 7), P(7, 9, 8), P(5, 8, 4), P(36, 14, 14), P(7, 4, 15), P(6, 9, 9) } },
            new Style { Name = "Tecnología", Parts = new[] { P(38, 14, 6), P(40, 2, 13), P(40, 6, 3), P(40, 4, 6), P(40, 5, 2), P(36, 15, 15), P(38, 8, 13), O } },
            new Style { Name = "Fantasma", Parts = new[] { P(48, 3, 13), P(48, 1, 15), P(48, 3, 11), P(48, 3, 12), P(48, 3, 10), P(46, 10, 15), P(48, 1, 15), P(48, 1, 15) } },
        };

        // ---------- the player's own styles, and codes to share a look ----------

        /// Styles saved by this player (config "Aspecto/MisEstilos": "name=code|name=code").
        public static readonly List<Style> Mine2 = new List<Style>();
        public static IEnumerable<Style> AllStyles { get { foreach (var s in Styles) yield return s; foreach (var s in Mine2) yield return s; } }

        public static void LoadMyStyles(string text)
        {
            Mine2.Clear();
            foreach (var part in (text ?? "").Split('|'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                int look = FromShareCode(kv[1]);
                if (look >= 0) Mine2.Add(new Style { Name = kv[0], Parts = Unpack(look) });
            }
        }
        private static string MyStylesText() => string.Join("|", Mine2.ConvertAll(s => s.Name.Replace("|", "").Replace("=", "") + "=" + ShareCode(Pack(s.Parts))).ToArray());

        public static void SaveStyle(Part[] parts)
        {
            if (Mine2.Count >= 5) Mine2.RemoveAt(0);   // one more row of buttons at most
            int n = 1; while (Mine2.Exists(s => s.Name == "Mío " + n)) n++;
            Mine2.Add(new Style { Name = "Mío " + n, Parts = (Part[])parts.Clone() });
            Plugin.SaveMyStyles(MyStylesText());
        }
        public static void DeleteStyle(int i) { if (i >= 0 && i < Mine2.Count) { Mine2.RemoveAt(i); Plugin.SaveMyStyles(MyStylesText()); } }

        /// "ML-" + each part code in base 36 (3 characters each): short enough to send by chat or WhatsApp.
        public static string ShareCode(int look)
        {
            const string D = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new System.Text.StringBuilder("ML-");
            foreach (var c in Codes(look)) { int x = c; sb.Append(D[x / 1296 % 36]).Append(D[x / 36 % 36]).Append(D[x % 36]); }
            return sb.ToString();
        }
        public static int FromShareCode(string s)
        {
            const string D = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            s = (s ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (s.StartsWith("ML-")) s = s.Substring(3);
            if (s.Length != N * 3) return -1;
            var c = new int[N];
            for (int i = 0; i < N; i++)
            {
                int a = D.IndexOf(s[i * 3]), b = D.IndexOf(s[i * 3 + 1]), d = D.IndexOf(s[i * 3 + 2]);
                if (a < 0 || b < 0 || d < 0) return -1;
                c[i] = a * 1296 + b * 36 + d;
                if (c[i] > Tones * Steps * Steps) return -1;
            }
            return Register(c);
        }

        // Starting colours by player number: host Will's own, then red, yellow, blue, purple, orange, pink... (vest only)
        private static readonly int[,] Preset = { { 0, 14, 8 }, { 10, 15, 13 }, { 44, 14, 8 }, { 56, 13, 8 }, { 5, 15, 10 }, { 65, 10, 12 }, { 5, 11, 4 }, { 42, 1, 15 }, { 46, 3, 1 } };
        public static int Default(int playerId)
        {
            if (playerId <= 0) return 0;
            int i = (playerId - 1) % Preset.GetLength(0);
            return Code(P(Preset[i, 0], Preset[i, 1], Preset[i, 2]));   // old style: vest (and gem) only
        }

        private static int _chosen = -1;
        public static int Mine => _chosen >= 0 ? _chosen : Default(Session.MyId);
        public static bool Chosen => _chosen >= 0;
        public static void Choose(int look)
        {
            _chosen = look;
            Plugin.SaveLook(look);
            if (Net.Connected && IsNew(look)) Net.Send(Msg.LookData, DataBytes(look));
        }
        public static void LoadSaved(int look) { _chosen = look; }

        /// Other players' looks, from their poses.
        public static readonly Dictionary<int, int> Of = new Dictionary<int, int>();
    }

    /// Recoloured copies of Will's art: the same sprites, with body and hair shades swapped for the chosen colours.
    public static class Recolor
    {
        private static readonly Dictionary<(Texture2D, int), Texture2D> _tex = new Dictionary<(Texture2D, int), Texture2D>();
        private static readonly Dictionary<(Sprite, int), Sprite> _tinted = new Dictionary<(Sprite, int), Sprite>();
        private static readonly Dictionary<Sprite, Sprite> _original = new Dictionary<Sprite, Sprite>();
        private static readonly Dictionary<Sprite, int> _lookOf = new Dictionary<Sprite, int>();

        public static bool IsTinted(Sprite s) => s != null && _original.ContainsKey(s);
        public static Sprite Original(Sprite s) => s != null && _original.TryGetValue(s, out var o) ? o : s;
        public static int LookOf(Sprite s) => s != null && _lookOf.TryGetValue(s, out var l) ? l : 0;

        public static Sprite Tint(Sprite s, int look)
        {
            if (s == null || s.texture == null) return s;
            s = Original(s);
            if (look <= 0 || !Looks.Known(look)) return s;   // a new look whose colours have not arrived yet
            if (_tinted.TryGetValue((s, look), out var r) && r != null) return r;
            var tex = TintTexture(s.texture, look);
            if (tex == null) return s;
            // Will's sprites are tightly packed in an atlas (no rectangle to cut): rebuild the exact same mesh on
            // the tinted atlas. Rect = the atlas area its UVs cover; geometry = its own triangles; pivot such that
            // every vertex lands where the original's does.
            var uv = s.uv; var verts = s.vertices; var tris = s.triangles;
            float W = tex.width, H = tex.height, ppu = s.pixelsPerUnit;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var u in uv) { minX = Mathf.Min(minX, u.x * W); minY = Mathf.Min(minY, u.y * H); maxX = Mathf.Max(maxX, u.x * W); maxY = Mathf.Max(maxY, u.y * H); }
            var tr = new Rect(Mathf.Floor(minX), Mathf.Floor(minY), Mathf.Max(1f, Mathf.Ceil(maxX) - Mathf.Floor(minX)), Mathf.Max(1f, Mathf.Ceil(maxY) - Mathf.Floor(minY)));
            var local = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++) local[i] = new Vector2(uv[i].x * W - tr.x, uv[i].y * H - tr.y);
            var pivotPx = local[0] - verts[0] * ppu;
            r = Sprite.Create(tex, tr, new Vector2(pivotPx.x / tr.width, pivotPx.y / tr.height), ppu, 0, SpriteMeshType.Tight);
            try
            {
                for (int i = 0; i < local.Length; i++) local[i] = new Vector2(Mathf.Clamp(local[i].x, 0f, tr.width), Mathf.Clamp(local[i].y, 0f, tr.height));
                r.OverrideGeometry(local, tris);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[recolor] geometry {s.name}: {e.Message}"); }
            r.name = s.name;
            _tinted[(s, look)] = r;
            _original[r] = s;
            _lookOf[r] = look;
            return r;
        }

        private static readonly Dictionary<Sprite, Sprite> _ui = new Dictionary<Sprite, Sprite>();
        private static readonly Dictionary<Texture2D, Color32[]> _pixels = new Dictionary<Texture2D, Color32[]>();

        /// The game's menus (the inventory's Will preview) draw a sprite into a box using its full frame, padding
        /// included. A tinted sprite only covers the drawn part, so it came out stretched (a giant Will): give the
        /// menu a copy with the original frame, cut out of the tinted sheet triangle by triangle.
        public static Sprite UiSprite(Sprite tinted)
        {
            if (tinted == null || !IsTinted(tinted)) return tinted;
            if (_ui.TryGetValue(tinted, out var u) && u != null) return u;
            var orig = Original(tinted);
            try
            {
                var src = tinted.texture;
                int fw = Mathf.Max(1, Mathf.RoundToInt(orig.rect.width)), fh = Mathf.Max(1, Mathf.RoundToInt(orig.rect.height));
                var outPx = new Color32[fw * fh];
                if (!_pixels.TryGetValue(src, out var srcPx)) _pixels[src] = srcPx = src.GetPixels32();
                int W = src.width, H = src.height;
                var verts = orig.vertices; var uvs = orig.uv; var tris = orig.triangles;
                float ppu = orig.pixelsPerUnit;
                var fp = new Vector2[verts.Length];
                for (int i = 0; i < verts.Length; i++) fp[i] = verts[i] * ppu + orig.pivot;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector2 a = fp[tris[t]], b = fp[tris[t + 1]], c = fp[tris[t + 2]];
                    Vector2 ua = uvs[tris[t]], ub = uvs[tris[t + 1]], uc = uvs[tris[t + 2]];
                    float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                    if (Mathf.Abs(den) < 1e-6f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(fw - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(fh - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float l1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / den;
                            float l2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / den;
                            float l3 = 1f - l1 - l2;
                            if (l1 < -0.001f || l2 < -0.001f || l3 < -0.001f) continue;
                            var uv = ua * l1 + ub * l2 + uc * l3;
                            int sx = Mathf.Clamp((int)(uv.x * W), 0, W - 1), sy = Mathf.Clamp((int)(uv.y * H), 0, H - 1);
                            outPx[y * fw + x] = srcPx[sy * W + sx];
                        }
                }
                var tex = new Texture2D(fw, fh, TextureFormat.RGBA32, false) { filterMode = src.filterMode, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(outPx);
                tex.Apply(false, true);
                u = Sprite.Create(tex, new Rect(0, 0, fw, fh), new Vector2(orig.pivot.x / fw, orig.pivot.y / fh), ppu, 0, SpriteMeshType.FullRect);
                u.name = orig.name + " (coop ui)";
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[recolor] ui copy of {orig.name}: {e.Message}"); u = orig; }
            _ui[tinted] = u;
            return u;
        }

        private static bool IsUiCopy(Sprite s) => s.name.EndsWith(" (coop ui)");

        /// Drop the recoloured copies of looks nobody wears any more (dragging the bars makes many).
        public static void Prune(HashSet<int> keep)
        {
            foreach (var k in new List<(Texture2D, int)>(_tex.Keys))
                if (!keep.Contains(k.Item2)) { if (_tex[k] != null) { _pixels.Remove(_tex[k]); Object.Destroy(_tex[k]); } _tex.Remove(k); }
            foreach (var k in new List<(Sprite, int)>(_tinted.Keys))
                if (!keep.Contains(k.Item2))
                {
                    var r = _tinted[k];
                    _tinted.Remove(k);
                    if (r != null && _ui.TryGetValue(r, out var ui)) { _ui.Remove(r); if (ui != null && IsUiCopy(ui)) { Object.Destroy(ui.texture); Object.Destroy(ui); } }
                    if (r != null) { _original.Remove(r); _lookOf.Remove(r); Object.Destroy(r); }
                }
        }

        /// Sprite sheets are not readable: copy through the GPU.
        public static Texture2D ReadableCopy(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Texture2D TintTexture(Texture2D src, int look)
        {
            if (_tex.TryGetValue((src, look), out var t)) return t;
            try
            {
                var copy = ReadableCopy(src);
                var px = copy.GetPixels32();
                var codes = Looks.Codes(look);
                var paints = new Looks.Paint[Looks.N];
                // Only Will's own sheets have his hair, shirt, skin...; weapons and armour only get the vest colour.
                bool will = src.name.IndexOf("will", System.StringComparison.OrdinalIgnoreCase) >= 0;
                for (int i = 0; i < Looks.N; i++) paints[i] = Looks.PaintOf(will || i == Looks.Chaleco ? codes[i] : 0, i);
                var memo = new Dictionary<int, Color32>();   // pixel art: few colours, many pixels
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i]; if (c.a == 0) continue;
                    int key = c.r << 24 | c.g << 16 | c.b << 8 | c.a;
                    if (!memo.TryGetValue(key, out var o)) memo[key] = o = Shift(c, paints);
                    px[i] = o;
                }
                copy.SetPixels32(px);
                copy.filterMode = src.filterMode;
                copy.wrapMode = src.wrapMode;
                copy.Apply(false, false);   // stays readable: the inventory preview copies frames out of it (UiSprite)
                copy.name = src.name + " (coop " + look + ")";
                t = copy;
                Plugin.Log.LogInfo($"[recolor] look {look} copy of {src.name} ({src.width}x{src.height})");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[recolor] {src.name}: {e.Message}");
                t = null;
            }
            _tex[(src, look)] = t;
            return t;
        }

        /// Will's hair: the warm light browns of his hair (not the skin, which is pinker and lighter).
        public static bool IsHair(float h, float s, float v) => HairRule(h * 360f, s, v);
        // Hair palette: DAD6B6, AAA78E, 8D8A75, F0ECCC, FFFEF1 (hue 53-56) + pure white highlights.
        // The jacket sleeves are close (BBB59D, 9C957A... hue 45-48), so the hue window is tight.
        public static System.Func<float, float, float, bool> HairRule = (h, s, v) => (h >= 51f && h <= 60f && s >= 0.03f && s <= 0.21f && v >= 0.5f) || (s < 0.03f && v > 0.97f);

        // Will's shoes: dark reds that only his shoes use (the darkest red is shared with outlines and stays).
        private static readonly HashSet<int> ShoeColors = new HashSet<int> { 0x200909, 0x572521, 0x3A1010, 0x5C201B, 0x542420 };

        /// Which part of Will a colour of his art belongs to (-1: none, keep it). Measured on his sheets:
        /// vest teals 002F36/0E565D/02424C, gem 30FB9B/42A475, shirt BBB59D/9C957A/7B7560, trousers AD5817/7B310F,
        /// backpack 84592A/714924/634124, skin EEBD87/FAD08F/DE9D55/B56C2C, hair DAD6B6/AAA78E/F0ECCC.
        public static int PartOf(Color32 c, float h, float s, float v)
        {
            h *= 360f;
            if (ShoeColors.Contains(c.r << 16 | c.g << 8 | c.b)) return Looks.Zapatos;
            if (h >= 140f && h <= 166f && s >= 0.5f && v >= 0.55f) return Looks.Gema;
            if (s >= 0.15f && h >= 75f && h <= 210f) return Looks.Chaleco;
            if (HairRule(h, s, v)) return Looks.Pelo;
            if (h >= 44f && h <= 50.5f && s >= 0.14f && s <= 0.24f && v >= 0.28f && v <= 0.76f) return Looks.Camiseta;
            if (h >= 16f && h <= 27f && s >= 0.84f && v >= 0.44f && v <= 0.72f) return Looks.Pantalon;
            if (h >= 24f && h <= 33f && s >= 0.55f && s <= 0.74f && v >= 0.25f && v <= 0.53f) return Looks.Mochila;
            if (h >= 26f && h <= 37f && s >= 0.4f && s <= 0.78f && v >= 0.69f) return Looks.Piel;
            return -1;
        }

        private static Color32 Shift(Color32 c, Looks.Paint[] paints)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            int part = PartOf(c, h, s, v);
            if (part < 0) return c;
            var p = paints[part];
            if (p.Hue < 0f) return c;
            Color32 o;
            if (Looks.Pale(part))
            {
                // Pale parts: give them the colour's saturation, keep the shading from the original brightness.
                float nh = p.Hue / 360f + (s < 0.03f ? 0f : (h - Looks.CenterHue[part] / 360f) * 0.25f);
                nh -= Mathf.Floor(nh);
                o = (Color32)Color.HSVToRGB(nh, Mathf.Clamp01(p.Sat * (0.55f + s * 2.5f)), Mathf.Clamp01(p.Light * (0.25f + 0.75f * v)));
            }
            else
            {
                // The same shades of the player's colour (hue spread kept small so the shading reads the same).
                float nh = p.Hue / 360f + (h - Looks.CenterHue[part] / 360f) * 0.25f;
                nh -= Mathf.Floor(nh);
                o = (Color32)Color.HSVToRGB(nh, Mathf.Clamp01(s * p.Sat + (p.Sat > 0.5f ? 0.1f : 0f)), Mathf.Clamp01(v * p.Light + p.Add));
            }
            o.a = c.a;
            return o;
        }        public static float HairCenter = 54f;
        public static Color HairSwatch = new Color32(0xDA, 0xD6, 0xB6, 0xFF);
    }

    /// After the animator picks this frame's sprites, swap Will's to this player's look (any player, host included).
    public class RecolorLocalWill : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private float _rescan, _prune;

        private void LateUpdate()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            int look = Plugin.Role == "" && !Looks.Chosen ? 0 : Looks.Mine;
            if (Time.unscaledTime > _prune)
            {
                _prune = Time.unscaledTime + 3f;
                var keep = new HashSet<int>(Looks.Of.Values) { look, 0 };
                Recolor.Prune(keep);
            }
            if (Time.unscaledTime > _rescan)
            {
                _rescan = Time.unscaledTime + 1f;
                _renderers.Clear();
                var art = hero.transform.Find("HeroMerchantArt");
                if (art != null) art.GetComponentsInChildren(true, _renderers);
            }
            foreach (var sr in _renderers)
            {
                if (sr == null || sr.sprite == null || sr.name.Contains("Light")) continue;
                if (Recolor.LookOf(sr.sprite) == look) continue;
                sr.sprite = Recolor.Tint(sr.sprite, look);
            }
        }
    }
}

namespace MoonlighterCoop
{
    // The inventory's Will preview copies Will's current sprites into menu images: give them frame-sized copies
    // of the recoloured ones (see Recolor.UiSprite), or Will shows up giant over the right page.
    [HarmonyLib.HarmonyPatch(typeof(HeroInventoryPanel), "UpdateWillPreview")]
    static class InventoryPreviewSize
    {
        static void Postfix(HeroInventoryPanel __instance)
        {
            foreach (var img in new[] { __instance.imageBasePreview, __instance.imageHeadPreview, __instance.imageChestPreview, __instance.imageBootsPreview,
                                        __instance.imageExtraHeadPreview, __instance.imageExtraChestPreview, __instance.imageExtraBootsPreview })
                if (img != null && img.overrideSprite != null && Recolor.IsTinted(img.overrideSprite)) img.overrideSprite = Recolor.UiSprite(img.overrideSprite);
        }
    }
}
