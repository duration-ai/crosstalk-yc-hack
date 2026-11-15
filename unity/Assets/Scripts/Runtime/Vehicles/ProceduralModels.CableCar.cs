using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // San Francisco cable car, Powell–Hyde style, 8.4 m: a maroon and cream enclosed cabin in the
        // middle, open ends with outward-facing wooden benches, running boards along both sides, brass
        // grab poles and grip lever, a cream roof with a clerestory, and small iron wheels.
        public static (GameObject, Bounds) CableCar(Material material)
        {
            var body = Cached("cable car body", () =>
            {
                var lp = new LowPoly();
                var maroon = C("#8c1c1c");
                var cream = C("#efe4c8");
                var wood = C("#8a5a32");
                var darkWood = C("#5b3a22");
                var brass = C("#d4a640");
                var glass = C("#2a2f3a");
                var iron = C("#2b2b2b");
                lp.Box(new Vector3(0f, 0.5f, 0f), new Vector3(1.9f, 0.45f, 7.8f), iron);
                lp.Box(new Vector3(0f, 0.85f, 0f), new Vector3(2.4f, 0.14f, 8.4f), wood);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 1.36f, 0.62f, 0f), new Vector3(0.32f, 0.07f, 7.6f), darkWood);
                    lp.Box(new Vector3(s * 1.14f, 1.85f, 0f), new Vector3(0.1f, 1.9f, 3.3f), maroon);
                    lp.Box(new Vector3(s * 1.2f, 2.25f, 0f), new Vector3(0.03f, 0.75f, 3.1f), cream);
                    for (int w = -1; w <= 1; w++)
                        lp.Box(new Vector3(s * 1.22f, 2.25f, w * 1.0f), new Vector3(0.03f, 0.55f, 0.8f), glass);
                    foreach (float end in new[] { 1f, -1f })
                    {
                        lp.Box(new Vector3(s * 0.72f, 1.28f, end * 2.9f), new Vector3(0.55f, 0.12f, 2.4f), wood);
                        lp.Box(new Vector3(s * 0.46f, 1.62f, end * 2.9f), new Vector3(0.08f, 0.6f, 2.4f), darkWood);
                        foreach (float z in new[] { 1.9f, 2.9f, 3.9f })
                            lp.CylinderY(new Vector3(s * 1.15f, 0.92f, end * z), 0.045f, 0.045f, 2.2f, 6, brass, brass);
                    }
                }
                foreach (float end in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(0f, 1.85f, end * 1.62f), new Vector3(2.3f, 1.9f, 0.1f), maroon);
                    lp.Box(new Vector3(0f, 1.35f, end * 4.15f), new Vector3(2.3f, 0.9f, 0.1f), maroon);
                    lp.Box(new Vector3(0f, 1.82f, end * 4.16f), new Vector3(2.3f, 0.08f, 0.1f), brass);
                    lp.Box(new Vector3(0f, 2.75f, end * 4.22f), new Vector3(0.32f, 0.32f, 0.06f), C("#fff4c8", 0.9f));
                    lp.Box(new Vector3(0f, 2.98f, end * 4.24f), new Vector3(1.4f, 0.22f, 0.04f), cream);
                }
                lp.Box(new Vector3(0f, 1.6f, 1.95f), new Vector3(0.07f, 1.3f, 0.07f), brass);
                lp.Box(new Vector3(0f, 3.15f, 0f), new Vector3(2.6f, 0.15f, 8.6f), cream);
                lp.Box(new Vector3(0f, 3.34f, 0f), new Vector3(1.4f, 0.24f, 7.4f), maroon);
                lp.Box(new Vector3(0f, 3.48f, 0f), new Vector3(1.5f, 0.05f, 7.5f), cream);
                return lp;
            });
            var root = new GameObject("cable-car");
            Part(root.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Wheel(0.36f, 0.18f, "#2b2b2b", "#6b6e72", 10), 0.82f, 2.7f, -2.7f, 0.36f);
            return (root, new Bounds(new Vector3(0f, 1.75f, 0f), new Vector3(2.8f, 3.5f, 8.6f)));
        }
    }
}
