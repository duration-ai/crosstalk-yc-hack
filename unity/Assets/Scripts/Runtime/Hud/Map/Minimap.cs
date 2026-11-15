using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Nav;
using Dogpatch.Race;
using Dogpatch.Slipstream;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Map
{
    // GTA-style minimap in the bottom-left corner, the one corner the Slipstream HUD leaves free
    // (place and the lobby roster top left, meters and pause top right, Call and Ready bottom right).
    // A circle with an ink rim, heading-up with the camera, zooming out with speed, sized in
    // physical units from Screen.dpi (capped by the safe area) so it reads the same on every phone.
    // The map is vector meshes (MapGeometry, built off the main thread) rendered each frame into a
    // 4x MSAA target exactly the circle's size in screen pixels, so it is 1:1 and antialiased at any
    // zoom, then drawn as a textured fan under the rim; there is no second camera. On top: the route
    // (sunshine), the start ring, ask pickups, the finish flag, your parked race car, the other
    // racers as their colour blocks, and you. Racers and the objective outside the circle are
    // pinned to its rim as small chevrons pointing their way.
    //
    // A tap on the circle opens the full map: north-up over the whole screen with pinch zoom and
    // pan, street names on the major streets, the landmarks, and the same marks with names on the
    // players. A tap (or the X) closes it. Drags that start on the circle still reach the drag
    // control; only the tap is taken. While the full map is open it takes every touch, but the
    // world (and any online race) keeps running underneath.
    public sealed class Minimap : MonoBehaviour
    {
        const string PrefKey = "dogpatch.minimap";
        const int FanSegments = 96;
        const float NearMeters = 90f;
        const float FarMeters = 210f;
        const float TapSeconds = 0.35f;

        static readonly Color Ink = SpeedDial.Hex("#1C2347");
        static readonly Color Sunshine = SpeedDial.Hex("#FFCB1F");
        static readonly Color Pink = SpeedDial.Hex("#FF3E8E");
        static readonly Color White = Color.white;
        static readonly Color Bay = SpeedDial.Hex("#17B2D0");

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

        // True while the full-screen map is up; the HUD stops taking touches meanwhile.
        public static bool FullOpen { get; private set; }

        public GameController Game;

        Material marks;
        Material mapMaterial;
        MapGeometry geometry;
        Task<MapGeometry> build;
        float buildStarted;
        RenderTexture target;
        CommandBuffer commands;
        Mesh unitQuad;
        Hud hud;
        Font font;
        GUIStyle style;
        float radiusMeters = NearMeters;
        Func<Vector2, bool> dragIgnore;
        Func<Vector2, bool> shoveIgnore;

        float s;
        Vector2 dialCenter;
        float dialRadius;

        float radius;
        float clip;
        Vector2 center;
        Vector2 half;
        Vector2 me;
        Vector2 right;
        Vector2 forward;
        float pxPerMeter;

        Vector2 fullCenter;
        float fullZoom;
        int tapFinger = -1;
        Vector2 tapStart;
        float tapTime;
        bool tapValid;
        long gestureKey = -1;
        Vector2 gestureMid;
        float gestureSpan;
        Vector2 lastMouse;

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
            hud = GetComponent<Hud>();
            var palette = new MapGeometry.Palette
            {
                Land = Hex32("#E6DDCB"),
                Park = Hex32("#48C26A"),
                Path = Hex32("#EEE7DA"),
                Casing = Hex32("#8E929B"),
                Street = Hex32("#A9ADB4"),
                Building = Hex32("#FF9C86"),
            };
            var data = Game.Data;
            buildStarted = Time.realtimeSinceStartup;
            build = Task.Run(() => MapGeometry.Build(data, palette));
        }

        void OnDestroy()
        {
            FullOpen = false;
            if (target != null) target.Release();
        }

        void Update()
        {
            if (build != null && build.IsCompleted)
            {
                if (build.IsFaulted) Debug.LogError("[Minimap] map build failed: " + build.Exception?.GetBaseException());
                else
                {
                    geometry = build.Result;
                    geometry.Upload();
                    Debug.Log($"[Minimap] built {geometry.TriangleCount} map triangles, {geometry.Streets.Count} street labels, {geometry.Places.Count} places in {(Time.realtimeSinceStartup - buildStarted) * 1000f:F0} ms");
                }
                build = null;
            }

            if (Game == null || !Game.IsReady) return;
            Layout();
            HookInput();
            if (FullOpen) FullGestures();
            else WatchDialTap();

            float speed = Mathf.Abs(Game.CurrentCar != null ? Game.CurrentCar.Speed : Game.Player != null ? Game.Player.Speed : 0f);
            float target = Mathf.Lerp(NearMeters, FarMeters, Mathf.Clamp01((speed - 4f) / 24f));
            radiusMeters = Mathf.Lerp(radiusMeters, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.6f));
        }

        // Sized in points from the screen's dpi on a phone (about 11 mm across the radius at 460
        // ppi), never more than 7% over what the safe area gives it, so portrait stays modest.
        void Layout()
        {
            var safe = Screen.safeArea;
            float fit = Mathf.Min(safe.height / 402f, safe.width / 560f);
            s = Application.isMobilePlatform && Screen.dpi > 0f ? Mathf.Min(Screen.dpi / 160f, fit * 1.07f) : fit;
            dialRadius = 70f * s;
            dialCenter = new Vector2(safe.xMin + 22f * s + dialRadius, safe.yMin + 18f * s + dialRadius);
        }

        bool OnDial(Vector2 p)
        {
            float r = dialRadius + 6f * s;
            return Shown && geometry != null && dialRadius > 0f && (p - dialCenter).sqrMagnitude <= r * r;
        }

        // Keeps the full map's touch block chained in front of whatever the race and the operator
        // set on the drag and shove controls (they assign theirs when a race sets up). A tap on the
        // dial must not jump either, so the drag control drops taps there.
        void HookInput()
        {
            var drag = Game.Drag;
            if (drag != null)
            {
                drag.IgnoreTap = OnDial;
                if (drag.IgnoreTouch != dragIgnore)
                {
                    var previous = drag.IgnoreTouch;
                    dragIgnore = p => FullOpen || (previous != null && previous(p));
                    drag.IgnoreTouch = dragIgnore;
                }
            }
            var shove = RaceDirector.Instance != null ? RaceDirector.Instance.Shove : null;
            if (shove != null && shove.IgnoreTouch != shoveIgnore)
            {
                var previous = shove.IgnoreTouch;
                shoveIgnore = p => FullOpen || OnDial(p) || (previous != null && previous(p));
                shove.IgnoreTouch = shoveIgnore;
            }
        }

        bool IsTap(Vector2 p) => (p - tapStart).magnitude < Mathf.Min(Screen.width, Screen.height) * 0.035f && Time.unscaledTime - tapTime < TapSeconds;

        void WatchDialTap()
        {
            if (Input.GetKeyDown(KeyCode.M) && Shown && geometry != null) SetFull(true);
            if (!Shown || geometry == null || (hud != null && hud.Paused))
            {
                tapFinger = -1;
                return;
            }
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began && tapFinger < 0 && OnDial(t.position))
                    {
                        tapFinger = t.fingerId;
                        tapStart = t.position;
                        tapTime = Time.unscaledTime;
                    }
                    else if (t.fingerId == tapFinger && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                    {
                        tapFinger = -1;
                        if (t.phase == TouchPhase.Ended && IsTap(t.position)) SetFull(true);
                    }
                }
                return;
            }
            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && OnDial(mouse))
            {
                tapFinger = 1000;
                tapStart = mouse;
                tapTime = Time.unscaledTime;
            }
            if (tapFinger == 1000 && Input.GetMouseButtonUp(0))
            {
                tapFinger = -1;
                if (IsTap(mouse)) SetFull(true);
            }
        }

        // Opens the full map centred on you, about 700 m across the short side of a phone.
        public void SetFull(bool open)
        {
            if (open == FullOpen) return;
            FullOpen = open;
            tapFinger = -1;
            tapValid = false;
            gestureKey = -1;
            if (!open) return;
            Game.Drag?.Cancel();
            var focus = Game.Focus;
            fullCenter = focus != null ? new Vector2(focus.position.x, focus.position.z) : geometry.Bounds.center;
            fullZoom = Mathf.Clamp(0.6f * s, MinZoom, MaxZoom);
            ClampView();
        }

        Rect MapArea => new Rect(geometry.Bounds.xMin - 60f, geometry.Bounds.yMin - 60f, geometry.Bounds.width + 120f, geometry.Bounds.height + 120f);
        float MinZoom => Mathf.Min(Screen.width / MapArea.width, Screen.height / MapArea.height);
        float MaxZoom => 5f * s;
        Vector2 ScreenMid => new Vector2(Screen.width, Screen.height) * 0.5f;

        void ClampView()
        {
            var area = MapArea;
            var halfView = ScreenMid / fullZoom;
            fullCenter.x = halfView.x * 2f >= area.width ? area.center.x : Mathf.Clamp(fullCenter.x, area.xMin + halfView.x, area.xMax - halfView.x);
            fullCenter.y = halfView.y * 2f >= area.height ? area.center.y : Mathf.Clamp(fullCenter.y, area.yMin + halfView.y, area.yMax - halfView.y);
        }

        void Pan(Vector2 screenDelta)
        {
            fullCenter -= screenDelta / fullZoom;
            ClampView();
        }

        void ZoomAbout(Vector2 screen, float factor)
        {
            var world = fullCenter + (screen - ScreenMid) / fullZoom;
            fullZoom = Mathf.Clamp(fullZoom * factor, MinZoom, MaxZoom);
            fullCenter = world - (screen - ScreenMid) / fullZoom;
            ClampView();
        }

        // One finger pans, two pinch-zoom about their midpoint (and pan with it). A single short
        // touch that barely moves closes the map, wherever it lands (the X included).
        void FullGestures()
        {
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape))
            {
                SetFull(false);
                return;
            }
            if (Input.touchCount > 0)
            {
                Touch a = default, b = default;
                int live = 0;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began)
                    {
                        tapValid = Input.touchCount == 1;
                        tapFinger = t.fingerId;
                        tapStart = t.position;
                        tapTime = Time.unscaledTime;
                    }
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    {
                        if (t.fingerId == tapFinger && t.phase == TouchPhase.Ended && tapValid && IsTap(t.position))
                        {
                            SetFull(false);
                            return;
                        }
                        continue;
                    }
                    if (live == 0) a = t;
                    else if (live == 1) b = t;
                    live++;
                }
                if (live > 1 || (live == 1 && (a.position - tapStart).magnitude >= Mathf.Min(Screen.width, Screen.height) * 0.035f)) tapValid = false;
                if (live == 0)
                {
                    gestureKey = -1;
                    return;
                }
                long key = live == 1 ? a.fingerId : 1000L * (a.fingerId + 1) + b.fingerId;
                var mid = live == 1 ? a.position : (a.position + b.position) * 0.5f;
                float span = live == 1 ? 0f : (a.position - b.position).magnitude;
                if (key == gestureKey)
                {
                    if (live > 1 && gestureSpan > 1f) ZoomAbout(gestureMid, span / gestureSpan);
                    Pan(mid - gestureMid);
                }
                gestureKey = key;
                gestureMid = mid;
                gestureSpan = span;
                return;
            }
            gestureKey = -1;
            Vector2 mouse = Input.mousePosition;
            if (Input.mouseScrollDelta.y != 0f) ZoomAbout(mouse, Mathf.Pow(1.15f, Input.mouseScrollDelta.y));
            if (Input.GetMouseButtonDown(0))
            {
                tapFinger = 1000;
                tapValid = true;
                tapStart = mouse;
                tapTime = Time.unscaledTime;
            }
            else if (tapFinger == 1000 && Input.GetMouseButton(0)) Pan(mouse - lastMouse);
            if (tapFinger == 1000 && Input.GetMouseButtonUp(0))
            {
                tapFinger = -1;
                if (IsTap(mouse))
                {
                    SetFull(false);
                    return;
                }
            }
            lastMouse = mouse;
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

        bool Inside(Vector2 p, float margin)
        {
            if (FullOpen) return Mathf.Abs(p.x - center.x) < half.x - margin && Mathf.Abs(p.y - center.y) < half.y - margin;
            return (p - center).sqrMagnitude < (clip - margin) * (clip - margin);
        }

        void OnGUI()
        {
            GUI.depth = FullOpen ? -20 : 0;
            if (Event.current.type != EventType.Repaint || geometry == null || Game == null || !Game.IsReady || (!Shown && !FullOpen)) return;
            var focus = Game.Focus;
            var cam = Game.View;
            if (focus == null || cam == null) return;
            var player = new Vector2(focus.position.x, focus.position.z);

            if (FullOpen)
            {
                center = ScreenMid;
                half = ScreenMid;
                me = fullCenter;
                forward = Vector2.up;
                right = Vector2.right;
                pxPerMeter = fullZoom;
            }
            else
            {
                center = dialCenter;
                radius = dialRadius;
                clip = radius - 0.5f * s;
                half = new Vector2(radius, radius);
                me = player;
                float yaw = cam.transform.eulerAngles.y * Mathf.Deg2Rad;
                forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
                pxPerMeter = radius / radiusMeters;
            }
            RenderMap();

            GL.PushMatrix();
            GL.LoadPixelMatrix();
            DrawMap();
            GL.PopMatrix();
            if (FullOpen) DrawStreetNames();
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            marks.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            DrawMarks(focus, player);
            GL.End();
            GL.PopMatrix();
            if (FullOpen) DrawFullLabels(player);
            else DrawDistance();
        }

        // Renders the vector map into a 4x MSAA target the size of what it fills on screen (the
        // dial's square, or the whole screen), so every map pixel is one screen pixel.
        void RenderMap()
        {
            int w = Mathf.CeilToInt(half.x * 2f), h = Mathf.CeilToInt(half.y * 2f);
            if (target == null || target.width != w || target.height != h)
            {
                if (target != null)
                {
                    target.Release();
                    Destroy(target);
                }
                target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "MinimapMap", antiAliasing = 4, memorylessMode = RenderTextureMemoryless.MSAA,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
                target.Create();
                mapMaterial.mainTexture = target;
            }
            var worldToPixel = Matrix4x4.identity;
            worldToPixel.m00 = right.x * pxPerMeter;
            worldToPixel.m01 = right.y * pxPerMeter;
            worldToPixel.m03 = w * 0.5f - Vector2.Dot(right, me) * pxPerMeter;
            worldToPixel.m10 = forward.x * pxPerMeter;
            worldToPixel.m11 = forward.y * pxPerMeter;
            worldToPixel.m13 = h * 0.5f - Vector2.Dot(forward, me) * pxPerMeter;
            var reach = new Vector2(w, h) * (FullOpen ? 0.5f : 0.75f) / pxPerMeter;
            var view = new Rect(me - reach, reach * 2f);

            if (commands == null)
            {
                commands = new CommandBuffer { name = "Minimap" };
                unitQuad = new Mesh { name = "MapBay" };
                unitQuad.SetVertices(new[] { Vector3.zero, Vector3.up, new Vector3(1f, 1f, 0f), Vector3.right });
                var bay = (Color32)Bay;
                unitQuad.SetColors(new[] { bay, bay, bay, bay });
                unitQuad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            }
            var previous = RenderTexture.active;
            commands.Clear();
            commands.SetRenderTarget(target);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, w, 0f, h, -1f, 1f));
            commands.DrawMesh(unitQuad, Matrix4x4.Scale(new Vector3(w, h, 1f)), marks, 0, 0);
            geometry.Draw(commands, marks, worldToPixel, view);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = previous;
        }

        void DrawMap()
        {
            mapMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            if (FullOpen)
            {
                GL.Color(White);
                GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(0f, 1f); GL.Vertex3(0f, Screen.height, 0f);
                GL.TexCoord2(1f, 1f); GL.Vertex3(Screen.width, Screen.height, 0f);
                GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(1f, 1f); GL.Vertex3(Screen.width, Screen.height, 0f);
                GL.TexCoord2(1f, 0f); GL.Vertex3(Screen.width, 0f, 0f);
                GL.End();
                return;
            }
            var fill = new Color(1f, 1f, 1f, 0.92f);
            void V(Vector2 offset)
            {
                GL.Color(fill);
                GL.TexCoord2(0.5f + offset.x / (2f * radius), 0.5f + offset.y / (2f * radius));
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

        void DrawMarks(Transform focus, Vector2 player)
        {
            var race = RaceDirector.Instance;
            bool raceUp = race != null && race.Setup;
            var nav = NavService.Instance;

            if (!FullOpen)
            {
                Ring(center, radius + 3.4f * s, 3.9f * s, Ink);
                var north = MapDirection(0f);
                var northRight = new Vector2(north.y, -north.x);
                var tip = center + north * (radius + 6.4f * s);
                var baseCenter = center + north * (radius + 0.6f * s);
                Tri(tip, baseCenter + northRight * 3.6f * s, baseCenter - northRight * 3.6f * s, Sunshine);
            }

            if (nav != null && nav.HasObjective && nav.Path.Count >= 2) Route(nav.Path);
            else if (raceUp && race.Gridded && !race.ResultsIn) Route(race.Course.Route.Points);

            if (raceUp && !race.Gridded)
            {
                var ring = ToMap(race.Course.RingCenter);
                if (Inside(ring, 6f * s)) Ring(ring, Mathf.Max(race.Course.RingRadius * pxPerMeter, 4.5f * s), 1.8f * s, Sunshine);
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
                var color = nav.Kind == NavKind.Mission ? Pink : Sunshine;
                if (!Inside(p, 5f * s)) Pin(p, 4.4f * s, color);
                else if (nav.Kind == NavKind.Waypoint) Flag(p, Sunshine);
                else if (nav.Kind == NavKind.Mission) Ring(p, (5.2f + Mathf.Sin(Time.unscaledTime * 6f) * 0.8f) * s, 1.4f * s, Pink);
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
            var mine = ToMap(player);
            if (!Inside(mine, 4f * s)) Pin(mine, 4.4f * s, Sunshine);
            else
            {
                if (Game.CurrentCar != null) Car(mine, heading, 1f, mySlotColor);
                Arrow(mine, heading, (Game.CurrentCar != null ? 5.6f : 6.6f) * s, Sunshine);
            }
        }

        // Major street names along their streets, upright, ink on a land-coloured halo; a name is
        // skipped where it would crowd one already drawn.
        void DrawStreetNames()
        {
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(8.5f * s);
            var drawn = new List<(Vector2 at, float reach)>();
            var halo = SpeedDial.Hex("#E6DDCB");
            foreach (var label in geometry.Streets)
            {
                var p = ToMap(label.Position);
                if (!Inside(p, 10f * s)) continue;
                var size = style.CalcSize(new GUIContent(label.Text));
                float reach = size.x * 0.5f + 6f * s;
                bool crowded = false;
                foreach (var (at, r) in drawn)
                    if ((at - p).magnitude < reach + r) { crowded = true; break; }
                if (crowded) continue;
                drawn.Add((p, reach));
                var d = label.Direction;
                if (d.x < 0f) d = -d;
                var gui = new Vector2(p.x, Screen.height - p.y);
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg, gui);
                Outlined(new Rect(gui.x - size.x * 0.5f, gui.y - size.y * 0.5f, size.x, size.y), label.Text, 1.1f * s, Ink, halo);
                GUI.matrix = saved;
            }
        }

        // Landmarks, the race start and the players' names as forward-leaning chips, then the X.
        void DrawFullLabels(Vector2 player)
        {
            var race = RaceDirector.Instance;
            bool raceUp = race != null && race.Setup;
            foreach (var place in geometry.Places)
            {
                var p = ToMap(place.Position);
                if (!Inside(p, 4f * s)) continue;
                Gl(() =>
                {
                    Disc(p, 4.2f * s, Ink, 16);
                    Disc(p, 2.8f * s, White, 16);
                });
                Chip(p + new Vector2(0f, 13f * s), place.Text, 9.5f, Ink, White);
            }
            if (raceUp && !race.Gridded)
            {
                var ring = ToMap(race.Course.RingCenter);
                if (Inside(ring, 4f * s)) Chip(ring + new Vector2(0f, Mathf.Max(race.Course.RingRadius * pxPerMeter, 4.5f * s) + 11f * s), "RACE START", 9.5f, Sunshine, Ink);
            }
            if (raceUp && race.Last?.racers != null)
                foreach (var r in race.Last.racers)
                {
                    if (r.left || r.playerId == race.MyId || string.IsNullOrEmpty(r.displayName)) continue;
                    var body = race.BodyOf(r.playerId);
                    var p = ToMap(body != null ? body.position : new Vector3(r.position.x, r.position.y, r.position.z));
                    if (Inside(p, 5f * s)) Chip(p + new Vector2(0f, 14f * s), r.displayName.ToUpperInvariant(), 9f, SlipstreamHud.SlotColor(r.color), White);
                }
            var mine = ToMap(player);
            if (Inside(mine, 4f * s)) Chip(mine + new Vector2(0f, 15f * s), "YOU", 9f, Sunshine, Ink);
            DrawClose();
        }

        // The close X in the top-left of the safe area, an ink parallelogram, with the title.
        void DrawClose()
        {
            var safe = Screen.safeArea;
            float size = 17f * s;
            var c = new Vector2(safe.xMin + 16f * s + size, safe.yMax - 14f * s - size);
            Gl(() =>
            {
                Parallelogram(c, size, size, 3f * s, Ink);
                float arm = size * 0.5f, thick = 1.9f * s;
                var d1 = new Vector2(1f, 1f).normalized;
                var d2 = new Vector2(1f, -1f).normalized;
                Quad(c - d1 * arm + d2 * thick, c + d1 * arm + d2 * thick, c + d1 * arm - d2 * thick, c - d1 * arm - d2 * thick, Sunshine);
                Quad(c - d2 * arm - d1 * thick, c + d2 * arm - d1 * thick, c + d2 * arm + d1 * thick, c - d2 * arm + d1 * thick, Sunshine);
            });
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(15f * s);
            var title = style.CalcSize(new GUIContent("DOGPATCH"));
            Chip(new Vector2(c.x + size + 10f * s + title.x * 0.5f + 8f * s, c.y), "DOGPATCH", 15f, Ink, White);
        }

        // A forward-leaning panel with text centred on p (GL pixels, origin bottom left).
        void Chip(Vector2 p, string text, float fontSize, Color fill, Color ink)
        {
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(fontSize * s);
            var size = style.CalcSize(new GUIContent(text));
            float w = size.x * 0.5f + 5f * s, h = size.y * 0.5f + 0.5f * s;
            Gl(() =>
            {
                Parallelogram(p, w + 1.2f * s, h + 1.2f * s, h * 0.35f, Ink);
                Parallelogram(p, w, h, h * 0.35f, fill);
            });
            style.normal.textColor = TextColor(ink);
            GUI.Label(new Rect(p.x - size.x * 0.5f, Screen.height - p.y - size.y * 0.5f, size.x, size.y), text, style);
        }

        void Gl(Action draw)
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            marks.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            draw();
            GL.End();
            GL.PopMatrix();
        }

        void EnsureStyle()
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { font = font, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(), alignment = TextAnchor.LowerLeft };
        }

        // Ink underlay then a sunshine line, clipped to the circle, with round joints.
        void Route(IReadOnlyList<Vector2> points)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float width = (pass == 0 ? 2.5f : 1.4f) * s;
                var color = pass == 0 ? Ink : Sunshine;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var a = ToMap(points[i]);
                    var b = ToMap(points[i + 1]);
                    if (!FullOpen && !ClipToCircle(ref a, ref b, center, clip - width)) continue;
                    var d = b - a;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    var n = new Vector2(-d.y, d.x).normalized * width;
                    Quad(a + n, b + n, b - n, a - n, color);
                    Disc(b, width, color, 8);
                    if (i == 0) Disc(a, width, color, 8);
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

        // A small chevron on the rim (or the screen edge, on the full map) pointing toward
        // something outside it.
        void Pin(Vector2 p, float size, Color color)
        {
            var offset = p - center;
            var dir = offset.normalized;
            if (!FullOpen)
            {
                Arrow(center + dir * (clip - size * 0.9f), dir, size, color);
                return;
            }
            float edge = size * 1.6f;
            float t = Mathf.Min((half.x - edge) / Mathf.Max(Mathf.Abs(offset.x), 1e-3f), (half.y - edge) / Mathf.Max(Mathf.Abs(offset.y), 1e-3f));
            Arrow(center + offset * Mathf.Min(t, 1f), dir, size, color);
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
            EnsureStyle();
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

        void Outlined(Rect r, string text, float outline) => Outlined(r, text, outline, White, Ink);

        void Outlined(Rect r, string text, float outline, Color fill, Color edge)
        {
            style.normal.textColor = TextColor(edge);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), text, style);
            }
            style.normal.textColor = TextColor(fill);
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
