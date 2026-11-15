using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // A side-on body made of a lower prism up to the belt line and a greenhouse lofted from the belt
        // (full width) to a narrower roof line, so the glass leans in like a real car. roof is the top
        // edge from front (windscreen base on the belt) to back; colour picks each greenhouse side panel
        // by its z, so pillars can be body colour and the rest glass.
        static void Greenhouse(LowPoly lp, Vector2[] roof, float beltHalf, float roofHalf, System.Func<float, float> beltY,
            System.Func<int, Color> sideColour, System.Func<int, Color> topColour)
        {
            for (int i = 0; i < roof.Length - 1; i++)
            {
                var a = roof[i];
                var b = roof[i + 1];
                lp.Quad(new Vector3(-roofHalf, a.y, a.x), new Vector3(-roofHalf, b.y, b.x), new Vector3(roofHalf, b.y, b.x), new Vector3(roofHalf, a.y, a.x), topColour(i));
                foreach (float s in new[] { 1f, -1f })
                {
                    var a0 = new Vector3(s * beltHalf, beltY(a.x), a.x);
                    var b0 = new Vector3(s * beltHalf, beltY(b.x), b.x);
                    var a1 = new Vector3(s * roofHalf, a.y, a.x);
                    var b1 = new Vector3(s * roofHalf, b.y, b.x);
                    lp.Tri(a0, a1, b1, sideColour(i));
                    lp.Tri(a0, b1, b0, sideColour(i));
                }
            }
        }

        // Fifth-generation Waymo roof sensor stack: a black base drum, the blue rider-display ring, the
        // tall black lidar hat and, on top, the spinning lidar can with a cyan emitter. Scaled up a
        // little so it reads from the chase camera.
        static void WaymoDome(Transform root, Material material, Vector3 position, float scale)
        {
            var dome = Cached($"waymo dome {scale:F2}", () =>
            {
                var lp = new LowPoly();
                var black = C("#1c1d20");
                var dark = C("#2b2e33");
                var blue = C("#3fa7ff", 0.9f);
                float k = scale;
                lp.CylinderY(Vector3.zero, 0.36f * k, 0.34f * k, 0.14f * k, 12, dark, black);
                lp.CylinderY(new Vector3(0f, 0.14f * k, 0f), 0.345f * k, 0.345f * k, 0.05f * k, 12, blue, blue);
                lp.CylinderY(new Vector3(0f, 0.19f * k, 0f), 0.33f * k, 0.26f * k, 0.22f * k, 12, black, black);
                lp.CylinderY(new Vector3(0f, 0.41f * k, 0f), 0.2f * k, 0.2f * k, 0.04f * k, 10, dark, dark);
                return lp;
            });
            var puck = Cached($"waymo lidar {scale:F2}", () =>
            {
                var lp = new LowPoly();
                float k = scale;
                lp.CylinderY(Vector3.zero, 0.19f * k, 0.17f * k, 0.2f * k, 10, C("#1c1d20"), C("#2b2e33"));
                lp.Box(new Vector3(0f, 0.1f * k, 0.18f * k), new Vector3(0.14f * k, 0.08f * k, 0.03f), C("#5ff2ff", 1f));
                return lp;
            });
            var go = Part(root, "sensor-dome", dome, material, position);
            Part(go.transform, "lidar", puck, material, new Vector3(0f, 0.45f * scale, 0f)).AddComponent<Spinner>();
        }

        // Waymo Jaguar I-Pace after the Commons reference photos (handoffs/waymo-refs.md): a low, wide,
        // cab-forward crossover with a short bonnet, a fast-sloping rear, black glass and roof, black
        // wheel arches, rocker cladding and big dark wheels, the black hexagonal grille, the two fender
        // camera pods, mirror cameras, bumper perimeter lidars, a white roof rack carrying the dome, and
        // the thin full-width tail lamp.
        public static (GameObject, Bounds) WaymoIPace(Material material)
        {
            const float half = 0.96f;
            var body = Cached("waymo ipace body", () =>
            {
                var lp = new LowPoly();
                var white = C("#f4f5f6");
                var whiteTop = C("#fbfbfc");
                var glass = C("#1a1e26");
                var black = C("#1c1d20");
                var blue = C("#3fa7ff", 0.6f);
                lp.Prism(new[]
                {
                    new Vector2(-2.2f, 0.3f), new Vector2(-2.34f, 0.55f), new Vector2(-2.36f, 0.95f), new Vector2(-2.24f, 1.06f),
                    new Vector2(1.05f, 1.0f), new Vector2(2.3f, 0.82f), new Vector2(2.4f, 0.58f), new Vector2(2.3f, 0.3f),
                }, half, white, whiteTop);
                var roof = new[]
                {
                    new Vector2(1.05f, 1.0f), new Vector2(0.18f, 1.5f), new Vector2(-1.0f, 1.53f), new Vector2(-1.4f, 1.5f),
                    new Vector2(-1.95f, 1.38f), new Vector2(-2.24f, 1.06f),
                };
                float Belt(float z) => Mathf.Lerp(1.06f, 1.0f, Mathf.InverseLerp(-2.24f, 1.05f, z));
                Greenhouse(lp, roof, half, 0.76f, Belt, i => i >= 3 ? white : glass, i => i == 0 || i == 4 ? glass : black);
                foreach (float s in new[] { 1f, -1f })
                {
                    float x = s * (half + 0.006f);
                    foreach (float zc in new[] { 1.48f, -1.5f })
                        Side(lp, x, new[]
                        {
                            new Vector2(zc - 0.6f, 0.3f), new Vector2(zc - 0.54f, 0.64f), new Vector2(zc - 0.3f, 0.86f), new Vector2(zc + 0.3f, 0.86f),
                            new Vector2(zc + 0.54f, 0.64f), new Vector2(zc + 0.6f, 0.3f),
                        }, black);
                    Side(lp, x, new[] { new Vector2(-0.9f, 0.3f), new Vector2(-0.9f, 0.45f), new Vector2(0.88f, 0.45f), new Vector2(0.88f, 0.3f) }, black);
                    Side(lp, x, new[] { new Vector2(-0.05f, 0.46f), new Vector2(-0.05f, 1.02f), new Vector2(-0.02f, 1.02f), new Vector2(-0.02f, 0.46f) }, C("#c9ced4"));
                    Side(lp, x, new[] { new Vector2(-1.6f, 0.98f), new Vector2(-1.6f, 1.01f), new Vector2(0.9f, 0.98f), new Vector2(0.9f, 0.95f) }, blue);
                    lp.Box(new Vector3(s * 0.99f, 1.02f, 1.62f), new Vector3(0.18f, 0.2f, 0.3f), black);
                    lp.Box(new Vector3(s * 1.09f, 1.02f, 1.62f), new Vector3(0.02f, 0.12f, 0.18f), glass);
                    lp.Box(new Vector3(s * 1.04f, 1.1f, 0.9f), new Vector3(0.18f, 0.11f, 0.13f), white);
                    lp.Box(new Vector3(s * 1.04f, 1.2f, 0.9f), new Vector3(0.14f, 0.09f, 0.14f), black);
                    lp.Box(new Vector3(s * 0.8f, 0.52f, 2.32f), new Vector3(0.18f, 0.16f, 0.14f), black);
                    lp.Box(new Vector3(s * 0.76f, 0.56f, -2.3f), new Vector3(0.16f, 0.14f, 0.12f), black);
                    lp.Box(new Vector3(s * 0.62f, 0.76f, 2.34f), new Vector3(0.46f, 0.07f, 0.05f), Quaternion.Euler(0f, 0f, s * 6f), C("#f7f8f9", 0.9f));
                    lp.Box(new Vector3(s * 0.66f, 0.92f, -2.34f), new Vector3(0.4f, 0.09f, 0.05f), C("#d1281f", 0.55f));
                }
                lp.Box(new Vector3(0f, 0.58f, 2.4f), new Vector3(0.82f, 0.3f, 0.05f), black);
                for (int i = -3; i <= 3; i++)
                    lp.Box(new Vector3(i * 0.11f, 0.58f, 2.425f), new Vector3(0.02f, 0.26f, 0.01f), C("#3a3f46"));
                lp.Box(new Vector3(0f, 0.37f, 2.34f), new Vector3(1.5f, 0.12f, 0.05f), black);
                lp.Box(new Vector3(0f, 0.93f, -2.36f), new Vector3(0.95f, 0.035f, 0.04f), C("#d1281f", 0.55f));
                lp.Box(new Vector3(0f, 0.42f, -2.28f), new Vector3(1.7f, 0.22f, 0.1f), black);
                lp.Box(new Vector3(0f, 1.38f, -1.98f), new Vector3(1.5f, 0.05f, 0.25f), Quaternion.Euler(-12f, 0f, 0f), black);
                lp.Box(new Vector3(0f, 1.56f, -0.55f), new Vector3(1.36f, 0.06f, 1.25f), whiteTop);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 0.6f, 1.64f, 0.0f), new Vector3(0.22f, 0.14f, 0.2f), black);
                    lp.Box(new Vector3(s * 0.62f, 1.64f, -1.1f), new Vector3(0.2f, 0.12f, 0.16f), black);
                }
                lp.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.6f, 0.14f, 4.2f), black);
                return lp;
            });
            var brake = Cached("waymo ipace brake", () =>
            {
                var lp = new LowPoly();
                var red = C("#ff2a1f", 1f);
                lp.Box(new Vector3(0f, 0.93f, -2.375f), new Vector3(1.0f, 0.05f, 0.03f), red);
                foreach (float s in new[] { 1f, -1f }) lp.Box(new Vector3(s * 0.66f, 0.92f, -2.37f), new Vector3(0.44f, 0.11f, 0.03f), red);
                return lp;
            });
            var root = new GameObject("waymo-ipace");
            Part(root.transform, "body", body, material, Vector3.zero);
            Part(root.transform, "brake-glow", brake, material, Vector3.zero).SetActive(false);
            Wheels(root.transform, material, Wheel(0.41f, 0.3f, "#1c1d20", "#2f3338", 12), 0.84f, 1.48f, -1.5f, 0.41f);
            WaymoDome(root.transform, material, new Vector3(0f, 1.59f, -0.5f), 1.15f);
            return (root, new Bounds(new Vector3(0f, 1.1f, 0f), new Vector3(2.2f, 2.2f, 4.8f)));
        }
    }
}
