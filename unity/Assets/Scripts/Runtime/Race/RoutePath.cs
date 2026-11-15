using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Race
{
    // A route polyline on the ground plane with distances along it: projection of a position onto
    // the route (the contract's "progress"), and points and directions at a distance.
    public sealed class RoutePath
    {
        public readonly Vector2[] Points;
        public readonly float[] Distance;
        public float Length => Distance[Distance.Length - 1];
        public Vector2 Start => Points[0];
        public Vector2 End => Points[Points.Length - 1];

        public RoutePath(IReadOnlyList<Vector2> points)
        {
            Points = new Vector2[points.Count];
            Distance = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Points[i] = points[i];
                Distance[i] = i == 0 ? 0f : Distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
        }

        public static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        // Progress of p in meters from the start. With a hint, only the stretch within window
        // meters of it is searched, so a route that passes near itself never jumps ahead.
        public float Project(Vector2 p, float hint = -1f, float window = 0f)
        {
            float best = float.MaxValue;
            float progress = 0f;
            for (int i = 0; i < Points.Length - 1; i++)
            {
                if (hint >= 0f && (Distance[i + 1] < hint - window || Distance[i] > hint + window)) continue;
                var a = Points[i];
                var b = Points[i + 1];
                var ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float d = (a + ab * t - p).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                progress = Distance[i] + Mathf.Sqrt(len2) * t;
            }
            return best == float.MaxValue ? Project(p) : progress;
        }

        int Segment(float d)
        {
            if (d <= 0f) return 0;
            for (int i = 0; i < Points.Length - 1; i++)
                if (d <= Distance[i + 1]) return i;
            return Points.Length - 2;
        }

        public Vector2 PointAt(float d)
        {
            int i = Segment(d);
            float len = Distance[i + 1] - Distance[i];
            float t = len < 1e-6f ? 0f : Mathf.Clamp01((d - Distance[i]) / len);
            return Vector2.Lerp(Points[i], Points[i + 1], t);
        }

        public Vector2 DirectionAt(float d)
        {
            int i = Segment(d);
            return (Points[i + 1] - Points[i]).normalized;
        }

        public Vector2 RightAt(float d)
        {
            var dir = DirectionAt(d);
            return new Vector2(dir.y, -dir.x);
        }

        // Largest heading change (degrees) between from and from + ahead along the route.
        public float TurnAhead(float from, float ahead)
        {
            var start = DirectionAt(from);
            float worst = 0f;
            for (float d = from + 4f; d <= from + ahead; d += 4f)
                worst = Mathf.Max(worst, Vector2.Angle(start, DirectionAt(d)));
            return worst;
        }

        public static float Heading(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
    }
}
