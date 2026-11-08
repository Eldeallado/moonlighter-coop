using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Top left, under your own health: a small health bar for every other player (their colour, name, life).
    public class PartyHud : MonoBehaviour
    {
        private Texture2D _white;
        private GUIStyle _name, _num;

        private void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }

        private void OnGUI()
        {
            if (!Net.Connected || HeroMerchant.Instance == null || ColorMenu.Open || CoopMenu.Open) return;
            if (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null) return;   // inventory, shop panels...
            if (HUDManager.Instance != null && HUDManager.Instance.IsHUDHidden()) return;           // cutscenes, loading
            var others = Puppet.All.Values.Where(p => p != null && p.MaxHp > 0f).OrderBy(p => p.PlayerId).ToList();
            if (others.Count == 0) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            float k = Screen.height / 531f;
            if (_name == null)
            {
                _name = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
                _num = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            }
            _name.fontSize = Mathf.Max(9, (int)(11 * k)); _num.fontSize = Mathf.Max(8, (int)(10 * k));
            // Just under the game's own heart, health bar and gold.
            float x = 22 * k, y = 128 * k, w = 170 * k, rowH = 27 * k, barH = 7 * k, sw = 9 * k;
            foreach (var p in others)
            {
                bool ghost = Downed.IsGhost(p.PlayerId);
                float frac = ghost ? 0f : Mathf.Clamp01(p.Hp / p.MaxHp);
                Fill(new Rect(x - 4 * k, y - 2 * k, w + 8 * k, rowH), new Color(0f, 0f, 0f, 0.45f));
                // Their colour: the body swatch of their look.
                Fill(new Rect(x, y + 2 * k, sw, sw), Color.black);
                Fill(new Rect(x + 1 * k, y + 3 * k, sw - 2 * k, sw - 2 * k), Looks.Swatch(Looks.Decode(Looks.BodyCode(p.Look)), false));
                _name.normal.textColor = ghost ? new Color(0.7f, 0.8f, 1f) : Color.white;
                GUI.Label(new Rect(x + sw + 4 * k, y - 3 * k, w - sw - 50 * k, 16 * k), $"J{p.PlayerId + 1} " + (string.IsNullOrEmpty(p.Name) ? "" : p.Name), _name);
                _num.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                GUI.Label(new Rect(x + w - 50 * k, y - 3 * k, 50 * k, 16 * k), ghost ? "fantasma" : $"{Mathf.CeilToInt(p.Hp)}/{Mathf.CeilToInt(p.MaxHp)}", _num);
                var bar = new Rect(x, y + 14 * k, w, barH);
                Fill(bar, new Color(0.15f, 0.05f, 0.05f, 0.9f));
                var col = frac > 0.5f ? new Color(0.85f, 0.2f, 0.2f) : frac > 0.25f ? new Color(0.95f, 0.5f, 0.15f) : new Color(1f, 0.25f, 0.25f);
                Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height), col);
                Fill(new Rect(bar.x, bar.y, bar.width * frac, 1.5f * k), new Color(1f, 1f, 1f, 0.25f));
                y += rowH + 4 * k;
            }
        }
    }
}
