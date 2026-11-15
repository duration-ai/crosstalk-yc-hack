using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    public static class Geometry
    {
        public static Vector2[] ToPoints(float[] flat)
        {
            var pts = new Vector2[flat.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(flat[i * 2], flat[i * 2 + 1]);
            return pts;
        }

        public static float SignedArea(Vector2[] poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                a += poly[j].x * poly[i].y - poly[i].x * poly[j].y;
            return a * 0.5f;
        }

        public static Vector2 Centroid(Vector2[] poly)
        {
            float area = 0f, cx = 0f, cy = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float cross = poly[j].x * poly[i].y - poly[i].x * poly[j].y;
                area += cross;
                cx += (poly[j].x + poly[i].x) * cross;
                cy += (poly[j].y + poly[i].y) * cross;
            }
            if (Mathf.Abs(area) < 1e-4f)
            {
                var sum = Vector2.zero;
                foreach (var p in poly) sum += p;
                return sum / poly.Length;
            }
            return new Vector2(cx / (3f * area), cy / (3f * area));
        }

        public static bool Contains(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        public static Rect BoundsOf(Vector2[] poly)
        {
            var min = poly[0];
            var max = poly[0];
            foreach (var p in poly)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Ear clipping. Returns index triples; winding follows the input polygon's orientation.
        public static List<int> Triangulate(Vector2[] poly)
        {
            var result = new List<int>();
            int n = poly.Length;
            if (n < 3) return result;
            var idx = new List<int>(n);
            bool ccw = SignedArea(poly) > 0f;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

            int guard = n * n;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count];
                    int b = idx[i];
                    int c = idx[(i + 1) % idx.Count];
                    if (Cross(poly[a], poly[b], poly[c]) <= 1e-6f) continue;
                    bool ear = true;
                    for (int k = 0; k < idx.Count && ear; k++)
                    {
                        int p = idx[k];
                        if (p == a || p == b || p == c) continue;
                        if (InTriangle(poly[p], poly[a], poly[b], poly[c])) ear = false;
                    }
                    if (!ear) continue;
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3)
            {
                result.Add(idx[0]);
                result.Add(idx[1]);
                result.Add(idx[2]);
            }
            return result;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f;

        public static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return a + ab * t;
        }
    }
}
