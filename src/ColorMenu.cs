using UnityEngine;

namespace MoonlighterCoop
{
    /// F3 (a key the game never uses): pick the colour of each part of your Will (vest, shirt, trousers, backpack,
    /// shoes, gem, hair, skin) with bars (tone, intensity, brightness), or a whole style at once, watching him
    /// change in a live preview. Mouse, keyboard or pad. The choice is saved and every other player sees it.
    public class ColorMenu : MonoBehaviour
    {
        public static bool Open;
        public static ColorMenu Instance;

        private Looks.Part[] _parts = new Looks.Part[Looks.N];
        private int _part;                 // part being edited
        private int _row;                  // 0 = part list, 1..3 = bars, 4 = styles
        private int _style;                // highlighted style (keyboard / pad)
        private int _dragBar = -1;
        private bool _dirty; private float _dirtyAt;
        private bool _cursorWas;

        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _white, _rainbow, _grad1, _grad2;
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
                _parts = Looks.Unpack(Looks.Mine);
                _row = 1; _dragBar = -1;
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

        /// For the QA remote: a whole look at once.
        public static void Set(int look)
        {
            Looks.Choose(look);
            if (Instance != null) Instance._parts = Looks.Unpack(look);
        }

        private void Changed() { _dirty = true; _dirtyAt = Time.unscaledTime; _grad1 = _grad2 = null; }
        private void Commit() { _dirty = false; Looks.Choose(Looks.Pack(_parts)); }

        private static System.Collections.Generic.List<Looks.Style> StyleList => new System.Collections.Generic.List<Looks.Style>(Looks.AllStyles);

        private void ApplyStyle(int i)
        {
            _style = i;
            _parts = (Looks.Part[])StyleList[i].Parts.Clone();
            Changed();
        }

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

        private static int Max(int bar) => bar == 0 ? Looks.Tones - 1 : Looks.Steps - 1;
        private static int Get(Looks.Part p, int bar) => bar == 0 ? p.Tone : bar == 1 ? p.Intensity : p.Bright;

        private void SetBar(int bar, int value)
        {
            ref var p = ref _parts[_part];
            if (p.Own) { p.Own = false; p.Tone = Looks.Start[_part, 0]; p.Intensity = Looks.Start[_part, 1]; p.Bright = Looks.Start[_part, 2]; }
            value = bar == 0 ? (value % Looks.Tones + Looks.Tones) % Looks.Tones : Mathf.Clamp(value, 0, Max(bar));
            if (bar == 0) p.Tone = value; else if (bar == 1) p.Intensity = value; else p.Bright = value;
            Changed();
        }

        private void Step(int d)
        {
            if (_row == 0) { _part = (_part + d + Looks.N) % Looks.N; _grad1 = _grad2 = null; }
            else if (_row == 4) { int n = StyleList.Count; _style = (_style + d + n) % n; }
            else SetBar(_row - 1, Get(_parts[_part], _row - 1) + d);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) Toggle();
            if (!Open) return;
            if (HeroMerchant.Instance == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null)) { SetOpen(false); return; }
            PadNav.Poll();
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || PadNav.Up) _row = (_row + 4) % 5;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || PadNav.Down) _row = (_row + 1) % 5;
            if (PadNav.Left || PadNav.Right) Step(PadNav.Right ? 1 : -1);
            int d = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1 : 0);
            if (d != 0 && Time.unscaledTime >= _nextRepeat)
            {
                bool bar = _row >= 1 && _row <= 3;
                _nextRepeat = Time.unscaledTime + (_held && bar ? 0.06f : 0.3f); _held = true;
                Step(d);
            }
            if (d == 0) { _held = false; _nextRepeat = 0f; }
            if (_dirty && Time.unscaledTime - _dirtyAt > 0.15f) Commit();
            bool accept = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || PadNav.A;
            if (accept && _row == 4) ApplyStyle(_style);
            else if (accept && _row == 0) _row = 1;
            else if (accept || Input.GetKeyDown(KeyCode.Escape) || PadNav.B || PadNav.Combo) SetOpen(false);
        }
        private float _nextRepeat; private bool _held;
        private string _note = ""; private float _noteAt = -10f;

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
            _small.fontSize = (int)(13 * k); _button.fontSize = _buttonOn.fontSize = (int)(14 * k);
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _head.normal.textColor = Color.white; _label.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _small.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            _buttonOn.normal.textColor = new Color(1f, 0.85f, 0.45f);
        }

        private void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }
        private void Frame(Rect r, Color c, float w) { Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c); Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c); }
        private static readonly Color Gold = new Color(1f, 0.85f, 0.45f), Dim = new Color(0.35f, 0.3f, 0.25f);

        private void OnGUI()
        {
            if (!Open) return;
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            Styles(k);
            GUI.depth = -1000;
            float w = Mathf.Min(900 * k, Screen.width - 20), h = Mathf.Min(600 * k, Screen.height - 20);
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            Fill(panel, new Color(0.09f, 0.07f, 0.06f, 0.95f));
            Frame(panel, new Color(0.62f, 0.45f, 0.22f), 3 * k);
            Frame(new Rect(panel.x + 6 * k, panel.y + 6 * k, panel.width - 12 * k, panel.height - 12 * k), new Color(0.3f, 0.22f, 0.12f), 1 * k);
            GUI.Label(new Rect(panel.x + 24 * k, panel.y + 12 * k, w, 36 * k), "Aspecto de tu Will", _title);

            // Preview
            var pv = new Rect(panel.x + 24 * k, panel.y + 58 * k, 210 * k, 252 * k);
            Fill(pv, new Color(0.03f, 0.03f, 0.03f, 1f));
            if (_rt != null) GUI.DrawTexture(new Rect(pv.x + 4 * k, pv.y + 4 * k, pv.width - 8 * k, pv.height - 8 * k), _rt, ScaleMode.ScaleAndCrop);
            Frame(pv, new Color(0.62f, 0.45f, 0.22f), 2 * k);

            // Parts: two columns of four, each with its colour
            float px = pv.xMax + 26 * k, pw = panel.xMax - px - 24 * k, cw = (pw - 10 * k) / 2f, rh = 30 * k;
            GUI.Label(new Rect(px, panel.y + 54 * k, pw, 26 * k), (_row == 0 ? "▶ " : "") + "Parte", _head);
            for (int i = 0; i < Looks.N; i++)
            {
                var r = new Rect(px + (i % 2) * (cw + 10 * k), panel.y + 84 * k + (i / 2) * (rh + 6 * k), cw, rh);
                bool on = i == _part;
                if (GUI.Button(r, "", on ? _buttonOn : _button)) { _part = i; _row = 1; _grad1 = _grad2 = null; }
                Fill(new Rect(r.x + 5 * k, r.y + 5 * k, rh - 10 * k, rh - 10 * k), Looks.Swatch(_parts[i], i));
                Frame(new Rect(r.x + 5 * k, r.y + 5 * k, rh - 10 * k, rh - 10 * k), Color.white, 1 * k);
                GUI.Label(new Rect(r.x + rh + 2 * k, r.y, cw - rh, rh), Looks.Names[i] + (_parts[i].Own ? "  (Will)" : ""), on ? _head : _label);
                if (on) Frame(r, _row == 0 ? Gold : new Color(0.62f, 0.45f, 0.22f), 2 * k);
            }

            // Bars of the chosen part
            float by0 = panel.y + 84 * k + 4 * (rh + 6 * k) + 8 * k;
            Bars(new Rect(px, by0, pw, 150 * k), k);

            // Styles
            float sy = Mathf.Max(pv.yMax, by0 + 146 * k) + 10 * k;
            GUI.Label(new Rect(pv.x, sy, 300 * k, 26 * k), (_row == 4 ? "▶ " : "") + "Estilos", _head);
            var styles = StyleList; int n = styles.Count, perRow = 5;
            float sw = (panel.width - 48 * k - (perRow - 1) * 8 * k) / perRow;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(pv.x + (i % perRow) * (sw + 8 * k), sy + 30 * k + (i / perRow) * 36 * k, sw, 30 * k);
                if (GUI.Button(r, styles[i].Name, _row == 4 && _style == i ? _buttonOn : _button)) { _row = 4; ApplyStyle(i); }
                if (_row == 4 && _style == i) Frame(r, Gold, 2 * k);
            }

            // Buttons
            float btY = panel.yMax - 72 * k, bwid = 140 * k;
            if (GUI.Button(new Rect(pv.x, btY, bwid, 32 * k), "Al azar", _button))
            {
                for (int i = 0; i < Looks.N; i++)
                    _parts[i] = i == Looks.Piel && Random.value < 0.6f ? new Looks.Part { Own = true }
                        : Looks.P(Random.Range(0, Looks.Tones), Looks.Pale(i) ? Random.Range(1, 12) : Random.Range(6, 16), Random.Range(3, 15));
                Changed();
            }
            if (GUI.Button(new Rect(pv.x + bwid + 10 * k, btY, bwid, 32 * k), "Will original", _button)) { for (int i = 0; i < Looks.N; i++) _parts[i].Own = true; Changed(); }
            if (GUI.Button(new Rect(panel.xMax - 24 * k - bwid, btY, bwid, 32 * k), "Listo", _buttonOn)) SetOpen(false);
            // Own styles and share codes
            float sx = pv.x + 2 * (bwid + 10 * k), sbw = 118 * k;
            if (GUI.Button(new Rect(sx, btY, sbw, 32 * k), "Guardar estilo", _button)) { Looks.SaveStyle(_parts); _note = "Guardado en tus estilos"; _noteAt = Time.unscaledTime; }
            if (GUI.Button(new Rect(sx + sbw + 8 * k, btY, sbw, 32 * k), "Copiar código", _button)) { GUIUtility.systemCopyBuffer = Looks.ShareCode(Looks.Pack(_parts)); _note = "Copiado: " + GUIUtility.systemCopyBuffer; _noteAt = Time.unscaledTime; }
            if (GUI.Button(new Rect(sx + 2 * (sbw + 8 * k), btY, sbw, 32 * k), "Pegar código", _button))
            {
                int l = Looks.FromShareCode(GUIUtility.systemCopyBuffer);
                if (l >= 0) { _parts = Looks.Unpack(l); Changed(); _note = "Aspecto pegado"; } else _note = "El portapapeles no tiene un código ML-…";
                _noteAt = Time.unscaledTime;
            }
            int custom = _style - Looks.Styles.Length;
            if (_row == 4 && custom >= 0 && custom < Looks.Mine2.Count && GUI.Button(new Rect(sx + 3 * (sbw + 8 * k), btY, 80 * k, 32 * k), "Borrar", _button)) { Looks.DeleteStyle(custom); _style = 0; }
            if (Time.unscaledTime - _noteAt < 4f) GUI.Label(new Rect(pv.x, btY - 26 * k, panel.width - 48 * k, 22 * k), _note, _label);
            GUI.Label(new Rect(panel.x + 20 * k, panel.yMax - 34 * k, w - 40 * k, 24 * k),
                PadNav.Active ? "Cruceta ↑↓ fila, ←→ cambiar · A aplicar estilo / cerrar · B cerrar" : "Ratón, o ↑↓ fila y ←→ cambiar · Enter aplica el estilo · F3 o Esc para cerrar", _small);
        }

        private void Bars(Rect r, float k)
        {
            ref var p = ref _parts[_part];
            int part = _part;
            GUI.Label(new Rect(r.x, r.y, r.width - 130 * k, 28 * k), Looks.Names[part], _head);
            float bw = 120 * k;
            if (GUI.Button(new Rect(r.xMax - bw, r.y + 1 * k, bw, 26 * k), p.Own ? "✔ Como Will" : "Como Will", p.Own ? _buttonOn : _button)) { p.Own = !p.Own; Changed(); }
            var basePart = p.Own ? Looks.P(Looks.Start[part, 0], Looks.Start[part, 1], Looks.Start[part, 2]) : p;
            if (_grad1 == null)
            {
                var q = basePart;
                _grad1 = Grad(x => Looks.Swatch(Looks.P(q.Tone, Mathf.RoundToInt(x * 15), q.Bright), part), 16);
                _grad2 = Grad(x => Looks.Swatch(Looks.P(q.Tone, q.Intensity, Mathf.RoundToInt(x * 15)), part), 16);
            }
            var texs = new[] { _rainbow, _grad1, _grad2 };
            for (int i = 0; i < 3; i++)
            {
                float y = r.y + 34 * k + i * 36 * k;
                bool sel = _row == i + 1;
                GUI.Label(new Rect(r.x, y, 110 * k, 26 * k), (sel ? "▶ " : "   ") + BarNames[i], _label);
                var br = new Rect(r.x + 112 * k, y + 4 * k, r.width - 112 * k, 18 * k);
                GUI.color = p.Own ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                GUI.DrawTexture(br, texs[i]);
                GUI.color = Color.white;
                Frame(br, sel ? Gold : Dim, (sel ? 2 : 1) * k);
                if (!p.Own)
                {
                    float f = Get(p, i) / (float)Max(i);
                    var hx = br.x + f * br.width;
                    Fill(new Rect(hx - 4 * k, br.y - 5 * k, 8 * k, br.height + 10 * k), Color.black);
                    Fill(new Rect(hx - 2 * k, br.y - 3 * k, 4 * k, br.height + 6 * k), Color.white);
                }
                var e = Event.current;
                if (e.type == EventType.MouseDown && br.Contains(e.mousePosition)) { _dragBar = i; _row = i + 1; }
                if (e.type == EventType.MouseUp) _dragBar = -1;
                if (_dragBar == i && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                {
                    int v = Mathf.RoundToInt(Mathf.Clamp01((e.mousePosition.x - br.x) / br.width) * Max(i));
                    if (p.Own || v != Get(p, i)) SetBar(i, v);
                    e.Use();
                }
            }
        }
    }
}
