using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Top left, under your own health: every other player with a small portrait (Will's face in their colours),
    /// name and health. Over each partner's Will, a discreet name tag that fades when you are close to them.
    public class PartyHud : MonoBehaviour
    {
        // Will standing, facing us (24x41): the dungeon/town idle frame, or the shop one.
        private static readonly string[] FaceSprites = { "Will_Idle_Down_1", "will front idle cycle shop_01" };
        private static Sprite _face; private static float _nextFind;
        private static Sprite FaceSprite()
        {
            if (_face != null) return _face;
            if (Time.unscaledTime < _nextFind) return null;
            _nextFind = Time.unscaledTime + 3f;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var n in FaceSprites) { _face = all.FirstOrDefault(s => s != null && s.name == n); if (_face != null) break; }
            return _face;
        }

        private void OnGUI()
        {
            if (!Net.Connected || HeroMerchant.Instance == null || ColorMenu.Open || CoopMenu.Open) return;
            if (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null) return;   // inventory, shop panels...
            if (HUDManager.Instance != null && HUDManager.Instance.IsHUDHidden()) return;           // cutscenes, loading
            var others = Puppet.All.Values.Where(p => p != null && p.MaxHp > 0f).OrderBy(p => p.PlayerId).ToList();
            if (others.Count == 0) return;
            Ui.Load();
            NameTags(others);
            float k = Screen.height / 531f;
            // Just under the game's own heart, health bar and gold: a small plate per player.
            float x = 22 * k, y = 128 * k, w = 186 * k, rowH = 34 * k, face = 28 * k;
            foreach (var p in others)
            {
                bool ghost = Downed.IsGhost(p.PlayerId);
                float frac = ghost ? 0f : Mathf.Clamp01(p.Hp / p.MaxHp);
                Ui.Plate(new Rect(x - 5 * k, y - 3 * k, w + 10 * k, rowH), 0.95f);
                // Portrait: their Will's face in a dark round frame.
                var fr = new Rect(x, y, face, face);
                Ui.Round(fr, Ui.Line, Ui.U);
                Ui.Round(Ui.Shrink(fr, Ui.U), new Color32(0x2A, 0x25, 0x1F, 0xFF), Ui.U);
                var o = GUI.color; if (ghost) GUI.color = new Color(0.75f, 0.85f, 1f, 0.55f);
                Face(Ui.Shrink(fr, Ui.U * 1.5f), p.Look);
                GUI.color = o;
                float tx = fr.xMax + 6 * k, tw = w - face - 6 * k;
                Ui.Label(new Rect(tx, y - 1 * k, tw - 56 * k, 16 * k), $"J{p.PlayerId + 1} " + (string.IsNullOrEmpty(p.Name) ? "" : p.Name), 11f * k / Ui.K, ghost ? Ui.InkFaint : Ui.Ink);
                Ui.Label(new Rect(tx + tw - 56 * k, y - 1 * k, 56 * k, 16 * k), ghost ? "fantasma" : $"{Mathf.CeilToInt(p.Hp)}/{Mathf.CeilToInt(p.MaxHp)}", 10f * k / Ui.K, ghost ? Ui.InkFaint : Ui.InkSoft, TextAnchor.MiddleRight);
                var col = frac > 0.5f ? new Color(0.8f, 0.2f, 0.17f) : frac > 0.25f ? new Color(0.9f, 0.48f, 0.15f) : new Color(1f, 0.25f, 0.2f);
                Ui.Bar(new Rect(tx, y + 16 * k, tw, 8 * k), frac, col, false);
                Lag.Icon(new Rect(fr.xMax - 9 * k, fr.yMax - 9 * k, 10 * k, 10 * k), p.PlayerId);
                y += rowH + 4 * k;
            }
        }

        /// The top of Will's front-facing sprite, recoloured with a look (his face, hair and vest).
        public static void Face(Rect r, int look)
        {
            var t = FaceTex(look);
            if (t != null) GUI.DrawTexture(r, t);
        }

        // One small texture per look: the top square of the tinted frame (head and shoulders), cut out once.
        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> _faces = new System.Collections.Generic.Dictionary<int, Texture2D>();
        private static Texture2D FaceTex(int look)
        {
            if (_faces.TryGetValue(look, out var t)) return t;
            var sp = FaceSprite();
            if (sp == null) return null;
            try
            {
                var tinted = Recolor.Tint(sp, look) ?? sp;
                var src = tinted.texture;
                var full = src.isReadable ? src : Recolor.ReadableCopy(src);
                var tr = tinted.textureRect;
                int side = Mathf.Min((int)tr.width, (int)tr.height), top = (int)tr.yMax;
                t = new Texture2D(side, side, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(full.GetPixels((int)tr.x, top - side, side, side));
                t.Apply();
                if (full != src) Object.Destroy(full);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[hud] face: " + e.Message); t = null; }
            _faces[look] = t;
            return t;
        }

        /// Name tags over the partners' Wills, hidden when you stand next to them.
        private static void NameTags(System.Collections.Generic.List<Puppet> others)
        {
            var cam = Camera.main; var hero = HeroMerchant.Instance;
            if (cam == null || hero == null) return;
            float k = Ui.K;
            foreach (var p in others)
            {
                if (!p.Visible || !p.gameObject.activeInHierarchy) continue;
                float d = Vector2.Distance(p.transform.position, hero.transform.position);
                float a = Mathf.Clamp01((d - 30f) / 30f);   // fades out when you are close
                if (a <= 0.01f) continue;
                var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 40f);
                if (sp.z < 0f) continue;
                string name = string.IsNullOrEmpty(p.Name) ? "Jugador " + (p.PlayerId + 1) : p.Name;
                float w = Ui.Width(name, 13f) + 16 * k, h = 20 * k;
                var r = new Rect(sp.x - w / 2f, Screen.height - sp.y - h, w, h);
                Ui.Plate(r, 0.85f * a);
                var o = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                Ui.Label(r, name, 13f, Downed.IsGhost(p.PlayerId) ? Ui.InkFaint : Ui.Ink, TextAnchor.MiddleCenter);
                GUI.color = o;
            }
        }
    }
}
