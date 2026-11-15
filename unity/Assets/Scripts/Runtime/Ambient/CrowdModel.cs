using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Ambient
{
    public enum Clip { Idle, Walk, Run, Sit, Push }

    public enum Slot { Keep, Top, Under, Bottom, Hair, Skin }

    // A Polyfork character baked for GPU crowds. Polyfork binds rigidly (one bone per vertex), so
    // each vertex just names its bone, and a small texture holds every bone's skin matrix for every
    // frame of Idle, Walk, Run, plus two poses built from them: sitting and pushing a cart. The
    // crowd shader skins on the GPU, so every instance of a model shares one instanced draw call.
    // Outfit zones (Polyfork's colour knobs) are tagged per vertex so each instance wears its own
    // palette colours without a separate mesh.
    public sealed class CrowdModel
    {
        public const float FrameRate = 30f;

        public string Id;
        public Mesh Mesh;
        public Texture2D Bones;
        public Vector3 HeadPivot;
        public float WalkSpeed = 1.3f;
        public float RunSpeed = 3.4f;
        public readonly int[] ClipStart = new int[5];
        public readonly int[] ClipFrames = new int[5];

        struct Bone
        {
            public int Node;
            public Matrix4x4 InverseBind;
        }

        public int Frame(Clip clip, float time)
        {
            int frames = ClipFrames[(int)clip];
            int f = Mathf.FloorToInt(time * FrameRate) % frames;
            if (f < 0) f += frames;
            return ClipStart[(int)clip] + f;
        }

        public static CrowdModel FromCharacter(GlbAsset shape, GlbAsset clipSource, Dictionary<Color32, Slot> zones)
        {
            var model = new CrowdModel { Id = shape.Id };
            var rest = new Matrix4x4[shape.Nodes.Length];
            var restTrs = Locals(shape);
            Globals(shape, restTrs, rest);

            var bones = new List<Bone>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var data = new List<Vector4>();
            var indices = new List<int>();
            for (int i = 0; i < shape.Nodes.Length; i++)
            {
                var node = shape.Nodes[i];
                if (node.Mesh < 0) continue;
                var mesh = shape.Meshes[node.Mesh];
                var verts = mesh.vertices;
                var norms = mesh.normals;
                var cols = mesh.colors;
                var weights = mesh.boneWeights;
                bool skinned = shape.MeshIsSkinned[node.Mesh] && node.Skin >= 0 && weights.Length == verts.Length;
                int baseVertex = positions.Count;
                int rigidBone = skinned ? -1 : BoneFor(bones, i, rest[i].inverse);
                for (int v = 0; v < verts.Length; v++)
                {
                    int bone = rigidBone;
                    var p = verts[v];
                    var n = v < norms.Length ? norms[v] : Vector3.up;
                    if (skinned)
                    {
                        var skin = shape.Skins[node.Skin];
                        int j = Heaviest(weights[v]);
                        bone = BoneFor(bones, skin.Joints[j], skin.InverseBindMatrices[j]);
                    }
                    else
                    {
                        p = rest[i].MultiplyPoint3x4(p);
                        n = rest[i].MultiplyVector(n);
                    }
                    var color = v < cols.Length ? cols[v] : Color.white;
                    var (slot, shade) = Match(color, zones);
                    string boneName = shape.Nodes[bones[bone].Node].Name;
                    bool head = boneName.EndsWith("Head") || boneName.Contains("Face_");
                    positions.Add(p);
                    normals.Add(n);
                    colors.Add(color);
                    data.Add(new Vector4(bone, (float)slot, head ? 1f : 0f, shade));
                }
                foreach (int t in mesh.triangles) indices.Add(baseVertex + t);
            }

            model.Mesh = new Mesh { name = shape.Id + "/crowd" };
            if (positions.Count > 65535) model.Mesh.indexFormat = IndexFormat.UInt32;
            model.Mesh.SetVertices(positions);
            model.Mesh.SetNormals(normals);
            model.Mesh.SetColors(colors);
            model.Mesh.SetUVs(1, data);
            model.Mesh.SetTriangles(indices, 0);
            model.Mesh.bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(3f, 2.6f, 3f));
            model.Mesh.UploadMeshData(true);

            int headNode = Find(shape, "Head");
            if (headNode >= 0) model.HeadPivot = rest[headNode].GetColumn(3);
            model.BakeClips(shape, clipSource, restTrs, bones);
            return model;
        }

        // A static prop (skateboard, cart) drawn through the same instanced path with one fixed bone.
        public static CrowdModel FromStatic(string id, Mesh source, Dictionary<Color32, Color> recolor)
        {
            var model = new CrowdModel { Id = id };
            var verts = source.vertices;
            var cols = source.colors;
            var colors = new Color[verts.Length];
            var data = new Vector4[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                var c = v < cols.Length ? cols[v] : Color.white;
                colors[v] = Recolor(c, recolor);
                data[v] = new Vector4(0f, 0f, 0f, 1f);
            }
            model.Mesh = new Mesh { name = id + "/crowd", indexFormat = source.indexFormat };
            model.Mesh.vertices = verts;
            model.Mesh.normals = source.normals;
            model.Mesh.colors = colors;
            model.Mesh.SetUVs(1, data);
            model.Mesh.triangles = source.triangles;
            model.Mesh.RecalculateBounds();
            model.Mesh.UploadMeshData(true);

            var pixels = new ushort[3 * 4];
            Write(pixels, 0, Matrix4x4.identity);
            model.Bones = MakeTexture(id, 3, 1, pixels);
            for (int c = 0; c < 5; c++) model.ClipFrames[c] = 1;
            return model;
        }

        static Color Recolor(Color linear, Dictionary<Color32, Color> recolor)
        {
            if (recolor == null) return linear;
            Color32 g = linear.gamma;
            foreach (var pair in recolor)
            {
                if (Distance(g, pair.Key) > 40f) continue;
                float shade = Luma(linear.gamma) / Mathf.Max(Luma(pair.Key), 0.02f);
                return pair.Value * Mathf.Clamp(shade, 0.6f, 1.3f);
            }
            return linear;
        }

        static (Slot, float) Match(Color linear, Dictionary<Color32, Slot> zones)
        {
            if (zones == null || zones.Count == 0) return (Slot.Keep, 1f);
            Color32 g = linear.gamma;
            float best = 40f;
            var slot = Slot.Keep;
            Color32 zone = default;
            foreach (var pair in zones)
            {
                float d = Distance(g, pair.Key);
                if (d >= best) continue;
                best = d;
                slot = pair.Value;
                zone = pair.Key;
            }
            if (slot == Slot.Keep) return (Slot.Keep, 1f);
            float shade = Luma(linear.gamma) / Mathf.Max(Luma(zone), 0.02f);
            return (slot, Mathf.Clamp(shade, 0.55f, 1.35f));
        }

        static float Distance(Color32 a, Color32 b)
        {
            float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b;
            return Mathf.Sqrt(r * r + g * g + bl * bl);
        }

        static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        static float Luma(Color32 c) => Luma((Color)c);

        static int Heaviest(BoneWeight w)
        {
            int j = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; j = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; j = w.boneIndex2; }
            if (w.weight3 > best) j = w.boneIndex3;
            return j;
        }

        static int BoneFor(List<Bone> bones, int node, Matrix4x4 inverseBind)
        {
            for (int b = 0; b < bones.Count; b++)
            {
                if (bones[b].Node != node) continue;
                bool same = true;
                for (int k = 0; k < 16 && same; k++) same = Mathf.Abs(bones[b].InverseBind[k] - inverseBind[k]) < 1e-3f;
                if (same) return b;
            }
            bones.Add(new Bone { Node = node, InverseBind = inverseBind });
            return bones.Count - 1;
        }

        static int Find(GlbAsset asset, string name)
        {
            for (int i = 0; i < asset.Nodes.Length; i++)
                if (asset.Nodes[i].Name == name || asset.Nodes[i].Name.EndsWith(":" + name)) return i;
            return -1;
        }

        static (Vector3 t, Quaternion r, Vector3 s)[] Locals(GlbAsset asset)
        {
            var result = new (Vector3, Quaternion, Vector3)[asset.Nodes.Length];
            for (int i = 0; i < result.Length; i++) result[i] = (asset.Nodes[i].Translation, asset.Nodes[i].Rotation, asset.Nodes[i].Scale);
            return result;
        }

        static void Globals(GlbAsset asset, (Vector3 t, Quaternion r, Vector3 s)[] locals, Matrix4x4[] globals)
        {
            foreach (int root in asset.Roots) Visit(asset, root, Matrix4x4.identity, locals, globals);
        }

        static void Visit(GlbAsset asset, int index, Matrix4x4 parent, (Vector3 t, Quaternion r, Vector3 s)[] locals, Matrix4x4[] globals)
        {
            var l = locals[index];
            globals[index] = parent * Matrix4x4.TRS(l.t, l.r, l.s);
            foreach (int c in asset.Nodes[index].Children) Visit(asset, c, globals[index], locals, globals);
        }

        // Clips come from the base character; remixed figures share its Mixamo skeleton, so rotations
        // retarget directly and only the hip height is rescaled to the new proportions.
        void BakeClips(GlbAsset shape, GlbAsset source, (Vector3 t, Quaternion r, Vector3 s)[] restTrs, List<Bone> bones)
        {
            var map = new int[source.Nodes.Length];
            for (int i = 0; i < map.Length; i++) map[i] = Find(shape, source.Nodes[i].Name);
            int hips = Find(shape, "Hips");
            int sourceHips = Find(source, "Hips");
            float hipScale = hips >= 0 && sourceHips >= 0 && Mathf.Abs(source.Nodes[sourceHips].Translation.y) > 0.01f
                ? shape.Nodes[hips].Translation.y / source.Nodes[sourceHips].Translation.y
                : 1f;

            GlbClip idle = null, walk = null, run = null;
            foreach (var c in source.Clips)
            {
                if (c.Name == "Idle") idle = c;
                else if (c.Name == "Walk") walk = c;
                else if (c.Name == "Run") run = c;
            }
            idle ??= walk ?? run;
            walk ??= idle;
            run ??= walk;

            var poses = new List<(Vector3 t, Quaternion r, Vector3 s)[]>();
            void AddClip(Clip id, GlbClip clip, System.Action<(Vector3 t, Quaternion r, Vector3 s)[]> adjust, int maxFrames = int.MaxValue)
            {
                ClipStart[(int)id] = poses.Count;
                int frames = clip == null ? 1 : Mathf.Clamp(Mathf.RoundToInt(clip.Length * FrameRate), 1, maxFrames);
                for (int f = 0; f < frames; f++)
                {
                    var pose = ((Vector3, Quaternion, Vector3)[])restTrs.Clone();
                    if (clip != null)
                    {
                        float t = f / FrameRate;
                        foreach (var ch in clip.Channels)
                        {
                            int n = map[ch.Node];
                            if (n < 0) continue;
                            var value = Sample(ch, t);
                            if (ch.IsRotation) pose[n].Item2 = new Quaternion(value.x, value.y, value.z, value.w);
                            else if (n == hips) pose[n].Item1 = new Vector3(value.x, value.y, value.z) * hipScale;
                        }
                    }
                    adjust?.Invoke(pose);
                    poses.Add(pose);
                }
                ClipFrames[(int)id] = frames;
            }

            int Node(string name) => Find(shape, name);
            void Rotate((Vector3 t, Quaternion r, Vector3 s)[] pose, string name, Quaternion q)
            {
                int n = Node(name);
                if (n >= 0) pose[n].r = q;
            }

            AddClip(Clip.Idle, idle, null);
            AddClip(Clip.Walk, walk, null);
            AddClip(Clip.Run, run, null);

            int upLeg = Node("LeftUpLeg");
            float legDrop = hips >= 0 && upLeg >= 0 ? -restTrs[upLeg].t.y : 0.08f;
            AddClip(Clip.Sit, idle, pose =>
            {
                Rotate(pose, "LeftUpLeg", Quaternion.Euler(-86f, 4f, 0f));
                Rotate(pose, "RightUpLeg", Quaternion.Euler(-86f, -4f, 0f));
                Rotate(pose, "LeftLeg", Quaternion.Euler(84f, 0f, 0f));
                Rotate(pose, "RightLeg", Quaternion.Euler(84f, 0f, 0f));
                Rotate(pose, "LeftFoot", Quaternion.identity);
                Rotate(pose, "RightFoot", Quaternion.identity);
                Rotate(pose, "Spine", Quaternion.Euler(-4f, 0f, 0f));
                if (hips >= 0) pose[hips].t = new Vector3(0f, SeatHeight + legDrop + 0.07f, -0.05f);
            }, 1);

            var push = Quaternion.AngleAxis(32f, Vector3.right);
            AddClip(Clip.Push, walk, pose =>
            {
                Rotate(pose, "LeftShoulder", Quaternion.identity);
                Rotate(pose, "RightShoulder", Quaternion.identity);
                Rotate(pose, "LeftArm", push * Quaternion.AngleAxis(LeftArmForward, Vector3.up));
                Rotate(pose, "RightArm", push * Quaternion.AngleAxis(-LeftArmForward, Vector3.up));
                Rotate(pose, "LeftForeArm", Quaternion.AngleAxis(-12f, Vector3.right));
                Rotate(pose, "RightForeArm", Quaternion.AngleAxis(-12f, Vector3.right));
                Rotate(pose, "Spine1", Quaternion.Euler(8f, 0f, 0f));
            });

            int width = bones.Count * 3;
            var pixels = new ushort[width * poses.Count * 4];
            var globals = new Matrix4x4[shape.Nodes.Length];
            int foot = Node("LeftFoot");
            var footZ = new List<float>();
            for (int f = 0; f < poses.Count; f++)
            {
                Globals(shape, poses[f], globals);
                for (int b = 0; b < bones.Count; b++)
                    Write(pixels, (f * width + b * 3) * 4, globals[bones[b].Node] * bones[b].InverseBind);
                if (foot >= 0) footZ.Add(globals[foot].m23);
            }
            Bones = MakeTexture(shape.Id, width, poses.Count, pixels);

            if (foot >= 0)
            {
                WalkSpeed = Stride(footZ, Clip.Walk, walk, 1.3f);
                RunSpeed = Stride(footZ, Clip.Run, run, 3.4f);
            }
        }

        // An in-place cycle sweeps each foot back across its stride once per cycle while planted,
        // so ground speed at rate 1 is twice the sweep over the cycle length.
        float Stride(List<float> footZ, Clip clip, GlbClip source, float fallback)
        {
            if (source == null) return fallback;
            float min = float.MaxValue, max = float.MinValue;
            for (int f = 0; f < ClipFrames[(int)clip]; f++)
            {
                float z = footZ[ClipStart[(int)clip] + f];
                min = Mathf.Min(min, z);
                max = Mathf.Max(max, z);
            }
            float speed = 2f * (max - min) / Mathf.Max(source.Length, 0.1f);
            return speed > 0.3f ? speed : fallback;
        }

        public const float SeatHeight = 0.45f;
        const float LeftArmForward = 90f;

        static void Write(ushort[] pixels, int offset, Matrix4x4 m)
        {
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                    pixels[offset + row * 4 + col] = Mathf.FloatToHalf(m[row, col]);
        }

        static Texture2D MakeTexture(string id, int width, int height, ushort[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = id + "/bones",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, true);
            return texture;
        }

        static Vector4 Sample(GlbChannel channel, float t)
        {
            var times = channel.Times;
            if (times.Length == 1 || t <= times[0]) return channel.Values[0];
            int last = times.Length - 1;
            if (t >= times[last]) return channel.Values[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= t) lo = mid; else hi = mid;
            }
            float u = (t - times[lo]) / Mathf.Max(times[hi] - times[lo], 1e-5f);
            var a = channel.Values[lo];
            var b = channel.Values[hi];
            if (!channel.IsRotation) return Vector4.Lerp(a, b, u);
            var q = Quaternion.Slerp(new Quaternion(a.x, a.y, a.z, a.w), new Quaternion(b.x, b.y, b.z, b.w), u);
            return new Vector4(q.x, q.y, q.z, q.w);
        }
    }
}
