using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // Builds the drivable I-280 from freeway.json: the graph (heights, lanes, junctions), the deck and
    // ramp meshes in the viaduct look, and two collider sets. Deck tops sit on the Drivable layer, which
    // CarController's ground-follow raycasts against; parapets, sides, soffits and columns collide on
    // Default. Columns are slid along the deck until they clear every street, rail line, building and
    // lower deck, so the street grid under the viaduct stays drivable.
    public static class Freeway
    {
        public const int DrivableLayer = 8;
        public const int DrivableMask = 1 << DrivableLayer;
        const float ChunkSize = 200f;
        const float Piece = 100f;
        const float ColumnSpacing = 30f;
        const float SurfaceLift = 0.06f;

        public static FreewayGraph Build(WorldData data, WorldContext ctx, Material material)
        {
            var asset = Resources.Load<TextAsset>("World/freeway");
            if (asset == null) return null;
            var graph = new FreewayGraph(JsonUtility.FromJson<FreewayData>(asset.text), ctx.Roads, ctx.Bounds);
            var rails = new List<Vector2[]>();
            foreach (var rail in data.rails) rails.Add(Geometry.ToPoints(rail.pts));

            var visuals = new ChunkedMeshBuilder(ChunkSize);
            var surfaces = new ChunkedMeshBuilder(ChunkSize);
            var solids = new ChunkedMeshBuilder(ChunkSize);
            int columns = 0;
            foreach (var way in graph.Ways)
            {
                var bents = Columns(graph, way, ctx, rails);
                columns += bents.Count * LandmarkMeshes.ColumnOffsets(way.Width).Length;
                int pieces = Mathf.Max(1, Mathf.RoundToInt(way.Length / Piece));
                int start = 0;
                for (int k = 0; k < pieces; k++)
                {
                    int end = k == pieces - 1 ? way.Points.Length - 1 : Mathf.Max(start + 1, System.Array.FindIndex(way.Along, s => s >= way.Length * (k + 1) / pieces));
                    if (end <= start) continue;
                    var center = new Vector2[end - start + 1];
                    var heights = new float[center.Length];
                    for (int i = start; i <= end; i++)
                    {
                        center[i - start] = new Vector2(way.Points[i].x, way.Points[i].z);
                        heights[i - start] = Mathf.Max(way.Points[i].y, SurfaceLift);
                    }
                    float s0 = way.Along[start], s1 = way.Along[end];
                    var mine = bents.FindAll(s => s >= s0 && s < s1).ConvertAll(s => s - s0);
                    var mid = center[center.Length / 2];
                    LandmarkMeshes.Viaduct(visuals.At(mid), surfaces.At(mid), solids.At(mid), new LandmarkMeshes.DeckSpec
                    {
                        Centerline = center, Heights = heights, Width = way.Width, Lanes = way.Lanes, Columns = mine,
                        Parapet = (p, h) => Parapet(graph, way, ctx, p, h),
                        BarrierStart = k == 0 && way.ClippedStart && way.Points[0].y > 1f,
                        BarrierEnd = k == pieces - 1 && way.ClippedEnd && way.Points[way.Points.Length - 1].y > 1f,
                    });
                    start = end;
                }
            }

            var root = new GameObject("Freeway").transform;
            root.SetParent(ctx.Root.transform, false);
            foreach (var (key, builder) in visuals.All())
                WorldBuilder.Emit(root, "Freeway " + key, builder.ToMesh("freeway " + key), material, true);
            foreach (var (key, builder) in surfaces.All())
                Collider(root, "Freeway deck " + key, builder.ToMesh("freeway deck " + key), DrivableLayer);
            foreach (var (key, builder) in solids.All())
                Collider(root, "Freeway structure " + key, builder.ToMesh("freeway structure " + key), 0);
            Debug.Log($"[Dogpatch] freeway: {graph.Ways.Count} ways ({graph.Ways.FindAll(w => w.Ramp).Count} ramps), {graph.Junctions.Count} street junctions, {columns} columns, deck {FreewayGraph.DeckHeight} m");
            return graph;
        }

        static void Collider(Transform parent, string name, Mesh mesh, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // Edges keep their barrier unless they sit inside another deck at about the same height (a gore,
        // a merge, a split) or at street level where a ramp runs onto the street.
        static bool Parapet(FreewayGraph graph, FreewayGraph.Way way, WorldContext ctx, Vector2 p, float h)
        {
            if (h < 1f) return false;
            if (graph.SurfaceAt(p, h, 2f, 2f, out _, out _, way.Index, -0.3f)) return false;
            return h > 3f || !ctx.Roads.Nearest(p, 0f, out _);
        }

        // Bent positions (arc lengths) about every 30 m where the deck is high enough to stand on columns,
        // each slid up to 14 m along the deck until its footprint is clear.
        static List<float> Columns(FreewayGraph graph, FreewayGraph.Way way, WorldContext ctx, List<Vector2[]> rails)
        {
            var result = new List<float>();
            var offsets = LandmarkMeshes.ColumnOffsets(way.Width);
            float last = float.NegativeInfinity;
            for (float s = ColumnSpacing * 0.5f; s < way.Length - 3f; s += ColumnSpacing)
                for (int k = 0; k <= 14; k++)
                {
                    float at = s + (k % 2 == 0 ? k : -(k + 1)) * 1f;
                    if (at < 3f || at > way.Length - 3f || at - last < 12f) continue;
                    var (p, dir, h) = Sample(way, at);
                    if (h <= LandmarkMeshes.SolidBelow) continue;
                    bool clear = true;
                    foreach (float o in offsets)
                    {
                        var c = p + new Vector2(-dir.y, dir.x) * o;
                        clear &= !ctx.Roads.Nearest(c, LandmarkMeshes.ColumnRadius + 1f, out _)
                              && !ctx.Buildings.Blocked(c, LandmarkMeshes.ColumnRadius)
                              && !NearRail(rails, c, LandmarkMeshes.ColumnRadius + 2.5f)
                              && !graph.SurfaceAt(c, h - 1.5f, h + 1f, 0f, out _, out _, way.Index, LandmarkMeshes.ColumnRadius + 0.3f);
                    }
                    if (!clear) continue;
                    result.Add(at);
                    last = at;
                    break;
                }
            return result;
        }

        static (Vector2 p, Vector2 dir, float h) Sample(FreewayGraph.Way way, float s)
        {
            int i = 1;
            while (i < way.Along.Length - 1 && way.Along[i] < s) i++;
            float t = Mathf.InverseLerp(way.Along[i - 1], way.Along[i], s);
            var a = way.Points[i - 1];
            var b = way.Points[i];
            var p = Vector3.Lerp(a, b, t);
            return (new Vector2(p.x, p.z), new Vector2(b.x - a.x, b.z - a.z).normalized, p.y);
        }

        static bool NearRail(List<Vector2[]> rails, Vector2 p, float distance)
        {
            foreach (var pts in rails)
                for (int i = 0; i + 1 < pts.Length; i++)
                    if (Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, pts[i], pts[i + 1], out _)) < distance) return true;
            return false;
        }
    }
}
