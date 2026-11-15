using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // Monster truck: a bright orange pickup body jacked up on chrome suspension struts and yellow
        // coil springs over four giant 1.9 m tyres, with a roof light bar and a chrome grille.
        public static (GameObject, Bounds) MonsterTruck(Material material)
        {
            var body = Cached("monster truck body", () =>
            {
                var lp = new LowPoly();
                var paint = C("#ff6a1a");
                var dark = C("#22252a");
                var chrome = C("#c9cdd2");
                var glass = C("#1f2a44");
                var spring = C("#ffd23f");
                lp.Box(new Vector3(0f, 1.55f, 0f), new Vector3(1.5f, 0.28f, 4.2f), dark);
                foreach (float s in new[] { 1f, -1f })
                    foreach (float z in new[] { 1.65f, -1.65f })
                    {
                        lp.Box(new Vector3(s * 0.95f, 1.25f, z), new Vector3(0.16f, 0.9f, 0.16f), chrome);
                        lp.CylinderY(new Vector3(s * 0.75f, 1.0f, z), 0.16f, 0.16f, 0.65f, 8, spring, spring);
                        lp.Box(new Vector3(s * 0.6f, 0.95f, z), new Vector3(1.0f, 0.14f, 0.14f), dark);
                    }
                lp.Box(new Vector3(0f, 2.15f, 0.1f), new Vector3(2.3f, 0.75f, 4.7f), paint);
                lp.Box(new Vector3(0f, 2.9f, -0.25f), new Vector3(2.05f, 0.8f, 1.8f), paint);
                foreach (float s in new[] { 1f, -1f })
                    lp.Box(new Vector3(s * 1.03f, 2.95f, -0.25f), new Vector3(0.02f, 0.5f, 1.4f), glass);
                lp.Box(new Vector3(0f, 2.95f, 0.66f), new Vector3(1.8f, 0.55f, 0.02f), glass);
                lp.Box(new Vector3(0f, 2.95f, -1.16f), new Vector3(1.8f, 0.5f, 0.02f), glass);
                lp.Box(new Vector3(0f, 3.36f, -0.25f), new Vector3(1.7f, 0.12f, 0.25f), C("#fff4c8", 0.9f));
                lp.Box(new Vector3(0f, 2.1f, 2.47f), new Vector3(1.7f, 0.45f, 0.06f), chrome);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Box(new Vector3(s * 0.8f, 2.35f, 2.46f), new Vector3(0.35f, 0.18f, 0.06f), C("#fff4c8", 0.85f));
                    lp.Box(new Vector3(s * 0.85f, 2.3f, -2.22f), new Vector3(0.3f, 0.18f, 0.06f), C("#e8352c", 0.6f));
                    lp.Box(new Vector3(s * 1.12f, 2.6f, -1.6f), new Vector3(0.08f, 0.3f, 1.7f), paint);
                }
                lp.Box(new Vector3(0f, 1.85f, 2.55f), new Vector3(2.0f, 0.22f, 0.22f), chrome);
                lp.Box(new Vector3(0f, 1.85f, -2.42f), new Vector3(2.0f, 0.22f, 0.22f), chrome);
                return lp;
            });
            var root = new GameObject("monster-truck");
            Part(root.transform, "body", body, material, Vector3.zero);
            Wheels(root.transform, material, Wheel(0.95f, 0.75f, "#1c1d20", "#c9cdd2", 12), 1.38f, 1.65f, -1.65f, 0.95f);
            return (root, new Bounds(new Vector3(0f, 1.7f, 0.05f), new Vector3(3.5f, 3.4f, 5.2f)));
        }
    }
}
