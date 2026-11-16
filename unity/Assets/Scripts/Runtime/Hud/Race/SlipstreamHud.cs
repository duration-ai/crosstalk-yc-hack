using System.Collections.Generic;
using System.Linq;
using Dogpatch.Operator;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The race layer of the Slipstream HUD: forward-leaning ink, sunshine and hotline-pink panels in
    // the four corners, Exo 2 Black Italic numbers with a thin ink outline, and the guide's motion
    // (news shooting in from the right, the Call plate sliding in under the right thumb, countdown
    // segments dropping, numbers slamming in). Drawn with IMGUI under a shear matrix.
    public sealed partial class SlipstreamHud : MonoBehaviour
    {
        public RaceDirector Race;

        static readonly Color Ink = Hex("#1C2347");
        static readonly Color Sun = Hex("#FFCB1F");
        static readonly Color Pink = Hex("#FF3E8E");
        static readonly Color White = Hex("#FFFFFF");
        static readonly Color Paper = Hex("#F7F8F9");
        const float Lean = 0.21f;

        sealed class News
        {
            public string Text;
            public string Picto;
            public Color Color;
            public float Start = -1f;
        }

        Font font;
        Texture2D white;
        GUIStyle style;
        float s;
        float left, right, top, bottom;
        float band;
        bool narrow;
        readonly Queue<News> queue = new Queue<News>();
        News showing;
        int place;
        int previousPlace;
        float placeChangedAt = -10f;
        Rect readyRect;
        Rect callRect;
        bool readyVisible;
        bool callVisible;

        // On: from the mission lobby to the results, calls come only from driving through a spinning phone
        // and the manual phone button is hidden; from the lobby lock to the results the dev text field is too.
        public static bool HidePhoneInRaces = true;

        // The manual call button outside missions. A mission (its lobby, the race and the results) never
        // shows it: calls there come only from phone pickups. False hides it in free roam too.
        public static bool ManualCallsInFreeRoam = true;

        public static bool ManualCallsLocked => HidePhoneInRaces && RaceDirector.Instance is RaceDirector race && race.Gridded && !race.ResultsIn;
        int readyFinger = int.MinValue;
        int callFinger = int.MinValue;
        bool paused;
        bool readyWasVisible;
        float readyShownAt = -10f;
        float readyHeldAt = -10f;
        float readyReleasedAt = -10f;
        bool lobbyWas;
        float lobbyShownAt = -10f;

        public float NewsSince => showing == null || showing.Start < 0f ? -1f : Time.time - showing.Start;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color SlotColor(string color) =>
            color == "purple" ? Hex("#A35BFF") : color == "orange" ? Hex("#FF8A1E") : color == "green" ? Hex("#2EC27E") : Hex("#2D8CFF");

        static string Ordinal(int n) => n == 1 ? "ST" : n == 2 ? "ND" : n == 3 ? "RD" : "TH";

        void Awake()
        {
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
            white = Texture2D.whiteTexture;
        }

        void Start()
        {
            Race.EventArrived += OnEvent;
            Audio.GameEvents.Paused += OnPaused;
        }

        void OnDestroy() => Audio.GameEvents.Paused -= OnPaused;

        void OnPaused(bool on) => paused = on;

        void OnEnable() => Gameplay.ScreenShape.Changed += CancelPresses;

        void OnDisable() => Gameplay.ScreenShape.Changed -= CancelPresses;

        // A rotation moves every button, so presses that began in the old layout never land; a
        // held Call keeps talking until its finger lifts.
        void CancelPresses()
        {
            readyFinger = int.MinValue;
            plateFinger = int.MinValue;
            platePressed = -1;
        }

        void OnEvent(RaceEvent e)
        {
            var def = Race.Catalog.Get(e.type);
            string text = e.news ?? "";
            var mine = Race.Me != null ? WorldChanges.WorldChangeCatalog.ShortName(Race.Me.displayName) : null;
            if (!string.IsNullOrEmpty(mine))
            {
                var words = text.Split(' ');
                for (int i = 0; i < words.Length; i++)
                    if (words[i] == mine && (i == 0 || words[i - 1] == "WITH")) words[i] = "YOU";
                text = string.Join(" ", words);
            }
            queue.Enqueue(new News { Text = text, Picto = def?.pictogram ?? e.type, Color = SlotColor(e.color) });
        }

        public void PushNews(string text, string picto, string color) => queue.Enqueue(new News { Text = text, Picto = picto, Color = SlotColor(color) });

        // True when a screen point (pixels, origin bottom left) lands on a live button.
        public bool HitTest(Vector2 screen)
        {
            var p = new Vector2(screen.x, Screen.height - screen.y);
            float pad = 12f * s;
            return (readyVisible && Inflate(readyRect, pad).Contains(p)) || (callVisible && Inflate(callRect, pad).Contains(p)) || OnlineHit(p);
        }

        static Rect Inflate(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);

        void Update()
        {
            if (Race == null || !Race.Setup) return;
            if (leaveAt >= 0f && Time.time >= leaveAt)
            {
                leaveAt = -1f;
                if (Race.ResultsIn)
                {
                    Debug.Log("[SpeedDial] results tick: back to free roam");
                    Race.Leave();
                }
            }
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                var p = new Vector2(t.position.x, Screen.height - t.position.y);
                if (t.phase == TouchPhase.Began) Press(t.fingerId, p);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) Release(t.fingerId, p);
            }
            if (Input.touchCount > 0) return;
            var m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0)) Press(-1, m);
            if (Input.GetMouseButtonUp(0)) Release(-1, m);
        }

        void Press(int finger, Vector2 p)
        {
            float pad = 12f * s;
            if (OnlinePress(finger, p)) return;
            if (readyVisible && Inflate(readyRect, pad).Contains(p))
            {
                readyFinger = finger;
                readyHeldAt = Time.time;
            }
            else if (callVisible && Inflate(callRect, pad).Contains(p))
            {
                callFinger = finger;
                Race.Calls.BeginTalk();
            }
        }

        void Release(int finger, Vector2 p)
        {
            if (OnlineRelease(finger, p)) return;
            if (finger == callFinger)
            {
                callFinger = int.MinValue;
                Race.Calls.EndTalk();
            }
            if (finger == readyFinger)
            {
                readyFinger = int.MinValue;
                readyReleasedAt = Time.time;
                if (readyVisible && Inflate(readyRect, 12f * s).Contains(p)) PressReady();
            }
        }

        // READY in the lobby toggles you ready and back. On the results it's done: online that takes you
        // back to free roam once its press has played (a mission room is never rematched), offline it
        // rematches the local room.
        public void PressReady()
        {
            if (!Race.ResultsIn) Race.SetReady(!(Race.Me?.ready ?? false));
            else if (Race.Online) leaveAt = Time.time + 0.3f;
            else Race.SetReady(true);
        }

        float leaveAt = -1f;

        // ---- drawing primitives ----

        void Panel(Rect r, Color color, float lean = Lean)
        {
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -lean;
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            GUI.color = color;
            GUI.DrawTexture(r, white);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        void Framed(Rect r, Color fill, Color edge, float edgeWidth, float lean = Lean)
        {
            Panel(Inflate(r, edgeWidth), edge, lean);
            Panel(r, fill, lean);
        }

        void Icon(Rect r, string name, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Pictograms.Get(name), ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        void Stripes(float x, float y, float height, float length, Color color, bool toRight)
        {
            for (int i = 0; i < 3; i++)
            {
                float w = length * (1f - i * 0.22f);
                float yy = y + i * height * 0.3f;
                float xx = toRight ? x + i * 4f * s : x - w - i * 4f * s;
                Panel(new Rect(xx, yy, w, Mathf.Max(2f, height * 0.12f)), color, 0f);
            }
        }

        GUIStyle Style(float size, TextAnchor anchor)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, richText = false, padding = new RectOffset() };
            style.font = font;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
            style.alignment = anchor;
            return style;
        }

        Vector2 Measure(string text, float size) => Style(size, TextAnchor.UpperLeft).CalcSize(Dogpatch.Gameplay.HudLayout.Text(text));

        // IMGUI text is linearized once more than textures in a linear-space build, so text colors go
        // in gamma-corrected to land on the same palette as the panels.
        static Color TextColor(Color c, float alpha)
        {
            var g = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
            return new Color(g.r, g.g, g.b, c.a * alpha);
        }

        void Text(Rect r, string text, float size, Color fill, TextAnchor anchor = TextAnchor.MiddleLeft, float outline = 0f, float alpha = 1f)
        {
            var st = Style(size, anchor);
            if (outline > 0f)
            {
                st.normal.textColor = TextColor(Ink, alpha);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), text, st);
                }
            }
            st.normal.textColor = TextColor(fill, alpha);
            GUI.Label(r, text, st);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        // Back-out easing: overshoots about 10% before settling at 1 (the pop on slide-ins and presses).
        static float Overshoot(float t)
        {
            t -= 1f;
            return 1f + t * t * (3.2f * t + 2.2f);
        }

        // ---- frame ----

        void OnGUI()
        {
            if (Race == null || !Race.Setup) return;
            readyWasVisible = readyVisible;
            buttons.Clear();
            if (Dogpatch.Map.Minimap.FullOpen)
            {
                readyVisible = false;
                callVisible = false;
                return;
            }
            if (Race.Last == null && !Race.Online) return;
            GUI.depth = -5;
            s = Gameplay.HudLayout.U;
            left = Gameplay.HudLayout.Left;
            right = Gameplay.HudLayout.Right;
            top = Gameplay.HudLayout.Top;
            bottom = Gameplay.HudLayout.Bottom;
            narrow = Gameplay.HudLayout.Narrow;
            band = Gameplay.HudLayout.Band;
            readyVisible = false;
            callVisible = false;
            Gameplay.HudLayout.LobbyShowing = false;
            if (Race.Online)
            {
                WorldTags();
                NetChip();
                if (!Race.InRace) RouteTags();
            }
            if (Race.Last == null)
            {
                OperatorAndNews();
                if (ManualCallsInFreeRoam) PhoneButton(false);
                return;
            }

            var room = Race.Room;
            bool lobby = !Race.Gridded && (room.status == RoomStatus.Lobby || room.status == RoomStatus.Countdown) && NearRing();
            bool racing = Race.Started && !Race.ResultsIn;
            if (lobby && !lobbyWas) lobbyShownAt = Time.time;
            lobbyWas = lobby;

            if (!Race.ResultsIn) NameTags();
            if (racing) FindMe();
            if (lobby && !paused) Lobby(room);
            if (Race.Online && lobby && !paused) MissionPlate(room);
            if (Race.Gridded && !Race.ResultsIn) Countdown();
            if (racing && !Race.CrossedFinish) RaceCorners();
            if (Race.WaitingForResults)
            {
                FinishedBanner();
                LiveStandings();
            }
            if (Race.ResultsIn) Results();
            OperatorAndNews();
            if ((lobby || Race.Gridded || Race.ResultsIn ? !HidePhoneInRaces : ManualCallsInFreeRoam) || (PreviewState ?? Race.Calls.State) != CallState.None) PhoneButton(lobby || Race.ResultsIn);
            if (Race.Wrecking) WreckedBanner();
            if (Race.WreckFade > 0f) Panel(new Rect(0f, 0f, Screen.width, Screen.height), new Color(Ink.r, Ink.g, Ink.b, Race.WreckFade), 0f);
        }

        // The mission lobby only shows while you stand in or by the start ring.
        bool NearRing()
        {
            var focus = Race.Game.Focus;
            if (focus == null) return false;
            var d = focus.position - Race.Course.RingCenter;
            d.y = 0f;
            return Race.Game.CurrentCar == null && d.magnitude < Race.Course.RingRadius * 1.5f;
        }

        void Lobby(Room room)
        {
            Gameplay.HudLayout.LobbyShowing = true;
            HudV3.ArrivalCard.Draw(Race, room, lobbyShownAt);

            ReadyButton(Race.Me != null && Race.Me.ready);
        }

        void ReadyButton(bool pressed)
        {
            var r = Gameplay.HudLayout.Ready(112f * s, 64f * s);
            readyRect = r;
            readyVisible = true;
            if (!readyWasVisible) readyShownAt = Time.time;
            bool held = readyFinger != int.MinValue;
            float now = Time.time;
            float k = held ? Mathf.Lerp(1f, 0.88f, Mathf.Clamp01((now - readyHeldAt) / 0.06f))
                : now - readyReleasedAt < 0.32f ? 0.88f + 0.12f * Overshoot(Mathf.Clamp01((now - readyReleasedAt) / 0.28f))
                : Overshoot(Mathf.Clamp01((now - readyShownAt) / 0.3f));
            k = Mathf.Max(0.01f, k);
            float sx = k < 1f ? k + (1f - k) * 0.7f : k;
            r = new Rect(r.center.x - r.width * sx * 0.5f, r.center.y - r.height * k * 0.5f, r.width * sx, r.height * k);
            var racers = Race.Last?.racers;
            int total = racers?.Count ?? 0;
            int ready = racers?.Count(o => o.ready) ?? 0;
            V3.HudPlates.Ready(r, held ? V3.ReadyLook.Pressed : pressed ? V3.ReadyLook.Locked : V3.ReadyLook.Rest, s, ready, total);
        }

        void DrawFrame(Rect r, Color color, float width)
        {
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -Lean;
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, width), white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - width, r.width, width), white);
            GUI.DrawTexture(new Rect(r.x, r.y, width, r.height), white);
            GUI.DrawTexture(new Rect(r.xMax - width, r.y, width, r.height), white);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        void Countdown()
        {
            string label;
            float since;
            if (!Race.Started)
            {
                float f = Race.MsToStart / 1000f;
                int n = Mathf.Clamp(Mathf.CeilToInt(f), 1, 3);
                label = n.ToString();
                since = n - f;
            }
            else
            {
                since = Time.time - Race.StartedAt;
                if (since > 0.9f) return;
                label = "GO!";
            }
            bool go = Race.Started;
            float hold = go ? 0.9f : 1f;
            float alpha = Mathf.Clamp01((hold - since) / 0.08f);
            float scale = Mathf.Lerp(1.25f, 1f, EaseOut(Mathf.Clamp01(since / 0.12f)));
            float pt = HudV3.V3Draw.P / s;
            float size = (go ? 128f : 150f) * pt * scale;
            var m = Measure(label, size);
            var r = new Rect(Screen.width * 0.5f - m.x * 0.5f, Screen.height * 0.43f - m.y * 0.5f, m.x, m.y);
            Text(r, label, size, Sun, TextAnchor.UpperLeft, 5f * s * scale, alpha);
            StartLights(Screen.width * 0.5f, r.yMax + 6f * HudV3.V3Draw.P, go ? 3 : 4 - Mathf.Clamp(Mathf.CeilToInt(Race.MsToStart / 1000f), 1, 3), go, alpha);
        }

        // Once you're over the line: your place and time, and how long until the results at the latest.
        // Your car is a wreck: the word slams in for the second before you're back on the route.
        void WreckedBanner()
        {
            float since = Time.time - Race.WreckedAt;
            float scale = since < 0.12f ? Mathf.Lerp(2.4f, 1f, since / 0.12f) : 1f;
            var label = "WRECKED!";
            var size = Measure(label, 72f * scale);
            var r = new Rect(Screen.width * 0.5f - size.x * 0.5f, Screen.height * 0.3f - size.y * 0.5f, size.x, size.y);
            Text(r, label, 72f * scale, Pink, TextAnchor.UpperLeft, 3.5f * s * scale);
        }

        // Each start ring's route name floating over it, so you can pick a race by driving to its ring.
        void RouteTags()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var (name, at) in Race.RouteRingSpots())
            {
                if (Race.Joined && !Race.IsCourseRing(at)) continue;
                var sp = cam.WorldToScreenPoint(at + Vector3.up * 3.2f);
                if (sp.z < 1f || sp.z > 140f) continue;
                var size = Measure(name, 20f);
                var r = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y);
                Panel(Inflate(r, 6f * s), Pink);
                Text(r, name, 20f, White, TextAnchor.UpperLeft);
            }
        }

        void FinishedBanner()
        {
            var me = Race.Me;
            if (me == null) return;
            var label = "FINISHED " + me.place + Ordinal(me.place);
            var size = Measure(label, 64f);
            var r = new Rect(Screen.width * 0.5f - size.x * 0.5f, Screen.height * 0.3f - size.y * 0.5f, size.x, size.y);
            Text(r, label, 64f, Sun, TextAnchor.UpperLeft, 3f * s);
            float wait = Race.Room.closesAt > 0 ? Mathf.Max(0f, (Race.Room.closesAt - Race.NowMs) / 1000f) : 0f;
            var sub = (me.finishMs / 1000f).ToString("F1") + "S   RESULTS IN " + Mathf.CeilToInt(wait) + "S";
            var subSize = Measure(sub, 22f);
            Text(new Rect(Screen.width * 0.5f - subSize.x * 0.5f, r.yMax + 6f * s, subSize.x, subSize.y), sub, 22f, White, TextAnchor.UpperLeft, 1.6f * s);
        }

        void RaceCorners()
        {
            var me = Race.Me;
            if (me == null) return;
            if (me.place != place)
            {
                previousPlace = place;
                place = me.place;
                placeChangedAt = Time.time;
            }
            int total = Race.Last.racers.Count(r => !r.left);
            float since = Time.time - placeChangedAt;

            float placeSize = 76f * HudV3.V3Draw.P / s;
            var digitSize = Measure("4", placeSize);
            var digit = new Rect(left, top - 6f * s, digitSize.x, digitSize.y);
            if (since < 0.45f && previousPlace > 0)
            {
                float a = 1f - since / 0.45f;
                Panel(new Rect(left - 6f * s, top + 58f * s, digit.width + 64f * s, 7f * s), new Color(Sun.r, Sun.g, Sun.b, a));
            }
            if (since < 0.28f && previousPlace > 0)
            {
                float p = EaseOut(since / 0.28f);
                GUI.BeginGroup(new Rect(digit.x - 8f * s, digit.y + 6f * s, digit.width + 16f * s, digit.height - 8f * s));
                var local = new Rect(8f * s, -6f * s, digit.width, digit.height);
                Text(new Rect(local.x, local.y + p * digit.height, local.width, local.height), previousPlace.ToString(), placeSize, Sun, TextAnchor.UpperLeft, 2.6f * s);
                Text(new Rect(local.x, local.y - (1f - p) * digit.height, local.width, local.height), place.ToString(), placeSize, Sun, TextAnchor.UpperLeft, 2.6f * s);
                GUI.EndGroup();
            }
            else
            {
                float pop = previousPlace > place && since < 0.5f ? 1f + 0.28f * Mathf.Sin(Mathf.Clamp01((since - 0.28f) / 0.22f) * Mathf.PI) : 1f;
                var popped = new Rect(digit.x - digit.width * (pop - 1f) * 0.3f, digit.y - digit.height * (pop - 1f) * 0.4f, digit.width * pop, digit.height * pop);
                Text(popped, place.ToString(), placeSize * pop, Sun, TextAnchor.UpperLeft, 2.6f * s * pop);
            }
            int shown = since < 0.14f && previousPlace > 0 ? previousPlace : place;
            var suffix = Measure(Ordinal(shown), 22f);
            Text(new Rect(digit.xMax - 2f * s, top + 4f * s, suffix.x, suffix.y), Ordinal(shown), 22f, White, TextAnchor.UpperLeft, 1.6f * s);
            Text(new Rect(digit.xMax - 8f * s, top + 28f * s, 60f * s, suffix.y), "/" + total, 22f, White, TextAnchor.UpperLeft, 1.6f * s);

            float length = Mathf.Max(Race.Room.routeLength, 1f);
            int meters = Mathf.Max(0, Mathf.RoundToInt(me.metersToFinish));
            var meterAt = Gameplay.HudLayout.Meters;
            float edge = meterAt.x;
            float mtop = meterAt.y;
            var mSize = Measure("M", 16f);
            var num = meters.ToString();
            float meterSize = 44f * HudV3.V3Draw.P / s;
            var nSize = Measure(num, meterSize);
            var mRect = new Rect(edge - mSize.x, mtop + 20f * s, mSize.x, mSize.y);
            Text(mRect, "M", 16f, White, TextAnchor.UpperLeft, 1.4f * s);
            var nRect = new Rect(mRect.x - nSize.x - 2f * s, mtop - 4f * s, nSize.x, nSize.y);
            Text(nRect, num, meterSize, White, TextAnchor.UpperLeft, 2.2f * s);
            var flag = new Rect(nRect.x - 36f * s, mtop + 6f * s, 26f * s, 22f * s);
            Framed(flag, Sun, Ink, 1.6f * s);
            Icon(new Rect(flag.x + 4f * s, flag.y + 2f * s, 18f * s, 18f * s), "yc", Ink);

            float frac = me.finished ? 1f : Mathf.Clamp01(1f - me.metersToFinish / length);
            int done = Mathf.FloorToInt(frac * 12f);
            float segW = 8f * s;
            float gap = 2.6f * s;
            float x0 = edge - 12f * (segW + gap) + gap;
            for (int i = 0; i < 12; i++)
            {
                var seg = new Rect(x0 + i * (segW + gap), nRect.yMax + 1f * s, segW, 5f * s);
                Panel(seg, i < done ? White : i == done ? Sun : new Color(Ink.r, Ink.g, Ink.b, 0.45f));
            }
        }

        void Results()
        {
            var results = Race.Last.results;
            var mine = results.FirstOrDefault(r => r.playerId == Race.MyId);
            float since = Time.time - Race.ResultsAt;
            if (mine != null)
            {
                float scale = since < 0.15f ? Mathf.Lerp(2.6f, 1f, since / 0.15f) : 1f;
                var label = mine.place.ToString();
                var size = Measure(label, 150f * scale);
                var anchor = new Vector2(left + 34f * s, top + 50f * s);
                var digit = new Rect(anchor.x, anchor.y + (1f - scale) * 40f * s, size.x, size.y);
                Text(digit, label, 150f * scale, Sun, TextAnchor.UpperLeft, 4.5f * s * scale);
                if (since > 0.12f)
                {
                    var suffix = Ordinal(mine.place);
                    var ss = Measure(suffix, 50f);
                    Text(new Rect(anchor.x + Measure(label, 150f).x - 6f * s, anchor.y + 8f * s, ss.x, ss.y), suffix, 50f, White, TextAnchor.UpperLeft, 2.6f * s);
                }
                if (since > 0.35f)
                {
                    float p = EaseOut(Mathf.Clamp01((since - 0.35f) / 0.25f));
                    float x = Mathf.Lerp(-320f * s, left + 24f * s, p);
                    float y = top + 196f * s;
                    var block = new Rect(x, y, 44f * s, 40f * s);
                    var bar = new Rect(block.xMax + 2f * s, y, 250f * s, 40f * s);
                    Panel(bar, Paper);
                    Panel(block, Sun);
                    Icon(new Rect(block.x + 8f * s, block.y + 6f * s, 28f * s, 28f * s), mine.award == "CHAOS CALLER" ? "call" : "yc", Ink);
                    Text(new Rect(bar.x + 14f * s, bar.y, bar.width, bar.height), mine.award, 20f, Ink);
                    if (p < 1f) Stripes(block.x - 6f * s, y + 10f * s, 30f * s, 60f * s, Sun, false);
                }
                if (mine.finished && mine.finishMs > 0 && since > 0.6f)
                {
                    float tick = EaseOut(Mathf.Clamp01((since - 0.6f) / 0.7f));
                    var time = (mine.finishMs / 1000f * tick).ToString("F1") + "S";
                    var ts = Measure(time, 28f);
                    Text(new Rect(left + 34f * s, top + 244f * s, ts.x, ts.y), time, 28f, tick < 1f ? White : Sun, TextAnchor.UpperLeft, 1.8f * s);
                }
            }

            var ordered = results.OrderBy(r => r.place).ToList();
            float rowY = Standings(ordered.Select(r => (r.place, r.playerId, r.displayName, r.color, r.finished ? FinishTime(r.finishMs) : "DNF")).ToList(), since);
            Podium(ordered, rowY + 6f * s, since);
            if (since > 0.6f) ReadyButton(false);
        }

        static string FinishTime(long ms) => (ms / 1000f).ToString("F1") + "S";

        // Over the line while others still race: the standings as they stand, each finish time as it
        // lands and the metres still to go for the rest, in the same place the results take.
        void LiveStandings()
        {
            var rows = Race.Last.racers.Where(r => !r.left).OrderBy(r => r.place)
                .Select(r => (r.place, r.playerId, r.displayName, r.color, r.finished ? FinishTime(r.finishMs) : Mathf.RoundToInt(r.metersToFinish) + "M"))
                .ToList();
            Standings(rows, Time.time - Race.FinishedAt);
        }

        // The standings rows top right, sliding in one by one: place, name, and a time or note beside
        // each. Returns the y under the last row.
        float Standings(List<(int place, string playerId, string name, string color, string note)> rows, float since)
        {
            float rowY = Gameplay.HudLayout.ResultsTop;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool me = r.playerId == Race.MyId;
                float slide = Mathf.Clamp01((since - 0.45f - i * 0.08f) / 0.28f);
                if (slide <= 0f)
                {
                    rowY += 31f * s;
                    continue;
                }
                float dx = (1f - Overshoot(slide)) * 260f * s;
                var block = new Rect(right - 160f * s + dx, rowY, 26f * s, 24f * s);
                var bar = new Rect(block.xMax + 3f * s, rowY, 128f * s, 24f * s);
                Panel(bar, me ? Ink : Paper);
                Panel(block, me ? Sun : SlotColor(r.color));
                Text(block, r.place.ToString(), 15f, me ? Ink : White, TextAnchor.MiddleCenter);
                Text(new Rect(bar.x + 10f * s, bar.y, bar.width, bar.height), me ? "YOU" : r.name.ToUpperInvariant(), 14f, me ? White : Ink);
                Text(new Rect(block.x - 78f * s, rowY, 70f * s, 24f * s), r.note, 15f, r.note == "DNF" ? Pink : White, TextAnchor.MiddleRight, 1.4f * s);
                rowY += 31f * s;
            }
            return rowY;
        }

        // Mario Kart's podium in flat Slipstream blocks under the standings: 2nd, 1st, 3rd rise in turn
        // with an overshoot, each in its racer's colour (yours in sunshine).
        void Podium(List<RaceResult> ordered, float y, float since)
        {
            if (ordered.Count < 2) return;
            int[] order = { 2, 1, 3 };
            float[] heights = { 30f, 44f, 20f };
            float w = 44f * s;
            float gap = 4f * s;
            float x0 = right - 160f * s + (157f * s - (3f * w + 2f * gap)) * 0.5f;
            float baseY = y + 46f * s;
            for (int i = 0; i < 3; i++)
            {
                var r = ordered.FirstOrDefault(o => o.place == order[i]);
                if (r == null) continue;
                bool me = r.playerId == Race.MyId;
                float rise = Overshoot(Mathf.Clamp01((since - 0.85f - i * 0.12f) / 0.3f));
                if (rise <= 0f) continue;
                float h = heights[order[i] - 1] * s * rise;
                var col = new Rect(x0 + i * (w + gap), baseY - h, w, h);
                Panel(new Rect(col.x - 2f * s, col.y - 2f * s, col.width + 4f * s, col.height + 2f * s), Ink, 0f);
                Panel(col, me ? Sun : SlotColor(r.color), 0f);
                if (rise > 0.6f) Text(new Rect(col.x, col.y, col.width, Mathf.Min(col.height, 26f * s)), r.place.ToString(), 18f, me ? Ink : White, TextAnchor.MiddleCenter);
            }
        }

        void OperatorAndNews()
        {
            if (OperatorBand()) return;

            if (showing == null && queue.Count > 0)
            {
                showing = queue.Dequeue();
                showing.Start = Time.time;
            }
            if (showing == null) return;
            float since = Time.time - showing.Start;
            if (since > 2.45f)
            {
                showing = null;
                return;
            }
            float baseLeft = narrow ? left : Mathf.Max(left + 150f * s, Screen.width * 0.5f - 270f * s);
            float offset;
            float lean;
            if (since < 0.12f)
            {
                float p = EaseOut(since / 0.12f);
                offset = (1f - p) * (Screen.width - baseLeft);
                lean = 0.65f;
            }
            else if (since < 0.3f)
            {
                float p = (since - 0.12f) / 0.18f;
                offset = -6f * s * Mathf.Sin(p * Mathf.PI);
                lean = Mathf.Lerp(0.65f, Lean, Mathf.Clamp01(p * 2f));
            }
            else if (since < 2.2f)
            {
                offset = 0f;
                lean = Lean;
            }
            else
            {
                float p = (since - 2.2f) / 0.25f;
                offset = -p * p * (baseLeft + 500f * s);
                lean = Mathf.Lerp(Lean, 0.45f, p);
            }
            float y = band;
            var textSize = Measure(showing.Text, 15f);
            var block = new Rect(baseLeft + offset, y, 40f * s, 30f * s);
            var bar = new Rect(block.xMax + 2f * s, y, Mathf.Max(textSize.x + 28f * s, 150f * s), 30f * s);
            Panel(bar, Ink, lean);
            Panel(block, showing.Color, lean);
            Icon(new Rect(block.x + 8f * s, block.y + 3f * s, 24f * s, 24f * s), showing.Picto, White);
            float textAlpha = Mathf.Clamp01((since - 0.06f) / 0.12f);
            Text(new Rect(bar.x + 12f * s, bar.y, bar.width, bar.height), showing.Text, 15f, White, TextAnchor.MiddleLeft, 0f, textAlpha);
            float stripeLength = since < 0.18f ? 90f * s : 30f * s;
            Stripes(bar.xMax + 6f * s, y + 8f * s, 24f * s, stripeLength, showing.Color, true);
        }
    }
}
