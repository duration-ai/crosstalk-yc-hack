using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Ambient
{
    public enum WalkKind : byte { Sidewalk, Footway, Plaza, Curb, Crossing }

    public enum SpotKind : byte { Bench, BusStop, Door, Pier }

    public struct WalkEdge
    {
        public int To;
        public float Length;
        public WalkKind Kind;
        public int Crossing;
    }

    // A crosswalk from curb A to curb B over one road. At signalised junctions it follows a simple
    // two-phase cycle: pedestrians may start across while traffic on the road they cross is held.
    public sealed class Crossing
    {
        public const float Cycle = 40f;
        public Vector2 A, B;
        public int NodeA, NodeB;
        public int Road;
        public bool FromData;
        public bool Signal;
        public float SignalOffset;
        public int WalkAxis;

        public Vector2 Center => (A + B) * 0.5f;

        public bool MayStart(float time)
        {
            if (!Signal) return true;
            float local = Mathf.Repeat(time + SignalOffset, Cycle);
            float start = WalkAxis * Cycle * 0.5f;
            return local >= start && local < start + 11f;
        }
    }

    public struct Spot
    {
        public SpotKind Kind;
        public Vector2 Position;
        public Vector2 Facing;
        public int Node;
        public int Occupant;
    }

    // The walkable network, built at startup from the street extract: sidewalks along both sides of
    // every street (offset by its width, broken where they would cross another road or a building),
    // OSM footways (minus the ones that duplicate a sidewalk), crosswalks where an OSM footway
    // crosses a street plus one per approach at every junction without one, and open plazas in the
    // parks and around Pier 70. It also lists where people go: benches, bus stops, street-facing
    // building doors and Pier 70.
    public sealed class PedestrianGraph
    {
        const float Step = 6f;
        const float Cell = 12f;
        const float SidewalkInset = 1.5f;

        public static readonly Rect Pier70 = Rect.MinMaxRect(140f, 100f, 520f, 500f);

        public readonly List<Vector2> Nodes = new List<Vector2>();
        public readonly List<WalkKind> Kinds = new List<WalkKind>();
        public readonly List<List<WalkEdge>> Edges = new List<List<WalkEdge>>();
        public readonly List<Crossing> Crossings = new List<Crossing>();
        public readonly List<Spot> Spots = new List<Spot>();
        public int[] Component;
        public int[] ComponentSize;

        readonly WorldContext world;
        readonly Asphalt asphalt;
        readonly List<int> chainOf = new List<int>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
        readonly List<int> scratch = new List<int>();
        int chains;

        public PedestrianGraph(WorldData data, WorldContext world)
        {
            this.world = world;
            asphalt = new Asphalt(world.Roads);
            BuildSidewalks();
            LinkLooseEnds(WalkKind.Sidewalk, 9f);
            BuildFootways();
            LinkLooseEnds(WalkKind.Footway, 6f);
            CornerCrossings(FindJunctions());
            BuildPlazas(data);
            BuildSpots(data);
            FindComponents();
        }

        public static bool IsStreet(Road road) => road.kind != "path" && road.kind != "service" && road.kind != "track";

        // --- queries -------------------------------------------------------------------------

        public void Query(Vector2 p, float radius, List<int> result)
        {
            result.Clear();
            float r2 = radius * radius;
            int x0 = Mathf.FloorToInt((p.x - radius) / Cell), x1 = Mathf.FloorToInt((p.x + radius) / Cell);
            int z0 = Mathf.FloorToInt((p.y - radius) / Cell), z1 = Mathf.FloorToInt((p.y + radius) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int i in list)
                        if ((Nodes[i] - p).sqrMagnitude <= r2) result.Add(i);
                }
        }

        public int Nearest(Vector2 p, float radius, bool clear)
        {
            Query(p, radius, scratch);
            int best = -1;
            float bestD = float.MaxValue;
            foreach (int i in scratch)
            {
                float d = (Nodes[i] - p).sqrMagnitude;
                if (d >= bestD || Kinds[i] == WalkKind.Curb) continue;
                if (clear && !ClearLine(p, Nodes[i])) continue;
                bestD = d;
                best = i;
            }
            return best;
        }

        public bool Walkable(Vector2 p) =>
            world.Bounds.Contains(p) && !asphalt.Near(p, 0.3f, out _, out _) && !world.Buildings.Blocked(p, 0.3f);

        public bool OnStreet(Vector2 p, float margin) => asphalt.Near(p, margin, out _, out _);

        public bool InBuilding(Vector2 p, float clearance) => world.Buildings.Blocked(p, clearance);

        public bool ClearLine(Vector2 a, Vector2 b)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b));
            for (int k = 1; k < n; k++)
            {
                var p = Vector2.Lerp(a, b, k / (float)n);
                if (asphalt.Near(p, -0.1f, out _, out _) || world.Buildings.Blocked(p, 0.15f)) return false;
            }
            return true;
        }

        // A* over the walk graph. Crosswalks cost extra so people stay on their side unless the
        // destination is across.
        float[] g;
        int[] parent;
        int[] stamp;
        int generation;
        readonly List<(float f, int node)> heap = new List<(float, int)>();

        public bool FindPath(int start, int goal, List<int> path, int maxExpand = 4000)
        {
            path.Clear();
            if (start < 0 || goal < 0 || Component[start] != Component[goal]) return false;
            int n = Nodes.Count;
            if (g == null || g.Length != n)
            {
                g = new float[n];
                parent = new int[n];
                stamp = new int[n];
            }
            generation++;
            heap.Clear();
            g[start] = 0f;
            parent[start] = -1;
            stamp[start] = generation;
            Push((Vector2.Distance(Nodes[start], Nodes[goal]), start));
            int expanded = 0;
            while (heap.Count > 0 && expanded++ < maxExpand)
            {
                var (f, cur) = Pop();
                if (cur == goal)
                {
                    for (int c = goal; c >= 0; c = parent[c]) path.Add(c);
                    path.Reverse();
                    return true;
                }
                if (f - Vector2.Distance(Nodes[cur], Nodes[goal]) > g[cur] + 1e-3f) continue;
                foreach (var e in Edges[cur])
                {
                    float cost = g[cur] + (e.Kind == WalkKind.Crossing ? e.Length * 1.5f + 6f : e.Length);
                    if (stamp[e.To] == generation && cost >= g[e.To]) continue;
                    stamp[e.To] = generation;
                    g[e.To] = cost;
                    parent[e.To] = cur;
                    Push((cost + Vector2.Distance(Nodes[e.To], Nodes[goal]), e.To));
                }
            }
            return false;
        }

        void Push((float f, int node) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heap[p].f <= heap[i].f) break;
                (heap[p], heap[i]) = (heap[i], heap[p]);
                i = p;
            }
        }

        (float f, int node) Pop()
        {
            var top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == i) break;
                (heap[m], heap[i]) = (heap[i], heap[m]);
                i = m;
            }
            return top;
        }

        // --- construction -------------------------------------------------------------------

        int AddNode(Vector2 p, WalkKind kind, int chain)
        {
            int id = Nodes.Count;
            Nodes.Add(p);
            Kinds.Add(kind);
            Edges.Add(new List<WalkEdge>(3));
            chainOf.Add(chain);
            var cell = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
            list.Add(id);
            return id;
        }

        bool Linked(int a, int b)
        {
            foreach (var e in Edges[a]) if (e.To == b) return true;
            return false;
        }

        void Link(int a, int b, WalkKind kind, int crossing = -1)
        {
            if (a == b || Linked(a, b)) return;
            float len = Vector2.Distance(Nodes[a], Nodes[b]);
            Edges[a].Add(new WalkEdge { To = b, Length = len, Kind = kind, Crossing = crossing });
            Edges[b].Add(new WalkEdge { To = a, Length = len, Kind = kind, Crossing = crossing });
        }

        // Samples a polyline every step metres, returning each point with the right-hand normal of
        // the segment it lies on.
        static IEnumerable<(Vector2 p, Vector2 right)> Samples(Vector2[] pts, float step)
        {
            float carry = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                var d = (b - a) / len;
                var right = new Vector2(d.y, -d.x);
                for (float t = carry; t < len; t += step) yield return (a + d * t, right);
                carry = step - ((len - carry) % step);
                if (carry >= step) carry -= step;
                if (i == pts.Length - 2) yield return (b, right);
            }
        }

        void BuildSidewalks()
        {
            var net = world.Roads;
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                var pts = net.Points[r];
                if (!IsStreet(road) || pts.Length < 2) continue;
                float hw = RoadNetwork.HalfWidth(road);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (road.oneway && HasTwin(r, pts, side, hw)) continue;
                    int chain = chains++;
                    int prev = -1;
                    foreach (var (p, right) in Samples(pts, Step))
                    {
                        var q = p + right * side * (hw + SidewalkInset);
                        if (!Walkable(q))
                        {
                            q = p + right * side * (hw + 0.7f);
                            if (!Walkable(q))
                            {
                                prev = -1;
                                continue;
                            }
                        }
                        int id = AddNode(q, WalkKind.Sidewalk, chain);
                        if (prev >= 0 && ClearLine(Nodes[prev], q)) Link(prev, id, WalkKind.Sidewalk);
                        prev = id;
                    }
                }
            }
        }

        // A one-way carriageway with its opposite carriageway alongside (3rd Street around the T Third
        // median) has no sidewalk on the median side.
        bool HasTwin(int r, Vector2[] pts, int side, float hw)
        {
            var road = world.Roads.Roads[r];
            int mid = pts.Length / 2;
            var a = pts[Mathf.Max(mid - 1, 0)];
            var b = pts[Mathf.Min(mid, pts.Length - 1)];
            if (a == b) b = pts[pts.Length - 1];
            var d = (b - a).normalized;
            var probe = (a + b) * 0.5f + new Vector2(d.y, -d.x) * side * (hw + 7f);
            if (!asphalt.Near(probe, 6f, out int other, out _) || other == r) return false;
            var twin = world.Roads.Roads[other];
            return twin.oneway && twin.name == road.name && !string.IsNullOrEmpty(road.name);
        }

        void LinkLooseEnds(WalkKind kind, float radius)
        {
            var best = new Dictionary<int, (int node, float d)>();
            int count = Nodes.Count;
            for (int i = 0; i < count; i++)
            {
                if (Kinds[i] != kind || Edges[i].Count > 1) continue;
                Query(Nodes[i], radius, scratch);
                best.Clear();
                foreach (int j in scratch)
                {
                    if (j == i || chainOf[j] == chainOf[i] || Kinds[j] == WalkKind.Curb || Kinds[j] == WalkKind.Plaza) continue;
                    float d = (Nodes[j] - Nodes[i]).sqrMagnitude;
                    if (!best.TryGetValue(chainOf[j], out var b) || d < b.d) best[chainOf[j]] = (j, d);
                }
                int links = 0;
                foreach (var pair in Sorted(best))
                {
                    if (links >= 2) break;
                    if (!ClearLine(Nodes[i], Nodes[pair])) continue;
                    Link(i, pair, WalkKind.Sidewalk);
                    links++;
                }
            }
        }

        static List<int> Sorted(Dictionary<int, (int node, float d)> best)
        {
            var list = new List<(int node, float d)>(best.Values);
            list.Sort((x, y) => x.d.CompareTo(y.d));
            var result = new List<int>(list.Count);
            foreach (var x in list) result.Add(x.node);
            return result;
        }

        void BuildFootways()
        {
            var net = world.Roads;
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (net.Roads[r].kind != "path") continue;
                var pts = net.Points[r];
                int chain = chains++;
                int prev = -1;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    var a = pts[i];
                    var b = pts[i + 1];
                    float len = Vector2.Distance(a, b);
                    if (len < 0.3f) continue;
                    var dir = (b - a) / len;
                    if (asphalt.Crosses(a, b, out int crossed, out var x, out var roadDir))
                    {
                        AddCrossing(crossed, x, roadDir, true);
                        prev = -1;
                        continue;
                    }
                    if (asphalt.Near((a + b) * 0.5f, 5f, out _, out var nearDir) && Mathf.Abs(Vector2.Dot(dir, nearDir)) > 0.85f)
                    {
                        prev = -1;
                        continue;
                    }
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / 5f));
                    for (int k = prev < 0 ? 0 : 1; k <= steps; k++)
                    {
                        var p = Vector2.Lerp(a, b, k / (float)steps);
                        if (!Walkable(p))
                        {
                            prev = -1;
                            continue;
                        }
                        int id = AddNode(p, WalkKind.Footway, chain);
                        if (prev >= 0 && ClearLine(Nodes[prev], p)) Link(prev, id, WalkKind.Footway);
                        prev = id;
                    }
                }
            }
        }

        int AddCrossing(int road, Vector2 x, Vector2 roadDir, bool fromData)
        {
            if (world.Surface != null && world.Surface.NearestCrosswalk(road, x, 8f, out var painted))
            {
                x = painted.Center;
                roadDir = painted.Along;
            }
            foreach (var c in Crossings)
                if (c.Road == road && (c.Center - x).sqrMagnitude < 64f) return -1;
            float hw = RoadNetwork.HalfWidth(world.Roads.Roads[road]);
            var n = new Vector2(roadDir.y, -roadDir.x);
            var a = Kerb(x, n, hw, road);
            var b = Kerb(x, -n, hw, road);
            if (!Walkable(a) || !Walkable(b)) return -1;
            for (int k = 1; k < 8; k++)
            {
                var p = Vector2.Lerp(a, b, k / 8f);
                if (world.Buildings.Blocked(p, 0.2f)) return -1;
                if (asphalt.Near(p, -0.2f, out int other, out _) && other != road && !SameStreet(other, road)) return -1;
            }
            var na = Neighbours(a);
            var nb = Neighbours(b);
            if (na.Count == 0 || nb.Count == 0) return -1;

            int index = Crossings.Count;
            int ia = AddNode(a, WalkKind.Curb, -1);
            int ib = AddNode(b, WalkKind.Curb, -1);
            Link(ia, ib, WalkKind.Crossing, index);
            foreach (int j in na) Link(ia, j, WalkKind.Sidewalk);
            foreach (int j in nb) Link(ib, j, WalkKind.Sidewalk);
            Crossings.Add(new Crossing { A = a, B = b, NodeA = ia, NodeB = ib, Road = road, FromData = fromData });
            return index;
        }

        // The kerb on one side of a street, carried on over the opposite carriageway of a divided street.
        Vector2 Kerb(Vector2 x, Vector2 n, float hw, int road)
        {
            var p = x + n * (hw + 0.7f);
            for (float extra = 0f; extra < 30f && !Walkable(p); extra += 1f)
            {
                var q = x + n * (hw + 0.7f + extra);
                if (asphalt.Near(q, 0.3f, out int other, out _) && other != road && !SameStreet(other, road)) break;
                p = q;
            }
            return p;
        }

        bool SameStreet(int a, int b)
        {
            var ra = world.Roads.Roads[a];
            var rb = world.Roads.Roads[b];
            return !string.IsNullOrEmpty(ra.name) && ra.name == rb.name;
        }

        List<int> Neighbours(Vector2 p)
        {
            Query(p, 7f, scratch);
            var found = new List<(int node, float d)>();
            foreach (int j in scratch)
            {
                if (Kinds[j] == WalkKind.Curb || Kinds[j] == WalkKind.Plaza) continue;
                if (!ClearLine(p, Nodes[j])) continue;
                found.Add((j, (Nodes[j] - p).sqrMagnitude));
            }
            found.Sort((x, y) => x.d.CompareTo(y.d));
            var result = new List<int>();
            for (int i = 0; i < found.Count && result.Count < 2; i++) result.Add(found[i].node);
            return result;
        }

        sealed class Junction
        {
            public Vector2 P;
            public readonly List<(int road, int vertex, int step)> Arms = new List<(int, int, int)>();
        }

        List<Junction> FindJunctions()
        {
            var net = world.Roads;
            var map = new Dictionary<Vector2Int, Junction>();
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (!IsStreet(net.Roads[r])) continue;
                var pts = net.Points[r];
                for (int v = 0; v < pts.Length; v++)
                {
                    var key = Vector2Int.RoundToInt(pts[v] * 2f);
                    if (!map.TryGetValue(key, out var j)) map[key] = j = new Junction { P = pts[v] };
                    if (v > 0) j.Arms.Add((r, v, -1));
                    if (v < pts.Length - 1) j.Arms.Add((r, v, 1));
                }
            }
            var result = new List<Junction>();
            foreach (var j in map.Values) if (j.Arms.Count >= 3) result.Add(j);
            return result;
        }

        // Walks distance along road r from vertex v in direction step; returns the point and the
        // road's direction there.
        bool Along(int r, int v, int step, float distance, out Vector2 point, out Vector2 dir)
        {
            var pts = world.Roads.Points[r];
            point = pts[v];
            dir = Vector2.zero;
            for (int i = v; i + step >= 0 && i + step < pts.Length; i += step)
            {
                var a = pts[i];
                var b = pts[i + step];
                float len = Vector2.Distance(a, b);
                if (len < 1e-3f) continue;
                dir = (b - a) / len;
                if (distance <= len)
                {
                    point = a + dir * distance;
                    return true;
                }
                distance -= len;
            }
            return false;
        }

        void CornerCrossings(List<Junction> junctions)
        {
            var net = world.Roads;
            foreach (var j in junctions)
            {
                float maxHw = 0f;
                bool signal = false;
                foreach (var arm in j.Arms)
                {
                    var road = net.Roads[arm.road];
                    maxHw = Mathf.Max(maxHw, RoadNetwork.HalfWidth(road));
                    signal |= road.kind == "primary" || road.kind == "secondary" || road.kind == "tertiary";
                }
                float offset = Mathf.Repeat(j.P.x * 7.13f + j.P.y * 3.71f, Crossing.Cycle);
                float baseAngle = -1f;
                foreach (var arm in j.Arms)
                {
                    if (!Along(arm.road, arm.vertex, arm.step, maxHw + 2.2f, out var x, out var dir)) continue;
                    float angle = Mathf.Repeat(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, 180f);
                    if (baseAngle < 0f) baseAngle = angle;
                    float delta = Mathf.Abs(Mathf.DeltaAngle(angle, baseAngle));
                    if (delta > 90f) delta = 180f - delta;
                    int axis = delta < 45f ? 0 : 1;

                    int index = -1;
                    float nearest = 18f * 18f;
                    for (int k = 0; k < Crossings.Count; k++)
                    {
                        var c = Crossings[k];
                        var off = c.Center - j.P;
                        if (!c.FromData || c.Road != arm.road || Vector2.Dot(off, x - j.P) <= 0f || off.sqrMagnitude >= nearest) continue;
                        nearest = off.sqrMagnitude;
                        index = k;
                    }
                    if (index < 0) index = AddCrossing(arm.road, x, dir, false);
                    if (index < 0 || !signal) continue;
                    var crossing = Crossings[index];
                    crossing.Signal = true;
                    crossing.SignalOffset = offset;
                    crossing.WalkAxis = 1 - axis;
                }
            }
        }

        void BuildPlazas(WorldData data)
        {
            const float step = 7f;
            var lands = new List<Vector2[]>();
            foreach (var l in data.land) lands.Add(Geometry.ToPoints(l.pts));
            foreach (var park in data.parks)
            {
                var poly = Geometry.ToPoints(park.pts);
                if (poly.Length >= 3) Plaza(Geometry.BoundsOf(poly), step, p => Geometry.Contains(poly, p), false);
            }
            Plaza(Pier70, 8f, p =>
            {
                foreach (var l in lands) if (Geometry.Contains(l, p)) return true;
                return false;
            }, true);
        }

        void Plaza(Rect rect, float step, System.Func<Vector2, bool> inside, bool pier)
        {
            var cells = new Dictionary<Vector2Int, int>();
            int chain = chains++;
            for (int x = 0; x * step <= rect.width; x++)
                for (int z = 0; z * step <= rect.height; z++)
                {
                    var p = new Vector2(rect.xMin + x * step, rect.yMin + z * step);
                    if (!inside(p) || !Walkable(p) || world.Buildings.Blocked(p, 1.2f)) continue;
                    if (Nearest(p, 3f, false) >= 0) continue;
                    cells[new Vector2Int(x, z)] = AddNode(p, WalkKind.Plaza, chain);
                }
            foreach (var pair in cells)
            {
                int a = pair.Value;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        if (dz == 0 && dx <= 0) continue;
                        if (!cells.TryGetValue(pair.Key + new Vector2Int(dx, dz), out int b)) continue;
                        if (ClearLine(Nodes[a], Nodes[b])) Link(a, b, WalkKind.Plaza);
                    }
                Query(Nodes[a], step + 1f, scratch);
                foreach (int j in scratch)
                {
                    if (Kinds[j] != WalkKind.Sidewalk && Kinds[j] != WalkKind.Footway) continue;
                    if (ClearLine(Nodes[a], Nodes[j])) Link(a, j, WalkKind.Plaza);
                }
                if (pier && (pair.Key.x + pair.Key.y * 3) % 7 == 0)
                {
                    float angle = (pair.Key.x * 37 + pair.Key.y * 61) % 360 * Mathf.Deg2Rad;
                    Spots.Add(new Spot { Kind = SpotKind.Pier, Position = Nodes[a], Facing = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)), Node = a, Occupant = -1 });
                }
            }
        }

        void BuildSpots(WorldData data)
        {
            foreach (var f in world.Furniture.Seating)
            {
                var fwd = new Vector2(Mathf.Sin(f.heading * Mathf.Deg2Rad), Mathf.Cos(f.heading * Mathf.Deg2Rad));
                var right = new Vector2(fwd.y, -fwd.x);
                bool bench = f.kind == "bench";
                for (int k = -1; k <= 1; k += 2)
                {
                    var spot = bench ? f.position + right * (k * 0.45f) + fwd * 0.05f : f.position + right * (k * 1.0f) + fwd * 0.3f;
                    int node = Nearest(spot + fwd * 0.8f, 7f, true);
                    if (node < 0) continue;
                    Spots.Add(new Spot { Kind = bench ? SpotKind.Bench : SpotKind.BusStop, Position = spot, Facing = fwd, Node = node, Occupant = -1 });
                }
            }

            foreach (var building in data.buildings)
            {
                var poly = Geometry.ToPoints(building.pts);
                if (poly.Length < 3) continue;
                if (Geometry.SignedArea(poly) < 0f) System.Array.Reverse(poly);
                if (Geometry.SignedArea(poly) < 40f) continue;
                float bestLen = 0f;
                Spot best = default;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[j];
                    var b = poly[i];
                    float len = Vector2.Distance(a, b);
                    if (len < 4f || len <= bestLen) continue;
                    var outward = new Vector2(b.y - a.y, a.x - b.x).normalized;
                    var mid = (a + b) * 0.5f;
                    var door = mid + outward * 0.5f;
                    if (!world.Bounds.Contains(door) || asphalt.Near(door, 0.3f, out _, out _)) continue;
                    int node = Nearest(mid + outward * 2.5f, 5f, false);
                    if (node < 0 || Kinds[node] != WalkKind.Sidewalk || !ClearLine(door + outward * 0.3f, Nodes[node])) continue;
                    bestLen = len;
                    best = new Spot { Kind = SpotKind.Door, Position = door, Facing = -outward, Node = node, Occupant = -1 };
                }
                if (bestLen > 0f) Spots.Add(best);
            }
        }

        void FindComponents()
        {
            int n = Nodes.Count;
            Component = new int[n];
            for (int i = 0; i < n; i++) Component[i] = -1;
            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (int s = 0; s < n; s++)
            {
                if (Component[s] >= 0) continue;
                int id = sizes.Count;
                int size = 0;
                stack.Push(s);
                Component[s] = id;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    size++;
                    foreach (var e in Edges[cur])
                    {
                        if (Component[e.To] >= 0) continue;
                        Component[e.To] = id;
                        stack.Push(e.To);
                    }
                }
                sizes.Add(size);
            }
            ComponentSize = sizes.ToArray();
        }

        // Street asphalt (service lanes and footways excluded) as a segment grid.
        sealed class Asphalt
        {
            const float Cell = 20f;
            const float Reach = 12f;

            struct Seg
            {
                public int Road;
                public Vector2 A, B;
                public float Hw;
            }

            readonly List<Seg> segments = new List<Seg>();
            readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();

            public Asphalt(RoadNetwork net)
            {
                for (int r = 0; r < net.Roads.Length; r++)
                {
                    if (!IsStreet(net.Roads[r])) continue;
                    var pts = net.Points[r];
                    float hw = RoadNetwork.HalfWidth(net.Roads[r]);
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        int index = segments.Count;
                        segments.Add(new Seg { Road = r, A = pts[i], B = pts[i + 1], Hw = hw });
                        var min = Vector2.Min(pts[i], pts[i + 1]) - Vector2.one * (hw + Reach);
                        var max = Vector2.Max(pts[i], pts[i + 1]) + Vector2.one * (hw + Reach);
                        for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                            for (int z = Mathf.FloorToInt(min.y / Cell); z <= Mathf.FloorToInt(max.y / Cell); z++)
                            {
                                var key = new Vector2Int(x, z);
                                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                                list.Add(index);
                            }
                    }
                }
            }

            // True when p is within margin of a street's edge (negative margin: that far inside it).
            public bool Near(Vector2 p, float margin, out int road, out Vector2 dir)
            {
                road = -1;
                dir = Vector2.zero;
                if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var list)) return false;
                float best = margin;
                foreach (int s in list)
                {
                    var seg = segments[s];
                    var q = Geometry.ClosestPointOnSegment(p, seg.A, seg.B, out _);
                    float d = Vector2.Distance(p, q) - seg.Hw;
                    if (d >= best) continue;
                    best = d;
                    road = seg.Road;
                    dir = (seg.B - seg.A).normalized;
                }
                return road >= 0;
            }

            public bool Crosses(Vector2 a, Vector2 b, out int road, out Vector2 point, out Vector2 dir)
            {
                road = -1;
                point = dir = Vector2.zero;
                var min = Vector2.Min(a, b);
                var max = Vector2.Max(a, b);
                var seen = new HashSet<int>();
                for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                    for (int z = Mathf.FloorToInt(min.y / Cell); z <= Mathf.FloorToInt(max.y / Cell); z++)
                    {
                        if (!cells.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                        foreach (int s in list)
                        {
                            if (!seen.Add(s)) continue;
                            var seg = segments[s];
                            if (!Intersect(a, b, seg.A, seg.B, out var hit)) continue;
                            road = seg.Road;
                            point = hit;
                            dir = (seg.B - seg.A).normalized;
                            return true;
                        }
                    }
                return false;
            }

            static bool Intersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out Vector2 hit)
            {
                hit = Vector2.zero;
                var r = p2 - p1;
                var s = q2 - q1;
                float denom = r.x * s.y - r.y * s.x;
                if (Mathf.Abs(denom) < 1e-6f) return false;
                var qp = q1 - p1;
                float t = (qp.x * s.y - qp.y * s.x) / denom;
                float u = (qp.x * r.y - qp.y * r.x) / denom;
                if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
                hit = p1 + r * t;
                return true;
            }
        }
    }
}
