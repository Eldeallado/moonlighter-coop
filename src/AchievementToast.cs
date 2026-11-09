using UnityEngine;

namespace MoonlighterCoop
{
    /// A new achievement, shown big for a few seconds: a green rune medal drops from the top of the screen and
    /// bounces, a panel unfolds under it with the name, and after a while everything rises out again.
    /// (Preview: the driver's "uishow logro"; not wired to the real achievements until the player approves it.)
    public class AchievementToast : MonoBehaviour
    {
        private static AchievementToast _i;
        private string _name, _text;
        private float _at = -100f;
        private const float Stay = 4.2f;

        private void Awake() { _i = this; }

        public static void Show(string name, string text)
        {
            if (_i == null) return;
            _i._name = name; _i._text = text; _i._at = Time.unscaledTime;
            Ui.Sound("gui_selector_pick");
        }

        private static float Bounce(float t)
        {
            // Ease out with two small bounces.
            if (t < 0.6f) { float a = t / 0.6f; return a * a; }
            if (t < 0.85f) { float a = (t - 0.725f) / 0.125f; return 1f - 0.12f * (1f - a * a); }
            { float a = (t - 0.925f) / 0.075f; return 1f - 0.04f * (1f - a * a); }
        }

        private void OnGUI()
        {
            float age = Time.unscaledTime - _at;
            if (age < 0f || age > Stay + 0.5f) return;
            Ui.Load();
            float k = Ui.K, u = Ui.U;
            GUI.depth = -1100;
            // Leaving: everything slides back up.
            float leave = Mathf.Clamp01((age - Stay) / 0.5f);
            float lift = leave * leave * 260f * k;

            // The panel unfolds once the medal has landed.
            float pw = 560 * k, ph = 92 * k;
            var panel = new Rect((Screen.width - pw) / 2f, 70 * k - lift, pw, ph);
            float open = Mathf.Clamp01((age - 0.55f) / 0.25f);
            if (open > 0f)
            {
                var m = GUI.matrix; var c = GUI.color;
                GUIUtility.ScaleAroundPivot(new Vector2(1f, Mathf.Lerp(0.05f, 1f, 1f - Mathf.Pow(1f - open, 3f))), new Vector2(panel.center.x, panel.y));
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(open * 2f) * (1f - leave));
                Ui.Scroll(panel);
                Ui.Label(new Rect(panel.x, panel.y + 30 * k, panel.width, 26 * k), "¡Logro conseguido!  ·  " + _name, 19f, Ui.Green, TextAnchor.MiddleCenter);
                Ui.Label(new Rect(panel.x + 20 * k, panel.y + 56 * k, panel.width - 40 * k, 24 * k), _text, 15.5f, Ui.Ink, TextAnchor.MiddleCenter);
                GUI.matrix = m; GUI.color = c;
            }

            // The medal: falls from above the screen onto the panel's top edge, bouncing, then spins a little.
            float fall = Bounce(Mathf.Clamp01(age / 0.7f));
            float size = 56 * k;
            float y = Mathf.Lerp(-size - 20 * k, panel.y - size * 0.55f, fall) - (open > 0f ? 0f : 0f);
            var mr = new Rect(Screen.width / 2f - size / 2f, y, size, size);
            var gc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 1f - leave);
            // A glow ring that pulses after landing.
            if (age > 0.7f)
            {
                float pulse = Mathf.Repeat(age - 0.7f, 1.2f) / 1.2f;
                float g = size * (1f + pulse * 0.6f);
                var t = Ui.Sprite("actionable_base");
                if (t != null) Ui.Draw(t, new Rect(mr.center.x - g / 2f, mr.center.y - g / 2f, g, g), new Color(Ui.Green.r, Ui.Green.g, Ui.Green.b, 0.35f * (1f - pulse)));
            }
            Ui.Check(Ui.Shrink(mr, -2 * u), false);
            Ui.Check(mr, true);
            Ui.Spirals(new Rect(mr.x - 26 * k, mr.y, mr.width + 52 * k, mr.height), mr.width + 20 * k, Ui.Green);
            GUI.color = gc;
        }
    }
}
