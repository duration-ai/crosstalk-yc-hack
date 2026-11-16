using System;
using System.Collections.Generic;
using Dogpatch.Ambient;
using Dogpatch.Gameplay;
using Dogpatch.Smash;
using Dogpatch.WorldChanges;
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
            r.Register("extinguish", () => new ExtinguishEffect());
            foreach (var kind in new[] { "see_through", "bouncy", "icy", "rock_solid", "soft", "ghost" }) r.Register(kind, () => new MorphEffect(kind));
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

    // Every AI driver floors it (up to about 2x cruise), tailgates bumper to bumper and shunts whoever is
    // ahead at the top strength, runs every light, swerves across its lane and leans on the horn, with
    // a red anger mark bobbing over each nearby car and its brake lights strobing.
    public sealed class RoadRageEffect : OperatorEffect
    {
        static readonly Knob Rage = new Knob();
        float k;

        public override void Apply(string strength, EffectTarget target)
        {
            k = Scale(strength, 1f, 1.4f, 1.8f);
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
        readonly Dictionary<TrafficAgent, List<GameObject>> glows = new Dictionary<TrafficAgent, List<GameObject>>();

        void Update()
        {
            if (TrafficAgent.Rage <= 0f || TrafficSystem.Instance == null)
            {
                foreach (var lights in glows.Values)
                    foreach (var g in lights) if (g != null) g.SetActive(false);
                glows.Clear();
                return;
            }
            var cam = WorldEffectsB.Eye;
            if (cam == null) return;
            var sprites = EffectSprites.Get();
            var eye = cam.transform.position;
            bool strobe = Mathf.Repeat(Time.time * 5f, 1f) < 0.5f;
            foreach (var a in TrafficSystem.Instance.Agents)
            {
                if (a.OnRail || a.Car == null || (a.transform.position - eye).sqrMagnitude > 90f * 90f) continue;
                float bob = Mathf.Abs(Mathf.Sin(Time.time * 6f + a.GetInstanceID()));
                var top = a.transform.position + Vector3.up * (a.Car.HalfExtents.y * 2f + 0.9f + bob * 0.3f);
                sprites.Mark(EffectSprites.Icon.Anger, top, 0.8f + 0.2f * TrafficAgent.Rage + bob * 0.15f);
                if ((a.transform.position - eye).sqrMagnitude < 45f * 45f) a.Car.GetComponent<VehicleAudio>()?.Signal(UnityEngine.Random.Range(1.5f, 3.5f));
                if (!glows.TryGetValue(a, out var lights))
                {
                    lights = new List<GameObject>();
                    foreach (var t in a.Car.GetComponentsInChildren<Transform>(true)) if (t.name == "brake-glow") lights.Add(t.gameObject);
                    glows[a] = lights;
                }
                foreach (var g in lights) if (g != null) g.SetActive(strobe);
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

// One object's stacked Operator looks and motions (see_through, bouncy, icy, rock_solid, soft,
    // ghost): the last colour mix wins, the lowest alpha and strongest rim win, hops, jiggles and
    // bobs take the largest. Cars move their visual children so the AI and physics keep the body;
    // a lifted-out city piece moves as a whole from its base. Everything goes back when the last
    // mod is removed.
    public sealed class Morph : MonoBehaviour
    {
        public sealed class Mod
        {
            public Color Mix;
            public float Amount;
            public float Alpha = 1f;
            public float Rim;
            public float Hop;
            public float Jiggle;
            public float Bob;
        }

        static Shader shader;

        readonly List<Mod> mods = new List<Mod>();
        Renderer[] renderers;
        Material[][] materials;
        UnityEngine.Rendering.ShadowCastingMode[] shadows;
        Transform[] parts;
        Vector3[] restPosition;
        Vector3[] restScale;
        Material material;
        float seed;

        public static Morph On(GameObject go)
        {
            var m = go.GetComponent<Morph>();
            if (m != null) return m;
            m = go.AddComponent<Morph>();
            m.Init();
            return m;
        }

        void Init()
        {
            seed = UnityEngine.Random.value * 10f;
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && r.GetComponentInParent<FireFx>() == null) list.Add(r);
            renderers = list.ToArray();
            materials = new Material[renderers.Length][];
            shadows = new UnityEngine.Rendering.ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                materials[i] = renderers[i].sharedMaterials;
                shadows[i] = renderers[i].shadowCastingMode;
            }
            var moving = new List<Transform>();
            if (GetComponent<Rigidbody>() != null || GetComponent<CarController>() != null)
            {
                foreach (Transform child in transform)
                    if (child.GetComponentInChildren<Renderer>() != null && child.GetComponentInChildren<FireFx>() == null) moving.Add(child);
            }
            else moving.Add(transform);
            parts = moving.ToArray();
            restPosition = new Vector3[parts.Length];
            restScale = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                restPosition[i] = parts[i].localPosition;
                restScale[i] = parts[i].localScale;
            }
        }

        public void Add(Mod mod)
        {
            mods.Add(mod);
            Restyle();
        }

        public void Remove(Mod mod)
        {
            mods.Remove(mod);
            if (mods.Count > 0)
            {
                Restyle();
                return;
            }
            Destroy(this);
        }

        void Restyle()
        {
            Mod mix = null;
            float alpha = 1f, rim = 0f;
            foreach (var m in mods)
            {
                if (m.Amount > 0f) mix = m;
                alpha = Mathf.Min(alpha, m.Alpha);
                rim = Mathf.Max(rim, m.Rim);
            }
            if (mix == null && alpha >= 0.999f && rim <= 0f)
            {
                RestoreLook();
                return;
            }
            if (shader == null) shader = Resources.Load<Shader>("Shaders/FlatMorph");
            if (material == null) material = new Material(shader) { name = "Morph" };
            var c = mix != null ? mix.Mix : Color.white;
            material.SetColor("_Mix", new Color(c.r, c.g, c.b, mix != null ? mix.Amount : 0f));
            material.SetFloat("_Alpha", alpha);
            material.SetFloat("_Rim", rim);
            bool clear = alpha < 0.999f;
            material.SetFloat("_SrcBlend", clear ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", clear ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", clear ? 0f : 1f);
            material.renderQueue = clear ? 3000 : 2000;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var set = new Material[materials[i].Length];
                for (int k = 0; k < set.Length; k++) set[k] = material;
                r.sharedMaterials = set;
                r.shadowCastingMode = clear ? UnityEngine.Rendering.ShadowCastingMode.Off : shadows[i];
            }
        }

        void RestoreLook()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].sharedMaterials = materials[i];
                renderers[i].shadowCastingMode = shadows[i];
            }
        }

        void LateUpdate()
        {
            float hop = 0f, jiggle = 0f, bob = 0f;
            foreach (var m in mods)
            {
                hop = Mathf.Max(hop, m.Hop);
                jiggle = Mathf.Max(jiggle, m.Jiggle);
                bob = Mathf.Max(bob, m.Bob);
            }
            float t = Time.time + seed;
            float u = Mathf.Abs(Mathf.Sin(t * Mathf.PI / 0.75f));
            float y = hop * u + bob * (0.6f + 0.4f * Mathf.Sin(t * 1.7f));
            float sy = 1f;
            if (hop > 0f) sy = u < 0.2f ? 1f - 0.3f * (1f - u / 0.2f) : 1f + 0.12f * u;
            if (jiggle > 0f) sy *= 1f + jiggle * Mathf.Sin(t * 10f) * (0.7f + 0.3f * Mathf.Sin(t * 2.3f));
            float sw = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
            float sway = bob > 0f ? Mathf.Sin(t * 1.3f) * 4f : 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                parts[i].localPosition = restPosition[i] + (parts[i].parent != null ? parts[i].parent.InverseTransformVector(Vector3.up) : Vector3.up) * y;
                parts[i].localScale = Vector3.Scale(restScale[i], new Vector3(sw, sy, sw));
                if (parts[i] == transform && bob > 0f) transform.rotation = Quaternion.Euler(sway, transform.eulerAngles.y, sway * 0.6f);
            }
        }

        void OnDestroy()
        {
            RestoreLook();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                parts[i].localPosition = restPosition[i];
                parts[i].localScale = restScale[i];
            }
            if (material != null) Destroy(material);
        }
    }

    // A city piece lifted out of its merged chunk while any object effect holds it; its own
    // collider stays on (unbreakable while lifted) unless a ghost holds it.
    public static class SpanCopies
    {
        sealed class Held
        {
            public GameObject Copy;
            public int Refs;
            public int Ghosts;
        }

        static readonly Dictionary<EntityTag, Held> held = new Dictionary<EntityTag, Held>();

        public static GameObject Take(EntityTag tag, Material material, bool ghost)
        {
            if (!held.TryGetValue(tag, out var h))
            {
                var copy = tag.Span.Detach(tag.Id + " (operator)", material);
                if (copy == null) return null;
                held[tag] = h = new Held { Copy = copy };
            }
            h.Refs++;
            if (ghost) h.Ghosts++;
            Solid(tag, h.Ghosts == 0);
            return h.Copy;
        }

        public static void Give(EntityTag tag, bool ghost)
        {
            if (!held.TryGetValue(tag, out var h)) return;
            if (ghost) h.Ghosts--;
            if (--h.Refs > 0)
            {
                Solid(tag, h.Ghosts == 0);
                return;
            }
            held.Remove(tag);
            if (h.Copy != null) Object.Destroy(h.Copy);
            tag.Span.Show();
        }

        static void Solid(EntityTag tag, bool solid)
        {
            foreach (var part in tag.Span.Parts)
                if (part.Collider != null) part.Collider.enabled = solid;
        }
    }

    // A ghost car clips through everything but the floor: buildings, cars, traffic, props, trees,
    // viaduct columns and people. Only streets, sidewalks, ground, freeway decks and ramps (the
    // Drivable layer), the seabed, drivable water and the map edge stay solid. When the ghost ends it
    // takes the car out of anything it is still inside.
    public sealed class GhostPass : MonoBehaviour
    {
        readonly List<(Collider a, Collider b)> ignored = new List<(Collider, Collider)>();
        readonly HashSet<Collider> skipped = new HashSet<Collider>();
        Collider[] mine;
        float next;

        void Awake() => mine = GetComponentsInChildren<Collider>();

        static bool IsFloor(Collider c)
        {
            if (c.gameObject.layer == World.Freeway.DrivableLayer) return true;
            var n = c.name;
            return n.StartsWith("Asphalt collider") || n.StartsWith("Sidewalks collider") || n.StartsWith("Ground collider")
                || n == "Seabed" || n == "MapEdge" || n.StartsWith("Drivable water") || n == "Ramp";
        }

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.2f;
            foreach (var hit in Physics.OverlapSphere(transform.position, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (skipped.Contains(hit) || hit.transform.IsChildOf(transform)) continue;
                skipped.Add(hit);
                if (IsFloor(hit)) continue;
                foreach (var c in mine)
                {
                    Physics.IgnoreCollision(c, hit, true);
                    ignored.Add((c, hit));
                }
            }
        }

        void OnDestroy()
        {
            foreach (var (a, b) in ignored)
                if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
            if (this != null && gameObject != null && gameObject.activeInHierarchy) PushOut();
        }

        // Out of a building footprint first (merged building meshes are hollow, so a car fully inside
        // one touches nothing), then out of whatever it still overlaps.
        void PushOut()
        {
            var box = GetComponent<BoxCollider>();
            var body = GetComponent<Rigidbody>();
            if (box == null) return;
            var start = transform.position;
            if (InsideBuilding(start))
            {
                for (float r = 6f; r <= 90f; r += 6f)
                {
                    bool found = false;
                    for (int i = 0; i < 12 && !found; i++)
                    {
                        var dir = Quaternion.Euler(0f, i * 30f, 0f) * transform.forward;
                        var at = start + dir * r;
                        if (InsideBuilding(at) || !FloorBelow(at, out var floor)) continue;
                        transform.position = floor + Vector3.up * 0.3f;
                        found = true;
                    }
                    if (found) break;
                }
            }
            for (int step = 0; step < 6; step++)
            {
                bool moved = false;
                var bounds = box.bounds;
                foreach (var hit in Physics.OverlapBox(bounds.center, bounds.extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit == box || hit.transform.IsChildOf(transform) || IsFloor(hit)) continue;
                    if (!Physics.ComputePenetration(box, box.transform.position, box.transform.rotation, hit, hit.transform.position, hit.transform.rotation, out var dir, out var dist)) continue;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    transform.position += dir.normalized * (dist + 0.1f);
                    moved = true;
                }
                if (!moved) break;
            }
            if (body != null && transform.position != start)
            {
                body.position = transform.position;
                Debug.Log($"[Operator] ghost ended inside something: moved the car {Vector3.Distance(start, transform.position):F1} m to clear ground");
            }
        }

        static bool InsideBuilding(Vector3 at)
        {
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * 120f, Vector3.down, 119f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.name.StartsWith("BuildingCollider") && hit.point.y > at.y + 1f) return true;
            return false;
        }

        static bool FloorBelow(Vector3 at, out Vector3 floor)
        {
            floor = at;
            if (!Physics.Raycast(at + Vector3.up * 60f, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore) || !IsFloor(hit.collider)) return false;
            floor = hit.point;
            return true;
        }
    }

    // A ghost building is part of a merged building collider, so instead of switching it off this
    // lets every moving body (cars, the player) inside the building's footprint through that
    // collider, and puts it back for each one as it leaves.
    public sealed class GhostZone : MonoBehaviour
    {
        public Bounds Area;
        readonly List<Collider> walls = new List<Collider>();
        readonly HashSet<Collider> passing = new HashSet<Collider>();
        readonly List<Collider> gone = new List<Collider>();
        readonly HashSet<Collider> seen = new HashSet<Collider>();

        void Start()
        {
            foreach (var c in Physics.OverlapBox(Area.center, Area.extents * 0.6f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (c.name.StartsWith("BuildingCollider")) walls.Add(c);
        }

        void FixedUpdate()
        {
            if (walls.Count == 0) return;
            seen.Clear();
            foreach (var c in Physics.OverlapBox(Area.center, Area.extents + new Vector3(3f, 1f, 3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                bool moving = c is CharacterController || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic);
                if (!moving) continue;
                seen.Add(c);
                if (!passing.Add(c)) continue;
                foreach (var w in walls) Physics.IgnoreCollision(w, c, true);
            }
            gone.Clear();
            foreach (var c in passing) if (c == null || !seen.Contains(c)) gone.Add(c);
            foreach (var c in gone)
            {
                passing.Remove(c);
                if (c != null) foreach (var w in walls) Physics.IgnoreCollision(w, c, false);
            }
        }

        void OnDestroy()
        {
            foreach (var c in passing)
                if (c != null) foreach (var w in walls) if (w != null) Physics.IgnoreCollision(w, c, false);
        }
    }

    // see_through, bouncy, icy, rock_solid, soft and ghost on objects: a look, a motion and the
    // physics to match. Pedestrians (drawn by the crowd renderer) take the look on their clothes,
    // and stone ones stand still as statues.
    public sealed class MorphEffect : OperatorEffect
    {
        const int MaxTargets = 40;

        static PhysicsMaterial ice, rubber, jelly;

        readonly string kind;
        readonly List<Action> undo = new List<Action>();

        public MorphEffect(string kind) => this.kind = kind;

        Morph.Mod Mod(float k) => kind switch
        {
            "see_through" => new Morph.Mod { Mix = WorldEffectsB.Hex("#9ED7FF"), Amount = 0.35f, Alpha = Mathf.Lerp(0.5f, 0.2f, k), Rim = 0.5f },
            "ghost" => new Morph.Mod { Mix = new Color(0.92f, 0.95f, 1f), Amount = 0.75f, Alpha = Mathf.Lerp(0.55f, 0.3f, k), Rim = 0.45f, Bob = 0.6f * k },
            "icy" => new Morph.Mod { Mix = WorldEffectsB.Hex("#CFEFFF"), Amount = 0.45f + 0.3f * k, Rim = 0.55f },
            "rock_solid" => new Morph.Mod { Mix = WorldEffectsB.Hex("#8E9099"), Amount = 0.7f + 0.25f * k },
            "soft" => new Morph.Mod { Mix = WorldEffectsB.Hex("#8CE3BF"), Amount = 0.35f, Alpha = 0.8f, Rim = 0.3f, Jiggle = 0.1f + 0.12f * k },
            _ => new Morph.Mod { Mix = WorldEffectsB.Hex("#FF9C86"), Amount = 0.2f, Hop = 0.5f + 1f * k },
        };

        Color PuffColor => kind switch
        {
            "see_through" => WorldEffectsB.Hex("#9ED7FF"),
            "icy" => WorldEffectsB.Hex("#CFEFFF"),
            "rock_solid" => SmashFx.SmokeGrey,
            "soft" => WorldEffectsB.Hex("#8CE3BF"),
            "bouncy" => WorldEffectsB.Hex("#FF9C86"),
            _ => SmashFx.SmokeLight,
        };

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 0.4f, 0.7f, 1f);
            bool isGhost = kind == "ghost";
            int n = 0;
            foreach (var tag in target.Entities)
            {
                if (n >= MaxTargets) break;
                var ped = tag.GetComponent<Pedestrian>();
                if (ped != null)
                {
                    var m = Mod(k);
                    PedestrianSystem.Instance?.AddLook(ped, m.Mix, Mathf.Max(m.Amount, 0.5f), kind == "rock_solid");
                    undo.Add(() => PedestrianSystem.Instance?.RemoveLook(ped, kind == "rock_solid"));
                    CrowdFx.Ensure(Context);
                    WorldEffectsB.Poof(ped.Position + Vector3.up, 1.2f, PuffColor, 6);
                    n++;
                    continue;
                }
                var body = tag.Span != null ? SpanCopies.Take(tag, Context.Material, isGhost) : tag.gameObject;
                if (body == null) continue;
                n++;
                if (tag.Span != null) undo.Add(() => SpanCopies.Give(tag, isGhost));
                if (isGhost && tag.Class == "building")
                {
                    var zone = body.AddComponent<GhostZone>();
                    zone.Area = tag.WorldBounds;
                    undo.Add(() => { if (zone != null) Object.Destroy(zone); });
                }
                var morph = Morph.On(body);
                var mod = Mod(k);
                morph.Add(mod);
                undo.Add(() => { if (morph != null) morph.Remove(mod); });
                var b = tag.WorldBounds;
                WorldEffectsB.Poof(b.center, Mathf.Clamp(b.extents.magnitude, 1.5f, 8f), PuffColor, 10);
                if (tag.Span == null) Physics(tag.gameObject, k);
            }
        }

        void Physics(GameObject go, float k)
        {
            var car = go.GetComponent<CarController>();
            var body = go.GetComponent<Rigidbody>();
            var colliders = go.GetComponentsInChildren<Collider>();
            void Swap(PhysicsMaterial with)
            {
                foreach (var c in colliders)
                {
                    if (c.isTrigger) continue;
                    var was = c.sharedMaterial;
                    c.sharedMaterial = with;
                    undo.Add(() => { if (c != null) c.sharedMaterial = was; });
                }
            }
            switch (kind)
            {
                case "icy":
                    if (ice == null) ice = new PhysicsMaterial("Operator ice") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                    Swap(ice);
                    break;
                case "bouncy":
                    if (rubber == null) rubber = new PhysicsMaterial("Operator rubber") { bounciness = 1f, bounceCombine = PhysicsMaterialCombine.Maximum };
                    Swap(rubber);
                    if (car != null && body != null && !body.isKinematic)
                    {
                        var ticker = go.AddComponent<HopTicker>();
                        ticker.Cars = new List<CarController> { car };
                        ticker.Up = 3f + 4f * k;
                        undo.Add(() => { if (ticker != null) Object.Destroy(ticker); });
                    }
                    break;
                case "soft":
                    if (jelly == null) jelly = new PhysicsMaterial("Operator jelly") { bounciness = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum, dynamicFriction = 0.9f, staticFriction = 0.9f };
                    Swap(jelly);
                    break;
                case "rock_solid":
                    var damage = go.GetComponent<VehicleDamage>();
                    if (damage != null)
                    {
                        damage.Invulnerable++;
                        undo.Add(() => { if (damage != null) damage.Invulnerable--; });
                    }
                    if (body != null)
                    {
                        float mass = body.mass;
                        body.mass = mass * 5f;
                        undo.Add(() => { if (body != null) body.mass = mass; });
                    }
                    if (go.GetComponent<TrafficAgent>() is TrafficAgent agent && agent.enabled)
                    {
                        var frozen = go.AddComponent<Frozen>();
                        undo.Add(() => { if (frozen != null) Object.Destroy(frozen); });
                    }
                    break;
                case "ghost":
                    if (body == null || body.isKinematic)
                    {
                        foreach (var c in colliders)
                        {
                            if (!c.enabled || c.isTrigger) continue;
                            c.enabled = false;
                            undo.Add(() => { if (c != null) c.enabled = true; });
                        }
                    }
                    else
                    {
                        var pass = go.AddComponent<GhostPass>();
                        undo.Add(() => { if (pass != null) Object.Destroy(pass); });
                    }
                    break;
            }
        }

        public override void Revert()
        {
            for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
            undo.Clear();
        }
    }

    // Instant: a gush of water and a cloud of steam on each target, and any flames on it go out.
    public sealed class ExtinguishEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target)
        {
            var fires = Object.FindObjectsByType<FireFx>(FindObjectsSortMode.None);
            var fx = SmashWorld.Instance?.Fx;
            int n = 0;
            foreach (var tag in target.Entities)
            {
                if (n++ >= 12) break;
                var b = tag.WorldBounds;
                b.Expand(4f);
                foreach (var fire in fires)
                    if (fire != null && b.Contains(fire.transform.position)) Object.Destroy(fire.gameObject);
                var top = new Vector3(b.center.x, b.max.y - 3f, b.center.z);
                fx?.Spout(new Vector3(b.center.x, b.min.y + 2f, b.center.z), 1.2f);
                fx?.Smoke(top, SmashFx.SmokeLight, Mathf.Clamp(b.extents.magnitude, 2f, 6f), 14, 2.5f);
                WorldEffectsB.Poof(top, Mathf.Clamp(b.extents.magnitude, 2f, 8f), WorldEffectsB.Hex("#9ED7FF"), 12);
            }
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
