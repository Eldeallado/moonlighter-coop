using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Top left, under your own health: a small health bar for every other player (their colour, name, life).
    public class PartyHud : MonoBehaviour
    {

        private void OnGUI()
        {
            if (!Net.Connected || HeroMerchant.Instance == null || ColorMenu.Open || CoopMenu.Open) return;
            if (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null) return;   // inventory, shop panels...
            if (HUDManager.Instance != null && HUDManager.Instance.IsHUDHidden()) return;           // cutscenes, loading
            var others = Puppet.All.Values.Where(p => p != null && p.MaxHp > 0f).OrderBy(p => p.PlayerId).ToList();
            if (others.Count == 0) return;
            Ui.Load();
            float k = Screen.height / 531f;
            // Just under the game's own heart, health bar and gold: a small parchment plate per player.
            float x = 22 * k, y = 128 * k, w = 176 * k, rowH = 30 * k, sw = 12 * k;
            foreach (var p in others)
            {
                bool ghost = Downed.IsGhost(p.PlayerId);
                float frac = ghost ? 0f : Mathf.Clamp01(p.Hp / p.MaxHp);
                Ui.Plate(new Rect(x - 5 * k, y - 3 * k, w + 10 * k, rowH), 0.95f);
                // Their colour: the vest swatch of their look, framed like an item slot.
                var swr = new Rect(x, y + 1 * k, sw, sw);
                Ui.Box(swr, Ui.Outline);
                Ui.Box(new Rect(swr.x + Ui.U, swr.y + Ui.U, swr.width - 2 * Ui.U, swr.height - 2 * Ui.U), Looks.Swatch(Looks.Unpack(p.Look)[Looks.Chaleco], Looks.Chaleco));
                Ui.Label(new Rect(swr.xMax + 4 * k, y - 2 * k, w - sw - 58 * k, 16 * k), $"J{p.PlayerId + 1} " + (string.IsNullOrEmpty(p.Name) ? "" : p.Name), 11f * k / Ui.K, ghost ? Ui.InkFaint : Ui.Ink);
                Ui.Label(new Rect(x + w - 56 * k, y - 2 * k, 56 * k, 16 * k), ghost ? "fantasma" : $"{Mathf.CeilToInt(p.Hp)}/{Mathf.CeilToInt(p.MaxHp)}", 10f * k / Ui.K, ghost ? Ui.InkFaint : Ui.InkSoft, TextAnchor.MiddleRight);
                var col = frac > 0.5f ? new Color(0.8f, 0.2f, 0.17f) : frac > 0.25f ? new Color(0.9f, 0.48f, 0.15f) : new Color(1f, 0.25f, 0.2f);
                Ui.Bar(new Rect(swr.xMax + 4 * k, y + 15 * k, w - sw - 4 * k, 8 * k), frac, col);
                y += rowH + 4 * k;
            }
        }
    }
}
