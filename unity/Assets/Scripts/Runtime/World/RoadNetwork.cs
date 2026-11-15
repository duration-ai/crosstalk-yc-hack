using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // Spatial lookup over the street network: nearest street (for the HUD and spawning),
    // intersection detection, and per-road polylines in local metres.
    public sealed partial class RoadNetwork
    {
        const float Cell = 40f;

        public struct Hit
        {
            public int Road;
            public Vector2 Point;
            public Vector2 Direction;
            public float Distance;
        }

        struct Segment
        {
            public int Road;
            public Vector2 A;
            public Vector2 B;
        }

        public readonly Road[] Roads;
        public readonly Vector2[][] Points;
        public readonly List<Vector2> Intersections = new List<Vector2>();

        readonly List<Segment> segments = new List<Segment>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
        readonly Dictionary<Vector2Int, List<Vector2>> intersectionGrid = new Dictionary<Vector2Int, List<Vector2>>();

        public RoadNetwork(Road[] roads)
        {
            Roads = roads;
            Points = new Vector2[roads.Length][];
            var counts = new Dictionary<Vector2Int, int>();
            for (int r = 0; r < roads.Length; r++)
            {
                var pts = Geometry.ToPoints(roads[r].pts);
                Points[r] = pts;
                if (!IsDriveable(roads[r])) continue;
                var seen = new HashSet<Vector2Int>();
                foreach (var p in pts)
                {
                    var key = Vector2Int.RoundToInt(p * 2f);
                    if (!seen.Add(key)) continue;
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;
                }
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    int index = segments.Count;
                    segments.Add(new Segment { Road = r, A = pts[i], B = pts[i + 1] });
                    var min = Vector2.Min(pts[i], pts[i + 1]);
                    var max = Vector2.Max(pts[i], pts[i + 1]);
                    for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                        for (int z = Mathf.FloorToInt(min.y / Cell); z <= Mathf.FloorToInt(max.y / Cell); z++)
                        {
                            var cell = new Vector2Int(x, z);
                            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                            list.Add(index);
                        }
                }
            }
            foreach (var pair in counts)
            {
                if (pair.Value < 2) continue;
                var p = (Vector2)pair.Key * 0.5f;
                Intersections.Add(p);
                var cell = CellOf(p);
                if (!intersectionGrid.TryGetValue(cell, out var list)) intersectionGrid[cell] = list = new List<Vector2>();
                list.Add(p);
            }
        }

        public static bool IsDriveable(Road road) => road.kind != "path";

        public static float HalfWidth(Road road) => Mathf.Max(road.width, 2f) * 0.5f;

        static Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));

        public bool Nearest(Vector2 p, float maxDistance, out Hit hit, int excludeRoad = -1)
        {
            hit = new Hit { Road = -1, Distance = float.MaxValue };
            int reach = Mathf.CeilToInt(maxDistance / Cell);
            var c = CellOf(p);
            for (int x = c.x - reach; x <= c.x + reach; x++)
                for (int z = c.y - reach; z <= c.y + reach; z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int s in list)
                    {
                        var seg = segments[s];
                        if (seg.Road == excludeRoad) continue;
                        var q = Geometry.ClosestPointOnSegment(p, seg.A, seg.B, out _);
                        float d = Vector2.Distance(p, q) - HalfWidth(Roads[seg.Road]);
                        if (d >= hit.Distance) continue;
                        hit = new Hit { Road = seg.Road, Point = q, Direction = (seg.B - seg.A).normalized, Distance = d };
                    }
                }
            return hit.Road >= 0 && hit.Distance <= maxDistance;
        }

        public float DistanceToIntersection(Vector2 p, float maxDistance = 60f)
        {
            float best = maxDistance;
            var c = CellOf(p);
            int reach = Mathf.CeilToInt(maxDistance / Cell);
            for (int x = c.x - reach; x <= c.x + reach; x++)
                for (int z = c.y - reach; z <= c.y + reach; z++)
                {
                    if (!intersectionGrid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (var q in list) best = Mathf.Min(best, Vector2.Distance(p, q));
                }
            return best;
        }

        public string StreetNameAt(Vector2 p)
        {
            if (!Nearest(p, 25f, out var hit)) return "";
            return Roads[hit.Road].name;
        }
    }

    // Point-in-building queries so props and spawns never land inside a footprint.
    public sealed class BuildingIndex
    {
        const float Cell = 50f;
        readonly List<Vector2[]> polygons = new List<Vector2[]>();
        readonly List<Rect> bounds = new List<Rect>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();

        public void Add(Vector2[] poly)
        {
            int index = polygons.Count;
            var rect = Geometry.BoundsOf(poly);
            polygons.Add(poly);
            bounds.Add(rect);
            for (int x = Mathf.FloorToInt(rect.xMin / Cell); x <= Mathf.FloorToInt(rect.xMax / Cell); x++)
                for (int z = Mathf.FloorToInt(rect.yMin / Cell); z <= Mathf.FloorToInt(rect.yMax / Cell); z++)
                {
                    var cell = new Vector2Int(x, z);
                    if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                    list.Add(index);
                }
        }

        public bool Blocked(Vector2 p, float clearance)
        {
            for (int x = Mathf.FloorToInt((p.x - clearance) / Cell); x <= Mathf.FloorToInt((p.x + clearance) / Cell); x++)
                for (int z = Mathf.FloorToInt((p.y - clearance) / Cell); z <= Mathf.FloorToInt((p.y + clearance) / Cell); z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        var r = bounds[i];
                        if (p.x < r.xMin - clearance || p.x > r.xMax + clearance || p.y < r.yMin - clearance || p.y > r.yMax + clearance) continue;
                        if (Geometry.Contains(polygons[i], p)) return true;
                        if (clearance <= 0f) continue;
                        var poly = polygons[i];
                        for (int a = 0, b = poly.Length - 1; a < poly.Length; b = a++)
                            if (Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, poly[b], poly[a], out _)) < clearance) return true;
                    }
                }
            return false;
        }
    }
}
