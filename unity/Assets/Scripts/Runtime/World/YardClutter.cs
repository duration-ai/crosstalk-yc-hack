using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.World
{
    // Clutter for the open yards the ground cover lays down: rows of shipping containers stacked
    // one to three high on industrial yards, pallet stacks and crates on yards and dirt lots. Flat
    // colour boxes with darker door frames, merged into one mesh, with box colliders so cars bump
    // into them. Kept clear of streets, buildings, rails, parking lots and the race start and finish.
    public static class YardClutter
    {
        static Color L(string hex) => SpeedDial.Hex(hex).linear;

        static readonly Color[] Containers = { L("#E2603F"), L("#2C4FA3"), L("#8CE3BF"), L("#FFE27A"), L("#9ED7FF"), L("#C94A32"), L("#FF9C86") };
        static readonly Color Wood = L("#C9A27A"), WoodDark = L("#A9825C");

        public static void Build(WorldContext ctx, List<(string kind, Vector2[] poly)> ground, List<Vector2[]> rails, Vector2[] keepClear, Material material)
        {
            var random = new System.Random(707);
            var mb = new MeshBuilder();
            var root = new GameObject("YardClutter");
            root.transform.SetParent(ctx.Root.transform, false);
            var parking = new List<Vector2[]>();
            foreach (var (kind, poly) in ground) if (kind == "parking") parking.Add(poly);

            bool Clear(Vector2 p, float radius)
            {
                if (ctx.Buildings.Blocked(p, radius + 2f)) return false;
                if (ctx.Roads.Nearest(p, radius + 8f, out _)) return false;
                foreach (var c in keepClear) if (Vector2.Distance(p, c) < 45f) return false;
                foreach (var lot in parking) if (Geometry.Contains(lot, p)) return false;
                foreach (var rail in rails)
                    for (int i = 0; i < rail.Length - 1; i++)
                        if (Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, rail[i], rail[i + 1], out _)) < radius + 5f) return false;
                return true;
            }

            int clusters = 0, boxes = 0;
            foreach (var (kind, poly) in ground)
            {
                bool yard = kind == "industrial";
                if (!yard && kind != "dirt") continue;
                var bounds = Geometry.BoundsOf(poly);
                float area = Mathf.Abs(Geometry.SignedArea(poly));
                int wanted = Mathf.FloorToInt(area / (yard ? 2200f : 4500f));
                for (int attempt = 0; attempt < wanted * 10 && wanted > 0; attempt++)
                {
                    var p = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, (float)random.NextDouble()), Mathf.Lerp(bounds.yMin, bounds.yMax, (float)random.NextDouble()));
                    bool containers = yard && random.NextDouble() < 0.6;
                    float radius = containers ? 9f : 4f;
                    if (!Geometry.Contains(poly, p) || !Clear(p, radius)) continue;
                    float yaw = random.Next(4) * 90f + (float)(random.NextDouble() - 0.5) * 8f;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var holder = new GameObject("Yard " + clusters);
                    holder.transform.SetParent(root.transform, false);
                    holder.transform.SetPositionAndRotation(new Vector3(p.x, 0f, p.y), rot);
                    void Solid(Vector3 local, Vector3 size, Color color)
                    {
                        mb.Box(holder.transform.TransformPoint(local), size, rot, color);
                        var box = holder.AddComponent<BoxCollider>();
                        box.center = local;
                        box.size = size;
                        boxes++;
                    }
                    if (containers)
                    {
                        int columns = 2 + random.Next(4);
                        for (int c = 0; c < columns; c++)
                        {
                            int high = 1 + random.Next(random.NextDouble() < 0.3 ? 3 : 2);
                            for (int h = 0; h < high; h++)
                            {
                                var color = Containers[random.Next(Containers.Length)];
                                var at = new Vector3((c - (columns - 1) * 0.5f) * 2.6f, 1.3f + h * 2.6f, 0f);
                                Solid(at, new Vector3(2.44f, 2.59f, 12.2f), color);
                                var frame = color * 0.72f;
                                mb.Box(holder.transform.TransformPoint(at + new Vector3(0f, 0f, 6.05f)), new Vector3(2.5f, 2.63f, 0.18f), rot, frame);
                                mb.Box(holder.transform.TransformPoint(at + new Vector3(0f, 0f, -6.05f)), new Vector3(2.5f, 2.63f, 0.18f), rot, frame);
                            }
                        }
                    }
                    else
                    {
                        int stacks = 3 + random.Next(5);
                        for (int s = 0; s < stacks; s++)
                        {
                            var at = new Vector2(((float)random.NextDouble() - 0.5f) * 6f, ((float)random.NextDouble() - 0.5f) * 6f);
                            if (random.NextDouble() < 0.5)
                            {
                                float h = 0.15f * (2 + random.Next(7));
                                Solid(new Vector3(at.x, h * 0.5f, at.y), new Vector3(1.2f, h, 1f), random.NextDouble() < 0.5 ? Wood : WoodDark);
                            }
                            else
                            {
                                float e = 0.8f + (float)random.NextDouble() * 0.5f;
                                Solid(new Vector3(at.x, e * 0.5f, at.y), new Vector3(e, e, e), Wood);
                            }
                        }
                    }
                    clusters++;
                    if (--wanted <= 0) break;
                }
            }
            WorldBuilder.Emit(root.transform, "Yard clutter", mb.ToMesh("yard clutter"), material, true);
            Debug.Log($"[YardClutter] {clusters} clusters, {boxes} solid boxes, {mb.VertexCount / 3} tris");
        }
    }
}
