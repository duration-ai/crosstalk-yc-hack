using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.Smash;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Low-poly flames sized to what is burning: tongues of layered flat-shaded cones (a wide yellow
    // base, orange body, red tips) that flicker, lean and lick upward, rising embers and dark smoke
    // from the pooled SmashFx particles (no extra draw calls), and, for the nearest fires, a
    // flickering orange light. A fire that Burns hurts the car it sits on and any racer that drives
    // through it, through VehicleDamage.Burn (rates in Resources/Smash/damage.json).
    public sealed class FireFx : MonoBehaviour
    {
        const float BurnTick = 0.25f;
        const float ParticleRange = 110f;
        static readonly Color Ember = new Color(1f, 0.62f, 0.12f);
        static readonly Color EmberHot = new Color(1f, 0.86f, 0.3f);
        static readonly Color FireSmoke = new Color(0.16f, 0.15f, 0.16f);

        public bool Burns;

        Transform[] tongues;
        Vector3[] rest;
        float[] seeds;
        Light glow;
        float radius;
        float height;
        float born;
        bool rich;
        float nextEmber;
        float nextSmoke;
        float lastBurn;
        bool resolved;
        VehicleDamage carried;

        public static FireFx Create(Material material, Bounds burning, float size, bool light)
        {
            var go = new GameObject("Fire");
            go.transform.position = new Vector3(burning.center.x, burning.max.y - burning.size.y * 0.25f, burning.center.z);
            var fx = go.AddComponent<FireFx>();
            float radius = Mathf.Clamp(Mathf.Max(burning.extents.x, burning.extents.z), 0.4f, 8f);
            float height = Mathf.Clamp(burning.size.y * 0.6f, 1.6f, 9f) * size;
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 6, 14);
            fx.radius = radius;
            fx.height = height;
            fx.born = Time.time;
            fx.lastBurn = Time.time;
            fx.rich = light;
            fx.tongues = new Transform[count];
            fx.rest = new Vector3[count];
            fx.seeds = new float[count];
            var rng = new System.Random(go.GetInstanceID());
            for (int i = 0; i < count; i++)
            {
                var t = new GameObject("Flame").transform;
                t.SetParent(go.transform, false);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = i == 0 ? 0f : Mathf.Sqrt((float)rng.NextDouble()) * radius * 0.85f;
                t.localPosition = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float centre = 1f - r / (radius + 0.01f) * 0.35f;
                float w = Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble()) * Mathf.Max(radius * 0.32f, 0.4f) * size;
                fx.rest[i] = new Vector3(w, height * centre * Mathf.Lerp(0.65f, 1.1f, (float)rng.NextDouble()), w);
                t.localScale = Vector3.zero;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = Tongue(i % TongueVariants);
                var renderer = t.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                fx.tongues[i] = t;
                fx.seeds[i] = (float)rng.NextDouble() * 10f;
            }
            if (light)
            {
                fx.glow = go.AddComponent<Light>();
                fx.glow.type = LightType.Point;
                fx.glow.color = new Color(1f, 0.52f, 0.16f);
                fx.glow.range = radius * 4f + 9f;
                fx.glow.intensity = 0f;
                fx.glow.shadows = LightShadows.None;
            }
            return fx;
        }

        const int TongueVariants = 3;
        static readonly Mesh[] tongueMeshes = new Mesh[TongueVariants];

        // One tongue is three staggered six-sided cones in one mesh, so it stays one draw call: a
        // short wide yellow core, a taller orange body, and a thin red tip that pokes out above,
        // each with a slight lean and twist so the bands read as licks of flame, not a cake.
        static Mesh Tongue(int variant)
        {
            if (tongueMeshes[variant] != null) return tongueMeshes[variant];
            var mb = new MeshBuilder();
            var yellow = PaintJob.Linear("#FFD53A");
            var yellowDeep = PaintJob.Linear("#FFB21F");
            var orange = PaintJob.Linear("#FF7A1A");
            var orangeDeep = PaintJob.Linear("#F25C14");
            var red = PaintJob.Linear("#E2361B");
            var redDeep = PaintJob.Linear("#B8241A");
            float lean = (variant - 1) * 0.08f;
            Cone(mb, 0.4f, 0.5f, new Vector3(lean * 0.4f, 0f, 0f), 0f, yellow, yellowDeep);
            Cone(mb, 0.32f, 0.78f, new Vector3(lean, 0f, 0.03f), 25f + variant * 7f, orange, orangeDeep);
            Cone(mb, 0.2f, 1f, new Vector3(lean * 1.8f, 0f, -0.02f), 50f + variant * 11f, red, redDeep);
            return tongueMeshes[variant] = mb.ToMesh("flame-" + variant);
        }

        static void Cone(MeshBuilder mb, float r, float h, Vector3 tipOffset, float twist, Color light, Color dark)
        {
            const int Sides = 6;
            var top = new Vector3(tipOffset.x, h, tipOffset.z);
            for (int i = 0; i < Sides; i++)
            {
                float a0 = (i * 60f + twist) * Mathf.Deg2Rad;
                float a1 = ((i + 1) * 60f + twist) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                var b = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                var mid = (a + b) * 0.5f;
                mb.Triangle(a, b, top, i % 2 == 0 ? light : dark, new Vector3(mid.x, r * 0.6f, mid.z));
            }
        }

        void Update()
        {
            float now = Time.time;
            float grow = Mathf.SmoothStep(0f, 1f, (now - born) / 0.6f);
            for (int i = 0; i < tongues.Length; i++)
            {
                var t = tongues[i];
                float s = seeds[i];
                float f = Mathf.PerlinNoise(now * 3.5f + s, s);
                float lick = Mathf.Max(0f, Mathf.Sin(now * (5f + s) + s * 3f));
                float h = rest[i].y * (0.72f + f * 0.45f + lick * lick * 0.18f);
                float w = rest[i].x * (0.9f + Mathf.PerlinNoise(s, now * 4.5f) * 0.25f);
                t.localScale = new Vector3(w, h, w) * grow;
                t.localRotation = Quaternion.Euler(Mathf.Sin(now * 6.5f + s) * 7f, now * 35f + s * 40f, Mathf.Cos(now * 5.3f + s * 2f) * 7f);
            }
            if (glow != null)
            {
                float flicker = Mathf.PerlinNoise(now * 7f, 0.3f) * 0.7f + Mathf.PerlinNoise(now * 19f, 1.7f) * 0.3f;
                glow.intensity = (1.8f + flicker * 2.2f) * grow;
            }
            Particles(now);
            if (Burns && now - lastBurn >= BurnTick) Burn(now);
        }

        // Embers and smoke from the shared pooled particles, only near the camera, and at a lower
        // rate for the far fires that get no light.
        void Particles(float now)
        {
            var world = SmashWorld.Instance;
            if (world == null || world.Fx == null || now < nextEmber && now < nextSmoke) return;
            var view = world.View;
            var p = transform.position;
            if (view != null && (view.transform.position - p).sqrMagnitude > ParticleRange * ParticleRange) return;
            float rate = rich ? 1f : 0.45f;
            if (now >= nextEmber)
            {
                nextEmber = now + Random.Range(0.05f, 0.14f) / rate;
                var at = p + new Vector3(Random.Range(-radius, radius) * 0.7f, height * Random.Range(0.3f, 0.8f), Random.Range(-radius, radius) * 0.7f);
                var drift = new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
                world.Fx.Smoke(at, Random.value < 0.35f ? EmberHot : Ember, Random.Range(0.28f, 0.42f), 1, Random.Range(3f, 5f), drift, 1.3f);
            }
            if (now >= nextSmoke)
            {
                nextSmoke = now + Random.Range(0.12f, 0.2f) / rate;
                var at = p + new Vector3(Random.Range(-0.3f, 0.3f) * radius, height * 0.85f, Random.Range(-0.3f, 0.3f) * radius);
                world.Fx.Smoke(at, FireSmoke, Mathf.Clamp(radius * 0.6f, 0.6f, 1.8f), 1, 3f, new Vector3(0.5f, 0f, 0.25f), 1.5f);
            }
        }

        // The fire truck drives through flames and can be set alight without a scratch.
        static bool Fireproof(VehicleDamage car) => Vehicles.VehicleRoster.TypeOf(car.Car) == Vehicles.VehicleTypes.FireTruck;

        // Every BurnTick: the car this fire is attached to burns, and any racer whose car is inside
        // the flames' footprint and height takes the drive-through rate. Both go through
        // VehicleDamage.Burn, which never takes a racer below the burn floor.
        void Burn(float now)
        {
            float dt = Mathf.Min(now - lastBurn, 0.5f);
            lastBurn = now;
            if (!resolved)
            {
                carried = GetComponentInParent<VehicleDamage>();
                resolved = true;
            }
            var tuning = DamageTuning.Current;
            if (carried != null && !Fireproof(carried)) carried.Burn(tuning.burnCarPerSecond * dt);
            var c = transform.position;
            float reach = radius + 1.2f;
            var all = VehicleDamage.All;
            for (int i = 0; i < all.Count; i++)
            {
                var car = all[i];
                if (car == carried) continue;
                var p = car.transform.position;
                float dx = p.x - c.x;
                float dz = p.z - c.z;
                if (dx * dx + dz * dz > reach * reach || p.y > c.y + height || p.y + 2.5f < c.y - 1f || !car.Car.Driven || Fireproof(car)) continue;
                car.Burn(tuning.burnSpotPerSecond * dt);
            }
        }
    }

    // A ballistic hop that lands back where it started: up, spin, down. A lifted city piece goes
    // back into its chunk when it lands.
    public sealed class Launched : MonoBehaviour
    {
        Vector3 home;
        Quaternion homeRotation;
        float vy;
        float y;
        float spin;
        MeshSpan span;
        bool copy;

        public static void Start(Transform t, float up, MeshSpan span)
        {
            var existing = t.GetComponent<Launched>();
            if (existing != null) Destroy(existing);
            var l = t.gameObject.AddComponent<Launched>();
            l.home = t.position;
            l.homeRotation = t.rotation;
            l.vy = up;
            l.spin = Random.Range(-360f, 360f);
            l.span = span;
            l.copy = span != null;
            var body = t.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            vy += Physics.gravity.y * dt;
            y += vy * dt;
            if (y <= 0f && vy < 0f)
            {
                transform.SetPositionAndRotation(home, homeRotation);
                if (copy)
                {
                    span.Show();
                    Destroy(gameObject);
                }
                else Destroy(this);
                return;
            }
            transform.position = home + Vector3.up * y;
            transform.rotation = homeRotation * Quaternion.Euler(0f, spin * Mathf.Clamp01(y / 6f), Mathf.Sin(y * 0.4f) * 25f);
        }
    }

    // Keeps a racer's car bouncing while vehicle_hop lasts.
    public sealed class HopTicker : MonoBehaviour
    {
        public List<CarController> Cars;
        public float Up = 5f;
        float next;

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 1.3f;
            foreach (var car in Cars)
                if (car != null && car.Driven) CarFx.On(car).Launch(Up);
        }
    }

    // Holds a vehicle or person still: traffic and bot drivers pause, a rigidbody's velocity is
    // zeroed after the car's own physics step, animation stops.
    [DefaultExecutionOrder(60)]
    public sealed class Frozen : MonoBehaviour
    {
        public float Until = float.MaxValue;
        readonly List<Behaviour> paused = new List<Behaviour>();
        Rigidbody body;
        GlbAnimator animator;
        float animatorSpeed;

        void OnEnable()
        {
            body = GetComponent<Rigidbody>();
            foreach (var b in new Behaviour[] { GetComponent<TrafficCar>(), GetComponent<Race.BotDriver>(), GetComponent<Race.RacerAvatar>() })
                if (b != null && b.enabled)
                {
                    b.enabled = false;
                    paused.Add(b);
                }
            animator = GetComponentInChildren<GlbAnimator>();
            if (animator != null)
            {
                animatorSpeed = animator.Speed;
                animator.Speed = 0f;
            }
        }

        void FixedUpdate()
        {
            if (body != null && !body.isKinematic) body.linearVelocity = new Vector3(0f, Mathf.Min(body.linearVelocity.y, 0f), 0f);
        }

        void Update()
        {
            if (Time.time >= Until) Destroy(this);
        }

        void OnDisable()
        {
            foreach (var b in paused) if (b != null) b.enabled = true;
            paused.Clear();
            if (animator != null) animator.Speed = animatorSpeed;
        }
    }

    // Scales a head bone toward Target and puts it back when Target returns to 1.
    public sealed class BigHead : MonoBehaviour
    {
        public float Target = 2.4f;
        Vector3 rest;
        float size = 1f;

        public static Transform FindHead(Transform root)
        {
            Transform best = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (!n.Contains("head") || n.Contains("light") || n.Contains("end") || n.Contains("top")) continue;
                if (best == null || t.name.Length < best.name.Length) best = t;
            }
            return best;
        }

        void Awake() => rest = transform.localScale;

        void LateUpdate()
        {
            size = Mathf.MoveTowards(size, Target, Time.deltaTime * 4f);
            transform.localScale = rest * size;
            if (Mathf.Approximately(Target, 1f) && Mathf.Approximately(size, 1f)) Destroy(this);
        }
    }
}
