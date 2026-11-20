using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Screen-edge speed lines in the reference art's flat graphic style: crisp white streaks, pointed
    // at the inner end, flying outward from just above the middle of the screen, plus a few wide soft
    // bands. They ramp in with MotionFx.Rush (speed, boost, downhill) and keep the middle of the
    // screen clear. One mesh on a camera child just past the near plane, drawn in the overlay queue
    // with no depth test: one draw call, thin triangles only, no full-screen pass.
    // Tunables: MotionTuning.speedLines.
    [DefaultExecutionOrder(1200)]
    public sealed class SpeedLines : MonoBehaviour
    {
        struct Line
        {
            public float Angle;
            public float Radius;
            public float Length;
            public float Flow;
            public bool Band;
        }

        Camera view;
        Line[] lines;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Color[] colors;
        float level;
        // During a boost the streaks warm to sunshine (Hot Wheels Unleashed / Rocket League boost lines).
        // Set in Start: Unity refuses QualitySettings reads from a static or field initializer.
        Color boostTint;
        readonly System.Random random = new System.Random(11);

        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        void Start()
        {
            boostTint = QualitySettings.activeColorSpace == ColorSpace.Linear ? SpeedDial.Hex("#FFCB1F").linear : SpeedDial.Hex("#FFCB1F");
            view = GetComponent<Camera>();
            var t = MotionTuning.Current.speedLines;
            lines = new Line[Mathf.Max(t.count, 1)];
            for (int i = 0; i < lines.Length; i++) lines[i] = NewLine(t, true);

            vertices = new Vector3[lines.Length * 4];
            colors = new Color[vertices.Length];
            var indices = new int[lines.Length * 6];
            for (int q = 0; q < lines.Length; q++)
            {
                int v = q * 4, k = q * 6;
                indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                indices[k + 3] = v; indices[k + 4] = v + 2; indices[k + 5] = v + 3;
            }
            mesh = new Mesh { name = "speed lines" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);

            var go = new GameObject("SpeedLines");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "Speed lines" };
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
        }

        Line NewLine(MotionTuning.SpeedLineFeel t, bool scatter) => new Line
        {
            Angle = PickAngle(),
            Radius = t.innerRadius + (scatter ? R(0f, 0.5f) : R(0f, 0.1f)),
            Length = R(t.lengthMin, t.lengthMax),
            Flow = R(0.75f, 1.3f),
            Band = random.NextDouble() < t.bandShare,
        };

        // Lines fan out to the sides and corners, never straight down over the car or straight up the
        // middle of the sky.
        float PickAngle()
        {
            while (true)
            {
                float a = R(0f, 360f);
                if (Mathf.Abs(Mathf.DeltaAngle(a, 270f)) < 30f || Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 20f) continue;
                return a * Mathf.Deg2Rad;
            }
        }

        void LateUpdate()
        {
            if (mesh == null) return;
            var t = MotionTuning.Current.speedLines;
            bool paused = Time.timeScale <= 0f;
            float target = paused ? 0f : MotionFx.Rush;
            float rate = target > level ? t.fadeIn : t.fadeOut;
            level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * rate);
            meshRenderer.enabled = level > 0.01f;
            if (!meshRenderer.enabled) return;

            float dt = Time.deltaTime;
            float depth = view.nearClipPlane * 1.5f;
            float h = depth * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = view.aspect;
            float intensity = EffectsSettings.Scale;
            float alpha = Mathf.Min(t.alpha * intensity, 0.95f) * Mathf.Min(1f, level * 1.5f);
            int active = Mathf.CeilToInt(lines.Length * Mathf.Clamp01(0.35f + 0.65f * level) * Mathf.Min(EffectsSettings.Intensity, 1f));
            var center = new Vector2(0f, t.centerY);

            for (int i = 0; i < lines.Length; i++)
            {
                ref var line = ref lines[i];
                line.Radius += t.flow * line.Flow * dt * (0.5f + level);
                if (line.Radius > 1.2f) line = NewLine(t, false);
                int v = i * 4;
                if (i >= active)
                {
                    vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
                    continue;
                }
                var dir = new Vector2(Mathf.Cos(line.Angle), Mathf.Sin(line.Angle));
                var radii = new Vector2(aspect * 0.5f, 0.5f);
                var inner = center + Vector2.Scale(dir, radii) * line.Radius;
                var outer = center + Vector2.Scale(dir, radii) * (line.Radius + line.Length);
                var mid = Vector2.Lerp(inner, outer, 0.72f);
                var along = (outer - inner).normalized;
                var normal = new Vector2(-along.y, along.x) * ((line.Band ? t.bandWidth : t.width) * 0.5f / 720f);
                float fade = Mathf.Clamp01((line.Radius - t.innerRadius) / 0.15f) * alpha * (line.Band ? t.bandAlpha / Mathf.Max(t.alpha, 0.01f) : 1f);

                vertices[v] = View(inner, h, depth);
                vertices[v + 1] = View(mid + normal, h, depth);
                vertices[v + 2] = View(outer, h, depth);
                vertices[v + 3] = View(mid - normal, h, depth);
                var c = Color.Lerp(Color.white, boostTint, MotionFx.BoostFactor * 0.85f);
                c.a = fade;
                colors[v + 1] = colors[v + 3] = c;
                c.a = fade * 0.9f;
                colors[v + 2] = c;
                c.a = fade * 0.15f;
                colors[v] = c;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
        }

        // Screen units (1 = screen height, origin at the centre) to the camera's local space at depth.
        static Vector3 View(Vector2 p, float h, float depth) => new Vector3(p.x * 2f * h, p.y * 2f * h, depth);
    }
}
