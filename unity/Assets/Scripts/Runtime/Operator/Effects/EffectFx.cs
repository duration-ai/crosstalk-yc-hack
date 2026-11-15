using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Particle fire sized to what is burning: faceted flame puffs that rise, taper and cool from
    // yellow to red, sparks drifting up, dark smoke above, and for the nearest fires a flickering
    // warm light. Explosion adds a one-off fireball blast. Extinguish lets the flames die out.
    public sealed class FireFx : MonoBehaviour
    {
        static Material glow;
        static Mesh flame;
        static Mesh spark;
        static Mesh tongue;

        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        Light flicker;
        float lightPeak;
        float lightFloor;
        float flashUntil;
        float stopAt = float.MaxValue;
        float seed;

        public IReadOnlyList<ParticleSystem> Systems => systems;

        public static FireFx Create(Material material, Bounds burning, float size, bool withLight)
        {
            float radius = Mathf.Clamp(Mathf.Max(burning.extents.x, burning.extents.z), 0.4f, 8f);
            float height = Mathf.Clamp(burning.size.y, 0.6f, 14f);
            float k = Mathf.Sqrt(Mathf.Max(radius, height * 0.4f)) * size;
            var go = new GameObject("Fire");
            go.transform.position = new Vector3(burning.center.x, burning.max.y - height * 0.6f * 0.35f, burning.center.z);
            var fx = go.AddComponent<FireFx>();
            fx.seed = Random.value * 10f;
            var box = new Vector3(Mathf.Max(burning.size.x * 0.75f, 0.35f), Mathf.Max(height * 0.6f, 0.3f), Mathf.Max(burning.size.z * 0.75f, 0.35f));
            float s = 0.55f * k;
            float rise = Mathf.Sqrt(k);

            var core = fx.Add("Core", Tongue(), Glow(), 400, new Vector3(box.x * 0.8f, box.y * 0.35f, box.z * 0.8f), Vector3.down * (box.y * 0.2f));
            var main = core.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f * s, 1.1f * s);
            Velocity(core, 0.15f * rise, 0.6f * rise, 1.4f * rise);
            Size(core, new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f));
            Colors(core, new[] { (0f, "#FFFFE0"), (0.4f, "#FFE680"), (1f, "#FFB52E") }, 0.7f);
            Upright(core, 120f);
            Rate(core, Mathf.Clamp(28f * k * k, 24f, 200f));

            var flames = fx.Add("Flames", Tongue(), Glow(), 800, box, Vector3.zero);
            main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.55f * s, 0.9f * s);
            main.startSizeY = new ParticleSystem.MinMaxCurve(1f * s, 1.6f * s);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.55f * s, 0.9f * s);
            Velocity(flames, 0.25f * rise, 1.3f * rise, 2.5f * rise);
            Size(flames, new Keyframe(0f, 0.4f), new Keyframe(0.18f, 1f), new Keyframe(0.55f, 0.65f), new Keyframe(1f, 0f));
            Colors(flames, new[] { (0f, "#FFE680"), (0.3f, "#FFA126"), (0.6f, "#F25A16"), (1f, "#B8220E") }, 0.75f);
            Upright(flames, 200f);
            Noise(flames, 0.25f * rise, 1.6f);
            Rate(flames, Mathf.Clamp(45f * k * k, 40f, 320f));

            var embers = fx.Add("Embers", Spark(), Glow(), 200, box, Vector3.zero);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * rise, 0.15f * rise);
            Velocity(embers, 0.9f, 1.8f * rise, 4f * rise);
            Size(embers, new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            Colors(embers, new[] { (0f, "#FFF6C0"), (0.5f, "#FFA52A"), (1f, "#FF4A12") }, 0.7f);
            Spin(embers, 400f);
            Noise(embers, 1.4f, 2.2f);
            Rate(embers, Mathf.Clamp(7f * k * k, 6f, 70f));

            var smoke = fx.Add("Smoke", Flame(), material, 120, new Vector3(box.x * 0.5f, box.y * 0.15f, box.z * 0.5f), Vector3.up * (box.y * 0.5f + 2.4f * s));
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * s, 1.3f * s);
            Velocity(smoke, 0.35f * rise, 1f * rise, 1.8f * rise);
            Size(smoke, new Keyframe(0f, 0.2f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.05f));
            Colors(smoke, new[] { (0f, "#4A4444"), (0.5f, "#6E6A6A"), (1f, "#A3A1A1") }, 2f);
            Spin(smoke, 50f);
            Noise(smoke, 0.5f, 0.6f);
            Rate(smoke, Mathf.Clamp(2.5f * k * k, 2.5f, 20f));
            smoke.GetComponent<ParticleSystemRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (withLight)
            {
                fx.flicker = go.AddComponent<Light>();
                fx.flicker.type = LightType.Point;
                fx.flicker.color = new Color(1f, 0.55f, 0.2f);
                fx.flicker.range = radius * 4f + 10f;
                fx.lightFloor = 2.2f;
                fx.lightPeak = 3.6f;
                fx.flicker.intensity = fx.lightFloor;
            }
            foreach (var ps in fx.systems) ps.Play();
            return fx;
        }

        // A fireball: flames and sparks blasted outwards with a bright flash, burning on briefly
        // and dying out, then the whole thing cleans itself up.
        public static FireFx Explosion(Material material, Bounds at, float size, bool withLight)
        {
            var fx = Create(material, at, size, withLight);
            float k = Mathf.Sqrt(Mathf.Clamp(Mathf.Max(at.extents.x, at.extents.z, at.extents.y), 0.5f, 8f)) * size;

            var blast = fx.Add("Blast", Flame(), Glow(), 300, Vector3.zero, Vector3.zero);
            var shape = blast.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f * k;
            shape.scale = Vector3.one;
            var main = blast.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f * k, 11f * k);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * k, 1f * k);
            var limit = blast.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.5f;
            limit.dampen = 0.12f;
            Size(blast, new Keyframe(0f, 0.4f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
            Colors(blast, new[] { (0f, "#FFFFFF"), (0.15f, "#FFE58A"), (0.45f, "#FF8A1E"), (1f, "#B0200E") }, 0.8f);
            Spin(blast, 220f);
            Burst(blast, Mathf.RoundToInt(Mathf.Clamp(70f * k, 50f, 220f)));

            var sparks = fx.Add("Blast sparks", Spark(), Glow(), 200, Vector3.zero, Vector3.zero);
            shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f * k;
            shape.scale = Vector3.one;
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f * k, 18f * k);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.gravityModifier = 1.2f;
            Colors(sparks, new[] { (0f, "#FFF6C0"), (0.6f, "#FFA52A"), (1f, "#FF4A12") }, 0.7f);
            Spin(sparks, 600f);
            Burst(sparks, Mathf.RoundToInt(Mathf.Clamp(50f * k, 40f, 160f)));

            var puffs = fx.Add("Blast smoke", Flame(), material, 120, Vector3.zero, Vector3.up * k);
            shape = puffs.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.8f * k;
            shape.scale = Vector3.one;
            main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * k, 5f * k);
            main.startSize = new ParticleSystem.MinMaxCurve(0.55f * k, 1f * k);
            limit = puffs.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.8f;
            limit.dampen = 0.08f;
            main.gravityModifier = -0.12f;
            Size(puffs, new Keyframe(0f, 0.4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.05f));
            Colors(puffs, new[] { (0f, "#4A4040"), (0.5f, "#6E6A6A"), (1f, "#A3A1A1") }, 2f);
            Spin(puffs, 60f);
            Burst(puffs, Mathf.RoundToInt(Mathf.Clamp(10f * k, 8f, 30f)));
            foreach (var ps in fx.systems) if (!ps.isPlaying) ps.Play();

            if (fx.flicker != null)
            {
                fx.flicker.range *= 1.6f;
                fx.flashUntil = Time.time + 0.35f;
            }
            fx.Extinguish(1.4f);
            return fx;
        }

        // Stops feeding the flames after a delay; what is still burning dies out and the fire removes itself.
        public void Extinguish(float after = 0f)
        {
            stopAt = Time.time + after;
            if (after <= 0f) Stop();
        }

        void Stop()
        {
            stopAt = float.MinValue;
            foreach (var ps in systems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (transform.parent != null) transform.SetParent(null, true);
            Destroy(gameObject, 4f);
        }

        void Update()
        {
            if (Time.time >= stopAt && stopAt != float.MinValue) Stop();
            if (flicker == null) return;
            float fade = stopAt == float.MinValue ? Mathf.Max(0f, flicker.intensity - Time.deltaTime * 2f) : -1f;
            if (Time.time < flashUntil) flicker.intensity = 9f;
            else if (fade >= 0f) flicker.intensity = fade;
            else flicker.intensity = Mathf.Lerp(lightFloor, lightPeak, Mathf.PerlinNoise(Time.time * 7f, seed));
        }

        ParticleSystem Add(string name, Mesh mesh, Material material, int max, Vector3 box, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            systems.Add(ps);
            return ps;
        }

        static void Velocity(ParticleSystem ps, float sideways, float upMin, float upMax)
        {
            var v = ps.velocityOverLifetime;
            v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-sideways, sideways);
            v.y = new ParticleSystem.MinMaxCurve(upMin, upMax);
            v.z = new ParticleSystem.MinMaxCurve(-sideways, sideways);
        }

        static void Size(ParticleSystem ps, params Keyframe[] keys)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        // Colours are sRGB hex; alpha holds until fadeFrom of the lifetime, then fades out (opaque
        // smoke passes 2 to skip the fade and shrinks away instead).
        static void Colors(ParticleSystem ps, (float t, string hex)[] stops, float fadeFrom)
        {
            var keys = new GradientColorKey[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                ColorUtility.TryParseHtmlString(stops[i].hex, out var c);
                keys[i] = new GradientColorKey(c, stops[i].t);
            }
            var alpha = fadeFrom > 1f
                ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeFrom), new GradientAlphaKey(0f, 1f) };
            var g = new Gradient();
            g.SetKeys(keys, alpha);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = g;
        }

        static void Spin(ParticleSystem ps, float degrees)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            float r = degrees * Mathf.Deg2Rad;
            rot.x = new ParticleSystem.MinMaxCurve(-r * 0.4f, r * 0.4f);
            rot.y = new ParticleSystem.MinMaxCurve(-r, r);
            rot.z = new ParticleSystem.MinMaxCurve(-r * 0.4f, r * 0.4f);
        }

        // Flame tongues stay pointing up and only turn about the vertical.
        static void Upright(ParticleSystem ps, float degrees)
        {
            var main = ps.main;
            main.startRotationX = 0f;
            main.startRotationZ = 0f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = 0f;
            rot.z = 0f;
            rot.y = new ParticleSystem.MinMaxCurve(-degrees * Mathf.Deg2Rad, degrees * Mathf.Deg2Rad);
        }

        static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.8f;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        static void Rate(ParticleSystem ps, float perSecond)
        {
            var emission = ps.emission;
            emission.rateOverTime = perSecond;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            var main = ps.main;
            main.loop = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static Material Glow()
        {
            if (glow == null) glow = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Fire glow" };
            return glow;
        }

        static Mesh Flame() => flame != null ? flame : flame = Shaded(Smash.SmashFx.Icosahedron(), "flame puff");

        static Mesh Tongue()
        {
            if (tongue != null) return tongue;
            var mesh = Object.Instantiate(Smash.SmashFx.Icosahedron());
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++)
                if (v[i].y > 0.01f) v[i] = new Vector3(v[i].x * 0.65f, v[i].y * 2f, v[i].z * 0.65f);
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return tongue = Shaded(mesh, "flame tongue");
        }

        static Mesh Spark() => spark != null ? spark : spark = Shaded(Smash.SmashFx.Tetra(), "ember");

        // Bakes a per-face brightness into the white vertex colours so unlit flames still read as
        // faceted: faces turned up and towards the light are brightest. 8-bit colours, as the particle
        // mesh path needs.
        static Mesh Shaded(Mesh source, string name)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = name;
            var v = mesh.vertices;
            var tris = mesh.triangles;
            var colors = new Color32[v.Length];
            var light = new Vector3(0.35f, 1f, -0.25f).normalized;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var n = Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]).normalized;
                byte b = (byte)(255f * Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(Vector3.Dot(n, light) * 0.5f + 0.5f)));
                for (int j = 0; j < 3; j++) colors[tris[i + j]] = new Color32(b, b, b, 255);
            }
            mesh.colors32 = colors;
            return mesh;
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
