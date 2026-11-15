using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Slipstream HUD (handoffs/style/STYLE-GUIDE.md): the world fills the screen, the middle is
    // left to the game, and only a small pause button sits in the top-right corner. Also draws the
    // faint drag trace under the thumb, which fades as soon as the finger lifts, and speed lines at
    // the screen edges that grow with speed. Everything is flat GL geometry in screen space; the
    // only typeface is Exo 2 Black Italic.
    public sealed class Hud : MonoBehaviour
    {
        const float TraceFade = 0.3f;
        const int SpeedLineCount = 18;

        public GameController Game;
        public DragInput Input;

        static readonly Color Ink = SpeedDial.Hex("#1C2347");
        static readonly Color Sunshine = SpeedDial.Hex("#FFCB1F");
        static readonly Color White = Color.white;

        Material material;
        Font font;
        GUIStyle title;

        public bool Paused { get; private set; }
        bool pressing;
        int pressFinger = -1;

        Vector2 traceAnchor;
        Vector2 traceTip;
        float traceAlpha;

        struct Streak
        {
            public float Angle;
            public float Radius;
            public float Length;
            public float Speed;
        }

        readonly Streak[] streaks = new Streak[SpeedLineCount];
        float speedLines;
        float smearSpin;
        readonly System.Random random = new System.Random(5);

        // Gameplay feedback stays out of the middle of the screen (no sentences in the HUD), so
        // toasts only go to the log.
        public void Toast(string message, float seconds = 2.5f) => Debug.Log("[Hud] " + message);

        void Awake()
        {
            useGUILayout = false;
            material = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "Hud" };
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
            for (int i = 0; i < streaks.Length; i++) streaks[i] = NewStreak(true);
        }

        void Start()
        {
            if (Input != null) Input.IgnoreAt = p => Paused || PauseRect().Contains(p);
        }

        float Scale => Mathf.Min(Screen.width, Screen.height) / 720f;

        // Pause button rect in screen pixels with the origin bottom-left, inside the safe area.
        Rect PauseRect()
        {
            float s = Scale;
            var safe = Screen.safeArea;
            float w = 74f * s, h = 50f * s, m = 22f * s;
            return new Rect(safe.xMax - m - w, safe.yMax - m - h, w, h);
        }

        void Update()
        {
            HandlePause();

            if (Input != null && Input.Dragging)
            {
                traceAnchor = Input.Anchor;
                traceTip = Input.Current;
                traceAlpha = 1f;
            }
            else
            {
                traceAlpha = Mathf.MoveTowards(traceAlpha, 0f, Time.unscaledDeltaTime / TraceFade);
            }

            float target = Mathf.Clamp01(MotionFx.SpeedFactor * 1.25f - 0.1f + MotionFx.BoostFactor);
            speedLines = Mathf.MoveTowards(speedLines, Paused ? 0f : target, Time.unscaledDeltaTime * 2.5f);
            float dt = Paused ? 0f : Time.unscaledDeltaTime;
            smearSpin += dt * (0.4f + speedLines);
            for (int i = 0; i < streaks.Length; i++)
            {
                streaks[i].Radius += streaks[i].Speed * dt * (0.6f + speedLines);
                if (streaks[i].Radius > 1.25f) streaks[i] = NewStreak(false);
            }
        }

        void HandlePause()
        {
            var rect = PauseRect();
            bool down = false, up = false, inside = false;
            if (UnityEngine.Input.touchCount > 0)
            {
                for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                {
                    var t = UnityEngine.Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began && pressFinger < 0 && rect.Contains(t.position))
                    {
                        pressFinger = t.fingerId;
                        down = true;
                    }
                    if (t.fingerId != pressFinger) continue;
                    inside = rect.Contains(t.position);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) up = true;
                }
            }
            else
            {
                Vector2 mouse = UnityEngine.Input.mousePosition;
                inside = rect.Contains(mouse);
                if (UnityEngine.Input.GetMouseButtonDown(0) && inside) { pressFinger = 1000; down = true; }
                if (pressFinger == 1000 && UnityEngine.Input.GetMouseButtonUp(0)) up = true;
            }
            if (down) pressing = true;
            if (pressFinger >= 0 && !up) pressing = inside;
            if (!up) return;
            if (pressing) SetPaused(!Paused);
            pressing = false;
            pressFinger = -1;
        }

        void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Audio.GameEvents.RaisePaused(paused);
            if (paused && Input != null) Input.Cancel();
            Debug.Log("[Hud] " + (paused ? "paused" : "resumed"));
        }

        Streak NewStreak(bool scatter) => new Streak
        {
            Angle = PickAngle(),
            Radius = scatter ? 0.45f + (float)random.NextDouble() * 0.8f : 0.45f + (float)random.NextDouble() * 0.2f,
            Length = 0.12f + (float)random.NextDouble() * 0.22f,
            Speed = 1.6f + (float)random.NextDouble() * 1.4f,
        };

        // Streaks fan out toward the four corners and sides, never straight down over the car or
        // straight up the middle of the sky.
        float PickAngle()
        {
            while (true)
            {
                float a = (float)random.NextDouble() * 360f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 270f)) < 32f || Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 22f) continue;
                return a * Mathf.Deg2Rad;
            }
        }

        void OnGUI()
        {
            float s = Scale;
            if (Paused && Event.current.type != EventType.Repaint) DrawEnvironmentMenu(s);
            if (Event.current.type != EventType.Repaint) return;
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);
            if (speedLines > 0.01f) DrawSmear();
            if (speedLines > 0.01f) DrawSpeedLines();
            DrawCarMarker(s);
            if (traceAlpha > 0.01f) DrawTrace(s);
            DrawPause(s);
            GL.End();
            GL.PopMatrix();
            if (Paused) DrawPausedText(s);
            if (Paused) DrawEnvironmentMenu(s);
        }

        void DrawSpeedLines()
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.56f);
            float reach = new Vector2(Screen.width, Screen.height).magnitude * 0.5f;
            float width = Mathf.Max(1.5f, Scale * 3.2f);
            foreach (var st in streaks)
            {
                var dir = new Vector2(Mathf.Cos(st.Angle), Mathf.Sin(st.Angle));
                var normal = new Vector2(-dir.y, dir.x);
                var inner = center + dir * st.Radius * reach;
                var outer = center + dir * (st.Radius + st.Length) * reach;
                float fadeIn = Mathf.Clamp01((st.Radius - 0.45f) / 0.25f);
                var c = new Color(1f, 1f, 1f, 0.55f * speedLines * fadeIn);
                Tri(inner, outer + normal * width, outer - normal * width, c);
            }
        }

        // Cheap radial speed smear: a ring of translucent white wedges around the screen edge that
        // fade to nothing toward the middle. Flat geometry, no post-processing.
        void DrawSmear()
        {
            const int wedges = 36;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.56f);
            float outer = new Vector2(Screen.width, Screen.height).magnitude * 0.62f;
            float inner = outer * 0.55f;
            for (int i = 0; i < wedges; i++)
            {
                float a0 = (i + 0.15f) * Mathf.PI * 2f / wedges + smearSpin * 0.05f;
                float a1 = (i + 0.85f) * Mathf.PI * 2f / wedges + smearSpin * 0.05f;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                float pulse = 0.6f + 0.4f * Mathf.Sin(i * 2.3f + smearSpin * 3f);
                var edge = new Color(1f, 1f, 1f, 0.2f * speedLines * pulse);
                var clear = new Color(1f, 1f, 1f, 0f);
                GL.Color(clear); GL.Vertex3(center.x + d0.x * inner, center.y + d0.y * inner, 0f);
                GL.Color(edge); GL.Vertex3(center.x + d0.x * outer, center.y + d0.y * outer, 0f);
                GL.Color(edge); GL.Vertex3(center.x + d1.x * outer, center.y + d1.y * outer, 0f);
                GL.Color(clear); GL.Vertex3(center.x + d0.x * inner, center.y + d0.y * inner, 0f);
                GL.Color(edge); GL.Vertex3(center.x + d1.x * outer, center.y + d1.y * outer, 0f);
                GL.Color(clear); GL.Vertex3(center.x + d1.x * inner, center.y + d1.y * inner, 0f);
            }
        }

        // The tap-a-car pictogram: a bobbing, forward-leaning sunshine chevron with an ink outline
        // over the nearest car you can get into. No words.
        void DrawCarMarker(float s)
        {
            var car = Game != null ? Game.NearestCar : null;
            if (car == null || Game.View == null || Paused) return;
            var marker = car.transform.position + Vector3.up * (car.HalfExtents.y * 2f + 1.1f + Mathf.Sin(Time.time * 4f) * 0.15f);
            var p = Game.View.WorldToScreenPoint(marker);
            if (p.z <= 0f) return;
            var c = new Vector2(p.x, p.y);
            float w = 15f * s, h = 13f * s, lean = 4f * s, t = 5.5f * s;
            void Chevron(float grow, Color color)
            {
                var tip = c + new Vector2(0f, -h - grow);
                var l = c + new Vector2(-w - grow + lean, h + grow);
                var r = c + new Vector2(w + grow + lean, h + grow);
                var li = l + new Vector2(t * 1.1f, 0f);
                var ri = r - new Vector2(t * 1.1f, 0f);
                var tipIn = tip + new Vector2(0f, t * 1.6f);
                Quad(l, li, tipIn, tip, color);
                Quad(tip, tipIn, ri, r, color);
            }
            Chevron(2.4f * s, Ink);
            Chevron(0f, Sunshine);
        }

        void DrawTrace(float s)
        {
            float reach = Input != null ? Input.Reach : 200f * s;
            var a = traceAnchor;
            var tip = a + Vector2.ClampMagnitude(traceTip - a, reach);
            float ring = 46f * s;
            var faint = new Color(1f, 1f, 1f, 0.5f * traceAlpha);
            Ring(a, ring, 2.6f * s, faint);
            Disc(a, 4f * s, faint);
            var d = tip - a;
            if (d.magnitude > ring * 0.4f)
            {
                var dir = d.normalized;
                var n = new Vector2(-dir.y, dir.x);
                float w = 1.6f * s;
                var from = a + dir * 4f * s;
                var to = tip - dir * 17f * s;
                Quad(from + n * w, to + n * w, to - n * w, from - n * w, faint);
                var head = tip - dir * 22f * s;
                Tri(head + dir * 9f * s, head - dir * 3f * s + n * 7f * s, head - dir * 3f * s - n * 7f * s, faint);
            }
            Disc(tip, 15f * s, new Color(1f, 1f, 1f, 0.85f * traceAlpha));
        }

        // Pause at rest: white forward-leaning plate, ink outline, ink bars. Pressed: inverted to
        // sunshine, shifted down and right and shrunk slightly. Flat, no bevel, no gloss.
        void DrawPause(float s)
        {
            var r = PauseRect();
            var center = r.center;
            float k = 1f;
            if (pressing)
            {
                k = 0.9f;
                center += new Vector2(3f, -3f) * s;
            }
            float w = r.width * 0.5f * k, h = r.height * 0.5f * k, lean = 9f * s * k;
            Parallelogram(center, w, h, lean, Ink);
            float b = 3f * s * k;
            Parallelogram(center, w - b, h - b, lean, pressing ? Sunshine : White);
            float bar = 5.5f * s * k, bh = h * 0.5f, gap = 6.5f * s * k, blean = lean * 0.55f;
            Parallelogram(center + new Vector2(-gap, 0f), bar * 0.5f, bh, blean, Ink);
            Parallelogram(center + new Vector2(gap, 0f), bar * 0.5f, bh, blean, Ink);
        }

        void DrawPausedText(float s)
        {
            if (title == null)
            {
                title = new GUIStyle { font = font, alignment = TextAnchor.MiddleLeft };
            }
            title.fontSize = Mathf.RoundToInt(44f * s);
            var safe = Screen.safeArea;
            float left = safe.xMin + 24f * s, top = Screen.height - safe.yMax + 22f * s;
            Outlined(new Rect(left, top, 400f * s, 56f * s), "PAUSED", title, White, s);
        }

        static readonly (string label, string query)[][] EnvironmentRows =
        {
            new[] { ("DAWN", "time=dawn"), ("DAY", "time=day"), ("GOLDEN", "time=golden"), ("DUSK", "time=dusk"), ("NIGHT", "time=night") },
            new[] { ("SUN", "weather=sun"), ("RAIN", "weather=rain"), ("SNOW", "weather=snow"), ("FOG", "fog=1"), ("CLEAR", "fog=0.35") },
        };

        GUIStyle chip;

        // Debug menu while paused: time of day, weather and fog presets as flat chips under PAUSED.
        // They apply at once.
        void DrawEnvironmentMenu(float s)
        {
            if (chip == null) chip = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter };
            chip.fontSize = Mathf.RoundToInt(18f * s);
            var safe = Screen.safeArea;
            float left = safe.xMin + 24f * s, top = Screen.height - safe.yMax + 92f * s;
            float w = 104f * s, h = 40f * s, gap = 12f * s;
            for (int row = 0; row < EnvironmentRows.Length; row++)
                for (int i = 0; i < EnvironmentRows[row].Length; i++)
                {
                    var (label, query) = EnvironmentRows[row][i];
                    var r = new Rect(left + i * (w + gap), top + row * (h + gap), w, h);
                    if (Event.current.type == EventType.Repaint)
                    {
                        material.SetPass(0);
                        GL.PushMatrix();
                        GL.LoadPixelMatrix();
                        GL.Begin(GL.TRIANGLES);
                        var c = new Vector2(r.center.x, Screen.height - r.center.y);
                        Parallelogram(c, w * 0.5f, h * 0.5f, 7f * s, Ink);
                        GL.End();
                        GL.PopMatrix();
                    }
                    chip.normal.textColor = White;
                    if (GUI.Button(r, label, chip)) WorldEnvironment.Apply(query);
                }
        }

        static void Outlined(Rect r, string text, GUIStyle style, Color color, float s)
        {
            float o = Mathf.Max(1f, 1.6f * s);
            style.normal.textColor = Ink;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), text, style);
            }
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        static void Tri(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            GL.Color(color);
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
        }

        static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            Tri(a, b, c, color);
            Tri(a, c, d, color);
        }

        static void Parallelogram(Vector2 c, float w, float h, float lean, Color color) =>
            Quad(c + new Vector2(-w - lean, -h), c + new Vector2(-w + lean, h), c + new Vector2(w + lean, h), c + new Vector2(w - lean, -h), color);

        static void Disc(Vector2 c, float r, Color color, int segments = 24)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, color);
            }
        }

        static void Ring(Vector2 c, float r, float width, Color color, int segments = 40)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Quad(c + d0 * (r - width), c + d0 * r, c + d1 * r, c + d1 * (r - width), color);
            }
        }
    }
}
