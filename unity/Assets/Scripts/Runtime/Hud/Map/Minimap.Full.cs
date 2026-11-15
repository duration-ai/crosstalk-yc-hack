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
    // the pinch, a double tap zooms in, the target button recentres on you, street names fade in as
    // you zoom in, a long press drops a waypoint pin, and a mission marker offers TRAVEL. A tap
    // outside the map (or on the X) closes it.
    public sealed partial class Minimap
    {
        const string StylePrefKey = "dogpatch.mapstyle";
        public static readonly string[] StyleNames = { "A", "B", "C" };

        static int? mapStyle;

        public static int MapStyle
        {
            get => mapStyle ??= Mathf.Clamp(PlayerPrefs.GetInt(StylePrefKey, 0), 0, StyleNames.Length - 1);
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
        bool longPressed;
        int selectedKind = -1;
        int selectedPlace;
        float selectedAt;
        Rect cardRect;
        Rect cardButton;

        Vector2 ScreenMid => new Vector2(Screen.width, Screen.height) * 0.5f;
        Rect MapArea => new Rect(geometry.Bounds.xMin - 60f, geometry.Bounds.yMin - 60f, geometry.Bounds.width + 120f, geometry.Bounds.height + 120f);
        float BaseZoom => Mathf.Min(half.x, half.y) * 2f / 650f;
        float MinZoom => BaseZoom * 0.5f;
        float MaxZoom => BaseZoom * 4f;
        float ButtonR => 16f * s;
        float LegendH => 30f * s;

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
                    radius = shortSide * 0.425f;
                    clip = radius - 1f * s;
                    half = new Vector2(radius, radius);
                    break;
                case 1:
                    half = ScreenMid;
                    break;
                default:
                    half = Vector2.one * shortSide * 0.4f;
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
            selectedKind = -1;
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
        public void Preview(float zoom)
        {
            FrameFull();
            if (MissionUp(RaceDirector.Instance)) Select(-1);
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
                    longPressed = false;
                }
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (t.fingerId == tapFinger && t.phase == TouchPhase.Ended && tapValid && !longPressed && IsTap(t.position))
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
            if (live == 1 && tapValid && !longPressed && a.fingerId == tapFinger && Time.unscaledTime - tapTime > 0.5f)
            {
                longPressed = true;
                DropPin(a.position);
            }
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
            if (Input.GetMouseButtonDown(1)) DropPin(mouse);
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
        // then outside the view (drops the card, or closes the map), a double tap (zooms in), and
        // a pin: a cluster zooms in until it splits, a single pin is selected. Empty map drops
        // the selection.
        void FullTap(Vector2 p)
        {
            bool card = selectedKind >= 0;
            if (card && cardButton.Contains(p))
            {
                CardAction();
                return;
            }
            if (card && cardRect.Contains(p)) return;
            if ((p - CloseAt).magnitude < ButtonR * 1.5f)
            {
                SetFull(false);
                return;
            }
            if (!card && (p - RecentreAt).magnitude < ButtonR * 1.5f)
            {
                recentring = true;
                return;
            }
            if (!Inside(p, 0f))
            {
                if (card) selectedKind = -1;
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
            if (card && (ToMap(SelectedWorld) - p).magnitude < 20f * s) return;
            foreach (var c in Clusters())
            {
                if ((c.At - p).magnitude > 20f * s) continue;
                if (c.Count > 1 && fullZoom < MaxZoom * 0.95f)
                {
                    zoomPivot = c.At;
                    zoomGoal = Mathf.Min(fullZoom * 2.2f, MaxZoom);
                    return;
                }
                Select(c.Top);
                return;
            }
            var race = RaceDirector.Instance;
            if (MissionUp(race) && (ToMap(race.Course.RingCenter) - p).magnitude < race.Course.RingRadius * fullZoom)
            {
                Select(-1);
                return;
            }
            selectedKind = -1;
        }

        static bool MissionUp(RaceDirector race) => race != null && race.Setup && !race.Gridded;

        void Select(int id)
        {
            selectedKind = id < 0 ? 0 : 1;
            selectedPlace = Mathf.Max(id, 0);
            selectedAt = Time.unscaledTime;
        }

        Vector2 SelectedWorld
        {
            get
            {
                if (selectedKind != 0) return geometry.Places[selectedPlace].Position;
                var ring = RaceDirector.Instance.Course.RingCenter;
                return new Vector2(ring.x, ring.z);
            }
        }

        // The mission travels there; a landmark becomes the waypoint and the map closes.
        void CardAction()
        {
            var race = RaceDirector.Instance;
            if (selectedKind == 0)
            {
                if (CanTravel(race)) StartTravel();
                return;
            }
            var at = geometry.Places[selectedPlace].Position;
            if (NavService.Instance != null && Game.Focus != null && !(race != null && (race.Gridded || race.Started)))
                NavService.Instance.SetWaypoint(new Vector3(at.x, Game.Focus.position.y, at.y));
            SetFull(false);
        }

        struct PinCluster
        {
            public Vector2 At;
            public int Top;
            public int Count;
        }

        // The mission and the landmarks in view, merged where they would overlap on screen; the
        // first one in (the mission) is the cluster's face. The selected pin stays out.
        List<PinCluster> Clusters()
        {
            var list = new List<PinCluster>();
            void Add(int id, Vector2 p)
            {
                if (!Inside(p, 6f * s) || (selectedKind >= 0 && (id < 0 ? selectedKind == 0 : selectedKind == 1 && selectedPlace == id))) return;
                for (int i = 0; i < list.Count; i++)
                    if ((list[i].At - p).magnitude < 30f * s)
                    {
                        var c = list[i];
                        c.Count++;
                        list[i] = c;
                        return;
                    }
                list.Add(new PinCluster { At = p, Top = id, Count = 1 });
            }
            var race = RaceDirector.Instance;
            if (MissionUp(race)) Add(-1, ToMap(race.Course.RingCenter));
            for (int i = 0; i < geometry.Places.Count; i++) Add(i, ToMap(geometry.Places[i].Position));
            return list;
        }

        // A long press (right click on desktop) points the arrow at that spot; not during a race.
        void DropPin(Vector2 p)
        {
            var race = RaceDirector.Instance;
            var nav = NavService.Instance;
            if (nav == null || Game.Focus == null || !Inside(p, 0f) || (race != null && (race.Gridded || race.Started))) return;
            var w = ScreenToWorld(p);
            nav.SetWaypoint(new Vector3(w.x, Game.Focus.position.y, w.y));
            Debug.Log($"[Minimap] waypoint pinned at ({w.x:F0}, {w.y:F0})");
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
            style.fontSize = Mathf.RoundToInt(8.5f * s);
            var drawn = new List<(Vector2 at, float reach)>();
            var halo = SpeedDial.Hex("#E6DDCB");
            halo.a = alpha;
            var ink = Ink;
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

        // Players as coloured dots with an initial, the pins (icons; names only once zoomed in),
        // then, with a selection, everything else dimmed under the popped pin and its card.
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
                        Disc(p, 6.6f * s, Ink, 20);
                        Disc(p, 5.2f * s, color, 20);
                    });
                    if (!string.IsNullOrEmpty(r.displayName)) Label(p, r.displayName.Substring(0, 1).ToUpperInvariant(), 7.5f, White);
                }
            bool named = fullZoom / BaseZoom >= 1.45f;
            foreach (var c in Clusters()) DrawPin(c.Top, c.At, c.Count, named, false);
            if (selectedKind >= 0)
            {
                Gl(() => Shape(Outline(0f), Vector2.zero, new Color(Ink.r, Ink.g, Ink.b, 0.38f)));
                DrawPin(selectedKind == 0 ? -1 : selectedPlace, ToMap(SelectedWorld), 1, true, true);
                DrawCard(player);
            }
            DrawChrome();
        }

        // The mission is a sunshine disc with a checker; a landmark an ink and white dot. A cluster
        // carries a pink count. The selected pin is bigger, outlined in white and pulses.
        void DrawPin(int id, Vector2 p, int count, bool named, bool selected)
        {
            float t = Time.unscaledTime - selectedAt;
            float k = selected ? 1.4f + 0.06f * Mathf.Sin(t * 6f) : 1f;
            var badge = p + new Vector2(8f, 8f) * s * k;
            Gl(() =>
            {
                if (selected)
                {
                    float pulse = Mathf.Repeat(t * 1.1f, 1f);
                    Ring(p, (13f + 16f * pulse) * s, 2.2f * s, new Color(Sunshine.r, Sunshine.g, Sunshine.b, 1f - pulse), 40);
                    Disc(p, (id < 0 ? 12f : 9.2f) * k * s, White, 28);
                }
                if (id < 0)
                {
                    Disc(p, 10f * k * s, Ink, 28);
                    Disc(p, 8.2f * k * s, Sunshine, 28);
                    float q = 2.8f * k * s;
                    for (int x = -1; x <= 0; x++)
                        for (int y = -1; y <= 0; y++)
                        {
                            var o = p + new Vector2(x * q, y * q);
                            Quad(o, o + new Vector2(0f, q), o + new Vector2(q, q), o + new Vector2(q, 0f), (x + y) % 2 == 0 ? Ink : White);
                        }
                }
                else
                {
                    Disc(p, 7f * k * s, Ink, 20);
                    Disc(p, 4.6f * k * s, White, 20);
                    Disc(p, 2.2f * k * s, Ink, 12);
                }
                if (count > 1)
                {
                    Disc(badge, 6.8f * s, Ink, 16);
                    Disc(badge, 5.6f * s, Pink, 16);
                }
            });
            if (count > 1) Label(badge, count.ToString(), 8f, White);
            string text = id < 0 ? "RACE START" : geometry.Places[id].Text;
            if (selected) Chip(p + new Vector2(0f, 24f * s), text, 11f, id < 0 ? Sunshine : Ink, id < 0 ? Ink : White);
            else if (count == 1 && named) Chip(p + new Vector2(0f, 15f * s), text, 9f, Ink, White);
        }

        // Docked to the bottom edge (bottom right in landscape): what it is, the route, how far,
        // who is waiting there, and one big button.
        void DrawCard(Vector2 player)
        {
            var race = RaceDirector.Instance;
            var safe = Screen.safeArea;
            bool landscape = Screen.width > Screen.height;
            float w = landscape ? Mathf.Min(330f * s, safe.width * 0.42f) : safe.width - 32f * s;
            float h = 118f * s;
            float x0 = landscape ? safe.xMax - 18f * s - w : safe.xMin + 16f * s;
            float y0 = safe.yMin + 14f * s;
            cardRect = new Rect(x0, y0, w, h);
            bool mission = selectedKind == 0;
            var world = SelectedWorld;
            float meters = Vector2.Distance(player, world);
            string title = mission ? "SPEED DIAL" : geometry.Places[selectedPlace].Text;
            string route = mission ? $"{race.Course.Data.startName} TO {race.Course.Data.finishName}".ToUpperInvariant() : "LANDMARK";
            string info = meters < 1000f ? $"{Mathf.Max(10, Mathf.RoundToInt(meters / 10f) * 10)} M AWAY" : $"{meters / 1000f:F1} KM AWAY";
            if (mission)
            {
                int waiting = 0;
                if (race.Last?.racers != null)
                    foreach (var r in race.Last.racers)
                    {
                        if (r.left || r.playerId == race.MyId) continue;
                        var body = race.BodyOf(r.playerId);
                        var pos = body != null ? body.position : new Vector3(r.position.x, r.position.y, r.position.z);
                        if (Vector2.Distance(new Vector2(pos.x, pos.z), world) < race.Course.RingRadius + 4f) waiting++;
                    }
                info += waiting == 1 ? "    1 PLAYER WAITING" : $"    {waiting} PLAYERS WAITING";
            }
            bool can = !mission || CanTravel(race);
            string action = !mission ? "SET WAYPOINT" : can ? "FAST TRAVEL" : race.Started || race.Gridded ? "IN A RACE" : "IN A LOBBY";
            var c = cardRect.center;
            var button = new Rect(x0 + 14f * s, y0 + 12f * s, w - 28f * s, 38f * s);
            cardButton = new Rect(button.x - 6f * s, button.y - 6f * s, button.width + 12f * s, button.height + 12f * s);
            Gl(() =>
            {
                Parallelogram(c + new Vector2(6f * s, -7f * s), w * 0.5f, h * 0.5f, 7f * s, new Color(0f, 0f, 0f, 0.35f));
                Parallelogram(c, w * 0.5f, h * 0.5f, 7f * s, White);
                Parallelogram(c, w * 0.5f - 2.5f * s, h * 0.5f - 2.5f * s, 7f * s, Ink);
                var fill = can ? Sunshine : new Color(0.55f, 0.57f, 0.65f, 1f);
                Parallelogram(button.center, button.width * 0.5f, button.height * 0.5f, 6f * s, fill);
            });
            TextAt(new Vector2(x0 + 18f * s, y0 + h - 12f * s), title, 17f, White);
            TextAt(new Vector2(x0 + 18f * s, y0 + h - 36f * s), route, 9.5f, Sunshine);
            TextAt(new Vector2(x0 + 18f * s, y0 + h - 52f * s), info, 9.5f, White);
            Label(button.center, action, 16f, Ink);
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
            if (selectedKind < 0) Button(RecentreAt, false);
            if (MapStyle == 1)
            {
                var c = CloseAt;
                EnsureStyle();
                style.fontSize = Mathf.RoundToInt(15f * s);
                var title = style.CalcSize(new GUIContent("DOGPATCH"));
                Chip(new Vector2(c.x + ButtonR + 14f * s + title.x * 0.5f, c.y), "DOGPATCH", 15f, Ink, White);
                if (selectedKind < 0) DrawLegend();
            }
            if (MapStyle == 2) Chip(ToScreen(new Vector2(-half.x + 70f * s, half.y + 2f * s)), "DOGPATCH", 13f, Ink, White);
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

        // B's key: what each mark means, and that a long press drops a pin.
        void DrawLegend()
        {
            var safe = Screen.safeArea;
            float y = safe.yMin + 12f * s + LegendH * 0.5f, icon = 16f * s, gap = 14f * s;
            string[] names = { "YOU", "RACERS", "RACE START", "FINISH", "PICKUP", "HOLD TO PIN" };
            EnsureStyle();
            style.fontSize = Mathf.RoundToInt(10f * s);
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
                        case 4: Diamond(at, 4.8f * s, 3.4f * s); break;
                        default: Flag(at - new Vector2(4f * s, 6.5f * s), Sunshine); break;
                    }
                });
                style.fontSize = Mathf.RoundToInt(10f * s);
                var size = style.CalcSize(new GUIContent(names[i]));
                style.normal.textColor = TextColor(White);
                GUI.Label(new Rect(x + icon + 4f * s, Screen.height - y - size.y * 0.5f, size.x, size.y), names[i], style);
                x += icon + 4f * s + size.x + gap;
            }
        }
    }
}
