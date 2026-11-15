using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Nav;
using Dogpatch.Race;
using Dogpatch.Slipstream;
using UnityEngine;

namespace Dogpatch.Map
{
    // The expanded map, in three styles to compare live (pause menu, MAP STYLE; default A):
    //   A. a big circle, 85% of the short side, over the dimmed game, with a thick ink ring and a
    //      compass N, markers outside the view pinned to the rim;
    //   B. a full-screen GTA-style pause map with a legend strip;
    //   C. a floating rounded-square card, tilted a few degrees over a drop shadow.
    // All three: one finger pans in every direction with inertia, a pinch zooms 0.5x to 4x about
    // the pinch, a double tap zooms in, the picked button recentres on you, street names fade in as
    // you zoom in, a long press drops a waypoint pin, and a mission marker offers TRAVEL. A tap
    // outside the map (or on the X) closes it.
    public sealed partial class Minimap
    {
        const string StylePrefKey = "dogpatch.mapstyle";
        public static readonly string[] StyleNames = { "A", "B", "C" };

        static int? mapStyle;

        public static int MapStyle
        {
            get => mapStyle ??= Mathf.Clamp(PlayerPrefs.GetInt(StylePrefKey, 1), 0, StyleNames.Length - 1);
            set
            {
                mapStyle = Mathf.Clamp(value, 0, StyleNames.Length - 1);
                PlayerPrefs.SetInt(StylePrefKey, mapStyle.Value);
                PlayerPrefs.Save();
            }
        }

        Vector2 velocity;
        float lastTapAt = -1f;
        Vector2 lastTapPos;
        float zoomGoal;
        Vector2 zoomPivot;
        bool recentring;
        MapTarget picked;
        float selectedAt;
        Rect cardRect;
        Rect cardButton;

        Vector2 ScreenMid => new Vector2(Screen.width, Screen.height) * 0.5f;
        Rect MapArea => new Rect(geometry.Bounds.xMin - 60f, geometry.Bounds.yMin - 60f, geometry.Bounds.width + 120f, geometry.Bounds.height + 120f);
        float BaseZoom => Mathf.Min(half.x, half.y) * 2f / 650f;
        float MinZoom => BaseZoom * 0.5f;
        float MaxZoom => BaseZoom * 4f;
        float ButtonR => 22f * s;
        float LegendH => 40f * s;

        // Lays out the open map for the current style; north up, centred on fullCenter.
        void FrameFull()
        {
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            round = false;
            corner = 0f;
            tilt = 0f;
            center = ScreenMid;
            switch (MapStyle)
            {
                case 0:
                    round = true;
                    radius = shortSide * 0.48f;
                    clip = radius - 1f * s;
                    half = new Vector2(radius, radius);
                    break;
                case 1:
                    half = ScreenMid;
                    break;
                default:
                    half = new Vector2(Screen.safeArea.width * 0.45f, Screen.safeArea.height * 0.44f);
                    corner = 18f * s;
                    tilt = -4f * Mathf.Deg2Rad;
                    break;
            }
            me = fullCenter;
            right = Vector2.right;
            forward = Vector2.up;
            pxPerMeter = fullZoom;
        }

        // Opens the map centred on you, about 650 m across the view's short side.
        public void SetFull(bool open)
        {
            if (open == FullOpen) return;
            FullOpen = open;
            tapFinger = -1;
            tapValid = false;
            gestureKey = -1;
            picked = default;
            velocity = Vector2.zero;
            zoomGoal = 0f;
            recentring = false;
            if (!open) return;
            Game.Drag?.Cancel();
            var focus = Game.Focus;
            fullCenter = focus != null ? new Vector2(focus.position.x, focus.position.z) : geometry.Bounds.center;
            FrameFull();
            fullZoom = BaseZoom;
            ClampView();
        }

        // For captures: select the mission and zoom by factor about the centre.
        public void Preview(float zoom, bool selectMission = true)
        {
            FrameFull();
            var race = RaceDirector.Instance;
            if (selectMission && race != null && race.Setup) Pick(CourseRing(race));
            ZoomAbout(center, zoom);
        }

        void ClampView()
        {
            var area = MapArea;
            fullCenter = new Vector2(Mathf.Clamp(fullCenter.x, area.xMin, area.xMax), Mathf.Clamp(fullCenter.y, area.yMin, area.yMax));
        }

        Vector2 ScreenToWorld(Vector2 p) => fullCenter + ToLocal(p) / fullZoom;

        void Pan(Vector2 screenDelta)
        {
            fullCenter -= Rotate(screenDelta, -tilt) / fullZoom;
            ClampView();
        }

        void ZoomAbout(Vector2 screen, float factor)
        {
            var world = ScreenToWorld(screen);
            fullZoom = Mathf.Clamp(fullZoom * factor, MinZoom, MaxZoom);
            fullCenter = world - ToLocal(screen) / fullZoom;
            ClampView();
            me = fullCenter;
            pxPerMeter = fullZoom;
        }

        Vector2 CloseAt
        {
            get
            {
                var safe = Screen.safeArea;
                if (MapStyle == 1) return new Vector2(safe.xMin + 16f * s + ButtonR, safe.yMax - 14f * s - ButtonR);
                if (round) return center + new Vector2(1f, 1f).normalized * (radius + 2f * s);
                return ToScreen(half - Vector2.one * 4f * s);
            }
        }

        Vector2 RecentreAt
        {
            get
            {
                var safe = Screen.safeArea;
                if (MapStyle == 1) return new Vector2(safe.xMax - 20f * s - ButtonR, safe.yMin + LegendH + 24f * s + ButtonR);
                if (round) return center + new Vector2(1f, -1f).normalized * (radius + 2f * s);
                return ToScreen(new Vector2(half.x - 4f * s, -half.y + 4f * s));
            }
        }

        void FullGestures()
        {
            FrameFull();
            float dt = Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape))
            {
                SetFull(false);
                return;
            }
            bool touching = Input.touchCount > 0 || Input.GetMouseButton(0);
            if (!touching && velocity.sqrMagnitude > 25f)
            {
                Pan(velocity * dt);
                velocity *= Mathf.Exp(-dt * 4.5f);
            }
            else if (!touching) velocity = Vector2.zero;
            if (zoomGoal > 0f)
            {
                float z = Mathf.Lerp(fullZoom, zoomGoal, 1f - Mathf.Exp(-dt * 14f));
                ZoomAbout(zoomPivot, z / fullZoom);
                if (Mathf.Abs(fullZoom - zoomGoal) < zoomGoal * 0.01f) zoomGoal = 0f;
            }
            if (recentring && Game.Focus != null)
            {
                var goal = new Vector2(Game.Focus.position.x, Game.Focus.position.z);
                fullCenter = Vector2.Lerp(fullCenter, goal, 1f - Mathf.Exp(-dt * 10f));
                ClampView();
                if ((fullCenter - goal).magnitude < 1f) recentring = false;
            }
            me = fullCenter;
            pxPerMeter = fullZoom;
            if (Input.touchCount > 0) Touches(dt);
            else
            {
                gestureKey = -1;
                MouseGestures(dt);
            }
        }

        void StopMotion()
        {
            velocity = Vector2.zero;
            zoomGoal = 0f;
            recentring = false;
        }

        void Touches(float dt)
        {
            Touch a = default, b = default;
            int live = 0;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    StopMotion();
                    tapValid = Input.touchCount == 1;
                    tapFinger = t.fingerId;
                    tapStart = t.position;
                    tapTime = Time.unscaledTime;
                }
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (t.fingerId == tapFinger && t.phase == TouchPhase.Ended && tapValid && IsTap(t.position))
                    {
                        FullTap(t.position);
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
                var delta = mid - gestureMid;
                Pan(delta);
                velocity = live == 1 ? Vector2.Lerp(velocity, delta / Mathf.Max(dt, 1e-3f), 0.4f) : Vector2.zero;
            }
            gestureKey = key;
            gestureMid = mid;
            gestureSpan = span;
        }

        void MouseGestures(float dt)
        {
            Vector2 mouse = Input.mousePosition;
            if (Input.mouseScrollDelta.y != 0f) ZoomAbout(mouse, Mathf.Pow(1.15f, Input.mouseScrollDelta.y));
            if (Input.GetMouseButtonDown(0))
            {
                StopMotion();
                tapFinger = 1000;
                tapValid = true;
                tapStart = mouse;
                tapTime = Time.unscaledTime;
            }
            else if (tapFinger == 1000 && Input.GetMouseButton(0))
            {
                var delta = mouse - lastMouse;
                Pan(delta);
                velocity = Vector2.Lerp(velocity, delta / Mathf.Max(dt, 1e-3f), 0.4f);
            }
            if (tapFinger == 1000 && Input.GetMouseButtonUp(0))
            {
                tapFinger = -1;
                if (IsTap(mouse))
                {
                    velocity = Vector2.zero;
                    FullTap(mouse);
                }
            }
            lastMouse = mouse;
        }

        // The card first (its button acts, the rest of it absorbs), then the X and recentre buttons,
        // then outside the view (drops the card, or closes the map), a double tap (zooms in), a
        // pin (a cluster zooms in until it splits, a single pin is picked), and anywhere else on
        // the map drops a travel pin there. Every pick gets the card with FAST TRAVEL.
        void FullTap(Vector2 p)
        {
            bool card = picked.Kind != TargetKind.None;
            if (card && cardButton.Contains(p))
            {
                if (TravelReady(out _)) StartTravel(picked);
                return;
            }
            if (card && cardRect.Contains(p)) return;
            if ((p - CloseAt).magnitude < ButtonR * 1.4f)
            {
                SetFull(false);
                return;
            }
            if (!card && (p - RecentreAt).magnitude < ButtonR * 1.4f)
            {
                recentring = true;
                return;
            }
            if (!Inside(p, 0f))
            {
                if (card) picked = default;
                else SetFull(false);
                return;
            }
            bool doubleTap = Time.unscaledTime - lastTapAt < 0.32f && (p - lastTapPos).magnitude < 40f * s;
            lastTapAt = doubleTap ? -1f : Time.unscaledTime;
            lastTapPos = p;
            if (doubleTap)
            {
                zoomPivot = p;
                zoomGoal = Mathf.Min(fullZoom * 2f, MaxZoom);
                return;
            }
            foreach (var c in Clusters())
            {
                if ((c.At - p).magnitude > 26f * s) continue;
                if (c.Count > 1 && fullZoom < MaxZoom * 0.95f)
                {
                    zoomPivot = c.At;
                    zoomGoal = Mathf.Min(fullZoom * 2.2f, MaxZoom);
                    return;
                }
                Pick(c.Pin);
                return;
            }
            Pick(new MapTarget { Kind = TargetKind.Spot, Name = "DROPPED PIN", World = ScreenToWorld(p) });
        }

        void Pick(MapTarget t)
        {
            picked = t;
            selectedAt = Time.unscaledTime;
        }

        struct PinCluster
        {
            public Vector2 At;
            public MapTarget Pin;
            public int Count;
        }

        // Every route's start ring, then the landmarks.
        List<MapTarget> Pins()
        {
            var pins = new List<MapTarget>();
            var race = RaceDirector.Instance;
            if (race != null && race.Setup)
                foreach (var (name, at) in race.RouteRingSpots()) pins.Add(new MapTarget { Kind = TargetKind.Ring, Name = name, World = new Vector2(at.x, at.z) });
            foreach (var place in geometry.Places) pins.Add(new MapTarget { Kind = TargetKind.Place, Name = place.Text, World = place.Position });
            return pins;
        }

        // Pins in view, merged where they would overlap on screen; the first in (a ring) is the
        // cluster's face. The picked pin stays out.
        List<PinCluster> Clusters()
        {
            var list = new List<PinCluster>();
            foreach (var pin in Pins())
            {
                var p = ToMap(pin.World);
                if (!Inside(p, 6f * s) || (picked.Kind == pin.Kind && (picked.World - pin.World).sqrMagnitude < 1f)) continue;
                int hit = list.FindIndex(c => (c.At - p).magnitude < 44f * s);
                if (hit < 0)
                {
                    list.Add(new PinCluster { At = p, Pin = pin, Count = 1 });
                    continue;
                }
                var cluster = list[hit];
                cluster.Count++;
                list[hit] = cluster;
            }
            return list;
        }

        // Where the jump lands: the race course's own ring uses #83's staggered spawn spot; any
        // other ring the nearest road point about 34 m out on your side, facing the ring; a
        // landmark or a dropped pin the nearest road point, facing along the road. Never water or
        // a building, because it is always on a road.
        bool Landing(MapTarget t, out Vector3 at, out float facing)
        {
            at = default;
            facing = 0f;
            var race = RaceDirector.Instance;
            if (race == null || !race.Setup || Game.Focus == null) return false;
            var course = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z);
            if (t.Kind == TargetKind.Ring && (t.World - course).sqrMagnitude < 1f)
            {
                at = race.SpawnSpot(out facing);
                return true;
            }
            var me = new Vector2(Game.Focus.position.x, Game.Focus.position.z);
            var aim = t.World;
            if (t.Kind == TargetKind.Ring)
            {
                var away = me - t.World;
                aim += (away.sqrMagnitude > 1f ? away.normalized : Vector2.down) * 34f;
            }
            if (!Game.World.Roads.Nearest(aim, 90f, out var hit)) return false;
            var dir = t.Kind == TargetKind.Ring ? t.World - hit.Point : hit.Direction;
            facing = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            at = new Vector3(hit.Point.x, race.Course.GroundY(hit.Point) + 0.1f, hit.Point.y);
            return true;
        }

        // Whether FAST TRAVEL works now, and what the button says when it does not.
        bool TravelReady(out string blocked)
        {
            var race = RaceDirector.Instance;
            blocked = race != null && (race.Started || race.Gridded) ? "IN A RACE" : "IN A LOBBY";
            if (!CanTravel(race)) return false;
            blocked = "NO ROAD HERE";
            return Landing(picked, out _, out _);
        }

        // Dims the game for A and C, the card's drop shadow for C, and the ink frame.
        void DrawBackdrop()
        {
            float w = Screen.width, h = Screen.height;
            Gl(() =>
            {
                if (MapStyle != 1) Quad(Vector2.zero, new Vector2(0f, h), new Vector2(w, h), new Vector2(w, 0f), new Color(Ink.r, Ink.g, Ink.b, 0.55f));
                if (MapStyle == 2) Shape(Outline(6f * s), new Vector2(10f * s, -14f * s), new Color(0f, 0f, 0f, 0.4f));
                if (MapStyle == 0) Shape(Outline(9f * s), Vector2.zero, Ink);
                if (MapStyle == 2) Shape(Outline(6f * s), Vector2.zero, Ink);
            });
        }

        // Major street names, upright along their streets, fading in from 1.15x to 1.6x zoom;
        // a name is skipped where it would crowd one already drawn.
        void DrawStreetNames()
        {
            float alpha = Mathf.InverseLerp(1.15f, 1.6f, fullZoom / BaseZoom);
            if (alpha <= 0f) return;
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(11f * s);
            var drawn = new List<(Vector2 at, float reach)>();
            var halo = MapLook.Current.LabelHalo;
            halo.a = alpha;
            var ink = MapLook.Current.LabelInk;
            ink.a = alpha;
            foreach (var label in geometry.Streets)
            {
                var p = ToMap(label.Position);
                if (!Inside(p, 24f * s)) continue;
                var size = style.CalcSize(new GUIContent(label.Text));
                float reach = size.x * 0.5f + 6f * s;
                bool crowded = false;
                foreach (var (at, r) in drawn)
                    if ((at - p).magnitude < reach + r)
                    {
                        crowded = true;
                        break;
                    }
                if (crowded) continue;
                drawn.Add((p, reach));
                var d = Rotate(label.Direction, tilt);
                if (d.x < 0f) d = -d;
                var gui = new Vector2(p.x, Screen.height - p.y);
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg, gui);
                Outlined(new Rect(gui.x - size.x * 0.5f, gui.y - size.y * 0.5f, size.x, size.y), label.Text, 1.1f * s, ink, halo);
                GUI.matrix = saved;
            }
        }

        // Players as coloured dots with an initial, the pins (route rings always named, landmarks
        // once zoomed in), then, with a pick, everything else dimmed under it and its card.
        void DrawFullLabels(Vector2 player)
        {
            var race = RaceDirector.Instance;
            if (race != null && race.Setup && race.Last?.racers != null)
                foreach (var r in race.Last.racers)
                {
                    if (r.left || r.playerId == race.MyId) continue;
                    var body = race.BodyOf(r.playerId);
                    var p = ToMap(body != null ? body.position : new Vector3(r.position.x, r.position.y, r.position.z));
                    if (!Inside(p, 6f * s)) continue;
                    var color = SlipstreamHud.SlotColor(r.color);
                    Gl(() =>
                    {
                        Disc(p, 9.5f * s, Ink, 24);
                        Disc(p, 7.6f * s, color, 24);
                    });
                    if (!string.IsNullOrEmpty(r.displayName)) Label(p, r.displayName.Substring(0, 1).ToUpperInvariant(), 10.5f, White);
                }
            bool named = fullZoom / BaseZoom >= 1.45f;
            foreach (var c in Clusters()) DrawPin(c.Pin, c.At, c.Count, named, false);
            if (picked.Kind != TargetKind.None)
            {
                Gl(() => Shape(Outline(0f), Vector2.zero, new Color(Ink.r, Ink.g, Ink.b, 0.38f)));
                DrawPin(picked, ToMap(picked.World), 1, true, true);
                DrawCard(player);
            }
            DrawChrome();
        }

        // A route ring is a forward-leaning sunshine badge with a checkered flag on a pointer,
        // always named; a landmark an ink and white dot; a dropped pin a sunshine teardrop. A
        // cluster carries a pink count. The picked pin is bigger, outlined in white and pulses.
        void DrawPin(MapTarget pin, Vector2 p, int count, bool named, bool selected)
        {
            float t = Time.unscaledTime - selectedAt;
            float k = selected ? 1.3f + 0.05f * Mathf.Sin(t * 6f) : 1f;
            float u = s * k;
            var face = pin.Kind == TargetKind.Ring ? p + new Vector2(0f, 26f * u) : pin.Kind == TargetKind.Spot ? p + new Vector2(0f, 22f * u) : p;
            var badge = face + new Vector2(17f, 14f) * u;
            Gl(() =>
            {
                if (selected)
                {
                    float pulse = Mathf.Repeat(t * 1.1f, 1f);
                    Ring(p, (14f + 22f * pulse) * s, 2.6f * s, new Color(Sunshine.r, Sunshine.g, Sunshine.b, 1f - pulse), 40);
                }
                switch (pin.Kind)
                {
                    case TargetKind.Ring:
                        Tri(p, face + new Vector2(-8f * u, -10f * u), face + new Vector2(8f * u, -10f * u), Ink);
                        if (selected) Parallelogram(face, 23f * u, 18f * u, 5f * u, White);
                        Parallelogram(face, 20f * u, 15.5f * u, 4.5f * u, Ink);
                        Parallelogram(face, 16.5f * u, 12f * u, 4f * u, MapLook.Current.Mission);
                        float q = 4.2f * u;
                        for (int x = -2; x < 2; x++)
                            for (int y = -1; y < 1; y++)
                            {
                                var o = face + new Vector2(x * q + 1.2f * u, y * q);
                                Quad(o, o + new Vector2(0f, q), o + new Vector2(q, q), o + new Vector2(q, 0f), ((x + y) & 1) == 0 ? Ink : White);
                            }
                        Disc(p, 3f * s, Ink, 12);
                        break;
                    case TargetKind.Spot:
                        Tri(p, face + new Vector2(-10f * u, -6f * u), face + new Vector2(10f * u, -6f * u), Ink);
                        Disc(face, 13f * u, Ink, 28);
                        Disc(face, 10f * u, Sunshine, 28);
                        Disc(face, 4f * u, Ink, 16);
                        break;
                    default:
                        if (MapLook.Current.PopPins)
                        {
                            Quad(p + new Vector2(-1.4f * s, -12f * s), p + new Vector2(-1.4f * s, 0f), p + new Vector2(1.4f * s, 0f), p + new Vector2(1.4f * s, -12f * s), Ink);
                            Disc(p + new Vector2(0f, -12f * s), 2.6f * s, Ink, 10);
                        }
                        if (selected) Disc(p, 12.5f * u, White, 28);
                        Disc(p, 10f * u, Ink, 24);
                        Disc(p, 6.6f * u, White, 24);
                        Disc(p, 3.2f * u, Ink, 12);
                        break;
                }
                if (count > 1)
                {
                    Disc(badge, 9f * s, Ink, 20);
                    Disc(badge, 7.4f * s, Pink, 20);
                }
            });
            if (count > 1) Label(badge, count.ToString(), 10.5f, White);
            var below = new Vector2(0f, -15f * s);
            if (pin.Kind == TargetKind.Ring) Chip(face + new Vector2(0f, 30f * u), pin.Name, selected ? 15f : 12f, Ink, White);
            else if (pin.Kind == TargetKind.Place && (selected || (count == 1 && named))) Chip(p + new Vector2(0f, 22f * u), pin.Name, selected ? 15f : 12f, Ink, White);
        }

        // Docked to the bottom edge (bottom right in landscape): what it is, the route, how far,
        // who is waiting there, and one big FAST TRAVEL button.
        void DrawCard(Vector2 player)
        {
            var race = RaceDirector.Instance;
            var safe = Screen.safeArea;
            bool landscape = Screen.width > Screen.height;
            float w = landscape ? Mathf.Min(430f * s, safe.width * 0.46f) : safe.width - 32f * s;
            float h = 168f * s;
            float x0 = landscape ? safe.xMax - 20f * s - w : safe.xMin + 16f * s;
            float y0 = safe.yMin + 16f * s;
            cardRect = new Rect(x0, y0, w, h);
            float meters = Vector2.Distance(player, picked.World);
            string sub = "NEAREST ROAD TO THE PIN";
            if (picked.Kind == TargetKind.Place) sub = "LANDMARK";
            if (picked.Kind == TargetKind.Ring)
            {
                var course = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z);
                sub = (picked.World - course).sqrMagnitude < 1f ? $"{race.Course.Data.startName} TO {race.Course.Data.finishName}".ToUpperInvariant() : "SPEED DIAL RACE START";
            }
            string info = meters < 1000f ? $"{Mathf.Max(10, Mathf.RoundToInt(meters / 10f) * 10)} M AWAY" : $"{meters / 1000f:F1} KM AWAY";
            if (picked.Kind == TargetKind.Ring)
            {
                int waiting = 0;
                if (race.Last?.racers != null)
                    foreach (var r in race.Last.racers)
                    {
                        if (r.left || r.playerId == race.MyId) continue;
                        var body = race.BodyOf(r.playerId);
                        var pos = body != null ? body.position : new Vector3(r.position.x, r.position.y, r.position.z);
                        if (Vector2.Distance(new Vector2(pos.x, pos.z), picked.World) < race.Course.RingRadius + 4f) waiting++;
                    }
                info += waiting == 1 ? "    1 PLAYER WAITING" : $"    {waiting} PLAYERS WAITING";
            }
            bool can = TravelReady(out string blocked);
            var c = cardRect.center;
            var button = new Rect(x0 + 18f * s, y0 + 14f * s, w - 36f * s, 52f * s);
            cardButton = new Rect(button.x - 8f * s, button.y - 8f * s, button.width + 16f * s, button.height + 16f * s);
            Gl(() =>
            {
                Parallelogram(c + new Vector2(7f * s, -8f * s), w * 0.5f, h * 0.5f, 9f * s, new Color(0f, 0f, 0f, 0.35f));
                Parallelogram(c, w * 0.5f, h * 0.5f, 9f * s, White);
                Parallelogram(c, w * 0.5f - 3f * s, h * 0.5f - 3f * s, 9f * s, Ink);
                Parallelogram(button.center, button.width * 0.5f, button.height * 0.5f, 8f * s, can ? Sunshine : new Color(0.55f, 0.57f, 0.65f, 1f));
                for (int i = 0; i < 3; i++)
                {
                    float y = y0 + h - (30f + i * 12f) * s;
                    Quad(new Vector2(x0 - (34f + i * 10f) * s, y - 2.5f * s), new Vector2(x0 - (34f + i * 10f) * s, y + 2.5f * s), new Vector2(x0 - 6f * s, y + 2.5f * s), new Vector2(x0 - 6f * s, y - 2.5f * s), i == 1 ? Pink : Sunshine);
                }
            });
            TextAt(new Vector2(x0 + 24f * s, y0 + h - 14f * s), picked.Name, 24f, White);
            TextAt(new Vector2(x0 + 24f * s, y0 + h - 46f * s), sub, 12.5f, Sunshine);
            TextAt(new Vector2(x0 + 24f * s, y0 + h - 68f * s), info, 12.5f, White);
            Label(button.center, can ? "FAST TRAVEL" : blocked, 22f, Ink);
        }

        // Left-aligned text with its top-left corner at p (GL pixels).
        void TextAt(Vector2 p, string text, float fontSize, Color color)
        {
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(fontSize * s);
            var size = style.CalcSize(new GUIContent(text));
            style.normal.textColor = TextColor(color);
            GUI.Label(new Rect(p.x, Screen.height - p.y, size.x, size.y), text, style);
        }

        void DrawChrome()
        {
            if (MapStyle == 0)
            {
                var n = center + new Vector2(0f, radius + 4.5f * s);
                Gl(() =>
                {
                    Tri(n + new Vector2(0f, 19f * s), n + new Vector2(-7f * s, 9f * s), n + new Vector2(7f * s, 9f * s), Sunshine);
                    Disc(n, 11f * s, Ink, 24);
                });
                Label(n, "N", 13f, White);
            }
            Button(CloseAt, true);
            if (picked.Kind == TargetKind.None) Button(RecentreAt, false);
            if (MapStyle == 1)
            {
                var c = CloseAt;
                EnsureStyle();
                style.fontSize = Mathf.RoundToInt(20f * s);
                var title = style.CalcSize(new GUIContent("DOGPATCH"));
                Chip(new Vector2(c.x + ButtonR + 16f * s + title.x * 0.5f, c.y), "DOGPATCH", 20f, Ink, White);
                if (picked.Kind == TargetKind.None) DrawLegend();
            }
            if (MapStyle == 2) Chip(ToScreen(new Vector2(-half.x + 80f * s, half.y + 2f * s)), "DOGPATCH", 17f, Ink, White);
        }

        void Button(Vector2 c, bool close)
        {
            float r = ButtonR;
            Gl(() =>
            {
                Disc(c, r + 2f * s, White, 28);
                Disc(c, r, Ink, 28);
                if (close)
                {
                    float arm = r * 0.5f, thick = 1.8f * s;
                    var d1 = new Vector2(1f, 1f).normalized;
                    var d2 = new Vector2(1f, -1f).normalized;
                    Quad(c - d1 * arm + d2 * thick, c + d1 * arm + d2 * thick, c + d1 * arm - d2 * thick, c - d1 * arm - d2 * thick, Sunshine);
                    Quad(c - d2 * arm - d1 * thick, c + d2 * arm - d1 * thick, c + d2 * arm + d1 * thick, c - d2 * arm + d1 * thick, Sunshine);
                    return;
                }
                Ring(c, r * 0.55f, 2f * s, Sunshine, 28);
                Disc(c, 2.4f * s, Sunshine, 12);
                foreach (var d in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                    Quad(c + d * r * 0.4f + new Vector2(d.y, -d.x) * 1f * s, c + d * r * 0.8f + new Vector2(d.y, -d.x) * 1f * s, c + d * r * 0.8f - new Vector2(d.y, -d.x) * 1f * s, c + d * r * 0.4f - new Vector2(d.y, -d.x) * 1f * s, Sunshine);
            });
        }

        // Centred text at p (GL pixels).
        void Label(Vector2 p, string text, float fontSize, Color color)
        {
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(fontSize * s);
            var size = style.CalcSize(new GUIContent(text));
            style.normal.textColor = TextColor(color);
            GUI.Label(new Rect(p.x - size.x * 0.5f, Screen.height - p.y - size.y * 0.5f, size.x, size.y), text, style);
        }

        // B's key: what each mark means, and that a tap travels.
        void DrawLegend()
        {
            var safe = Screen.safeArea;
            float y = safe.yMin + 12f * s + LegendH * 0.5f, icon = 20f * s, gap = 18f * s;
            string[] names = { "YOU", "RACERS", "RACE START", "FINISH", "PHONE", "TAP TO TRAVEL" };
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(13f * s);
            float total = 0f;
            foreach (var n in names) total += icon + 4f * s + style.CalcSize(new GUIContent(n)).x + gap;
            total -= gap;
            float x = Screen.width * 0.5f - total * 0.5f;
            Gl(() => Parallelogram(new Vector2(Screen.width * 0.5f, y), total * 0.5f + 16f * s, LegendH * 0.5f, 5f * s, Ink));
            for (int i = 0; i < names.Length; i++)
            {
                var at = new Vector2(x + icon * 0.5f, y);
                int kind = i;
                Gl(() =>
                {
                    switch (kind)
                    {
                        case 0: Arrow(at, Vector2.up, 5.5f * s, Sunshine); break;
                        case 1: Parallelogram(at, 4.2f * s, 4.2f * s, 1.4f * s, SpeedDial.Hex(RaceDirector.SlotHex[1])); break;
                        case 2: Ring(at, 5.5f * s, 1.7f * s, Sunshine, 24); break;
                        case 3: Flag(at - new Vector2(4f * s, 6.5f * s)); break;
                        case 4: Phone(at, 7f * s); break;
                        default: Flag(at - new Vector2(4f * s, 6.5f * s), Sunshine); break;
                    }
                });
                style.fontSize = Mathf.RoundToInt(13f * s);
                var size = style.CalcSize(new GUIContent(names[i]));
                style.normal.textColor = TextColor(White);
                GUI.Label(new Rect(x + icon + 4f * s, Screen.height - y - size.y * 0.5f, size.x, size.y), names[i], style);
                x += icon + 4f * s + size.x + gap;
            }
        }

        MapTarget CourseRing(RaceDirector race) => new MapTarget
        {
            Kind = TargetKind.Ring,
            Name = race.RouteName(race.RouteId),
            World = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z),
        };
    }

    public enum TargetKind { None, Ring, Place, Spot }

    // Something on the map you can fast travel to: a route's start ring, a landmark, or a dropped pin.
    public struct MapTarget
    {
        public TargetKind Kind;
        public string Name;
        public Vector2 World;
    }
}
