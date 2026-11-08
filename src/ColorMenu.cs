using UnityEngine;

namespace MoonlighterCoop
{
    /// F3 (a key the game never uses): pick your Will's body and hair colour with bars (tone, intensity,
    /// brightness), watching him change in a live preview. Mouse, or arrows + Enter. The choice is saved and
    /// every other player sees it at once.
    public class ColorMenu : MonoBehaviour
    {
        public static bool Open;
        public static ColorMenu Instance;

        private Looks.Part _body, _hair;
        private int _row;                  // 0..2 body bars, 3..5 hair bars
        private int _dragBar = -1;
        private bool _dirty; private float _dirtyAt;
        private bool _cursorWas;

        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _white, _rainbow, _gradBody1, _gradBody2, _gradHair1, _gradHair2;
        private GUIStyle _title, _head, _label, _small, _button, _buttonOn;

        private static readonly string[] BarNames = { "Tono", "Intensidad", "Brillo" };

        private void Awake() { Instance = this; }
        public static void Toggle() { if (Instance != null) Instance.SetOpen(!Open); }

        public void SetOpen(bool open)
        {
            if (open == Open) return;
            var hero = HeroMerchant.Instance;
            if (open && (hero == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null))) return;
            Open = open;
            if (open)
            {
                _body = Looks.Decode(Looks.BodyCode(Looks.Mine));
                _hair = Looks.Decode(Looks.HairCode(Looks.Mine));
                _row = 0; _dragBar = -1;
                _cursorWas = Cursor.visible; Cursor.visible = true;
                hero.heroMerchantController.DisableMoveInput();
                hero.heroMerchantController.DisableAttackInput();
                StartPreview();
            }
            else
            {
                if (_dirty) Commit();
                Cursor.visible = _cursorWas;
                StopPreview();
                if (hero != null)
                {
                    hero.heroMerchantController.EnableMoveInput();
                    if (!Downed.Me) hero.heroMerchantController.EnableAttackInput();
                }
            }
            Plugin.Log.LogInfo($"[colors] menu {(open ? "open" : "closed")}");
        }

        /// For the QA remote: set both parts at once (codes as packed in a look).
        public static void Set(int bodyCode, int hairCode)
        {
            Looks.Choose(bodyCode | hairCode << 15);
            if (Instance != null) { Instance._body = Looks.Decode(bodyCode); Instance._hair = Looks.Decode(hairCode); }
        }

        private void Changed() { _dirty = true; _dirtyAt = Time.unscaledTime; _gradBody1 = _gradBody2 = _gradHair1 = _gradHair2 = null; }
        private void Commit() { _dirty = false; Looks.Choose(Looks.Pack(_body, _hair)); }

        // ---------- live preview: a small camera looking at the local Will ----------

        private void StartPreview()
        {
            var main = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            if (main == null) return;
            if (_rt == null) { _rt = new RenderTexture(160, 192, 16) { filterMode = FilterMode.Point }; _rt.Create(); }
            if (_cam == null)
            {
                var go = new GameObject("CoopColorPreview");
                DontDestroyOnLoad(go);
                _cam = go.AddComponent<Camera>();
            }
            _cam.CopyFrom(main);
            _cam.orthographic = true;
            _cam.orthographicSize = 30f;
            _cam.targetTexture = _rt;
            _cam.depth = main.depth - 10;
            _cam.enabled = true;
            PlacePreview();
        }

        private void StopPreview() { if (_cam != null) _cam.enabled = false; }

        private void PlacePreview()
        {
            var hero = HeroMerchant.Instance;
            if (_cam == null || hero == null) return;
            var main = Camera.main;
            float z = main != null ? main.transform.position.z : -10f;
            _cam.transform.position = new Vector3(hero.transform.position.x, hero.transform.position.y + 16f, z);
        }

        private void LateUpdate() { if (Open) PlacePreview(); }

        // ---------- input ----------

        private ref Looks.Part PartOf(int bar) => ref (bar < 3 ? ref _body : ref _hair);

        private static int Max(int bar) => bar % 3 == 0 ? Looks.Tones - 1 : Looks.Steps - 1;

        private static int Get(Looks.Part p, int bar) => bar % 3 == 0 ? p.Tone : bar % 3 == 1 ? p.Intensity : p.Bright;

        private void SetBar(int bar, int value)
        {
            ref var p = ref PartOf(bar);
            if (p.Own) { p.Own = false; if (bar < 3) { p.Tone = 34; p.Intensity = 13; p.Bright = 6; } else { p.Tone = 11; p.Intensity = 3; p.Bright = 14; } }
            value = bar % 3 == 0 ? (value % Looks.Tones + Looks.Tones) % Looks.Tones : Mathf.Clamp(value, 0, Max(bar));
            if (bar % 3 == 0) p.Tone = value; else if (bar % 3 == 1) p.Intensity = value; else p.Bright = value;
            Changed();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) Toggle();
            if (!Open) return;
            if (HeroMerchant.Instance == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null)) { SetOpen(false); return; }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) _row = (_row + 5) % 6;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) _row = (_row + 1) % 6;
            int d = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1 : 0);
            if (d != 0 && Time.unscaledTime >= _nextRepeat)
            {
                _nextRepeat = Time.unscaledTime + (_held ? 0.06f : 0.3f); _held = true;
                SetBar(_row, Get(PartOf(_row), _row) + d);
            }
            if (d == 0) { _held = false; _nextRepeat = 0f; }
            if (_dirty && Time.unscaledTime - _dirtyAt > 0.15f) Commit();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        }
        private float _nextRepeat; private bool _held;

        // ---------- drawing ----------

        private Texture2D Grad(System.Func<float, Color> f, int n)
        {
            var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++) t.SetPixel(i, 0, f(i / (float)(n - 1)));
            t.Apply();
            return t;
        }

        private void Styles(float k)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_rainbow == null) _rainbow = Grad(x => Color.HSVToRGB(x * 355f / 360f, 0.85f, 0.95f), 72);
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                _small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _button = new GUIStyle(GUI.skin.button);
                _buttonOn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            }
            _title.fontSize = (int)(26 * k); _head.fontSize = (int)(19 * k); _label.fontSize = (int)(15 * k);
            _small.fontSize = (int)(13 * k); _button.fontSize = _buttonOn.fontSize = (int)(15 * k);
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _head.normal.textColor = Color.white; _label.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _small.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            _buttonOn.normal.textColor = new Color(1f, 0.85f, 0.45f);
        }

        private void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        private void Frame(Rect r, Color c, float w) { Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c); Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c); }

        private void OnGUI()
        {
            if (!Open) return;
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            Styles(k);
            GUI.depth = -1000;
            float w = 780 * k, h = 470 * k;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            Fill(panel, new Color(0.09f, 0.07f, 0.06f, 0.95f));
            Frame(panel, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            Frame(new Rect(panel.x + 6 * k, panel.y + 6 * k, panel.width - 12 * k, panel.height - 12 * k), new Color(0.3f, 0.22f, 0.12f), 1 * k);
            GUI.Label(new Rect(panel.x + 24 * k, panel.y + 14 * k, w, 36 * k), "Aspecto de tu Will", _title);

            // Preview
            var pv = new Rect(panel.x + 24 * k, panel.y + 62 * k, 220 * k, 264 * k);
            Fill(pv, new Color(0.03f, 0.03f, 0.03f, 1f));
            if (_rt != null) GUI.DrawTexture(new Rect(pv.x + 4 * k, pv.y + 4 * k, pv.width - 8 * k, pv.height - 8 * k), _rt, ScaleMode.ScaleAndCrop);
            Frame(pv, new Color(0.62f, 0.45f, 0.22f), 2 * k);
            float sw = (pv.width - 12 * k) / 2f;
            Swatch(new Rect(pv.x, pv.yMax + 10 * k, sw, 30 * k), "Cuerpo", Looks.Swatch(_body, false), k);
            Swatch(new Rect(pv.x + sw + 12 * k, pv.yMax + 10 * k, sw, 30 * k), "Pelo", Looks.Swatch(_hair, true), k);

            // Bars
            float bx = pv.xMax + 34 * k, bw = panel.xMax - bx - 30 * k;
            Section(new Rect(bx, panel.y + 58 * k, bw, 150 * k), "Cuerpo", 0, ref _body, false, k);
            Section(new Rect(bx, panel.y + 218 * k, bw, 150 * k), "Pelo", 3, ref _hair, true, k);

            // Buttons
            float by = panel.yMax - 74 * k, bwid = 150 * k;
            if (GUI.Button(new Rect(bx, by, bwid, 34 * k), "Al azar", _button))
            {
                _body = new Looks.Part { Tone = Random.Range(0, Looks.Tones), Intensity = Random.Range(8, 16), Bright = Random.Range(3, 14) };
                _hair = new Looks.Part { Tone = Random.Range(0, Looks.Tones), Intensity = Random.Range(2, 14), Bright = Random.Range(2, 16) };
                Changed();
            }
            if (GUI.Button(new Rect(bx + bwid + 12 * k, by, bwid, 34 * k), "Will original", _button)) { _body.Own = true; _hair.Own = true; Changed(); }
            if (GUI.Button(new Rect(panel.xMax - 30 * k - bwid, by, bwid, 34 * k), "Listo", _buttonOn)) SetOpen(false);
            GUI.Label(new Rect(panel.x + 20 * k, panel.yMax - 34 * k, w - 40 * k, 24 * k),
                "Arrastra las barras con el ratón · ↑↓ elegir barra, ←→ mover · Enter o F3 para cerrar", _small);
        }

        private void Swatch(Rect r, string name, Color c, float k)
        {
            Fill(new Rect(r.x, r.y, r.height, r.height), c);
            Frame(new Rect(r.x, r.y, r.height, r.height), Color.white, 1 * k);
            GUI.Label(new Rect(r.x + r.height + 8 * k, r.y, r.width - r.height, r.height), name, _label);
        }

        private void Section(Rect r, string title, int firstBar, ref Looks.Part p, bool hair, float k)
        {
            GUI.Label(new Rect(r.x, r.y, 200 * k, 30 * k), title, _head);
            float bw = 120 * k;
            if (GUI.Button(new Rect(r.xMax - bw, r.y + 2 * k, bw, 26 * k), p.Own ? "✔ Como Will" : "Como Will", p.Own ? _buttonOn : _button)) { p.Own = !p.Own; Changed(); }
            var basePart = p.Own ? Looks.Decode(1 + ((hair ? 11 : 34) * 16 + (hair ? 3 : 13)) * 16 + (hair ? 14 : 6)) : p;
            Texture2D t1, t2;
            if (hair) { if (_gradHair1 == null) { var q = basePart; _gradHair1 = Grad(x => Looks.Swatch(new Looks.Part { Tone = q.Tone, Intensity = Mathf.RoundToInt(x * 15), Bright = q.Bright }, true), 16); _gradHair2 = Grad(x => Looks.Swatch(new Looks.Part { Tone = q.Tone, Intensity = q.Intensity, Bright = Mathf.RoundToInt(x * 15) }, true), 16); } t1 = _gradHair1; t2 = _gradHair2; }
            else { if (_gradBody1 == null) { var q = basePart; _gradBody1 = Grad(x => Looks.Swatch(new Looks.Part { Tone = q.Tone, Intensity = Mathf.RoundToInt(x * 15), Bright = q.Bright }, false), 16); _gradBody2 = Grad(x => Looks.Swatch(new Looks.Part { Tone = q.Tone, Intensity = q.Intensity, Bright = Mathf.RoundToInt(x * 15) }, false), 16); } t1 = _gradBody1; t2 = _gradBody2; }
            var texs = new[] { _rainbow, t1, t2 };
            for (int i = 0; i < 3; i++)
            {
                int bar = firstBar + i;
                float y = r.y + 38 * k + i * 38 * k;
                GUI.Label(new Rect(r.x, y, 110 * k, 26 * k), (_row == bar ? "▶ " : "   ") + BarNames[i], _label);
                var br = new Rect(r.x + 112 * k, y + 4 * k, r.width - 112 * k, 18 * k);
                GUI.color = p.Own ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                GUI.DrawTexture(br, texs[i]);
                GUI.color = Color.white;
                Frame(br, _row == bar ? new Color(1f, 0.85f, 0.45f) : new Color(0.35f, 0.3f, 0.25f), (_row == bar ? 2 : 1) * k);
                if (!p.Own)
                {
                    float f = Get(p, bar) / (float)Max(bar);
                    var hx = br.x + f * br.width;
                    Fill(new Rect(hx - 4 * k, br.y - 5 * k, 8 * k, br.height + 10 * k), Color.black);
                    Fill(new Rect(hx - 2 * k, br.y - 3 * k, 4 * k, br.height + 6 * k), Color.white);
                }
                var e = Event.current;
                if (e.type == EventType.MouseDown && br.Contains(e.mousePosition)) { _dragBar = bar; _row = bar; }
                if (e.type == EventType.MouseUp) _dragBar = -1;
                if (_dragBar == bar && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                {
                    int v = Mathf.RoundToInt(Mathf.Clamp01((e.mousePosition.x - br.x) / br.width) * Max(bar));
                    if (p.Own || v != Get(p, bar)) SetBar(bar, v);
                    e.Use();
                }
            }
        }
    }
}
