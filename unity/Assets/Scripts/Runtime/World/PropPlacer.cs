using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.World
{
    // Places the animated locals. Street furniture, trees and buildings come from the extract
    // through StreetFurniture, BuildingFitter and Landmarks.
    public sealed class PropPlacer
    {
        readonly WorldContext ctx;
        readonly ModelLibrary models;
        readonly System.Random random;

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

        bool Free(Vector2 p, float clearance)
        {
            if (!ctx.Bounds.Contains(p)) return false;
            if (ctx.Buildings.Blocked(p, clearance)) return false;
            return !ctx.Roads.Nearest(p, clearance, out _);
        }

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
    }
}
