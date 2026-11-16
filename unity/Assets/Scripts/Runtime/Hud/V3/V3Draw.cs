using UnityEngine;

namespace Dogpatch.HudV3
{
    // Shared IMGUI primitives for the HUD v3 boards: P is one board point (the board is a 402 pt
    // wide portrait phone, so P is the short screen side over 402), panels lean under a shear, and
    // text is Exo 2 Black Italic with an optional ink outline.
    public static class V3Draw
    {
        public static readonly Color Ink = Hex("#1C2347");
        public static readonly Color Sun = Hex("#FFCB1F");
        public static readonly Color Pink = Hex("#FF3E8E");
        public static readonly Color White = Color.white;

        static Font font;
        static GUIStyle style;

        public static float P => Mathf.Min(Screen.width, Screen.height) / 402f;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        public static void Panel(Rect r, Color color, float leanPt = 9f)
        {
            float lean = r.height > 0f ? leanPt * P / r.height : 0f;
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -lean;
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        public static void Icon(Rect r, string name, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Slipstream.Pictograms.Get(name), ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        public static GUIStyle Style(float pt, TextAnchor anchor)
        {
            if (font == null) font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, richText = false, padding = new RectOffset() };
            style.font = font;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(pt * P));
            style.alignment = anchor;
            return style;
        }

        public static Vector2 Measure(string text, float pt) => Style(pt, TextAnchor.UpperLeft).CalcSize(Gameplay.HudLayout.Text(text));

        // IMGUI text is linearized once more than textures in a linear-space build.
        static Color TextColor(Color c, float alpha)
        {
            var g = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
            return new Color(g.r, g.g, g.b, c.a * alpha);
        }

        public static void Text(Rect r, string text, float pt, Color fill, TextAnchor anchor = TextAnchor.MiddleLeft, float outlinePt = 0f, float alpha = 1f)
        {
            var st = Style(pt, anchor);
            if (outlinePt > 0f)
            {
                float o = outlinePt * P;
                st.normal.textColor = TextColor(Ink, alpha);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), text, st);
                }
            }
            st.normal.textColor = TextColor(fill, alpha);
            GUI.Label(r, text, st);
        }

        public static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        public static float Overshoot(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * (3.2f * t + 2.2f);
        }

        // Segments along a row, the first lit share filled.
        public static void Segments(Rect r, int count, float lit, Color on, Color off, float gapPt = 2f)
        {
            float gap = gapPt * P;
            float w = (r.width - gap * (count - 1)) / count;
            int n = Mathf.Clamp(Mathf.RoundToInt(lit * count), 0, count);
            for (int i = 0; i < count; i++)
                Panel(new Rect(r.x + i * (w + gap), r.y, w, r.height), i < n ? on : off, 2f);
        }
    }
}
