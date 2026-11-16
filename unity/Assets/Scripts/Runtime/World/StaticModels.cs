using System;
using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    // Collects static Polyfork placements and generated geometry per layer and per 100 m chunk, then
    // merges each (layer, chunk) into one mesh on the shared flat material, so the whole city is a
    // few hundred draw calls before culling. DistanceLod hides detail layers far from the camera.
    // Every placement is recorded in Index with its range in the merged chunk and its physics data.
    public sealed class StaticModels
    {
        public const float ChunkSize = 100f;

        public enum Layer { Buildings, BuildingsFar, Trees, Props, Landmarks }

        sealed class Part
        {
            public int Instance = -1;
            public Mesh Mesh;
            public Matrix4x4 Transform;
            public float CapsuleRadius;
            public float CapsuleHeight;
            public int VertexStart, IndexStart, VertexCount, IndexCount;
            public Vector3 BoxSize;
        }

        readonly ModelLibrary models;
        readonly Dictionary<(Layer, Vector2Int), List<Part>> instances = new Dictionary<(Layer, Vector2Int), List<Part>>();
        readonly Dictionary<(Layer, Vector2Int), MeshBuilder> generated = new Dictionary<(Layer, Vector2Int), MeshBuilder>();
        readonly Dictionary<(Layer, Vector2Int), List<Part>> generatedParts = new Dictionary<(Layer, Vector2Int), List<Part>>();
        readonly Dictionary<Layer, long> triangles = new Dictionary<Layer, long>();
        readonly Dictionary<string, int> uses = new Dictionary<string, int>();
        readonly Dictionary<string, Mesh> hullMeshes = new Dictionary<string, Mesh>();

        public readonly StaticInstanceIndex Index = new StaticInstanceIndex();

        public StaticModels(ModelLibrary models) => this.models = models;

        public IReadOnlyDictionary<string, int> Uses => uses;
        public long Triangles(Layer layer) => triangles.TryGetValue(layer, out var t) ? t : 0;

        public static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));

        public Mesh Mesh(string id) => models.Get(id)?.BakeStatic();

        static string KindOf(Layer layer) => layer switch
        {
            Layer.Buildings => "building",
            Layer.Trees => "tree",
            Layer.Props => "prop",
            _ => "landmark",
        };

        // Places a variant so that its footprint centre and base sit at the origin of 'placement'.
        public int Place(Layer layer, string id, Vector2 at, Matrix4x4 placement, string sourceId = null)
        {
            var mesh = Mesh(id);
            if (mesh == null || mesh.vertexCount == 0) return -1;
            var b = mesh.bounds;
            return Add(layer, id, at, mesh, placement * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z)), sourceId, 0f, 0f);
        }

        // Places a variant by its own origin (poles, signs and trees stand on their origin). A capsule
        // radius gives the instance a solid capsule; a breakable instance without one gets its Polyfork hull.
        public int PlaceAtOrigin(Layer layer, string id, Vector2 at, Matrix4x4 placement, string sourceId = null, float capsuleRadius = 0f, float capsuleHeight = 2.5f)
        {
            var mesh = Mesh(id);
            if (mesh == null || mesh.vertexCount == 0) return -1;
            return Add(layer, id, at, mesh, placement * Matrix4x4.Translate(new Vector3(0f, -mesh.bounds.min.y, 0f)), sourceId, capsuleRadius, capsuleHeight);
        }

        int Add(Layer layer, string id, Vector2 at, Mesh mesh, Matrix4x4 transform, string sourceId, float capsuleRadius, float capsuleHeight)
        {
            var entry = models.Entry(id);
            var s = transform.lossyScale;
            var cls = layer == Layer.Buildings || layer == Layer.BuildingsFar || entry == null || entry.physicsClass != "breakable" ? PhysicsClass.Static : PhysicsClass.Breakable;
            int instance = Index.Add(new StaticInstance
            {
                Kind = KindOf(layer), Variant = id, Asset = entry?.asset ?? "", SourceId = sourceId ?? "", Class = cls,
                MassKg = (entry?.massKg ?? 0f) * Mathf.Abs(s.x * s.y * s.z), PolyforkBody = entry?.body ?? "", Material = entry?.material ?? "",
                CollidersUrl = entry?.collidersUrl ?? "", Transform = transform, Source = mesh, HullResource = entry?.colliders ?? "",
                WorldBounds = TransformBounds(mesh.bounds, transform),
            });
            var key = (layer, ChunkOf(at));
            if (!instances.TryGetValue(key, out var list)) instances[key] = list = new List<Part>();
            list.Add(new Part { Instance = instance, Mesh = mesh, Transform = transform, CapsuleRadius = capsuleRadius, CapsuleHeight = capsuleHeight });
            triangles[layer] = Triangles(layer) + mesh.triangles.Length / 3;
            uses.TryGetValue(id, out int n);
            uses[id] = n + 1;
            return instance;
        }

        public MeshBuilder Generated(Layer layer, Vector2 at)
        {
            var key = (layer, ChunkOf(at));
            if (!generated.TryGetValue(key, out var mb)) generated[key] = mb = new MeshBuilder();
            return mb;
        }

        // Generated geometry that physics treats as one object (a billboard on its legs): draw writes
        // it, pivot is its base. Breakable parts get a solid box collider of boxSize around the pivot.
        public int GeneratedInstance(Layer layer, Vector2 at, string name, string sourceId, PhysicsClass cls, float massKg, string material, Matrix4x4 pivot, Vector3 boxSize, Action<MeshBuilder> draw)
        {
            var key = (layer, ChunkOf(at));
            var mb = Generated(layer, at);
            int v0 = mb.VertexCount, i0 = mb.IndexCount;
            draw(mb);
            int instance = Index.Add(new StaticInstance
            {
                Kind = KindOf(layer), Variant = name, Asset = "", SourceId = sourceId ?? "", Class = cls, MassKg = massKg,
                PolyforkBody = "", Material = material, CollidersUrl = "", Transform = pivot, HullResource = "",
            });
            if (!generatedParts.TryGetValue(key, out var list)) generatedParts[key] = list = new List<Part>();
            list.Add(new Part { Instance = instance, VertexStart = v0, IndexStart = i0, VertexCount = mb.VertexCount - v0, IndexCount = mb.IndexCount - i0, Transform = pivot, BoxSize = boxSize });
            return instance;
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        static int IndexCount(Mesh mesh)
        {
            long n = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s);
            return (int)n;
        }

        // Streaming (in the game, unless -no-stream-world): detailed building chunks, most of the merged-mesh
        // memory, are built only within StreamLoad of the camera and freed past StreamUnload (WorldStreamer).
        // Footprint colliders stay loaded, so buildings are always solid; plain extrusions show elsewhere.
        // Editor batch tools (WorldRenders, WorldReport) build everything, since nothing streams between views.
        public static bool Streaming => Application.isPlaying && Array.IndexOf(Environment.GetCommandLineArgs(), "-no-stream-world") < 0;
        public const float StreamLoad = 360f, StreamUnload = 480f;

        Transform emitParent;
        Material emitMaterial;
        DistanceLod emitLod;
        readonly HashSet<Vector2Int> streamed = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, GameObject> loaded = new Dictionary<Vector2Int, GameObject>();

        public void Emit(Transform parent, Material material, DistanceLod lod)
        {
            var breakables = new GameObject("Breakables").transform;
            breakables.SetParent(parent, false);
            emitParent = parent;
            emitMaterial = material;
            emitLod = lod;
            bool streaming = Streaming;
            var keys = new HashSet<(Layer, Vector2Int)>(instances.Keys);
            keys.UnionWith(generated.Keys);
            foreach (var key in keys)
            {
                if (streaming && key.Item1 == Layer.Buildings)
                {
                    streamed.Add(key.Item2);
                    if (instances.TryGetValue(key, out var parts))
                        foreach (var part in parts) Index.Bound(Index.Get(part.Instance), null);
                    continue;
                }
                EmitChunk(key, parent, material, lod, breakables, true);
            }
        }

        // Builds the streamed detail chunks within StreamLoad of eye (nearest first, at most budget of
        // them) and frees those past StreamUnload. Returns how many it built.
        public int Stream(Vector3 eye, int budget)
        {
            if (emitParent == null) return 0;
            var at = new Vector2(eye.x, eye.z);
            float Distance(Vector2Int c) => Vector2.Distance(at, new Vector2(Mathf.Clamp(at.x, c.x * ChunkSize, (c.x + 1) * ChunkSize), Mathf.Clamp(at.y, c.y * ChunkSize, (c.y + 1) * ChunkSize)));
            var far = new List<Vector2Int>();
            foreach (var (chunk, _) in loaded) if (Distance(chunk) > StreamUnload) far.Add(chunk);
            foreach (var chunk in far) Unload(chunk);
            var near = new List<Vector2Int>();
            foreach (var chunk in streamed) if (!loaded.ContainsKey(chunk) && Distance(chunk) < StreamLoad) near.Add(chunk);
            near.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
            int built = 0;
            foreach (var chunk in near)
            {
                if (built >= budget) break;
                EmitChunk((Layer.Buildings, chunk), emitParent, emitMaterial, emitLod, null, false);
                if (!loaded.ContainsKey(chunk)) loaded[chunk] = null;
                built++;
            }
            return built;
        }

        void Unload(Vector2Int chunk)
        {
            var go = loaded[chunk];
            loaded.Remove(chunk);
            if (instances.TryGetValue((Layer.Buildings, chunk), out var parts))
                foreach (var part in parts) Index.Get(part.Instance).Chunk = null;
            if (go == null) return;
            var filter = go.GetComponent<MeshFilter>();
            emitLod.Remove(go.GetComponent<MeshRenderer>());
            Index.Forget(filter.sharedMesh);
            UnityEngine.Object.Destroy(filter.sharedMesh);
            UnityEngine.Object.Destroy(go);
        }

        void EmitChunk((Layer, Vector2Int) key, Transform parent, Material material, DistanceLod lod, Transform breakables, bool bind)
        {
            var combine = new List<CombineInstance>();
            Mesh g = null;
            var parts = new List<Part>();
            int vertexOffset = 0, indexOffset = 0;
            if (instances.TryGetValue(key, out var list))
                foreach (var part in list)
                {
                    part.VertexStart = vertexOffset;
                    part.IndexStart = indexOffset;
                    part.VertexCount = part.Mesh.vertexCount;
                    part.IndexCount = IndexCount(part.Mesh);
                    vertexOffset += part.VertexCount;
                    indexOffset += part.IndexCount;
                    combine.Add(new CombineInstance { mesh = part.Mesh, transform = part.Transform });
                    parts.Add(part);
                }
            if (generated.TryGetValue(key, out var mb) && mb.VertexCount > 0)
            {
                g = mb.ToMesh("generated");
                combine.Add(new CombineInstance { mesh = g, transform = Matrix4x4.identity });
                if (key.Item1 != Layer.BuildingsFar && bind) triangles[key.Item1] = Triangles(key.Item1) + g.triangles.Length / 3;
                if (generatedParts.TryGetValue(key, out var gparts))
                    foreach (var part in gparts)
                    {
                        part.VertexStart += vertexOffset;
                        part.IndexStart += indexOffset;
                        parts.Add(part);
                    }
            }
            if (combine.Count == 0) return;
            var name = key.Item1 + " " + key.Item2.x + "," + key.Item2.y;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            mesh.RecalculateBounds();
            if (g != null && !bind) UnityEngine.Object.Destroy(g);
            Compact(mesh);
            bool shadows = key.Item1 != Layer.BuildingsFar;
            var go = WorldBuilder.Emit(parent, name, mesh, material, shadows);
            lod.Add(key.Item1, key.Item2, go.GetComponent<MeshRenderer>());
            if (!bind) loaded[key.Item2] = go;
            var filter = go.GetComponent<MeshFilter>();

            var breakableRanges = new List<(int start, int count)>();
            foreach (var part in parts)
            {
                var instance = Index.Get(part.Instance);
                instance.Chunk = filter;
                instance.SubMesh = 0;
                instance.VertexStart = part.VertexStart;
                instance.VertexCount = part.VertexCount;
                instance.IndexStart = part.IndexStart;
                instance.IndexCount = part.IndexCount;
                if (!bind) continue;
                if (part.Mesh == null) instance.WorldBounds = RangeBounds(mesh, part);
                Index.Bound(instance, Collide(breakables, instance, part));
                if (key.Item1 == Layer.Landmarks && instance.Class != PhysicsClass.Static) breakableRanges.Add((part.IndexStart, part.IndexCount));
            }

            // Viaduct columns, the stack, crane gantries and platforms collide as built. Breakable
            // parts (billboards, shelters) have their own colliders, so they are left out; a chunk
            // holding only breakables gets no chunk collider.
            if (key.Item1 == Layer.Landmarks)
            {
                var collider = mesh;
                if (breakableRanges.Count > 0)
                {
                    var all = mesh.GetTriangles(0);
                    var keep = new bool[all.Length];
                    for (int i = 0; i < keep.Length; i++) keep[i] = true;
                    foreach (var (start, count) in breakableRanges)
                        for (int i = 0; i < count; i++) keep[start + i] = false;
                    var solid = new List<int>();
                    for (int i = 0; i < all.Length; i++) if (keep[i]) solid.Add(all[i]);
                    collider = null;
                    if (solid.Count > 0)
                    {
                        collider = new Mesh { name = name + " collider", indexFormat = IndexFormat.UInt32 };
                        collider.SetVertices(new List<Vector3>(mesh.vertices));
                        collider.SetTriangles(solid, 0);
                    }
                }
                if (collider != null) go.AddComponent<MeshCollider>().sharedMesh = collider;
            }
        }

        // Merged chunks are most of the world's mesh memory (tens of millions of vertices once South Beach
        // is drivable). Packing normals into bytes and colours into halves takes a vertex from 40 bytes to
        // 24, in the CPU copy and on the GPU, and the data converts in place. Positions stay full floats.
        static readonly VertexAttributeDescriptor[] CompactLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4),
        };

        static void Compact(Mesh mesh)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.Normal) || !mesh.HasVertexAttribute(VertexAttribute.Color) || mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) return;
            mesh.SetVertexBufferParams(mesh.vertexCount, CompactLayout);
        }

        static Bounds RangeBounds(Mesh mesh, Part part)
        {
            var verts = new List<Vector3>();
            mesh.GetVertices(verts);
            var b = new Bounds(verts[part.VertexStart], Vector3.zero);
            for (int i = 1; i < part.VertexCount; i++) b.Encapsulate(verts[part.VertexStart + i]);
            return b;
        }

        // Solid colliders (never triggers, no Rigidbody): a capsule for poles and trunks, the Polyfork
        // convex hull for other breakables, a box for generated breakables.
        Collider Collide(Transform parent, StaticInstance instance, Part part)
        {
            if (part.CapsuleRadius <= 0f && instance.Class == PhysicsClass.Static) return null;
            var m = instance.Transform;
            var go = new GameObject(instance.Variant + " #" + instance.Id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
            if (part.CapsuleRadius > 0f)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = part.CapsuleRadius;
                capsule.height = Mathf.Max(part.CapsuleHeight, part.CapsuleRadius * 2f);
                capsule.center = new Vector3(0f, capsule.height * 0.5f - m.GetPosition().y + instance.WorldBounds.min.y, 0f);
                return capsule;
            }
            if (part.Mesh == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = part.BoxSize;
                box.center = new Vector3(0f, part.BoxSize.y * 0.5f, 0f);
                return box;
            }
            go.transform.localScale = m.lossyScale;
            var hull = HullMesh(instance);
            if (hull == null)
            {
                var box = go.AddComponent<BoxCollider>();
                var b = part.Mesh.bounds;
                box.center = b.center;
                box.size = b.size;
                return box;
            }
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = hull;
            return mc;
        }

        // The asset's whole-object convex hull, from the union of its Polyfork hull parts.
        Mesh HullMesh(StaticInstance instance)
        {
            if (string.IsNullOrEmpty(instance.HullResource)) return null;
            if (hullMeshes.TryGetValue(instance.HullResource, out var cached)) return cached;
            var hulls = Index.HullsOf(instance);
            Mesh mesh = null;
            if (hulls?.parts != null && hulls.parts.Length > 0)
            {
                var pts = new List<Vector3>();
                foreach (var part in hulls.parts)
                    for (int i = 0; i + 2 < part.pts.Length; i += 3) pts.Add(new Vector3(part.pts[i], part.pts[i + 1], part.pts[i + 2]));
                var tris = new List<int>();
                for (int i = 1; i + 1 < pts.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                mesh = new Mesh { name = instance.Asset + " hull", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(pts);
                mesh.SetTriangles(tris, 0);
            }
            hullMeshes[instance.HullResource] = mesh;
            return mesh;
        }
    }
}
