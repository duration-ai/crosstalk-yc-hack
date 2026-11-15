using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.Glb;
using Dogpatch.Smash;
using UnityEngine;

namespace Dogpatch.World
{
    // Places Polyfork street furniture, trees and rooftop dressing, then merges each chunk's
    // static props into one mesh (vertex colours preserved) to keep draw calls low on iPhone.
    public sealed class PropPlacer
    {
        const float ChunkSize = 200f;
        const float PropCell = 10f;

        readonly WorldContext ctx;
        readonly ModelLibrary models;
        readonly System.Random random;
        readonly Dictionary<Vector2Int, List<Vector2>> placed = new Dictionary<Vector2Int, List<Vector2>>();

        public PropBatch Batch { get; } = new PropBatch(ChunkSize);

        public PropPlacer(WorldContext ctx, ModelLibrary models, System.Random random)
        {
            this.ctx = ctx;
            this.models = models;
            this.random = random;
        }

        string Pick(string role)
        {
            var list = models.Role(role);
            return list.Count == 0 ? null : list[random.Next(list.Count)].id;
        }

        bool Near(Vector2 p, float distance)
        {
            var c = new Vector2Int(Mathf.FloorToInt(p.x / PropCell), Mathf.FloorToInt(p.y / PropCell));
            for (int x = c.x - 1; x <= c.x + 1; x++)
                for (int z = c.y - 1; z <= c.y + 1; z++)
                {
                    if (!placed.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (var q in list) if (Vector2.Distance(p, q) < distance) return true;
                }
            return false;
        }

        // Rejects spots inside buildings, on a roadway, or on top of another prop.
        bool Free(Vector2 p, float clearance)
        {
            if (!ctx.Bounds.Contains(p)) return false;
            if (ctx.Buildings.Blocked(p, clearance)) return false;
            if (ctx.Roads.Nearest(p, clearance, out _)) return false;
            return !Near(p, clearance + 0.8f);
        }

        bool Place(string role, Vector2 p, float yaw, float y = 0.03f, float colliderRadius = 0f, float scale = 1f)
        {
            string id = Pick(role);
            if (id == null) return false;
            var asset = models.Get(id);
            if (asset == null) return false;
            var mesh = asset.BakeStatic();
            if (mesh.vertexCount == 0) return false;

            var position = new Vector3(p.x, y, p.y);
            Batch.Add(id, mesh, Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale), colliderRadius, Mathf.Min(mesh.bounds.size.y * scale, 4f));

            var cell = new Vector2Int(Mathf.FloorToInt(p.x / PropCell), Mathf.FloorToInt(p.y / PropCell));
            if (!placed.TryGetValue(cell, out var pl)) placed[cell] = pl = new List<Vector2>();
            pl.Add(p);
            return true;
        }

        static float Yaw(Vector2 forward) => Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;

        public void PlaceStreetDressing(WorldData data)
        {
            var net = ctx.Roads;
            for (int r = 0; r < net.Roads.Length; r++)
            {
                var road = net.Roads[r];
                if (road.kind == "path" || road.kind == "service" || road.kind == "track") continue;
                var pts = net.Points[r];
                float hw = RoadNetwork.HalfWidth(road);
                bool primary = road.kind == "primary" || road.kind == "secondary";
                float lightPhase = (float)random.NextDouble() * 30f;
                float treePhase = (float)random.NextDouble() * 12f;
                float walked = 0f;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float len = Vector2.Distance(pts[i], pts[i + 1]);
                    if (len < 0.5f) continue;
                    var d = (pts[i + 1] - pts[i]) / len;
                    var left = new Vector2(-d.y, d.x);
                    for (float t = Mod(-walked + lightPhase, 32f); t < len; t += 32f)
                    {
                        float side = ((int)((walked + t) / 32f) % 2 == 0) ? 1f : -1f;
                        var p = pts[i] + d * t + left * side * (hw + 0.8f);
                        if (net.DistanceToIntersection(p, 10f) < 7f || !Free(p, 0.4f)) continue;
                        Place("streetlight", p, Yaw(-left * side), 0.03f, 0.2f);
                    }
                    for (float t = Mod(-walked + treePhase, 13f); t < len; t += 13f)
                    {
                        if (random.NextDouble() < 0.35) continue;
                        float side = random.Next(2) == 0 ? 1f : -1f;
                        var p = pts[i] + d * t + left * side * (hw + 2.2f);
                        if (net.DistanceToIntersection(p, 12f) < 9f || !Free(p, 0.9f)) continue;
                        Place("tree", p, (float)random.NextDouble() * 360f, 0.03f, 0.35f, 0.85f + (float)random.NextDouble() * 0.3f);
                    }
                    if (primary)
                    {
                        for (float t = (float)random.NextDouble() * 40f; t < len; t += 40f + (float)random.NextDouble() * 25f)
                        {
                            float side = random.Next(2) == 0 ? 1f : -1f;
                            var p = pts[i] + d * t + left * side * (hw + 1.4f);
                            if (net.DistanceToIntersection(p, 10f) < 8f || !Free(p, 0.6f)) continue;
                            string[] roles = { "bench", "bin", "newsbox", "meter", "busshelter" };
                            string role = roles[random.Next(roles.Length)];
                            Place(role, p, Yaw(-left * side), 0.03f, role == "busshelter" ? 0f : 0.3f);
                        }
                    }
                    walked += len;
                }
            }

            foreach (var x in net.Intersections)
            {
                if (!net.Nearest(x, 0f, out var hit)) continue;
                var road = net.Roads[hit.Road];
                float hw = RoadNetwork.HalfWidth(road);
                var d = hit.Direction;
                var n = new Vector2(-d.y, d.x);
                bool major = road.kind == "primary" || road.kind == "secondary" || road.kind == "tertiary";
                if (major)
                {
                    var corner = x + n * (hw + 1.0f) - d * (hw + 2.5f);
                    if (Free(corner, 0.4f)) Place("trafficlight", corner, Yaw(d), 0.03f, 0.2f);
                    var opposite = x - n * (hw + 1.0f) + d * (hw + 2.5f);
                    if (Free(opposite, 0.4f)) Place("trafficlight", opposite, Yaw(-d), 0.03f, 0.2f);
                }
                if (random.NextDouble() < 0.45)
                {
                    var spot = x + n * (hw + 0.7f) + d * (hw + 3.5f);
                    if (Free(spot, 0.4f)) Place("hydrant", spot, Yaw(-n), 0.03f, 0.25f);
                }
            }

            for (int i = 0; i + 1 < data.trees.Length; i += 2)
            {
                var p = new Vector2(data.trees[i], data.trees[i + 1]);
                if (Free(p, 0.6f)) Place("tree", p, (float)random.NextDouble() * 360f, 0.03f, 0.35f);
            }

            foreach (var park in data.parks)
            {
                var poly = Geometry.ToPoints(park.pts);
                if (poly.Length < 3) continue;
                var rect = Geometry.BoundsOf(poly);
                int count = Mathf.Min(40, Mathf.RoundToInt(Mathf.Abs(Geometry.SignedArea(poly)) / 180f));
                for (int k = 0, tries = 0; k < count && tries < count * 6; tries++)
                {
                    var p = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, (float)random.NextDouble()), Mathf.Lerp(rect.yMin, rect.yMax, (float)random.NextDouble()));
                    if (!Geometry.Contains(poly, p) || !Free(p, 2f)) continue;
                    bool bench = random.NextDouble() < 0.15;
                    if (Place(bench ? "bench" : "tree", p, (float)random.NextDouble() * 360f, 0.03f, bench ? 0f : 0.35f)) k++;
                }
            }
        }

        public void PlaceRooftops(WorldData data, List<(Vector2[] poly, float height)> buildings)
        {
            foreach (var (poly, height) in buildings)
            {
                float area = Geometry.SignedArea(poly);
                if (area < 220f || height < 7f || height > 70f) continue;
                var c = Geometry.Centroid(poly);
                if (!Geometry.Contains(poly, c)) continue;
                double roll = random.NextDouble();
                if (roll < 0.18) PlaceRoof("rooftop-water-tower-b38362", c, height);
                else if (roll < 0.6) PlaceRoof("rooftop-ac-unit-6ccb3c", c, height);
            }
        }

        void PlaceRoof(string id, Vector2 p, float height)
        {
            var asset = models.Get(id);
            if (asset == null) return;
            Batch.Add(id, asset.BakeStatic(), Matrix4x4.TRS(new Vector3(p.x, height, p.y), Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Vector3.one));
        }

        public void Emit(Transform parent, Material material) => Batch.Emit(parent, material);

        // A few animated locals standing around near the start and in the parks.
        public void PlaceNpcs(WorldContext world, Material material)
        {
            var spawn = new Vector2(world.PlayerSpawn.Position.x, world.PlayerSpawn.Position.z);
            int placedCount = 0;
            for (int tries = 0; tries < 200 && placedCount < 8; tries++)
            {
                float radius = placedCount < 3 ? 25f : 160f;
                var p = spawn + Random2(radius);
                if (Vector2.Distance(p, spawn) < 6f || !Free(p, 1f)) continue;
                string id = Pick("npc");
                var asset = id == null ? null : models.Get(id);
                if (asset == null) return;
                var npc = asset.Instantiate(material, world.Root.transform);
                npc.name = "NPC " + id;
                npc.transform.position = new Vector3(p.x, 0.03f, p.y);
                npc.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                npc.GetComponent<GlbAnimator>()?.Play("Idle");
                var cap = npc.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f;
                cap.height = 1.7f;
                cap.center = new Vector3(0f, 0.85f, 0f);
                placedCount++;
            }
        }

        Vector2 Random2(float radius)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)random.NextDouble()) * radius;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        static float Mod(float a, float m) => ((a % m) + m) % m;
    }
}
