using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Everything that sits at the horizon and travels with the camera: the gradient sky dome,
    // chunky faceted clouds and the San Francisco fog bank lying on the bay. Moving with the
    // camera on the ground plane keeps them at the horizon wherever you are in Dogpatch.
    [DefaultExecutionOrder(1000)]
    public sealed class SkyRig : MonoBehaviour
    {
        const float DomeRadius = 2600f;
        const float FogBankRadius = 1650f;
        Camera view;

        public Material Props { get; private set; }
        public Transform Bank { get; private set; }

        public static SkyRig Create(Camera view)
        {
            var go = new GameObject("SkyRig");
            var rig = go.AddComponent<SkyRig>();
            rig.view = view;
            var random = new System.Random(415);
            var flat = Resources.Load<Shader>("Shaders/FlatVertexColor");
            var material = new Material(flat) { name = "Sky props" };

            rig.Props = material;
            Add(go.transform, "Dome", Dome(), new Material(Resources.Load<Shader>("Shaders/Sky")) { name = "Sky" });
            Add(go.transform, "Clouds", Clouds(random), material);
            rig.Bank = Add(go.transform, "FogBank", FogBank(random), material);
            return rig;
        }

        static Transform Add(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        void LateUpdate()
        {
            var p = view.transform.position;
            transform.position = new Vector3(p.x, 0f, p.z);
        }

        // Gradient weights for the Sky shader: red blends horizon to middle, green middle to top.
        static Color SkyAt(float elevation)
        {
            if (elevation <= 0f) return new Color(0f, 0f, 0f, 1f);
            if (elevation < 18f) return new Color(Mathf.SmoothStep(0f, 1f, elevation / 18f), 0f, 0f, 1f);
            return new Color(1f, Mathf.SmoothStep(0f, 1f, (elevation - 18f) / 42f), 0f, 1f);
        }

        static Mesh Dome()
        {
            float[] rings = { -90f, -4f, 0f, 2f, 5f, 9f, 14f, 20f, 28f, 38f, 50f, 65f, 90f };
            const int segments = 48;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (float e in rings)
            {
                float y = Mathf.Sin(e * Mathf.Deg2Rad), r = Mathf.Cos(e * Mathf.Deg2Rad);
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * DomeRadius);
                    colors.Add(SkyAt(e));
                }
            }
            for (int i = 0; i < rings.Length - 1; i++)
                for (int s = 0; s < segments; s++)
                {
                    int a = i * (segments + 1) + s, b = a + segments + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var mesh = new Mesh { name = "sky dome" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }

        static Mesh Clouds(System.Random random)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < 16; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) / 16f * 360f;
                float distance = 1050f + (float)random.NextDouble() * 450f;
                float height = 170f + (float)random.NextDouble() * 230f;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var center = dir * distance + Vector3.up * height;
                var side = Vector3.Cross(Vector3.up, dir);
                float size = 26f + (float)random.NextDouble() * 22f;
                int puffs = 4 + random.Next(4);
                for (int p = 0; p < puffs; p++)
                {
                    float along = (p - (puffs - 1) * 0.5f) * size * 1.15f;
                    float lift = (1f - Mathf.Abs(along) / (puffs * size)) * size * 0.7f * (float)random.NextDouble();
                    float r = size * (0.75f + (float)random.NextDouble() * 0.6f);
                    SpeedDial.Lump(mb, center + side * along + Vector3.up * lift, new Vector3(r * 1.2f, r, r), (float)random.NextDouble() * 360f, -0.35f, SpeedDial.Cloud, random, 0);
                }
            }
            var mesh = mb.ToMesh("clouds");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }

        // The signature fog bank: a continuous faceted wall lying on the water all round the
        // horizon, with a jagged top, and rounded mounds rolling over it so it reads as one bank of
        // San Francisco fog rather than separate clouds.
        static Mesh FogBank(System.Random random)
        {
            var mb = new MeshBuilder();
            const int segments = 160;
            var tops = new float[segments];
            for (int i = 0; i < segments; i++) tops[i] = 38f + (float)random.NextDouble() * 22f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                float h0 = tops[i], h1 = tops[(i + 1) % segments];
                var front0 = d0 * FogBankRadius;
                var front1 = d1 * FogBankRadius;
                var crest0 = d0 * (FogBankRadius + 40f) + Vector3.up * h0;
                var crest1 = d1 * (FogBankRadius + 40f) + Vector3.up * h1;
                var back0 = d0 * (FogBankRadius + 120f) + Vector3.up * h0 * 0.6f;
                var back1 = d1 * (FogBankRadius + 120f) + Vector3.up * h1 * 0.6f;
                mb.Quad(front0 + Vector3.down * 4f, crest0, crest1, front1 + Vector3.down * 4f, SpeedDial.Cloud, -d0);
                mb.Quad(crest0, back0, back1, crest1, SpeedDial.Cloud, Vector3.up - d0 * 0.3f);
            }
            const int mounds = 110;
            for (int i = 0; i < mounds; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) / mounds * 360f;
                float distance = FogBankRadius + 50f + (float)random.NextDouble() * 60f;
                var center = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                float length = 90f + (float)random.NextDouble() * 70f;
                float height = 40f + (float)random.NextDouble() * 35f;
                SpeedDial.Lump(mb, center + Vector3.up * 30f, new Vector3(length, height, 60f), angle + 90f, -0.2f, SpeedDial.Cloud, random, 1);
            }
            var mesh = mb.ToMesh("fog bank");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }
    }
}
