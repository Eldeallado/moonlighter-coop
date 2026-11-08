using UnityEngine;

namespace MoonlighterCoop
{
    /// F3 (a key the game never uses): pick your Will's body and hair colour. Mouse, or arrows + Enter.
    /// The choice is saved and every other player sees it at once.
    public class ColorMenu : MonoBehaviour
    {
        public static bool Open;
        private int _row;            // 0 body, 1 hair
        private int _body, _hair;
        private bool _cursorWas;
        private GUIStyle _title, _label, _small, _box;
        private Texture2D _white;

        public static void Toggle() { if (Instance != null) Instance.SetOpen(!Open); }
        public static ColorMenu Instance;
        private void Awake() { Instance = this; }

        public void SetOpen(bool open)
        {
            if (open == Open) return;
            var hero = HeroMerchant.Instance;
            if (open && (hero == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null))) return;
            Open = open;
            if (open)
            {
                _body = Looks.BodyOf(Looks.Mine); _hair = Looks.HairOf(Looks.Mine); _row = 0;
                _cursorWas = Cursor.visible; Cursor.visible = true;
                hero.heroMerchantController.DisableMoveInput();
                hero.heroMerchantController.DisableAttackInput();
            }
            else
            {
                Cursor.visible = _cursorWas;
                if (hero != null)
                {
                    hero.heroMerchantController.EnableMoveInput();
                    if (!Downed.Me) hero.heroMerchantController.EnableAttackInput();
                }
            }
            Plugin.Log.LogInfo($"[colors] menu {(open ? "open" : "closed")}");
        }

        private void Apply() => Looks.Choose(Looks.Pack(_body, _hair));

        public static void Set(int body, int hair)
        {
            Looks.Choose(Looks.Pack(body, hair));
            if (Instance != null) { Instance._body = Looks.BodyOf(Looks.Mine); Instance._hair = Looks.HairOf(Looks.Mine); }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) Toggle();
            if (!Open) return;
            if (HeroMerchant.Instance == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null)) { SetOpen(false); return; }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) _row = 0;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) _row = 1;
            int d = (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ? 1 : 0) - (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ? 1 : 0);
            if (d != 0)
            {
                if (_row == 0) _body = (_body + d + Looks.Body.Length) % Looks.Body.Length;
                else _hair = (_hair + d + Looks.Hair.Length) % Looks.Hair.Length;
                Apply();
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        }

        public static Color Swatch(Looks.Paint p, bool hair)
        {
            if (p.Hue < 0f) return hair ? Recolor.HairSwatch : new Color(0.16f, 0.55f, 0.5f);
            return hair ? Color.HSVToRGB(p.Hue / 360f, Mathf.Clamp01(p.Sat * 0.9f), Mathf.Clamp01(p.Light * 0.85f))
                        : Color.HSVToRGB(p.Hue / 360f, Mathf.Clamp01(0.6f * p.Sat), Mathf.Clamp01(0.5f * p.Light + p.Add));
        }

        private void OnGUI()
        {
            if (!Open) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            float k = Mathf.Max(0.6f, Screen.height / 720f);
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _box = new GUIStyle(GUI.skin.box);
            }
            _title.fontSize = (int)(24 * k); _label.fontSize = (int)(18 * k); _small.fontSize = (int)(14 * k);
            float w = 560 * k, h = 300 * k;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.06f, w, h);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f); GUI.DrawTexture(rect, _white); GUI.color = old;
            GUI.Label(new Rect(rect.x, rect.y + 6 * k, w, 34 * k), "Color de tu Will", _title);
            Row(rect, 50 * k, k, 0, "Cuerpo", Looks.Body, ref _body, false);
            Row(rect, 150 * k, k, 1, "Pelo", Looks.Hair, ref _hair, true);
            GUI.Label(new Rect(rect.x + 10 * k, rect.y + h - 44 * k, w - 20 * k, 40 * k),
                "Ratón para elegir · flechas ↑↓ fila, ←→ color · Enter o F3 para cerrar", _small);
        }

        private void Row(Rect panel, float top, float k, int row, string title, Looks.Paint[] list, ref int sel, bool hair)
        {
            var old = GUI.color;
            string mark = _row == row ? "▶ " : "   ";
            GUI.Label(new Rect(panel.x + 16 * k, panel.y + top, panel.width - 32 * k, 28 * k), $"{mark}{title}: {list[sel].Name}", _label);
            float size = 38 * k, gap = 8 * k;
            float total = list.Length * size + (list.Length - 1) * gap;
            float x0 = panel.x + (panel.width - total) / 2f, y = panel.y + top + 34 * k;
            for (int i = 0; i < list.Length; i++)
            {
                var r = new Rect(x0 + i * (size + gap), y, size, size);
                if (i == sel) { GUI.color = Color.white; GUI.DrawTexture(new Rect(r.x - 3 * k, r.y - 3 * k, r.width + 6 * k, r.height + 6 * k), _white); }
                GUI.color = Swatch(list[i], hair);
                GUI.DrawTexture(r, _white);
                GUI.color = old;
                if (GUI.Button(r, "", GUIStyle.none)) { sel = i; _row = row; Apply(); }
            }
            GUI.color = old;
        }
    }
}
