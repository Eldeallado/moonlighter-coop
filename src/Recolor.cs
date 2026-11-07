using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Each guest's Will: exactly the same art with the greens turned into that player's colour
    /// (guest 1 red, guest 2 yellow, ...). The host's Will is never touched.
    public static class Recolor
    {
        // Target hue (degrees) per player number.
        private static readonly float[] Hues = { 0f, 0f, 46f, 280f, 30f };
        // Brightness boost per player: a dark yellow reads olive, so yellow gets more light.
        private static readonly float[] Light = { 1.25f, 1.25f, 1.75f, 1.3f, 1.4f };

        private static readonly Dictionary<(Texture2D, int), Texture2D> _tex = new Dictionary<(Texture2D, int), Texture2D>();
        private static readonly Dictionary<(Sprite, int), Sprite> _tinted = new Dictionary<(Sprite, int), Sprite>();
        private static readonly Dictionary<Sprite, Sprite> _original = new Dictionary<Sprite, Sprite>();

        public static bool IsTinted(Sprite s) => s != null && _original.ContainsKey(s);
        public static Sprite Original(Sprite s) => s != null && _original.TryGetValue(s, out var o) ? o : s;

        public static Sprite Tint(Sprite s, int player)
        {
            if (s == null || s.texture == null || player <= 0) return s;
            s = Original(s);
            if (_tinted.TryGetValue((s, player), out var r) && r != null) return r;
            var tex = TintTexture(s.texture, player);
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
            _tinted[(s, player)] = r;
            _original[r] = s;
            return r;
        }

        private static Texture2D TintTexture(Texture2D src, int player)
        {
            if (_tex.TryGetValue((src, player), out var t)) return t;
            try
            {
                // Sprite sheets are not readable: copy through the GPU.
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = copy.GetPixels32();
                float hue = Hues[Mathf.Clamp(player, 0, Hues.Length - 1)] / 360f;
                float light = Light[Mathf.Clamp(player, 0, Light.Length - 1)];
                for (int i = 0; i < px.Length; i++) px[i] = Shift(px[i], hue, light);
                copy.SetPixels32(px);
                copy.filterMode = src.filterMode;
                copy.wrapMode = src.wrapMode;
                copy.Apply(false, true);
                copy.name = src.name + " (coop " + player + ")";
                t = copy;
                Plugin.Log.LogInfo($"[recolor] player {player} copy of {src.name} ({src.width}x{src.height})");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[recolor] {src.name}: {e.Message}");
                t = null;
            }
            _tex[(src, player)] = t;
            return t;
        }

        private static Color32 Shift(Color32 c, float targetHue, float light)
        {
            if (c.a == 0) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Will's greens and teals (about 75..210 degrees) become the same shades of the player's colour.
            if (s < 0.15f || h < 75f / 360f || h > 210f / 360f) return c;
            float nh = targetHue + (h - 150f / 360f) * 0.25f;
            nh -= Mathf.Floor(nh);
            // Will's teal is dark and soft; the same shade in another colour reads muddy, so liven it a bit.
            var o = (Color32)Color.HSVToRGB(nh, Mathf.Min(1f, s * 1.35f + 0.1f), Mathf.Min(1f, v * light));
            o.a = c.a;
            return o;
        }
    }

    /// On a guest: after the animator picks this frame's sprites, swap Will's to this player's colour.
    public class RecolorLocalWill : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private float _rescan;

        private void LateUpdate()
        {
            if (!Session.IsGuest || !Net.Connected || Session.MyId <= 0) return;
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            if (Time.unscaledTime > _rescan)
            {
                _rescan = Time.unscaledTime + 1f;
                _renderers.Clear();
                var art = hero.transform.Find("HeroMerchantArt");
                if (art != null) art.GetComponentsInChildren(true, _renderers);
            }
            foreach (var sr in _renderers)
            {
                if (sr == null || sr.sprite == null || sr.name.Contains("Light") || Recolor.IsTinted(sr.sprite)) continue;
                sr.sprite = Recolor.Tint(sr.sprite, Session.MyId);
            }
        }
    }
}
