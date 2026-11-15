using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Ambient
{
    public enum Clip { Idle, Walk, Run, Sit, Push, Brace, Leap, Tuck, Flail, Sprawl, Kneel }

    public enum Slot { Keep, Top, Under, Bottom, Hair, Skin }

    // A Polyfork character baked for GPU crowds. Polyfork binds rigidly (one bone per vertex), so
    // each vertex just names its bone, and a small texture holds every bone's skin matrix for every
    // frame of Idle, Walk, Run, plus poses built from them: sitting, pushing a cart, and the reaction
    // poses (brace, leap, tuck, a flailing loop, sprawled and kneeling) that knock-downs and dives
    // blend between. The crowd shader skins on the GPU, so every instance of a model shares one
    // instanced draw call.
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
        public const int ClipCount = 11;
        public readonly int[] ClipStart = new int[ClipCount];
        public readonly int[] ClipFrames = new int[ClipCount];

        // Hip height in model space; reacting bodies rotate about the point this high above the origin.
        public float HipHeight = 0.9f;
        // Hip height above the ground when standing in the Idle pose, by the contact points below.
        public float StandHeight = 0.9f;
        // Per clip (first frame): head, hands, elbows, knees, feet, toes, hips and chest relative to
        // the hip pivot, with w the body's thickness around that point. Enough to keep any rotated
        // pose out of the ground.
        readonly Vector4[][] contacts = new Vector4[ClipCount][];

        static readonly (string name, float pad)[] ContactJoints =
        {
            ("Head", 0.17f), ("LeftHand", 0.06f), ("RightHand", 0.06f), ("LeftForeArm", 0.06f), ("RightForeArm", 0.06f),
            ("LeftLeg", 0.07f), ("RightLeg", 0.07f), ("LeftFoot", 0.07f), ("RightFoot", 0.07f), ("LeftToeBase", 0.04f),
            ("RightToeBase", 0.04f), ("Hips", 0.13f), ("Spine2", 0.14f),
        };

        // How high the hip pivot must sit above the ground so no part of the posed body is below it.
        public float Clearance(Quaternion orientation, Clip clip)
        {
            var points = contacts[(int)clip];
            if (points == null) return StandHeight;
            float h = 0f;
            foreach (var p in points)
            {
                var r = orientation * (Vector3)p;
                h = Mathf.Max(h, p.w - r.y);
            }
            return h;
        }

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
            for (int c = 0; c < ClipCount; c++) model.ClipFrames[c] = 1;
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

            // Reaction poses. Polyfork's rig is T-posed with every local frame aligned to the body
            // (+Z forward, the left arm along -X), so rotations are written for the left side and
            // mirrored onto the right.
            var idlePose = poses[ClipStart[(int)Clip.Idle]];
            void AddPose(Clip id, int frames, System.Action<(Vector3 t, Quaternion r, Vector3 s)[], float> adjust)
            {
                ClipStart[(int)id] = poses.Count;
                for (int f = 0; f < frames; f++)
                {
                    var pose = ((Vector3, Quaternion, Vector3)[])idlePose.Clone();
                    adjust(pose, f * Mathf.PI * 2f / frames);
                    poses.Add(pose);
                }
                ClipFrames[(int)id] = frames;
            }
            void Pair((Vector3 t, Quaternion r, Vector3 s)[] pose, string bone, Quaternion left, Quaternion? right = null)
            {
                Rotate(pose, "Left" + bone, left);
                Rotate(pose, "Right" + bone, right ?? Mirror(left));
            }
            void Arms((Vector3 t, Quaternion r, Vector3 s)[] pose, Quaternion arm, Quaternion fore, Quaternion? rightArm = null, Quaternion? rightFore = null)
            {
                Pair(pose, "Shoulder", Quaternion.identity);
                Pair(pose, "Arm", arm, rightArm);
                Pair(pose, "ForeArm", fore, rightFore);
            }
            void Legs((Vector3 t, Quaternion r, Vector3 s)[] pose, Quaternion up, Quaternion knee, Quaternion foot)
            {
                Pair(pose, "UpLeg", up);
                Pair(pose, "Leg", knee);
                Pair(pose, "Foot", foot);
            }
            Quaternion X(float a) => Quaternion.AngleAxis(a, Vector3.right);
            Quaternion Y(float a) => Quaternion.AngleAxis(a, Vector3.up);
            Quaternion Z(float a) => Quaternion.AngleAxis(a, Vector3.forward);

            // Startled crouch before a dive: knees bent, hunched, forearms up in front of the face.
            AddPose(Clip.Brace, 1, (pose, _) =>
            {
                Legs(pose, Quaternion.Euler(-50f, 0f, -8f), X(75f), X(-25f));
                Rotate(pose, "Spine", X(18f));
                Rotate(pose, "Spine1", X(10f));
                Rotate(pose, "Neck", X(-12f));
                Arms(pose, X(-40f) * Z(65f), Y(110f));
            });
            // The dive itself: stretched out, arms reaching overhead, legs trailing.
            AddPose(Clip.Leap, 1, (pose, _) =>
            {
                Legs(pose, Quaternion.Euler(18f, 0f, -5f), X(35f), X(30f));
                Rotate(pose, "Spine", X(-12f));
                Rotate(pose, "Spine1", X(-8f));
                Rotate(pose, "Neck", X(-25f));
                Arms(pose, X(20f) * Z(-78f), Y(10f));
            });
            // Curled into a ball for rolls: knees to the chest, arms round the shins, chin down.
            AddPose(Clip.Tuck, 1, (pose, _) =>
            {
                Legs(pose, Quaternion.Euler(-125f, 0f, -10f), X(145f), X(-20f));
                Rotate(pose, "Spine", X(25f));
                Rotate(pose, "Spine1", X(25f));
                Rotate(pose, "Spine2", X(15f));
                Rotate(pose, "Neck", X(25f));
                Arms(pose, X(50f) * Y(90f), Y(80f));
            });
            // Airborne: arms windmilling and legs kicking, left and right half a cycle apart.
            AddPose(Clip.Flail, 18, (pose, phase) =>
            {
                float o = phase + Mathf.PI;
                Arms(pose, X(25f * Mathf.Sin(phase)) * Z(-35f + 40f * Mathf.Sin(phase)), Y(25f + 25f * Mathf.Sin(phase + 1f)),
                    Mirror(X(25f * Mathf.Sin(o)) * Z(-35f + 40f * Mathf.Sin(o))), Mirror(Y(25f + 25f * Mathf.Sin(o + 1f))));
                Rotate(pose, "LeftUpLeg", Quaternion.Euler(-35f + 35f * Mathf.Sin(phase), 0f, -12f));
                Rotate(pose, "RightUpLeg", Mirror(Quaternion.Euler(-35f + 35f * Mathf.Sin(o), 0f, -12f)));
                Rotate(pose, "LeftLeg", X(45f + 30f * Mathf.Cos(phase)));
                Rotate(pose, "RightLeg", X(45f + 30f * Mathf.Cos(o)));
                Rotate(pose, "Spine", X(-12f));
                Rotate(pose, "Neck", X(-15f));
            });
            // Lying where they landed: limbs splayed, one knee up, head turned.
            AddPose(Clip.Sprawl, 1, (pose, _) =>
            {
                Arms(pose, Z(-25f), Y(35f), Mirror(Z(35f)), Mirror(Y(15f)));
                Rotate(pose, "LeftUpLeg", Quaternion.Euler(-40f, 0f, -18f));
                Rotate(pose, "LeftLeg", X(70f));
                Rotate(pose, "LeftFoot", X(-30f));
                Rotate(pose, "RightUpLeg", Mirror(Quaternion.Euler(5f, 0f, -10f)));
                Rotate(pose, "RightLeg", X(10f));
                Rotate(pose, "Spine", X(-5f));
                Rotate(pose, "Head", Y(25f));
            });
            // Halfway up: down on the left knee, right foot planted, hand on the knee.
            AddPose(Clip.Kneel, 1, (pose, _) =>
            {
                Rotate(pose, "LeftUpLeg", Quaternion.Euler(0f, 0f, -5f));
                Rotate(pose, "LeftLeg", X(90f));
                Rotate(pose, "LeftFoot", X(40f));
                Rotate(pose, "RightUpLeg", Mirror(Quaternion.Euler(-85f, 0f, -8f)));
                Rotate(pose, "RightLeg", X(85f));
                Rotate(pose, "RightFoot", Quaternion.identity);
                Rotate(pose, "Spine", X(25f));
                Rotate(pose, "Spine1", X(10f));
                Rotate(pose, "Neck", X(-15f));
                Arms(pose, X(-10f) * Z(70f), Y(30f), Mirror(X(55f) * Y(90f)), Mirror(Y(20f)));
            });

            int width = bones.Count * 3;
            var pixels = new ushort[width * poses.Count * 4];
            var globals = new Matrix4x4[shape.Nodes.Length];
            int foot = Node("LeftFoot");
            var footZ = new List<float>();
            var joints = new int[ContactJoints.Length];
            for (int j = 0; j < joints.Length; j++) joints[j] = Node(ContactJoints[j].name);
            for (int f = 0; f < poses.Count; f++)
            {
                Globals(shape, poses[f], globals);
                for (int b = 0; b < bones.Count; b++)
                    Write(pixels, (f * width + b * 3) * 4, globals[bones[b].Node] * bones[b].InverseBind);
                if (foot >= 0) footZ.Add(globals[foot].m23);
                if (f == 0 && hips >= 0) HipHeight = globals[hips].m13;
                int clip = System.Array.IndexOf(ClipStart, f);
                if (clip < 0 || clip == (int)Clip.Sit || clip == (int)Clip.Push) continue;
                var points = new List<Vector4>();
                for (int j = 0; j < joints.Length; j++)
                {
                    if (joints[j] < 0) continue;
                    Vector3 p = globals[joints[j]].GetColumn(3);
                    points.Add(new Vector4(p.x, p.y - HipHeight, p.z, ContactJoints[j].pad));
                }
                contacts[clip] = points.ToArray();
            }
            StandHeight = Clearance(Quaternion.identity, Clip.Idle);
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

        static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
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
