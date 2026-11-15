using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // A polyline with arc length, sampled by distance.
    public sealed class TrackPath
    {
        public readonly Vector2[] Points;
        readonly float[] cumulative;

        public float Length => cumulative[cumulative.Length - 1];

        public TrackPath(Vector2[] points)
        {
            Points = points;
            cumulative = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cumulative[i] = cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }

        public Vector2 Sample(float s, out Vector2 tangent)
        {
            if (Points.Length == 1)
            {
                tangent = Vector2.up;
                return Points[0];
            }
            s = Mathf.Clamp(s, 0f, Length);
            int lo = 0, hi = cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= s) lo = mid;
                else hi = mid;
            }
            float seg = cumulative[hi] - cumulative[lo];
            tangent = seg > 1e-4f ? (Points[hi] - Points[lo]) / seg : (lo > 0 ? (Points[lo] - Points[lo - 1]).normalized : Vector2.up);
            return seg > 1e-4f ? Vector2.Lerp(Points[lo], Points[hi], (s - cumulative[lo]) / seg) : Points[lo];
        }

        public float Closest(Vector2 p, out float distance)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i < Points.Length - 1; i++)
            {
                var a = Points[i];
                var ab = Points[i + 1] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bestS = cumulative[i] + Mathf.Sqrt(len2) * t;
            }
            distance = Mathf.Sqrt(best);
            return bestS;
        }

        public static TrackPath Curve(Vector2 p0, Vector2 d0, Vector2 p3, Vector2 d3, int steps = 8)
        {
            float k = Vector2.Distance(p0, p3) * 0.42f;
            var p1 = p0 + d0 * k;
            var p2 = p3 - d3 * k;
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1f - t;
                pts[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            }
            return new TrackPath(pts);
        }
    }

    public enum NodeControl { None, Stop, Signal }

    // Directed lane graph over the real street network: every driveable road is cut at junctions,
    // each piece carries its lanes in each legal direction (one-way roads only along their points),
    // lanes are offset to the right of the centreline and trimmed back from the junction, and lanes
    // meeting at a junction are joined by curved links. Junctions carry their signal or stop control.
    public sealed class StreetGraph
    {
        public static bool AllGreen;

        public sealed class Node
        {
            public int Id;
            public Vector2 Position;
            public float Radius;
            public int Pieces;
            public NodeControl Control;
            public float SignalOffset;
            public Vector2 SignalAxis;
            public readonly List<Lane> In = new List<Lane>();
            public readonly List<Lane> Out = new List<Lane>();
            public readonly List<object> Queue = new List<object>();
            public int Occupants;

            // 0 green, 1 yellow, 2 red for traffic arriving with heading dir.
            public int SignalState(Vector2 dir, float time)
            {
                if (AllGreen) return 0;
                const float green = 14f, yellow = 3f, clear = 1.5f, half = green + yellow + clear;
                float t = Mathf.Repeat(time + SignalOffset, half * 2f);
                bool axisA = Mathf.Abs(Vector2.Dot(dir, SignalAxis)) > 0.7071f;
                if (!axisA) t = Mathf.Repeat(t - half, half * 2f);
                return t < green ? 0 : t < green + yellow ? 1 : 2;
            }
        }

        public sealed class Lane
        {
            public int Id;
            public Node From;
            public Node To;
            public TrackPath Path;
            public int Road;
            public int Piece;
            public int Index;
            public int Count;
            public float Width;
            public float Speed;
            public bool TwoWay;
            public bool StopAtEnd;
            public readonly List<Link> Next = new List<Link>();

            public Vector2 EndDirection
            {
                get
                {
                    Path.Sample(Path.Length, out var t);
                    return t;
                }
            }
        }

        public sealed class Link
        {
            public Lane From;
            public Lane To;
            public TrackPath Path;
            public float Turn;
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Lane> Lanes = new List<Lane>();
        public readonly TrafficRoad[] Roads;

        const float Cell = 40f;
        readonly Dictionary<Vector2Int, List<Lane>> grid = new Dictionary<Vector2Int, List<Lane>>();

        static bool Driveable(TrafficRoad r) =>
            r.pts != null && r.pts.Length >= 4 && r.kind != "path" && r.kind != "service" && r.kind != "track" && r.kind != "footway" && r.kind != "cycleway" && r.kind != "pedestrian" && r.kind != "steps";

        static int Rank(string kind) =>
            kind == "primary" || kind == "primary_link" || kind == "trunk" ? 3 : kind == "secondary" || kind == "tertiary" ? 2 : 1;

        static float DefaultSpeed(TrafficRoad r) => r.MaxSpeed > 0f ? r.MaxSpeed : Rank(r.kind) >= 2 ? 12.5f : 11f;

        static Vector2[] ToPoints(float[] flat)
        {
            var pts = new Vector2[flat.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(flat[i * 2], flat[i * 2 + 1]);
            return pts;
        }

        public StreetGraph(TrafficWorld world)
        {
            Roads = world.roads;
            var counts = new Dictionary<Vector2Int, int>();
            var points = new Vector2[Roads.Length][];
            for (int r = 0; r < Roads.Length; r++)
            {
                if (!Driveable(Roads[r])) continue;
                points[r] = ToPoints(Roads[r].pts);
                var seen = new HashSet<Vector2Int>();
                foreach (var p in points[r])
                {
                    var key = Vector2Int.RoundToInt(p * 2f);
                    if (!seen.Add(key)) continue;
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;
                }
            }

            var nodes = new Dictionary<Vector2Int, Node>();
            Node NodeAt(Vector2 p)
            {
                var key = Vector2Int.RoundToInt(p * 2f);
                if (nodes.TryGetValue(key, out var n)) return n;
                n = new Node { Id = Nodes.Count, Position = p };
                nodes[key] = n;
                Nodes.Add(n);
                return n;
            }

            var pieces = new List<(int road, Vector2[] pts, Node a, Node b)>();
            for (int r = 0; r < Roads.Length; r++)
            {
                var pts = points[r];
                if (pts == null) continue;
                int start = 0;
                for (int i = 1; i < pts.Length; i++)
                {
                    bool split = i == pts.Length - 1 || counts[Vector2Int.RoundToInt(pts[i] * 2f)] >= 2;
                    if (!split) continue;
                    var piece = new Vector2[i - start + 1];
                    System.Array.Copy(pts, start, piece, 0, piece.Length);
                    float length = 0f;
                    for (int k = 1; k < piece.Length; k++) length += Vector2.Distance(piece[k - 1], piece[k]);
                    if (length > 0.5f) pieces.Add((r, piece, NodeAt(piece[0]), NodeAt(piece[piece.Length - 1])));
                    start = i;
                }
            }

            foreach (var (road, _, a, b) in pieces)
            {
                float hw = Mathf.Max(Roads[road].width, 4f) * 0.5f;
                a.Pieces++;
                b.Pieces++;
                a.Radius = Mathf.Max(a.Radius, hw + 1f);
                b.Radius = Mathf.Max(b.Radius, hw + 1f);
            }
            foreach (var n in Nodes)
                if (n.Pieces <= 2) n.Radius = 0f;
                else n.Radius = Mathf.Min(n.Radius, 12f);

            for (int p = 0; p < pieces.Count; p++)
            {
                var (road, pts, a, b) = pieces[p];
                var info = Roads[road];
                bool oneway = info.oneway;
                int forward = info.lanesForward > 0 ? info.lanesForward : oneway ? (info.lanes > 0 ? info.lanes : info.width >= 10f ? 2 : 1) : (info.lanes >= 2 ? info.lanes / 2 : info.width >= 15f ? 2 : 1);
                int backward = oneway ? 0 : info.lanesBackward > 0 ? info.lanesBackward : info.lanes >= 2 ? info.lanes - info.lanes / 2 : forward;
                int total = forward + backward;
                float width = Mathf.Max(info.width, 4f);
                float laneWidth = Mathf.Clamp(width / total, 2.8f, 3.6f);
                float speed = DefaultSpeed(info);
                for (int dir = 0; dir < 2; dir++)
                {
                    int count = dir == 0 ? forward : backward;
                    if (count == 0) continue;
                    var line = dir == 0 ? pts : Reversed(pts);
                    var from = dir == 0 ? a : b;
                    var to = dir == 0 ? b : a;
                    for (int k = 0; k < count; k++)
                    {
                        float offset;
                        if (oneway) offset = -count * laneWidth * 0.5f + (k + 0.5f) * laneWidth;
                        else if (count == 1) offset = Mathf.Clamp(width * 0.5f - 3.4f, 0.9f, laneWidth * 0.5f);
                        else offset = (k + 0.5f) * laneWidth;
                        var lane = new Lane
                        {
                            Id = Lanes.Count, From = from, To = to, Road = road, Piece = p, Index = k, Count = count,
                            Width = laneWidth, Speed = speed, TwoWay = !oneway,
                        };
                        lane.Path = new TrackPath(Trim(Offset(line, offset), from.Radius, to.Radius));
                        Lanes.Add(lane);
                        from.Out.Add(lane);
                        to.In.Add(lane);
                    }
                }
            }

            foreach (var lane in Lanes)
            {
                var node = lane.To;
                lane.Path.Sample(lane.Path.Length, out var d0);
                var p0 = lane.Path.Points[lane.Path.Points.Length - 1];
                foreach (var next in node.Out)
                {
                    if (next.Piece == lane.Piece && node.Pieces > 1) continue;
                    if (next.Index < Mathf.Min(lane.Index, next.Count - 1) || next.Index > Mathf.Max(lane.Index, next.Count - lane.Count + lane.Index)) continue;
                    next.Path.Sample(0f, out var d3);
                    var p3 = next.Path.Points[0];
                    lane.Next.Add(new Link { From = lane, To = next, Path = TrackPath.Curve(p0, d0, p3, d3, node.Radius > 0f ? 8 : 2), Turn = Vector2.SignedAngle(d3, d0) });
                }
            }

            foreach (var lane in Lanes)
                foreach (var pt in lane.Path.Points)
                {
                    var cell = new Vector2Int(Mathf.FloorToInt(pt.x / Cell), Mathf.FloorToInt(pt.y / Cell));
                    if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<Lane>();
                    if (list.Count == 0 || list[list.Count - 1] != lane) list.Add(lane);
                }

            AssignControls(world);
        }

        static Vector2[] Reversed(Vector2[] pts)
        {
            var r = (Vector2[])pts.Clone();
            System.Array.Reverse(r);
            return r;
        }

        static Vector2[] Offset(Vector2[] pts, float right)
        {
            var result = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var din = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                var dout = i < pts.Length - 1 ? (pts[i + 1] - pts[i]).normalized : din;
                var d = (din + dout).normalized;
                if (d.sqrMagnitude < 1e-4f) d = dout;
                var n = new Vector2(d.y, -d.x);
                float miter = 1f / Mathf.Max(0.5f, Vector2.Dot(n, new Vector2(dout.y, -dout.x)));
                result[i] = pts[i] + n * right * miter;
            }
            return result;
        }

        static Vector2[] Trim(Vector2[] pts, float startCut, float endCut)
        {
            var path = new TrackPath(pts);
            float len = path.Length;
            float total = startCut + endCut;
            if (total > len * 0.7f)
            {
                float k = len * 0.7f / Mathf.Max(total, 1e-3f);
                startCut *= k;
                endCut *= k;
            }
            var list = new List<Vector2> { path.Sample(startCut, out _) };
            float s = 0f;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                s += Vector2.Distance(pts[i - 1], pts[i]);
                if (s > startCut + 0.1f && s < len - endCut - 0.1f) list.Add(pts[i]);
            }
            list.Add(path.Sample(len - endCut, out _));
            return list.ToArray();
        }

        void AssignControls(TrafficWorld world)
        {
            var rng = new System.Random(2024);
            foreach (var n in Nodes)
            {
                n.SignalOffset = (float)rng.NextDouble() * 37f;
                if (n.In.Count > 0) n.SignalAxis = n.In[0].EndDirection;
            }

            if (world.HasControls)
            {
                foreach (var signal in world.Props("traffic_signal"))
                {
                    var n = NearestJunction(signal.Position, 20f);
                    if (n != null) n.Control = NodeControl.Signal;
                }
                // A stop sign's heading is the way its face points, toward the traffic it controls.
                foreach (var stop in world.Props("stop_sign"))
                {
                    var n = NearestJunction(stop.Position, 25f);
                    if (n == null || n.Control == NodeControl.Signal) continue;
                    n.Control = NodeControl.Stop;
                    bool allWay = stop.Tag("stop") == "all";
                    var facing = stop.headingKnown ? new Vector2(Mathf.Sin(stop.heading * Mathf.Deg2Rad), Mathf.Cos(stop.heading * Mathf.Deg2Rad)) : Vector2.zero;
                    foreach (var lane in n.In)
                        if (allWay || facing == Vector2.zero || Vector2.Dot(lane.EndDirection, facing) < -0.5f) lane.StopAtEnd = true;
                }
                return;
            }

            // No signal or stop data yet: signals where two ranked streets cross, stops for the minor
            // approaches where a ranked street meets a residential one, all-way stops elsewhere.
            foreach (var n in Nodes)
            {
                if (n.Pieces < 3) continue;
                var names = new HashSet<string>();
                int ranked = 0, maxRank = 0;
                foreach (var lane in n.In)
                {
                    var road = Roads[lane.Road];
                    int rank = Rank(road.kind);
                    maxRank = Mathf.Max(maxRank, rank);
                    if (rank >= 2 && names.Add(road.name ?? "")) ranked++;
                }
                if (ranked >= 2)
                {
                    n.Control = NodeControl.Signal;
                    continue;
                }
                n.Control = NodeControl.Stop;
                foreach (var lane in n.In) lane.StopAtEnd = maxRank < 2 || Rank(Roads[lane.Road].kind) < 2;
            }
        }

        Node NearestJunction(Vector2 p, float within)
        {
            Node best = null;
            float bestD = within;
            foreach (var n in Nodes)
            {
                if (n.Pieces < 3) continue;
                float d = Vector2.Distance(n.Position, p);
                if (d >= bestD) continue;
                bestD = d;
                best = n;
            }
            return best;
        }

        public IEnumerable<Lane> LanesNear(Vector2 p, float radius)
        {
            var seen = new HashSet<Lane>();
            int reach = Mathf.CeilToInt(radius / Cell);
            var c = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            for (int x = c.x - reach; x <= c.x + reach; x++)
                for (int z = c.y - reach; z <= c.y + reach; z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (var lane in list)
                        if (seen.Add(lane)) yield return lane;
                }
        }

        // Closest lane whose direction roughly agrees with heading (any direction when heading is zero).
        public Lane Nearest(Vector2 p, Vector2 heading, float radius, out float s, out float distance)
        {
            Lane best = null;
            s = 0f;
            distance = radius;
            foreach (var lane in LanesNear(p, radius))
            {
                float ls = lane.Path.Closest(p, out float d);
                if (d >= distance) continue;
                lane.Path.Sample(ls, out var t);
                if (heading != Vector2.zero && Vector2.Dot(t, heading) < 0.2f) continue;
                best = lane;
                distance = d;
                s = ls;
            }
            return best;
        }

        // Shortest lane route from one lane to another (Dijkstra over lane ends), lanes in order.
        public List<Lane> Route(Lane from, Lane to)
        {
            var dist = new Dictionary<Lane, float> { [from] = 0f };
            var prev = new Dictionary<Lane, Lane>();
            var open = new List<Lane> { from };
            var done = new HashSet<Lane>();
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++)
                    if (dist[open[i]] < dist[open[bi]]) bi = i;
                var lane = open[bi];
                open.RemoveAt(bi);
                if (lane == to) break;
                if (!done.Add(lane)) continue;
                foreach (var link in lane.Next)
                {
                    float nd = dist[lane] + lane.Path.Length + link.Path.Length;
                    if (dist.TryGetValue(link.To, out float od) && od <= nd) continue;
                    dist[link.To] = nd;
                    prev[link.To] = lane;
                    open.Add(link.To);
                }
            }
            if (!dist.ContainsKey(to)) return null;
            var route = new List<Lane>();
            for (var l = to; l != null; l = prev.TryGetValue(l, out var p) ? p : null)
            {
                route.Add(l);
                if (l == from) break;
            }
            route.Reverse();
            return route;
        }
    }
}
