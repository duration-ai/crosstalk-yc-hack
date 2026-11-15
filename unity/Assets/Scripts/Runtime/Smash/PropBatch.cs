using System.Collections.Generic;
using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Smash
{
    // Merges placed props into one mesh per chunk, gives each prop its own collider, and remembers
    // each prop's index range in its chunk so a single prop can be hidden (its triangles made
    // degenerate) and later shown again without rebuilding the chunk.
    public sealed class PropBatch : IBreakableIndex
    {
        sealed class Item
        {
            public string AssetId;
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public float ColliderRadius;
            public float ColliderHeight;
            public PhysicsEntry Physics;
            public Bounds WorldBounds;
            public Mesh Chunk;
            public int IndexStart;
            public int IndexCount;
            public Collider Collider;
            public bool Detached;
        }

        readonly float chunkSize;
        readonly List<Item> items = new List<Item>();
        readonly Dictionary<Vector2Int, List<int>> chunks = new Dictionary<Vector2Int, List<int>>();
        readonly Dictionary<Collider, int> byCollider = new Dictionary<Collider, int>();
        readonly Dictionary<Mesh, int[]> chunkIndices = new Dictionary<Mesh, int[]>();
        static int[] zeros = new int[0];

        public PropBatch(float chunkSize) => this.chunkSize = chunkSize;

        public int Count => items.Count;

        public void Add(string assetId, Mesh mesh, Matrix4x4 matrix, float colliderRadius = 0f, float colliderHeight = 0f)
        {
            var p = matrix.GetColumn(3);
            var key = new Vector2Int(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));
            if (!chunks.TryGetValue(key, out var list)) chunks[key] = list = new List<int>();
            list.Add(items.Count);
            items.Add(new Item
            {
                AssetId = assetId,
                Mesh = mesh,
                Matrix = matrix,
                ColliderRadius = colliderRadius,
                ColliderHeight = colliderHeight,
                Physics = PhysicsCatalog.Get(assetId),
                WorldBounds = GeometryUtility.CalculateBounds(Corners(mesh.bounds), matrix),
            });
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return c;
        }

        public void Emit(Transform parent, Material material)
        {
            var colliderRoot = new GameObject("PropColliders").transform;
            colliderRoot.SetParent(parent, false);
            foreach (var pair in chunks)
            {
                var combine = new CombineInstance[pair.Value.Count];
                int start = 0;
                var mesh = new Mesh { name = "props " + pair.Key, indexFormat = IndexFormat.UInt32 };
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    var item = items[pair.Value[i]];
                    combine[i] = new CombineInstance { mesh = item.Mesh, transform = item.Matrix };
                    item.Chunk = mesh;
                    item.IndexStart = start;
                    item.IndexCount = (int)item.Mesh.GetIndexCount(0);
                    start += item.IndexCount;
                }
                mesh.CombineMeshes(combine, true, true);
                mesh.RecalculateBounds();
                WorldBuilder.Emit(parent, "Props " + pair.Key, mesh, material, true);
            }

            for (int id = 0; id < items.Count; id++)
            {
                var item = items[id];
                var cls = PhysicsCatalog.ClassOf(item.Physics);
                bool smashable = cls == PhysicsClass.Breakable || cls == PhysicsClass.Loose;
                if (item.ColliderRadius <= 0f && !smashable) continue;
                var go = new GameObject(item.AssetId);
                go.transform.SetParent(colliderRoot, false);
                if (item.ColliderRadius > 0f)
                {
                    go.transform.position = item.Matrix.GetColumn(3);
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius = item.ColliderRadius;
                    capsule.height = Mathf.Max(item.ColliderHeight, item.ColliderRadius * 2f);
                    capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                    item.Collider = capsule;
                }
                else
                {
                    go.transform.SetPositionAndRotation(item.Matrix.GetColumn(3), item.Matrix.rotation);
                    go.transform.localScale = item.Matrix.lossyScale;
                    var box = go.AddComponent<BoxCollider>();
                    box.center = item.Mesh.bounds.center;
                    box.size = item.Mesh.bounds.size;
                    item.Collider = box;
                }
                if (smashable) byCollider[item.Collider] = id;
            }
        }

        public bool TryFromCollider(Collider collider, out int id) => byCollider.TryGetValue(collider, out id);

        public BreakableInfo Info(int id)
        {
            var item = items[id];
            return new BreakableInfo(id, item.AssetId, item.Matrix, item.WorldBounds, item.Physics);
        }

        public bool IsDetached(int id) => items[id].Detached;

        public Mesh Detach(int id)
        {
            var item = items[id];
            if (item.Detached || item.Chunk == null) return item.Mesh;
            if (!chunkIndices.ContainsKey(item.Chunk)) chunkIndices[item.Chunk] = item.Chunk.GetIndices(0);
            if (zeros.Length < item.IndexCount) zeros = new int[item.IndexCount];
            item.Chunk.SetIndexBufferData(zeros, 0, item.IndexStart, item.IndexCount, Flags);
            if (item.Collider != null) item.Collider.enabled = false;
            item.Detached = true;
            return item.Mesh;
        }

        public void Restore(int id)
        {
            var item = items[id];
            if (!item.Detached) return;
            item.Chunk.SetIndexBufferData(chunkIndices[item.Chunk], item.IndexStart, item.IndexStart, item.IndexCount, Flags);
            if (item.Collider != null) item.Collider.enabled = true;
            item.Detached = false;
        }

        const MeshUpdateFlags Flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
    }
}
