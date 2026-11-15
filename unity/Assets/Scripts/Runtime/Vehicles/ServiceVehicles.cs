using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Service and novelty rides built in code, flat-shaded vertex colour like ProceduralModels, drawn
    // chunky and toy-like so they read from the chase cam: the amphibious duck tour boat and the
    // ladder fire truck. Front is +z, wheel pivots follow the Polyfork rig names (steer-*, wheel-*).
    public static class ServiceVehicles
    {
        static readonly System.Collections.Generic.Dictionary<string, Mesh> cache = new System.Collections.Generic.Dictionary<string, Mesh>();

        static Color C(string hex, float glow = 0f) => LowPoly.Hex(hex, glow);

        static Mesh Cached(string key, System.Func<LowPoly> build)
        {
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            return cache[key] = build().ToMesh(key);
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            if (mesh == null) return go;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Mesh Wheel(float radius, float width, string tyre, string hub) =>
            Cached($"service wheel {radius} {width} {tyre} {hub}", () =>
            {
                var lp = new LowPoly();
                lp.CylinderX(Vector3.zero, radius, width, 10, C(tyre), C(tyre));
                lp.CylinderX(Vector3.zero, radius * 0.55f, width * 1.06f, 10, C(hub), C(hub));
                return lp;
            });

        // One axle: two wheels at +-x, steering pivots when steer is set.
        static void Axle(Transform root, Material material, Mesh wheel, string tag, float x, float y, float z, bool steer)
        {
            foreach (var (side, sx) in new[] { ("l", 1f), ("r", -1f) })
            {
                var name = tag + side;
                var pos = new Vector3(sx * x, y, z);
                var parent = steer ? Part(root, "steer-" + name, null, material, pos).transform : root;
                var pivot = Part(parent, "wheel-" + name, null, material, steer ? Vector3.zero : pos);
                Part(pivot.transform, "wheel-" + name + "-mesh", wheel, material, Vector3.zero);
            }
        }

        // DUKW-style duck tour boat, 9.8 m: a sunny yellow boat hull with a raked, pointed bow on six
        // fat wheels, a blue boot stripe, a striped canopy on posts over rows of blue benches, a
        // windshield, life rings, a pennant, and an orange duck bill with eyes on the bow so nobody
        // mistakes it for a bus. Floats on HandlingProfile.Draft (DuckBoat sinks the model into the bay).
        public static (GameObject, Bounds) DuckBoat(Material material)
        {
            var body = Cached("duck boat body", () =>
            {
                var lp = new LowPoly();
                var yellow = C("#ffc72c");
                var yellowDeep = C("#f0a81c");
                var blue = C("#2457b8");
                var navy = C("#1b2f5e");
                var white = C("#f6f4ee");
                var red = C("#e2402f");
                var orange = C("#ff8a1e");
                var black = C("#1c1d22");
                var seat = C("#3c7fd8");
                var glass = C("#9fd3ea");

                // Hull rings: keel (y 0.6) and deck (y 1.9), each a pointed outline in plan.
                Vector3[] Ring(float y, float half, float stern, float shoulder, float bow) => new[]
                {
                    new Vector3(-half, y, stern), new Vector3(-half, y, shoulder), new Vector3(0f, y, bow),
                    new Vector3(half, y, shoulder), new Vector3(half, y, stern),
                };
                var keel = Ring(0.6f, 1.05f, -4.5f, 2.2f, 4.1f);
                var chine = Ring(1.15f, 1.3f, -4.8f, 2.5f, 4.75f);
                var deck = Ring(1.95f, 1.32f, -4.85f, 2.6f, 4.95f);
                for (int i = 0; i < 4; i++)
                {
                    lp.Quad(keel[i], keel[i + 1], chine[i + 1], chine[i], blue);
                    lp.Quad(chine[i], chine[i + 1], deck[i + 1], deck[i], i == 1 || i == 2 ? yellowDeep : yellow);
                }
                lp.Quad(keel[4], keel[0], chine[0], chine[4], navy);
                lp.Quad(chine[4], chine[0], deck[0], deck[4], yellowDeep);
                lp.Quad(keel[0], keel[4], keel[3], keel[1], navy);
                lp.Tri(keel[1], keel[3], keel[2], navy);
                lp.Quad(deck[0], deck[1], deck[3], deck[4], C("#c98f3a"));
                lp.Tri(deck[1], deck[2], deck[3], C("#c98f3a"));
                foreach (float s in new[] { 1f, -1f })
                    lp.Box(new Vector3(s * 1.31f, 1.2f, -1.1f), new Vector3(0.04f, 0.14f, 7.2f), red);

                // Duck bill, eyes and a gunwale rail.
                lp.Box(new Vector3(0f, 1.75f, 5.25f), new Vector3(0.9f, 0.18f, 0.8f), orange);
                lp.Box(new Vector3(0f, 1.6f, 5.2f), new Vector3(0.8f, 0.12f, 0.6f), C("#e06b10"));
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 0.74f, 1.62f, 3.55f), new Vector3(0.05f, 0.42f, 0.42f), Quaternion.Euler(0f, s * -32f, 0f), white);
                    lp.Box(new Vector3(s * 0.77f, 1.62f, 3.6f), new Vector3(0.05f, 0.2f, 0.2f), Quaternion.Euler(0f, s * -32f, 0f), black);
                    lp.Box(new Vector3(s * 1.3f, 2.1f, -1.0f), new Vector3(0.08f, 0.08f, 7.6f), white);
                }

                // Benches, windshield, driver's console.
                for (int row = 0; row < 6; row++)
                {
                    float z = 1.3f - row * 0.95f;
                    lp.Box(new Vector3(0f, 2.15f, z), new Vector3(2.3f, 0.2f, 0.45f), seat);
                    lp.Box(new Vector3(0f, 2.5f, z - 0.2f), new Vector3(2.3f, 0.55f, 0.1f), seat);
                }
                lp.Box(new Vector3(0f, 2.15f, 2.4f), new Vector3(2.4f, 0.4f, 0.5f), navy);
                lp.Box(new Vector3(0f, 2.75f, 2.7f), new Vector3(2.4f, 0.8f, 0.06f), Quaternion.Euler(-12f, 0f, 0f), glass);

                // Canopy on six posts, red and white striped edge, pennant mast.
                foreach (float s in new[] { 1f, -1f })
                    foreach (float z in new[] { 2.55f, -1.0f, -4.5f })
                        lp.Box(new Vector3(s * 1.15f, 2.6f, z), new Vector3(0.1f, 1.3f, 0.1f), white);
                lp.Box(new Vector3(0f, 3.25f, -1.0f), new Vector3(2.5f, 0.1f, 7.4f), white);
                for (int i = 0; i < 6; i++)
                    lp.Box(new Vector3(-1.04f + i * 0.416f, 3.34f, -1.0f), new Vector3(0.416f, 0.1f, 7.3f), i % 2 == 0 ? red : white);
                foreach (float s in new[] { 1f, -1f })
                    for (int i = 0; i < 8; i++)
                        lp.Box(new Vector3(s * 1.26f, 3.12f, 2.6f - i * 0.95f - 0.47f), new Vector3(0.05f, 0.2f, 0.95f), i % 2 == 0 ? yellow : white);
                lp.Box(new Vector3(0f, 3.9f, -4.2f), new Vector3(0.06f, 1.2f, 0.06f), white);
                lp.Tri(new Vector3(0f, 4.45f, -4.2f), new Vector3(0f, 4.05f, -4.2f), new Vector3(0f, 4.25f, -4.95f), red);

                // A big rubber-duck figurehead on the bow: neck, head, bill and eyes.
                lp.Box(new Vector3(0f, 2.35f, 4.1f), new Vector3(0.6f, 0.8f, 0.6f), yellow);
                lp.Box(new Vector3(0f, 3.0f, 4.15f), new Vector3(0.95f, 0.85f, 0.95f), yellow);
                lp.Box(new Vector3(0f, 2.9f, 4.8f), new Vector3(0.6f, 0.22f, 0.55f), orange);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 0.48f, 3.15f, 4.35f), new Vector3(0.04f, 0.26f, 0.26f), white);
                    lp.Box(new Vector3(s * 0.5f, 3.15f, 4.4f), new Vector3(0.04f, 0.13f, 0.13f), black);
                }

                // Life rings on the flanks.
                foreach (float s in new[] { 1f, -1f })
                    foreach (float z in new[] { 0.6f, -2.9f })
                    {
                        lp.CylinderX(new Vector3(s * 1.36f, 1.55f, z), 0.32f, 0.08f, 10, white, red);
                        lp.CylinderX(new Vector3(s * 1.38f, 1.55f, z), 0.14f, 0.08f, 8, yellowDeep, yellowDeep);
                    }
                lp.Box(new Vector3(0f, 1.45f, -4.86f), new Vector3(1.6f, 0.12f, 0.04f), C("#e8352c", 0.6f));
                lp.Box(new Vector3(0f, 1.6f, 4.6f), new Vector3(1.0f, 0.1f, 0.04f), C("#f7f8f9", 0.85f));
                return lp;
            });
            var root = new GameObject("duck-boat");
            Part(root.transform, "body", body, material, Vector3.zero);
            var wheel = Wheel(0.72f, 0.5f, "#202126", "#e2402f");
            Axle(root.transform, material, wheel, "f", 1.2f, 0.72f, 2.9f, true);
            Axle(root.transform, material, wheel, "m", 1.2f, 0.72f, -1.7f, false);
            Axle(root.transform, material, wheel, "r", 1.2f, 0.72f, -3.35f, false);
            return (root, new Bounds(new Vector3(0f, 1.7f, 0.1f), new Vector3(2.9f, 3.4f, 10.0f)));
        }

        // Ladder fire truck, 10.4 m: a tall boxy red cab with a white roof, a chrome grille and bumper,
        // a light bar (siren-a and siren-b, flashed by FireTruck), red body with chrome roll-up lockers
        // and a white stripe, a silver aerial ladder along the top on a rear turntable, and a brass
        // water cannon (the "cannon" pivot) on the cab roof that FireTruck aims.
        public static (GameObject, Bounds) FireTruck(Material material)
        {
            var body = Cached("fire truck body", () =>
            {
                var lp = new LowPoly();
                var red = C("#d8231f");
                var redDeep = C("#a9151a");
                var white = C("#f6f4ee");
                var chrome = C("#c9ced4");
                var steel = C("#8e959e");
                var black = C("#1c1d22");
                var glass = C("#24344f");

                lp.Box(new Vector3(0f, 0.75f, 0f), new Vector3(2.3f, 0.5f, 10f), black);
                // Cab: slab sides, slight raked windshield, white roof cap.
                lp.Box(new Vector3(0f, 1.9f, 3.85f), new Vector3(2.5f, 2.3f, 2.3f), red);
                lp.Box(new Vector3(0f, 3.1f, 3.8f), new Vector3(2.52f, 0.14f, 2.2f), white);
                lp.Box(new Vector3(0f, 2.45f, 5.0f), new Vector3(2.3f, 1.0f, 0.08f), Quaternion.Euler(-8f, 0f, 0f), glass);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 1.26f, 2.45f, 4.05f), new Vector3(0.04f, 0.85f, 1.3f), glass);
                    lp.Box(new Vector3(s * 1.36f, 2.4f, 4.95f), new Vector3(0.12f, 0.45f, 0.08f), chrome);
                }
                lp.Box(new Vector3(0f, 1.25f, 5.02f), new Vector3(1.6f, 0.9f, 0.06f), chrome);
                for (int i = 0; i < 5; i++) lp.Box(new Vector3(0f, 0.95f + i * 0.15f, 5.06f), new Vector3(1.5f, 0.04f, 0.04f), steel);
                lp.Box(new Vector3(0f, 0.65f, 5.15f), new Vector3(2.6f, 0.3f, 0.3f), chrome);
                foreach (float s in new[] { 1f, -1f })
                    lp.Box(new Vector3(s * 1.0f, 1.55f, 5.05f), new Vector3(0.35f, 0.25f, 0.06f), C("#fff6d8", 0.9f));

                // Rear body with lockers and a stripe.
                lp.Box(new Vector3(0f, 1.75f, -1.25f), new Vector3(2.5f, 2.0f, 7.5f), red);
                lp.Box(new Vector3(0f, 2.8f, -1.25f), new Vector3(2.4f, 0.1f, 7.5f), redDeep);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 1.26f, 1.15f, 0f), new Vector3(0.04f, 0.16f, 10f), white);
                    foreach (float z in new[] { 1.4f, 0.0f, -2.6f, -4.0f })
                    {
                        lp.Box(new Vector3(s * 1.265f, 1.95f, z), new Vector3(0.03f, 1.1f, 1.15f), chrome);
                        for (int k = 0; k < 4; k++) lp.Box(new Vector3(s * 1.28f, 1.55f + k * 0.26f, z), new Vector3(0.02f, 0.03f, 1.15f), steel);
                    }
                }
                lp.Box(new Vector3(0f, 1.6f, -5.02f), new Vector3(2.3f, 1.2f, 0.06f), chrome);
                foreach (float s in new[] { 1f, -1f })
                    lp.Box(new Vector3(s * 1.0f, 1.0f, -5.06f), new Vector3(0.35f, 0.25f, 0.04f), C("#e8352c", 0.6f));

                // Turntable and the aerial ladder lying along the top, tip resting over the cab.
                lp.CylinderY(new Vector3(0f, 2.85f, -3.6f), 0.95f, 0.9f, 0.25f, 12, steel, chrome);
                lp.Box(new Vector3(0f, 3.2f, -3.6f), new Vector3(1.0f, 0.3f, 0.9f), redDeep);
                var tilt = Quaternion.Euler(-4f, 0f, 0f);
                foreach (float s in new[] { 1f, -1f })
                    lp.Box(new Vector3(s * 0.6f, 3.75f, 0.3f), new Vector3(0.14f, 0.34f, 9.4f), tilt, C("#e9edf1"));
                for (int k = 0; k < 16; k++)
                {
                    float z = -4.0f + k * 0.58f;
                    lp.Box(new Vector3(0f, 3.75f + (z - 0.3f) * 0.07f, z), new Vector3(1.2f, 0.08f, 0.09f), chrome);
                }
                return lp;
            });
            var lightBase = Cached("fire truck light bar", () =>
            {
                var lp = new LowPoly();
                lp.Box(new Vector3(0f, 3.25f, 4.55f), new Vector3(2.1f, 0.16f, 0.4f), C("#1c1d22"));
                return lp;
            });
            Mesh Lamp(string key, string hex, float x) => Cached(key, () =>
            {
                var lp = new LowPoly();
                lp.Box(new Vector3(x, 3.42f, 4.55f), new Vector3(0.95f, 0.22f, 0.36f), C(hex, 1f));
                return lp;
            });
            var cannon = Cached("fire truck cannon", () =>
            {
                var lp = new LowPoly();
                var brass = C("#d9a33c");
                lp.CylinderY(new Vector3(0f, -0.1f, 0f), 0.28f, 0.22f, 0.3f, 8, C("#8e959e"), brass);
                lp.Box(new Vector3(0f, 0.3f, 0.35f), new Vector3(0.26f, 0.26f, 1.0f), Quaternion.Euler(-12f, 0f, 0f), brass);
                lp.Box(new Vector3(0f, 0.42f, 0.9f), new Vector3(0.34f, 0.34f, 0.2f), Quaternion.Euler(-12f, 0f, 0f), C("#b07c22"));
                return lp;
            });
            var root = new GameObject("fire-truck");
            Part(root.transform, "body", body, material, Vector3.zero);
            Part(root.transform, "light-bar", lightBase, material, Vector3.zero);
            Part(root.transform, "siren-a", Lamp("fire truck lamp a", "#ff2a22", -0.55f), material, Vector3.zero);
            Part(root.transform, "siren-b", Lamp("fire truck lamp b", "#fff1e8", 0.55f), material, Vector3.zero);
            Part(root.transform, "cannon", cannon, material, new Vector3(0f, 3.35f, 3.0f));
            var wheel = Wheel(0.62f, 0.45f, "#1c1d22", "#c9ced4");
            Axle(root.transform, material, wheel, "f", 1.08f, 0.62f, 3.6f, true);
            Axle(root.transform, material, wheel, "m", 1.08f, 0.62f, -2.3f, false);
            Axle(root.transform, material, wheel, "r", 1.08f, 0.62f, -3.65f, false);
            return (root, new Bounds(new Vector3(0f, 1.85f, 0.05f), new Vector3(2.7f, 3.7f, 10.4f)));
        }
    }
}
