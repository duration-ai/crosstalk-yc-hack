using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Glb
{
    public sealed class GlbNode
    {
        public string Name;
        public Vector3 Translation = Vector3.zero;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Scale = Vector3.one;
        public int Mesh = -1;
        public int Skin = -1;
        public int[] Children = Array.Empty<int>();
        public int Parent = -1;

        public Matrix4x4 LocalMatrix => Matrix4x4.TRS(Translation, Rotation, Scale);
    }

    public sealed class GlbSkin
    {
        public int[] Joints;
        public Matrix4x4[] InverseBindMatrices;
    }

    public sealed class GlbChannel
    {
        public int Node;
        public bool IsRotation;
        public float[] Times;
        public Vector4[] Values;
    }

    public sealed class GlbClip
    {
        public string Name;
        public float Length;
        public GlbChannel[] Channels;
    }

    // Reads binary glTF 2.0 files straight from bytes. Unity's importer drops COLOR_0, which is
    // where Polyfork keeps all of its colour, so every mesh is rebuilt here with that attribute
    // baked into Mesh.colors. glTF is right-handed and Unity left-handed: X is mirrored and
    // triangle winding is flipped on load, which keeps the model's front facing +Z.
    public sealed class GlbAsset
    {
        public string Id;
        public GlbNode[] Nodes;
        public int[] Roots;
        public Mesh[] Meshes;
        public bool[] MeshIsSkinned;
        public GlbSkin[] Skins;
        public GlbClip[] Clips;

        static readonly Matrix4x4 Mirror = Matrix4x4.Scale(new Vector3(-1, 1, 1));

        public static GlbAsset Load(string id, byte[] bytes)
        {
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
                throw new FormatException(id + " is not a GLB file");

            int offset = 12;
            Dictionary<string, object> json = null;
            int binOffset = -1;
            while (offset < bytes.Length)
            {
                int chunkLength = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                if (chunkType == 0x4E4F534A)
                    json = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(bytes, offset + 8, chunkLength));
                else if (chunkType == 0x004E4942)
                    binOffset = offset + 8;
                offset += 8 + chunkLength;
            }
            if (json == null || binOffset < 0) throw new FormatException(id + " has no JSON or BIN chunk");

            var asset = new GlbAsset { Id = id };
            var reader = new AccessorReader(json, bytes, binOffset);
            asset.ReadNodes(json);
            asset.ReadSkins(json, reader);
            asset.ReadMeshes(json, reader);
            asset.ReadClips(json, reader);
            return asset;
        }

        void ReadNodes(Dictionary<string, object> json)
        {
            var nodes = List(json, "nodes");
            Nodes = new GlbNode[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = (Dictionary<string, object>)nodes[i];
                var node = new GlbNode
                {
                    Name = n.TryGetValue("name", out var name) ? (string)name : "node" + i,
                    Mesh = Int(n, "mesh", -1),
                    Skin = Int(n, "skin", -1),
                };
                if (n.TryGetValue("matrix", out var m))
                {
                    var f = Floats((List<object>)m);
                    var mat = new Matrix4x4();
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++)
                            mat[r, c] = f[c * 4 + r];
                    mat = Mirror * mat * Mirror;
                    node.Translation = mat.GetColumn(3);
                    node.Rotation = mat.rotation;
                    node.Scale = mat.lossyScale;
                }
                if (n.TryGetValue("translation", out var t)) node.Translation = ToVector3((List<object>)t);
                if (n.TryGetValue("rotation", out var q)) node.Rotation = ToQuaternion(Floats((List<object>)q));
                if (n.TryGetValue("scale", out var s))
                {
                    var f = Floats((List<object>)s);
                    node.Scale = new Vector3(f[0], f[1], f[2]);
                }
                if (n.TryGetValue("children", out var ch))
                {
                    var list = (List<object>)ch;
                    node.Children = new int[list.Count];
                    for (int c = 0; c < list.Count; c++) node.Children[c] = Convert.ToInt32(list[c]);
                }
                Nodes[i] = node;
            }
            foreach (var node in Nodes)
                foreach (int c in node.Children)
                    Nodes[c].Parent = Array.IndexOf(Nodes, node);

            var scenes = List(json, "scenes");
            int sceneIndex = Int(json, "scene", 0);
            if (scenes.Count > 0)
            {
                var sceneNodes = (List<object>)((Dictionary<string, object>)scenes[sceneIndex])["nodes"];
                Roots = new int[sceneNodes.Count];
                for (int i = 0; i < Roots.Length; i++) Roots[i] = Convert.ToInt32(sceneNodes[i]);
            }
            else
            {
                var roots = new List<int>();
                for (int i = 0; i < Nodes.Length; i++) if (Nodes[i].Parent < 0) roots.Add(i);
                Roots = roots.ToArray();
            }
        }

        void ReadSkins(Dictionary<string, object> json, AccessorReader reader)
        {
            var skins = List(json, "skins");
            Skins = new GlbSkin[skins.Count];
            for (int i = 0; i < skins.Count; i++)
            {
                var s = (Dictionary<string, object>)skins[i];
                var joints = (List<object>)s["joints"];
                var skin = new GlbSkin { Joints = new int[joints.Count], InverseBindMatrices = new Matrix4x4[joints.Count] };
                for (int j = 0; j < joints.Count; j++) skin.Joints[j] = Convert.ToInt32(joints[j]);
                if (s.TryGetValue("inverseBindMatrices", out var ibm))
                {
                    var f = reader.Read(Convert.ToInt32(ibm), out _);
                    for (int j = 0; j < joints.Count; j++)
                    {
                        var mat = new Matrix4x4();
                        for (int c = 0; c < 4; c++)
                            for (int r = 0; r < 4; r++)
                                mat[r, c] = f[j * 16 + c * 4 + r];
                        skin.InverseBindMatrices[j] = Mirror * mat * Mirror;
                    }
                }
                else
                {
                    for (int j = 0; j < joints.Count; j++) skin.InverseBindMatrices[j] = Matrix4x4.identity;
                }
                Skins[i] = skin;
            }
        }

        void ReadMeshes(Dictionary<string, object> json, AccessorReader reader)
        {
            var materials = List(json, "materials");
            var meshes = List(json, "meshes");
            Meshes = new Mesh[meshes.Count];
            MeshIsSkinned = new bool[meshes.Count];
            for (int m = 0; m < meshes.Count; m++)
            {
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var colors = new List<Color>();
                var weights = new List<BoneWeight>();
                var indices = new List<int>();
                bool skinned = false;

                foreach (Dictionary<string, object> prim in (List<object>)((Dictionary<string, object>)meshes[m])["primitives"])
                {
                    if (Int(prim, "mode", 4) != 4) continue;
                    var attrs = (Dictionary<string, object>)prim["attributes"];
                    int baseVertex = positions.Count;
                    var pos = reader.Read(Convert.ToInt32(attrs["POSITION"]), out int posWidth);
                    int count = pos.Length / posWidth;
                    for (int v = 0; v < count; v++) positions.Add(new Vector3(-pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]));

                    if (attrs.TryGetValue("NORMAL", out var na))
                    {
                        var nrm = reader.Read(Convert.ToInt32(na), out _);
                        for (int v = 0; v < count; v++) normals.Add(new Vector3(-nrm[v * 3], nrm[v * 3 + 1], nrm[v * 3 + 2]));
                    }
                    else
                    {
                        for (int v = 0; v < count; v++) normals.Add(Vector3.up);
                    }

                    var factor = MaterialColor(materials, Int(prim, "material", -1));
                    if (attrs.TryGetValue("COLOR_0", out var ca))
                    {
                        var col = reader.Read(Convert.ToInt32(ca), out int colWidth);
                        for (int v = 0; v < count; v++)
                        {
                            var c = new Color(col[v * colWidth], col[v * colWidth + 1], col[v * colWidth + 2], colWidth == 4 ? col[v * colWidth + 3] : 1f);
                            colors.Add(c * factor);
                        }
                    }
                    else
                    {
                        for (int v = 0; v < count; v++) colors.Add(factor);
                    }

                    if (attrs.TryGetValue("JOINTS_0", out var ja) && attrs.TryGetValue("WEIGHTS_0", out var wa))
                    {
                        skinned = true;
                        var j = reader.Read(Convert.ToInt32(ja), out _);
                        var w = reader.Read(Convert.ToInt32(wa), out _);
                        for (int v = 0; v < count; v++)
                        {
                            float sum = w[v * 4] + w[v * 4 + 1] + w[v * 4 + 2] + w[v * 4 + 3];
                            if (sum <= 0f) sum = 1f;
                            weights.Add(new BoneWeight
                            {
                                boneIndex0 = (int)j[v * 4], weight0 = w[v * 4] / sum,
                                boneIndex1 = (int)j[v * 4 + 1], weight1 = w[v * 4 + 1] / sum,
                                boneIndex2 = (int)j[v * 4 + 2], weight2 = w[v * 4 + 2] / sum,
                                boneIndex3 = (int)j[v * 4 + 3], weight3 = w[v * 4 + 3] / sum,
                            });
                        }
                    }
                    else if (skinned)
                    {
                        for (int v = 0; v < count; v++) weights.Add(new BoneWeight { weight0 = 1f });
                    }

                    if (prim.TryGetValue("indices", out var ia))
                    {
                        var idx = reader.Read(Convert.ToInt32(ia), out _);
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            indices.Add(baseVertex + (int)idx[t]);
                            indices.Add(baseVertex + (int)idx[t + 2]);
                            indices.Add(baseVertex + (int)idx[t + 1]);
                        }
                    }
                    else
                    {
                        for (int t = 0; t + 2 < count; t += 3)
                        {
                            indices.Add(baseVertex + t);
                            indices.Add(baseVertex + t + 2);
                            indices.Add(baseVertex + t + 1);
                        }
                    }
                }

                var mesh = new Mesh { name = Id + "/mesh" + m };
                if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0);
                if (skinned && weights.Count == positions.Count) mesh.boneWeights = weights.ToArray();
                mesh.RecalculateBounds();
                Meshes[m] = mesh;
                MeshIsSkinned[m] = skinned;
            }
        }

        void ReadClips(Dictionary<string, object> json, AccessorReader reader)
        {
            var animations = List(json, "animations");
            Clips = new GlbClip[animations.Count];
            for (int a = 0; a < animations.Count; a++)
            {
                var anim = (Dictionary<string, object>)animations[a];
                var samplers = (List<object>)anim["samplers"];
                var channels = new List<GlbChannel>();
                float length = 0f;
                foreach (Dictionary<string, object> ch in (List<object>)anim["channels"])
                {
                    var target = (Dictionary<string, object>)ch["target"];
                    if (!target.ContainsKey("node")) continue;
                    string path = (string)target["path"];
                    if (path != "rotation" && path != "translation") continue;
                    var sampler = (Dictionary<string, object>)samplers[Convert.ToInt32(ch["sampler"])];
                    var times = reader.Read(Convert.ToInt32(sampler["input"]), out _);
                    var values = reader.Read(Convert.ToInt32(sampler["output"]), out int width);
                    bool rotation = path == "rotation";
                    var converted = new Vector4[times.Length];
                    for (int k = 0; k < times.Length; k++)
                    {
                        if (rotation)
                            converted[k] = new Vector4(values[k * width], -values[k * width + 1], -values[k * width + 2], values[k * width + 3]);
                        else
                            converted[k] = new Vector4(-values[k * width], values[k * width + 1], values[k * width + 2], 0f);
                    }
                    if (times.Length > 0) length = Mathf.Max(length, times[times.Length - 1]);
                    channels.Add(new GlbChannel { Node = Convert.ToInt32(target["node"]), IsRotation = rotation, Times = times, Values = converted });
                }
                Clips[a] = new GlbClip
                {
                    Name = anim.TryGetValue("name", out var n) ? (string)n : "clip" + a,
                    Length = Mathf.Max(length, 0.01f),
                    Channels = channels.ToArray(),
                };
            }
        }

        // Builds the node hierarchy as GameObjects. Skinned meshes get a SkinnedMeshRenderer bound
        // to the joint transforms so GlbAnimator can play the file's clips on them.
        public GameObject Instantiate(Material material, Transform parent = null)
        {
            var root = new GameObject(Id);
            if (parent != null) root.transform.SetParent(parent, false);
            var transforms = new Transform[Nodes.Length];
            foreach (int r in Roots) CreateNode(r, root.transform, transforms);

            for (int i = 0; i < Nodes.Length; i++)
            {
                var node = Nodes[i];
                if (node.Mesh < 0 || transforms[i] == null) continue;
                var go = transforms[i].gameObject;
                if (node.Skin >= 0 && MeshIsSkinned[node.Mesh])
                {
                    var skin = Skins[node.Skin];
                    var mesh = Meshes[node.Mesh];
                    if (mesh.bindposes.Length != skin.Joints.Length) mesh.bindposes = skin.InverseBindMatrices;
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    var bones = new Transform[skin.Joints.Length];
                    for (int j = 0; j < bones.Length; j++) bones[j] = transforms[skin.Joints[j]];
                    smr.bones = bones;
                    smr.rootBone = bones.Length > 0 ? bones[0] : go.transform;
                    smr.sharedMesh = mesh;
                    smr.sharedMaterial = material;
                    smr.updateWhenOffscreen = true;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = Meshes[node.Mesh];
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
            }

            if (Clips.Length > 0)
            {
                var animator = root.AddComponent<GlbAnimator>();
                animator.Bind(this, transforms);
            }
            return root;
        }

        void CreateNode(int index, Transform parent, Transform[] transforms)
        {
            var node = Nodes[index];
            var go = new GameObject(node.Name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = node.Translation;
            t.localRotation = node.Rotation;
            t.localScale = node.Scale;
            transforms[index] = t;
            foreach (int c in node.Children) CreateNode(c, t, transforms);
        }

        Mesh baked;

        // Flattens every non-skinned mesh into one mesh in model space, used for static dressing
        // that is merged into chunk meshes.
        public Mesh BakeStatic()
        {
            if (baked != null) return baked;
            var combine = new List<CombineInstance>();
            foreach (int r in Roots) CollectStatic(r, Matrix4x4.identity, combine);
            baked = new Mesh { name = Id + "/baked", indexFormat = IndexFormat.UInt32 };
            baked.CombineMeshes(combine.ToArray(), true, true);
            baked.RecalculateBounds();
            return baked;
        }

        void CollectStatic(int index, Matrix4x4 parentMatrix, List<CombineInstance> combine)
        {
            var node = Nodes[index];
            var matrix = parentMatrix * node.LocalMatrix;
            if (node.Mesh >= 0 && !MeshIsSkinned[node.Mesh])
                combine.Add(new CombineInstance { mesh = Meshes[node.Mesh], transform = matrix });
            foreach (int c in node.Children) CollectStatic(c, matrix, combine);
        }

        public Bounds ComputeBounds()
        {
            var mesh = BakeStatic();
            if (mesh.vertexCount > 0) return mesh.bounds;
            var b = new Bounds();
            bool first = true;
            foreach (var m in Meshes)
            {
                if (first) { b = m.bounds; first = false; }
                else b.Encapsulate(m.bounds);
            }
            return b;
        }

        static Color MaterialColor(List<object> materials, int index)
        {
            if (index < 0 || index >= materials.Count) return Color.white;
            var mat = (Dictionary<string, object>)materials[index];
            if (!mat.TryGetValue("pbrMetallicRoughness", out var pbr)) return Color.white;
            if (!((Dictionary<string, object>)pbr).TryGetValue("baseColorFactor", out var f)) return Color.white;
            var c = Floats((List<object>)f);
            return new Color(c[0], c[1], c[2], c[3]);
        }

        static Quaternion ToQuaternion(float[] q) => new Quaternion(q[0], -q[1], -q[2], q[3]);

        static Vector3 ToVector3(List<object> list)
        {
            var f = Floats(list);
            return new Vector3(-f[0], f[1], f[2]);
        }

        static float[] Floats(List<object> list)
        {
            var f = new float[list.Count];
            for (int i = 0; i < f.Length; i++) f[i] = Convert.ToSingle(list[i]);
            return f;
        }

        static List<object> List(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var v) ? (List<object>)v : new List<object>();

        static int Int(Dictionary<string, object> json, string key, int fallback) =>
            json.TryGetValue(key, out var v) ? Convert.ToInt32(v) : fallback;

        sealed class AccessorReader
        {
            readonly List<object> accessors;
            readonly List<object> views;
            readonly byte[] bytes;
            readonly int binOffset;

            public AccessorReader(Dictionary<string, object> json, byte[] bytes, int binOffset)
            {
                accessors = List(json, "accessors");
                views = List(json, "bufferViews");
                this.bytes = bytes;
                this.binOffset = binOffset;
            }

            public float[] Read(int index, out int width)
            {
                var acc = (Dictionary<string, object>)accessors[index];
                int count = Convert.ToInt32(acc["count"]);
                int componentType = Convert.ToInt32(acc["componentType"]);
                bool normalized = acc.TryGetValue("normalized", out var n) && (bool)n;
                width = ((string)acc["type"]) switch
                {
                    "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1,
                };
                var result = new float[count * width];
                if (!acc.TryGetValue("bufferView", out var bv)) return result;

                var view = (Dictionary<string, object>)views[Convert.ToInt32(bv)];
                int componentSize = componentType switch { 5120 => 1, 5121 => 1, 5122 => 2, 5123 => 2, _ => 4 };
                int start = binOffset + Int(view, "byteOffset", 0) + Int(acc, "byteOffset", 0);
                int stride = Int(view, "byteStride", 0);
                if (stride == 0) stride = componentSize * width;

                for (int e = 0; e < count; e++)
                {
                    int p = start + e * stride;
                    for (int c = 0; c < width; c++)
                    {
                        int q = p + c * componentSize;
                        float v = componentType switch
                        {
                            5126 => BitConverter.ToSingle(bytes, q),
                            5125 => BitConverter.ToUInt32(bytes, q),
                            5123 => normalized ? BitConverter.ToUInt16(bytes, q) / 65535f : BitConverter.ToUInt16(bytes, q),
                            5122 => normalized ? Mathf.Max(BitConverter.ToInt16(bytes, q) / 32767f, -1f) : BitConverter.ToInt16(bytes, q),
                            5121 => normalized ? bytes[q] / 255f : bytes[q],
                            5120 => normalized ? Mathf.Max((sbyte)bytes[q] / 127f, -1f) : (sbyte)bytes[q],
                            _ => 0f,
                        };
                        result[e * width + c] = v;
                    }
                }
                return result;
            }
        }
    }
}
