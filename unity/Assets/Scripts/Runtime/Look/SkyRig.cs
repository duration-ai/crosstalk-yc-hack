using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Everything that sits at the horizon and travels with the camera: the gradient sky dome,
    // chunky faceted clouds and the San Francisco fog bank lying on the bay. Moving with the
    // camera on the ground plane keeps them at the horizon wherever you are in Dogpatch.
    [DefaultExecutionOrder(1200)]
    public sealed class SkyRig : MonoBehaviour
    {
        const float DomeRadius = 2600f;
        const float FogBankRadius = 1900f;
        const float MinSpan = 12f;
        const float MaxSpan = 40f;
        Camera view;

        public Material Props { get; private set; }
        public Transform Bank { get; private set; }

        public static SkyRig Create(Camera view)
        {
            var go = new GameObject("SkyRig");
            var rig = go.AddComponent<SkyRig>();
            rig.view = view;
            var random = new System.Random(415);
            var material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "Sky props" };
            material.SetFloat("_FogAmount", 0.2f);

            rig.Props = material;
            Add(go.transform, "Dome", Dome(), new Material(Resources.Load<Shader>("Shaders/Sky")) { name = "Sky" });
            Add(go.transform, "Clouds", Clouds(random), material);
            rig.Bank = Add(go.transform, "FogBank", FogBank(random), material);
            rig.LateUpdate();
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

        // Runs after the follow camera and CameraFx have set this frame's pose and field of view.
        // The deep top colour lands at the top edge of the frame, so the low chase camera shows the
        // whole gradient from the art instead of only the pale band near the horizon.
        void LateUpdate()
        {
            var p = view.transform.position;
            transform.position = new Vector3(p.x, 0f, p.z);
            float pitch = Mathf.Asin(Mathf.Clamp(view.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float span = Mathf.Clamp(pitch + view.fieldOfView * 0.5f, MinSpan, MaxSpan);
            Shader.SetGlobalFloat("_SkySpan", Mathf.Sin(span * Mathf.Deg2Rad));
        }

        static Mesh Dome()
        {
            float[] rings = { -90f, -4f, 0f, 2f, 5f, 9f, 14f, 20f, 28f, 38f, 50f, 65f, 90f };
            const int segments = 48;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (float e in rings)
            {
                float y = Mathf.Sin(e * Mathf.Deg2Rad), r = Mathf.Cos(e * Mathf.Deg2Rad);
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * DomeRadius);
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
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }

        // Chunky flat-bottomed clouds of four to seven faceted puffs, 5 to 20 degrees up, big
        // enough to read on a phone (reference 01 and 02).
        static Mesh Clouds(System.Random random)
        {
            var mb = new MeshBuilder();
            const int count = 22;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.7f) / count * 360f;
                float distance = 850f + (float)random.NextDouble() * 500f;
                float elevation = 5f + Mathf.Pow((float)random.NextDouble(), 1.4f) * 15f;
                float height = distance * Mathf.Tan(elevation * Mathf.Deg2Rad);
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var center = dir * distance + Vector3.up * height;
                var side = Vector3.Cross(Vector3.up, dir);
                float size = (34f + (float)random.NextDouble() * 26f) * distance / 1000f;
                int puffs = 4 + random.Next(4);
                for (int p = 0; p < puffs; p++)
                {
                    float along = (p - (puffs - 1) * 0.5f) * size * 1.05f;
                    float middle = 1f - Mathf.Abs(along) / (puffs * size * 0.6f);
                    float r = size * (0.7f + (float)random.NextDouble() * 0.45f) * Mathf.Lerp(0.75f, 1.2f, Mathf.Clamp01(middle));
                    float lift = Mathf.Clamp01(middle) * size * 0.35f;
                    var at = center + side * along + dir * ((float)random.NextDouble() - 0.5f) * size + Vector3.up * lift;
                    SpeedDial.Lump(mb, at, new Vector3(r * 1.25f, r, r), (float)random.NextDouble() * 360f, -0.2f, SpeedDial.Cloud, random, 0);
                }
            }
            var mesh = mb.ToMesh("clouds");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }

        // The signature fog bank: a continuous faceted wall lying on the water all round the
        // horizon, with a jagged top, and rounded mounds rolling over it so it reads as one bank of
        // San Francisco fog rather than separate clouds. It piles up highest to the east, over the
        // bay, and thins out over the hills to the west.
        static Mesh FogBank(System.Random random)
        {
            var mb = new MeshBuilder();
            const int segments = 180;
            static float Bay(Vector3 d) => Mathf.Lerp(0.5f, 1.25f, Mathf.SmoothStep(0f, 1f, d.x * 0.5f + 0.5f));
            var tops = new float[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                tops[i] = (62f + (float)random.NextDouble() * 30f) * Bay(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
            }
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                float h0 = tops[i], h1 = tops[(i + 1) % segments];
                var front0 = d0 * FogBankRadius;
                var front1 = d1 * FogBankRadius;
                var crest0 = d0 * (FogBankRadius + 50f) + Vector3.up * h0;
                var crest1 = d1 * (FogBankRadius + 50f) + Vector3.up * h1;
                var back0 = d0 * (FogBankRadius + 150f) + Vector3.up * h0 * 0.6f;
                var back1 = d1 * (FogBankRadius + 150f) + Vector3.up * h1 * 0.6f;
                mb.Quad(front0 + Vector3.down * 4f, crest0, crest1, front1 + Vector3.down * 4f, SpeedDial.Cloud, -d0);
                mb.Quad(crest0, back0, back1, crest1, SpeedDial.Cloud, Vector3.up - d0 * 0.3f);
            }
            const int mounds = 130;
            for (int i = 0; i < mounds; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) / mounds * 360f;
                float distance = FogBankRadius + 60f + (float)random.NextDouble() * 70f;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float bay = Bay(dir);
                float length = (110f + (float)random.NextDouble() * 80f) * Mathf.Lerp(0.8f, 1.1f, bay);
                float height = (50f + (float)random.NextDouble() * 40f) * bay;
                SpeedDial.Lump(mb, dir * distance + Vector3.up * 34f * bay, new Vector3(length, height, 70f), angle + 90f, -0.2f, SpeedDial.Cloud, random, 1);
            }
            var mesh = mb.ToMesh("fog bank");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }
    }
}
