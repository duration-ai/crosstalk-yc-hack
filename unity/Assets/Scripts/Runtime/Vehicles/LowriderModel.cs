using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // '64 Impala-style lowrider: a long, low, wide hardtop in candy purple under a pearl roof,
        // chrome bumpers, grille, flank spear and rockers, triple round tail lamps, and small wire
        // wheels on fat whitewalls. The body hangs off a "lowrider-frame" node so Hydraulics can
        // bounce it on the wheels.
        public static (GameObject, Bounds) Lowrider(Material material)
        {
            var body = Cached("lowrider body", () =>
            {
                var lp = new LowPoly();
                var candy = C("#8e1fb5");
                var candyTop = C("#b43ad0");
                var roof = C("#f2e8f6");
                var glass = C("#1d2440");
                var chrome = C("#eef3f7", 0.2f);
                var dark = C("#17181c");
                lp.Prism(new[]
                {
                    new Vector2(-2.78f, 0.32f), new Vector2(-2.84f, 0.8f), new Vector2(-2.5f, 0.88f), new Vector2(2.4f, 0.88f),
                    new Vector2(2.84f, 0.8f), new Vector2(2.84f, 0.32f),
                }, 1.02f, candy, candyTop);
                lp.Prism(new[]
                {
                    new Vector2(-1.58f, 0.87f), new Vector2(-1.08f, 1.38f), new Vector2(0.36f, 1.38f), new Vector2(1.06f, 0.87f),
                }, 0.88f, candy, roof);
                lp.Quad(new Vector3(-0.8f, 0.92f, 1.05f), new Vector3(-0.77f, 1.37f, 0.4f), new Vector3(0.77f, 1.37f, 0.4f), new Vector3(0.8f, 0.92f, 1.05f), glass);
                lp.Quad(new Vector3(0.8f, 0.92f, -1.57f), new Vector3(0.77f, 1.37f, -1.1f), new Vector3(-0.77f, 1.37f, -1.1f), new Vector3(-0.8f, 0.92f, -1.57f), glass);
                foreach (float s in new[] { 1f, -1f })
                {
                    Side(lp, s * 0.885f, new[] { new Vector2(-1.42f, 0.95f), new Vector2(-1.02f, 1.33f), new Vector2(0.32f, 1.33f), new Vector2(0.95f, 0.95f) }, glass);
                    Side(lp, s * 0.886f, new[] { new Vector2(-0.33f, 0.95f), new Vector2(-0.33f, 1.33f), new Vector2(-0.27f, 1.33f), new Vector2(-0.27f, 0.95f) }, chrome);
                    Side(lp, s * 1.025f, new[] { new Vector2(-2.7f, 0.64f), new Vector2(-2.7f, 0.7f), new Vector2(2.7f, 0.7f), new Vector2(2.7f, 0.64f) }, chrome);
                    Side(lp, s * 1.025f, new[] { new Vector2(-2.2f, 0.34f), new Vector2(-2.2f, 0.4f), new Vector2(2.2f, 0.4f), new Vector2(2.2f, 0.34f) }, chrome);
                    for (int i = 0; i < 3; i++)
                        lp.Box(new Vector3(s * (0.42f + i * 0.2f), 0.64f, -2.86f), new Vector3(0.15f, 0.15f, 0.04f), C("#ff2a2a", 0.7f));
                    lp.Box(new Vector3(s * 0.55f, 0.6f, 2.86f), new Vector3(0.17f, 0.17f, 0.04f), C("#fff6d8", 1f));
                    lp.Box(new Vector3(s * 0.8f, 0.6f, 2.86f), new Vector3(0.17f, 0.17f, 0.04f), C("#fff6d8", 1f));
                }
                lp.Box(new Vector3(0f, 0.6f, 2.85f), new Vector3(0.9f, 0.2f, 0.03f), C("#aab3bb", 0.1f));
                lp.Box(new Vector3(0f, 0.4f, 2.9f), new Vector3(2.12f, 0.17f, 0.16f), chrome);
                lp.Box(new Vector3(0f, 0.42f, -2.88f), new Vector3(2.12f, 0.17f, 0.16f), chrome);
                lp.Box(new Vector3(0f, 0.92f, 2.2f), new Vector3(0.05f, 0.08f, 0.3f), chrome);
                lp.Box(new Vector3(0f, 0.3f, 0f), new Vector3(1.8f, 0.1f, 5.3f), dark);
                return lp;
            });
            var root = new GameObject("lowrider");
            var frame = Part(root.transform, "lowrider-frame", null, material, Vector3.zero);
            Part(frame.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Whitewall(0.34f, 0.24f), 0.93f, 1.8f, -1.65f, 0.34f);
            return (root, new Bounds(new Vector3(0f, 0.7f, 0f), new Vector3(2.12f, 1.4f, 5.8f)));
        }

        // Small wire wheel on a fat whitewall: black tread, a wide white ring, a chrome rim with spokes.
        static Mesh Whitewall(float radius, float width) =>
            Cached($"whitewall {radius} {width}", () =>
            {
                var lp = new LowPoly();
                var chrome = C("#eef3f7", 0.2f);
                lp.CylinderX(Vector3.zero, radius, width, 12, C("#18191c"), C("#18191c"));
                lp.CylinderX(Vector3.zero, radius * 0.82f, width * 1.03f, 12, C("#f6f6f0"), C("#f6f6f0"));
                lp.CylinderX(Vector3.zero, radius * 0.55f, width * 1.06f, 12, chrome, chrome);
                for (int i = 0; i < 4; i++)
                    lp.Box(Vector3.zero, new Vector3(width * 1.1f, radius * 1.05f, 0.025f), Quaternion.Euler(i * 45f, 0f, 0f), C("#9aa4ad"));
                lp.CylinderX(Vector3.zero, radius * 0.16f, width * 1.2f, 6, C("#d4a72c", 0.2f), C("#d4a72c", 0.2f));
                return lp;
            });
    }
}
