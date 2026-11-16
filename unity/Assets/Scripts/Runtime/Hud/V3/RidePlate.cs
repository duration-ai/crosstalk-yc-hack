using UnityEngine;
using static Dogpatch.HudV3.V3Draw;

namespace Dogpatch.HudV3
{
    // HUD v3 board 2: the robotaxi or cab ride plate along the top, between the portrait corner and
    // the map. An ink bar with a leaning state block (white with the car while it drives, sunshine
    // with MANUAL once you steer), the destination, 12 progress segments and the metres left. On
    // arrival the whole plate turns sunshine with ARRIVED for 1.2 s, then slides out left.
    public static class RidePlate
    {
        public enum State { Driving, Manual, Arrived }

        const float EnterSeconds = 0.16f;
        const float ExitSeconds = 0.14f;

        public static Rect Area
        {
            get
            {
                float p = P;
                var hud = Gameplay.HudLayout.AvatarButton;
                float x = hud.xMax + 12f * p;
                float mapLeft = Gameplay.HudLayout.MinimapReadoutBelow
                    ? Gameplay.HudLayout.MinimapScreen.x - Gameplay.HudLayout.MinimapRadius - 10f * p
                    : Gameplay.HudLayout.Right;
                float w = Mathf.Clamp(mapLeft - x, 150f * p, 250f * p);
                return new Rect(x, hud.y + 4f * p, w, 34f * p);
            }
        }

        // shownAt: when the plate appeared; hideAt: when it starts to leave (or a negative value).
        public static void Draw(State state, string destination, float metres, float progress, float shownAt, float hideAt)
        {
            float p = P;
            float now = Time.time;
            var r = Area;
            float enter = Overshoot((now - shownAt) / EnterSeconds);
            r.y -= (1f - enter) * (r.yMax + 8f * p);
            if (hideAt >= 0f && now > hideAt)
            {
                float t = EaseOut((now - hideAt) / ExitSeconds);
                if (t >= 1f) return;
                r.x -= t * (r.xMax + 16f * p);
            }

            if (state == State.Arrived)
            {
                Panel(new Rect(r.x - 2f * p, r.y - 2f * p, r.width + 4f * p, r.height + 4f * p), Ink);
                Panel(r, Sun);
                var tick = new Rect(r.x + 10f * p, r.y + 6f * p, 22f * p, r.height - 12f * p);
                Icon(tick, "ready", Ink);
                Text(new Rect(tick.xMax + 8f * p, r.y, r.width, r.height), "ARRIVED", 16f, Ink);
                return;
            }

            Panel(r, Ink);
            bool manual = state == State.Manual;
            var block = new Rect(r.x + 4f * p, r.y + 4f * p, 42f * p, r.height - 8f * p);
            Panel(block, manual ? Sun : White);
            var glyph = new Rect(block.x + 9f * p, block.y + 3f * p, block.width - 18f * p, block.height - 6f * p);
            if (manual) Text(new Rect(block.x, block.y, block.width, block.height), "MANUAL", 7.5f, Ink, TextAnchor.MiddleCenter);
            else Icon(glyph, "traffic", Ink);

            string m = metres >= 1000f ? $"{metres / 1000f:F1}km" : $"{Mathf.Round(metres / 10f) * 10f:F0}m";
            var mSize = Measure(m, 22f);
            float textX = block.xMax + 10f * p;
            float metresX = r.xMax - 10f * p - mSize.x;
            Text(new Rect(metresX, r.y, mSize.x, r.height), m, 22f, manual ? Sun : White);
            float inner = Mathf.Max(20f * p, metresX - 8f * p - textX);
            Text(new Rect(textX, r.y + 4f * p, inner, 17f * p), destination, 15f, White);
            Segments(new Rect(textX, r.yMax - 11f * p, inner, 6f * p), 12, progress, manual ? Sun : White, Alpha(White, 0.25f));
        }
    }
}
