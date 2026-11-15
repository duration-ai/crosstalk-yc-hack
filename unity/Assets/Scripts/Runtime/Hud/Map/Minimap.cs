using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Nav;
using Dogpatch.Race;
using Dogpatch.Slipstream;
using UnityEngine;

namespace Dogpatch.Map
{
    // GTA-style minimap in the bottom-left corner, the one corner the Slipstream HUD leaves free
    // (place and the lobby roster top left, meters and pause top right, Call and Ready bottom right).
    // A circle with an ink rim, heading-up with the camera, zooming out with speed. The map is a
    // texture baked once at startup (MapBaker) and drawn as a textured fan under the rim, so there is
    // no second camera. On top: the route (sunshine), the start ring, payphones (pink dots), ask
    // pickups, the finish flag, your parked race car, the other racers as their colour blocks, and
    // you. Racers and the objective outside the circle are pinned to its rim as small chevrons
    // pointing their way. It draws only, so touches fall through to the drag control.
    public sealed class Minimap : MonoBehaviour
    {
        const string PrefKey = "dogpatch.minimap";
        const int FanSegments = 64;
        const float NearMeters = 70f;
        const float FarMeters = 170f;

        static readonly Color Ink = SpeedDial.Hex("#1C2347");
        static readonly Color Sunshine = SpeedDial.Hex("#FFCB1F");
        static readonly Color Pink = SpeedDial.Hex("#FF3E8E");
        static readonly Color White = Color.white;

        static bool? shown;

        // The pause menu's MAP chip; remembered across launches.
        public static bool Shown
        {
            get => shown ??= PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set
            {
                shown = value;
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public GameController Game;

        Material marks;
        Material mapMaterial;
        Texture2D map;
        Rect bounds;
        Task<MapBaker.Result> bake;
        float bakeStarted;
        Font font;
        GUIStyle style;
        float radiusMeters = NearMeters;

        float s;
        float radius;
        float clip;
        Vector2 center;
        Vector2 me;
        Vector2 right;
        Vector2 forward;
        float pxPerMeter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            GameController.WorldReady -= Create;
            GameController.WorldReady += Create;
        }

        static void Create(GameController game)
        {
            if (game.GetComponent<Minimap>() == null) game.gameObject.AddComponent<Minimap>().Game = game;
        }

        static Color32 Hex32(string hex) => SpeedDial.Hex(hex);

        void Start()
        {
            useGUILayout = false;
            marks = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "MinimapMarks" };
            mapMaterial = new Material(Resources.Load<Shader>("Shaders/MapTexture")) { name = "MinimapMap" };
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
            var palette = new MapBaker.Palette
            {
                Bay = Hex32("#17B2D0"),
                Land = Hex32("#E6DDCB"),
                Park = Hex32("#48C26A"),
                Path = Hex32("#EEE7DA"),
                Street = Hex32("#A9ADB4"),
                Building = Hex32("#FF9C86"),
            };
            var data = Game.Data;
            bakeStarted = Time.realtimeSinceStartup;
            bake = Task.Run(() => MapBaker.Bake(data, palette));
        }

        void Update()
        {
            if (bake != null && bake.IsCompleted)
            {
                if (bake.IsFaulted) Debug.LogError("[Minimap] bake failed: " + bake.Exception?.GetBaseException());
                else
                {
                    var r = bake.Result;
                    map = new Texture2D(r.Width, r.Height, TextureFormat.RGBA32, false, true)
                    {
                        name = "Minimap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                    };
                    map.SetPixels32(r.Pixels);
                    map.Apply(false, true);
                    bounds = r.Bounds;
                    mapMaterial.mainTexture = map;
                    Debug.Log($"[Minimap] baked {r.Width}x{r.Height} map in {(Time.realtimeSinceStartup - bakeStarted) * 1000f:F0} ms");
                }
                bake = null;
            }

            if (Game == null || !Game.IsReady) return;
            float speed = Mathf.Abs(Game.CurrentCar != null ? Game.CurrentCar.Speed : Game.Player != null ? Game.Player.Speed : 0f);
            float target = Mathf.Lerp(NearMeters, FarMeters, Mathf.Clamp01((speed - 4f) / 24f));
            radiusMeters = Mathf.Lerp(radiusMeters, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.6f));
        }

        Vector2 ToMap(Vector2 world)
        {
            var d = world - me;
            return center + new Vector2(Vector2.Dot(d, right), Vector2.Dot(d, forward)) * pxPerMeter;
        }

        Vector2 ToMap(Vector3 world) => ToMap(new Vector2(world.x, world.z));

        Vector2 MapDirection(float headingDegrees)
        {
            float h = headingDegrees * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Sin(h), Mathf.Cos(h));
            return new Vector2(Vector2.Dot(d, right), Vector2.Dot(d, forward));
        }

        bool Inside(Vector2 p, float margin) => (p - center).sqrMagnitude < (clip - margin) * (clip - margin);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Shown || map == null || Game == null || !Game.IsReady) return;
            var focus = Game.Focus;
            var cam = Game.View;
            if (focus == null || cam == null) return;

            s = Mathf.Min(Screen.height / 402f, Screen.width / 560f);
            var safe = Screen.safeArea;
            radius = 50f * s;
            clip = radius - 0.5f * s;
            center = new Vector2(safe.xMin + 22f * s + radius, safe.yMin + 18f * s + radius);
            me = new Vector2(focus.position.x, focus.position.z);
            float yaw = cam.transform.eulerAngles.y * Mathf.Deg2Rad;
            forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            pxPerMeter = radius / radiusMeters;

            GL.PushMatrix();
            GL.LoadPixelMatrix();
            DrawMap();
            marks.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            DrawMarks(focus);
            GL.End();
            GL.PopMatrix();
            DrawDistance();
        }

        void DrawMap()
        {
            mapMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            var fill = new Color(1f, 1f, 1f, 0.88f);
            Vector2 Uv(Vector2 offset)
            {
                var w = me + (right * offset.x + forward * offset.y) / pxPerMeter;
                return new Vector2((w.x - bounds.xMin) / bounds.width, (w.y - bounds.yMin) / bounds.height);
            }
            void V(Vector2 offset)
            {
                GL.Color(fill);
                var uv = Uv(offset);
                GL.TexCoord2(uv.x, uv.y);
                GL.Vertex3(center.x + offset.x, center.y + offset.y, 0f);
            }
            for (int i = 0; i < FanSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / FanSegments, a1 = (i + 1) * Mathf.PI * 2f / FanSegments;
                V(Vector2.zero);
                V(new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius);
                V(new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius);
            }
            GL.End();
        }

        void DrawMarks(Transform focus)
        {
            var race = RaceDirector.Instance;
            bool raceUp = race != null && race.Setup;
            var nav = NavService.Instance;

            Ring(center, radius + 3.4f * s, 3.9f * s, Ink);
            var north = MapDirection(0f);
            var northRight = new Vector2(north.y, -north.x);
            var tip = center + north * (radius + 6.4f * s);
            var baseCenter = center + north * (radius + 0.6f * s);
            Tri(tip, baseCenter + northRight * 3.6f * s, baseCenter - northRight * 3.6f * s, Sunshine);

            if (nav != null && nav.HasObjective && nav.Path.Count >= 2) Route(nav.Path);
            else if (raceUp && race.Gridded && !race.ResultsIn) Route(race.Course.Route.Points);

            if (raceUp && !race.Gridded)
            {
                var ring = ToMap(race.Course.RingCenter);
                if (Inside(ring, 6f * s)) Ring(ring, Mathf.Max(race.Course.RingRadius * pxPerMeter, 4.5f * s), 1.8f * s, Sunshine);
            }

            if (raceUp && race.Calls != null)
                foreach (var phone in race.Calls.Phones)
                {
                    if (phone == null) continue;
                    var p = ToMap(phone.transform.position);
                    if (!Inside(p, 3f * s)) continue;
                    Disc(p, 3.5f * s, Ink, 12);
                    Disc(p, 2.5f * s, phone.Ringing ? Pink : new Color(Pink.r, Pink.g, Pink.b, 0.45f), 12);
                }

            foreach (var (target, _) in MapMarkers.All)
            {
                var p = ToMap(target.position);
                if (Inside(p, 4f * s)) Diamond(p, 4.8f * s, 3.4f * s);
                else Pin(p, 3.6f * s, White);
            }

            if (raceUp)
            {
                var finish = ToMap(race.Course.Finish);
                if (Inside(finish, 7f * s)) Flag(finish);
            }

            if (nav != null && nav.HasObjective)
            {
                var p = ToMap(nav.Objective);
                var color = nav.Kind == NavKind.Payphone ? Pink : Sunshine;
                if (!Inside(p, 5f * s)) Pin(p, 4.4f * s, color);
                else if (nav.Kind == NavKind.Waypoint) Flag(p, Sunshine);
                else if (nav.Kind == NavKind.Payphone) Ring(p, (5.2f + Mathf.Sin(Time.unscaledTime * 6f) * 0.8f) * s, 1.4f * s, Pink);
            }

            var mySlotColor = raceUp ? SpeedDial.Hex(RaceDirector.SlotHex[race.MySlot]) : White;
            if (raceUp && race.MyCar != null && Game.CurrentCar != race.MyCar)
            {
                var p = ToMap(race.MyCar.transform.position);
                if (Inside(p, 5f * s)) Car(p, MapDirection(race.MyCar.transform.eulerAngles.y), 0.8f, mySlotColor);
            }

            if (raceUp && race.Last?.racers != null)
                foreach (var r in race.Last.racers)
                {
                    if (r.left || r.playerId == race.MyId) continue;
                    var body = race.BodyOf(r.playerId);
                    var world = body != null ? body.position : new Vector3(r.position.x, r.position.y, r.position.z);
                    var p = ToMap(world);
                    var color = SlipstreamHud.SlotColor(r.color);
                    if (Inside(p, 5f * s))
                    {
                        Parallelogram(p, 5.2f * s, 5.2f * s, 1.6f * s, Ink);
                        Parallelogram(p, 3.8f * s, 3.8f * s, 1.6f * s, color);
                    }
                    else Pin(p, 3.6f * s, color);
                }

            var heading = MapDirection(focus.eulerAngles.y);
            if (Game.CurrentCar != null) Car(center, heading, 1f, mySlotColor);
            Arrow(center, heading, (Game.CurrentCar != null ? 5.6f : 6.6f) * s, Sunshine);
        }

        // Ink underlay then a sunshine line, clipped to the circle, with round joints.
        void Route(IReadOnlyList<Vector2> points)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float half = (pass == 0 ? 2.5f : 1.4f) * s;
                var color = pass == 0 ? Ink : Sunshine;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var a = ToMap(points[i]);
                    var b = ToMap(points[i + 1]);
                    if (!ClipToCircle(ref a, ref b, center, clip - half)) continue;
                    var d = b - a;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    var n = new Vector2(-d.y, d.x).normalized * half;
                    Quad(a + n, b + n, b - n, a - n, color);
                    Disc(b, half, color, 8);
                    if (i == 0) Disc(a, half, color, 8);
                }
            }
        }

        static bool ClipToCircle(ref Vector2 a, ref Vector2 b, Vector2 c, float r)
        {
            var d = b - a;
            var f = a - c;
            float qa = Vector2.Dot(d, d);
            float qc = Vector2.Dot(f, f) - r * r;
            if (qa < 1e-6f) return qc <= 0f;
            float qb = 2f * Vector2.Dot(f, d);
            float disc = qb * qb - 4f * qa * qc;
            if (disc < 0f) return false;
            float root = Mathf.Sqrt(disc);
            float t0 = Mathf.Max(0f, (-qb - root) / (2f * qa));
            float t1 = Mathf.Min(1f, (-qb + root) / (2f * qa));
            if (t0 > t1) return false;
            var start = a + d * t0;
            b = a + d * t1;
            a = start;
            return true;
        }

        // A small chevron on the rim pointing toward something outside the circle.
        void Pin(Vector2 p, float size, Color color)
        {
            var dir = (p - center).normalized;
            var at = center + dir * (clip - size * 0.9f);
            Arrow(at, dir, size, color);
        }

        // Forward-pointing arrowhead in local axes (forward = dir), ink outline first.
        void Arrow(Vector2 c, Vector2 dir, float size, Color color)
        {
            var r = new Vector2(dir.y, -dir.x);
            void Shape(float k, Color col)
            {
                var o = c - dir * size * 0.1f;
                var tipP = o + dir * size * k;
                var rb = o + (r * 0.75f - dir * 0.8f) * size * k;
                var lb = o + (-r * 0.75f - dir * 0.8f) * size * k;
                var notch = o - dir * size * 0.35f * k;
                Tri(tipP, rb, notch, col);
                Tri(tipP, notch, lb, col);
            }
            Shape(1.45f, Ink);
            Shape(1f, color);
        }

        void Car(Vector2 c, Vector2 dir, float scale, Color color)
        {
            var r = new Vector2(dir.y, -dir.x);
            void Box(float w, float l, Color col) =>
                Quad(c + (r * w + dir * l), c + (r * w - dir * l), c + (-r * w - dir * l), c + (-r * w + dir * l), col);
            Box(4.4f * s * scale, 7.4f * s * scale, Ink);
            Box(3.2f * s * scale, 6.2f * s * scale, color);
        }

        void Diamond(Vector2 c, float outer, float inner)
        {
            Quad(c + Vector2.up * outer, c + Vector2.right * outer, c + Vector2.down * outer, c + Vector2.left * outer, Ink);
            Quad(c + Vector2.up * inner, c + Vector2.right * inner, c + Vector2.down * inner, c + Vector2.left * inner, White);
        }

        // Checkered flag on an ink pole, the flag cloth leaning forward like every Slipstream panel.
        void Flag(Vector2 foot, Color? solid = null)
        {
            float pole = 13f * s, w = 10f * s, h = 7f * s, lean = 1.6f * s;
            Quad(foot + new Vector2(-1.1f * s, 0f), foot + new Vector2(-1.1f * s, pole), foot + new Vector2(1.1f * s, pole), foot + new Vector2(1.1f * s, 0f), Ink);
            var origin = foot + new Vector2(1.1f * s, pole - h);
            Quad(origin + new Vector2(-1f * s, -1f * s), origin + new Vector2(-1f * s + lean, h + 1f * s), origin + new Vector2(w + 1f * s + lean, h + 1f * s), origin + new Vector2(w + 1f * s, -1f * s), Ink);
            const int cols = 4, rows = 3;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                {
                    var color = solid ?? ((x + y) % 2 == 0 ? White : Ink);
                    float x0 = x * w / cols, x1 = (x + 1) * w / cols, y0 = y * h / rows, y1 = (y + 1) * h / rows;
                    float l0 = lean * y0 / h, l1 = lean * y1 / h;
                    Quad(origin + new Vector2(x0 + l0, y0), origin + new Vector2(x0 + l1, y1), origin + new Vector2(x1 + l1, y1), origin + new Vector2(x1 + l0, y0), color);
                }
        }

        // Distance to a free-roam objective, frameless white Exo 2 with a thin ink outline just
        // above the rim (a race already has its meters to YC top right).
        void DrawDistance()
        {
            var nav = NavService.Instance;
            if (nav == null || !nav.HasObjective || nav.Arrived || nav.Kind == NavKind.Race) return;
            int meters = Mathf.RoundToInt(nav.Remaining);
            meters = meters < 100 ? Mathf.Max(5, Mathf.RoundToInt(meters / 5f) * 5) : Mathf.RoundToInt(meters / 10f) * 10;
            if (style == null) style = new GUIStyle(GUI.skin.label) { font = font, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(), alignment = TextAnchor.LowerLeft };
            var num = meters.ToString();
            style.fontSize = Mathf.RoundToInt(20f * s);
            var numSize = style.CalcSize(new GUIContent(num));
            float x = center.x + radius * 0.18f;
            float baseline = Screen.height - (center.y + radius + 7f * s);
            Outlined(new Rect(x, baseline - numSize.y, numSize.x, numSize.y), num, 1.6f * s);
            style.fontSize = Mathf.RoundToInt(11f * s);
            var mSize = style.CalcSize(new GUIContent("M"));
            Outlined(new Rect(x + numSize.x + 2f * s, baseline - mSize.y - 2f * s, mSize.x, mSize.y), "M", 1.2f * s);
        }

        // IMGUI text is linearized once more than GL colours in a linear-space build, so text
        // colours go in gamma-corrected (as in SlipstreamHud).
        static Color TextColor(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;

        void Outlined(Rect r, string text, float outline)
        {
            style.normal.textColor = TextColor(Ink);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), text, style);
            }
            style.normal.textColor = TextColor(White);
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

        static void Disc(Vector2 c, float r, Color color, int segments = 20)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, color);
            }
        }

        static void Ring(Vector2 c, float r, float width, Color color, int segments = 64)
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
