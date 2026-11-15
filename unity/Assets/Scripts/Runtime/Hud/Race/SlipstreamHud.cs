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

        // Off while Chris tests with 100 asks; on, races are hands-free only (phone pickups).
        public static bool HidePhoneInRaces = false;
        int readyFinger = int.MinValue;
        int callFinger = int.MinValue;
        bool paused;

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
            if (callVisible && Inflate(callRect, pad).Contains(p))
            {
                callFinger = finger;
                Race.Calls.BeginTalk();
            }
            else if (readyVisible && Inflate(readyRect, pad).Contains(p)) readyFinger = finger;
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
                if (readyVisible && Inflate(readyRect, 12f * s).Contains(p)) PressReady();
            }
        }

        public void PressReady()
        {
            if (Race.ResultsIn) Race.SetReady(true);
            else Race.SetReady(!(Race.Me?.ready ?? false));
        }

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

        Vector2 Measure(string text, float size) => Style(size, TextAnchor.UpperLeft).CalcSize(new GUIContent(text));

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

        // ---- frame ----

        void OnGUI()
        {
            if (Race == null || !Race.Setup) return;
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
            }
            if (Race.Last == null)
            {
                OperatorAndNews();
                PhoneButton(false);
                return;
            }

            var room = Race.Room;
            bool lobby = !Race.Gridded && (room.status == RoomStatus.Lobby || room.status == RoomStatus.Countdown) && NearRing();
            bool racing = Race.Started && !Race.ResultsIn;

            if (!Race.ResultsIn) NameTags();
            if (racing) FindMe();
            if (lobby && !paused) Lobby(room);
            if (Race.Online && lobby && !paused) MissionPlate(room);
            if (Race.Gridded && !Race.ResultsIn) Countdown();
            if (racing) RaceCorners();
            if (racing && Race.CrossedFinish) FinishedBanner();
            if (Race.ResultsIn) Results();
            OperatorAndNews();
            if (!(HidePhoneInRaces && Race.Gridded)) PhoneButton(lobby || Race.ResultsIn);
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
            var racers = Race.Last.racers.OrderBy(r => r.slot).ToList();
            var at = Gameplay.HudLayout.LobbyPanel;
            float y = at.y;
            foreach (var r in racers)
            {
                bool me = r.playerId == Race.MyId;
                var block = new Rect(at.x, y, 30f * s, 24f * s);
                var bar = new Rect(block.xMax + 3f * s, y, 150f * s, 24f * s);
                Panel(bar, me ? Ink : Paper);
                Panel(block, SlotColor(r.color));
                if (!Race.Online) Icon(new Rect(block.x + 6f * s, block.y + 2f * s, 20f * s, 20f * s), r.ready ? "ready" : "dash", White);
                Text(new Rect(bar.x + 12f * s, bar.y, bar.width, bar.height), me ? "YOU" : r.displayName.ToUpperInvariant(), 14f, me ? White : Ink);
                y += 31f * s;
            }

            if (room.status == RoomStatus.Countdown)
            {
                float f = Race.MsToStart / 1000f;
                int secs = Mathf.Clamp(Mathf.CeilToInt(f), 0, 99);
                var label = secs.ToString("00");
                var size = Measure(label, 44f);
                var r = new Rect(Screen.width * 0.5f - size.x * 0.5f, top - 4f * s, size.x, size.y);
                Text(r, label, 44f, Sun, TextAnchor.UpperLeft, 2.4f * s);
                int count = 10;
                float segW = 9f * s;
                float gap = 3f * s;
                float x0 = Screen.width * 0.5f - (count * (segW + gap) - gap) * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    var seg = new Rect(x0 + i * (segW + gap), r.yMax + 1f * s, segW, 5f * s);
                    if (i < secs) Panel(seg, Sun);
                    else if (i == secs)
                    {
                        float p = Mathf.Clamp01((secs + 1 - f) * 2.2f);
                        if (p >= 1f) continue;
                        seg.y += EaseOut(p) * 18f * s;
                        Panel(seg, new Color(Sun.r, Sun.g, Sun.b, 1f - p));
                    }
                    else Panel(seg, new Color(Ink.r, Ink.g, Ink.b, 0.35f));
                }
            }

            if (!Race.Online) ReadyButton(Race.Me != null && Race.Me.ready);
        }

        void ReadyButton(bool pressed)
        {
            var r = Gameplay.HudLayout.Ready(112f * s, 62f * s);
            readyRect = r;
            readyVisible = true;
            bool held = readyFinger != int.MinValue;
            DrawFrame(new Rect(r.x + 7f * s, r.y + 7f * s, r.width, r.height), Sun, 1.6f * s);
            if (pressed || held)
            {
                var p = new Rect(r.x + 2f * s + r.width * 0.02f, r.y + 2f * s + r.height * 0.02f, r.width * 0.96f, r.height * 0.96f);
                Framed(p, White, Ink, 3f * s);
                Icon(new Rect(p.center.x - 16f * s, p.center.y - 16f * s, 32f * s, 32f * s), "ready", Ink);
            }
            else
            {
                Framed(r, Sun, Ink, 3f * s);
                Icon(new Rect(r.center.x - 16f * s, r.center.y - 16f * s, 32f * s, 32f * s), "ready", Ink);
            }
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
            float scale = since < 0.12f ? Mathf.Lerp(2.4f, 1f, since / 0.12f) : 1f + Mathf.Sin(Mathf.Min(since - 0.12f, 0.2f) / 0.2f * Mathf.PI) * 0.05f;
            float size = 150f * scale;
            var m = Measure(label, size);
            float shake = since < 0.2f ? Mathf.Sin(since * 90f) * 4f * s * (1f - since / 0.2f) : 0f;
            var r = new Rect(Screen.width * 0.5f - m.x * 0.5f + shake, Screen.height * 0.42f - m.y * 0.5f, m.x, m.y);
            Text(r, label, size, Sun, TextAnchor.UpperLeft, 5f * s * scale);
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

            var digitSize = Measure("4", 64f);
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
                Text(new Rect(local.x, local.y + p * digit.height, local.width, local.height), previousPlace.ToString(), 64f, Sun, TextAnchor.UpperLeft, 2.6f * s);
                Text(new Rect(local.x, local.y - (1f - p) * digit.height, local.width, local.height), place.ToString(), 64f, Sun, TextAnchor.UpperLeft, 2.6f * s);
                GUI.EndGroup();
            }
            else Text(digit, place.ToString(), 64f, Sun, TextAnchor.UpperLeft, 2.6f * s);
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
            var nSize = Measure(num, 40f);
            var mRect = new Rect(edge - mSize.x, mtop + 20f * s, mSize.x, mSize.y);
            Text(mRect, "M", 16f, White, TextAnchor.UpperLeft, 1.4f * s);
            var nRect = new Rect(mRect.x - nSize.x - 2f * s, mtop - 4f * s, nSize.x, nSize.y);
            Text(nRect, num, 40f, White, TextAnchor.UpperLeft, 2.2f * s);
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
            }

            float rowY = Gameplay.HudLayout.ResultsTop;
            foreach (var r in results.OrderBy(r => r.place))
            {
                bool me = r.playerId == Race.MyId;
                var block = new Rect(right - 160f * s, rowY, 26f * s, 24f * s);
                var bar = new Rect(block.xMax + 3f * s, rowY, 128f * s, 24f * s);
                Panel(bar, me ? Ink : Paper);
                Panel(block, me ? Sun : SlotColor(r.color));
                Text(block, r.place.ToString(), 15f, me ? Ink : White, TextAnchor.MiddleCenter);
                Text(new Rect(bar.x + 10f * s, bar.y, bar.width, bar.height), me ? "YOU" : r.displayName.ToUpperInvariant(), 14f, me ? White : Ink);
                rowY += 31f * s;
            }
            if (since > 0.6f) ReadyButton(false);
        }

        void OperatorAndNews()
        {
            var calls = Race.Calls;
            bool operatorUp = calls.State == CallState.Answered;
            if (operatorUp)
            {
                float t = Time.time - calls.StateSince;
                float inP = EaseOut(Mathf.Clamp01(t / 0.18f));
                float outP = Mathf.Clamp01((t - 2.3f) / 0.25f);
                float baseX = narrow ? left : Screen.width * 0.5f - 150f * s;
                float x = Mathf.Lerp(-360f * s, baseX, inP) - outP * outP * (baseX + 400f * s);
                float lineY = band;
                var size = Measure(calls.Line, 18f);
                var handset = new Rect(x, lineY, 40f * s, 38f * s);
                var line = new Rect(handset.xMax + 2f * s, lineY, size.x + 34f * s, 38f * s);
                Stripes(handset.x - 6f * s, lineY + 8f * s, 30f * s, 48f * s, Pink, false);
                Panel(line, Pink);
                Panel(handset, White);
                Icon(new Rect(handset.x + 7f * s, handset.y + 6f * s, 26f * s, 26f * s), "call", Pink);
                Text(new Rect(line.x + 14f * s, line.y, line.width, line.height), calls.Line, 18f, White);
                return;
            }

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

        // The cell-phone button: the Operator, always in reach. Hold to talk, let go to send. The badge is
        // the asks left; with none the button greys out and pressing it shows the no-asks line. It
        // wiggles and pulses when the phone buzzes (a new ask, GO).
        void PhoneButton(bool readyShowing)
        {
            var calls = Race.Calls;
            float now = Time.time;
            bool live = calls.State == CallState.Talking || calls.State == CallState.Waiting;
            bool hasAsks = calls.HasAsks;
            float size = 72f * s;
            float width = live ? 150f * s : size;
            var slot = Gameplay.HudLayout.Call(width, size, readyShowing);
            float x = slot.x;
            float y = slot.y;
            float buzz = now - calls.BuzzedAt;
            if (buzz < 1.2f && !live) x += Mathf.Sin(buzz * 42f) * 4f * s * (1f - buzz / 1.2f);
            var r = new Rect(x, y, width, size);
            callRect = r;
            callVisible = true;

            if (buzz < 1.2f && !live)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(buzz * 18f);
                DrawFrame(Inflate(new Rect(r.x + 6f * s, r.y + 6f * s, r.width, r.height), pulse * 4f * s), new Color(Pink.r, Pink.g, Pink.b, 0.5f + 0.5f * pulse), 2f * s);
            }

            var fill = hasAsks ? Pink : new Color(0.55f, 0.57f, 0.63f);
            bool pressed = calls.State == CallState.Talking && now - calls.StateSince < 0.12f;
            if (pressed) Framed(new Rect(r.x + r.width * 0.02f, r.y + r.height * 0.02f, r.width * 0.96f, r.height * 0.96f), White, Pink, 3f * s);
            else
            {
                Panel(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), Ink);
                Panel(r, fill);
            }
            var icon = new Rect(r.x + (size - 50f * s) * 0.5f, r.center.y - 25f * s, 50f * s, 50f * s);
            Icon(icon, "cellphone", pressed ? Pink : hasAsks ? White : new Color(1f, 1f, 1f, 0.6f));

            if (live)
            {
                float level = calls.State == CallState.Talking ? 1f : 0.35f;
                for (int i = 0; i < 6; i++)
                {
                    float h = (0.3f + 0.7f * Mathf.Abs(Mathf.Sin(now * (7f + i * 1.7f) + i * 1.3f))) * level;
                    float bh = Mathf.Max(4f * s, h * 38f * s);
                    Panel(new Rect(r.x + size + i * 12f * s, r.center.y + 19f * s - bh, 6.5f * s, bh), White, 0.12f);
                }
            }

            if (calls.CountsAsks)
            {
                var badge = new Rect(r.x + size - 20f * s, r.y - 10f * s, 30f * s, 24f * s);
                Panel(new Rect(badge.x - 2f * s, badge.y - 2f * s, badge.width + 4f * s, badge.height + 4f * s), Ink);
                Panel(badge, hasAsks ? Sun : Paper);
                Text(badge, Mathf.Max(0, calls.Asks()).ToString(), 15f, Ink, TextAnchor.MiddleCenter);
            }

            float refused = now - calls.TurnedDownAt;
            if (refused < 2.2f)
            {
                float a = Mathf.Clamp01(refused / 0.1f) * Mathf.Clamp01((2.2f - refused) / 0.3f);
                string line = CallController.NoAsksLine.ToUpperInvariant();
                var m = Measure(line, 14f);
                var bar = Gameplay.HudLayout.CallFromLeft
                    ? new Rect(r.xMax + 10f * s, r.center.y - 15f * s, m.x + 24f * s, 30f * s)
                    : new Rect(r.x - m.x - 34f * s, r.center.y - 15f * s, m.x + 24f * s, 30f * s);
                Panel(bar, new Color(Ink.r, Ink.g, Ink.b, a));
                Text(new Rect(bar.x + 12f * s, bar.y, bar.width, bar.height), line, 14f, new Color(1f, 1f, 1f, a));
            }
        }
    }
}
