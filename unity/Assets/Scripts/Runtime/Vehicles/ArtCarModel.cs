using UnityEngine;

namespace Dogpatch.Vehicles
{
    public static partial class ProceduralModels
    {
        // Burning Man mutant vehicle: a giant goldfish riding a low purple flatbed, with a chrome
        // poofer on its head (Flamethrower), and LED strips down the deck and along the fish's flanks.
        // The LEDs come in three "led-N" parts with the colours shifted one step each, so turning
        // them on in turn makes the strips chase.
        public static (GameObject, Bounds) ArtCar(Material material)
        {
            var rings = new[]
            {
                new Vector4(-2.7f, 2.55f, 0.32f, 0.22f), new Vector4(-1.8f, 2.55f, 0.95f, 0.7f), new Vector4(-0.4f, 2.6f, 1.4f, 1.12f),
                new Vector4(1.1f, 2.6f, 1.38f, 1.1f), new Vector4(2.3f, 2.55f, 1.05f, 0.88f), new Vector4(3.15f, 2.45f, 0.5f, 0.45f),
            };
            const int sides = 8;
            Vector3 P(Vector4 ring, float a) => new Vector3(Mathf.Sin(a) * ring.w, ring.y + Mathf.Cos(a) * ring.z, ring.x);
            var body = Cached("art car body", () =>
            {
                var lp = new LowPoly();
                var deck = C("#2a2630");
                var deckTop = C("#4a3f55");
                var gold = C("#f3a531");
                var orange = C("#e8692a");
                var belly = C("#fbe3b8");
                var chrome = C("#e6ecf2", 0.2f);
                lp.Box(new Vector3(0f, 0.85f, 0f), new Vector3(2.5f, 0.5f, 6.6f), deck);
                lp.Box(new Vector3(0f, 1.13f, 0f), new Vector3(2.6f, 0.06f, 6.7f), deckTop);
                foreach (float s in new[] { 1f, -1f })
                    foreach (float z in new[] { -1.6f, 1.4f })
                        lp.Bar(new Vector3(s * 0.55f, 1.15f, z), new Vector3(s * 0.4f, 1.8f, z), 0.16f, deck);
                for (int r = 0; r + 1 < rings.Length; r++)
                    for (int i = 0; i < sides; i++)
                    {
                        float a0 = i * Mathf.PI * 2f / sides;
                        float a1 = (i + 1) * Mathf.PI * 2f / sides;
                        var color = Mathf.Cos((a0 + a1) * 0.5f) < -0.4f ? belly : (r + i) % 2 == 0 ? gold : orange;
                        lp.Quad(P(rings[r], a0), P(rings[r], a1), P(rings[r + 1], a1), P(rings[r + 1], a0), color);
                    }
                var nose = new Vector3(0f, 2.45f, 3.35f);
                var tail = new Vector3(0f, 2.55f, -2.75f);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides;
                    float a1 = (i + 1) * Mathf.PI * 2f / sides;
                    lp.Tri(nose, P(rings[rings.Length - 1], a0), P(rings[rings.Length - 1], a1), orange);
                    lp.Tri(tail, P(rings[0], a0), P(rings[0], a1), orange);
                }
                lp.Box(new Vector3(0f, 2.2f, 3.22f), new Vector3(0.55f, 0.14f, 0.2f), C("#5a1020"));
                lp.Tri(new Vector3(0f, 2.55f, -2.6f), new Vector3(0f, 4.1f, -4.1f), new Vector3(0f, 2.6f, -3.4f), gold);
                lp.Tri(new Vector3(0f, 2.55f, -2.6f), new Vector3(0f, 2.5f, -3.4f), new Vector3(0f, 1.3f, -3.95f), orange);
                lp.Box(new Vector3(0f, 3.35f, -3.35f), new Vector3(0.26f, 1.7f, 0.8f), Quaternion.Euler(-42f, 0f, 0f), gold);
                lp.Box(new Vector3(0f, 1.95f, -3.3f), new Vector3(0.26f, 1.2f, 0.6f), Quaternion.Euler(42f, 0f, 0f), orange);
                lp.Tri(new Vector3(0f, 3.45f, -1.7f), new Vector3(0f, 4.75f, -2.0f), new Vector3(0f, 4.0f, 0.3f), orange);
                foreach (float s in new[] { 1f, -1f })
                {
                    lp.Tri(new Vector3(s * 1.05f, 2.2f, 1.0f), new Vector3(s * 1.95f, 1.65f, 0.15f), new Vector3(s * 1.1f, 2.05f, -0.2f), gold);
                    lp.CylinderX(new Vector3(s * 0.86f, 2.85f, 2.35f), 0.36f, 0.16f, 10, C("#ffffff"), C("#ffffff"));
                    lp.CylinderX(new Vector3(s * 0.92f, 2.88f, 2.42f), 0.18f, 0.12f, 8, C("#111114"), C("#111114"));
                }
                lp.CylinderY(new Vector3(0f, 3.85f, 1.2f), 0.34f, 0.3f, 0.5f, 10, chrome, chrome);
                lp.Bar(new Vector3(0f, 4.2f, 1.2f), new Vector3(0f, 4.3f, 2.8f), 0.2f, chrome);
                lp.Box(new Vector3(0f, 4.3f, 2.85f), new Vector3(0.3f, 0.3f, 0.12f), C("#2b2e33"));
                lp.Box(new Vector3(0f, 4.3f, 2.93f), new Vector3(0.12f, 0.12f, 0.04f), C("#ffb347", 1f));
                return lp;
            });
            var root = new GameObject("art-car");
            Part(root.transform, "body", body, material, Vector3.zero);
            var palette = new[] { "#ff2d95", "#ffe14d", "#2dfcff", "#7b5cff", "#50ff6a", "#ff7a1a" };
            for (int phase = 0; phase < 3; phase++)
            {
                int shift = phase;
                var led = Cached("art car led " + shift, () =>
                {
                    var lp = new LowPoly();
                    int k = 0;
                    foreach (float s in new[] { 1f, -1f })
                    {
                        for (int i = 0; i < 14; i++, k++)
                            lp.Box(new Vector3(s * 1.27f, 0.92f, -3.12f + i * 0.48f), new Vector3(0.04f, 0.14f, 0.36f), C(palette[(k + shift) % palette.Length], 1f));
                        for (int r = 0; r + 1 < rings.Length; r++)
                            for (int j = 0; j < 3; j++, k++)
                            {
                                var p = Vector3.Lerp(P(rings[r], s * Mathf.PI * 0.5f), P(rings[r + 1], s * Mathf.PI * 0.5f), (j + 0.5f) / 3f);
                                lp.Box(p + new Vector3(s * 0.03f, 0f, 0f), new Vector3(0.08f, 0.14f, 0.24f), C(palette[(k + shift) % palette.Length], 1f));
                            }
                    }
                    return lp;
                });
                Part(root.transform, "led-" + phase, led, material, Vector3.zero).SetActive(phase == 0);
            }
            Wheels(root.transform, material, Wheel(0.55f, 0.42f, "#1c1d20", "#c7a13a", 10), 1.08f, 2.3f, -2.3f, 0.55f);
            return (root, new Bounds(new Vector3(0f, 2.2f, -0.4f), new Vector3(2.8f, 4.4f, 7.6f)));
        }
    }
}
