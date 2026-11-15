using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Dogpatch.Operator
{
    // Shared pieces for WorldEffectsA: who the racers are, layered shape changes, guarded knobs,
    // sky grades, hovering cars, swarms of small props and animated ground sheets.

    // A game knob set by one effect and put back only if nobody has written it since, so effects
    // that set the same knob absolutely (MoreEffects) never get stuck by our revert.
    public sealed class Knob
    {
        readonly Func<float> get;
        readonly Action<float> set;
        readonly float before;
        readonly float mine;

        public Knob(Func<float> get, Action<float> set, float value)
        {
            this.get = get;
            this.set = set;
            before = get();
            mine = value;
            set(value);
        }

        public static Knob Times(Func<float> get, Action<float> set, float k) => new Knob(get, set, get() * k);

        public void Undo()
        {
            if (Mathf.Approximately(get(), mine)) set(before);
        }
    }

    // Runs an effect's per-frame work on the Operator's host object while the effect lasts.
    public sealed class EffectTicker : MonoBehaviour
    {
        public Action Tick;
        public Action FixedTick;

        public static EffectTicker On(EffectContext context, Action tick, Action fixedTick = null)
        {
            var ticker = context.Host.gameObject.AddComponent<EffectTicker>();
            ticker.Tick = tick;
            ticker.FixedTick = fixedTick;
            return ticker;
        }

        void Update() => Tick?.Invoke();

        void FixedUpdate() => FixedTick?.Invoke();
    }

    public static class Racers
    {
        // Every racer's car this device draws: yours, and the racers' cars the row resolved to.
        public static List<CarController> Cars(EffectContext context, EffectTarget target)
        {
            var cars = new List<CarController>();
            void Add(CarController car)
            {
                if (car != null && !cars.Contains(car)) cars.Add(car);
            }
            Add(context.Game != null ? context.Game.CurrentCar : null);
            Add(context.Race != null ? context.Race.MyCar : null);
            if (target != null)
                foreach (var tag in target.Entities)
                    if (tag != null) Add(tag.GetComponent<CarController>());
            return cars;
        }

        // The model roots of racers on foot: your character's visual and the racers' avatars.
        public static List<Transform> Bodies(EffectContext context, EffectTarget target)
        {
            var bodies = new List<Transform>();
            var player = context.Game != null ? context.Game.Player : null;
            if (player != null && player.Visual != null) bodies.Add(player.Visual);
            if (target != null)
                foreach (var tag in target.Entities)
                    if (tag != null && tag.GetComponent<Race.RacerAvatar>() != null && !bodies.Contains(tag.transform)) bodies.Add(tag.transform);
            return bodies;
        }

        public static bool IsRacer(CarController car) => car.TryGetComponent<EntityTag>(out var tag) && (tag.ControlledBy == "me" || tag.ControlledBy == "player");

        // Cars that move under physics on this device (yours, local bots): they can really fly.
        public static bool Physical(CarController car) => car != null && car.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic && !car.Autopilot;

        // Cars near a point, nearest first: parked, traffic and remote racers drawn from the net.
        public static List<CarController> Nearby(Vector3 around, float radius, int max)
        {
            var found = new List<(float d, CarController car)>();
            foreach (var car in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                float d = (car.transform.position - around).sqrMagnitude;
                if (d < radius * radius) found.Add((d, car));
            }
            found.Sort((a, b) => a.d.CompareTo(b.d));
            var cars = new List<CarController>();
            for (int i = 0; i < found.Count && i < max; i++) cars.Add(found[i].car);
            return cars;
        }

        // The car body MotionFx squashes: the top-level child of the car holding its first renderer.
        public static Transform CarVisual(CarController car)
        {
            var renderer = car.GetComponentInChildren<MeshRenderer>();
            if (renderer == null) return null;
            var t = renderer.transform;
            while (t.parent != null && t.parent != car.transform) t = t.parent;
            return t.parent == car.transform ? t : null;
        }

        // A node between a model root and its parts that only our effects scale, lift and tilt,
        // so the squash MotionFx and PlayerController write on the root never fights ours. Every
        // part keeps its Transform, so wheels, bones, panels and animation bindings still work.
        public static Transform Shape(Transform holder)
        {
            if (holder == null) return null;
            const string Name = "Shape (operator)";
            var existing = holder.Find(Name);
            if (existing != null) return existing;
            var node = new GameObject(Name).transform;
            node.SetParent(holder, false);
            var parts = new List<Transform>();
            foreach (Transform child in holder)
                if (child != node) parts.Add(child);
            foreach (var part in parts) part.SetParent(node, false);
            return node;
        }

        public static Transform CarShape(CarController car) => Shape(CarVisual(car));

        public static ShapeLayers Layers(Transform t)
        {
            if (t == null) return null;
            return t.TryGetComponent<ShapeLayers>(out var layers) ? layers : t.gameObject.AddComponent<ShapeLayers>();
        }

        // The car's collision box in its own space: where armour, wings and hulls go.
        public static (Vector3 center, Vector3 size) Box(CarController car)
        {
            var box = car.GetComponent<BoxCollider>();
            if (box != null) return (box.center, box.size);
            var e = car.HalfExtents;
            return (new Vector3(0f, e.y, 0f), e * 2f);
        }

        public static int Seed(string id)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id ?? "") h = (h ^ c) * 16777619;
                return (int)(h & 0x7fffffff);
            }
        }

        public static float Hash(float x, float z)
        {
            float h = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        public static float GroundY(Vector3 at, float from = 60f) =>
            Physics.Raycast(new Vector3(at.x, at.y + from, at.z), Vector3.down, out var hit, from + 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
    }

    // Combines every effect's scale, lift and tilt on one shape node: scales multiply and spring
    // in with a wobble, lifts take the highest, tilts compose. Empty again, it eases home and goes.
    public sealed class ShapeLayers : MonoBehaviour
    {
        readonly Dictionary<object, Vector3> scales = new Dictionary<object, Vector3>();
        readonly Dictionary<object, Func<float>> lifts = new Dictionary<object, Func<float>>();
        readonly Dictionary<object, Func<Quaternion>> tilts = new Dictionary<object, Func<Quaternion>>();
        Vector3 restScale, restPosition;
        Quaternion restRotation;
        Vector3 scale = Vector3.one, scaleVelocity;
        float lift;
        public float Pivot = 0.8f;

        void Awake()
        {
            restScale = transform.localScale;
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        public void Scale(object key, Vector3 factor) => scales[key] = factor;

        public void Lift(object key, Func<float> height) => lifts[key] = height;

        public void Tilt(object key, Func<Quaternion> tilt) => tilts[key] = tilt;

        public void Remove(object key)
        {
            scales.Remove(key);
            lifts.Remove(key);
            tilts.Remove(key);
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var target = Vector3.one;
            foreach (var s in scales.Values) target = Vector3.Scale(target, s);
            scaleVelocity += ((target - scale) * 260f - scaleVelocity * 14f) * dt;
            scale += scaleVelocity * dt;

            float high = 0f;
            foreach (var l in lifts.Values) high = Mathf.Max(high, l());
            lift = Mathf.Lerp(lift, high, 1f - Mathf.Exp(-5f * dt));

            var tilt = Quaternion.identity;
            foreach (var t in tilts.Values) tilt *= t();

            bool empty = scales.Count == 0 && lifts.Count == 0 && tilts.Count == 0;
            if (empty && (scale - Vector3.one).sqrMagnitude < 1e-5f && scaleVelocity.sqrMagnitude < 1e-4f && lift < 0.01f)
            {
                transform.localScale = restScale;
                transform.localPosition = restPosition;
                transform.localRotation = restRotation;
                Destroy(this);
                return;
            }
            var pivot = new Vector3(0f, Pivot, 0f);
            transform.localScale = Vector3.Scale(restScale, scale);
            transform.localRotation = restRotation * tilt;
            transform.localPosition = restPosition + Vector3.up * lift + pivot - tilt * pivot;
        }
    }

    // Pops a prop in with an overshoot, and out again before it is destroyed.
    public sealed class Pop : MonoBehaviour
    {
        float t;
        bool leaving;
        Vector3 full;

        public static Pop In(GameObject go)
        {
            var pop = go.AddComponent<Pop>();
            pop.full = go.transform.localScale;
            go.transform.localScale = Vector3.one * 0.001f;
            return pop;
        }

        public static void Out(GameObject go)
        {
            if (go == null) return;
            if (go.TryGetComponent<Pop>(out var pop)) pop.Leave();
            else Destroy(go);
        }

        public void Leave()
        {
            leaving = true;
            enabled = true;
            t = 0f;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (leaving)
            {
                float k = 1f - Mathf.Clamp01(t / 0.25f);
                transform.localScale = full * Mathf.Max(k, 0.001f);
                if (k <= 0f) Destroy(gameObject);
                return;
            }
            float u = Mathf.Clamp01(t / 0.45f);
            float s = 1f + Mathf.Sin(u * Mathf.PI * 2.5f) * (1f - u) * 0.35f;
            transform.localScale = full * Mathf.Max(u < 0.2f ? u / 0.2f : s, 0.001f);
            if (u >= 1f)
            {
                transform.localScale = full;
                enabled = false;
            }
        }
    }

    // Small meshes built in code for the effects: flat-shaded, vertex-coloured, linear colours.
    public static class PropMeshes
    {
        public static Color C(string hex) => SpeedDial.Hex(hex).linear;

        public static GameObject Attach(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        // A faceted ball: an octahedron split once, each vertex pushed out by a seeded jitter.
        public static void Ball(MeshBuilder mb, Vector3 center, float radius, Color a, Color b, int seed, float jitter = 0.15f)
        {
            var rng = new System.Random(seed);
            Vector3[] p = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[,] faces = { { 0, 3, 4 }, { 0, 4, 2 }, { 0, 2, 5 }, { 0, 5, 3 }, { 1, 4, 3 }, { 1, 2, 4 }, { 1, 5, 2 }, { 1, 3, 5 } };
            var cache = new Dictionary<Vector3, Vector3>();
            Vector3 J(Vector3 v)
            {
                v = v.normalized;
                if (!cache.TryGetValue(v, out var j)) cache[v] = j = v * (1f + ((float)rng.NextDouble() - 0.5f) * 2f * jitter);
                return center + j * radius;
            }
            int n = 0;
            for (int f = 0; f < 8; f++)
            {
                var v0 = p[faces[f, 0]];
                var v1 = p[faces[f, 1]];
                var v2 = p[faces[f, 2]];
                var m01 = (v0 + v1) * 0.5f;
                var m12 = (v1 + v2) * 0.5f;
                var m20 = (v2 + v0) * 0.5f;
                foreach (var (x, y, z) in new[] { (v0, m01, m20), (m01, v1, m12), (m20, m12, v2), (m01, m12, m20) })
                    mb.Triangle(J(x), J(y), J(z), n++ % 3 == 0 ? b : a, (x + y + z).normalized);
            }
        }

        public static Mesh Wings((Vector3 center, Vector3 size) box)
        {
            var mb = new MeshBuilder();
            var white = C("#F7F8F9");
            var tip = C("#FFCB1F");
            float y = box.center.y + box.size.y * 0.1f;
            float hx = box.size.x * 0.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                var root = new Vector3(box.center.x + side * hx * 0.9f, y, box.center.z);
                var span = new Vector3(side * 2.1f, 0.35f, 0f);
                float chord = box.size.z * 0.32f;
                Vector3 a = root + new Vector3(0f, 0f, chord), b = root + new Vector3(0f, 0f, -chord * 0.6f);
                Vector3 c = root + span + new Vector3(0f, 0f, -chord * 0.9f), d = root + span + new Vector3(0f, 0f, chord * 0.1f);
                var down = Vector3.down * 0.07f;
                mb.Quad(a, d, c, b, white, Vector3.up);
                mb.Quad(a + down, b + down, c + down, d + down, white, Vector3.down);
                mb.Quad(d, d + down, c + down, c, tip, new Vector3(side, 0f, 0f));
                mb.Quad(a, a + down, d + down, d, white, Vector3.forward);
            }
            return mb.ToMesh("wings");
        }

        public static Mesh Armour((Vector3 center, Vector3 size) box)
        {
            var mb = new MeshBuilder();
            var steel = C("#8C93A6");
            var dark = C("#4B5368");
            var stripe = C("#FFCB1F");
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            float low = box.center.y - box.size.y * 0.5f + 0.05f;
            float h = box.size.y * 0.55f;
            foreach (float side in new[] { -1f, 1f })
            {
                mb.Box(new Vector3(box.center.x + side * (hx + 0.06f), low + h * 0.5f, box.center.z), new Vector3(0.1f, h, hz * 1.55f), Quaternion.identity, steel);
                for (int i = -2; i <= 2; i++)
                    mb.Box(new Vector3(box.center.x + side * (hx + 0.12f), low + h * 0.8f, box.center.z + i * hz * 0.32f), new Vector3(0.05f, 0.08f, 0.08f), Quaternion.identity, stripe);
            }
            float ramZ = box.center.z + hz + 0.18f;
            mb.Box(new Vector3(box.center.x, low + 0.3f, ramZ), new Vector3(box.size.x * 1.08f, 0.32f, 0.22f), Quaternion.identity, dark);
            for (int i = 0; i < 4; i++)
                mb.Box(new Vector3(box.center.x - hx + (i + 0.5f) * box.size.x / 4f, low + 0.3f, ramZ + 0.12f), new Vector3(box.size.x / 8f, 0.3f, 0.04f), Quaternion.Euler(0f, 0f, 35f), stripe);
            mb.Box(new Vector3(box.center.x, low + 0.3f, box.center.z - hz - 0.12f), new Vector3(box.size.x * 1.02f, 0.26f, 0.14f), Quaternion.identity, dark);
            mb.Box(new Vector3(box.center.x, box.center.y + box.size.y * 0.5f + 0.05f, box.center.z - hz * 0.1f), new Vector3(box.size.x * 0.7f, 0.08f, hz * 0.8f), Quaternion.identity, steel);
            return mb.ToMesh("armour");
        }

        public static Mesh Helmet()
        {
            var mb = new MeshBuilder();
            Ball(mb, new Vector3(0f, 0.06f, 0f), 0.2f, C("#8C93A6"), C("#C0C4CC"), 7, 0.05f);
            mb.Box(new Vector3(0f, 0.27f, 0f), new Vector3(0.05f, 0.08f, 0.32f), Quaternion.identity, C("#FFCB1F"));
            mb.Box(new Vector3(0f, -0.02f, 0.17f), new Vector3(0.3f, 0.06f, 0.06f), Quaternion.identity, C("#4B5368"));
            return mb.ToMesh("helmet");
        }

        public static Mesh Cape()
        {
            var mb = new MeshBuilder();
            var top = C("#2D8CFF");
            var hem = C("#1C2347");
            Vector3 a = new Vector3(-0.22f, 0f, 0f), b = new Vector3(0.22f, 0f, 0f), c = new Vector3(0.34f, -0.9f, 0f), d = new Vector3(-0.34f, -0.9f, 0f);
            var tail = new Vector3(0f, -1.02f, 0f);
            mb.Quad(a, b, c, d, top, Vector3.back);
            mb.Quad(a, d, c, b, top, Vector3.forward);
            mb.Triangle(d, c, tail, hem, Vector3.back);
            mb.Triangle(d, tail, c, hem, Vector3.forward);
            return mb.ToMesh("cape");
        }

        // A boat hull under a car: an ink rim, white topsides, a red bottom and a pointed bow,
        // reaching down to the bay's surface when the car rides at street level.
        public static Mesh Hull((Vector3 center, Vector3 size) box)
        {
            var mb = new MeshBuilder();
            var white = C("#F4F4F0");
            var red = C("#E8333A");
            var ink = C("#1C2347");
            float hx = box.size.x * 0.5f + 0.18f, hz = box.size.z * 0.5f + 0.15f;
            float top = box.center.y - box.size.y * 0.5f + 0.42f;
            var o = new Vector3(box.center.x, 0f, box.center.z);
            Vector3[] rim = { new Vector3(-hx, top, -hz), new Vector3(hx, top, -hz), new Vector3(hx, top, hz * 0.7f), new Vector3(0f, top, hz + 0.6f), new Vector3(-hx, top, hz * 0.7f) };
            Vector3 Down(Vector3 v, float y, float k) => o + new Vector3(v.x * k, y, v.z * (k + (1f - k) * 0.6f));
            for (int i = 0; i < rim.Length; i++)
            {
                var a = rim[i];
                var b = rim[(i + 1) % rim.Length];
                var outward = new Vector3((a + b).x, 0f, (a + b).z);
                Vector3 sa = Down(a, -0.1f, 0.85f), sb = Down(b, -0.1f, 0.85f), ka = Down(a, -0.75f, 0.45f), kb = Down(b, -0.75f, 0.45f);
                mb.Quad(o + a + Vector3.up * 0.1f, o + b + Vector3.up * 0.1f, o + b, o + a, ink, outward);
                mb.Quad(o + a, o + b, sb, sa, white, outward);
                mb.Quad(sa, sb, kb, ka, red, outward + Vector3.down);
            }
            return mb.ToMesh("hull");
        }

        public static Mesh Balloons()
        {
            var mb = new MeshBuilder();
            string[] colors = { "#2D8CFF", "#A35BFF", "#FF8A1E", "#2EC27E" };
            var ink = C("#1C2347");
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                var p = new Vector3(Mathf.Cos(a) * 0.45f, 1.7f + (i % 2) * 0.35f, Mathf.Sin(a) * 0.45f);
                Ball(mb, p, 0.38f, C(colors[i]), C(colors[i]) * 0.8f, 30 + i, 0.08f);
                mb.Box(p * 0.5f + Vector3.down * 0.05f, new Vector3(0.02f, p.magnitude - 0.3f, 0.02f), Quaternion.FromToRotation(Vector3.up, p.normalized), ink);
            }
            return mb.ToMesh("balloons");
        }

        public static Mesh Duck()
        {
            var mb = new MeshBuilder();
            var yellow = C("#FFCB1F");
            mb.Box(new Vector3(0f, 0.18f, 0f), new Vector3(0.5f, 0.32f, 0.7f), Quaternion.identity, yellow);
            mb.Box(new Vector3(0f, 0.46f, 0.22f), new Vector3(0.3f, 0.3f, 0.3f), Quaternion.identity, yellow);
            mb.Box(new Vector3(0f, 0.42f, 0.43f), new Vector3(0.18f, 0.08f, 0.16f), Quaternion.identity, C("#FF8A1E"));
            mb.Box(new Vector3(0f, 0.3f, -0.38f), new Vector3(0.2f, 0.16f, 0.12f), Quaternion.Euler(-30f, 0f, 0f), yellow);
            mb.Box(new Vector3(0.155f, 0.52f, 0.28f), new Vector3(0.02f, 0.06f, 0.06f), Quaternion.identity, C("#1C2347"));
            mb.Box(new Vector3(-0.155f, 0.52f, 0.28f), new Vector3(0.02f, 0.06f, 0.06f), Quaternion.identity, C("#1C2347"));
            return mb.ToMesh("duck");
        }

        public static Mesh Tumbleweed(int seed)
        {
            var mb = new MeshBuilder();
            Ball(mb, Vector3.zero, 0.7f, C("#B98A4E"), C("#8E6436"), seed, 0.35f);
            Ball(mb, new Vector3(0.1f, 0.05f, -0.08f), 0.55f, C("#D4A464"), C("#A47440"), seed + 1, 0.4f);
            return mb.ToMesh("tumbleweed");
        }

        public static Mesh Bubble(Color color)
        {
            var mb = new MeshBuilder();
            Ball(mb, Vector3.zero, 1f, color, new Color(color.r, color.g, color.b, color.a * 0.6f), 3, 0.05f);
            return mb.ToMesh("bubble");
        }

        // A thin wavy ribbon of hot air, standing up.
        public static Mesh Wisp(Color color)
        {
            var mb = new MeshBuilder();
            var faint = new Color(color.r, color.g, color.b, 0f);
            for (int i = 0; i < 6; i++)
            {
                float y0 = i * 0.5f, y1 = (i + 1) * 0.5f;
                float x0 = Mathf.Sin(i * 1.3f) * 0.25f, x1 = Mathf.Sin((i + 1) * 1.3f) * 0.25f;
                var c0 = Color.Lerp(color, faint, i / 6f);
                var c1 = Color.Lerp(color, faint, (i + 1) / 6f);
                mb.Triangle(new Vector3(x0 - 0.3f, y0, 0f), new Vector3(x0 + 0.3f, y0, 0f), new Vector3(x1 + 0.3f, y1, 0f), c0, Vector3.back);
                mb.Triangle(new Vector3(x0 - 0.3f, y0, 0f), new Vector3(x1 + 0.3f, y1, 0f), new Vector3(x1 - 0.3f, y1, 0f), c1, Vector3.back);
            }
            return mb.ToMesh("wisp");
        }

        public static Mesh Crack(int seed, float length)
        {
            var mb = new MeshBuilder();
            var rng = new System.Random(seed);
            var dark = C("#2B1E1A");
            var rim = C("#8A6A50");
            const int n = 7;
            var prev = new Vector3(-length * 0.5f, 0f, 0f);
            for (int i = 1; i <= n; i++)
            {
                var next = new Vector3(-length * 0.5f + length * i / n, 0f, ((float)rng.NextDouble() - 0.5f) * 0.9f);
                float w = 0.25f + 0.35f * Mathf.Sin(Mathf.PI * i / n);
                var side = Vector3.Cross(Vector3.up, (next - prev).normalized) * w;
                var up = Vector3.up * 0.02f;
                mb.Quad(prev - side * 0.6f, prev + side * 0.6f, next + side * 0.6f, next - side * 0.6f, dark, Vector3.up);
                mb.Quad(prev + side * 0.6f + up, prev + side * 1.3f, next + side * 1.3f, next + side * 0.6f + up, rim, Vector3.up);
                mb.Quad(prev - side * 1.3f, prev - side * 0.6f + up, next - side * 0.6f + up, next - side * 1.3f, rim, Vector3.up);
                prev = next;
            }
            return mb.ToMesh("crack");
        }
    }

    // Keeps a physics car flying at a height above whatever is below and just ahead of it,
    // through CarFx's airtime (a floor on vertical speed each step), and lands it gently after.
    [DefaultExecutionOrder(40)]
    public sealed class Hover : MonoBehaviour
    {
        readonly Dictionary<object, float> heights = new Dictionary<object, float>();
        readonly RaycastHit[] hits = new RaycastHit[8];
        CarController car;
        Rigidbody body;
        float landingUntil;
        float seed;

        public static Hover On(CarController car) => car.TryGetComponent<Hover>(out var h) ? h : car.gameObject.AddComponent<Hover>();

        public void Set(object key, float height) => heights[key] = height;

        public void Remove(object key)
        {
            if (heights.Remove(key) && heights.Count == 0) landingUntil = Time.time + 5f;
        }

        void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();
            seed = (GetInstanceID() & 1023) * 0.01f;
        }

        float Below(Vector3 at, float from)
        {
            float best = float.NegativeInfinity;
            int n = Physics.RaycastNonAlloc(new Vector3(at.x, from, at.z), Vector3.down, hits, from + 50f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].rigidbody != body && hits[i].collider.attachedRigidbody == null) best = Mathf.Max(best, hits[i].point.y);
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        void FixedUpdate()
        {
            if (body.isKinematic || car.Autopilot) return;
            var fx = CarFx.On(car);
            var p = body.position;
            if (heights.Count == 0)
            {
                bool high = !Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 1.1f, ~0, QueryTriggerInteraction.Ignore);
                if (high && Time.time < landingUntil) fx.Launch(-4.5f);
                else Destroy(this);
                return;
            }
            float h = 0f;
            foreach (var v in heights.Values) h = Mathf.Max(h, v);
            var planar = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            float top = p.y + h + 40f;
            float ground = Mathf.Max(Below(p, top), Below(p + planar * 0.9f, top));
            float target = ground + h + Mathf.Sin(Time.time * 1.7f + seed) * 0.4f;
            fx.Launch(Mathf.Clamp((target - p.y) * 2.2f, -6f, 10f));
        }
    }

    // Thick air or deep water: a drag on a physics car's planar speed, after its own step.
    [DefaultExecutionOrder(60)]
    public sealed class Syrup : MonoBehaviour
    {
        readonly Dictionary<object, Func<float>> drags = new Dictionary<object, Func<float>>();
        Rigidbody body;

        public static Syrup On(CarController car) => car.TryGetComponent<Syrup>(out var s) ? s : car.gameObject.AddComponent<Syrup>();

        public void Set(object key, Func<float> drag) => drags[key] = drag;

        public void Remove(object key)
        {
            drags.Remove(key);
            if (drags.Count == 0) Destroy(this);
        }

        void Awake() => body = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            if (body.isKinematic) return;
            float c = 0f;
            foreach (var d in drags.Values) c += d();
            if (c <= 0f) return;
            var v = body.linearVelocity;
            var planar = new Vector3(v.x, 0f, v.z);
            body.linearVelocity = v - planar * Mathf.Clamp01(c * Time.fixedDeltaTime);
        }
    }

    // Many copies of one small mesh drawn as one mesh, posed every frame by a callback.
    public sealed class Swarm : MonoBehaviour
    {
        Mesh mesh;
        Vector3[] source;
        Vector3[] posed;
        int count;
        public Func<int, Matrix4x4> Pose;

        public static Swarm Create(string name, Mesh template, int count, Material material, Func<int, Matrix4x4> pose, bool shadows = false)
        {
            var go = new GameObject(name);
            var swarm = go.AddComponent<Swarm>();
            swarm.count = count;
            swarm.Pose = pose;
            swarm.source = template.vertices;
            var colors = template.colors;
            var tris = template.triangles;
            int v = swarm.source.Length;
            var allColors = new Color[v * count];
            var allTris = new int[tris.Length * count];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(colors, 0, allColors, i * v, v);
                for (int t = 0; t < tris.Length; t++) allTris[i * tris.Length + t] = tris[t] + i * v;
            }
            swarm.posed = new Vector3[v * count];
            swarm.mesh = new Mesh { name = name, indexFormat = v * count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            swarm.mesh.MarkDynamic();
            swarm.mesh.vertices = swarm.posed;
            swarm.mesh.colors = allColors;
            swarm.mesh.triangles = allTris;
            swarm.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            go.AddComponent<MeshFilter>().sharedMesh = swarm.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            swarm.LateUpdate();
            return swarm;
        }

        void LateUpdate()
        {
            int v = source.Length;
            for (int i = 0; i < count; i++)
            {
                var m = Pose(i);
                for (int j = 0; j < v; j++) posed[i * v + j] = m.MultiplyPoint3x4(source[j]);
            }
            mesh.vertices = posed;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }

    // A wavy sheet over the ground around the camera (flood water, lava): a grid that follows the
    // camera in whole cells, its level easing toward Target. Sink() lowers it out of sight and
    // destroys it.
    public sealed class Sheet : MonoBehaviour
    {
        const int N = 64;
        const float Cell = 9f;

        public float Level;
        public float Target;
        public float Speed = 0.6f;
        public float Amplitude = 0.12f;
        public bool FadeEdges;
        public Func<float, float, Color> Paint;
        public Transform Eye;
        public Transform Floor;

        Mesh mesh;
        Vector3[] vertices;
        Color[] colors;
        Vector2Int origin = new Vector2Int(int.MinValue, int.MinValue);
        float bottom;
        bool retiring;

        public static Sheet Create(string name, Material material, Transform eye, float from, float to, Func<float, float, Color> paint)
        {
            var go = new GameObject(name);
            var sheet = go.AddComponent<Sheet>();
            sheet.Eye = eye;
            sheet.Level = from;
            sheet.bottom = from;
            sheet.Target = to;
            sheet.Paint = paint;
            sheet.vertices = new Vector3[(N + 1) * (N + 1)];
            sheet.colors = new Color[sheet.vertices.Length];
            var tris = new int[N * N * 6];
            int k = 0;
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int i = z * (N + 1) + x;
                    if (((x + z) & 1) == 0)
                    {
                        tris[k++] = i; tris[k++] = i + N + 1; tris[k++] = i + N + 2;
                        tris[k++] = i; tris[k++] = i + N + 2; tris[k++] = i + 1;
                    }
                    else
                    {
                        tris[k++] = i; tris[k++] = i + N + 1; tris[k++] = i + 1;
                        tris[k++] = i + 1; tris[k++] = i + N + 1; tris[k++] = i + N + 2;
                    }
                }
            sheet.mesh = new Mesh { name = name };
            sheet.mesh.MarkDynamic();
            sheet.mesh.vertices = sheet.vertices;
            sheet.mesh.colors = sheet.colors;
            sheet.mesh.triangles = tris;
            sheet.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            go.AddComponent<MeshFilter>().sharedMesh = sheet.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            sheet.LateUpdate();
            return sheet;
        }

        public bool Retiring => retiring;

        public void Sink()
        {
            retiring = true;
            Target = bottom;
        }

        void LateUpdate()
        {
            Level = Mathf.MoveTowards(Level, Target, Speed * Time.deltaTime);
            if (retiring && Level <= bottom + 0.001f)
            {
                Destroy(gameObject);
                return;
            }
            var eye = Eye != null ? Eye.position : Vector3.zero;
            var at = new Vector2Int(Mathf.FloorToInt(eye.x / Cell) - N / 2, Mathf.FloorToInt(eye.z / Cell) - N / 2);
            bool moved = at != origin;
            origin = at;
            float t = Time.time;
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                {
                    int i = z * (N + 1) + x;
                    float wx = (origin.x + x) * Cell, wz = (origin.y + z) * Cell;
                    float jitter = Racers.Hash(wx, wz);
                    float wave = Mathf.Sin(wx * 0.23f + t * 1.4f + jitter * 3f) + Mathf.Sin(wz * 0.19f - t * 1.1f);
                    vertices[i] = new Vector3(wx + (jitter - 0.5f) * Cell * 0.5f, Level + wave * Amplitude * 0.5f, wz + (Racers.Hash(wz, wx) - 0.5f) * Cell * 0.5f);
                    if (!moved) continue;
                    var c = Paint(wx, wz);
                    if (FadeEdges) c.a *= Mathf.Clamp01(Mathf.Min(Mathf.Min(x, N - x), Mathf.Min(z, N - z)) / (N * 0.25f));
                    colors[i] = c;
                }
            mesh.vertices = vertices;
            if (moved) mesh.colors = colors;
            if (Floor != null) Floor.position = new Vector3(eye.x, Level - 0.1f, eye.z);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }

    // Colour grades layered on the sky after EnvironmentRig has set this frame's values: sky
    // gradient, clouds, ambient, sun, fog and the night glow of windows and lamps. Each grade
    // blends in and out over a second or two; nothing accumulates because the rig rewrites the
    // base values every frame. No post-processing: only globals the shared shaders already read.
    [DefaultExecutionOrder(1300)]
    public sealed class SkyGrade : MonoBehaviour
    {
        public sealed class Grade
        {
            public Color Top, Mid, Horizon;
            public float Sky = 1f;
            public Color Ambient = Color.white;
            public Color Sun = Color.white;
            public float SunScale = 1f;
            public float FogReach = 1f;
            public Color Fog;
            public float FogWeight;
            public Color Clouds = Color.white;
            public float Glow = 1f;
            public Vector4? Wind;
            public Func<float> Flicker;
            public float Seconds = 1.5f;
            public float Weight;
            public float Target = 1f;
        }

        static SkyGrade instance;
        static readonly List<Grade> grades = new List<Grade>();
        static readonly int TopId = Shader.PropertyToID("_SkyTop"), MidId = Shader.PropertyToID("_SkyMid"), HorizonId = Shader.PropertyToID("_SkyHorizon");
        static readonly int SkyAmbientId = Shader.PropertyToID("_SkyAmbient"), SideAmbientId = Shader.PropertyToID("_SideAmbient"), GroundAmbientId = Shader.PropertyToID("_GroundAmbient");
        static readonly int NightId = Shader.PropertyToID("_DogpatchNight"), WindId = Shader.PropertyToID("_DogpatchWind");

        Light sun;
        SkyRig sky;
        Camera view;

        public static int Active => grades.Count;

        public static Grade Add(Grade grade)
        {
            if (instance == null) instance = new GameObject("SkyGrade (operator)").AddComponent<SkyGrade>();
            grades.Add(grade);
            return grade;
        }

        public static void Remove(Grade grade)
        {
            if (grade != null) grade.Target = 0f;
        }

        static Color Lerp(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = grades.Count - 1; i >= 0; i--)
            {
                var g = grades[i];
                g.Weight = Mathf.MoveTowards(g.Weight, g.Target, dt / Mathf.Max(g.Seconds, 0.05f));
                if (g.Weight <= 0f && g.Target <= 0f) grades.RemoveAt(i);
            }
            if (grades.Count == 0) return;
            if (sun == null) sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && (sun == null || l.intensity > sun.intensity)) sun = l;
            if (sky == null) sky = FindAnyObjectByType<SkyRig>();
            if (view == null)
            {
                var game = FindAnyObjectByType<GameController>();
                view = game != null ? game.View : Camera.main;
            }

            Color top = Shader.GetGlobalColor(TopId), mid = Shader.GetGlobalColor(MidId), horizon = Shader.GetGlobalColor(HorizonId);
            Color skyAmb = Shader.GetGlobalColor(SkyAmbientId), sideAmb = Shader.GetGlobalColor(SideAmbientId), groundAmb = Shader.GetGlobalColor(GroundAmbientId);
            float glow = Shader.GetGlobalFloat(NightId);
            var fog = RenderSettings.fogColor;
            float reach = 1f, sunScale = 1f;
            var sunTint = Color.white;
            var clouds = sky != null ? sky.Props.GetColor("_Tint") : Color.white;
            Vector4? wind = null;
            bool lights = false;
            foreach (var g in grades)
            {
                float w = Mathf.SmoothStep(0f, 1f, g.Weight);
                if (g.Flicker != null) w *= g.Flicker();
                top = Lerp(top, g.Top, w * g.Sky);
                mid = Lerp(mid, g.Mid, w * g.Sky);
                horizon = Lerp(horizon, g.Horizon, w * g.Sky);
                var amb = Lerp(Color.white, g.Ambient, w);
                skyAmb *= amb;
                sideAmb *= amb;
                groundAmb *= amb;
                sunTint *= Lerp(Color.white, g.Sun, w);
                sunScale *= Mathf.Lerp(1f, g.SunScale, w);
                reach *= Mathf.Lerp(1f, g.FogReach, w);
                fog = Lerp(fog, g.Fog.gamma, w * g.FogWeight);
                clouds *= Lerp(Color.white, g.Clouds, w);
                glow *= Mathf.Lerp(1f, g.Glow, w);
                if (g.Wind.HasValue && w > 0.01f) wind = Vector4.Lerp(Shader.GetGlobalVector(WindId), g.Wind.Value, w);
                lights |= g.Glow < 0.5f && w > 0.5f;
            }
            Shader.SetGlobalColor(TopId, top);
            Shader.SetGlobalColor(MidId, mid);
            Shader.SetGlobalColor(HorizonId, horizon);
            Shader.SetGlobalColor(SkyAmbientId, skyAmb);
            Shader.SetGlobalColor(SideAmbientId, sideAmb);
            Shader.SetGlobalColor(GroundAmbientId, groundAmb);
            Shader.SetGlobalFloat(NightId, glow);
            if (wind.HasValue) Shader.SetGlobalVector(WindId, wind.Value);
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance *= reach;
            RenderSettings.fogEndDistance *= reach;
            if (view != null) view.backgroundColor = fog;
            if (sky != null) sky.Props.SetColor("_Tint", clouds);
            if (sun != null)
            {
                sun.intensity *= sunScale;
                sun.color *= sunTint;
            }
            if (lights) QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 2);
        }
    }

    // Slow motion or fast forward: holds Time.timeScale at Scale whenever something else (the
    // pause menu's resume) has put it back to 1, leaves pauses and HitStop alone, and scales the
    // physics step with it so the step count per real second stays the same.
    public sealed class TimeWarp : MonoBehaviour
    {
        static float baseStep = -1f;
        public float Scale = 1f;

        public static TimeWarp Begin(GameObject host, float scale)
        {
            if (baseStep < 0f) baseStep = Time.fixedDeltaTime / Mathf.Max(Time.timeScale, 0.01f);
            var warp = host.AddComponent<TimeWarp>();
            warp.Scale = scale;
            warp.Update();
            return warp;
        }

        void Update()
        {
            if (Mathf.Abs(Time.timeScale - 1f) < 0.01f) Time.timeScale = Scale;
            Time.fixedDeltaTime = Mathf.Approximately(Time.timeScale, Scale) ? baseStep * Scale : baseStep;
        }

        public void End()
        {
            if (Mathf.Approximately(Time.timeScale, Scale)) Time.timeScale = 1f;
            if (baseStep > 0f) Time.fixedDeltaTime = baseStep;
            Destroy(this);
        }
    }
}
