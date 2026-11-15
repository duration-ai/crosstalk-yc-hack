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

        // Never narrower than its lanes: OSM gives lane counts on most arterials, and its width is a per-kind
        // default. Curbed streets also carry a parking lane along each curb, as SF streets do.
        public static float HalfWidth(Road road)
        {
            float width = Mathf.Max(road.width, 2f);
            if (!IsDriveable(road)) return width * 0.5f;
            if (road.lanes > 0) width = Mathf.Max(width, road.lanes * LaneWidth);
            if (IsCurbed(road))
            {
                var (f, b) = LaneCounts(road);
                width = Mathf.Max(width, (f + b) * LaneWidth + 2f * ParkingLane);
            }
            return width * 0.5f;
        }

        public const float LaneWidth = 3.1f;
        public const float ParkingLane = 2.4f;

        static readonly HashSet<string> CurbedKinds = new HashSet<string>
        {
            "primary", "primary_link", "secondary", "secondary_link", "tertiary", "tertiary_link", "residential", "unclassified", "living_street",
        };

        // City streets at grade: the asphalt sits below raised sidewalks with a curb. Service roads, tracks,
        // bridges and tunnels stay flush.
        public static bool IsCurbed(Road road) => CurbedKinds.Contains(road.kind) && !road.bridge && !road.tunnel && road.layer == 0;

        // Travel lanes in the way's direction (forward) and against it (backward), and where the two directions
        // meet: Divider is the lateral offset of that line, positive to the left of the way's direction.
        public struct LaneLayout
        {
            public int Forward;
            public int Backward;
            public float Width;
            public float Divider;
        }

        static (int forward, int backward) LaneCounts(Road road)
        {
            int n = road.lanes, f = road.lanesForward, b = road.lanesBackward;
            if (road.oneway) return (Mathf.Max(n, f, 1), 0);
            if (f + b > 0)
            {
                if (n < f + b) n = f + b;
                if (f == 0) f = n - b;
                if (b == 0) b = n - f;
                return (f, b);
            }
            if (n > 1) return (n - n / 2, n / 2);
            return (1, n == 1 ? 0 : 1);
        }

        // Travel lanes sit centred between the parking lanes of a curbed street.
        public static LaneLayout Lanes(Road road)
        {
            var (f, b) = LaneCounts(road);
            float hw = HalfWidth(road);
            float travel = 2f * hw - (IsCurbed(road) ? 2f * ParkingLane : 0f);
            float lane = Mathf.Min(LaneWidth + 0.2f, travel / Mathf.Max(f + b, 1));
            return new LaneLayout { Forward = f, Backward = b, Width = lane, Divider = road.oneway || b == 0 ? hw : (f - b) * lane * 0.5f };
        }

        // True when p lies on the asphalt of a curbed street (a capsule of HalfWidth + margin around each
        // segment, for margins up to CurbReach). Street building asks this every half metre, so curbed
        // segments get their own fine grid, filled lazily.
        public bool OnCurbedCarriageway(Vector2 p, float margin, int excludeRoad = -1)
        {
            if (curbGrid == null) BuildCurbGrid();
            if (!curbGrid.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / CurbCell), Mathf.FloorToInt(p.y / CurbCell)), out var list)) return false;
            foreach (int s in list)
            {
                var seg = segments[s];
                if (seg.Road == excludeRoad) continue;
                float reach = HalfWidth(Roads[seg.Road]) + margin;
                if ((p - Geometry.ClosestPointOnSegment(p, seg.A, seg.B, out _)).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        // True when p is on the asphalt of a curbed street other than road and not of the same name, so a
        // street's own continuation past a junction doesn't count.
        public bool OnOtherStreet(Vector2 p, int road, string name)
        {
            if (curbGrid == null) BuildCurbGrid();
            if (!curbGrid.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / CurbCell), Mathf.FloorToInt(p.y / CurbCell)), out var list)) return false;
            foreach (int s in list)
            {
                var seg = segments[s];
                if (seg.Road == road || (!string.IsNullOrEmpty(name) && Roads[seg.Road].name == name)) continue;
                float reach = HalfWidth(Roads[seg.Road]) - 0.1f;
                if ((p - Geometry.ClosestPointOnSegment(p, seg.A, seg.B, out _)).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        const float CurbCell = 8f;
        const float CurbReach = 1f;
        Dictionary<Vector2Int, List<int>> curbGrid;

        void BuildCurbGrid()
        {
            curbGrid = new Dictionary<Vector2Int, List<int>>();
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                if (!IsCurbed(Roads[seg.Road])) continue;
                var pad = Vector2.one * (HalfWidth(Roads[seg.Road]) + CurbReach);
                var min = Vector2.Min(seg.A, seg.B) - pad;
                var max = Vector2.Max(seg.A, seg.B) + pad;
                for (int x = Mathf.FloorToInt(min.x / CurbCell); x <= Mathf.FloorToInt(max.x / CurbCell); x++)
                    for (int z = Mathf.FloorToInt(min.y / CurbCell); z <= Mathf.FloorToInt(max.y / CurbCell); z++)
                    {
                        var cell = new Vector2Int(x, z);
                        if (!curbGrid.TryGetValue(cell, out var list)) curbGrid[cell] = list = new List<int>();
                        list.Add(s);
                    }
            }
        }

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
