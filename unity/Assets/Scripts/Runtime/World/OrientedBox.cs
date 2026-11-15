using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    // Minimum-area rectangle around a footprint (rotating calipers over its convex hull). Axis is a
    // unit vector along one side; ExtentAxis and ExtentNormal are half sizes along Axis and its normal.
    public struct OrientedBox
    {
        public Vector2 Center;
        public Vector2 Axis;
        public float ExtentAxis;
        public float ExtentNormal;

        public Vector2 Normal => new Vector2(-Axis.y, Axis.x);
        public float Area => 4f * ExtentAxis * ExtentNormal;

        public static OrientedBox Of(Vector2[] poly)
        {
            var hull = Hull(poly);
            var best = new OrientedBox { Center = poly[0], Axis = Vector2.right };
            float bestArea = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                var edge = hull[(i + 1) % hull.Count] - hull[i];
                if (edge.sqrMagnitude < 1e-6f) continue;
                var u = edge.normalized;
                var v = new Vector2(-u.y, u.x);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (var p in hull)
                {
                    float a = Vector2.Dot(p, u), b = Vector2.Dot(p, v);
                    minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a);
                    minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b);
                }
                float area = (maxU - minU) * (maxV - minV);
                if (area >= bestArea) continue;
                bestArea = area;
                best = new OrientedBox
                {
                    Center = u * (minU + maxU) * 0.5f + v * (minV + maxV) * 0.5f,
                    Axis = u,
                    ExtentAxis = (maxU - minU) * 0.5f,
                    ExtentNormal = (maxV - minV) * 0.5f,
                };
            }
            return best;
        }

        static List<Vector2> Hull(Vector2[] poly)
        {
            var pts = new List<Vector2>(poly);
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (var p in pts)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                pts.Reverse();
            }
            return hull.Count >= 3 ? hull : new List<Vector2>(poly);
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
