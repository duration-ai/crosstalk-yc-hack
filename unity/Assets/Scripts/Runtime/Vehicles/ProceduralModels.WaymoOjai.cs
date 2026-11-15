using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // Waymo Ojai (the Zeekr-built robotaxi) after the Commons reference photos
        // (handoffs/waymo-refs.md): a tall, boxy, pale-blue-white van with a stubby nose and a near-flat
        // front, a black face band holding thin headlamps and the centre sensor, a steep windscreen,
        // black wraparound glass, a big sliding rear door, black arches and bumpers, flat-cover wheels,
        // corner camera pods, a black roof sensor bar across the front of the roof carrying the dome,
        // and a big black rear window with vertical tail lamps.
        public static (GameObject, Bounds) WaymoOjai(Material material)
        {
            const float half = 0.98f;
            var body = Cached("waymo ojai body v2", () =>
            {
                var lp = new LowPoly();
                var white = C("#dfe9f7");
                var roofTop = C("#eef3fa");
                var glass = C("#1a1e26");
                var black = C("#1c1d20");
                var seam = C("#b7c0cc");
                lp.Prism(new[]
                {
                    new Vector2(-2.28f, 0.32f), new Vector2(-2.33f, 1.12f), new Vector2(2.08f, 1.12f), new Vector2(2.3f, 1.0f),
                    new Vector2(2.36f, 0.62f), new Vector2(2.3f, 0.32f),
                }, half, white, roofTop);
                var roof = new[]
                {
                    new Vector2(2.08f, 1.12f), new Vector2(1.45f, 1.9f), new Vector2(1.2f, 1.96f), new Vector2(-2.12f, 1.96f),
                    new Vector2(-2.3f, 1.84f), new Vector2(-2.33f, 1.12f),
                };
                Greenhouse(lp, roof, half, 0.9f, z => 1.12f, i => i <= 2 ? glass : white, i => i == 0 || i == 4 ? glass : roofTop);
                foreach (float s in new[] { 1f, -1f })
                {
                    float x = s * (half + 0.006f);
                    foreach (float zc in new[] { 1.58f, -1.52f })
                        Side(lp, x, new[]
                        {
                            new Vector2(zc - 0.56f, 0.32f), new Vector2(zc - 0.5f, 0.66f), new Vector2(zc - 0.26f, 0.86f), new Vector2(zc + 0.26f, 0.86f),
                            new Vector2(zc + 0.5f, 0.66f), new Vector2(zc + 0.56f, 0.32f),
                        }, black);
                    Side(lp, x, new[] { new Vector2(-0.96f, 0.32f), new Vector2(-0.96f, 0.42f), new Vector2(1.02f, 0.42f), new Vector2(1.02f, 0.32f) }, black);
                    foreach (float z in new[] { -1.0f, 0.3f, 1.0f })
                        lp.Box(new Vector3(s * (half + 0.01f), 0.9f, z), new Vector3(0.015f, 1.0f, 0.03f), seam);
                    lp.Box(new Vector3(s * (half + 0.012f), 1.14f, -0.4f), new Vector3(0.02f, 0.03f, 1.6f), seam);
                    lp.Box(new Vector3(s * (half + 0.03f), 0.86f, -0.15f), new Vector3(0.04f, 0.05f, 0.22f), black);
                    lp.Box(new Vector3(s * 0.94f, 0.92f, 2.18f), new Vector3(0.16f, 0.26f, 0.3f), black);
                    lp.Box(new Vector3(s * 1.04f, 1.42f, 1.46f), new Vector3(0.16f, 0.16f, 0.16f), black);
                    lp.Box(new Vector3(s * 0.6f, 0.83f, 2.33f), new Vector3(0.4f, 0.06f, 0.04f), C("#f7f8f9", 0.9f));
                    lp.Box(new Vector3(s * 0.86f, 1.15f, -2.335f), new Vector3(0.12f, 0.42f, 0.04f), C("#d1281f", 0.55f));
                    lp.Box(new Vector3(s * 0.72f, 2.05f, 1.2f), new Vector3(0.24f, 0.16f, 0.28f), black);
                }
                lp.Quad(new Vector3(-0.88f, 0.72f, 2.33f), new Vector3(-0.86f, 0.94f, 2.3f), new Vector3(0.86f, 0.94f, 2.3f), new Vector3(0.88f, 0.72f, 2.33f), black);
                lp.Box(new Vector3(0f, 0.58f, 2.37f), new Vector3(0.12f, 0.2f, 0.05f), black);
                lp.Box(new Vector3(0f, 0.38f, 2.32f), new Vector3(1.7f, 0.14f, 0.06f), black);
                lp.Quad(new Vector3(0.78f, 1.25f, -2.345f), new Vector3(0.76f, 1.8f, -2.32f), new Vector3(-0.76f, 1.8f, -2.32f), new Vector3(-0.78f, 1.25f, -2.345f), glass);
                lp.Box(new Vector3(0f, 0.4f, -2.29f), new Vector3(1.8f, 0.18f, 0.08f), black);
                lp.Box(new Vector3(0f, 1.9f, -2.3f), new Vector3(1.5f, 0.06f, 0.14f), black);
                lp.Box(new Vector3(0f, 2.03f, 1.2f), new Vector3(1.5f, 0.14f, 0.5f), black);
                lp.Box(new Vector3(0f, 2.03f, 1.455f), new Vector3(0.5f, 0.05f, 0.01f), C("#3fa7ff", 0.8f));
                lp.Box(new Vector3(0f, 2.01f, -1.9f), new Vector3(0.6f, 0.1f, 0.24f), black);
                lp.Box(new Vector3(0f, 0.24f, 0f), new Vector3(1.7f, 0.14f, 4.4f), black);
                return lp;
            });
            var brake = Cached("waymo ojai brake", () =>
            {
                var lp = new LowPoly();
                foreach (float s in new[] { 1f, -1f }) lp.Box(new Vector3(s * 0.86f, 1.15f, -2.36f), new Vector3(0.14f, 0.44f, 0.03f), C("#ff2a1f", 1f));
                return lp;
            });
            var wheel = Cached("ojai wheel", () =>
            {
                var lp = new LowPoly();
                lp.CylinderX(Vector3.zero, 0.4f, 0.28f, 12, C("#1c1d20"), C("#26282c"));
                lp.CylinderX(Vector3.zero, 0.28f, 0.29f, 10, C("#3a3f46"), C("#3a3f46"));
                foreach (float a in new[] { 45f, -45f })
                    lp.Box(Vector3.zero, new Vector3(0.3f, 0.5f, 0.05f), Quaternion.Euler(a, 0f, 0f), C("#1c1d20"));
                return lp;
            });
            var root = new GameObject("waymo-ojai");
            Part(root.transform, "body", body, material, Vector3.zero);
            Part(root.transform, "brake-glow", brake, material, Vector3.zero).SetActive(false);
            Wheels(root.transform, material, wheel, 0.86f, 1.58f, -1.52f, 0.4f);
            WaymoDome(root.transform, material, new Vector3(0f, 2.1f, 1.15f), 0.85f);
            return (root, new Bounds(new Vector3(0f, 1.25f, 0f), new Vector3(2.1f, 2.5f, 4.74f)));
        }
    }
}
