using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Builds the static particle mesh that Shaders/Precipitation animates on the GPU around the
    // camera: rain streaks or snow flakes, thinned by the material's _Amount.
    public static class Precipitation
    {
        public static Material Create(Transform parent, bool snow)
        {
            int count = snow ? 1400 : 2200;
            var random = new System.Random(snow ? 91 : 33);
            var vertices = new Vector3[count * 4];
            var uvs = new Vector4[count * 4];
            var indices = new int[count * 6];
            Vector2[] corners = { new Vector2(-1f, -0.5f), new Vector2(1f, -0.5f), new Vector2(1f, 0.5f), new Vector2(-1f, 0.5f) };
            for (int i = 0; i < count; i++)
            {
                var anchor = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                float id = (i + 0.5f) / count;
                for (int c = 0; c < 4; c++)
                {
                    vertices[i * 4 + c] = anchor;
                    uvs[i * 4 + c] = new Vector4(corners[c].x, corners[c].y, id, 0f);
                }
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = snow ? "snow" : "rain" };
            mesh.vertices = vertices;
            mesh.SetUVs(0, uvs);
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

            var material = new Material(Resources.Load<Shader>("Shaders/Precipitation")) { name = mesh.name };
            material.SetFloat("_Snow", snow ? 1f : 0f);
            material.SetFloat("_Box", snow ? 26f : 30f);
            material.SetFloat("_Fall", snow ? 1.6f : 16f);
            material.SetVector("_Size", snow ? new Vector4(0.06f, 0.12f, 0f, 0f) : new Vector4(0.012f, 0.75f, 0f, 0f));
            material.SetColor("_Color", snow ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.86f, 0.92f, 1f, 0.38f));
            material.SetFloat("_Amount", 0f);

            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return material;
        }
    }
}
