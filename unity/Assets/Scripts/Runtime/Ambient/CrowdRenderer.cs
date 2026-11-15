using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Ambient
{
    // Collects this frame's crowd instances per model and draws each model with one instanced call
    // (two when some instances are close enough to cast shadows and others are not). The material
    // is a committed asset with instancing enabled, so builds keep the shader's instancing variants.
    public sealed class CrowdRenderer
    {
        public const int MaxPerBatch = 128;

        public struct Outfit
        {
            public Vector4 Top, Under, Bottom, Hair, Skin;
        }

        sealed class Batch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxPerBatch];
            public readonly Vector4[] Anim = new Vector4[MaxPerBatch];
            public readonly Vector4[] Top = new Vector4[MaxPerBatch];
            public readonly Vector4[] Under = new Vector4[MaxPerBatch];
            public readonly Vector4[] Bottom = new Vector4[MaxPerBatch];
            public readonly Vector4[] Hair = new Vector4[MaxPerBatch];
            public readonly Vector4[] Skin = new Vector4[MaxPerBatch];
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public int Count;
            public Bounds Bounds;
        }

        static readonly int AnimId = Shader.PropertyToID("_Anim");
        static readonly int TopId = Shader.PropertyToID("_Top");
        static readonly int UnderId = Shader.PropertyToID("_Under");
        static readonly int BottomId = Shader.PropertyToID("_Bottom");
        static readonly int HairId = Shader.PropertyToID("_Hair");
        static readonly int SkinId = Shader.PropertyToID("_Skin");
        static readonly int BonesId = Shader.PropertyToID("_Bones");
        static readonly int HeadPivotId = Shader.PropertyToID("_HeadPivot");
        static readonly int BoneTexelId = Shader.PropertyToID("_BoneTexel");

        readonly Material material;
        readonly Dictionary<CrowdModel, Batch[]> batches = new Dictionary<CrowdModel, Batch[]>();

        public int DrawCalls { get; private set; }
        public int Instances { get; private set; }

        public CrowdRenderer()
        {
            material = Resources.Load<Material>("Ambient/CrowdVertexColor");
        }

        public void Add(CrowdModel model, in Matrix4x4 matrix, Vector4 anim, in Outfit outfit, bool shadows)
        {
            if (!batches.TryGetValue(model, out var pair)) batches[model] = pair = new[] { new Batch(), new Batch() };
            var b = pair[shadows ? 1 : 0];
            if (b.Count >= MaxPerBatch) return;
            int i = b.Count++;
            b.Matrices[i] = matrix;
            b.Anim[i] = anim;
            b.Top[i] = outfit.Top;
            b.Under[i] = outfit.Under;
            b.Bottom[i] = outfit.Bottom;
            b.Hair[i] = outfit.Hair;
            b.Skin[i] = outfit.Skin;
            var p = new Vector3(matrix.m03, matrix.m13 + 1f, matrix.m23);
            if (i == 0) b.Bounds = new Bounds(p, new Vector3(3f, 2.6f, 3f));
            else b.Bounds.Encapsulate(new Bounds(p, new Vector3(3f, 2.6f, 3f)));
        }

        public void Submit()
        {
            DrawCalls = 0;
            Instances = 0;
            foreach (var pair in batches)
            {
                var model = pair.Key;
                for (int s = 0; s < 2; s++)
                {
                    var b = pair.Value[s];
                    if (b.Count == 0) continue;
                    b.Block.SetTexture(BonesId, model.Bones);
                    b.Block.SetVector(HeadPivotId, model.HeadPivot);
                    b.Block.SetVector(BoneTexelId, new Vector4(1f / model.Bones.width, 1f / model.Bones.height, 0f, 0f));
                    b.Block.SetVectorArray(AnimId, b.Anim);
                    b.Block.SetVectorArray(TopId, b.Top);
                    b.Block.SetVectorArray(UnderId, b.Under);
                    b.Block.SetVectorArray(BottomId, b.Bottom);
                    b.Block.SetVectorArray(HairId, b.Hair);
                    b.Block.SetVectorArray(SkinId, b.Skin);
                    var rp = new RenderParams(material)
                    {
                        matProps = b.Block,
                        worldBounds = b.Bounds,
                        shadowCastingMode = s == 1 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                        receiveShadows = true,
                    };
                    Graphics.RenderMeshInstanced(rp, model.Mesh, 0, b.Matrices, b.Count);
                    DrawCalls++;
                    Instances += b.Count;
                    b.Count = 0;
                }
            }
        }
    }
}
