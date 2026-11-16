using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Dogpatch.Slipstream.V3
{
    // HUD v3 pictogram family (board 3): a 64-unit grid, one 6-unit stroke with butt caps and mitred
    // joins, leaned 8 degrees forward (x' = x - 0.1405 (y - 32)), fills only for masses. Every glyph is
    // baked once into one atlas from signed distances, with a second atlas holding the 2.4-unit outline
    // used over the world. Call HudIcons.Draw(name, rect, color) from any OnGUI.
    public static class HudIcons
    {
        const int Cell = 128;
        const int Columns = 8;
        const float HalfStroke = 3f;
        const float Lean = 0.1405f;
        const float OutlineUnits = 2.4f;

        // One glyph per line, y down on the 64 grid. L polyline, P closed outline, A arc (cx cy r from to,
        // degrees clockwise from +x), O ring, F filled polygon, C filled disc; a leading '-' carves it out.
        static readonly string[][] Glyphs =
        {
            new[] { "call", "F 9 16 19 9 27 20 21 26 26 34 33 40 39 36 51 44 46 54 34 55 22 47 13 36 9 26", "A 31 33 10 -80 -10", "A 31 33 19 -80 -10" },
            new[] { "out-of-asks", "A 32 38 16 180 360", "F 6 32 22 32 22 42 6 42", "F 42 32 58 32 58 42 42 42" },
            new[] { "talk", "A 32 18 8 180 360", "L 24 18 24 28", "L 40 18 40 28", "A 32 28 8 0 180", "A 32 28 15 0 180", "L 32 43 32 53", "L 21 55 43 55" },
            new[] { "ready", "L 6 24 18 24", "L 6 34 16 34", "L 6 44 14 44", "L 22 34 32 44 56 16" },
            new[] { "confirm", "L 10 34 25 49 54 16" },
            new[] { "cancel", "O 32 32 22", "L 17 47 47 17" },
            new[] { "close", "L 13 13 51 51", "L 51 13 13 51" },
            new[] { "back", "L 54 32 13 32", "L 29 15 12 32 29 49" },
            new[] { "pause", "F 15 11 27 11 27 53 15 53", "F 37 11 49 11 49 53 37 53" },
            new[] { "recentre", "O 32 32 14", "C 32 32 5", "L 32 6 32 18", "L 32 46 32 58", "L 6 32 18 32", "L 46 32 58 32" },
            new[] { "map", "P 8 16 22 10 42 16 56 10 56 48 42 54 22 48 8 54", "L 22 10 22 48", "L 42 16 42 54" },
            new[] { "fast-travel", "A 36 23 13 135 405", "L 26.8 32.2 36 52 45.2 32.2", "C 36 23 4.5", "L 6 26 16 26", "L 9 37 19 37" },
            new[] { "finish", "L 14 58 14 9", "P 14 9 52 9 52 37 14 37", "F 14 9 26.7 9 26.7 23 14 23", "F 39.3 9 52 9 52 23 39.3 23", "F 26.7 23 39.3 23 39.3 37 26.7 37" },
            new[] { "yc", "P 10 10 54 10 54 54 10 54", "L 21 19 32 32 43 19", "L 32 32 32 45" },
            new[] { "start-ring", "P 7 47 19 41 45 41 57 47 45 53 19 53", "L 32 45 32 20", "F 21 23 32 8 43 23" },
            new[] { "you", "F 32 6 52 56 32 45 12 56" },
            new[] { "racer", "C 32 19 11", "A 32 58 21 180 360" },
            new[] { "get-in", "P 7 44 7 31 15 29 21 16 43 16 49 29 57 31 57 44", "L 21 29 43 29", "C 18 45 7", "C 46 45 7" },
            new[] { "get-out", "C 40 10 6", "L 37 21 30 35 38 43 36 55", "L 30 35 22 45 12 45", "L 35 23 22 27 17 35", "L 37 23 46 31 53 27" },
            new[] { "jump", "L 14 31 32 13 50 31", "L 14 45 32 27 50 45", "L 10 55 54 55" },
            new[] { "shove", "L 6 32 38 32", "L 27 21 38 32 27 43", "L 46 19 52 13", "L 47 32 58 32", "L 46 45 52 51" },
            new[] { "shutter", "P 7 20 19 20 23 11 41 11 45 20 57 20 57 53 7 53", "O 32 36 9" },
            new[] { "flip-camera", "A 32 32 18 200 330", "F 42 18 56 17 52 31", "A 32 32 18 20 150", "F 22 46 8 47 12 33" },
            new[] { "skip", "L 9 16 23 32 9 48", "L 25 16 39 32 25 48", "L 50 13 50 51" },
            new[] { "retake", "A 32 34 18 -45 225", "F 38 9 54 14 44 26", "C 32 34 4.5" },
            new[] { "random-name", "P 9 9 55 9 55 55 9 55", "C 21 21 4.5", "C 32 32 4.5", "C 43 43 4.5" },
            new[] { "edit", "P 13 51 13 40 41 12 52 23 24 51", "L 35 18 46 29" },
            new[] { "settings", "O 32 32 18", "O 32 32 6", "L 50 32 58 32", "L 44.7 44.7 50.4 50.4", "L 32 50 32 58", "L 19.3 44.7 13.6 50.4", "L 14 32 6 32", "L 19.3 19.3 13.6 13.6", "L 32 14 32 6", "L 44.7 19.3 50.4 13.6" },
            new[] { "sound-on", "F 6 24 16 24 29 12 29 52 16 40 6 40", "A 30 32 11 -50 50", "A 30 32 21 -50 50" },
            new[] { "sound-off", "F 6 24 16 24 29 12 29 52 16 40 6 40", "L 38 23 56 41", "L 56 23 38 41" },
            new[] { "music", "L 24 47 24 12 50 7 50 42", "L 24 20 50 15", "C 17 47 8", "C 43 42 8" },
            new[] { "vibration", "P 22 8 42 8 42 56 22 56", "L 12 19 12 45", "L 52 19 52 45", "L 30 48 34 48" },
            new[] { "stick", "C 32 17 10", "L 32 26 32 44", "P 13 44 51 44 51 55 13 55" },
            new[] { "drag", "C 14 50 7", "C 50 14 7", "L 14 50 50 14" },
            new[] { "graphics", "P 7 23 18 11 46 11 57 23 32 54", "L 7 23 57 23", "L 25 23 32 54 39 23" },
            new[] { "time", "A 32 46 11 180 360", "L 7 47 57 47", "L 32 13 32 25", "L 13 25 20 32", "L 51 25 44 32", "L 18 56 46 56" },
            new[] { "weather", "A 23 40 9 180 300", "A 38 38 12 225 360", "L 14 40 14 49", "L 50 38 50 49", "L 14 49 50 49", "L 48 7 48 13", "L 58 18 52 20", "A 46 24 6 150 300" },
            new[] { "gravity", "F 21 7 43 7 45 23 19 23", "L 32 29 32 44", "L 23 36 32 45 41 36", "L 9 54 55 54" },
            new[] { "ice", "L 7 11 57 11", "F 11 14 23 14 17 44", "F 26 14 38 14 32 57", "F 41 14 53 14 47 38" },
            new[] { "fog", "L 9 20 45 20", "L 17 31 55 31", "L 9 42 41 42", "L 21 53 51 53" },
            new[] { "traffic", "P 21 6 43 6 43 58 21 58", "C 32 17 5", "C 32 32 5", "C 32 47 5" },
            new[] { "recolor", "A 32 40 15 -20 200", "L 17.9 34.9 32 7 46.1 34.9" },
            new[] { "swap-ride", "L 10 22 50 22", "L 40 12 51 22 40 32", "L 54 42 14 42", "L 24 32 13 42 24 52" },
            new[] { "resize", "L 13 51 51 13", "L 30 13 51 13 51 34", "L 13 30 13 51 34 51" },
            new[] { "tiny", "L 8 8 24 24", "L 13 24 24 24 24 13", "L 56 8 40 24", "L 51 24 40 24 40 13", "L 8 56 24 40", "L 13 40 24 40 24 51", "L 56 56 40 40", "L 51 40 40 40 40 51" },
            new[] { "giant", "L 26 26 9 9", "L 9 21 9 9 21 9", "L 38 26 55 9", "L 43 9 55 9 55 21", "L 26 38 9 55", "L 9 43 9 55 21 55", "L 38 38 55 55", "L 55 43 55 55 43 55" },
            new[] { "ramp", "F 7 55 57 55 57 33", "L 15 36 40 18", "L 29 16 40 18 39 30" },
            new[] { "rain", "A 23 26 9 180 300", "A 38 24 12 225 360", "L 14 26 14 35", "L 50 24 50 35", "L 14 35 50 35", "L 21 43 17 55", "L 33 43 29 55", "L 45 43 41 55" },
            new[] { "snow", "L 32 7 32 57", "L 10 19.5 54 44.5", "L 10 44.5 54 19.5" },
            new[] { "waves", "A 16 26 8 180 360", "A 32 26 8 0 180", "A 48 26 8 180 360", "A 16 42 8 180 360", "A 32 42 8 0 180", "A 48 42 8 180 360" },
            new[] { "boost", "L 5 21 15 21", "L 5 32 13 32", "L 5 43 15 43", "L 21 15 37 32 21 49", "L 39 15 55 32 39 49" },
            new[] { "night", "C 30 33 23", "-C 43 24 19", "F 49 41 51 47 57 49 51 51 49 57 47 51 41 49 47 47" },
            new[] { "explosion", "P 32 6 38 22 56 18 44 32 56 46 38 42 32 58 26 42 8 46 20 32 8 18 26 22" },
            new[] { "stop-all-cars", "O 32 32 22", "F 18 28 46 28 46 36 18 36" },
            new[] { "boost-pip", "C 32 32 10" },
        };

        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "phone", "call" }, { "cellphone", "call" }, { "go", "ready" }, { "mic", "talk" }, { "time-of-day", "time" },
            { "recenter", "recentre" }, { "goal", "yc" }, { "yc-goal", "yc" }, { "ring", "start-ring" }, { "car", "get-in" },
            { "swap", "swap-ride" }, { "dice", "random-name" }, { "camera", "shutter" }, { "dash", "boost-pip" },
        };

        static Texture2D fill;
        static Texture2D outline;
        static Dictionary<string, int> index;

        public static IEnumerable<string> Names
        {
            get { foreach (var g in Glyphs) yield return g[0]; }
        }

        public static bool Has(string name) => Resolve(name) >= 0;

        // Draws the glyph square, centred in rect, tinted by color. Unknown names draw nothing.
        public static void Draw(string name, Rect rect, Color color) => Blit(fill, name, rect, color);

        // White-on-world style from board 3: the glyph over a 2.4-unit outline (ink by default).
        public static void DrawOutlined(string name, Rect rect, Color color, Color edge)
        {
            Blit(outline, name, rect, edge);
            Blit(fill, name, rect, color);
        }

        static void Blit(Texture2D atlas, string name, Rect rect, Color color)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            int i = Resolve(name);
            if (i < 0) return;
            Build();
            float side = Mathf.Min(rect.width, rect.height);
            var r = new Rect(rect.center.x - side * 0.5f, rect.center.y - side * 0.5f, side, side);
            int rows = Mathf.CeilToInt(Glyphs.Length / (float)Columns);
            var uv = new Rect((i % Columns) / (float)Columns, 1f - (i / Columns + 1) / (float)rows, 1f / Columns, 1f / rows);
            var was = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(r, atlas, uv);
            GUI.color = was;
        }

        static int Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (index == null)
            {
                index = new Dictionary<string, int>();
                for (int i = 0; i < Glyphs.Length; i++) index[Glyphs[i][0]] = i;
            }
            if (index.TryGetValue(name, out var at)) return at;
            return Aliases.TryGetValue(name, out var alias) && index.TryGetValue(alias, out at) ? at : -1;
        }

        static void Build()
        {
            if (fill != null) return;
            int rows = Mathf.CeilToInt(Glyphs.Length / (float)Columns);
            int w = Columns * Cell, h = rows * Cell;
            var a = new Color32[w * h];
            var b = new Color32[w * h];
            float unit = Cell / 64f;
            for (int g = 0; g < Glyphs.Length; g++)
            {
                var shapes = Parse(Glyphs[g]);
                int ox = (g % Columns) * Cell;
                int oy = h - (g / Columns + 1) * Cell;
                for (int py = 0; py < Cell; py++)
                    for (int px = 0; px < Cell; px++)
                    {
                        float x = (px + 0.5f) / unit;
                        float y = (Cell - py - 0.5f) / unit;
                        float d = Distance(shapes, new Vector2(x + Lean * (y - 32f), y));
                        int at = (oy + py) * w + ox + px;
                        a[at] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d * unit) * 255f));
                        b[at] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - (d - OutlineUnits) * unit) * 255f));
                    }
            }
            fill = Upload(a, w, h, "hud-icons");
            outline = Upload(b, w, h, "hud-icons-outline");
        }

        static Texture2D Upload(Color32[] pixels, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        struct Shape
        {
            public char Kind;
            public bool Carve;
            public float[] V;
        }

        static Shape[] Parse(string[] glyph)
        {
            var shapes = new Shape[glyph.Length - 1];
            for (int i = 1; i < glyph.Length; i++)
            {
                var parts = glyph[i].Split(' ');
                bool carve = parts[0][0] == '-';
                var v = new float[parts.Length - 1];
                for (int k = 1; k < parts.Length; k++) v[k - 1] = float.Parse(parts[k], CultureInfo.InvariantCulture);
                shapes[i - 1] = new Shape { Kind = parts[0][carve ? 1 : 0], Carve = carve, V = v };
            }
            return shapes;
        }

        static float Distance(Shape[] shapes, Vector2 p)
        {
            float d = 1e5f;
            foreach (var s in shapes)
            {
                float e = One(s, p);
                d = s.Carve ? Mathf.Max(d, -e) : Mathf.Min(d, e);
            }
            return d;
        }

        static float One(Shape s, Vector2 p)
        {
            var v = s.V;
            switch (s.Kind)
            {
                case 'L': return Polyline(v, p, false);
                case 'P': return Polyline(v, p, true);
                case 'F': return Polygon(v, p);
                case 'C': return Vector2.Distance(p, new Vector2(v[0], v[1])) - v[2];
                case 'O': return Mathf.Abs(Vector2.Distance(p, new Vector2(v[0], v[1])) - v[2]) - HalfStroke;
                case 'A': return Arc(v[0], v[1], v[2], v[3], v[4], p);
            }
            return 1e5f;
        }

        // Each segment is a butt-capped bar; at a joint it runs on by half a stroke so corners close.
        static float Polyline(float[] v, Vector2 p, bool closed)
        {
            int n = v.Length / 2;
            int segs = closed ? n : n - 1;
            float d = 1e5f;
            for (int i = 0; i < segs; i++)
            {
                var a = Point(v, i, n);
                var b = Point(v, i + 1, n);
                float startExt = closed || i > 0 ? Miter(Point(v, i - 1, n), a, b) : 0f;
                float endExt = closed || i < segs - 1 ? Miter(a, b, Point(v, i + 2, n)) : 0f;
                d = Mathf.Min(d, Bar(a, b, Mathf.Max(startExt, 0f), Mathf.Max(endExt, 0f), p));
                if (endExt < 0f) d = Mathf.Min(d, Vector2.Distance(p, b) - HalfStroke);
            }
            return d;
        }

        static Vector2 Point(float[] v, int i, int n)
        {
            i = (i % n + n) % n;
            return new Vector2(v[i * 2], v[i * 2 + 1]);
        }

        // How far a bar runs past a joint so its outer corner meets the mitre point. Past a mitre limit
        // of about 2.7 it returns -1: the joint is rounded with a disc instead of growing a spike.
        static float Miter(Vector2 a, Vector2 joint, Vector2 b)
        {
            float t = Mathf.Tan(Vector2.Angle(joint - a, b - joint) * 0.5f * Mathf.Deg2Rad);
            return t > 2.5f ? -1f : HalfStroke * t;
        }

        static float Bar(Vector2 a, Vector2 b, float startExt, float endExt, Vector2 p)
        {
            var ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-4f) return Vector2.Distance(a, p) - HalfStroke;
            var dir = ab / len;
            var rel = p - a;
            float along = Vector2.Dot(rel, dir);
            float across = Mathf.Abs(dir.x * rel.y - dir.y * rel.x);
            float lo = -startExt, hi = len + endExt;
            float mid = (lo + hi) * 0.5f, half = (hi - lo) * 0.5f;
            var q = new Vector2(Mathf.Abs(along - mid) - half, across - HalfStroke);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        static float Arc(float cx, float cy, float r, float from, float to, Vector2 p)
        {
            var d = p - new Vector2(cx, cy);
            float dist = d.magnitude;
            float ring = Mathf.Abs(dist - r) - HalfStroke;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float mid = (from + to) * 0.5f, half = (to - from) * 0.5f;
            float over = Mathf.Abs(Mathf.DeltaAngle(mid, ang)) - half;
            if (over <= 0f) return ring;
            float cap = dist * Mathf.Sin(Mathf.Min(over, 90f) * Mathf.Deg2Rad);
            return Mathf.Max(ring, cap);
        }

        static float Polygon(float[] v, Vector2 p)
        {
            int n = v.Length / 2;
            bool inside = false;
            float best = 1e5f;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = new Vector2(v[j * 2], v[j * 2 + 1]);
                var b = new Vector2(v[i * 2], v[i * 2 + 1]);
                if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside ? -best : best;
        }
    }
}
