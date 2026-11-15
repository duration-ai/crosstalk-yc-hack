using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // The Speed Dial world look (handoffs/style/STYLE-GUIDE.md): palette, one warm sun from the
    // southwest with one shadow map, a sky-blue fill and a warm bounce, no tone mapping and no
    // post-processing, a gradient sky with faceted clouds and a fog bank lying on the bay.
    public static class SpeedDial
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Color Lin(string hex) => Hex(hex).linear;

        public static readonly Color SkyTop = Lin("#0B6FD6");
        public static readonly Color SkyMid = Lin("#1C9BF2");
        public static readonly Color SkyHorizon = Lin("#B4E2FC");
        public static readonly Color Bay = Lin("#17B2D0");
        public static readonly Color Asphalt = Lin("#A9ADB4");
        public static readonly Color Sidewalk = Lin("#EEE7DA");
        public static readonly Color DoubleYellow = Lin("#FFCB1F");
        public static readonly Color LaneWhite = Lin("#F7F8F9");
        public static readonly Color Park = Lin("#48C26A");
        public static readonly Color Land = Lin("#E6DDCB");
        public static readonly Color Rail = Lin("#1C2347");
        public static readonly Color RailBed = Lin("#A3AEC4");
        public static readonly Color Cloud = Lin("#FFFFFF");
        public static readonly Color Smoke = Lin("#F2F4FA");
        public static readonly Color TireMark = Lin("#7E869B");

        // Ambient terms, in linear light, tuned so a road in sun lands on #A9ADB4 and the same road
        // in shadow on #6C758B: shadow only removes the sun, the remaining sky fill is periwinkle.
        public static readonly Color SkyFill = new Color(0.38f, 0.43f, 0.57f);
        public static readonly Color SideFill = new Color(0.55f, 0.58f, 0.72f);
        public static readonly Color Bounce = new Color(0.52f, 0.44f, 0.36f);
        public static readonly Color SunColor = new Color(1f, 0.92f, 0.69f);
        const float SunIntensity = 0.76f;
        const float SunElevation = 55f;
        const float SunAzimuth = 45f;

        public static Light Setup(Camera view)
        {
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = SkyHorizon.gamma;
            view.allowHDR = false;
            view.allowMSAA = true;
            view.nearClipPlane = 0.3f;
            view.farClipPlane = 3000f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(SunElevation, SunAzimuth, 0f);
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1f;
            sun.shadowBias = 0.08f;
            sun.shadowNormalBias = 0.6f;

            QualitySettings.shadowDistance = 90f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
            QualitySettings.pixelLightCount = 1;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHorizon.gamma;
            RenderSettings.fogStartDistance = 450f;
            RenderSettings.fogEndDistance = 3600f;
            Shader.SetGlobalColor("_SkyAmbient", SkyFill);
            Shader.SetGlobalColor("_SideAmbient", SideFill);
            Shader.SetGlobalColor("_GroundAmbient", Bounce);

            Shader.SetGlobalColor("_SkyTop", SkyTop);
            Shader.SetGlobalColor("_SkyMid", SkyMid);
            Shader.SetGlobalColor("_SkyHorizon", SkyHorizon);
            EnvironmentRig.Create(view, sun, SkyRig.Create(view));
            view.gameObject.AddComponent<CameraFx>();
            return sun;
        }

        // Low-poly icosphere, subdivided n times, as unit-sphere triangles.
        public static System.Collections.Generic.List<(Vector3, Vector3, Vector3)> Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var tris = new System.Collections.Generic.List<(Vector3, Vector3, Vector3)>();
            for (int i = 0; i < f.Length; i += 3) tris.Add((v[f[i]].normalized, v[f[i + 1]].normalized, v[f[i + 2]].normalized));
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new System.Collections.Generic.List<(Vector3, Vector3, Vector3)>();
                foreach (var (a, b, c) in tris)
                {
                    var ab = (a + b).normalized;
                    var bc = (b + c).normalized;
                    var ca = (c + a).normalized;
                    next.Add((a, ab, ca));
                    next.Add((b, bc, ab));
                    next.Add((c, ca, bc));
                    next.Add((ab, bc, ca));
                }
                tris = next;
            }
            return tris;
        }

        // Adds a faceted lump: an icosphere squashed by scale, its bottom cut flat at floor
        // (in unit-sphere y), each vertex jittered so the facets read as hand-made.
        public static void Lump(MeshBuilder mb, Vector3 center, Vector3 scale, float yaw, float floor, Color color, System.Random random, int subdivisions = 1)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var jitter = new System.Collections.Generic.Dictionary<Vector3, Vector3>();
            Vector3 P(Vector3 u)
            {
                if (!jitter.TryGetValue(u, out var j))
                    jitter[u] = j = u * (0.88f + (float)random.NextDouble() * 0.24f);
                j.y = Mathf.Max(j.y, floor);
                return center + rotation * Vector3.Scale(j, scale);
            }
            foreach (var (a, b, c) in Icosphere(subdivisions))
            {
                var pa = P(a);
                var pb = P(b);
                var pc = P(c);
                var facing = Vector3.Cross(pb - pa, pc - pa);
                if (facing.sqrMagnitude < 1e-6f) continue;
                var outward = (pa + pb + pc) / 3f - center;
                mb.Triangle(pa, pb, pc, color, Vector3.Dot(facing, outward) >= 0f ? facing : -facing);
            }
        }
    }
}
