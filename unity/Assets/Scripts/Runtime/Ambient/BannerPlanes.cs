using Dogpatch.Vehicles;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Ambient
{
    // Small banner planes circling slowly over the bay and the skyline towing parody AI-startup ads,
    // like the ones that buzz San Francisco. Pure decoration: no colliders, two draw calls per plane.
    public sealed class BannerPlanes : MonoBehaviour
    {
        const float Speed = 26f;
        const float TowLength = 22f;
        const float BannerLength = 42f;
        const float BannerHeight = 6.5f;

        struct Flight
        {
            public Transform Plane;
            public Transform Banner;
            public Vector3 Centre;
            public float Radius;
            public float Phase;
            public float Direction;
        }

        Flight[] flights;

        static readonly (string ad, string panel, string ink)[] Banners =
        {
            ("AGI BY Q3 - GENERALLY", "#FFFFFF", "#111317"),
            ("STOP HIRING HUMANS - LOOMWORK", "#FFE27A", "#111317"),
            ("YOUR COFOUNDER IS A GPU - TENSORBRO", "#FFFFFF", "#C8102E"),
        };

        public static BannerPlanes Create(WorldContext world, Material material)
        {
            var go = new GameObject("BannerPlanes");
            var planes = go.AddComponent<BannerPlanes>();
            var b = world.Bounds;
            var routes = new[]
            {
                (centre: new Vector3(b.xMax - 160f, 125f, b.center.y + 120f), radius: 240f, direction: 1f),
                (centre: new Vector3(b.center.x + 80f, 150f, b.yMax - 140f), radius: 280f, direction: -1f),
                (centre: new Vector3(b.xMax - 260f, 110f, b.yMin + 220f), radius: 200f, direction: 1f),
            };
            var planeMesh = PlaneMesh();
            planes.flights = new Flight[routes.Length];
            for (int i = 0; i < routes.Length; i++)
            {
                var (ad, panel, ink) = Banners[i % Banners.Length];
                planes.flights[i] = new Flight
                {
                    Plane = Part(go.transform, "banner-plane", planeMesh, material),
                    Banner = Part(go.transform, "banner", BannerMesh(ad, panel, ink), material),
                    Centre = routes[i].centre,
                    Radius = routes[i].radius,
                    Phase = i * 2.1f,
                    Direction = routes[i].direction,
                };
            }
            return planes;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Update()
        {
            foreach (var f in flights)
            {
                float angle = f.Phase + f.Direction * Time.time * Speed / f.Radius;
                Place(f.Plane, f, angle, 0f, -f.Direction * 14f);
                float lag = (TowLength + BannerLength * 0.5f) / f.Radius;
                Place(f.Banner, f, angle - f.Direction * lag, Mathf.Sin(Time.time * 2.3f + f.Phase) * 3f, 0f);
            }
        }

        static void Place(Transform t, Flight f, float angle, float wobble, float bank)
        {
            var pos = f.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * f.Radius;
            var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * f.Direction;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, wobble, bank));
        }

        // A high-wing single prop with a tow hook, nose along +z, about 1.5x a real Cessna so it reads
        // from the street.
        static Mesh PlaneMesh()
        {
            var lp = new LowPoly();
            var white = LowPoly.Hex("#F4F5F7");
            var red = LowPoly.Hex("#C8102E");
            var glass = LowPoly.Hex("#2C3E57");
            var dark = LowPoly.Hex("#2A2C30");
            const float S = 1.5f;
            lp.Box(new Vector3(0f, 0f, 0.5f) * S, new Vector3(1.3f, 1.4f, 4.2f) * S, white);
            lp.Box(new Vector3(0f, 0.25f, -3.2f) * S, new Vector3(0.7f, 0.8f, 3.6f) * S, white);
            lp.Box(new Vector3(0f, -0.2f, 0.6f) * S, new Vector3(1.32f, 0.3f, 4.0f) * S, red);
            lp.Box(new Vector3(0f, 0.45f, 1.7f) * S, new Vector3(1.34f, 0.5f, 1.2f) * S, glass);
            lp.Box(new Vector3(0f, 0.85f, 0.9f) * S, new Vector3(11f, 0.18f, 1.6f) * S, white);
            lp.Box(new Vector3(0f, 0.86f, 0.9f) * S, new Vector3(11.05f, 0.12f, 0.4f) * S, red);
            lp.Box(new Vector3(0f, 0.6f, -4.8f) * S, new Vector3(3.6f, 0.12f, 1.0f) * S, white);
            lp.Box(new Vector3(0f, 1.2f, -4.8f) * S, new Vector3(0.12f, 1.4f, 1.0f) * S, red);
            lp.Box(new Vector3(0f, 0f, 2.75f) * S, new Vector3(0.5f, 0.5f, 0.3f) * S, dark);
            lp.Box(new Vector3(0f, 0f, 2.95f) * S, new Vector3(0.12f, 2.0f, 0.06f) * S, dark);
            lp.Bar(new Vector3(0f, -0.1f, -5f) * S, new Vector3(0f, -0.4f, -5f * S - TowLength), 0.08f, dark);
            return lp.ToMesh("banner plane");
        }

        // The banner hangs below the tow line, its leading edge at the local origin and trailing along -z,
        // with the text readable from both sides. Mesh pivot is the banner's centre.
        static Mesh BannerMesh(string text, string panelHex, string inkHex)
        {
            var mb = new MeshBuilder();
            var panel = LandmarkMeshes.Hex(panelHex);
            var ink = LandmarkMeshes.Hex(inkHex);
            var dark = LandmarkMeshes.Hex("#2A2C30");
            float half = BannerLength * 0.5f;
            mb.Box(new Vector3(0f, 0f, half + 0.3f), new Vector3(0.2f, BannerHeight + 0.4f, 0.3f), Quaternion.identity, dark);
            foreach (float side in new[] { 1f, -1f })
            {
                var normal = new Vector3(side, 0f, 0f);
                var right = new Vector3(0f, 0f, side);
                var c = normal * 0.05f;
                mb.Quad(c + new Vector3(0f, -BannerHeight * 0.5f, -half), c + new Vector3(0f, -BannerHeight * 0.5f, half), c + new Vector3(0f, BannerHeight * 0.5f, half), c + new Vector3(0f, BannerHeight * 0.5f, -half), panel, normal);
                LandmarkMeshes.TextBlock(mb, normal * 0.08f, right, normal, BannerLength - 2f, BannerHeight - 1f, text, ink);
            }
            return mb.ToMesh("ad banner");
        }
    }
}
