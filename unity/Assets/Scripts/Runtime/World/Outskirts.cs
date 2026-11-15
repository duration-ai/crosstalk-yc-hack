using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.World
{
    // Beyond the edge of the playable map the land used to stop and the bay ran on to the horizon,
    // so every street looking west, south or north ended in open water. This fills the land side
    // of the edge out to the horizon: Potrero Hill rising to the west, and low skyline blocks
    // (painted houses on the hill, warehouses to the south, taller Mission Bay blocks to the north)
    // that distance fog softens into the horizon. Where CityRegions streams in the real city, the land
    // is flat and the made-up blocks are left out. Purely scenery: out of reach behind the map-edge
    // walls, no colliders, in 1 km chunks so the camera culls what it isn't facing.
    public static class Outskirts
    {
        const float Reach = 2200f;
        const float Cell = 50f;
        const float SkylineReach = 1300f;

        public static float LandTop(Rect map) => map.yMax + Reach;

        static Color L(string hex) => SpeedDial.Hex(hex).linear;

        static readonly Color[] Houses = { L("#FFE27A"), L("#8CE3BF"), L("#FF9C86"), L("#BBA6F7"), L("#9ED7FF"), L("#FFF3DA") };
        static readonly Color[] Sheds = { L("#C9C6C0"), L("#E2603F"), L("#A3AEC4"), L("#DCD6CC") };
        static readonly Color[] Towers = { L("#FFF3DA"), L("#9ED7FF"), L("#DCD6CC"), L("#C94A32") };
        static readonly Color Roof = L("#6C758B");
        static readonly Color GroundA = L("#E3D9C5"), GroundB = L("#D6CCB6");

        public static void Build(Transform parent, Rect map, float coastNorthX, float coastEastZ, Material ground, Material material)
        {
            var random = new System.Random(2240);
            var real = CityRegions.Active();
            var landChunks = new ChunkedMeshBuilder(1000f);
            var blockChunks = new ChunkedMeshBuilder(1000f);

            // Land outside the map on its land sides, as one height field: Potrero Hill to the west,
            // flat elsewhere. Cells that are bay (north-east of the coast where it meets the map) are
            // skipped; a cell inside the map is skipped too.
            float x0 = map.xMin - Reach, x1 = map.xMax + 300f, z0 = map.yMin - Reach, z1 = map.yMax + Reach;
            bool Land(Vector2 p)
            {
                if (map.Contains(p)) return false;
                if (p.y > map.yMax) return p.x < coastNorthX + (p.y - map.yMax) * 0.3f;
                if (p.x > map.xMax) return p.y < coastEastZ - (p.x - map.xMax) * 0.5f;
                return true;
            }
            float Height(Vector2 p)
            {
                float d = Vector2.Distance(p, new Vector2(map.xMin - 750f, map.center.y - 150f));
                float hill = 75f * Mathf.Exp(-d * d / (650f * 650f));
                float outside = Mathf.Max(Mathf.Max(map.xMin - p.x, p.x - map.xMax), Mathf.Max(map.yMin - p.y, p.y - map.yMax));
                foreach (var (_, rect) in real)
                    hill *= Mathf.SmoothStep(0f, 1f, Mathf.Max(Mathf.Max(rect.xMin - p.x, p.x - rect.xMax), Mathf.Max(rect.yMin - p.y, p.y - rect.yMax)) / 200f);
                return hill * Mathf.SmoothStep(0f, 1f, outside / 300f);
            }
            Vector3 V(Vector2 p) => new Vector3(p.x, Height(p), p.y);
            for (float x = x0; x < x1; x += Cell)
                for (float z = z0; z < z1; z += Cell)
                {
                    var a = new Vector2(x, z);
                    var b = new Vector2(x + Cell, z);
                    var c = new Vector2(x + Cell, z + Cell);
                    var d = new Vector2(x, z + Cell);
                    if (!Land((a + c) * 0.5f)) continue;
                    var tone = Color.Lerp(GroundA, GroundB, (float)random.NextDouble());
                    var land = landChunks.At(a);
                    land.Triangle(V(a), V(d), V(c), tone, Vector3.up);
                    land.Triangle(V(a), V(c), V(b), tone, Vector3.up);
                }

            // Skyline blocks, densest near the map and thinning out. Streets every 110 m stay clear.
            for (float x = x0; x < x1; x += 14f)
                for (float z = z0; z < z1; z += 14f)
                {
                    var p = new Vector2(x + (float)random.NextDouble() * 4f, z + (float)random.NextDouble() * 4f);
                    if (!Land(p) || real.Exists(r => r.rect.Contains(p))) continue;
                    if (Mathf.Repeat(p.x, 110f) < 16f || Mathf.Repeat(p.y, 110f) < 16f) continue;
                    float gap = Mathf.Max(Mathf.Max(map.xMin - p.x, p.x - map.xMax), Mathf.Max(map.yMin - p.y, p.y - map.yMax));
                    if (gap < 40f || gap > SkylineReach || random.NextDouble() > Mathf.Lerp(0.45f, 0.04f, gap / SkylineReach)) continue;
                    float y = Height(p);
                    bool north = p.y > map.yMax, south = p.y < map.yMin && p.x > map.xMin;
                    Vector3 size;
                    Color color;
                    if (north)
                    {
                        if (random.NextDouble() > 0.35) continue;
                        size = new Vector3(18f + (float)random.NextDouble() * 16f, 18f + (float)random.NextDouble() * 34f, 18f + (float)random.NextDouble() * 16f);
                        color = Towers[random.Next(Towers.Length)];
                    }
                    else if (south)
                    {
                        if (random.NextDouble() > 0.3) continue;
                        size = new Vector3(22f + (float)random.NextDouble() * 20f, 7f + (float)random.NextDouble() * 6f, 26f + (float)random.NextDouble() * 22f);
                        color = Sheds[random.Next(Sheds.Length)];
                    }
                    else
                    {
                        size = new Vector3(7f + (float)random.NextDouble() * 3f, 7f + (float)random.NextDouble() * 5f, 11f + (float)random.NextDouble() * 5f);
                        color = Houses[random.Next(Houses.Length)];
                    }
                    var rotation = Quaternion.Euler(0f, random.Next(2) * 90f, 0f);
                    var blocks = blockChunks.At(p);
                    blocks.Box(new Vector3(p.x, y + size.y * 0.5f - 1f, p.y), size, rotation, color);
                    blocks.Box(new Vector3(p.x, y + size.y - 0.6f, p.y), new Vector3(size.x + 0.6f, 1.2f, size.z + 0.6f), rotation, Roof);
                }

            int landTris = 0, skylineTris = 0;
            foreach (var (key, mb) in landChunks.All())
            {
                landTris += mb.VertexCount / 3;
                WorldBuilder.Emit(parent, "Outskirts land " + key, mb.ToMesh("outskirts land " + key), ground, false);
            }
            foreach (var (key, mb) in blockChunks.All())
            {
                skylineTris += mb.VertexCount / 3;
                WorldBuilder.Emit(parent, "Outskirts skyline " + key, mb.ToMesh("outskirts skyline " + key), material, false);
            }
            Debug.Log($"[Outskirts] land {landTris} tris, skyline {skylineTris} tris, in 1 km chunks");
        }
    }
}
