using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A player's look: a colour for the body (Will's greens and teals) and one for the hair, each picked with
    /// three bars (tone, intensity, brightness) or left as Will's own. Packed in one int:
    /// part code = 0 (Will's own) or 1 + (tone*16 + intensity)*16 + brightness, tone 0..71 (5 degrees), others 0..15;
    /// look = body code | hair code << 15.
    public static class Looks
    {
        public struct Paint
        {
            public float Hue; public float Sat; public float Light; public float Add;
            public Paint(float hue, float sat, float light, float add = 0f) { Hue = hue; Sat = sat; Light = light; Add = add; }
        }

        public struct Part { public bool Own; public int Tone, Intensity, Bright; }

        public const int Tones = 72, Steps = 16;

        public static int Code(Part p) => p.Own ? 0 : 1 + (Mathf.Clamp(p.Tone, 0, Tones - 1) * Steps + Mathf.Clamp(p.Intensity, 0, Steps - 1)) * Steps + Mathf.Clamp(p.Bright, 0, Steps - 1);
        public static Part Decode(int code)
        {
            if (code <= 0) return new Part { Own = true, Tone = 0, Intensity = 12, Bright = 8 };
            code -= 1;
            return new Part { Tone = code / (Steps * Steps), Intensity = code / Steps % Steps, Bright = code % Steps };
        }
        public static int Pack(Part body, Part hair) => Code(body) | Code(hair) << 15;
        public static int BodyCode(int look) => look & 0x7FFF;
        public static int HairCode(int look) => look >> 15 & 0x7FFF;

        /// The colour a part's bars describe (also used to paint the bars and the preview swatch).
        public static Color Swatch(Part p, bool hair)
        {
            if (p.Own) return hair ? new Color32(0xDA, 0xD6, 0xB6, 0xFF) : new Color(0.12f, 0.5f, 0.48f);
            return Color.HSVToRGB(p.Tone * 5f / 360f, p.Intensity / 15f, 0.12f + 0.88f * p.Bright / 15f);
        }

        // Body: Will's teal (dark, saturated) becomes this tone; intensity and brightness scale its shades.
        public static Paint BodyPaint(int code)
        {
            if (code <= 0) return new Paint(-1f, 1f, 1f);
            var p = Decode(code);
            float s = p.Intensity / 15f, v = p.Bright / 15f;
            return new Paint(p.Tone * 5f, s * 1.45f, 0.4f + 1.6f * v, Mathf.Max(0f, v - 0.6f) * 0.5f);
        }

        // Hair: Will's cream hair; Light is the brightness of its lightest shade.
        public static Paint HairPaint(int code)
        {
            if (code <= 0) return new Paint(-1f, 1f, 1f);
            var p = Decode(code);
            return new Paint(p.Tone * 5f, p.Intensity / 15f, 0.28f + 0.72f * p.Bright / 15f);
        }

        // Starting colours by player number: host Will's own, then red, yellow, blue, purple, orange, pink...
        private static readonly int[,] Preset = { { 0, 14, 8 }, { 10, 15, 13 }, { 44, 14, 8 }, { 56, 13, 8 }, { 5, 15, 10 }, { 65, 10, 12 }, { 5, 11, 4 }, { 42, 1, 15 }, { 46, 3, 1 } };
        public static int Default(int playerId)
        {
            if (playerId <= 0) return 0;
            int i = (playerId - 1) % Preset.GetLength(0);
            return Pack(new Part { Tone = Preset[i, 0], Intensity = Preset[i, 1], Bright = Preset[i, 2] }, new Part { Own = true });
        }

        private static int _chosen = -1;
        public static int Mine => _chosen >= 0 ? _chosen : Default(Session.MyId);
        public static bool Chosen => _chosen >= 0;
        public static void Choose(int look)
        {
            _chosen = look;
            Plugin.SaveLook(look);
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
            if (look <= 0) return s;
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

        /// Drop the recoloured copies of looks nobody wears any more (dragging the bars makes many).
        public static void Prune(HashSet<int> keep)
        {
            foreach (var k in new List<(Texture2D, int)>(_tex.Keys))
                if (!keep.Contains(k.Item2)) { if (_tex[k] != null) Object.Destroy(_tex[k]); _tex.Remove(k); }
            foreach (var k in new List<(Sprite, int)>(_tinted.Keys))
                if (!keep.Contains(k.Item2))
                {
                    var r = _tinted[k];
                    _tinted.Remove(k);
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
                var body = Looks.BodyPaint(Looks.BodyCode(look));
                // Only Will's own sheets have his hair; weapons and armour keep their whites.
                var hair = src.name.IndexOf("will", System.StringComparison.OrdinalIgnoreCase) >= 0 ? Looks.HairPaint(Looks.HairCode(look)) : Looks.HairPaint(0);
                for (int i = 0; i < px.Length; i++) px[i] = Shift(px[i], body, hair);
                copy.SetPixels32(px);
                copy.filterMode = src.filterMode;
                copy.wrapMode = src.wrapMode;
                copy.Apply(false, true);
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

        private static Color32 Shift(Color32 c, Looks.Paint body, Looks.Paint hair)
        {
            if (c.a == 0) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color32 o;
            // Will's greens and teals (about 75..210 degrees) become the same shades of the player's colour.
            if (body.Hue >= 0f && s >= 0.15f && h >= 75f / 360f && h <= 210f / 360f)
            {
                float nh = body.Hue / 360f + (h - 150f / 360f) * 0.25f;
                nh -= Mathf.Floor(nh);
                o = (Color32)Color.HSVToRGB(nh, Mathf.Clamp01(s * body.Sat + (body.Sat > 0.5f ? 0.1f : 0f)), Mathf.Clamp01(v * body.Light + body.Add));
            }
            else if (hair.Hue >= 0f && IsHair(h, s, v))
            {
                // Hair is pale: give it the colour's saturation, keep the shading from the original brightness.
                float nh = hair.Hue / 360f + (s < 0.03f ? 0f : (h - HairCenter / 360f) * 0.25f);
                nh -= Mathf.Floor(nh);
                o = (Color32)Color.HSVToRGB(nh, Mathf.Clamp01(hair.Sat * (0.55f + s * 2.5f)), Mathf.Clamp01(hair.Light * (0.25f + 0.75f * v)));
            }
            else return c;
            o.a = c.a;
            return o;
        }
        public static float HairCenter = 54f;
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
