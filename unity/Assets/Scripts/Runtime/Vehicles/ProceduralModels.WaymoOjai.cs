using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // Waymo Ojai (the Zeekr-built robotaxi): a tall boxy white minivan with a flat front, a dark
        // glass band all round, a big sliding side door, a teal-blue Waymo stripe, and the shared robotaxi
        // sensor dome and corner pods (VehicleModels adds them for Robotaxi specs).
        public static (GameObject, Bounds) WaymoOjai(Material material)
        {
            var body = Cached("waymo ojai body", () =>
            {
                var lp = new LowPoly();
                var white = C("#f4f5f6");
                var roof = C("#e4e7ea");
                var glass = C("#1d222b");
                var seam = C("#b9bec4");
                var dark = C("#2a2e34");
                var teal = C("#3ec7c1");
                var blue = C("#4d8bf5");
                var profile = new[]
                {
                    new Vector2(-2.35f, 0.32f), new Vector2(-2.36f, 1.85f), new Vector2(-2.2f, 1.97f), new Vector2(2.12f, 1.97f),
                    new Vector2(2.34f, 1.78f), new Vector2(2.38f, 0.32f),
                };
                lp.Prism(profile, 0.97f, white, roof);
                foreach (float s in new[] { 1f, -1f })
                {
                    Side(lp, s * 0.975f, new[] { new Vector2(-2.15f, 1.15f), new Vector2(-2.15f, 1.8f), new Vector2(2.05f, 1.8f), new Vector2(2.25f, 1.15f) }, glass);
                    Side(lp, s * 0.976f, new[] { new Vector2(-2.3f, 0.92f), new Vector2(-2.3f, 1.0f), new Vector2(2.3f, 1.0f), new Vector2(2.3f, 0.92f) }, teal);
                    Side(lp, s * 0.976f, new[] { new Vector2(-2.3f, 0.86f), new Vector2(-2.3f, 0.92f), new Vector2(2.3f, 0.92f), new Vector2(2.3f, 0.86f) }, blue);
                    foreach (float z in new[] { -0.45f, 0.95f })
                        lp.Box(new Vector3(s * 0.98f, 1.1f, z), new Vector3(0.02f, 1.5f, 0.04f), seam);
                    lp.Box(new Vector3(s * 0.99f, 0.62f, 0.05f), new Vector3(0.02f, 0.03f, 1.3f), seam);
                    lp.Box(new Vector3(s * 0.99f, 0.78f, 0.75f), new Vector3(0.03f, 0.05f, 0.18f), dark);
                }
                lp.Quad(new Vector3(-0.85f, 1.12f, 2.37f), new Vector3(-0.8f, 1.74f, 2.3f), new Vector3(0.8f, 1.74f, 2.3f), new Vector3(0.85f, 1.12f, 2.37f), glass);
                lp.Quad(new Vector3(0.8f, 1.2f, -2.37f), new Vector3(0.78f, 1.78f, -2.37f), new Vector3(-0.78f, 1.78f, -2.37f), new Vector3(-0.8f, 1.2f, -2.37f), glass);
                lp.Box(new Vector3(0f, 0.95f, 2.39f), new Vector3(1.6f, 0.06f, 0.03f), C("#f7f8f9", 0.85f));
                lp.Box(new Vector3(0f, 1.0f, -2.37f), new Vector3(1.6f, 0.06f, 0.03f), C("#e8352c", 0.55f));
                lp.Box(new Vector3(0f, 0.5f, 2.39f), new Vector3(1.4f, 0.2f, 0.04f), dark);
                lp.Box(new Vector3(0f, 0.24f, 0f), new Vector3(1.7f, 0.16f, 4.4f), dark);
                return lp;
            });
            var root = new GameObject("waymo-ojai");
            Part(root.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Wheel(0.37f, 0.26f, "#1c1d20", "#d8dadd", 10), 0.86f, 1.55f, -1.55f, 0.37f);
            return (root, new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(2.0f, 2.4f, 4.8f)));
        }
    }
}
