using System.Collections.Generic;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Nav
{
    // Street graph for the direction arrow and the minimap route. OSM vertices shared by roads are
    // junctions and every road segment is an edge. The driving graph keeps car streets only and
    // gives one-way streets their drawing direction only; the walking graph adds paths and service
    // roads and ignores one-way. Route runs one multi-source A* from every street within reach of
    // you to the street point nearest the goal, so a start beside a one-way street the wrong way
    // takes the parallel street instead of looping the block.
    public sealed class NavGraph
    {
        const float Cell = 40f;
        const int MaxRings = 8;

        struct Segment
        {
            public int A;
            public int B;
            public bool OneWay;
        }

        readonly List<Vector2> nodes = new List<Vector2>();
        readonly List<List<(int to, float cost)>> edges = new List<List<(int, float)>>();
        readonly List<Segment> segments = new List<Segment>();
        readonly Dictionary<Vector2Int, int> ids = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
        readonly List<(int seg, Vector2 point, float distance)> starts = new List<(int, Vector2, float)>();
        readonly List<(float f, int node)> heap = new List<(float, int)>();
        float[] dist = new float[0];
        int[] prev = new int[0];
        int[] seen = new int[0];
        Vector2[] entry = new Vector2[0];
        int stamp;

        public readonly bool Driving;

        static bool CarStreet(string kind) =>
            kind != "path" && kind != "service" && kind != "track" && kind != "footway" && kind != "cycleway" && kind != "pedestrian" && kind != "steps";

        public NavGraph(RoadNetwork roads, bool driving)
        {
            Driving = driving;
            for (int r = 0; r < roads.Roads.Length; r++)
            {
                var road = roads.Roads[r];
                if (driving && !CarStreet(road.kind)) continue;
                bool oneway = driving && road.oneway;
                var pts = roads.Points[r];
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    int a = Node(pts[i]);
                    int b = Node(pts[i + 1]);
                    if (a == b) continue;
                    float cost = Vector2.Distance(pts[i], pts[i + 1]);
                    edges[a].Add((b, cost));
                    if (!oneway) edges[b].Add((a, cost));
                    int index = segments.Count;
                    segments.Add(new Segment { A = a, B = b, OneWay = oneway });
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
            dist = new float[nodes.Count];
            prev = new int[nodes.Count];
            seen = new int[nodes.Count];
            entry = new Vector2[nodes.Count];
        }

        public int NodeCount => nodes.Count;
        public int SegmentCount => segments.Count;

        int Node(Vector2 p)
        {
            var key = Vector2Int.RoundToInt(p * 2f);
            if (ids.TryGetValue(key, out int id)) return id;
            id = nodes.Count;
            ids[key] = id;
            nodes.Add(p);
            edges.Add(new List<(int, float)>());
            return id;
        }

        // Segments whose closest point lies within slack metres of the nearest one, nearest first.
        bool Near(Vector2 p, float slack)
        {
            starts.Clear();
            var c = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
            float best = float.MaxValue;
            int reach = 0;
            for (int ring = 0; ring <= MaxRings && best == float.MaxValue; ring++)
            {
                reach = ring;
                for (int x = c.x - ring; x <= c.x + ring; x++)
                    for (int z = c.y - ring; z <= c.y + ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x - c.x), Mathf.Abs(z - c.y)) != ring) continue;
                        if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                        foreach (int s in list)
                            best = Mathf.Min(best, Vector2.Distance(p, Closest(s, p)));
                    }
            }
            if (best == float.MaxValue) return false;
            float limit = best + slack;
            reach = Mathf.Max(reach, Mathf.CeilToInt(limit / Cell));
            var visited = new HashSet<int>();
            for (int x = c.x - reach; x <= c.x + reach; x++)
                for (int z = c.y - reach; z <= c.y + reach; z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int s in list)
                    {
                        if (!visited.Add(s)) continue;
                        var q = Closest(s, p);
                        float d = Vector2.Distance(p, q);
                        if (d <= limit) starts.Add((s, q, d));
                    }
                }
            starts.Sort((a, b) => a.distance.CompareTo(b.distance));
            return starts.Count > 0;
        }

        Vector2 Closest(int s, Vector2 p) => Geometry.ClosestPointOnSegment(p, nodes[segments[s].A], nodes[segments[s].B], out _);

        // Street path from "from" to "to": from, its street point, the junctions between, the
        // goal's street point and "to". False when no street is within reach of either end.
        public bool Route(Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();
            if (!Near(to, 0f)) return false;
            var (ts, tp, _) = starts[0];
            var goal = segments[ts];
            if (!Near(from, Driving ? 25f : 8f)) return false;

            stamp++;
            heap.Clear();
            float direct = float.MaxValue;
            Vector2 directFrom = from;
            foreach (var (fs, fp, approach) in starts)
            {
                var seg = segments[fs];
                if (fs == ts && (!seg.OneWay || Vector2.Dot(tp - fp, nodes[seg.B] - nodes[seg.A]) >= 0f))
                {
                    float d = approach + Vector2.Distance(fp, tp);
                    if (d < direct)
                    {
                        direct = d;
                        directFrom = fp;
                    }
                }
                Seed(seg.B, approach + Vector2.Distance(fp, nodes[seg.B]), fp, tp);
                if (!seg.OneWay) Seed(seg.A, approach + Vector2.Distance(fp, nodes[seg.A]), fp, tp);
            }

            int end = -1;
            float total = direct;
            while (heap.Count > 0)
            {
                var (f, u) = Pop();
                if (f >= total) break;
                if (f > dist[u] + Vector2.Distance(nodes[u], tp) + 1e-3f) continue;
                float tail = u == goal.A ? Vector2.Distance(nodes[u], tp) : !goal.OneWay && u == goal.B ? Vector2.Distance(nodes[u], tp) : -1f;
                if (tail >= 0f && dist[u] + tail < total)
                {
                    total = dist[u] + tail;
                    end = u;
                }
                foreach (var (v, cost) in edges[u])
                {
                    float nd = dist[u] + cost;
                    if (seen[v] == stamp && nd >= dist[v]) continue;
                    seen[v] = stamp;
                    dist[v] = nd;
                    prev[v] = u;
                    entry[v] = entry[u];
                    Push(nd + Vector2.Distance(nodes[v], tp), v);
                }
            }

            path.Add(from);
            if (end < 0)
            {
                if (direct == float.MaxValue) return false;
                path.Add(directFrom);
            }
            else
            {
                int count = path.Count;
                for (int n = end; n >= 0; n = prev[n]) path.Add(nodes[n]);
                path.Reverse(count, path.Count - count);
                path.Insert(count, entry[end]);
            }
            path.Add(tp);
            path.Add(to);
            return true;
        }

        void Seed(int node, float cost, Vector2 fp, Vector2 goal)
        {
            if (seen[node] == stamp && cost >= dist[node]) return;
            seen[node] = stamp;
            dist[node] = cost;
            prev[node] = -1;
            entry[node] = fp;
            Push(cost + Vector2.Distance(nodes[node], goal), node);
        }

        void Push(float f, int node)
        {
            heap.Add((f, node));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].f <= heap[i].f) break;
                (heap[parent], heap[i]) = (heap[i], heap[parent]);
                i = parent;
            }
        }

        (float f, int node) Pop()
        {
            var top = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
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
    }
}
