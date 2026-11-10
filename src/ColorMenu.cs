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
        private Texture2D _rainbow, _grad1, _grad2;
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
                _dragBar = -1; _wantFocus = KPart * 100 + _part;
                _cursorWas = Cursor.visible; Cursor.visible = true;
                _openedAt = Time.unscaledTime; Ui.Sound(Ui.SndOpen);
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

        // Pad / keyboard focus: every control registers its rect and an id (kind * 100 + index) while drawing;
        // the arrows move to the nearest control that way on screen (so down from a style goes to the style
        // under it, and on to the buttons). On a bar, left/right change its value instead.
        private const int KPart = 1, KBar = 2, KStyle = 3, KAction = 4, KOwn = 5;
        private readonly System.Collections.Generic.List<Rect> _rects = new System.Collections.Generic.List<Rect>();
        private readonly System.Collections.Generic.List<int> _ids = new System.Collections.Generic.List<int>();
        private System.Collections.Generic.List<Rect> _lastRects = new System.Collections.Generic.List<Rect>();
        private System.Collections.Generic.List<int> _lastIds = new System.Collections.Generic.List<int>();
        private int _focus, _wantFocus = -1;
        private float _openedAt;
        private bool _padA;
        private int FocusedId => _focus >= 0 && _focus < _lastIds.Count ? _lastIds[_focus] : -1;
        private bool OnBar => FocusedId / 100 == KBar;

        private void Update()
        {
            if (Teclas.Abajo(KeyCode.F3)) Toggle();
            if (!Open) return;
            if (HeroMerchant.Instance == null || (GUIManager.Instance != null && GUIManager.Instance.currentInventory != null)) { SetOpen(false); return; }
            PadNav.Poll();
            _padA = false;
            bool up = Teclas.Abajo(KeyCode.UpArrow) || Teclas.Abajo(KeyCode.W) || PadNav.Up;
            bool down = Teclas.Abajo(KeyCode.DownArrow) || Teclas.Abajo(KeyCode.S) || PadNav.Down;
            int was = _focus;
            if (up) _focus = Ui.Nearest(_lastRects, _focus, 0, -1);
            if (down) _focus = Ui.Nearest(_lastRects, _focus, 0, 1);
            if (OnBar)
            {
                // Bars: the pad steps once per press; held keys repeat, faster after a moment.
                int bar = FocusedId % 100;
                if (PadNav.Left || PadNav.Right) SetBar(bar, Get(_parts[_part], bar) + (PadNav.Right ? 1 : -1));
                int d = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1 : 0);
                if (d != 0 && Time.unscaledTime >= _nextRepeat)
                {
                    _nextRepeat = Time.unscaledTime + (_held ? 0.06f : 0.3f); _held = true;
                    SetBar(bar, Get(_parts[_part], bar) + d);
                }
                if (d == 0) { _held = false; _nextRepeat = 0f; }
            }
            else
            {
                if (PadNav.Left || Teclas.Abajo(KeyCode.LeftArrow) || Teclas.Abajo(KeyCode.A)) _focus = Ui.Nearest(_lastRects, _focus, -1, 0);
                if (PadNav.Right || Teclas.Abajo(KeyCode.RightArrow) || Teclas.Abajo(KeyCode.D)) _focus = Ui.Nearest(_lastRects, _focus, 1, 0);
            }
            if (_focus != was) Ui.Sound(Ui.SndMove);
            if (_dirty && Time.unscaledTime - _dirtyAt > 0.15f) Commit();
            if (Teclas.Abajo(KeyCode.Return) || Teclas.Abajo(KeyCode.KeypadEnter) || PadNav.A) _padA = true;   // pressed on the focused control while drawing
            if (Teclas.Abajo(KeyCode.Escape) || PadNav.B || PadNav.Combo) SetOpen(false);
        }
        private float _nextRepeat; private bool _held;
        private string _note = ""; private float _noteAt = -10f;

        /// Registers a control for the focus; true if it has it.
        private bool Item(Rect r, int id)
        {
            int idx = _rects.Count;
            _rects.Add(r); _ids.Add(id);
            return idx == _focus;
        }

        /// The focused control was "pressed" with A / Enter this frame.
        private bool Pressed(bool focused)
        {
            if (!focused || !_padA || Event.current.type != EventType.Repaint) return false;
            _padA = false;
            Ui.Sound(Ui.SndPress);
            return true;
        }

        // ---------- drawing ----------

        private Texture2D Grad(System.Func<float, Color> f, int n)
        {
            var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++) t.SetPixel(i, 0, f(i / (float)(n - 1)));
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!Open) return;
            Ui.Load();
            float k = Ui.K;
            if (_rainbow == null) _rainbow = Grad(x => Color.HSVToRGB(x * 355f / 360f, 0.85f, 0.95f), 72);
            GUI.depth = -1000;
            _rects.Clear(); _ids.Clear();
            bool pad = PadNav.Active;

            // En el móvil (pantalla más ancha) caben más estilos por fila
            var styles = StyleList; int n = styles.Count, perRow = Teclas.Movil ? 7 : 5, rows = (n + perRow - 1) / perRow;
            // Si no cabe en alto, se reduce todo a la vez (antes se cortaba el panel y lo de abajo quedaba fuera)
            k = Mathf.Min(k, (Screen.height - 24f) / (545 + rows * 58), (Screen.width - 20f) / 920f);
            float w = 920 * k, h = (545 + rows * 58) * k;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Ui.Dim(0.5f);
            Ui.BeginOpen(_openedAt, panel);
            Ui.Sheet(panel);
            Ui.Label(new Rect(panel.x + 30 * k, panel.y + 24 * k, w, 36 * k), "Aspecto de tu Will", 30f, Ui.Ink);
            Ui.Label(new Rect(panel.xMax - 330 * k, panel.y + 30 * k, 300 * k, 26 * k), "Lo ven todos los jugadores", 15f, Ui.InkFaint, TextAnchor.MiddleRight);
            Ui.Divider(panel.x + 30 * k, panel.y + 66 * k, w - 60 * k);

            // Preview, in a dark inset with the game's spirals under it.
            var pv = new Rect(panel.x + 30 * k, panel.y + 82 * k, 214 * k, 262 * k);
            Ui.Slot(pv);
            if (_rt != null) GUI.DrawTexture(Ui.Shrink(pv, 4 * k), _rt, ScaleMode.ScaleAndCrop);
            Ui.Label(new Rect(pv.x, pv.yMax + 2 * k, pv.width, 22 * k), "Vista previa", 14.5f, Ui.InkFaint, TextAnchor.MiddleCenter);

            // Parts: a 4 x 2 mosaic of tiles, each with its colour.
            float px = pv.xMax + 26 * k, pw = panel.xMax - 30 * k - px;
            Ui.Section(new Rect(px, panel.y + 78 * k, pw, 20 * k), "Partes");
            float tw = (pw - 3 * 8 * k) / 4f, th = 40 * k, ty = panel.y + 104 * k;
            for (int i = 0; i < Looks.N; i++)
            {
                var r = new Rect(px + (i % 4) * (tw + 8 * k), ty + (i / 4) * (th + 8 * k), tw, th);
                bool chosen = i == _part, focused = Item(r, KPart * 100 + i) && pad;
                bool hover = r.Contains(Event.current.mousePosition);
                Ui.Card(r, chosen || hover, focused);
                Ui.Swatch(new Rect(r.x + 8 * k, r.y + 9 * k, 22 * k, 22 * k), Looks.Swatch(_parts[i], i));
                Ui.Label(new Rect(r.x + 38 * k, r.y + 2 * k, r.width - 42 * k, 22 * k), Looks.Names[i], 16f, chosen ? Ui.Green : Ui.Ink);
                Ui.Label(new Rect(r.x + 38 * k, r.y + 19 * k, r.width - 42 * k, 18 * k), _parts[i].Own ? "como Will" : "propio", 13f, Ui.InkFaint);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) || Pressed(focused))
                {
                    _part = i; _grad1 = _grad2 = null;
                    if (focused) _wantFocus = KBar * 100;   // with the pad, go straight to its first bar
                }
            }

            // The chosen part's colour: three bars and "like Will".
            float by = ty + 2 * (th + 8 * k) + 8 * k;
            Bars(new Rect(px, by, pw, 140 * k), k, pad);

            // Styles: tiles with their name and a small palette; the last row centred.
            float sy = Mathf.Max(pv.yMax + 26 * k, by + 140 * k) + 6 * k;
            Ui.Section(new Rect(pv.x, sy, panel.width - 60 * k, 20 * k), "Estilos");
            float gap = 10 * k, sw = (panel.width - 60 * k - (perRow - 1) * gap) / perRow, sh = 50 * k;
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow, inRow = Mathf.Min(perRow, n - row * perRow);
                float rowX = pv.x + (panel.width - 60 * k - (inRow * sw + (inRow - 1) * gap)) / 2f;
                var r = new Rect(rowX + col * (sw + gap), sy + 26 * k + row * (sh + 8 * k), sw, sh);
                bool focused = Item(r, KStyle * 100 + i) && pad, chosen = i == _style;
                bool hover = r.Contains(Event.current.mousePosition);
                Ui.Card(r, chosen || hover, focused);
                bool mine = i >= Looks.Styles.Length;
                Ui.Label(new Rect(r.x, r.y + 3 * k, r.width, 22 * k), styles[i].Name, 15.5f, chosen ? Ui.Green : mine ? Ui.Gold : Ui.Ink, TextAnchor.MiddleCenter);
                // Its palette: vest, shirt, trousers, hair.
                var parts = styles[i].Parts; int[] show = { Looks.Chaleco, 1, 2, 6 };
                float d = 14 * k, x0 = r.center.x - (show.Length * d + (show.Length - 1) * 4 * k) / 2f;
                for (int s = 0; s < show.Length; s++)
                    Ui.Swatch(new Rect(x0 + s * (d + 4 * k), r.y + 28 * k, d, d), Looks.Swatch(parts[show[s]], show[s]));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) || Pressed(focused)) ApplyStyle(i);
            }

            // Actions along the bottom: what to do with the look, and Listo on the right.
            float ay = panel.yMax - 104 * k, ah = 32 * k;
            Ui.Box(new Rect(panel.x + 30 * k, ay - 10 * k, panel.width - 60 * k, Mathf.Max(1f, Ui.U * 0.5f)), Ui.Line);
            int custom = _style - Looks.Styles.Length;
            bool canDelete = custom >= 0 && custom < Looks.Mine2.Count;
            var acts = new System.Collections.Generic.List<string> { "Al azar", "Will original", "Guardar estilo", "Copiar código", "Pegar código" };
            if (canDelete) acts.Add("Borrar estilo");
            float lw = 120 * k, aw = (panel.width - 60 * k - lw - 20 * k - (acts.Count - 1) * 8 * k) / acts.Count;
            for (int a = 0; a < acts.Count; a++)
            {
                var r = new Rect(panel.x + 30 * k + a * (aw + 8 * k), ay, aw, ah);
                bool focused = Item(r, KAction * 100 + a) && pad;
                if (Ui.Button(r, acts[a], focused, false, true, 15f, acts[a] == "Borrar estilo" ? Ui.Tone.Red : Ui.Tone.Wood) || Pressed(focused)) Act(acts[a], custom);
            }
            var lr = new Rect(panel.xMax - 30 * k - lw, ay, lw, ah);
            bool lf = Item(lr, KAction * 100 + 99) && pad;
            if (Ui.Button(lr, "Listo", lf, false, true, 17f, Ui.Tone.Gold) || Pressed(lf)) SetOpen(false);
            bool note = Time.unscaledTime - _noteAt < 4f;   // a fresh note takes the hint line for a moment
            Ui.Label(new Rect(panel.x + 24 * k, panel.yMax - 62 * k, w - 48 * k, 20 * k),
                note ? _note : pad ? "Cruceta: moverse  ·  en una barra, izquierda y derecha cambian el color  ·  A: elegir  ·  B: cerrar"
                    : Teclas.Nombre("Ratón, o flechas para moverse (en una barra cambian el color)  ·  Enter: elegir  ·  F3 o Esc: cerrar",
                                "Toca una parte o un estilo  ·  en las barras, desliza el dedo  ·  botón Colores: cerrar"),
                14.5f, note ? Ui.Green : Ui.InkFaint, TextAnchor.MiddleCenter);

            Ui.EndOpen();
            if (Event.current.type == EventType.Repaint)
            {
                // Keep the focus on the same control when the layout changes (a style added or deleted).
                int had = FocusedId;
                _lastRects = new System.Collections.Generic.List<Rect>(_rects);
                _lastIds = new System.Collections.Generic.List<int>(_ids);
                if (_wantFocus < 0 && had >= 0) { int at = _lastIds.IndexOf(had); if (at >= 0) _focus = at; }
                if (_wantFocus >= 0) { int at = _lastIds.IndexOf(_wantFocus); if (at >= 0) _focus = at; _wantFocus = -1; }
                if (_focus >= _lastRects.Count) _focus = 0;
            }
        }

        private void Act(string what, int custom)
        {
            switch (what)
            {
                case "Al azar":
                    for (int i = 0; i < Looks.N; i++)
                        _parts[i] = i == Looks.Piel && Random.value < 0.6f ? new Looks.Part { Own = true }
                            : Looks.P(Random.Range(0, Looks.Tones), Looks.Pale(i) ? Random.Range(1, 12) : Random.Range(6, 16), Random.Range(3, 15));
                    Changed(); break;
                case "Will original": for (int i = 0; i < Looks.N; i++) _parts[i].Own = true; Changed(); break;
                case "Guardar estilo": Looks.SaveStyle(_parts); _note = "Guardado en tus estilos"; _noteAt = Time.unscaledTime; break;
                case "Copiar código": GUIUtility.systemCopyBuffer = Looks.ShareCode(Looks.Pack(_parts)); _note = "Copiado: " + GUIUtility.systemCopyBuffer; _noteAt = Time.unscaledTime; break;
                case "Pegar código":
                    int l = Looks.FromShareCode(GUIUtility.systemCopyBuffer);
                    if (l >= 0) { _parts = Looks.Unpack(l); Changed(); _note = "Aspecto pegado"; } else _note = "El portapapeles no tiene un código ML-…";
                    _noteAt = Time.unscaledTime; break;
                case "Borrar estilo": Looks.DeleteStyle(custom); _style = 0; _note = "Estilo borrado"; _noteAt = Time.unscaledTime; break;
            }
        }

        private void Bars(Rect r, float k, bool pad)
        {
            ref var p = ref _parts[_part];
            int part = _part;
            Ui.Section(new Rect(r.x, r.y, r.width - 150 * k, 20 * k), "Color de: " + Looks.Names[part]);
            var own = new Rect(r.xMax - 138 * k, r.y - 5 * k, 138 * k, 28 * k);
            bool of = Item(own, KOwn * 100) && pad;
            if (Ui.Button(own, "Como Will", of, p.Own, true, 15f) || Pressed(of)) { p.Own = !p.Own; Changed(); }
            var basePart = p.Own ? Looks.P(Looks.Start[part, 0], Looks.Start[part, 1], Looks.Start[part, 2]) : p;
            if (_grad1 == null)
            {
                var q = basePart;
                _grad1 = Grad(x => Looks.Swatch(Looks.P(q.Tone, Mathf.RoundToInt(x * 15), q.Bright), part), 16);
                _grad2 = Grad(x => Looks.Swatch(Looks.P(q.Tone, q.Intensity, Mathf.RoundToInt(x * 15)), part), 16);
            }
            var texs = new[] { _rainbow, _grad1, _grad2 };
            float u = Ui.U;
            for (int i = 0; i < 3; i++)
            {
                float y = r.y + 32 * k + i * 36 * k;
                var row = new Rect(r.x, y - 4 * k, r.width, 32 * k);
                bool sel = Item(row, KBar * 100 + i) && pad;
                if (sel) Ui.Round(row, new Color(Ui.BgLight.r, Ui.BgLight.g, Ui.BgLight.b, 0.8f), u);
                Ui.Label(new Rect(r.x + 10 * k, y, 100 * k, 24 * k), BarNames[i], 16f, sel ? Ui.Green : Ui.Ink);
                var br = new Rect(r.x + 116 * k, y + 4 * k, r.width - 126 * k, 16 * k);
                if (sel) { Ui.Arrow(new Rect(br.x - 14 * k, br.y, 10 * k, br.height), -1, Ui.Green); Ui.Arrow(new Rect(br.xMax + 4 * k, br.y, 10 * k, br.height), 1, Ui.Green); }
                Ui.Round(Ui.Shrink(br, -u), sel ? Ui.Green : Ui.Outline, u * 0.5f);
                GUI.color = p.Own ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                GUI.DrawTexture(br, texs[i]);
                GUI.color = Color.white;
                if (!p.Own)
                {
                    // The handle: a small cream peg with a dark rim.
                    float f = Get(p, i) / (float)Max(i);
                    var hx = br.x + f * br.width;
                    var peg = new Rect(hx - 4 * u, br.y - 3 * u, 8 * u, br.height + 6 * u);
                    Ui.Round(peg, Ui.Outline, u * 0.5f);
                    Ui.Round(Ui.Shrink(peg, u), Ui.Ink, u * 0.5f);
                }
                var e = Event.current;
                if (e.type == EventType.MouseDown && br.Contains(e.mousePosition)) { _dragBar = i; _wantFocus = KBar * 100 + i; }
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
