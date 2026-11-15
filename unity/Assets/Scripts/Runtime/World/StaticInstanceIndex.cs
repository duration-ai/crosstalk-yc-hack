using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    public enum PhysicsClass { Static, Breakable, Dynamic }

    // One placed model (or one generated landmark part) inside a merged static chunk.
    public sealed class StaticInstance
    {
        public int Id;
        public string Kind;          // building, tree, prop, landmark
        public string Variant;       // baked model key, or the generated part's name
        public string Asset;         // Polyfork asset id ("" for generated geometry)
        public string SourceId;      // DataSF / OSM / street-tree id it was fitted to
        public PhysicsClass Class;
        public float MassKg;         // Polyfork physics.mass_kg scaled by the instance's scale volume
        public string PolyforkBody;  // "static" or "dynamic" as Polyfork states it
        public string Material;      // Polyfork physics.material
        public string CollidersUrl;  // https://polyfork.dev/cdn/{asset}-colliders.json
        public Matrix4x4 Transform;  // model space (origin at the model's base) to world
        public Bounds WorldBounds;
        public MeshFilter Chunk;
        public int SubMesh;
        public int VertexStart, VertexCount, IndexStart, IndexCount;
        public Collider Collider;    // its own solid collider; null for buildings (footprint collider) and static landmarks (chunk collider)
        internal Mesh Source;
        internal string HullResource;
        internal ColliderHulls Hulls;
    }

    [Serializable]
    public class ColliderHulls
    {
        public string asset;
        public string url;
        public string approximation;
        public string space;
        public HullPart[] parts;
    }

    [Serializable]
    public class HullPart
    {
        public float[] pts;
    }

    // Every static instance in the merged chunks, with what physics needs to pull one out: Detach
    // hides its triangles in the chunk (made degenerate in the index buffer) and turns off its
    // collider, then returns its own mesh to spawn as a dynamic copy at Transform.
    public sealed class StaticInstanceIndex
    {
        const float Cell = 20f;

        sealed class ChunkState
        {
            public Mesh Mesh;
            public int[] Original;
            public int[] Current;
        }

        readonly List<StaticInstance> all = new List<StaticInstance>();
        readonly Dictionary<Collider, int> byCollider = new Dictionary<Collider, int>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
        readonly Dictionary<Mesh, ChunkState> chunks = new Dictionary<Mesh, ChunkState>();
        readonly HashSet<int> detached = new HashSet<int>();
        readonly Dictionary<string, ColliderHulls> hulls = new Dictionary<string, ColliderHulls>();

        public int Count => all.Count;
        public IReadOnlyList<StaticInstance> All => all;
        public StaticInstance Get(int id) => id >= 0 && id < all.Count ? all[id] : null;
        public bool IsDetached(int id) => detached.Contains(id);

        internal int Add(StaticInstance instance)
        {
            instance.Id = all.Count;
            all.Add(instance);
            return instance.Id;
        }

        internal void Bound(StaticInstance instance, Collider collider)
        {
            var b = instance.WorldBounds;
            for (int x = Mathf.FloorToInt(b.min.x / Cell); x <= Mathf.FloorToInt(b.max.x / Cell); x++)
                for (int z = Mathf.FloorToInt(b.min.z / Cell); z <= Mathf.FloorToInt(b.max.z / Cell); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(instance.Id);
                }
            if (collider == null) return;
            instance.Collider = collider;
            byCollider[collider] = instance.Id;
        }

        public bool TryFromCollider(Collider collider, out StaticInstance instance)
        {
            instance = collider != null && byCollider.TryGetValue(collider, out int id) ? all[id] : null;
            return instance != null;
        }

        public StaticInstance FromCollider(Collider collider) => TryFromCollider(collider, out var i) ? i : null;

        public IEnumerable<StaticInstance> Near(Vector3 p, float radius)
        {
            var seen = new HashSet<int>();
            for (int x = Mathf.FloorToInt((p.x - radius) / Cell); x <= Mathf.FloorToInt((p.x + radius) / Cell); x++)
                for (int z = Mathf.FloorToInt((p.z - radius) / Cell); z <= Mathf.FloorToInt((p.z + radius) / Cell); z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int id in list)
                        if (seen.Add(id) && all[id].WorldBounds.SqrDistance(p) <= radius * radius) yield return all[id];
                }
        }

        // Polyfork's convex hulls for the instance's asset, in model space (metres, default knobs).
        public ColliderHulls HullsOf(StaticInstance instance)
        {
            if (instance.Hulls != null || string.IsNullOrEmpty(instance.HullResource)) return instance.Hulls;
            if (!hulls.TryGetValue(instance.HullResource, out var h))
            {
                var text = Resources.Load<TextAsset>(instance.HullResource);
                hulls[instance.HullResource] = h = text == null ? null : JsonUtility.FromJson<ColliderHulls>(text.text);
            }
            return instance.Hulls = h;
        }

        // Hides the instance in its chunk and disables its collider; returns its own mesh, origin at
        // its base (a tree's trunk base), to spawn at instance.Transform. Calling it twice is harmless.
        public Mesh Detach(int id)
        {
            var instance = Get(id);
            if (instance == null || instance.Chunk == null) return null;
            var source = SourceOf(instance);
            if (!detached.Add(id)) return source;
            var state = State(instance.Chunk.sharedMesh);
            for (int i = 0; i < instance.IndexCount; i++) state.Current[instance.IndexStart + i] = state.Original[instance.IndexStart];
            state.Mesh.SetTriangles(state.Current, instance.SubMesh, false);
            if (instance.Collider != null) instance.Collider.enabled = false;
            return source;
        }

        public void Restore(int id)
        {
            var instance = Get(id);
            if (instance == null || !detached.Remove(id)) return;
            var state = State(instance.Chunk.sharedMesh);
            Array.Copy(state.Original, instance.IndexStart, state.Current, instance.IndexStart, instance.IndexCount);
            state.Mesh.SetTriangles(state.Current, instance.SubMesh, false);
            if (instance.Collider != null) instance.Collider.enabled = true;
        }

        ChunkState State(Mesh mesh)
        {
            if (chunks.TryGetValue(mesh, out var state)) return state;
            var tris = mesh.GetTriangles(0);
            chunks[mesh] = state = new ChunkState { Mesh = mesh, Original = tris, Current = (int[])tris.Clone() };
            return state;
        }

        // Generated parts have no model mesh of their own: cut it out of the chunk, back into model space.
        Mesh SourceOf(StaticInstance instance)
        {
            if (instance.Source != null) return instance.Source;
            var chunk = instance.Chunk.sharedMesh;
            var state = State(chunk);
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            chunk.GetVertices(verts);
            chunk.GetColors(cols);
            var toLocal = instance.Transform.inverse;
            var v = new Vector3[instance.VertexCount];
            var c = new Color[instance.VertexCount];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = toLocal.MultiplyPoint3x4(verts[instance.VertexStart + i]);
                c[i] = cols.Count > 0 ? cols[instance.VertexStart + i] : Color.white;
            }
            var t = new int[instance.IndexCount];
            for (int i = 0; i < t.Length; i++) t[i] = state.Original[instance.IndexStart + i] - instance.VertexStart;
            var mesh = new Mesh { name = instance.Variant };
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return instance.Source = mesh;
        }
    }
}
