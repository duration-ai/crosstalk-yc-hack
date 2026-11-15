using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    public enum HudOption { A, B, C }

    // Every HUD position in one place, for portrait and landscape, so the pieces drawn by different
    // files (Hud, SlipstreamHud, OperatorHud, Minimap, AvatarCapture, PerfReadout, DragInput) can
    // never overlap. Three candidate layouts for Tim's pick (-hud-layout A|B|C):
    //   A  Corners: minimap bottom left, fixed stick bottom right, Call above the stick, place top
    //      left, meters top right.
    //   B  Map up top: minimap top right under pause, place and meters stacked top left, Call
    //      bottom left for the left thumb, the stick alone bottom right.
    //   C  Bottom rail: the minimap, Call and the stick in one row along the bottom (Call within
    //      reach of either thumb), the top exactly as the style guide has it.
    // Rects are GUI space (origin top left) unless the name says Screen (origin bottom left, the
    // space of touches). U is the Slipstream unit: a 402-high landscape design scaled to the screen.
    // -safe-iphone stands in for an iPhone 14's notch and home indicator on desktop renders.
    public static class HudLayout
    {
        public static HudOption Option = Parse();
        static readonly bool iphone = Flag("-safe-iphone");

        // Set by SlipstreamHud each frame it draws the mission lobby panel.
        public static bool LobbyShowing;

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static HudOption Parse()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-hud-layout");
            string v = at >= 0 && at + 1 < args.Length ? args[at + 1].ToUpperInvariant() : "A";
            return v == "B" ? HudOption.B : v == "C" ? HudOption.C : HudOption.A;
        }

        public static bool Portrait => Screen.height > Screen.width;

        public static Rect Safe
        {
            get
            {
                if (!iphone) return Screen.safeArea;
                float k = Mathf.Min(Screen.width, Screen.height) / 1170f;
                return Portrait
                    ? Rect.MinMaxRect(0f, 102f * k, Screen.width, Screen.height - 141f * k)
                    : Rect.MinMaxRect(141f * k, 63f * k, Screen.width - 141f * k, Screen.height);
            }
        }

        public static float U => Mathf.Min(Screen.height / 402f, Screen.width / 560f);
        static float Short => Mathf.Min(Screen.width, Screen.height);

        public static float Left => Safe.xMin + 22f * U;
        public static float Right => Safe.xMax - 22f * U;
        public static float Top => Screen.height - Safe.yMax + 14f * U;
        public static float Bottom => Screen.height - Safe.yMin - 20f * U;
        public static bool Narrow => Right - Left < 700f * U;

        // ---- the stick ----

        public static float StickRadius => Short * (Option == HudOption.C ? 0.11f : 0.12f);
        static float StickMargin => Short * (Option == HudOption.C ? 0.055f : 0.07f);

        public static Vector2 StickScreen => new Vector2(Safe.xMax - StickMargin - StickRadius, Safe.yMin + StickMargin + StickRadius);

        // A touch that starts this close to the stick's centre grabs it.
        public static bool InStick(Vector2 screen) => (screen - StickScreen).magnitude < StickRadius * 1.6f;

        static float StickTopGui => Screen.height - StickScreen.y - StickRadius;
        static float StickRightGui => StickScreen.x + StickRadius;

        // ---- the minimap (screen space) ----

        // About 1.4x the old 50 U circle (Sudhir), 1.3x in the compact option; a tap opens the full map.
        public static float MinimapRadius => (Option == HudOption.C ? 65f : 70f) * U;

        // Touches that start on the minimap are its own (a tap opens the full map): never a pan,
        // a jump or a stick grab.
        public static bool InMinimap(Vector2 screen) => (screen - MinimapScreen).magnitude < MinimapRadius + 6f * U;

        public static Vector2 MinimapScreen
        {
            get
            {
                float r = MinimapRadius;
                if (Option == HudOption.B)
                    return new Vector2(Safe.xMax - 22f * U - r, Screen.height - PauseBottomGui - 18f * U - r);
                return new Vector2(Safe.xMin + 22f * U + r, Mathf.Max(Safe.yMin + 18f * U + r, StickScreen.y));
            }
        }

        // B puts the map under pause, so its distance readout goes under the rim instead of over it.
        public static bool MinimapReadoutBelow => Option == HudOption.B;

        static float MinimapBottomGui => Screen.height - MinimapScreen.y + MinimapRadius;
        static float MinimapTopGui => Screen.height - MinimapScreen.y - MinimapRadius - 16f * U;
        static float MinimapRightGui => MinimapScreen.x + MinimapRadius + 8f * U;

        // ---- pause (Hud.cs scale) ----

        public static Rect PauseScreen
        {
            get
            {
                float s = Short / 720f;
                float w = 74f * s, h = 50f * s, m = 22f * s;
                return new Rect(Safe.xMax - m - w, Safe.yMax - m - h, w, h);
            }
        }

        static float PauseBottomGui => Screen.height - PauseScreen.y;

        // ---- the top band: news and the Operator's line share one row, the arrow sits under it ----

        public static float Band
        {
            get
            {
                if (!Narrow) return Top + 4f * U;
                return Option == HudOption.B ? Mathf.Max(Top + 132f * U, MinimapBottomGui + 30f * U) : Top + 76f * U;
            }
        }

        // Kept clear for CROS-30's 3D direction arrow (#52): centred, up to 0.36 of the width, its
        // top edge under the top row (row 88 U, 116 U in portrait). Where the news row drops lower
        // (B in portrait) the arrow has to sit under it instead.
        public static Rect ArrowBand
        {
            get
            {
                float top = Top + (Narrow ? 108f : 80f) * U;
                if (Narrow && Option == HudOption.B) top = Band + 46f * U;
                return new Rect(Screen.width * 0.32f, top, Screen.width * 0.36f, (Narrow ? 106f : 60f) * U);
            }
        }

        // ---- race corners ----

        // Right edge and top of the meters-to-YC block.
        static bool Stacked => Option == HudOption.B;

        public static Vector2 Meters => Stacked ? new Vector2(Left + 205f * U, Top + 72f * U) : new Vector2(Right - 64f * U, Top);

        public static float ResultsTop => Option == HudOption.B ? MinimapBottomGui + 30f * U : PauseBottomGui + 12f * U;

        // The mission lobby panel (CROS-31: mission plate, name tabs, waiting or countdown) while
        // you stand in the ring: top left, under nothing (place and meters only show mid-race).
        public static Vector2 LobbyPanel => new Vector2(Left, Top + 2f * U);

        // Effect countdown chips: under the top-left stack, or under the map in B.
        public static Vector2 Chips => Option == HudOption.B
            ? new Vector2(Right - 200f * U, MinimapBottomGui + 30f * U)
            : new Vector2(Left, Top + (Stacked ? 140f : Narrow ? 124f : 92f) * U);

        // ---- Call (the Operator's hold-to-talk plate) and Ready ----

        // Call, at its live width: above the stick (A), bottom left for the left thumb so you can
        // talk and steer at once (B), or on the bottom rail between the minimap and the stick (C).
        // Never inside the stick's grab circle.
        public static Rect Call(float w, float h)
        {
            float rail = Mathf.Min(Bottom, Screen.height - StickScreen.y + StickRadius);
            switch (Option)
            {
                case HudOption.B:
                    return new Rect(Left + 8f * U, Bottom - h, w, h);
                case HudOption.C:
                    return new Rect(Mathf.Min(MinimapRightGui + 10f * U, StickScreen.x - StickRadius * 1.6f - 12f * U - w), rail - h, w, h);
                default:
                    return new Rect(StickRightGui - w - 6f * U, StickTopGui - StickRadius * 0.6f - 12f * U - h, w, h);
            }
        }

        // Ready (results only, until CROS-31 drops it) takes Call's spot; Call then lifts above it.
        public static Rect Ready(float w, float h) => Call(w, h);

        public static Rect Call(float w, float h, bool readyShowing)
        {
            var r = Call(w, h);
            if (readyShowing) r.y -= 62f * U + 22f * U;
            return r;
        }

        public static bool CallFromLeft => Option != HudOption.A;

        // The transcript bar, attached to the top of the Call plate.
        public static Rect Hearing(float w, float h)
        {
            var call = Call(140f * U, 60f * U);
            float x = Option == HudOption.A ? Mathf.Max(Left, call.xMax - w) : call.x;
            return new Rect(x, call.y - 10f * U - h, w, h);
        }

        // ---- the rest ----

        public static Rect Perf(float w, float h) => Option == HudOption.B
            ? new Rect(Right - w, ResultsTop + 120f * U, w, h)
            : new Rect(Right - w, Top + 70f * U, w, h);
    }
}
