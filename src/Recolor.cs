using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The guest's Will: exactly the same art, with the greens turned into reds (a hue shift of green pixels
    /// only). Used for the guest's own Will and for the guest's copy on the host's screen.
    public static class Recolor
    {
        private static readonly Dictionary<Texture2D, Texture2D> _tex = new Dictionary<Texture2D, Texture2D>();
        private static readonly Dictionary<Sprite, Sprite> _red = new Dictionary<Sprite, Sprite>();
        private static readonly Dictionary<Sprite, Sprite> _original = new Dictionary<Sprite, Sprite>();

        public static bool IsRed(Sprite s) => s != null && _original.ContainsKey(s);
        public static Sprite Original(Sprite s) => s != null && _original.TryGetValue(s, out var o) ? o : s;

        public static Sprite Red(Sprite s)
        {
            if (s == null || s.texture == null) return s;
            if (_original.ContainsKey(s)) return s;
            if (_red.TryGetValue(s, out var r) && r != null) return r;
            var tex = RedTexture(s.texture);
            if (tex == null) return s;
            // Will's sprites are tightly packed in an atlas (no rectangle to cut): rebuild the exact same mesh on
            // the red atlas. Rect = the atlas area its UVs cover; geometry = its own triangles; pivot such that
            // every vertex lands where the original's does.
            var uv = s.uv; var verts = s.vertices; var tris = s.triangles;
            float W = tex.width, H = tex.height, ppu = s.pixelsPerUnit;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var u in uv) { minX = Mathf.Min(minX, u.x * W); minY = Mathf.Min(minY, u.y * H); maxX = Mathf.Max(maxX, u.x * W); maxY = Mathf.Max(maxY, u.y * H); }
            var tr = new Rect(Mathf.Floor(minX), Mathf.Floor(minY), Mathf.Max(1f, Mathf.Ceil(maxX) - Mathf.Floor(minX)), Mathf.Max(1f, Mathf.Ceil(maxY) - Mathf.Floor(minY)));
            var local = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++) local[i] = new Vector2(uv[i].x * W - tr.x, uv[i].y * H - tr.y);
            var pivotPx = local[0] - verts[0] * ppu;
            var pivot = new Vector2(pivotPx.x / tr.width, pivotPx.y / tr.height);
            var off = Vector2.zero;
            r = Sprite.Create(tex, tr, pivot, ppu, 0, SpriteMeshType.Tight);
            try
            {
                for (int i = 0; i < local.Length; i++) local[i] = new Vector2(Mathf.Clamp(local[i].x, 0f, tr.width), Mathf.Clamp(local[i].y, 0f, tr.height));
                r.OverrideGeometry(local, tris);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[recolor] geometry {s.name}: {e.Message}"); }
            r.name = s.name;
            if (_red.Count < 2) Plugin.Log.LogInfo($"[recolor] sprite {s.name}: packed={s.packed} rect={s.rect} textureRect={tr} offset={off} rotation={s.packingRotation}");
            _red[s] = r;
            _original[r] = s;
            return r;
        }

        private static Texture2D RedTexture(Texture2D src)
        {
            if (_tex.TryGetValue(src, out var t)) return t;
            Color32[] px;
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
                px = copy.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = GreenToRed(px[i]);
                copy.SetPixels32(px);
                copy.filterMode = src.filterMode;
                copy.wrapMode = src.wrapMode;
                copy.Apply(false, false);
                copy.name = src.name + " (coop red)";
                t = copy;
                int opaque = 0; foreach (var p in px) if (p.a > 10) opaque++;
                Plugin.Log.LogInfo($"[recolor] {src.name}: {opaque} visible pixels, format {src.format}, readable {src.isReadable}");
                try { System.IO.File.WriteAllBytes(Plugin.StatusFile("red_" + src.name.Replace('/', '_') + ".png"), copy.EncodeToPNG()); } catch { }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[recolor] {src.name}: {e.Message}");
                t = null;
            }
            _tex[src] = t;
            if (t != null) Plugin.Log.LogInfo($"[recolor] red copy of {src.name} ({src.width}x{src.height})");
            return t;
        }

        private static Color32 GreenToRed(Color32 c)
        {
            if (c.a == 0) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Will's greens and teals (about 75..210 degrees) become the same shades of red (about -25..+22).
            if (s < 0.15f || h < 75f / 360f || h > 210f / 360f) return c;
            float nh = (h - 150f / 360f) * 0.35f;
            if (nh < 0f) nh += 1f;
            // Will's teal is dark and soft; the same shade as red reads brown, so make it a bit livelier.
            var o = (Color32)Color.HSVToRGB(nh, Mathf.Min(1f, s * 1.35f + 0.1f), Mathf.Min(1f, v * 1.2f));
            o.a = c.a;
            return o;
        }
    }

    /// On the guest: after the animator picks this frame's sprites, swap Will's to the red ones.
    public class RecolorLocalWill : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private float _rescan;

        private void LateUpdate()
        {
            if (!Session.IsGuest || !Net.Connected) return;
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
                if (sr == null || sr.sprite == null || sr.name.Contains("Light") || Recolor.IsRed(sr.sprite)) continue;
                sr.sprite = Recolor.Red(sr.sprite);
            }
        }
    }
}
