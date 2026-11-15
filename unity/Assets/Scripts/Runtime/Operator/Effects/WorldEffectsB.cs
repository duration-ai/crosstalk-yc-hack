using System;
using System.Collections.Generic;
using Dogpatch.Ambient;
using Dogpatch.Gameplay;
using Dogpatch.Smash;
using Dogpatch.Traffic;
using Dogpatch.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dogpatch.Operator
{
    // Crowd, traffic and object effects (CROS-39): rush hour, empty roads, road rage, green lights,
    // speed up and slow down. Everything they change goes through a Knob or a multiply that is
    // divided back out, so rows stack and revert in any order.
    public static class WorldEffectsB
    {
        public static void Register(EffectRegistry r)
        {
            r.Register("more_traffic", () => new TrafficDensityEffect(true));
            r.Register("less_traffic", () => new TrafficDensityEffect(false));
            r.Register("aggressive_traffic", () => new RoadRageEffect());
            r.Register("green_lights", () => new GreenLightsEffect());
            r.Register("speed_up", () => new PaceEffect(true));
            r.Register("slow_down", () => new PaceEffect(false));
            r.Register("more_pedestrians", () => new CrowdDensityEffect(true));
            r.Register("fewer_pedestrians", () => new CrowdDensityEffect(false));
            r.Register("angry_npcs", () => new CrowdMoodEffect(CrowdMood.Angry));
            r.Register("calm_npcs", () => new CrowdMoodEffect(CrowdMood.Calm));
            r.Register("panic", () => new CrowdMoodEffect(CrowdMood.Panic));
            r.Register("ped_theme", () => new PedThemeEffect());
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c.linear;
        }

        public static void Poof(Vector3 at, float radius, Color color, int count = 8) => SmashWorld.Instance?.Fx?.Poof(at, radius, color, count);

        public static Camera Eye => Camera.main;
    }

    // Values several rows put on one knob at once; each row adds its own and removes it on revert.
    public sealed class Knob
    {
        readonly List<float> values = new List<float>();

        public int Count => values.Count;

        public void Add(float v) => values.Add(v);

        public void Remove(float v) => values.Remove(v);

        public float Product
        {
            get
            {
                float p = 1f;
                foreach (var v in values) p *= v;
                return p;
            }
        }

        public float Max
        {
            get
            {
                float m = 0f;
                foreach (var v in values) m = Mathf.Max(m, v);
                return m;
            }
        }
    }

    // A new car springs up out of the road with a little overshoot and a puff.
    public sealed class PopIn : MonoBehaviour
    {
        Vector3 rest;
        float t;

        public static void Start(GameObject go)
        {
            if (go.GetComponent<PopIn>() != null) return;
            WorldEffectsB.Poof(go.transform.position + Vector3.up * 0.6f, 2f, SmashFx.SmokeLight, 8);
            go.AddComponent<PopIn>();
        }

        void Awake()
        {
            rest = transform.localScale;
            transform.localScale = rest * 0.05f;
        }

        void Update()
        {
            t += Time.deltaTime / 0.4f;
            if (t >= 1f)
            {
                transform.localScale = rest;
                Destroy(this);
                return;
            }
            float u = t - 1f;
            transform.localScale = rest * Mathf.Max(0.05f, 1f + 2.7f * u * u * u + 1.7f * u * u);
        }
    }

    // Shrinks a thing to nothing in a puff, puts its scale back and hands it to done (pooling).
    public sealed class PopOut : MonoBehaviour
    {
        Vector3 rest;
        float t;
        Action done;

        public static void Start(GameObject go, Action done)
        {
            if (go.GetComponent<PopOut>() != null) return;
            var pin = go.GetComponent<PopIn>();
            if (pin != null) DestroyImmediate(pin);
            go.AddComponent<PopOut>().done = done;
        }

        void Awake() => rest = transform.localScale;

        void Update()
        {
            t += Time.deltaTime / 0.3f;
            if (t < 1f)
            {
                transform.localScale = rest * (1f + Mathf.Sin(t * Mathf.PI) * 0.25f) * (1f - t * t);
                return;
            }
            WorldEffectsB.Poof(transform.position + Vector3.up * 0.6f, 2.2f, SmashFx.SmokeLight, 10);
            transform.localScale = rest;
            Destroy(this);
            done?.Invoke();
        }
    }

    // World-space flat shapes for effects, rebuilt every frame into one mesh and drawn in one call
    // with the unlit alpha material: speed stripes, slow bubbles, anger marks, hearts.
    [DefaultExecutionOrder(1000)]
    public sealed class EffectSprites : MonoBehaviour
    {
        public enum Icon { Anger, Heart, Alarm }

        static EffectSprites instance;
        static Vector3[] ico;
        static int[] icoTris;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        Mesh mesh;
        Material material;
        Bounds bounds;

        public static EffectSprites Get()
        {
            if (instance == null) instance = new GameObject("EffectSprites").AddComponent<EffectSprites>();
            return instance;
        }

        void Awake()
        {
            mesh = new Mesh { name = "effect sprites", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            material = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "EffectSprites" };
        }

        int V(Vector3 p, Color c)
        {
            if (verts.Count == 0) bounds = new Bounds(p, Vector3.one);
            else bounds.Encapsulate(p);
            verts.Add(p);
            colors.Add(c);
            return verts.Count - 1;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            tris.Add(V(a, col));
            tris.Add(V(b, col));
            tris.Add(V(c, col));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ab, Color cd)
        {
            int i = V(a, ab), j = V(b, ab), k = V(c, cd), l = V(d, cd);
            tris.Add(i); tris.Add(j); tris.Add(k);
            tris.Add(i); tris.Add(k); tris.Add(l);
        }

        // Three thin stripes trailing behind something moving along forward.
        public void Stripes(Vector3 center, Vector3 forward, float halfLength, float halfWidth, float height, float length)
        {
            var cam = WorldEffectsB.Eye;
            if (cam == null) return;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            var head = new Color(1f, 1f, 1f, 0.9f);
            var tail = new Color(1f, 1f, 1f, 0f);
            float time = Time.time;
            for (int i = 0; i < 3; i++)
            {
                var start = center - forward * halfLength + right * ((i - 1) * halfWidth * 0.8f) + Vector3.up * (height * (0.3f + 0.25f * i));
                float l = length * (1f - 0.25f * Mathf.Abs(i - 1)) * (0.8f + 0.2f * Mathf.Sin(time * 25f + i * 2f));
                var end = start - forward * l;
                var n = Vector3.Cross(forward, cam.transform.position - start).normalized * Mathf.Max(0.05f, height * 0.04f);
                Quad(start - n, start + n, end + n * 0.3f, end - n * 0.3f, head, tail);
            }
        }

        // A see-through faceted bubble.
        public void Bubble(Vector3 center, float radius, Color color)
        {
            if (ico == null)
            {
                var m = SmashFx.Icosahedron();
                ico = m.vertices;
                icoTris = m.triangles;
            }
            for (int i = 0; i < icoTris.Length; i += 3)
            {
                var c = color;
                float shade = 0.85f + 0.3f * Mathf.Repeat(i * 0.618f, 1f);
                c.r *= shade; c.g *= shade; c.b *= shade;
                Tri(center + ico[icoTris[i]].normalized * radius, center + ico[icoTris[i + 1]].normalized * radius, center + ico[icoTris[i + 2]].normalized * radius, c);
            }
        }

        public void Mark(Icon icon, Vector3 at, float size)
        {
            var cam = WorldEffectsB.Eye;
            if (cam == null) return;
            var r = cam.transform.right * size;
            var u = cam.transform.up * size;
            switch (icon)
            {
                case Icon.Anger:
                    var red = WorldEffectsB.Hex("#E8333A");
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = i * Mathf.PI / 4f, a1 = a0 + Mathf.PI / 8f, a2 = a0 - Mathf.PI / 8f;
                        Tri(at, at + (r * Mathf.Cos(a2) + u * Mathf.Sin(a2)) * 0.42f, at + r * Mathf.Cos(a0) + u * Mathf.Sin(a0), red);
                        Tri(at, at + r * Mathf.Cos(a0) + u * Mathf.Sin(a0), at + (r * Mathf.Cos(a1) + u * Mathf.Sin(a1)) * 0.42f, red);
                    }
                    break;
                case Icon.Heart:
                    var coral = WorldEffectsB.Hex("#FF9C86");
                    Vector3 prev = default;
                    for (int i = 0; i <= 20; i++)
                    {
                        float t = i / 20f * Mathf.PI * 2f;
                        float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f) / 17f;
                        float y = (13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t)) / 17f;
                        var p = at + r * x + u * y;
                        if (i > 0) Tri(at, prev, p, coral);
                        prev = p;
                    }
                    break;
                case Icon.Alarm:
                    var yellow = WorldEffectsB.Hex("#FFCB1F");
                    var ink = WorldEffectsB.Hex("#1C2347");
                    Quad(at + u * 1f - r * 0.22f, at + u * 1f + r * 0.22f, at - u * 0.25f + r * 0.14f, at - u * 0.25f - r * 0.14f, ink, ink);
                    Quad(at + u * 0.9f - r * 0.14f, at + u * 0.9f + r * 0.14f, at - u * 0.15f + r * 0.08f, at - u * 0.15f - r * 0.08f, yellow, yellow);
                    Quad(at - u * 0.45f, at - u * 0.6f + r * 0.15f, at - u * 0.75f, at - u * 0.6f - r * 0.15f, yellow, yellow);
                    break;
            }
        }

        void LateUpdate()
        {
            mesh.Clear();
            if (verts.Count == 0) return;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = bounds;
            Graphics.RenderMesh(new RenderParams(material)
            {
                worldBounds = bounds,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            }, mesh, 0, Matrix4x4.identity);
            verts.Clear();
            colors.Clear();
            tris.Clear();
        }
    }

    // Rush hour (more) or empty roads (less) for the ambient traffic.
    public sealed class TrafficDensityEffect : OperatorEffect
    {
        readonly bool more;
        float k;

        public TrafficDensityEffect(bool more) => this.more = more;

        public override void Apply(string strength, EffectTarget target)
        {
            k = more ? Scale(strength, 1.75f, 2.5f, 3.5f) : Scale(strength, 0.5f, 0.2f, 0f);
            TrafficSystem.Density.Add(k);
            TrafficSystem.Instance?.Rebalance();
        }

        public override void Revert()
        {
            TrafficSystem.Density.Remove(k);
            TrafficSystem.Instance?.Rebalance();
        }
    }

    // Every AI driver floors it, tailgates, runs lights, leans on the horn and weaves in its lane,
    // with a red anger mark bobbing over each nearby car.
    public sealed class RoadRageEffect : OperatorEffect
    {
        static readonly Knob Rage = new Knob();
        float k;

        public override void Apply(string strength, EffectTarget target)
        {
            k = Scale(strength, 0.4f, 0.7f, 1f);
            Rage.Add(k);
            TrafficAgent.Rage = Rage.Max;
            if (Context.Host.GetComponent<RoadRageMarks>() == null) Context.Host.gameObject.AddComponent<RoadRageMarks>();
            if (TrafficSystem.Instance == null) return;
            foreach (var a in TrafficSystem.Instance.Agents)
                if (!a.OnRail) a.Car.GetComponent<VehicleAudio>()?.Signal(0.1f);
        }

        public override void Revert()
        {
            Rage.Remove(k);
            TrafficAgent.Rage = Rage.Max;
        }
    }

    public sealed class RoadRageMarks : MonoBehaviour
    {
        void Update()
        {
            if (TrafficAgent.Rage <= 0f || TrafficSystem.Instance == null) return;
            var cam = WorldEffectsB.Eye;
            if (cam == null) return;
            var sprites = EffectSprites.Get();
            var eye = cam.transform.position;
            foreach (var a in TrafficSystem.Instance.Agents)
            {
                if (a.OnRail || a.Car == null || (a.transform.position - eye).sqrMagnitude > 90f * 90f) continue;
                float bob = Mathf.Abs(Mathf.Sin(Time.time * 6f + a.GetInstanceID()));
                var top = a.transform.position + Vector3.up * (a.Car.HalfExtents.y * 2f + 0.9f + bob * 0.3f);
                sprites.Mark(EffectSprites.Icon.Anger, top, 0.55f + 0.15f * TrafficAgent.Rage + bob * 0.1f);
            }
        }
    }

    // Every signal goes green for traffic, and every traffic light in the city shows it: red and
    // amber lamps go dark and the green lamp glows, with a green puff at the junctions near you.
    public sealed class GreenLightsEffect : OperatorEffect
    {
        static int active;
        static readonly Dictionary<int, Color[]> original = new Dictionary<int, Color[]>();

        public override void Apply(string strength, EffectTarget target)
        {
            if (active++ == 0)
            {
                StreetGraph.AllGreen = true;
                Paint(true);
            }
            var eye = WorldEffectsB.Eye != null ? WorldEffectsB.Eye.transform.position : Vector3.zero;
            var green = WorldEffectsB.Hex("#2EC27E");
            int n = 0;
            foreach (var tag in EntityTag.All)
            {
                if (tag.Span == null || tag.Desc != "traffic light" || (tag.Span.Bounds.center - eye).sqrMagnitude > 110f * 110f) continue;
                WorldEffectsB.Poof(tag.Span.Bounds.center + Vector3.up * 2f, 2.4f, green, 10);
                if (++n >= 16) break;
            }
        }

        public override void Revert()
        {
            if (--active > 0) return;
            active = 0;
            StreetGraph.AllGreen = false;
            Paint(false);
        }

        // One read and one write per chunk mesh for all the city's signals.
        static void Paint(bool green)
        {
            var off = new Color(0.03f, 0.035f, 0.05f, 1f);
            var lit = new Color(0.25f, 1f, 0.4f, 0.15f);
            var chunks = new Dictionary<MeshFilter, List<World.StaticInstance>>();
            foreach (var tag in EntityTag.All)
            {
                if (tag.Span == null || tag.Desc != "traffic light") continue;
                foreach (var part in tag.Span.Parts)
                {
                    if (part.Chunk == null) continue;
                    if (!chunks.TryGetValue(part.Chunk, out var list)) chunks[part.Chunk] = list = new List<World.StaticInstance>();
                    list.Add(part);
                }
            }
            foreach (var pair in chunks)
            {
                var mesh = pair.Key.sharedMesh;
                var colors = mesh.colors;
                if (colors.Length == 0) continue;
                foreach (var part in pair.Value)
                {
                    if (green)
                    {
                        var keep = new Color[part.VertexCount];
                        Array.Copy(colors, part.VertexStart, keep, 0, part.VertexCount);
                        original[part.Id] = keep;
                        for (int i = 0; i < part.VertexCount; i++)
                        {
                            var c = keep[i];
                            Color.RGBToHSV(c, out float h, out float s, out float v);
                            if (s < 0.5f || v < 0.2f) continue;
                            colors[part.VertexStart + i] = h > 0.2f && h < 0.55f ? lit : off;
                        }
                    }
                    else if (original.TryGetValue(part.Id, out var keep))
                    {
                        Array.Copy(keep, 0, colors, part.VertexStart, part.VertexCount);
                        original.Remove(part.Id);
                    }
                }
                mesh.colors = colors;
            }
        }
    }

    // Speeds up (or slows down) each target: an AI car's pace, a driven car's top speed, a
    // pedestrian's walk. Sped-up things trail three speed stripes; slowed ones sit in a pale blue
    // bubble.
    public sealed class PaceEffect : OperatorEffect
    {
        const int MaxTargets = 60;

        readonly bool faster;
        float k;
        readonly List<(TrafficAgent agent, CarController car, Pedestrian ped, int pedId, EntityTag tag)> movers = new List<(TrafficAgent, CarController, Pedestrian, int, EntityTag)>();
        PaceMarks marks;

        public PaceEffect(bool faster) => this.faster = faster;

        public override void Apply(string strength, EffectTarget target)
        {
            k = faster ? Scale(strength, 1.6f, 2.2f, 3f) : Scale(strength, 0.6f, 0.35f, 0.2f);
            foreach (var tag in target.Entities)
            {
                if (movers.Count >= MaxTargets) break;
                var agent = tag.GetComponent<TrafficAgent>();
                var car = agent == null ? tag.GetComponent<CarController>() : null;
                var ped = tag.GetComponent<Pedestrian>();
                if (agent != null) agent.Pace *= k;
                else if (car != null) car.SpeedMul *= k;
                else if (ped != null)
                {
                    ped.Cruise *= k;
                    ped.Hurry = 0f;
                }
                else continue;
                movers.Add((agent, car, ped, ped != null ? ped.Id : 0, tag));
                var at = ped != null ? ped.Position + Vector3.up : tag.WorldBounds.center;
                WorldEffectsB.Poof(at, 1.4f, faster ? SmashFx.SmokeLight : WorldEffectsB.Hex("#9ED7FF"), 6);
            }
            marks = Context.Host.gameObject.AddComponent<PaceMarks>();
            marks.Faster = faster;
            foreach (var m in movers) marks.Tags.Add((m.tag, m.ped, m.pedId));
        }

        public override void Revert()
        {
            foreach (var (agent, car, ped, pedId, _) in movers)
            {
                if (agent != null) agent.Pace = Unscale(agent.Pace);
                else if (car != null) car.SpeedMul = Unscale(car.SpeedMul);
                else if (ped != null && ped.Id == pedId) ped.Cruise /= k;
            }
            movers.Clear();
            if (marks != null) Object.Destroy(marks);
        }

        float Unscale(float v)
        {
            v /= k;
            return Mathf.Abs(v - 1f) < 1e-3f ? 1f : v;
        }
    }

    // Crowds (more) or ghost town (fewer) on the streets.
    public sealed class CrowdDensityEffect : OperatorEffect
    {
        readonly bool more;
        float k;

        public CrowdDensityEffect(bool more) => this.more = more;

        public override void Apply(string strength, EffectTarget target)
        {
            k = more ? Scale(strength, 1.75f, 2.5f, 3.5f) : Scale(strength, 0.5f, 0.2f, 0.05f);
            PedestrianSystem.Crowd.Add(k);
            PedestrianSystem.Instance?.Recount();
            CrowdFx.Ensure(Context);
        }

        public override void Revert()
        {
            PedestrianSystem.Crowd.Remove(k);
            PedestrianSystem.Instance?.Recount();
        }
    }

    public sealed class CrowdMoodEffect : OperatorEffect
    {
        readonly CrowdMood mood;
        float k;

        public CrowdMoodEffect(CrowdMood mood) => this.mood = mood;

        public override void Apply(string strength, EffectTarget target)
        {
            k = Scale(strength, 0.5f, 0.75f, 1f);
            PedestrianSystem.AddMood(mood, k);
            CrowdFx.Ensure(Context);
        }

        public override void Revert() => PedestrianSystem.RemoveMood(mood, k);
    }

    public sealed class PedThemeEffect : OperatorEffect
    {
        string theme;

        public override void Apply(string strength, EffectTarget target)
        {
            theme = string.IsNullOrEmpty(target.Theme) ? "beach_party" : target.Theme;
            PedestrianSystem.AddTheme(theme);
            CrowdFx.Ensure(Context);
        }

        public override void Revert() => PedestrianSystem.RemoveTheme(theme);
    }

    // Runs the crowd's per-frame dressing and mood marks while any crowd effect is on, and one
    // more frame after the last one so the outfits go back.
    public sealed class CrowdFx : MonoBehaviour
    {
        bool was;

        public static void Ensure(EffectContext context)
        {
            if (context.Host.GetComponent<CrowdFx>() == null) context.Host.gameObject.AddComponent<CrowdFx>();
        }

        void Update()
        {
            bool busy = PedestrianSystem.Busy;
            if ((busy || was) && PedestrianSystem.Instance != null) PedestrianSystem.Instance.OperatorFrame();
            was = busy;
        }
    }

    public sealed class PaceMarks : MonoBehaviour
    {
        public bool Faster;
        public readonly List<(EntityTag tag, Pedestrian ped, int pedId)> Tags = new List<(EntityTag, Pedestrian, int)>();

        void Update()
        {
            var cam = WorldEffectsB.Eye;
            if (cam == null) return;
            var sprites = EffectSprites.Get();
            var eye = cam.transform.position;
            var blue = new Color(0.55f, 0.82f, 1f, 0.3f);
            foreach (var (tag, ped, pedId) in Tags)
            {
                if (tag == null || !tag.isActiveAndEnabled) continue;
                Vector3 center, forward;
                float halfLength, halfWidth, height;
                if (ped != null)
                {
                    if (ped.Id != pedId) continue;
                    center = ped.Position;
                    forward = Quaternion.Euler(0f, ped.Heading, 0f) * Vector3.forward;
                    halfLength = 0.3f;
                    halfWidth = 0.35f;
                    height = 1.8f;
                }
                else
                {
                    var b = tag.WorldBounds;
                    center = new Vector3(b.center.x, b.min.y, b.center.z);
                    forward = tag.transform.forward;
                    var car = tag.GetComponent<CarController>();
                    var half = car != null ? car.HalfExtents : b.extents;
                    halfLength = half.z;
                    halfWidth = half.x;
                    height = b.size.y;
                }
                if ((center - eye).sqrMagnitude > 140f * 140f) continue;
                if (Faster) sprites.Stripes(center, forward, halfLength, halfWidth, height, 2.5f + halfLength * 1.5f);
                else sprites.Bubble(center + Vector3.up * height * 0.5f, Mathf.Max(halfLength, height * 0.5f) * 1.35f * (1f + 0.04f * Mathf.Sin(Time.time * 2f)), blue);
            }
        }
    }
}
