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
        const float FogBankRadius = 1800f;
        const float CloudDrift = 0.12f;
        const float MinSpan = 12f;
        const float MaxSpan = 40f;
        Camera view;

        public Material Props { get; private set; }
        public Transform Bank { get; private set; }
        public Material BankMaterial { get; private set; }
        Transform clouds;

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
            rig.clouds = Add(go.transform, "Clouds", Clouds(random), material);
            rig.BankMaterial = new Material(Resources.Load<Shader>("Shaders/FogBand")) { name = "Fog bank" };
            rig.Bank = Add(go.transform, "FogBank", FogBank(random), rig.BankMaterial);
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
            clouds.localRotation = Quaternion.Euler(0f, Time.time * CloudDrift, 0f);
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

        // Chunky flat-bottomed clouds of three to six faceted puffs, scattered at random bearings
        // (so they bunch and leave gaps) 9 to 30 degrees up: well clear of the horizon, inside the
        // chase camera's frame in both orientations. Sizes vary about threefold.
        static Mesh Clouds(System.Random random)
        {
            var mb = new MeshBuilder();
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)random.NextDouble() * 360f;
                float distance = 700f + (float)random.NextDouble() * 450f;
                float elevation = 9f + (float)random.NextDouble() * 21f;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var center = dir * distance + Vector3.up * distance * Mathf.Tan(elevation * Mathf.Deg2Rad);
                var side = Vector3.Cross(Vector3.up, dir);
                float size = (20f + Mathf.Pow((float)random.NextDouble(), 1.5f) * 40f) * distance / 900f;
                int puffs = 3 + random.Next(4);
                for (int p = 0; p < puffs; p++)
                {
                    float along = (p - (puffs - 1) * 0.5f) * size * 1.05f;
                    float middle = Mathf.Clamp01(1f - Mathf.Abs(along) / (puffs * size * 0.6f));
                    float r = size * (0.7f + (float)random.NextDouble() * 0.45f) * Mathf.Lerp(0.75f, 1.2f, middle);
                    var at = center + side * along + dir * ((float)random.NextDouble() - 0.5f) * size + Vector3.up * middle * size * 0.35f;
                    SpeedDial.Lump(mb, at, new Vector3(r * 1.25f, r, r), (float)random.NextDouble() * 360f, -0.2f, SpeedDial.Cloud, random, 0);
                }
            }
            var mesh = mb.ToMesh("clouds");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }

        // The fog bank lying on the bay: one smooth band low over the water, dense at the bottom and
        // fading out to a soft top edge, its height rolling gently along its length. It lies on the
        // bay side (east) and thins away to nothing to the west, so it never reads as hills.
        static Mesh FogBank(System.Random random)
        {
            const int segments = 160;
            const int rows = 6;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            float phase = (float)random.NextDouble() * 10f;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float bay = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.55f, d.x));
                float height = 70f * (0.8f + 0.2f * Mathf.Sin(a * 5f + phase) + 0.1f * Mathf.Sin(a * 13f + phase * 2f));
                for (int r = 0; r <= rows; r++)
                {
                    float t = r / (float)rows;
                    vertices.Add(d * (FogBankRadius - t * 60f) + Vector3.up * (t * height - 6f));
                    colors.Add(new Color(1f, 1f, 1f, bay * (1f - Mathf.SmoothStep(0f, 1f, t)) * Mathf.Lerp(1f, 0.85f, t)));
                }
            }
            for (int i = 0; i < segments; i++)
                for (int r = 0; r < rows; r++)
                {
                    int a = i * (rows + 1) + r, b = a + rows + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var mesh = new Mesh { name = "fog bank" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * DomeRadius * 2f);
            return mesh;
        }
    }
}
