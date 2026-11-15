using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Vehicles and parts Polyfork has no model for, built as flat-shaded vertex-coloured low poly at
    // real size, with the same node conventions as Polyfork's rigs: steer-fl/fr pivots holding
    // wheel-xx pivots holding the wheel meshes, so CarController spins and steers them. Front is +z.
    public static class ProceduralModels
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static Color C(string hex, float glow = 0f) => LowPoly.Hex(hex, glow);

        static Mesh Cached(string key, System.Func<LowPoly> build)
        {
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            mesh = build().ToMesh(key);
            cache[key] = mesh;
            return mesh;
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

        static Mesh Wheel(float radius, float width, string tyre, string hub, int sides = 10) =>
            Cached($"wheel {radius} {width} {tyre} {hub} {sides}", () =>
            {
                var lp = new LowPoly();
                lp.CylinderX(Vector3.zero, radius, width, sides, C(tyre), C(tyre));
                lp.CylinderX(Vector3.zero, radius * 0.58f, width * 1.04f, sides, C(hub), C(hub));
                return lp;
            });

        // Four wheels with the Polyfork pivot layout; front wheels steer.
        static void Wheels(Transform root, Material material, Mesh wheel, float x, float front, float rear, float y)
        {
            foreach (var (name, pos, steer) in new[]
            {
                ("fl", new Vector3(x, y, front), true), ("fr", new Vector3(-x, y, front), true),
                ("rl", new Vector3(x, y, rear), false), ("rr", new Vector3(-x, y, rear), false),
            })
            {
                var parent = root;
                if (steer) parent = Part(root, "steer-" + name, null, material, pos).transform;
                var pivot = Part(parent, "wheel-" + name, null, material, steer ? Vector3.zero : pos);
                Part(pivot.transform, "wheel-" + name + "-mesh", wheel, material, Vector3.zero);
            }
        }

        static void Side(LowPoly lp, float x, Vector2[] zy, Color color)
        {
            for (int i = 1; i < zy.Length - 1; i++)
                lp.Tri(new Vector3(x, zy[0].y, zy[0].x), new Vector3(x, zy[i].y, zy[i].x), new Vector3(x, zy[i + 1].y, zy[i + 1].x), color);
        }

        // Brushed-steel wedge pickup, 5.8 m, drawn a little toy-like (big wheels, fat flares) in the
        // spirit of Hot Wheels Unleashed's die-cast proportions: a flat-sided lower body up to the belt,
        // then one tilted greenhouse plane rising to the single apex crease, so the sides facet like
        // folded steel. Full-width light bars front and back, angular black arch flares, aero hubs.
        // A brighter rear bar (brake-glow) lights only while braking.
        public static (GameObject, Bounds) Cybertruck(Material material)
        {
            var rearBelt = new Vector2(-2.88f, 1.12f);
            var apex = new Vector2(-0.05f, 1.88f);
            var frontBelt = new Vector2(2.88f, 1.0f);
            const float apexHalf = 0.8f;
            var body = Cached("cybertruck body v2", () =>
            {
                var lp = new LowPoly();
                var steel = C("#c3c8ce");
                var steelTop = C("#dde1e5");
                var steelShade = C("#a9afb6");
                var glass = C("#1f2c5c");
                var black = C("#1c1d20");
                var seam = C("#9aa1a8");
                lp.Prism(new[]
                {
                    new Vector2(-2.8f, 0.42f), rearBelt, frontBelt, new Vector2(2.94f, 0.72f), new Vector2(2.62f, 0.36f), new Vector2(-2.55f, 0.34f),
                }, 1.0f, steel, steelTop);

                Vector3 At(Vector2 p, float x) => new Vector3(x, p.y, p.x);
                lp.Quad(At(rearBelt, -1f), At(apex, -apexHalf), At(apex, apexHalf), At(rearBelt, 1f), steelTop);
                lp.Quad(At(apex, -apexHalf), At(frontBelt, -1f), At(frontBelt, 1f), At(apex, apexHalf), steelTop);
                foreach (float s in new[] { 1f, -1f })
                    lp.Tri(At(rearBelt, s), At(apex, s * apexHalf), At(frontBelt, s), steelShade);

                // A point on the tilted greenhouse side, nudged outward so trim sits on top of it.
                Vector3 Upper(float s, float z, float y)
                {
                    var p = new Vector2(z, y);
                    var v0 = apex - rearBelt;
                    var v1 = frontBelt - rearBelt;
                    var v2 = p - rearBelt;
                    float d = v0.x * v1.y - v1.x * v0.y;
                    float b = (v2.x * v1.y - v1.x * v2.y) / d;
                    return new Vector3(s * (Mathf.Lerp(1f, apexHalf, b) + 0.012f), y, z);
                }
                foreach (float s in new[] { 1f, -1f })
                {
                    var window = new[] { new Vector2(-1.35f, 1.2f), new Vector2(-1.35f, 1.5f), new Vector2(-0.12f, 1.79f), new Vector2(1.3f, 1.4f), new Vector2(1.3f, 1.14f) };
                    for (int i = 1; i < window.Length - 1; i++)
                        lp.Tri(Upper(s, window[0].x, window[0].y), Upper(s, window[i].x, window[i].y), Upper(s, window[i + 1].x, window[i + 1].y), glass);
                    lp.Quad(Upper(s, -0.12f, 1.2f), Upper(s, -0.12f, 1.76f), Upper(s, -0.05f, 1.76f), Upper(s, -0.05f, 1.2f), steelShade);

                    float x = s * 1.006f;
                    foreach (float zc in new[] { 1.85f, -1.9f })
                        Side(lp, x, new[]
                        {
                            new Vector2(zc - 0.86f, 0.34f), new Vector2(zc - 0.72f, 0.86f), new Vector2(zc - 0.3f, 1.04f), new Vector2(zc + 0.3f, 1.04f),
                            new Vector2(zc + 0.72f, 0.86f), new Vector2(zc + 0.86f, 0.34f),
                        }, black);
                    Side(lp, x, new[] { new Vector2(-1.04f, 0.34f), new Vector2(-1.04f, 0.5f), new Vector2(0.99f, 0.5f), new Vector2(0.99f, 0.34f) }, black);
                    Side(lp, x, new[] { new Vector2(0.62f, 0.5f), new Vector2(0.62f, 1.06f), new Vector2(0.65f, 1.06f), new Vector2(0.65f, 0.5f) }, seam);
                    Side(lp, x, new[] { new Vector2(-0.98f, 0.5f), new Vector2(-0.98f, 1.1f), new Vector2(-0.95f, 1.1f), new Vector2(-0.95f, 0.5f) }, seam);
                    lp.Box(Upper(s, 1.05f, 1.3f) + new Vector3(s * 0.1f, 0f, 0f), new Vector3(0.2f, 0.11f, 0.16f), Quaternion.Euler(0f, s * 12f, 0f), black);
                }

                var hood = (frontBelt - apex);
                Vector3 OnHood(float t, float side)
                {
                    var p = apex + hood * t;
                    return new Vector3(side * Mathf.Lerp(apexHalf, 1f, t) * 0.9f, p.y + 0.01f, p.x);
                }
                lp.Quad(OnHood(0.03f, -1f), OnHood(0.03f, 1f), OnHood(0.44f, 1f), OnHood(0.44f, -1f), glass);
                var bed = (apex - rearBelt);
                Vector3 OnBed(float t, float side)
                {
                    var p = rearBelt + bed * t;
                    return new Vector3(side * Mathf.Lerp(1f, apexHalf, t) * 0.93f, p.y + 0.01f, p.x);
                }
                lp.Quad(OnBed(0.78f, -1f), OnBed(0.78f, 1f), OnBed(0.95f, 1f), OnBed(0.95f, -1f), glass);
                for (int i = 1; i <= 4; i++)
                {
                    float t = i * 0.13f;
                    lp.Quad(OnBed(t, -1f), OnBed(t, 1f), OnBed(t + 0.012f, 1f), OnBed(t + 0.012f, -1f), steelShade);
                }

                lp.Box(new Vector3(0f, 0.99f, 2.9f), new Vector3(2.0f, 0.07f, 0.05f), C("#f7f8f9", 1f));
                lp.Box(new Vector3(0f, 0.6f, 2.9f), new Vector3(1.7f, 0.08f, 0.06f), black);
                lp.Box(new Vector3(0f, 0.42f, 2.72f), new Vector3(1.86f, 0.14f, 0.3f), black);
                lp.Box(new Vector3(0f, 1.08f, -2.9f), new Vector3(2.0f, 0.06f, 0.05f), C("#a3241c", 0.5f));
                lp.Box(new Vector3(0f, 0.5f, -2.82f), new Vector3(1.9f, 0.2f, 0.1f), black);
                lp.Box(new Vector3(0f, 0.3f, 0f), new Vector3(1.7f, 0.2f, 5.0f), black);
                return lp;
            });
            var brake = Cached("cybertruck brake", () =>
            {
                var lp = new LowPoly();
                lp.Box(new Vector3(0f, 1.08f, -2.925f), new Vector3(2.04f, 0.09f, 0.03f), C("#ff2a1f", 1f));
                return lp;
            });
            var root = new GameObject("cybertruck");
            Part(root.transform, "body", body, material, Vector3.zero);
            Part(root.transform, "brake-glow", brake, material, Vector3.zero).SetActive(false);
            Wheels(root.transform, material, AeroWheel(0.5f, 0.38f), 0.84f, 1.85f, -1.9f, 0.5f);
            return (root, new Bounds(new Vector3(0f, 0.94f, 0f), new Vector3(2.1f, 1.88f, 5.84f)));
        }

        // Chunky tyre with a flat six-spoke aero cover and a dark centre cap.
        static Mesh AeroWheel(float radius, float width) =>
            Cached($"aero wheel {radius} {width}", () =>
            {
                var lp = new LowPoly();
                lp.CylinderX(Vector3.zero, radius, width, 12, C("#1c1d20"), C("#26282c"));
                lp.CylinderX(Vector3.zero, radius * 0.66f, width * 1.06f, 6, C("#9aa3ab"), C("#b5bcc3"));
                lp.CylinderX(Vector3.zero, radius * 0.2f, width * 1.1f, 6, C("#2b2e33"), C("#2b2e33"));
                for (int i = 0; i < 3; i++)
                    lp.Box(Vector3.zero, new Vector3(width * 1.08f, radius * 1.2f, radius * 0.07f), Quaternion.Euler(i * 60f, 0f, 0f), C("#5c646b"));
                return lp;
            });

        // Symmetric bidirectional robotaxi pod: no hood, the same face at both ends, glass all round
        // the upper half, sensor pods on the four roof corners, light strips at both ends.
        public static (GameObject, Bounds) Zoox(Material material)
        {
            var body = Cached("zoox body", () =>
            {
                var lp = new LowPoly();
                var white = C("#f4f4f0");
                var roof = C("#e6e8ea");
                var glass = C("#23356e");
                var black = C("#1c1d20");
                var teal = C("#3ec7c1");
                var profile = new[]
                {
                    new Vector2(-1.8f, 0.3f), new Vector2(-1.81f, 1.0f), new Vector2(-1.58f, 1.78f), new Vector2(-1.25f, 1.94f),
                    new Vector2(1.25f, 1.94f), new Vector2(1.58f, 1.78f), new Vector2(1.81f, 1.0f), new Vector2(1.8f, 0.3f),
                };
                lp.Prism(profile, 0.97f, white, roof);
                foreach (float s in new[] { 1f, -1f })
                {
                    Side(lp, s * 0.975f, new[] { new Vector2(-1.72f, 1.05f), new Vector2(-1.52f, 1.72f), new Vector2(1.52f, 1.72f), new Vector2(1.72f, 1.05f) }, glass);
                    Side(lp, s * 0.976f, new[] { new Vector2(-0.02f, 1.05f), new Vector2(-0.02f, 1.72f), new Vector2(0.02f, 1.72f), new Vector2(0.02f, 1.05f) }, white);
                    Side(lp, s * 0.975f, new[] { new Vector2(-1.8f, 0.95f), new Vector2(-1.8f, 1.0f), new Vector2(1.8f, 1.0f), new Vector2(1.8f, 0.95f) }, teal);
                    var end = new Vector3(0f, 0f, s * 1.815f);
                    lp.Quad(end + new Vector3(-0.85f, 1.05f, 0f), end + new Vector3(-0.72f, 1.68f, -s * 0.2f), end + new Vector3(0.72f, 1.68f, -s * 0.2f), end + new Vector3(0.85f, 1.05f, 0f), glass);
                    lp.Box(new Vector3(0f, 0.86f, s * 1.81f), new Vector3(1.5f, 0.06f, 0.03f), C("#f7f8f9", 0.85f));
                    lp.Box(new Vector3(0.72f, 0.5f, s * 1.81f), new Vector3(0.22f, 0.08f, 0.03f), C("#e8352c", 0.5f));
                    lp.Box(new Vector3(-0.72f, 0.5f, s * 1.81f), new Vector3(0.22f, 0.08f, 0.03f), C("#e8352c", 0.5f));
                    foreach (float t in new[] { 1f, -1f })
                    {
                        lp.CylinderY(new Vector3(t * 0.78f, 1.86f, s * 1.38f), 0.13f, 0.11f, 0.2f, 8, black, C("#3a3f46"));
                        lp.Box(new Vector3(t * 0.9f, 0.62f, s * 1.35f), new Vector3(0.18f, 0.5f, 0.62f), black);
                    }
                }
                lp.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.6f, 0.16f, 3.4f), black);
                return lp;
            });
            var root = new GameObject("zoox");
            Part(root.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Wheel(0.36f, 0.26f, "#1c1d20", "#d8dadd", 10), 0.82f, 1.32f, -1.32f, 0.36f);
            return (root, new Bounds(new Vector3(0f, 0.97f, 0f), new Vector3(1.96f, 1.94f, 3.63f)));
        }

        // Flying car after the Alef Model A / Xpeng X2 concepts: a sleek 4.6 m wedge with a dark glass
        // canopy, a cyan light line along each flank, and four ducted fans on short arms with glowing
        // cyan downwash discs underneath. No wheels; it hovers (HandlingProfile.HoverHeight).
        public static (GameObject, Bounds) FlyingCar(Material material)
        {
            var body = Cached("flying car body", () =>
            {
                var lp = new LowPoly();
                var white = C("#eef1f4");
                var roof = C("#d9e0e8");
                var glass = C("#1f2a44");
                var dark = C("#2b2f36");
                var cyan = C("#5ff2ff", 1f);
                var profile = new[]
                {
                    new Vector2(-2.3f, 0.45f), new Vector2(-2.3f, 0.95f), new Vector2(-1.6f, 1.15f), new Vector2(-0.8f, 1.4f),
                    new Vector2(0.4f, 1.4f), new Vector2(1.3f, 1.0f), new Vector2(2.3f, 0.72f), new Vector2(2.3f, 0.45f),
                };
                lp.Prism(profile, 0.85f, white, roof);
                lp.Quad(new Vector3(-0.8f, 1.0f, 1.2f), new Vector3(-0.72f, 1.37f, 0.42f), new Vector3(0.72f, 1.37f, 0.42f), new Vector3(0.8f, 1.0f, 1.2f), glass);
                lp.Quad(new Vector3(0.78f, 1.13f, -1.55f), new Vector3(0.7f, 1.37f, -0.85f), new Vector3(-0.7f, 1.37f, -0.85f), new Vector3(-0.78f, 1.13f, -1.55f), glass);
                foreach (float s in new[] { 1f, -1f })
                {
                    Side(lp, s * 0.855f, new[] { new Vector2(-1.45f, 1.08f), new Vector2(-0.78f, 1.35f), new Vector2(0.38f, 1.35f), new Vector2(1.15f, 1.0f) }, glass);
                    Side(lp, s * 0.856f, new[] { new Vector2(-2.25f, 0.62f), new Vector2(-2.25f, 0.68f), new Vector2(2.25f, 0.68f), new Vector2(2.25f, 0.62f) }, cyan);
                    foreach (float f in new[] { 1.55f, -1.55f })
                    {
                        lp.Box(new Vector3(s * 0.98f, 0.5f, f), new Vector3(0.35f, 0.08f, 0.24f), dark);
                        lp.CylinderY(new Vector3(s * 1.2f, 0.3f, f), 0.46f, 0.5f, 0.32f, 12, dark, dark);
                        lp.CylinderY(new Vector3(s * 1.2f, 0.25f, f), 0.38f, 0.38f, 0.05f, 12, cyan, cyan);
                    }
                }
                lp.Box(new Vector3(0f, 0.8f, 2.31f), new Vector3(1.4f, 0.06f, 0.03f), C("#f7f8f9", 0.85f));
                lp.Box(new Vector3(0f, 0.85f, -2.31f), new Vector3(1.4f, 0.06f, 0.03f), C("#e8352c", 0.6f));
                lp.Box(new Vector3(0f, 0.42f, 0f), new Vector3(1.5f, 0.08f, 4.2f), cyan);
                return lp;
            });
            var root = new GameObject("flying-car");
            Part(root.transform, "body", body, material, Vector3.zero);
            return (root, new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(3.2f, 1.2f, 4.62f)));
        }

        // Supermarket shopping cart: tapered wire basket, red handle, child seat flap, four casters.
        public static (GameObject, Bounds) ShoppingCart(Material material)
        {
            var body = Cached("cart body", () =>
            {
                var lp = new LowPoly();
                var chrome = C("#c9ced4");
                var red = C("#e2603f");
                float t = 0.018f;
                Vector3 TopBack(float x) => new Vector3(x, 0.98f, -0.36f);
                Vector3 TopFront(float x) => new Vector3(x * 0.92f, 0.96f, 0.44f);
                Vector3 BotBack(float x) => new Vector3(x * 0.9f, 0.5f, -0.24f);
                Vector3 BotFront(float x) => new Vector3(x * 0.84f, 0.5f, 0.36f);
                float w = 0.27f;
                lp.Bar(TopBack(-w), TopBack(w), t, chrome);
                lp.Bar(TopFront(-w), TopFront(w), t, chrome);
                lp.Bar(TopBack(-w), TopFront(-w), t, chrome);
                lp.Bar(TopBack(w), TopFront(w), t, chrome);
                lp.Bar(BotBack(-w), BotFront(-w), t, chrome);
                lp.Bar(BotBack(w), BotFront(w), t, chrome);
                lp.Bar(BotFront(-w), BotFront(w), t, chrome);
                lp.Bar(BotBack(-w), BotBack(w), t, chrome);
                for (int i = 0; i <= 6; i++)
                {
                    float f = i / 6f;
                    lp.Bar(Vector3.Lerp(TopBack(-w), TopFront(-w), f), Vector3.Lerp(BotBack(-w), BotFront(-w), f), t * 0.7f, chrome);
                    lp.Bar(Vector3.Lerp(TopBack(w), TopFront(w), f), Vector3.Lerp(BotBack(w), BotFront(w), f), t * 0.7f, chrome);
                    lp.Bar(Vector3.Lerp(BotBack(-w), BotFront(-w), f), Vector3.Lerp(BotBack(w), BotFront(w), f), t * 0.7f, chrome);
                }
                for (int i = 0; i <= 4; i++)
                {
                    float f = i / 4f;
                    lp.Bar(Vector3.Lerp(TopFront(-w), TopFront(w), f), Vector3.Lerp(BotFront(-w), BotFront(w), f), t * 0.7f, chrome);
                    lp.Bar(Vector3.Lerp(TopBack(-w), TopBack(w), f), Vector3.Lerp(BotBack(-w), BotBack(w), f), t * 0.7f, chrome);
                }
                lp.Bar(new Vector3(-0.26f, 1.02f, -0.46f), new Vector3(0.26f, 1.02f, -0.46f), 0.04f, red);
                lp.Bar(new Vector3(-0.26f, 1.02f, -0.46f), TopBack(-w), t, chrome);
                lp.Bar(new Vector3(0.26f, 1.02f, -0.46f), TopBack(w), t, chrome);
                lp.Box(new Vector3(0f, 0.86f, -0.3f), new Vector3(0.44f, 0.2f, 0.02f), Quaternion.Euler(-20f, 0f, 0f), red);
                lp.Bar(new Vector3(-0.24f, 0.08f, -0.36f), new Vector3(-0.22f, 0.08f, 0.42f), t * 1.4f, chrome);
                lp.Bar(new Vector3(0.24f, 0.08f, -0.36f), new Vector3(0.22f, 0.08f, 0.42f), t * 1.4f, chrome);
                lp.Bar(new Vector3(-0.24f, 0.08f, -0.36f), BotBack(-w), t, chrome);
                lp.Bar(new Vector3(0.24f, 0.08f, -0.36f), BotBack(w), t, chrome);
                lp.Bar(new Vector3(-0.22f, 0.08f, 0.42f), BotFront(-w), t, chrome);
                lp.Bar(new Vector3(0.22f, 0.08f, 0.42f), BotFront(w), t, chrome);
                lp.Box(new Vector3(0f, 0.2f, 0.03f), new Vector3(0.44f, 0.012f, 0.66f), chrome);
                return lp;
            });
            var root = new GameObject("shopping-cart");
            Part(root.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Wheel(0.055f, 0.035f, "#1c1d20", "#5c646b", 8), 0.22f, 0.4f, -0.34f, 0.055f);
            return (root, new Bounds(new Vector3(0f, 0.52f, -0.01f), new Vector3(0.58f, 1.04f, 0.98f)));
        }

        // Roof sensor hat after Waymo's dome, a touch oversized so it reads from the chase camera: a
        // black base plate, a white stack with a dark camera band and a teal rider-display ring, and on
        // top the lidar can, which spins with a glowing emitter window (Burnout Paradise-style "this
        // thing is alive" motion on an idle car). Four bumper sensor pods and two fender camera ears.
        public static void RobotaxiSensors(Transform root, Material material, float roofY, float roofZ, float halfWidth, float halfLength, float beltY)
        {
            var hat = Cached("sensor hat v2", () =>
            {
                var lp = new LowPoly();
                var white = C("#f7f7f3");
                var black = C("#1c1d20");
                lp.Box(new Vector3(0f, -0.1f, 0f), new Vector3(0.56f, 0.26f, 0.74f), black);
                lp.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.7f, 0.06f, 0.92f), black);
                lp.CylinderY(new Vector3(0f, 0.06f, 0f), 0.42f, 0.4f, 0.12f, 12, white, white);
                lp.CylinderY(new Vector3(0f, 0.18f, 0f), 0.405f, 0.405f, 0.05f, 12, C("#3ec7c1", 0.9f), C("#3ec7c1", 0.9f));
                lp.CylinderY(new Vector3(0f, 0.23f, 0f), 0.4f, 0.38f, 0.1f, 12, black, black);
                lp.CylinderY(new Vector3(0f, 0.33f, 0f), 0.38f, 0.27f, 0.1f, 12, white, white);
                lp.CylinderY(new Vector3(0f, 0.43f, 0f), 0.2f, 0.2f, 0.05f, 10, black, black);
                return lp;
            });
            var puck = Cached("sensor puck v2", () =>
            {
                var lp = new LowPoly();
                lp.CylinderY(Vector3.zero, 0.2f, 0.19f, 0.2f, 10, C("#2b2e33"), C("#f7f7f3"));
                lp.Box(new Vector3(0f, 0.1f, 0.19f), new Vector3(0.16f, 0.09f, 0.03f), C("#5ff2ff", 1f));
                lp.Box(new Vector3(0f, 0.1f, -0.19f), new Vector3(0.1f, 0.06f, 0.03f), C("#3ec7c1", 0.6f));
                return lp;
            });
            var pods = Cached($"sensor pods {halfWidth:F2} {halfLength:F2} {beltY:F2}", () =>
            {
                var lp = new LowPoly();
                var white = C("#f7f7f3");
                var black = C("#1c1d20");
                foreach (float x in new[] { 1f, -1f })
                {
                    foreach (float z in new[] { 1f, -1f })
                    {
                        var p = new Vector3(x * (halfWidth - 0.05f), beltY, z * (halfLength - 0.35f));
                        lp.CylinderY(p, 0.09f, 0.08f, 0.16f, 8, white, black);
                        lp.CylinderY(p + new Vector3(0f, 0.04f, 0f), 0.092f, 0.092f, 0.07f, 8, black, black);
                    }
                    var ear = new Vector3(x * (halfWidth + 0.02f), beltY + 0.2f, halfLength * 0.48f);
                    lp.Box(ear, new Vector3(0.16f, 0.16f, 0.26f), white);
                    lp.Box(ear + new Vector3(0f, 0.01f, 0.131f), new Vector3(0.1f, 0.08f, 0.01f), black);
                }
                return lp;
            });
            var hatGo = Part(root, "sensor-hat", hat, material, new Vector3(0f, roofY, roofZ));
            Part(hatGo.transform, "lidar", puck, material, new Vector3(0f, 0.48f, 0f)).AddComponent<Spinner>();
            Part(root, "sensor-pods", pods, material, Vector3.zero);
        }

        // Black battery box on the down tube and a small rear rack, turning the city bike into an e-bike.
        public static void EbikeKit(Transform root, Material material)
        {
            var kit = Cached("ebike kit", () =>
            {
                var lp = new LowPoly();
                lp.Box(new Vector3(0f, 0.52f, 0.12f), new Vector3(0.09f, 0.1f, 0.36f), Quaternion.Euler(-38f, 0f, 0f), C("#1c1d20"));
                lp.Box(new Vector3(0f, 0.86f, -0.52f), new Vector3(0.18f, 0.02f, 0.32f), C("#1c1d20"));
                lp.Box(new Vector3(0f, 0.5f, -0.1f), new Vector3(0.12f, 0.14f, 0.14f), C("#2b2e33"));
                return lp;
            });
            Part(root, "ebike-kit", kit, material, Vector3.zero);
        }

        // Bike-share dock: a long base rail with a docking post per slot and a kiosk tower at one end.
        // Built along +x, origin at the kiosk end.
        public static GameObject BikeDock(Material material, int slots)
        {
            var mesh = Cached("dock " + slots, () =>
            {
                var lp = new LowPoly();
                var grey = C("#3a3f46");
                var pink = C("#ff4fa3");
                float length = slots * 0.85f + 1.2f;
                lp.Box(new Vector3(length * 0.5f, 0.04f, 0f), new Vector3(length, 0.08f, 0.5f), C("#5c646b"));
                for (int i = 0; i < slots; i++)
                {
                    float x = 1.4f + i * 0.85f;
                    lp.Box(new Vector3(x, 0.42f, -0.05f), new Vector3(0.14f, 0.76f, 0.16f), grey);
                    lp.Box(new Vector3(x, 0.74f, -0.05f), new Vector3(0.15f, 0.1f, 0.17f), pink);
                }
                lp.Box(new Vector3(0.45f, 1.0f, 0f), new Vector3(0.5f, 2.0f, 0.36f), grey);
                lp.Box(new Vector3(0.45f, 1.55f, 0.19f), new Vector3(0.38f, 0.5f, 0.02f), C("#9ed7ff", 0.5f));
                lp.Box(new Vector3(0.45f, 2.02f, 0f), new Vector3(0.56f, 0.12f, 0.42f), pink);
                return lp;
            });
            var go = new GameObject("bike dock");
            Part(go.transform, "dock", mesh, material, Vector3.zero);
            return go;
        }
    }

    // Spins a part about its local up axis (lidar pucks).
    public sealed class Spinner : MonoBehaviour
    {
        public float DegreesPerSecond = 540f;

        void Update() => transform.localRotation *= Quaternion.Euler(0f, DegreesPerSecond * Time.deltaTime, 0f);
    }
}
