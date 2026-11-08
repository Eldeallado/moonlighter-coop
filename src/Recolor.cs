using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// A player's look: body colour (Will's greens and teals) and hair colour, each an index into the lists below
    /// (0 = Will's own colour). Packed as one int: body * 16 + hair.
    public static class Looks
    {
        public struct Paint
        {
            public string Name; public float Hue; public float Sat; public float Light; public float Add;
            public Paint(string name, float hue, float sat, float light, float add = 0f) { Name = name; Hue = hue; Sat = sat; Light = light; Add = add; }
        }

        // Body: Will's teal becomes this. Sat/Light multiply the original shade, Add lifts dark shades.
        public static readonly Paint[] Body =
        {
            new Paint("Original (verde)", -1f, 1f, 1f),
            new Paint("Rojo", 0f, 1.35f, 1.25f, 0.03f),
            new Paint("Amarillo", 50f, 1.4f, 1.75f, 0.08f),
            new Paint("Azul", 222f, 1.35f, 1.3f, 0.04f),
            new Paint("Morado", 280f, 1.3f, 1.3f, 0.04f),
            new Paint("Naranja", 26f, 1.45f, 1.5f, 0.06f),
            new Paint("Rosa", 325f, 1.0f, 1.6f, 0.1f),
            new Paint("Blanco", 210f, 0.12f, 1.7f, 0.28f),
            new Paint("Negro", 230f, 0.25f, 0.5f, 0f),
            new Paint("Marrón", 24f, 1.1f, 1.0f, 0.02f),
        };

        // Hair: Will's cream hair (and its white highlights) becomes this. Light = brightness of the lightest shade.
        public static readonly Paint[] Hair =
        {
            new Paint("Original", -1f, 1f, 1f),
            new Paint("Rubio", 45f, 0.75f, 0.97f),
            new Paint("Castaño", 24f, 0.75f, 0.58f),
            new Paint("Negro", 230f, 0.28f, 0.34f),
            new Paint("Pelirrojo", 14f, 0.95f, 0.88f),
            new Paint("Blanco", 210f, 0.04f, 1.0f),
            new Paint("Gris", 220f, 0.06f, 0.7f),
            new Paint("Azul", 212f, 0.85f, 0.88f),
            new Paint("Rosa", 322f, 0.6f, 0.98f),
            new Paint("Verde", 125f, 0.8f, 0.8f),
            new Paint("Morado", 275f, 0.75f, 0.8f),
        };
        public static int Pack(int body, int hair) => Mathf.Clamp(body, 0, Body.Length - 1) * 16 + Mathf.Clamp(hair, 0, Hair.Length - 1);
        public static int BodyOf(int look) => look / 16;
        public static int HairOf(int look) => look % 16;

        /// Colour each player number gets until they pick one: host Will's own, then red, yellow, blue, purple...
        public static int Default(int playerId) => playerId <= 0 ? 0 : Pack(new[] { 0, 1, 2, 3, 4, 5, 6, 9, 7, 8 }[Mathf.Min(playerId, 9)], 0);

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
                var body = Looks.Body[Looks.BodyOf(look)];
                // Only Will's own sheets have his hair; weapons and armour keep their whites.
                var hair = src.name.IndexOf("will", System.StringComparison.OrdinalIgnoreCase) >= 0 ? Looks.Hair[Looks.HairOf(look)] : Looks.Hair[0];
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
        private float _rescan;

        private void LateUpdate()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            int look = Plugin.Role == "" && !Looks.Chosen ? 0 : Looks.Mine;
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
