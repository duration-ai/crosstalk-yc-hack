using System.Linq;
using Dogpatch.Race;
using UnityEngine;
using static Dogpatch.HudV3.V3Draw;

namespace Dogpatch.HudV3
{
    // HUD v3 boards 3 and 4: the start-ring arrival card, top left. A sunshine title plate with the
    // route name, a short ink objective, the route in numbers (distance, chevrons, the YC chip), and
    // one block per racer slot with a ready tick, empty slots shimmering. The auto-start count sits
    // beside the blocks with ten segments that drop one a second. Rows drop in from the right 60 ms
    // apart; once you're ready the objective and route rows fold away and the card keeps the name
    // and the racers. Story text stays with the Operator's voice line.
    public static class ArrivalCard
    {
        const int Slots = 4;

        static float readyAt = -10f;
        static bool readyWas;
        static float countdownAt = -10f;
        static bool countdownWas;

        static string Objective(string routeId) => routeId switch
        {
            "a" => "PITCH THE INVESTOR FIRST",
            "b" => "PLUG IN AT PIER 70",
            "c" => "STOP YOUR COFOUNDER",
            "d" => "HOT DEMO OVER THE BRIDGE",
            _ => "BEAT THE INVITE TO YC",
        };

        public static void Draw(RaceDirector race, Room room, float shownAt)
        {
            float p = P;
            float now = Time.time;
            bool ready = race.Me != null && race.Me.ready;
            if (ready && !readyWas) readyAt = now;
            readyWas = ready;
            float fold = ready ? EaseOut((now - readyAt) / 0.16f) : 0f;

            var at = Gameplay.HudLayout.LobbyPanel;
            float x = at.x + 4f * p, y = at.y + 2f * p;
            float Slide(int row) => (1f - Overshoot((now - shownAt - row * 0.06f) / 0.14f)) * (Screen.width - x + 40f * p);

            var data = race.Course.Data;
            string title = string.IsNullOrEmpty(data?.routeName) ? "MISSION" : data.routeName.ToUpperInvariant();
            var titleSize = Measure(title, 23f);
            var plate = new Rect(x + Slide(0), y, 42f * p + titleSize.x + 26f * p, 40f * p);
            Panel(new Rect(plate.x - 2.5f * p, plate.y - 2.5f * p, plate.width + 5f * p, plate.height + 5f * p), Ink);
            Panel(plate, Sun);
            var block = new Rect(plate.x + 4f * p, plate.y + 4f * p, 42f * p, plate.height - 8f * p);
            Panel(block, Ink);
            Icon(new Rect(block.x + 10f * p, block.y + 4f * p, block.width - 20f * p, block.height - 8f * p), "dash", Sun);
            Text(new Rect(block.xMax + 10f * p, plate.y, titleSize.x + 8f * p, plate.height), title, 23f, Ink);
            y = plate.yMax + 6f * p;

            float keep = 1f - fold;
            if (keep > 0.01f)
            {
                var obj = Objective(race.RouteId);
                var objSize = Measure(obj, 13f);
                var bar = new Rect(x + 8f * p + Slide(1), y, objSize.x + 26f * p, 24f * p * keep);
                Panel(bar, Alpha(Ink, keep));
                if (keep > 0.5f) Text(new Rect(bar.x + 12f * p, bar.y, objSize.x + 4f * p, bar.height), obj, 13f, White, TextAnchor.MiddleLeft, 0f, keep);
                y = bar.yMax + 6f * p * keep;

                float length = room.routeLength > 0f ? room.routeLength : race.Course.Route != null ? race.Course.Route.Length : 0f;
                var km = (length / 1000f).ToString("0.0");
                var kmSize = Measure(km, 32f);
                float rx = x + 10f * p + Slide(2);
                float rh = 34f * p * keep;
                if (keep > 0.5f)
                {
                    Text(new Rect(rx, y, kmSize.x, rh), km, 32f, White, TextAnchor.MiddleLeft, 2f, keep);
                    var unit = Measure("KM", 15f);
                    Text(new Rect(rx + kmSize.x + 3f * p, y + 4f * p, unit.x, rh), "KM", 15f, White, TextAnchor.MiddleLeft, 1.4f, keep);
                    float cx = rx + kmSize.x + unit.x + 12f * p;
                    Text(new Rect(cx, y, 30f * p, rh), "»", 26f, Sun, TextAnchor.MiddleLeft, 1.6f, keep);
                    var chip = new Rect(cx + 28f * p, y + (rh - 26f * p) * 0.5f, 30f * p, 26f * p);
                    Panel(new Rect(chip.x - 2f * p, chip.y - 2f * p, chip.width + 4f * p, chip.height + 4f * p), Alpha(Ink, keep), 5f);
                    Panel(chip, Alpha(Sun, keep), 5f);
                    Icon(new Rect(chip.x + 5f * p, chip.y + 3f * p, chip.width - 10f * p, chip.height - 6f * p), "yc", Alpha(Ink, keep));
                }
                y += rh + 6f * p * keep;
            }

            var racers = race.Last.racers.Where(r => !r.left).OrderBy(r => r.slot).ToList();
            float bx = x + 6f * p + Slide(3);
            for (int i = 0; i < Slots; i++)
            {
                var b = new Rect(bx + i * 46f * p, y, 40f * p, 30f * p);
                if (i < racers.Count)
                {
                    var r = racers[i];
                    bool me = r.playerId == race.MyId;
                    Panel(new Rect(b.x - 2f * p, b.y - 2f * p, b.width + 4f * p, b.height + 4f * p), me ? White : Ink, 6f);
                    Panel(b, Slipstream.SlipstreamHud.SlotColor(r.color), 6f);
                    var initial = string.IsNullOrEmpty(r.displayName) ? "?" : r.displayName.Substring(0, 1).ToUpperInvariant();
                    Text(b, initial, 17f, White, TextAnchor.MiddleCenter, 1.4f);
                    if (r.ready)
                    {
                        var tick = new Rect(b.xMax - 12f * p, b.y - 8f * p, 18f * p, 16f * p);
                        Panel(tick, Ink, 3f);
                        Panel(new Rect(tick.x + 1.5f * p, tick.y + 1.5f * p, tick.width - 3f * p, tick.height - 3f * p), Sun, 3f);
                        Icon(new Rect(tick.x + 3f * p, tick.y + 2f * p, tick.width - 6f * p, tick.height - 4f * p), "ready", Ink);
                    }
                }
                else
                {
                    Panel(b, Alpha(Ink, 0.28f), 6f);
                    for (int d = 0; d < 4; d++)
                    {
                        Panel(new Rect(b.x + 3f * p + d * 9.5f * p, b.y, 5f * p, 1.6f * p), White, 0f);
                        Panel(new Rect(b.x + 3f * p + d * 9.5f * p, b.yMax - 1.6f * p, 5f * p, 1.6f * p), White, 0f);
                    }
                    float sweep = Mathf.Repeat(now / 1.4f + i * 0.12f, 1f);
                    if (sweep < 0.5f) Panel(new Rect(b.x + sweep * 2f * (b.width - 8f * p), b.y + 2f * p, 8f * p, b.height - 4f * p), Alpha(White, 0.35f), 6f);
                }
            }

            bool counting = room.status == RoomStatus.Countdown;
            if (counting && !countdownWas) countdownAt = now;
            countdownWas = counting;
            float storyKeep = keep * (counting ? 1f - EaseOut((now - countdownAt) / 0.16f) : 1f);
            var story = Slipstream.MissionBriefing.StoryOf(race.RouteId);
            if (storyKeep > 0.01f && !string.IsNullOrEmpty(story))
            {
                float u = Gameplay.HudLayout.U;
                float top = y + 30f * p + 14f * p;
                float right = Screen.width - Gameplay.HudLayout.Safe.xMin - 22f * u;
                var map = Gameplay.HudLayout.MinimapScreen;
                float mapR = Gameplay.HudLayout.MinimapRadius;
                if (top < Screen.height - map.y + mapR + 8f * p) right = Mathf.Min(right, map.x - mapR - 14f * p);
                float floor = Gameplay.HudLayout.Bottom;
                var call = Gameplay.HudLayout.Call(112f * u, 64f * u, true);
                if (call.x < right) floor = Mathf.Min(floor, call.y - 10f * p);
                var stick = Gameplay.HudLayout.StickScreen;
                float stickTop = Screen.height - stick.y - Gameplay.HudLayout.StickRadius - 10f * p;
                if (stick.x - Gameplay.HudLayout.StickRadius < right) floor = Mathf.Min(floor, stickTop);
                float w = Mathf.Min(360f * p, right - x);
                float pad = 10f * p;
                var st = Style(20f, TextAnchor.UpperLeft);
                st.wordWrap = true;
                float pt = 20f;
                float h = st.CalcHeight(new GUIContent(story), w - 2f * pad);
                while (pt > 11f && top + h + 2f * pad > floor)
                {
                    pt -= 1f;
                    st.fontSize = Mathf.Max(1, Mathf.RoundToInt(pt * p));
                    h = st.CalcHeight(new GUIContent(story), w - 2f * pad);
                }
                var storyPlate = new Rect(x + 6f * p + Slide(4), top, w, (h + 2f * pad) * storyKeep);
                Panel(new Rect(storyPlate.x - 2.5f * p, storyPlate.y - 2.5f * p, storyPlate.width + 5f * p, storyPlate.height + 5f * p), Alpha(Ink, storyKeep), 4f);
                Panel(storyPlate, Alpha(White, storyKeep), 4f);
                if (storyKeep > 0.5f)
                {
                    var ink = QualitySettings.activeColorSpace == ColorSpace.Linear ? Ink.gamma : Ink;
                    st.normal.textColor = new Color(ink.r, ink.g, ink.b, storyKeep);
                    GUI.Label(new Rect(storyPlate.x + pad, storyPlate.y + pad, w - 2f * pad, h), story, st);
                }
                st.wordWrap = false;
            }

            if (room.status == RoomStatus.Countdown)
            {
                float f = race.MsToStart / 1000f;
                int secs = Mathf.Clamp(Mathf.CeilToInt(f), 0, 99);
                var label = secs.ToString("00");
                float pulse = secs <= 3 ? 1f + 0.08f * Mathf.Sin(Mathf.Clamp01(secs - f) * Mathf.PI) : 1f;
                var size = Measure(label, 36f * pulse);
                float cx = bx + Slots * 46f * p + 8f * p;
                var r = new Rect(cx, y - 8f * p, size.x, size.y);
                Text(r, label, 36f * pulse, Sun, TextAnchor.UpperLeft, 2.5f);
                float segW = 5f * p, gap = 2f * p;
                for (int i = 0; i < 10; i++)
                {
                    var seg = new Rect(cx + i * (segW + gap), y + 30f * p, segW, 6f * p);
                    if (i < secs) Panel(seg, Sun, 1.5f);
                    else if (i == secs)
                    {
                        float t = Mathf.Clamp01((secs + 1 - f) * 2.2f);
                        if (t >= 1f) continue;
                        seg.y += EaseOut(t) * 14f * p;
                        Panel(seg, Alpha(Sun, 1f - t), 1.5f);
                    }
                    else Panel(seg, Alpha(Ink, 0.35f), 1.5f);
                }
            }
        }
    }
}
