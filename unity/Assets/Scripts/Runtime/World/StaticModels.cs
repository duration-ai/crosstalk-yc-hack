using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    // Collects static Polyfork placements and generated geometry per layer and per 100 m chunk, then
    // merges each (layer, chunk) into one mesh on the shared flat material, so the whole city is a
    // few hundred draw calls before culling. DistanceLod hides detail layers far from the camera.
    public sealed class StaticModels
    {
        public const float ChunkSize = 100f;

        public enum Layer { Buildings, BuildingsFar, Trees, Props, Landmarks }

        readonly ModelLibrary models;
        readonly Dictionary<(Layer, Vector2Int), List<CombineInstance>> instances = new Dictionary<(Layer, Vector2Int), List<CombineInstance>>();
        readonly Dictionary<(Layer, Vector2Int), MeshBuilder> generated = new Dictionary<(Layer, Vector2Int), MeshBuilder>();
        readonly Dictionary<Layer, long> triangles = new Dictionary<Layer, long>();
        readonly Dictionary<string, int> uses = new Dictionary<string, int>();

        public StaticModels(ModelLibrary models) => this.models = models;

        public IReadOnlyDictionary<string, int> Uses => uses;
        public long Triangles(Layer layer) => triangles.TryGetValue(layer, out var t) ? t : 0;

        public static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));

        public Mesh Mesh(string id) => models.Get(id)?.BakeStatic();

        // Places a variant so that its footprint centre and base sit at the origin of 'placement'.
        public bool Place(Layer layer, string id, Vector2 at, Matrix4x4 placement)
        {
            var mesh = Mesh(id);
            if (mesh == null || mesh.vertexCount == 0) return false;
            var b = mesh.bounds;
            var key = (layer, ChunkOf(at));
            if (!instances.TryGetValue(key, out var list)) instances[key] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = placement * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z)) });
            triangles[layer] = Triangles(layer) + mesh.triangles.Length / 3;
            uses.TryGetValue(id, out int n);
            uses[id] = n + 1;
            return true;
        }

        // Places a variant by its own origin (poles, signs and trees stand on their origin).
        public bool PlaceAtOrigin(Layer layer, string id, Vector2 at, Matrix4x4 placement)
        {
            var mesh = Mesh(id);
            if (mesh == null || mesh.vertexCount == 0) return false;
            var key = (layer, ChunkOf(at));
            if (!instances.TryGetValue(key, out var list)) instances[key] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = placement * Matrix4x4.Translate(new Vector3(0f, -mesh.bounds.min.y, 0f)) });
            triangles[layer] = Triangles(layer) + mesh.triangles.Length / 3;
            uses.TryGetValue(id, out int n);
            uses[id] = n + 1;
            return true;
        }

        public MeshBuilder Generated(Layer layer, Vector2 at)
        {
            var key = (layer, ChunkOf(at));
            if (!generated.TryGetValue(key, out var mb)) generated[key] = mb = new MeshBuilder();
            return mb;
        }

        public void Emit(Transform parent, Material material, DistanceLod lod)
        {
            var keys = new HashSet<(Layer, Vector2Int)>(instances.Keys);
            keys.UnionWith(generated.Keys);
            foreach (var key in keys)
            {
                var parts = new List<CombineInstance>();
                if (instances.TryGetValue(key, out var list)) parts.AddRange(list);
                if (generated.TryGetValue(key, out var mb) && mb.VertexCount > 0)
                {
                    var g = mb.ToMesh("generated");
                    parts.Add(new CombineInstance { mesh = g, transform = Matrix4x4.identity });
                    if (key.Item1 != Layer.BuildingsFar) triangles[key.Item1] = Triangles(key.Item1) + g.triangles.Length / 3;
                }
                if (parts.Count == 0) continue;
                var name = key.Item1 + " " + key.Item2.x + "," + key.Item2.y;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), true, true);
                mesh.RecalculateBounds();
                bool shadows = key.Item1 != Layer.BuildingsFar;
                var go = WorldBuilder.Emit(parent, name, mesh, material, shadows);
                lod.Add(key.Item1, go.GetComponent<MeshRenderer>());
            }
        }
    }
}
