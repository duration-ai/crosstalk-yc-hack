using System;
using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    // Streets as built. Curbed streets sit RoadY below sidewalks raised to SidewalkY, with a vertical
    // curb face, and their paint (double yellow, lane dashes from the real lane counts, zebra
    // crosswalks at junctions and OSM crossings, stop lines at stop signs and signals) is baked as
    // vertex-coloured quads on the asphalt. The coastline-accurate land polygon still covers
    // everything at y=0: street geometry renders first and writes the stencil, and land and parks
    // skip those pixels, so the recessed asphalt shows without cutting holes in the land mesh.
    // Physics gets its own surfaces: asphalt, sidewalk slabs with curb faces, and a 1 m raster of
    // the land with the curbed streets removed.
    public sealed class RoadSurface
    {
        public const float RoadY = -0.15f;
        public const float SidewalkY = 0.03f;
        public const float SidewalkWidth = 3f;
        public const float CrosswalkDepth = 3f;
        const float PaintY = RoadY + 0.015f;
        const float FlushY = 0.04f;
        const float PathY = 0.036f;
        const float Sample = 0.5f;
        const float ChunkSize = 200f;
        const int StencilRef = 1;

        static readonly Color Asphalt = Look.SpeedDial.Asphalt;
        static readonly Color Sidewalk = Look.SpeedDial.Sidewalk;
        static readonly Color CurbFace = Look.SpeedDial.Hex("#DCD3C3").linear;
        static readonly Color Yellow = Look.SpeedDial.DoubleYellow;
        static readonly Color White = Look.SpeedDial.LaneWhite;
        static readonly Color RailBed = Look.SpeedDial.RailBed;
        static readonly Color Rail = Look.SpeedDial.Rail;

        // The streets of the current world, for code that places things on the ground by position.
        public static RoadSurface Active { get; private set; }

        // Height of the drivable ground at p: the recessed asphalt on curbed streets, grade elsewhere.
        public static float GroundY(Vector2 p) => Active != null && Active.net.OnCurbedCarriageway(p, 0.2f) ? RoadY : 0f;

        public struct Junction
        {
            public Vector2 Point;
            public float Radius;
        }

        public readonly List<Junction> Junctions = new List<Junction>();
        public readonly List<Vector2> MidBlockCrosswalks = new List<Vector2>();
        public int CrosswalkCount { get; private set; }
        public int StopLineCount { get; private set; }

        readonly WorldContext ctx;
        readonly WorldData data;
        readonly RoadNetwork net;
        readonly float[][] arc;
        readonly List<(int idx, float clear)>[] nodes;
        readonly List<(float from, float to)>[] quiet;
        readonly HashSet<Vector2Int> sharedVertices = new HashSet<Vector2Int>();
        readonly ChunkedMeshBuilder visual = new ChunkedMeshBuilder(ChunkSize);
        readonly ChunkedMeshBuilder asphaltColliders = new ChunkedMeshBuilder(ChunkSize);
        readonly ChunkedMeshBuilder sidewalkColliders = new ChunkedMeshBuilder(ChunkSize);

        public RoadSurface(WorldContext ctx, WorldData data)
        {
            this.ctx = ctx;
            this.data = data;
            net = ctx.Roads;
            int count = net.Roads.Length;
            arc = new float[count][];
            nodes = new List<(int, float)>[count];
            quiet = new List<(float, float)>[count];
            for (int r = 0; r < count; r++)
            {
                var pts = net.Points[r];
                arc[r] = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++) arc[r][i] = arc[r][i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
                nodes[r] = new List<(int, float)>();
                quiet[r] = new List<(float, float)>();
            }
        }

        // Street geometry: drawn before the ground and marks the stencil wherever it covers.
        public static Material StreetMaterial(Material shared)
        {
            var m = new Material(shared) { name = "Streets", renderQueue = (int)RenderQueue.Geometry - 20 };
            m.SetFloat("_StencilRef", StencilRef);
            m.SetFloat("_StencilComp", (float)CompareFunction.Always);
            m.SetFloat("_StencilOp", (float)StencilOp.Replace);
            return m;
        }

        // Land and parks: drawn after the streets and skipped wherever a street already is.
        public static Material GroundMaterial(Material shared)
        {
            var m = new Material(shared) { name = "Ground", renderQueue = (int)RenderQueue.Geometry - 10 };
            m.SetFloat("_StencilRef", StencilRef);
            m.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
            m.SetFloat("_StencilOp", (float)StencilOp.Keep);
            return m;
        }

        public float ArcLength(int road) => arc[road][arc[road].Length - 1];

        public void Build(Material shared)
        {
            Active = this;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FindJunctions();
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                var pts = net.Points[r];
                if (pts.Length < 2) continue;
                float hw = RoadNetwork.HalfWidth(road);
                if (road.kind == "path")
                {
                    Strip(pts, Miters(pts), -hw, hw, PathY, Sidewalk, r, float.NaN, false, null);
                    continue;
                }
                if (!RoadNetwork.IsCurbed(road))
                {
                    Strip(pts, Miters(pts), -hw, hw, FlushY, Asphalt, -1, float.NaN, false, null);
                    continue;
                }
                var mid = pts[pts.Length / 2];
                visual.At(mid).Ribbon(pts, hw, RoadY, Asphalt, true);
                asphaltColliders.At(mid).Ribbon(pts, hw, RoadY, Asphalt, true);
                var miters = Miters(pts);
                Strip(pts, miters, hw, hw + SidewalkWidth, SidewalkY, Sidewalk, r, float.NaN, true, sidewalkColliders);
                Strip(pts, miters, -hw, -hw - SidewalkWidth, SidewalkY, Sidewalk, r, float.NaN, true, sidewalkColliders);
                DeadEnd(r, pts[0], (pts[0] - pts[1]).normalized, hw);
                DeadEnd(r, pts[pts.Length - 1], (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized, hw);
            }

            foreach (var rail in data.rails)
            {
                var pts = Geometry.ToPoints(rail.pts);
                if (pts.Length < 2) continue;
                var miters = Miters(pts);
                Strip(pts, miters, -1.4f, 1.4f, 0.045f, RailBed, -1, RoadY + 0.03f, false, null);
                Strip(pts, miters, 0.65f, 0.79f, 0.065f, Rail, -1, RoadY + 0.05f, false, null);
                Strip(pts, miters, -0.79f, -0.65f, 0.065f, Rail, -1, RoadY + 0.05f, false, null);
            }

            PaintCrossingsAndStops();
            PaintLanes();

            var streets = StreetMaterial(shared);
            foreach (var (key, builder) in visual.All())
                WorldBuilder.Emit(ctx.Root.transform, "Streets " + key, builder.ToMesh("streets " + key), streets, false);
            Colliders("Asphalt", asphaltColliders);
            Colliders("Sidewalks", sidewalkColliders);
            long paint = watch.ElapsedMilliseconds;
            Colliders("Ground", LandColliders());
            Debug.Log($"[Dogpatch] streets: {Junctions.Count} junctions, {CrosswalkCount} crosswalks, {StopLineCount} stop lines; surfaces {paint} ms, ground colliders {watch.ElapsedMilliseconds - paint} ms");
        }

        void Colliders(string name, ChunkedMeshBuilder builders)
        {
            foreach (var (key, builder) in builders.All())
            {
                var go = new GameObject(name + " collider " + key);
                go.transform.SetParent(ctx.Root.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = builder.ToMesh(name + " collider " + key);
            }
        }

        static Vector2Int Key(Vector2 p) => Vector2Int.RoundToInt(p * 2f);

        // A junction is a vertex shared by curbed streets with at least three legs. Each street's
        // crosswalk starts a metre past the widest crossing street's curb.
        void FindJunctions()
        {
            var refs = new Dictionary<Vector2Int, List<(int road, int idx)>>();
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (!RoadNetwork.IsCurbed(net.Roads[r])) continue;
                var pts = net.Points[r];
                for (int i = 0; i < pts.Length; i++)
                {
                    var key = Key(pts[i]);
                    if (!refs.TryGetValue(key, out var list)) refs[key] = list = new List<(int, int)>();
                    if (!list.Exists(e => e.road == r)) list.Add((r, i));
                }
            }

            foreach (var pair in refs)
            {
                var list = pair.Value;
                if (list.Count > 1) sharedVertices.Add(pair.Key);
                int legs = 0;
                foreach (var (road, idx) in list) legs += (idx > 0 ? 1 : 0) + (idx < net.Points[road].Length - 1 ? 1 : 0);
                if (legs < 3) continue;
                float radius = 0f;
                foreach (var (road, idx) in list)
                {
                    float clear = 0f;
                    foreach (var (other, _) in list)
                        if (other != road) clear = Mathf.Max(clear, RoadNetwork.HalfWidth(net.Roads[other]));
                    if (clear == 0f) clear = RoadNetwork.HalfWidth(net.Roads[road]);
                    clear += 1f;
                    nodes[road].Add((idx, clear));
                    float hold = clear + CrosswalkDepth + 1f;
                    quiet[road].Add((arc[road][idx] - hold, arc[road][idx] + hold));
                    radius = Mathf.Max(radius, clear + CrosswalkDepth);
                }
                Junctions.Add(new Junction { Point = net.Points[list[0].road][list[0].idx], Radius = radius });
            }
        }

        void PaintCrossingsAndStops()
        {
            var stops = new HashSet<(int, int)>();
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                if (!RoadNetwork.IsCurbed(road)) continue;
                float total = ArcLength(r);

                foreach (var (idx, clear) in nodes[r])
                {
                    float s = arc[r][idx];
                    if (idx < arc[r].Length - 1 && s + clear + CrosswalkDepth + 1f < total) Crosswalk(r, s + clear + CrosswalkDepth * 0.5f);
                    if (idx > 0 && s - clear - CrosswalkDepth - 1f > 0f) Crosswalk(r, s - clear - CrosswalkDepth * 0.5f);
                }

                if (road.markIdx == null || road.markKind == null) continue;
                float lastCrossing = float.NegativeInfinity;
                for (int k = 0; k < road.markIdx.Length && k < road.markKind.Length; k++)
                {
                    int markIdx = road.markIdx[k];
                    if (markIdx < 0 || markIdx >= arc[r].Length) continue;
                    float s = arc[r][markIdx];
                    string kind = road.markKind[k];
                    if (kind == "crossing")
                    {
                        if (NearNode(r, s, CrosswalkDepth + 4f) || s - lastCrossing < 5f) continue;
                        if (s < CrosswalkDepth || s > total - CrosswalkDepth) continue;
                        lastCrossing = s;
                        Crosswalk(r, s);
                        PointAt(r, s, out var p, out _);
                        MidBlockCrosswalks.Add(p);
                        quiet[r].Add((s - CrosswalkDepth * 0.5f - 1f, s + CrosswalkDepth * 0.5f + 1f));
                    }
                    else if (kind == "stop_sign" || kind == "traffic_signal")
                    {
                        int best = -1;
                        float bestGap = 30f;
                        for (int n = 0; n < nodes[r].Count; n++)
                        {
                            float gap = Mathf.Abs(arc[r][nodes[r][n].idx] - s);
                            if (gap < bestGap)
                            {
                                bestGap = gap;
                                best = n;
                            }
                        }
                        if (best < 0) continue;
                        var (idx, clear) = nodes[r][best];
                        float at = arc[r][idx];
                        bool onNode = Mathf.Abs(s - at) < 1.5f;
                        if ((onNode || s > at) && stops.Add((r, idx * 2))) StopLine(r, idx, clear, true);
                        if ((onNode || s < at) && stops.Add((r, idx * 2 + 1))) StopLine(r, idx, clear, false);
                    }
                }
            }
        }

        bool NearNode(int r, float s, float extra)
        {
            foreach (var (idx, clear) in nodes[r])
                if (Mathf.Abs(arc[r][idx] - s) < clear + extra) return true;
            return false;
        }

        // Continental zebra: bars along the street, 0.6 m wide with 0.6 m gaps, across the full width.
        void Crosswalk(int r, float s)
        {
            PointAt(r, s, out var c, out var d);
            var n = new Vector2(-d.y, d.x);
            float hw = RoadNetwork.HalfWidth(net.Roads[r]);
            var mb = visual.At(c);
            for (float u = -hw + 0.5f; u + 0.6f <= hw - 0.2f; u += 1.2f)
                Rect(mb, c + n * (u + 0.3f), d, CrosswalkDepth * 0.5f, 0.3f, White);
            CrosswalkCount++;
        }

        // A stop bar across the lanes that approach the junction on this leg.
        void StopLine(int r, int idx, float clear, bool forwardLeg)
        {
            float s = arc[r][idx] + (forwardLeg ? 1f : -1f) * (clear + CrosswalkDepth + 0.6f);
            if (s < 0.5f || s > ArcLength(r) - 0.5f) return;
            var road = net.Roads[r];
            var lanes = RoadNetwork.Lanes(road);
            float hw = RoadNetwork.HalfWidth(road);
            float from, to;
            if (forwardLeg)
            {
                if (lanes.Backward == 0) return;
                from = lanes.Divider;
                to = hw;
            }
            else
            {
                from = -hw;
                to = lanes.Divider;
            }
            from += 0.2f;
            to -= 0.2f;
            if (to - from < 1f) return;
            PointAt(r, s, out var c, out var d);
            var n = new Vector2(-d.y, d.x);
            Rect(visual.At(c), c + n * ((from + to) * 0.5f), d, 0.25f, (to - from) * 0.5f, White);
            StopLineCount++;
        }

        void PaintLanes()
        {
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                if (!RoadNetwork.IsCurbed(road)) continue;
                var lanes = RoadNetwork.Lanes(road);
                if (lanes.Forward > 0 && lanes.Backward > 0)
                {
                    Line(r, lanes.Divider + 0.11f, 0.1f, 0f, Yellow);
                    Line(r, lanes.Divider - 0.11f, 0.1f, 0f, Yellow);
                }
                if (lanes.Width < 2.6f) continue;
                float edge = lanes.Backward > 0 ? lanes.Divider : lanes.Forward * lanes.Width * 0.5f;
                for (int k = 1; k < lanes.Forward; k++) Line(r, edge - k * lanes.Width, 0.1f, 3f, White);
                for (int k = 1; k < lanes.Backward; k++) Line(r, lanes.Divider + k * lanes.Width, 0.1f, 3f, White);
            }
        }

        // A line at a lateral offset along a street, solid when dash is 0, else 3 m on 9 m off as in
        // SF, kept clear of junctions and crosswalks.
        void Line(int r, float lateral, float width, float dash, Color color)
        {
            const float Period = 12f;
            var pts = net.Points[r];
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float s0 = arc[r][i], s1 = arc[r][i + 1];
                if (s1 - s0 < 0.05f) continue;
                if (dash <= 0f)
                {
                    LinePieces(r, i, s0, s1, lateral, width, color);
                    continue;
                }
                for (float start = Mathf.Floor(s0 / Period) * Period; start < s1; start += Period)
                {
                    float lo = Mathf.Max(start, s0), hi = Mathf.Min(start + dash, s1);
                    if (hi > lo) LinePieces(r, i, lo, hi, lateral, width, color);
                }
            }
        }

        void LinePieces(int r, int seg, float lo, float hi, float lateral, float width, Color color)
        {
            var a = net.Points[r][seg];
            var b = net.Points[r][seg + 1];
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            float s0 = arc[r][seg];
            foreach (var (from, to) in Unquiet(r, lo, hi))
            {
                if (to - from < 0.2f) continue;
                var c = a + d * ((from + to) * 0.5f - s0) + n * lateral;
                Rect(visual.At(c), c, d, (to - from) * 0.5f, width * 0.5f, color);
            }
        }

        List<(float, float)> Unquiet(int r, float lo, float hi)
        {
            var pieces = new List<(float, float)> { (lo, hi) };
            foreach (var (qa, qb) in quiet[r])
            {
                for (int i = pieces.Count - 1; i >= 0; i--)
                {
                    var (a, b) = pieces[i];
                    if (qb <= a || qa >= b) continue;
                    pieces.RemoveAt(i);
                    if (qa > a) pieces.Add((a, qa));
                    if (qb < b) pieces.Add((qb, b));
                }
            }
            return pieces;
        }

        static void Rect(MeshBuilder mb, Vector2 c, Vector2 along, float halfAlong, float halfAcross, Color color)
        {
            var n = new Vector2(-along.y, along.x);
            var a = along * halfAlong;
            var w = n * halfAcross;
            mb.Quad(MeshBuilder.At(c - a - w, PaintY), MeshBuilder.At(c + a - w, PaintY), MeshBuilder.At(c + a + w, PaintY), MeshBuilder.At(c - a + w, PaintY), color, Vector3.up);
        }

        public void PointAt(int r, float s, out Vector2 point, out Vector2 dir)
        {
            var pts = net.Points[r];
            var a = arc[r];
            int i = 0;
            while (i < pts.Length - 2 && a[i + 1] < s) i++;
            float len = Mathf.Max(a[i + 1] - a[i], 1e-4f);
            dir = (pts[i + 1] - pts[i]) / len;
            point = Vector2.LerpUnclamped(pts[i], pts[i + 1], (s - a[i]) / len);
        }

        // Unit-lateral mitre vectors, so p + miter * offset follows the polyline at that offset.
        static Vector2[] Miters(Vector2[] pts)
        {
            var result = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var dPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                var dNext = i < pts.Length - 1 ? (pts[i + 1] - pts[i]).normalized : dPrev;
                var nPrev = new Vector2(-dPrev.y, dPrev.x);
                var nNext = new Vector2(-dNext.y, dNext.x);
                var miter = (nPrev + nNext).normalized;
                if (miter.sqrMagnitude < 1e-4f) miter = nNext;
                result[i] = miter / Mathf.Max(Vector2.Dot(miter, nNext), 0.5f);
            }
            return result;
        }

        // A band between two lateral offsets along a polyline, sampled every half metre against the
        // curbed asphalt (other than road 'own'). Over asphalt the band is dropped (lowered is NaN) or
        // drawn at 'lowered'. With curb, the band is a sidewalk: a vertical face on the inner edge down to
        // the asphalt, end faces where the asphalt cuts it, and a copy in 'colliders'.
        void Strip(Vector2[] pts, Vector2[] miters, float inner, float outer, float y, Color color, int own, float lowered, bool curb, ChunkedMeshBuilder colliders)
        {
            bool previousOnAsphalt = false;
            Vector3 pendingA = default, pendingB = default, pendingDir = default;
            bool pending = false;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                if (len < 0.01f) continue;
                var a0 = pts[i] + miters[i] * inner;
                var a1 = pts[i + 1] + miters[i + 1] * inner;
                var b0 = pts[i] + miters[i] * outer;
                var b1 = pts[i + 1] + miters[i + 1] * outer;
                var d = (pts[i + 1] - pts[i]) / len;
                var n = new Vector2(-d.y, d.x);
                var facing = new Vector3(n.x, 0f, n.y) * -Mathf.Sign(outer - inner);
                var along = new Vector3(d.x, 0f, d.y);
                int count = Mathf.Max(1, Mathf.CeilToInt(len / Sample));
                var onAsphalt = new bool[count];
                for (int k = 0; k < count; k++)
                {
                    float t = (k + 0.5f) / count;
                    var pa = Vector2.Lerp(a0, a1, t);
                    var pb = Vector2.Lerp(b0, b1, t);
                    onAsphalt[k] = net.OnCurbedCarriageway(pa, -0.05f, own) || net.OnCurbedCarriageway((pa + pb) * 0.5f, -0.05f, own) || net.OnCurbedCarriageway(pb, -0.05f, own);
                }

                if (pending && onAsphalt[0]) EndFace(pendingA, pendingB, pendingDir, y, colliders);
                pending = false;

                int k0 = 0;
                while (k0 < count)
                {
                    bool asphalt = onAsphalt[k0];
                    int k1 = k0;
                    while (k1 < count && onAsphalt[k1] == asphalt) k1++;
                    float t0 = (float)k0 / count, t1 = (float)k1 / count;
                    var A0 = Vector2.Lerp(a0, a1, t0);
                    var A1 = Vector2.Lerp(a0, a1, t1);
                    var B0 = Vector2.Lerp(b0, b1, t0);
                    var B1 = Vector2.Lerp(b0, b1, t1);
                    bool before = k0 > 0 ? onAsphalt[k0 - 1] : previousOnAsphalt;
                    if (!asphalt || !float.IsNaN(lowered))
                    {
                        float h = asphalt ? lowered : y;
                        var mb = visual.At((A0 + B1) * 0.5f);
                        mb.Quad(MeshBuilder.At(A0, h), MeshBuilder.At(A1, h), MeshBuilder.At(B1, h), MeshBuilder.At(B0, h), color, Vector3.up);
                        if (curb && !asphalt)
                        {
                            var cb = colliders.At((A0 + B1) * 0.5f);
                            cb.Quad(MeshBuilder.At(A0, h), MeshBuilder.At(A1, h), MeshBuilder.At(B1, h), MeshBuilder.At(B0, h), color, Vector3.up);
                            Wall(mb, cb, MeshBuilder.At(A0, 0f), MeshBuilder.At(A1, 0f), y, facing);
                            if (before) EndFace(MeshBuilder.At(A0, 0f), MeshBuilder.At(B0, 0f), -along, y, colliders);
                            if (k1 < count) EndFace(MeshBuilder.At(A1, 0f), MeshBuilder.At(B1, 0f), along, y, colliders);
                            else
                            {
                                pending = true;
                                pendingA = MeshBuilder.At(A1, 0f);
                                pendingB = MeshBuilder.At(B1, 0f);
                                pendingDir = along;
                            }
                        }
                    }
                    k0 = k1;
                }
                previousOnAsphalt = onAsphalt[count - 1];
            }
        }

        void EndFace(Vector3 a, Vector3 b, Vector3 facing, float top, ChunkedMeshBuilder colliders)
        {
            var p = new Vector2(a.x, a.z);
            Wall(visual.At(p), colliders.At(p), a, b, top, facing);
        }

        // Vertical curb face between two ground points, from the asphalt up to 'top'.
        static void Wall(MeshBuilder mb, MeshBuilder cb, Vector3 a, Vector3 b, float top, Vector3 facing)
        {
            var a0 = new Vector3(a.x, RoadY, a.z);
            var b0 = new Vector3(b.x, RoadY, b.z);
            var a1 = new Vector3(a.x, top, a.z);
            var b1 = new Vector3(b.x, top, b.z);
            mb.Quad(a0, b0, b1, a1, CurbFace, facing);
            cb.Quad(a0, b0, b1, a1, CurbFace, facing);
        }

        // A street that stops short of any other curbed street ends in a round turnaround: the asphalt
        // already has a round cap, so ring it with curb and sidewalk.
        void DeadEnd(int r, Vector2 end, Vector2 outward, float hw)
        {
            if (sharedVertices.Contains(Key(end)) || net.OnCurbedCarriageway(end, -0.5f, r)) return;
            var n = new Vector2(-outward.y, outward.x);
            var mb = visual.At(end);
            var cb = sidewalkColliders.At(end);
            const int Steps = 8;
            for (int k = 0; k < Steps; k++)
            {
                float p0 = Mathf.Lerp(-90f, 90f, (float)k / Steps) * Mathf.Deg2Rad;
                float p1 = Mathf.Lerp(-90f, 90f, (float)(k + 1) / Steps) * Mathf.Deg2Rad;
                var u0 = outward * Mathf.Cos(p0) + n * Mathf.Sin(p0);
                var u1 = outward * Mathf.Cos(p1) + n * Mathf.Sin(p1);
                var i0 = end + u0 * hw;
                var i1 = end + u1 * hw;
                var o0 = end + u0 * (hw + SidewalkWidth);
                var o1 = end + u1 * (hw + SidewalkWidth);
                mb.Quad(MeshBuilder.At(i0, SidewalkY), MeshBuilder.At(i1, SidewalkY), MeshBuilder.At(o1, SidewalkY), MeshBuilder.At(o0, SidewalkY), Sidewalk, Vector3.up);
                cb.Quad(MeshBuilder.At(i0, SidewalkY), MeshBuilder.At(i1, SidewalkY), MeshBuilder.At(o1, SidewalkY), MeshBuilder.At(o0, SidewalkY), Sidewalk, Vector3.up);
                var inward = -(u0 + u1).normalized;
                Wall(mb, cb, MeshBuilder.At(i0, 0f), MeshBuilder.At(i1, 0f), SidewalkY, new Vector3(inward.x, 0f, inward.y));
            }
        }

        // Cars parallel-parked in the parking lane along each curb, facing the way traffic flows in the
        // lane beside them: the right-hand curb of each direction on two-way streets, both curbs facing
        // forward on one-way streets. Spaces are 6.4 m with about a quarter left empty, and none at a
        // junction, crosswalk, driveway or side street, hydrant, bus stop, bike rack, Bay Wheels dock,
        // rail line, median or keepClear.
        public void PlanParking(System.Random random, List<Pose2D> into, List<Vector2> keepClear)
        {
            const float Space = 6.4f;
            const float HalfLength = 2.5f;
            var avoid = new List<(Vector2 p, float r)>();
            foreach (var prop in data.props)
            {
                float r = prop.kind == "fire_hydrant" ? 4.6f : prop.kind == "bus_stop" ? 12f : prop.kind == "level_crossing" ? 10f : prop.kind == "crossing" ? 5f : prop.kind == "bike_rack" ? 2.5f : prop.kind == "bicycle_rental" ? 9f : 0f;
                if (r > 0f) avoid.Add((new Vector2(prop.x, prop.z), r));
            }
            foreach (var p in keepClear) avoid.Add((p, 14f));
            foreach (var j in Junctions) avoid.Add((j.Point, j.Radius + HalfLength));
            var avoidGrid = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < avoid.Count; i++)
            {
                var c = Vector2Int.FloorToInt(avoid[i].p / 20f);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                    {
                        var key = c + new Vector2Int(x, z);
                        if (!avoidGrid.TryGetValue(key, out var list)) avoidGrid[key] = list = new List<int>();
                        list.Add(i);
                    }
            }
            var rails = new HashSet<Vector2Int>();
            foreach (var rail in data.rails)
            {
                var pts = Geometry.ToPoints(rail.pts);
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float len = Vector2.Distance(pts[i], pts[i + 1]);
                    for (float t = 0f; t <= len; t += 1f)
                    {
                        var c = Vector2Int.FloorToInt(Vector2.Lerp(pts[i], pts[i + 1], t / Mathf.Max(len, 1e-3f)) / 2f);
                        for (int x = -1; x <= 1; x++)
                            for (int z = -1; z <= 1; z++) rails.Add(c + new Vector2Int(x, z));
                    }
                }
            }
            var parked = new Dictionary<Vector2Int, List<Vector2>>();

            bool Clear(int r, Vector2 pos, Vector2 along, Vector2 outward, float hw)
            {
                if (rails.Contains(Vector2Int.FloorToInt(pos / 2f))) return false;
                if (avoidGrid.TryGetValue(Vector2Int.FloorToInt(pos / 20f), out var near))
                    foreach (int i in near)
                        if ((avoid[i].p - pos).sqrMagnitude < avoid[i].r * avoid[i].r) return false;
                if (net.OnCurbedCarriageway(pos + outward * 1.6f, 0f, r)) return false;
                for (float k = -1f; k <= 1f; k += 1f)
                    if (net.Nearest(pos + along * (HalfLength * k), 0.8f, out _, r)) return false;
                if (ctx.Buildings.Blocked(pos, 0.3f)) return false;
                var cell = Vector2Int.FloorToInt(pos / 10f);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (parked.TryGetValue(cell + new Vector2Int(x, z), out var cars))
                            foreach (var q in cars)
                                if ((q - pos).sqrMagnitude < 5.5f * 5.5f) return false;
                if (!parked.TryGetValue(cell, out var mine)) parked[cell] = mine = new List<Vector2>();
                mine.Add(pos);
                return true;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                if (!RoadNetwork.IsCurbed(road) || road.kind.EndsWith("_link") || net.Points[r].Length < 2) continue;
                var lanes = RoadNetwork.Lanes(road);
                float hw = RoadNetwork.HalfWidth(road);
                float travelHalf = (lanes.Forward + lanes.Backward) * lanes.Width * 0.5f;
                float total = ArcLength(r);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (hw - travelHalf < RoadNetwork.ParkingLane - 0.3f) continue;
                    bool facingForward = road.oneway || lanes.Backward == 0 || side < 0;
                    for (float s = HalfLength + 1f + (float)random.NextDouble() * Space; s < total - HalfLength - 1f; s += Space)
                    {
                        if (random.NextDouble() < 0.25) continue;
                        float at = s + ((float)random.NextDouble() - 0.5f) * 0.6f;
                        if (!Unquiet(r, at - HalfLength, at + HalfLength).Contains((at - HalfLength, at + HalfLength))) continue;
                        PointAt(r, at, out var c, out var d);
                        var outward = new Vector2(-d.y, d.x) * side;
                        var pos = c + outward * (hw - 1.15f);
                        if (!Clear(r, pos, d, outward, hw)) continue;
                        var facing = facingForward ? d : -d;
                        into.Add(new Pose2D { Position = MeshBuilder.At(pos, RoadY + 0.05f), Heading = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, Street = road.name });
                    }
                }
            }
            Debug.Log($"[Dogpatch] parking: {into.Count} curbside spaces taken in {watch.ElapsedMilliseconds} ms");
        }

        // Ground the player and parked cars stand on away from the streets: a 1 m raster of the land
        // polygons with every curbed street removed (dilated so the hole edge stays under a sidewalk),
        // merged into rectangles of at most 64 m a side.
        ChunkedMeshBuilder LandColliders()
        {
            const float Cell = 1f;
            var bounds = ctx.Bounds;
            int w = Mathf.CeilToInt(bounds.width / Cell), h = Mathf.CeilToInt(bounds.height / Cell);
            var solid = new bool[w * h];
            var xs = new List<float>();
            for (int j = 0; j < h; j++)
            {
                float z = bounds.yMin + (j + 0.5f) * Cell;
                xs.Clear();
                foreach (var land in data.land)
                {
                    var p = land.pts;
                    int count = p.Length / 2;
                    for (int a = 0, b = count - 1; a < count; b = a++)
                    {
                        float az = p[a * 2 + 1], bz = p[b * 2 + 1];
                        if ((az > z) == (bz > z)) continue;
                        float ax = p[a * 2], bx = p[b * 2];
                        xs.Add(ax + (z - az) * (bx - ax) / (bz - az));
                    }
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int i0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - bounds.xMin) / Cell - 0.5f));
                    int i1 = Mathf.Min(w - 1, Mathf.FloorToInt((xs[k + 1] - bounds.xMin) / Cell - 0.5f));
                    for (int i = i0; i <= i1; i++) solid[j * w + i] = true;
                }
            }

            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (!RoadNetwork.IsCurbed(net.Roads[r])) continue;
                float reach = RoadNetwork.HalfWidth(net.Roads[r]) + 0.75f;
                var pts = net.Points[r];
                for (int s = 0; s < pts.Length - 1; s++)
                {
                    var min = Vector2.Min(pts[s], pts[s + 1]) - Vector2.one * reach;
                    var max = Vector2.Max(pts[s], pts[s + 1]) + Vector2.one * reach;
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((min.x - bounds.xMin) / Cell)), i1 = Mathf.Min(w - 1, Mathf.FloorToInt((max.x - bounds.xMin) / Cell));
                    int j0 = Mathf.Max(0, Mathf.FloorToInt((min.y - bounds.yMin) / Cell)), j1 = Mathf.Min(h - 1, Mathf.FloorToInt((max.y - bounds.yMin) / Cell));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            var c = new Vector2(bounds.xMin + (i + 0.5f) * Cell, bounds.yMin + (j + 0.5f) * Cell);
                            if ((c - Geometry.ClosestPointOnSegment(c, pts[s], pts[s + 1], out _)).sqrMagnitude < reach * reach) solid[j * w + i] = false;
                        }
                }
            }

            var result = new ChunkedMeshBuilder(ChunkSize);
            const int MaxRect = 64;
            var open = new Dictionary<(int, int), int>();
            var runs = new HashSet<(int, int)>();
            var ended = new List<(int, int)>();
            for (int j = 0; j <= h; j++)
            {
                runs.Clear();
                for (int i = 0; j < h && i < w; i++)
                {
                    if (!solid[j * w + i]) continue;
                    int start = i;
                    while (i + 1 < w && solid[j * w + i + 1] && (i + 1) % MaxRect != 0) i++;
                    runs.Add((start, i));
                }
                ended.Clear();
                foreach (var pair in open)
                    if (!runs.Contains(pair.Key) || j % MaxRect == 0) ended.Add(pair.Key);
                foreach (var (x0, x1) in ended)
                {
                    var a = new Vector2(bounds.xMin + x0 * Cell, bounds.yMin + open[(x0, x1)] * Cell);
                    var b = new Vector2(bounds.xMin + (x1 + 1) * Cell, bounds.yMin + j * Cell);
                    result.At((a + b) * 0.5f).Quad(new Vector3(a.x, 0f, a.y), new Vector3(a.x, 0f, b.y), new Vector3(b.x, 0f, b.y), new Vector3(b.x, 0f, a.y), Color.white, Vector3.up);
                    open.Remove((x0, x1));
                }
                foreach (var run in runs)
                    if (!open.ContainsKey(run)) open[run] = j;
            }
            return result;
        }
    }
}
