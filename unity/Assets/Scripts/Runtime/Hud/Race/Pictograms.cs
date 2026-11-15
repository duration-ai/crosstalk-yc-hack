using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The guide's pictograms, drawn at startup into white alpha textures from strokes and fills on a
    // 64-unit grid: one stroke weight, leaned slightly forward, no text inside. Tint with GUI.color.
    public static class Pictograms
    {
        const int Size = 128;
        const float Lean = 0.13f;
        const float Stroke = 3.4f;

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        abstract class Shape
        {
            public abstract float Distance(Vector2 p);
        }

        sealed class Seg : Shape
        {
            readonly Vector2 a, b;
            readonly float w;
            public Seg(float ax, float ay, float bx, float by, float width = Stroke) { a = new Vector2(ax, ay); b = new Vector2(bx, by); w = width; }

            public override float Distance(Vector2 p)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                return Vector2.Distance(p, a + ab * t) - w;
            }
        }

        sealed class Arc : Shape
        {
            readonly Vector2 c;
            readonly float r, a0, a1, w;
            public Arc(float cx, float cy, float radius, float from, float to, float width = Stroke) { c = new Vector2(cx, cy); r = radius; a0 = from; a1 = to; w = width; }

            public override float Distance(Vector2 p)
            {
                var d = p - c;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float mid = (a0 + a1) * 0.5f;
                float half = (a1 - a0) * 0.5f;
                float delta = Mathf.DeltaAngle(mid, ang);
                if (Mathf.Abs(delta) <= half) return Mathf.Abs(d.magnitude - r) - w;
                var e0 = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
                var e1 = c + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
                return Mathf.Min(Vector2.Distance(p, e0), Vector2.Distance(p, e1)) - w;
            }
        }

        sealed class Fill : Shape
        {
            readonly Vector2[] pts;
            public Fill(params float[] xy)
            {
                pts = new Vector2[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            }

            public override float Distance(Vector2 p)
            {
                bool inside = false;
                float best = float.MaxValue;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    var a = pts[j];
                    var b = pts[i];
                    if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
                return inside ? -best : best;
            }
        }

        sealed class Dot : Shape
        {
            readonly Vector2 c;
            readonly float r;
            public Dot(float cx, float cy, float radius) { c = new Vector2(cx, cy); r = radius; }
            public override float Distance(Vector2 p) => Vector2.Distance(p, c) - r;
        }

        static Shape[] Box(float x0, float y0, float x1, float y1, float w = Stroke) => new Shape[]
        {
            new Seg(x0, y0, x1, y0, w), new Seg(x1, y0, x1, y1, w), new Seg(x1, y1, x0, y1, w), new Seg(x0, y1, x0, y0, w),
        };

        static Shape[] Cloud() => new Shape[]
        {
            new Arc(23, 31, 8, 180, 300), new Arc(37, 29, 11, 225, 360), new Seg(15, 31, 15, 39), new Seg(48, 29, 48, 39), new Seg(15, 39, 48, 39),
        };

        static Shape[] Shapes(string name)
        {
            var s = new List<Shape>();
            switch (name)
            {
                case "call":
                    s.Add(new Arc(32, 36, 17, 180, 360, 4.2f));
                    s.Add(new Fill(10, 30, 22, 30, 22, 40, 10, 40));
                    s.Add(new Fill(42, 30, 54, 30, 54, 40, 42, 40));
                    s.Add(new Fill(20, 40, 44, 40, 50, 54, 14, 54));
                    break;
                case "cellphone":
                    s.AddRange(Box(19, 6, 45, 58, 4.4f));
                    s.Add(new Fill(26, 15, 38, 15, 38, 43, 26, 43));
                    s.Add(new Seg(29, 10.5f, 35, 10.5f, 1.4f));
                    s.Add(new Dot(32, 50.5f, 3f));
                    break;
                case "ready":
                    s.Add(new Seg(13, 33, 26, 46, 4.2f));
                    s.Add(new Seg(26, 46, 51, 19, 4.2f));
                    break;
                case "pause":
                    s.Add(new Fill(18, 14, 28, 14, 28, 50, 18, 50));
                    s.Add(new Fill(36, 14, 46, 14, 46, 50, 36, 50));
                    break;
                case "yc":
                    s.Add(new Seg(17, 12, 17, 55, 3.6f));
                    s.AddRange(Box(17, 12, 51, 36, 3f));
                    s.Add(new Fill(17, 12, 34, 12, 34, 24, 17, 24));
                    s.Add(new Fill(34, 24, 51, 24, 51, 36, 34, 36));
                    break;
                case "gravity":
                    s.AddRange(Box(22, 9, 42, 24, 3.2f));
                    s.Add(new Seg(23, 41, 32, 33));
                    s.Add(new Seg(32, 33, 41, 41));
                    s.Add(new Seg(12, 51, 52, 51));
                    break;
                case "ice":
                    s.Add(new Seg(32, 10, 32, 54));
                    s.Add(new Seg(13, 21, 51, 43));
                    s.Add(new Seg(13, 43, 51, 21));
                    break;
                case "fog":
                    s.Add(new Seg(12, 22, 46, 22));
                    s.Add(new Seg(18, 32, 54, 32));
                    s.Add(new Seg(12, 42, 42, 42));
                    break;
                case "time":
                    s.Add(new Arc(32, 46, 11, 180, 360));
                    s.Add(new Seg(9, 47, 55, 47));
                    s.Add(new Seg(32, 16, 32, 24));
                    s.Add(new Seg(15, 26, 20, 31));
                    s.Add(new Seg(49, 26, 44, 31));
                    break;
                case "traffic":
                    s.Add(new Seg(8, 41, 8, 32));
                    s.Add(new Seg(8, 32, 19, 30));
                    s.Add(new Seg(19, 30, 26, 21));
                    s.Add(new Seg(26, 21, 42, 21));
                    s.Add(new Seg(42, 21, 48, 30));
                    s.Add(new Seg(48, 30, 56, 32));
                    s.Add(new Seg(56, 32, 56, 41));
                    s.Add(new Seg(8, 41, 56, 41));
                    s.Add(new Dot(19, 43, 5.5f));
                    s.Add(new Dot(45, 43, 5.5f));
                    break;
                case "recolor":
                    s.Add(new Arc(32, 39, 13, -25, 205));
                    s.Add(new Seg(20.2f, 33.5f, 32, 10));
                    s.Add(new Seg(43.8f, 33.5f, 32, 10));
                    break;
                case "swap":
                    s.Add(new Seg(12, 23, 50, 23));
                    s.Add(new Seg(40, 13, 50, 23));
                    s.Add(new Seg(40, 33, 50, 23));
                    s.Add(new Seg(52, 41, 14, 41));
                    s.Add(new Seg(24, 31, 14, 41));
                    s.Add(new Seg(24, 51, 14, 41));
                    break;
                case "resize":
                    s.Add(new Seg(16, 48, 48, 16));
                    s.Add(new Seg(33, 15, 49, 15));
                    s.Add(new Seg(49, 15, 49, 31));
                    s.Add(new Seg(15, 33, 15, 49));
                    s.Add(new Seg(15, 49, 31, 49));
                    break;
                case "ramp":
                    s.Add(new Fill(8, 53, 56, 53, 56, 33));
                    s.Add(new Arc(40, 38, 26, 200, 268));
                    s.Add(new Seg(39, 12, 47, 18));
                    s.Add(new Seg(39, 12, 45, 5));
                    break;
                case "rain":
                    s.AddRange(Cloud());
                    s.Add(new Seg(21, 46, 18, 55, 3f));
                    s.Add(new Seg(33, 46, 30, 55, 3f));
                    s.Add(new Seg(45, 46, 42, 55, 3f));
                    break;
                case "snow":
                    s.AddRange(Cloud());
                    s.Add(new Dot(20, 51, 3.4f));
                    s.Add(new Dot(32, 53, 3.4f));
                    s.Add(new Dot(44, 51, 3.4f));
                    break;
                case "waves":
                    s.Add(new Arc(14, 32, 12, -50, 50, 3f));
                    s.Add(new Arc(14, 32, 22, -45, 45, 3f));
                    break;
                case "dash":
                    s.Add(new Seg(20, 32, 44, 32, 4.4f));
                    break;
            }
            return s.ToArray();
        }

        public static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "call";
            if (cache.TryGetValue(name, out var tex)) return tex;
            var shapes = Shapes(name);
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "picto-" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            float unit = Size / 64f;
            for (int py = 0; py < Size; py++)
                for (int px = 0; px < Size; px++)
                {
                    float u = (px + 0.5f) / unit;
                    float v = (Size - py - 0.5f) / unit;
                    var p = new Vector2(u - (32f - v) * Lean, v);
                    float d = float.MaxValue;
                    foreach (var shape in shapes) d = Mathf.Min(d, shape.Distance(p));
                    byte a = (byte)(Mathf.Clamp01(0.5f - d * unit) * 255f);
                    pixels[py * Size + px] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            cache[name] = tex;
            return tex;
        }
    }
}
