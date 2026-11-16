using Dogpatch.Look;
using Dogpatch.Map;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Slipstream HUD (handoffs/style/STYLE-GUIDE.md): the world fills the screen, the middle is
    // left to the game, and only a small pause button sits in the top-right corner. Also draws the
    // fixed stick in the bottom-right corner: a faint ring with its dot at rest, brighter with a
    // trace from the centre while a thumb holds it. Everything is flat
    // GL geometry in screen space; the only typeface is Exo 2 Black Italic. Speed lines are drawn
    // by Look.SpeedLines.
    public sealed partial class Hud : MonoBehaviour
    {
        const float TraceFade = 0.3f;
        const float Rest = 0.45f;

        public GameController Game;
        public DragInput Input;

        static readonly Color Ink = SpeedDial.Hex("#1C2347");
        static readonly Color Sunshine = SpeedDial.Hex("#FFCB1F");
        static readonly Color White = Color.white;

        Material material;
        Font font;

        public bool Paused { get; private set; }
        float gestureStart = -1f;
        int gesturePeak;
        float gestureTravel;
        bool gestureStick;

        Vector2 traceAnchor;
        Vector2 traceTip;
        float traceAlpha;
        bool wasDragging;
        float dragStartAt = -10f;

        // Gameplay feedback stays out of the middle of the screen (no sentences in the HUD), so
        // toasts only go to the log.
        public void Toast(string message, float seconds = 2.5f) => Debug.Log("[Hud] " + message);

        void Awake()
        {
            useGUILayout = false;
            material = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "Hud" };
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
        }

        void Start()
        {
            if (Input != null) Input.IgnoreAt = p => Paused || (PlateShown && InPausePlate(p));
        }

        float Scale => Mathf.Min(Screen.width, Screen.height) / 720f;

        void Update()
        {
            HandlePausePlate();
            HandlePause();

            bool dragging = Input != null && Input.Dragging;
            if (dragging && !wasDragging) dragStartAt = Time.unscaledTime;
            wasDragging = dragging;
            if (dragging)
            {
                traceAnchor = Input.Anchor;
                traceTip = Input.Current;
                traceAlpha = 1f;
            }
            else
            {
                bool fixedStick = !DragInput.DragAnywhere;
                if (fixedStick) traceAnchor = HudLayout.StickScreen;
                traceAlpha = Mathf.MoveTowards(traceAlpha, fixedStick ? Rest : 0f, Time.unscaledDeltaTime / TraceFade);
                if (fixedStick) traceTip = Vector2.Lerp(traceTip, traceAnchor, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            }

        }

        // The pause plate (Hud.Pause.cs) pauses; so does a quick two-finger tap anywhere off the stick pauses and resumes
        // (both fingers up within 0.4 s, barely moving, never a third finger, which is the perf
        // readout's gesture). Escape does the same on desktop.
        void HandlePause()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) SetPaused(!Paused);
            int n = UnityEngine.Input.touchCount;
            if (n == 0)
            {
                bool tap = gestureStart >= 0f && gesturePeak == 2 && !gestureStick && Time.unscaledTime - gestureStart < 0.4f
                    && gestureTravel < Mathf.Min(Screen.width, Screen.height) * 0.04f;
                if (tap) SetPaused(!Paused);
                gestureStart = -1f;
                gesturePeak = 0;
                gestureTravel = 0f;
                gestureStick = false;
                return;
            }
            if (gestureStart < 0f) gestureStart = Time.unscaledTime;
            gesturePeak = Mathf.Max(gesturePeak, n);
            for (int i = 0; i < n; i++)
            {
                var t = UnityEngine.Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && HudLayout.InStick(t.position)) gestureStick = true;
                gestureTravel = Mathf.Max(gestureTravel, (t.position - t.rawPosition).magnitude);
            }
        }

        void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Audio.GameEvents.RaisePaused(paused);
            if (paused && Input != null) Input.Cancel();
            Debug.Log("[Hud] " + (paused ? "paused" : "resumed"));
        }

        void OnGUI()
        {
            float s = Scale;
            if (Paused && Event.current.type != EventType.Repaint) DrawPauseMenu();
            if (Event.current.type != EventType.Repaint) return;
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);
            DrawCarMarker(s);
            if (!Paused && Game != null && Game.IsReady && !DragInput.DragAnywhere) DrawStick(s);
            else if (!Paused && traceAlpha > 0.01f) DrawTrace(s);
            GL.End();
            GL.PopMatrix();
            DrawPausePlateButton();
            if (Paused) DrawPauseMenu();
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

        // The drag-anywhere scheme's faint trace under the thumb, which fades when the finger lifts.
        void DrawTrace(float s)
        {
            float reach = Input != null ? Input.Reach : 200f * s;
            var a = traceAnchor;
            var tip = a + Vector2.ClampMagnitude(traceTip - a, reach);
            var faint = new Color(1f, 1f, 1f, 0.5f * traceAlpha);
            float since = Time.unscaledTime - dragStartAt;
            Ring(a, 46f * s, 2.6f * s, faint);
            if (since < 0.28f) Ring(a, Mathf.Lerp(46f, 78f, since / 0.28f) * s, 2f * s, new Color(1f, 1f, 1f, 0.6f * (1f - since / 0.28f)));
            Disc(a, 4f * s, faint);
            float t = Mathf.Clamp01(since / 0.22f) - 1f;
            float pop = 1f + t * t * (3.2f * t + 2.2f);
            Disc(tip, 15f * s * Mathf.Max(0.05f, pop), new Color(1f, 1f, 1f, 0.85f * traceAlpha));
        }

        void DrawStick(float s)
        {
            float reach = HudLayout.StickRadius;
            var a = HudLayout.StickScreen;
            var tip = a + Vector2.ClampMagnitude(traceTip - a, reach);
            Disc(a, reach, new Color(Ink.r, Ink.g, Ink.b, 0.16f), 48);
            var d = tip - a;
            bool down = traceAlpha >= 0.999f;
            bool pushed = d.magnitude > reach * 0.4f;
            float heading = Mathf.Round(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg / 30f) * 30f;
            float outer = reach, inner = reach * 0.8f;
            for (int i = 0; i < 12; i++)
            {
                float mid = i * 30f;
                bool lit = pushed && Mathf.Abs(Mathf.DeltaAngle(mid, heading)) < 31f;
                var color = lit ? Sunshine : new Color(1f, 1f, 1f, 0.5f * Mathf.Max(traceAlpha, 0.6f));
                for (int k = 0; k < 3; k++)
                {
                    float a0 = (mid - 12f + k * 8f) * Mathf.Deg2Rad, a1 = (mid - 12f + (k + 1) * 8f) * Mathf.Deg2Rad;
                    var u0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                    var u1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                    Quad(a + u0 * inner, a + u0 * outer, a + u1 * outer, a + u1 * inner, color);
                }
            }
            float knob = reach * 0.39f;
            Disc(tip, knob + 2.4f * s, new Color(Ink.r, Ink.g, Ink.b, down ? 1f : 0.6f * traceAlpha), 32);
            Disc(tip, knob, down ? Sunshine : new Color(1f, 1f, 1f, 0.85f * traceAlpha), 32);
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
