using UnityEngine;

namespace Dogpatch.Slipstream.V3
{
    public enum CallLook { Rest, Ringing, Pressed, Live, OutOfAsks }

    public enum ReadyLook { Rest, Pressed, Locked }

    // HUD v3 button family (board 3): flat leaning plates with an ink drop, glyph-dominant, no slabs.
    // Every method draws only; hit-testing stays with the caller. u is the HUD's points-to-pixels scale.
    public static class HudPlates
    {
        public static readonly Color Ink = new Color32(0x1C, 0x23, 0x47, 0xFF);
        public static readonly Color Sun = new Color32(0xFF, 0xCB, 0x1F, 0xFF);
        public static readonly Color Pink = new Color32(0xFF, 0x3E, 0x8E, 0xFF);
        public static readonly Color White = Color.white;
        public static readonly Color Paper = new Color32(0xF7, 0xF8, 0xF9, 0xFF);

        static GUIStyle style;

        // A flat plate leaned forward by lean degrees about its centre.
        public static void Plate(Rect r, Color color, float lean = 14f)
        {
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -Mathf.Tan(lean * Mathf.Deg2Rad);
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            var was = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
            GUI.matrix = m;
        }

        // A plate with an outline of width edge drawn around it.
        public static void Edged(Rect r, Color fill, Color edge, float width, float lean = 14f)
        {
            Plate(Grow(r, width), edge, lean);
            Plate(r, fill, lean);
        }

        // Only the outline of a leaning plate.
        public static void Frame(Rect r, Color color, float width, float lean = 14f)
        {
            float k = Mathf.Tan(lean * Mathf.Deg2Rad);
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -k;
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            var was = GUI.color;
            GUI.color = color;
            var t = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, width), t);
            GUI.DrawTexture(new Rect(r.x, r.yMax - width, r.width, width), t);
            GUI.DrawTexture(new Rect(r.x, r.y, width, r.height), t);
            GUI.DrawTexture(new Rect(r.xMax - width, r.y, width, r.height), t);
            GUI.color = was;
            GUI.matrix = m;
        }

        public static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);

        // Pressed plates sit +3/+3 and shrink to 0.94 (board 3).
        public static Rect Pressed(Rect r, float u, float scale = 0.94f) =>
            new Rect(r.center.x - r.width * scale * 0.5f + 3f * u, r.center.y - r.height * scale * 0.5f + 3f * u, r.width * scale, r.height * scale);

        static Rect Glyph(Vector2 centre, float size) => new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size);

        public static void Label(Rect r, string text, float points, Color color, float u, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            if (style == null) style = new GUIStyle { font = Resources.Load<Font>("Fonts/Exo2-BlackItalic"), clipping = TextClipping.Overflow };
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(points * u));
            style.alignment = anchor;
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        // The Call plate, 96 x 64 at rest (lean 14), 150 wide while live. asks below zero hides the badge;
        // voice is 0..1 for the six bars; ringingSince is seconds since the phone started ringing.
        public static void Call(Rect r, CallLook look, int asks, float u, float voice = 1f, float ringingSince = 0f)
        {
            float now = Time.unscaledTime;
            if (look == CallLook.Ringing)
            {
                float p = Mathf.Repeat(ringingSince, 1.2f) / 1.2f;
                var echo = new Rect(r.center.x - r.width * (1f + 0.12f * p) * 0.5f, r.center.y - r.height * (1f + 0.12f * p) * 0.5f, r.width * (1f + 0.12f * p), r.height * (1f + 0.12f * p));
                Frame(echo, new Color(Pink.r, Pink.g, Pink.b, 1f - p), 2.5f * u);
            }

            float glyph = r.height * 0.66f;
            switch (look)
            {
                case CallLook.Pressed:
                    var pr = Pressed(r, u);
                    Edged(pr, White, Pink, 2.5f * u);
                    HudIcons.Draw("call", Glyph(pr.center, glyph * 0.94f), Pink);
                    break;
                case CallLook.Live:
                    Plate(new Rect(r.x + 4f * u, r.y + 4f * u, r.width, r.height), Ink);
                    Plate(r, Pink);
                    var mic = Glyph(new Vector2(r.x + r.height * 0.55f, r.center.y), glyph);
                    HudIcons.Draw("talk", mic, White);
                    float barW = 6.5f * u, gap = 6f * u;
                    for (int i = 0; i < 6; i++)
                    {
                        float h = (0.3f + 0.7f * Mathf.Abs(Mathf.Sin(now * (7f + i * 1.7f) + i * 1.3f))) * Mathf.Clamp01(voice);
                        float bh = Mathf.Max(4f * u, h * r.height * 0.56f);
                        Plate(new Rect(mic.xMax + 4f * u + i * (barW + gap), r.center.y - bh * 0.5f, barW, bh), White, 8f);
                    }
                    break;
                case CallLook.OutOfAsks:
                    Plate(new Rect(r.x + 4f * u, r.y + 4f * u, r.width, r.height), new Color(0f, 0f, 0f, 0.25f));
                    Edged(r, Ink, Ink, 0f);
                    HudIcons.Draw("out-of-asks", Glyph(new Vector2(r.center.x, r.center.y - r.height * 0.08f), glyph * 0.9f), Pink);
                    float segW = (r.width - 22f * u) / 8f;
                    for (int i = 0; i < 8; i++)
                        Plate(new Rect(r.x + 11f * u + i * segW, r.yMax - 11f * u, segW - 2f * u, 4f * u), new Color(Paper.r, Paper.g, Paper.b, 0.35f), 14f);
                    break;
                default:
                    Plate(new Rect(r.x + 4f * u, r.y + 4f * u, r.width, r.height), Ink);
                    Plate(r, Pink);
                    HudIcons.Draw("call", Glyph(r.center, glyph), White);
                    break;
            }

            if (asks >= 0 && look != CallLook.Live)
            {
                var badge = new Rect(r.xMax - 18f * u, r.y - 11f * u, 26f * u, 22f * u);
                Edged(badge, asks > 0 ? Sun : Paper, Ink, 2f * u);
                Label(badge, asks.ToString(), 15f, Ink, u);
            }
        }

        // The Ready plate, 112 x 64 (lean 14) with a 48-point glyph. Locked shows the ready count.
        public static void Ready(Rect r, ReadyLook look, float u, int ready = 0, int total = 0)
        {
            float glyph = Mathf.Min(48f * u, r.height * 0.75f);
            switch (look)
            {
                case ReadyLook.Pressed:
                    var pr = Pressed(r, u);
                    Edged(pr, White, Ink, 3f * u);
                    HudIcons.Draw("ready", Glyph(pr.center, glyph * 0.94f), Ink);
                    break;
                case ReadyLook.Locked:
                    Plate(new Rect(r.x + 4f * u, r.y + 4f * u, r.width, r.height), Sun);
                    Plate(r, Ink);
                    if (total > 0)
                    {
                        HudIcons.Draw("confirm", Glyph(new Vector2(r.x + r.width * 0.3f, r.center.y), glyph * 0.72f), Sun);
                        Label(new Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f, r.height), ready + "/" + total, 26f, Sun, u);
                    }
                    else HudIcons.Draw("confirm", Glyph(r.center, glyph * 0.8f), Sun);
                    break;
                default:
                    Plate(new Rect(r.x + 4f * u, r.y + 4f * u, r.width, r.height), Ink);
                    Plate(r, Sun);
                    HudIcons.Draw("ready", Glyph(r.center, glyph), Ink);
                    break;
            }
        }

        // Secondary plate, 84 x 56 (lean 12), white glyph on ink; pressed turns white with an ink outline.
        public static void Secondary(Rect r, string icon, bool pressed, float u)
        {
            float glyph = Mathf.Min(38f * u, r.height * 0.7f);
            if (pressed)
            {
                var pr = Pressed(r, u);
                Edged(pr, White, Ink, 3f * u, 12f);
                HudIcons.Draw(icon, Glyph(pr.center, glyph), Ink);
                return;
            }
            Plate(new Rect(r.x + 3f * u, r.y + 3f * u, r.width, r.height), new Color(0f, 0f, 0f, 0.25f), 12f);
            Plate(r, Ink, 12f);
            HudIcons.Draw(icon, Glyph(r.center, glyph), White);
        }

        // Small icon plate, 40 x 36 visible (48 x 48 hit), lean 8: paper fill, 2.5 ink outline, 26-point
        // glyph; pressed fills sunshine, +2/+2 and 0.94.
        public static void Small(Rect r, string icon, bool pressed, float u)
        {
            var plate = new Rect(r.center.x - 20f * u, r.center.y - 18f * u, 40f * u, 36f * u);
            if (pressed) plate = new Rect(plate.center.x - plate.width * 0.47f + 2f * u, plate.center.y - plate.height * 0.47f + 2f * u, plate.width * 0.94f, plate.height * 0.94f);
            Edged(plate, pressed ? Sun : Paper, Ink, 2.5f * u, 8f);
            HudIcons.Draw(icon, Glyph(plate.center, 26f * u * (pressed ? 0.94f : 1f)), Ink);
        }
    }
}
