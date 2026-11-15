using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Low-poly flames sized to what is burning: flickering vertex-coloured spikes and, for the
    // nearest fires, a warm point light.
    public sealed class FireFx : MonoBehaviour
    {
        Transform[] tongues;
        float[] seeds;
        Light glow;

        public static FireFx Create(Material material, Bounds burning, float size, bool light)
        {
            var go = new GameObject("Fire");
            go.transform.position = new Vector3(burning.center.x, burning.max.y - burning.size.y * 0.25f, burning.center.z);
            var fx = go.AddComponent<FireFx>();
            float radius = Mathf.Clamp(Mathf.Max(burning.extents.x, burning.extents.z), 0.4f, 8f);
            float height = Mathf.Clamp(burning.size.y * 0.5f, 1.2f, 9f) * size;
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 4, 14);
            var mesh = Spike();
            fx.tongues = new Transform[count];
            fx.seeds = new float[count];
            var rng = new System.Random(go.GetInstanceID());
            for (int i = 0; i < count; i++)
            {
                var t = new GameObject("Flame").transform;
                t.SetParent(go.transform, false);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * radius * 0.8f;
                t.localPosition = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float w = Mathf.Lerp(0.5f, 1.2f, (float)rng.NextDouble()) * Mathf.Max(radius * 0.45f, 0.4f) * size;
                t.localScale = new Vector3(w, height * Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble()), w);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = t.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fx.tongues[i] = t;
                fx.seeds[i] = (float)rng.NextDouble() * 10f;
            }
            if (light)
            {
                fx.glow = go.AddComponent<Light>();
                fx.glow.type = LightType.Point;
                fx.glow.color = new Color(1f, 0.55f, 0.2f);
                fx.glow.range = radius * 4f + 8f;
                fx.glow.intensity = 2.5f;
            }
            return fx;
        }

        static Mesh spike;

        static Mesh Spike()
        {
            if (spike != null) return spike;
            var mb = new MeshBuilder();
            var red = PaintJob.Linear("#E8401C");
            var orange = PaintJob.Linear("#FF8A1E");
            var yellow = PaintJob.Linear("#FFD23F");
            var top = new Vector3(0f, 1f, 0f);
            var ring = new Vector3[5];
            for (int i = 0; i < 5; i++) ring[i] = new Vector3(Mathf.Cos(i * Mathf.PI * 0.4f) * 0.5f, 0f, Mathf.Sin(i * Mathf.PI * 0.4f) * 0.5f);
            for (int i = 0; i < 5; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 5];
                var mid = (a + b) * 0.5f;
                mb.Triangle(a, b, top, i % 2 == 0 ? orange : red, new Vector3(mid.x, 0.3f, mid.z));
                mb.Triangle(a * 0.5f + Vector3.up * 0.1f, b * 0.5f + Vector3.up * 0.1f, top * 0.75f, yellow, new Vector3(mid.x, 0.3f, mid.z));
            }
            spike = mb.ToMesh("flame");
            return spike;
        }

        void Update()
        {
            for (int i = 0; i < tongues.Length; i++)
            {
                var t = tongues[i];
                float f = Mathf.PerlinNoise(Time.time * 4f + seeds[i], seeds[i]);
                var s = t.localScale;
                t.localScale = new Vector3(s.x, Mathf.Lerp(s.y, s.y * (0.85f + f * 0.3f), 0.2f), s.z);
                t.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 7f + seeds[i]) * 6f, Time.time * 40f + seeds[i] * 30f, 0f);
            }
            if (glow != null) glow.intensity = 2f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 1.5f;
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
