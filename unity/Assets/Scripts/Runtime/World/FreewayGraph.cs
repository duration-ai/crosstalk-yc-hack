using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // I-280 and its ramps as a directed graph of ways in local metres with a deck height at every
    // point. The world is flat, so the motorway stays on a 16 m deck through the whole bbox (the real
    // road dips into a cut under 20th and 18th St). Ramps grade from the street node they join to the
    // deck: each ramp point's height comes from its distance along the ramps to the nearest street
    // junction and to the nearest deck point, eased at both ends, so no ramp is steeper than it must be.
    // Traffic reads Ways (with Next/Prev), Lane() and Junctions; gameplay reads OnDeck and SurfaceAt.
    public sealed class FreewayGraph
    {
        public const float DeckHeight = 16f;
        public const float LaneWidth = 3.6f;
        const float Shoulders = 3f;
        const float Step = 5f;
        const float RampLength = 230f;
        const float Ease = 25f;
        const float Cell = 20f;
        const float MphToMs = 0.44704f;

        public sealed class Way
        {
            public int Index;
            public long OsmId;
            public string Name;
            public string Ref;
            public string Destination;
            public bool Ramp;
            public bool Bridge;
            public int Lanes;
            public float Width;
            public float SpeedLimit;
            public Vector3[] Points;
            public float[] Along;
            public long StartNode;
            public long EndNode;
            public bool ClippedStart;
            public bool ClippedEnd;
            public readonly List<int> Next = new List<int>();
            public readonly List<int> Prev = new List<int>();
            public float HalfWidth => Width * 0.5f;
            public float Length => Along[Along.Length - 1];
            public string Label => Ramp ? (string.IsNullOrEmpty(Destination) ? "I-280 ramp" : "I-280 ramp to " + Destination.Replace(";", " / ")) : "I-280";
        }

        // Where a ramp meets the street grid: the ramp's end at a street road's vertex.
        public struct Junction
        {
            public int Way;
            public bool AtStart;
            public int Road;
            public int RoadVertex;
            public Vector2 Point;
        }

        public readonly List<Way> Ways = new List<Way>();
        public readonly List<Junction> Junctions = new List<Junction>();
        readonly Dictionary<Vector2Int, List<(int way, int i)>> grid = new Dictionary<Vector2Int, List<(int, int)>>();

        public FreewayGraph(FreewayData data, RoadNetwork roads, Rect bounds)
        {
            var nodeIds = new List<long[]>();
            foreach (var w in data.ways)
            {
                var pts = Geometry.ToPoints(w.pts);
                if (pts.Length < 2) continue;
                var resampled = new List<Vector2> { pts[0] };
                var ids = new List<long> { w.nodes[0] };
                for (int i = 1; i < pts.Length; i++)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(pts[i - 1], pts[i]) / Step));
                    for (int k = 1; k <= n; k++)
                    {
                        resampled.Add(Vector2.Lerp(pts[i - 1], pts[i], k / (float)n));
                        ids.Add(k == n ? w.nodes[i] : 0);
                    }
                }
                bool ramp = w.kind == "motorway_link";
                var way = new Way
                {
                    Index = Ways.Count, OsmId = w.osmId, Name = w.name ?? "", Ref = w.@ref ?? "", Destination = w.destination ?? "",
                    Ramp = ramp, Bridge = w.bridge, Lanes = Mathf.Max(w.lanes, 1), Width = Mathf.Max(w.lanes, 1) * LaneWidth + Shoulders,
                    SpeedLimit = ParseSpeed(w.maxspeed, ramp ? 35f : 65f), StartNode = w.nodes[0], EndNode = w.nodes[w.nodes.Length - 1],
                    ClippedStart = w.nodes[0] == 0, ClippedEnd = w.nodes[w.nodes.Length - 1] == 0,
                    Points = new Vector3[resampled.Count], Along = new float[resampled.Count],
                };
                for (int i = 0; i < resampled.Count; i++)
                {
                    way.Points[i] = new Vector3(resampled[i].x, 0f, resampled[i].y);
                    if (i > 0) way.Along[i] = way.Along[i - 1] + Vector2.Distance(resampled[i - 1], resampled[i]);
                }
                Ways.Add(way);
                nodeIds.Add(ids.ToArray());
            }

            foreach (var a in Ways)
                foreach (var b in Ways)
                    if (a != b && a.EndNode != 0 && a.EndNode == b.StartNode)
                    {
                        a.Next.Add(b.Index);
                        b.Prev.Add(a.Index);
                    }

            FindJunctions(roads, nodeIds);
            SolveHeights(nodeIds);
            for (int w = 0; w < Ways.Count; w++)
                for (int i = 0; i + 1 < Ways[w].Points.Length; i++)
                {
                    var a = Ways[w].Points[i];
                    var b = Ways[w].Points[i + 1];
                    float r = Ways[w].HalfWidth;
                    for (int x = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r) / Cell); x <= Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r) / Cell); x++)
                        for (int z = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - r) / Cell); z <= Mathf.FloorToInt((Mathf.Max(a.z, b.z) + r) / Cell); z++)
                        {
                            var key = new Vector2Int(x, z);
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<(int, int)>();
                            list.Add((w, i));
                        }
                }
        }

        static float ParseSpeed(string maxspeed, float fallbackMph)
        {
            if (!string.IsNullOrEmpty(maxspeed) && float.TryParse(maxspeed.Split(' ')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                return maxspeed.Contains("mph") ? v * MphToMs : v / 3.6f;
            return fallbackMph * MphToMs;
        }

        // A ramp vertex that is also a vertex of a drivable street (same OSM node) joins the street grid.
        void FindJunctions(RoadNetwork roads, List<long[]> nodeIds)
        {
            var vertices = new Dictionary<Vector2Int, List<(int road, int vertex)>>();
            for (int r = 0; r < roads.Roads.Length; r++)
            {
                if (!RoadNetwork.IsDriveable(roads.Roads[r])) continue;
                var pts = roads.Points[r];
                for (int k = 0; k < pts.Length; k++)
                {
                    var key = Vector2Int.RoundToInt(pts[k]);
                    if (!vertices.TryGetValue(key, out var list)) vertices[key] = list = new List<(int, int)>();
                    list.Add((r, k));
                }
            }
            foreach (var way in Ways)
            {
                if (!way.Ramp) continue;
                foreach (bool atStart in new[] { true, false })
                {
                    var p = way.Points[atStart ? 0 : way.Points.Length - 1];
                    var q = new Vector2(p.x, p.z);
                    var c = Vector2Int.RoundToInt(q);
                    float best = 0.75f;
                    var found = new Junction { Way = -1 };
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!vertices.TryGetValue(c + new Vector2Int(dx, dz), out var list)) continue;
                            foreach (var (road, vertex) in list)
                            {
                                float d = Vector2.Distance(roads.Points[road][vertex], q);
                                if (d >= best) continue;
                                best = d;
                                found = new Junction { Way = way.Index, AtStart = atStart, Road = road, RoadVertex = vertex, Point = roads.Points[road][vertex] };
                            }
                        }
                    if (found.Way >= 0) Junctions.Add(found);
                }
            }
        }

        // Multi-source shortest paths over the sampled ways, from street junctions (height 0) and from the
        // deck (motorway points, ramp points still inside a motorway deck, ramps clipped on a bridge).
        void SolveHeights(List<long[]> nodeIds)
        {
            var key = new List<int[]>();
            var shared = new Dictionary<long, int>();
            int count = 0;
            for (int w = 0; w < Ways.Count; w++)
            {
                var ids = nodeIds[w];
                var k = new int[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] != 0 && shared.TryGetValue(ids[i], out int s)) k[i] = s;
                    else
                    {
                        k[i] = count++;
                        if (ids[i] != 0) shared[ids[i]] = k[i];
                    }
                }
                key.Add(k);
            }
            var edges = new List<(int to, float len)>[count];
            for (int n = 0; n < count; n++) edges[n] = new List<(int, float)>();
            var deck = new bool[count];
            var ground = new bool[count];
            for (int w = 0; w < Ways.Count; w++)
            {
                var way = Ways[w];
                for (int i = 0; i + 1 < way.Points.Length; i++)
                {
                    float len = way.Along[i + 1] - way.Along[i];
                    edges[key[w][i]].Add((key[w][i + 1], len));
                    edges[key[w][i + 1]].Add((key[w][i], len));
                }
                for (int i = 0; i < way.Points.Length; i++)
                    if (!way.Ramp || InsideMotorway(way, way.Points[i])) deck[key[w][i]] = true;
            }
            foreach (var j in Junctions) ground[key[j.Way][j.AtStart ? 0 : Ways[j.Way].Points.Length - 1]] = true;
            for (int w = 0; w < Ways.Count; w++)
            {
                var way = Ways[w];
                if (!way.Ramp) continue;
                if (way.ClippedStart) (way.Bridge ? deck : ground)[key[w][0]] = true;
                if (way.ClippedEnd) (way.Bridge ? deck : ground)[key[w][way.Points.Length - 1]] = true;
            }
            for (int n = 0; n < count; n++) if (ground[n]) deck[n] = false;

            var toGround = Distances(edges, ground, deck);
            var toDeck = Distances(edges, deck, ground);
            for (int w = 0; w < Ways.Count; w++)
                for (int i = 0; i < Ways[w].Points.Length; i++)
                {
                    int n = key[w][i];
                    float h;
                    if (deck[n]) h = DeckHeight;
                    else if (ground[n]) h = 0f;
                    else if (float.IsInfinity(toGround[n])) h = DeckHeight;
                    else if (float.IsInfinity(toDeck[n])) h = 0f;
                    else
                    {
                        float span = Mathf.Min(toGround[n] + toDeck[n], RampLength);
                        h = DeckHeight * Profile(toGround[n] / span, Mathf.Min(Ease / span, 0.3f));
                    }
                    Ways[w].Points[i].y = h;
                }
        }

        bool InsideMotorway(Way ramp, Vector3 p)
        {
            foreach (var m in Ways)
            {
                if (m.Ramp) continue;
                for (int i = 0; i + 1 < m.Points.Length; i++)
                {
                    var q = Geometry.ClosestPointOnSegment(new Vector2(p.x, p.z), new Vector2(m.Points[i].x, m.Points[i].z), new Vector2(m.Points[i + 1].x, m.Points[i + 1].z), out _);
                    if (Vector2.Distance(q, new Vector2(p.x, p.z)) < m.HalfWidth + ramp.HalfWidth - 0.5f) return true;
                }
            }
            return false;
        }

        static float[] Distances(List<(int to, float len)>[] edges, bool[] sources, bool[] walls)
        {
            var dist = new float[edges.Length];
            var open = new SortedSet<(float d, int n)>();
            for (int n = 0; n < dist.Length; n++)
            {
                dist[n] = sources[n] ? 0f : float.PositiveInfinity;
                if (sources[n]) open.Add((0f, n));
            }
            while (open.Count > 0)
            {
                var (d, u) = open.Min;
                open.Remove(open.Min);
                if (walls[u] && !sources[u]) continue;
                foreach (var (v, len) in edges[u])
                {
                    if (d + len >= dist[v]) continue;
                    if (!float.IsInfinity(dist[v])) open.Remove((dist[v], v));
                    dist[v] = d + len;
                    open.Add((dist[v], v));
                }
            }
            return dist;
        }

        // 0..1 with a constant grade in the middle and parabolic vertical curves over the first and last a.
        static float Profile(float t, float a)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            if (t < a) return t * t / (2f * a * (1f - a));
            if (t > 1f - a) return 1f - (1f - t) * (1f - t) / (2f * a * (1f - a));
            return (t - a * 0.5f) / (1f - a);
        }

        // Centre line of one lane: lane 0 is the rightmost, the way's points are offset to the right.
        public Vector3[] Lane(int way, int lane)
        {
            var w = Ways[way];
            float offset = ((w.Lanes - 1) * 0.5f - lane) * LaneWidth;
            var result = new Vector3[w.Points.Length];
            for (int i = 0; i < w.Points.Length; i++)
            {
                var a = w.Points[Mathf.Max(i - 1, 0)];
                var b = w.Points[Mathf.Min(i + 1, w.Points.Length - 1)];
                var d = new Vector2(b.x - a.x, b.z - a.z).normalized;
                result[i] = w.Points[i] + new Vector3(d.y, 0f, -d.x) * offset;
            }
            return result;
        }

        // The deck surface under or near p: the highest deck within maxAbove/maxBelow of p.y whose
        // footprint contains p. exclude skips one way (for overlap tests between ways).
        public bool SurfaceAt(Vector2 p, float y, float maxBelow, float maxAbove, out float height, out Way way, int exclude = -1, float margin = 0f)
        {
            height = float.NegativeInfinity;
            way = null;
            if (!grid.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var list)) return false;
            foreach (var (w, i) in list)
            {
                if (w == exclude) continue;
                var a = Ways[w].Points[i];
                var b = Ways[w].Points[i + 1];
                var q = Geometry.ClosestPointOnSegment(p, new Vector2(a.x, a.z), new Vector2(b.x, b.z), out float t);
                if (Vector2.Distance(q, p) > Ways[w].HalfWidth + margin) continue;
                float h = Mathf.Lerp(a.y, b.y, t);
                if (h < y - maxBelow || h > y + maxAbove || h <= height) continue;
                height = h;
                way = Ways[w];
            }
            return way != null;
        }

        // True when p (a car or walker's feet) is on a deck or ramp above street level.
        public bool OnDeck(Vector3 p, out Way way) => SurfaceAt(new Vector2(p.x, p.z), p.y, 1.5f, 1.5f, out float h, out way) && h > 0.5f;
    }
}
