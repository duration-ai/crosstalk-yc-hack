using System;
using System.Collections.Generic;
using Dogpatch.Audio;
using Dogpatch.Feel;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Speed Dial motion feel, styled on the reference art: chunky faceted puffs behind the rear
    // wheels on launches, drifts and hard braking, tire marks, a ring of puffs and shards on car
    // landings, sparks on scrapes, wind streaks at speed, and squash and stretch on the car body
    // for jumps. It watches the player and the car you drive and fires these itself; other systems
    // (camera, speed lines, haptics, audio) read SpeedFactor, Rush, BoostFactor and Lateral or listen
    // to the static events, and anything can call the triggers. Every effect is a fixed pool rebuilt
    // into one dynamic mesh: four draw calls in total. Tunables are in MotionTuning
    // (Resources/Look/motion.json); EffectsSettings.Intensity scales counts, sizes and opacity.
    public sealed class MotionFx : MonoBehaviour
    {
        const int MaxPuffs = 64;
        const int MaxSoftPuffs = 96;
        const int MaxSparks = 96;
        const int MaxStreaks = 32;
        const int MaxMarks = 600;
        const float MarkWidth = 0.2f;

        public static event Action<Vector3, float> Landed;
        public static event Action<Vector3> Stepped;
        public static event Action<Vector3, Vector3, float> Scraped;
        public static event Action<float> Boosted;
        public static event Action<Vector3, float> Splashed;

        // 0 at a crawl, 1 at the driven car's own top speed.
        public static float SpeedFactor { get; private set; }

        // 1 right after a boost, decaying to 0 over MotionTuning.speed.boostSeconds.
        public static float BoostFactor { get; private set; }

        // 0 to 1: how much the drive should feel like rushing. Speed past rushStart, plus boost and
        // going downhill. Drives the speed lines and the wind streaks.
        public static float Rush { get; private set; }

        // Signed sideways acceleration of the driven car in m/s², positive when turning right.
        public static float Lateral { get; private set; }

        public GameController Game;

        static MotionFx instance;

        static readonly Color Dust = SpeedDial.Hex("#EDE3D0").linear;
        static readonly Color PuffWhite = SpeedDial.Hex("#E6E9F1");
        static readonly Color ShardGrey = SpeedDial.Hex("#C9CEDB");
        static readonly Color SparkHot = SpeedDial.Hex("#FFF3DA");
        static readonly Color SparkCool = SpeedDial.Hex("#FFCB1F");
        static readonly Color SprayColor = SpeedDial.Hex("#DDEFFC").linear;
        static readonly Color Powder = SpeedDial.Hex("#F4F7FC").linear;
        static readonly Vector3 PuffLight = new Vector3(-0.35f, 0.85f, -0.4f).normalized;

        static MotionTuning Tune => MotionTuning.Current;

        public static void Land(Vector3 position, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (instance != null) instance.LandBurst(position, strength, 1f);
            Landed?.Invoke(position, strength);
        }

        public static void Footstep(Vector3 position)
        {
            if (instance != null) instance.SoftBurst(position, 1, 0, Dust, 0.06f, 0.4f, 0.3f, 0.35f);
            Stepped?.Invoke(position);
        }

        // A few soft dust puffs blown out from under a hovering vehicle; no landing events.
        public static void Downwash(Vector3 position)
        {
            if (instance != null) instance.SoftBurst(position, 3, 0, Dust, 0.12f, 1.2f, 0.5f, 0.35f);
        }

        public static void Scrape(Vector3 position, Vector3 normal, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (instance != null) instance.Sparks(position, normal, strength);
            if (CameraFeel.Instance != null) CameraFeel.Instance.AddTrauma(Tune.camera.scrapeShake * (0.5f + strength));
            Scraped?.Invoke(position, normal, strength);
        }

        // Wheel spray: blue-white puffs kicked up and back, fired automatically behind the rear wheels
        // on wet or snowy roads.
        public static void Splash(Vector3 position, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (instance != null) instance.Spray(position, strength);
            Splashed?.Invoke(position, strength);
        }

        public static void Boost(float strength = 1f)
        {
            strength = Mathf.Clamp01(strength);
            BoostFactor = Mathf.Max(BoostFactor, strength);
            if (instance != null && instance.car != null)
            {
                var t = instance.car.transform;
                var p = Tune.puffs;
                foreach (var wheel in instance.rearWheels)
                    for (int i = 0; i < EffectsSettings.Count(p.boostPuffs * strength); i++)
                        instance.WheelPuff(wheel.position, t, p.boostSize * instance.R(0.8f, 1.15f), p.kick * 1.6f);
            }
            if (CameraFeel.Instance != null) CameraFeel.Instance.AddTrauma(Tune.camera.boostShake * strength);
            Boosted?.Invoke(strength);
        }

        struct Puff
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 Spin;
            public float Age;
            public float Life;
            public float Size;
            public float Alpha;
            public Color Tint;
        }

        struct Spark
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Life;
        }

        struct Streak
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Length;
            public float Age;
            public float Life;
        }

        readonly Puff[] puffs = new Puff[MaxPuffs];
        readonly Puff[] softPuffs = new Puff[MaxSoftPuffs];
        int nextSoftPuff;
        Mesh softMesh;
        Vector3[] softVertices;
        Color[] softColors;
        bool softAlive;
        readonly Spark[] sparks = new Spark[MaxSparks];
        readonly Streak[] streaks = new Streak[MaxStreaks];
        int nextPuff, nextSpark, nextStreak;
        Mesh puffMesh, glowMesh, markMesh;
        Vector3[] puffVertices, glowVertices, markVertices;
        Color[] puffColors, glowColors;
        bool puffColorsDirty, marksDirty, puffsAlive, glowAlive;
        List<(Vector3, Vector3, Vector3)> puffShape;
        Vector3[] facetNormals;
        int nextMark;
        readonly Vector3?[] lastMark = new Vector3?[2];

        CarController car;
        readonly List<Transform> rearWheels = new List<Transform>();
        float lastSpeed, lastYaw, lastCarY, carFall, steady, accel, emitDebt, streakDebt, sprayDebt, downhill;
        bool carAirborne;
        Transform carVisual;
        float squash, squashVelocity;

        PlayerController player;
        CharacterController body;
        bool wasGrounded = true;
        float lastPlayerY, playerFall, airTime, stride;
        bool leftFoot;

        readonly System.Random random = new System.Random(70);
        Camera view;

        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        void Awake()
        {
            instance = this;
            VehicleRoster.Swapped += Retrack;
            GameEvents.Countdown += OnCountdown;
        }

        void OnDestroy()
        {
            VehicleRoster.Swapped -= Retrack;
            GameEvents.Countdown -= OnCountdown;
            if (instance == this) instance = null;
        }

        // The race start is the one built-in boost: GO with your foot down.
        void OnCountdown(int left)
        {
            if (left == 0 && car != null) Boost(Tune.speed.raceStartBoost);
        }

        // SwapInto rebuilds the same car with new wheels, so the cached rear wheels are gone.
        void Retrack(CarController swapped)
        {
            if (swapped != car) return;
            TrackCar(null);
            TrackCar(swapped);
        }

        void Start()
        {
            var flat = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "Motion fx" };
            var glow = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Motion fx glow" };

            puffShape = SpeedDial.Icosphere(0);
            facetNormals = new Vector3[puffShape.Count];
            for (int i = 0; i < puffShape.Count; i++)
            {
                var (a, b, c) = puffShape[i];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) puffShape[i] = (a, c, b);
                facetNormals[i] = ((a + b + c) / 3f).normalized;
            }
            puffVertices = new Vector3[MaxPuffs * puffShape.Count * 3];
            puffColors = new Color[puffVertices.Length];
            puffMesh = Dynamic("puffs", puffVertices, puffColors, Sequential(puffVertices.Length));
            Spawn("Puffs", puffMesh, flat, false);

            // Wheel puffs, landing rings and shards: see-through faceted lumps on the unlit material,
            // each facet shaded from a fixed light so they read as faceted without lighting.
            softVertices = new Vector3[MaxSoftPuffs * puffShape.Count * 3];
            softColors = new Color[softVertices.Length];
            softMesh = Dynamic("soft puffs", softVertices, softColors, Sequential(softVertices.Length));
            Spawn("SoftPuffs", softMesh, glow, false);

            glowVertices = new Vector3[(MaxSparks + MaxStreaks) * 4];
            glowColors = new Color[glowVertices.Length];
            glowMesh = Dynamic("sparks and streaks", glowVertices, glowColors, Quads(MaxSparks + MaxStreaks));
            Spawn("Glow", glowMesh, glow, false);

            markVertices = new Vector3[MaxMarks * 4];
            var markColors = new Color[markVertices.Length];
            for (int i = 0; i < markColors.Length; i++) markColors[i] = SpeedDial.TireMark;
            markMesh = Dynamic("tire marks", markVertices, markColors, Quads(MaxMarks));
            Spawn("TireMarks", markMesh, flat, true);
        }

        static int[] Sequential(int n)
        {
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            return indices;
        }

        static int[] Quads(int count)
        {
            var indices = new int[count * 6];
            for (int q = 0; q < count; q++)
            {
                int v = q * 4, t = q * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            return indices;
        }

        static Mesh Dynamic(string name, Vector3[] vertices, Color[] colors, int[] indices)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }

        void Spawn(string name, Mesh mesh, Material material, bool receiveShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || puffMesh == null) return;
            if (view == null) view = Camera.main;
            var tune = Tune.speed;
            BoostFactor = Mathf.MoveTowards(BoostFactor, 0f, dt / Mathf.Max(tune.boostSeconds, 0.05f));

            TrackCar(Game != null ? Game.CurrentCar : null);
            float speedTarget = 0f;
            if (car != null)
            {
                float top = Mathf.Max(car.Handling.TopSpeed, tune.minTopSpeed);
                speedTarget = Mathf.Clamp01((Mathf.Abs(car.Speed) - tune.floor) / Mathf.Max(top - tune.floor, 1f));
                UpdateCar(dt);
            }
            else
            {
                lastMark[0] = lastMark[1] = null;
                Lateral = Mathf.MoveTowards(Lateral, 0f, 40f * dt);
                downhill = 0f;
            }
            SpeedFactor = Mathf.MoveTowards(SpeedFactor, speedTarget, dt * 1.5f);
            float ramp = Mathf.Clamp01((SpeedFactor - tune.rushStart) / Mathf.Max(tune.rushFull - tune.rushStart, 0.01f));
            float rushTarget = car != null ? Mathf.Clamp01(ramp + BoostFactor * tune.boostRush + downhill * tune.downhillRush * Mathf.Min(SpeedFactor * 2f, 1f)) : 0f;
            Rush = Mathf.MoveTowards(Rush, rushTarget, dt * 2.5f);
            UpdatePlayer(dt);
            SpawnStreaks(dt);
            UpdateSquash(dt);

            UpdatePuffs(dt);
            UpdateGlow(dt);
            if (marksDirty)
            {
                markMesh.vertices = markVertices;
                marksDirty = false;
            }
        }

        void TrackCar(CarController next)
        {
            if (next == car) return;
            if (car != null && car.GetComponent<ScrapeSensor>() is ScrapeSensor old) Destroy(old);
            if (carVisual != null) carVisual.localScale = Vector3.one;
            car = next;
            carVisual = null;
            squash = squashVelocity = 0f;
            carAirborne = false;
            rearWheels.Clear();
            lastMark[0] = lastMark[1] = null;
            if (car == null) return;
            car.gameObject.AddComponent<ScrapeSensor>();
            lastSpeed = car.Speed;
            lastYaw = car.transform.eulerAngles.y;
            lastCarY = car.transform.position.y;
            accel = 0f;
            foreach (var t in car.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("wheel-") && !t.name.EndsWith("-mesh") && car.transform.InverseTransformPoint(t.position).z < 0f)
                    rearWheels.Add(t);
            if (rearWheels.Count > 0) return;
            var e = car.HalfExtents;
            foreach (float side in new[] { -1f, 1f })
            {
                var anchor = new GameObject("rear wheel fx").transform;
                anchor.SetParent(car.transform, false);
                anchor.localPosition = new Vector3(side * e.x * 0.75f, 0.3f, -e.z * 0.7f);
                rearWheels.Add(anchor);
            }
        }

        // The car's model is the direct child that holds its renderers; squash and stretch scale it
        // about its own origin at the wheels, leaving the body, colliders and CarFx's root scale alone.
        Transform FindVisual()
        {
            var renderer = car.GetComponentInChildren<MeshRenderer>();
            if (renderer == null) return null;
            var t = renderer.transform;
            while (t.parent != null && t.parent != car.transform) t = t.parent;
            return t.parent == car.transform ? t : null;
        }

        void UpdateCar(float dt)
        {
            var t = car.transform;
            var p = Tune.puffs;
            float speed = car.Speed;
            float yaw = t.eulerAngles.y;
            float rawAccel = (Mathf.Abs(speed) - Mathf.Abs(lastSpeed)) / dt;
            accel = Mathf.Lerp(accel, rawAccel, 1f - Mathf.Exp(-10f * dt));
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt * Mathf.Deg2Rad;
            lastSpeed = speed;
            lastYaw = yaw;
            Lateral = Mathf.Lerp(Lateral, yawRate * Mathf.Abs(speed), 1f - Mathf.Exp(-12f * dt));

            float vy = (t.position.y - lastCarY) / dt;
            lastCarY = t.position.y;
            if (Mathf.Abs(vy) > 30f)
            {
                carFall = 0f;
                carAirborne = false;
                return;
            }
            steady = Mathf.Abs(vy) < 0.5f ? steady + dt : 0f;
            downhill = Mathf.Lerp(downhill, carAirborne ? 0f : Mathf.Clamp01(-vy / Mathf.Max(Tune.speed.downhillFullDrop, 0.1f)), 1f - Mathf.Exp(-4f * dt));
            if (!carAirborne && vy > 3f)
            {
                carAirborne = true;
                Kick(Tune.squash.takeoffStretch);
                Boost(Tune.speed.takeoffBoost);
            }
            if (vy < -3f) carFall = Mathf.Min(carFall, vy);
            else if (carFall < -3f && vy > -0.5f)
            {
                float strength = Mathf.Clamp01(-carFall / 10f);
                Kick(-Tune.squash.landSquash * Mathf.Lerp(0.4f, 1f, strength));
                Land(t.position, strength);
                carFall = 0f;
                carAirborne = false;
            }
            if (carAirborne && steady > 0.25f) carAirborne = false;

            var handling = car.Handling;
            float top = Mathf.Max(handling.TopSpeed, Tune.speed.minTopSpeed);
            float share = Mathf.Abs(speed) / top;
            float lateral = Mathf.Abs(Lateral);
            float launch = share < p.launchMaxSpeedShare && accel > p.launchAccelShare * handling.Acceleration
                ? 1f - share / p.launchMaxSpeedShare : 0f;
            float drift = Mathf.Clamp01((lateral - p.driftStart) / Mathf.Max(p.driftFull - p.driftStart, 0.1f));
            float braking = Mathf.Abs(speed) > p.brakeMinSpeed ? Mathf.Clamp01((-accel / Mathf.Max(handling.Braking, 1f) - p.brakeDecelShare) / (1f - p.brakeDecelShare + 0.01f)) : 0f;
            bool grounded = !carAirborne;
            float skidRate = Mathf.Max(launch * p.launchRate, Mathf.Max(drift * p.driftRate, braking * p.brakeRate));
            float cruiseRate = Rush * p.cruiseRate;
            float rate = grounded ? Mathf.Max(skidRate, cruiseRate) : 0f;
            float skid = Mathf.Max(launch, Mathf.Max(drift, braking));
            float puffSize = skidRate >= cruiseRate ? p.size * (0.75f + 0.45f * skid) : p.cruiseSize;

            emitDebt = rate > 0f ? emitDebt + rate * EffectsSettings.Intensity * dt : 0f;
            while (emitDebt >= 1f)
            {
                emitDebt -= 1f;
                foreach (var wheel in rearWheels) WheelPuff(wheel.position, t, puffSize * R(0.85f, 1.15f), p.kick);
            }
            for (int w = 0; w < rearWheels.Count && w < 2; w++) Mark(w, rearWheels[w].position, grounded && Mathf.Max(Mathf.Max(drift, braking), launch * 0.6f) > 0.3f);

            float wet = Mathf.Max(Mathf.Max(WorldEnvironment.Wetness, WorldEnvironment.SnowCover), WorldEnvironment.Ice * Mathf.Clamp01(0.35f + drift));
            sprayDebt += wet * Mathf.Clamp01((Mathf.Abs(speed) - 3f) / 12f) * 13f * EffectsSettings.Intensity * dt;
            while (sprayDebt >= 1f)
            {
                sprayDebt -= 1f;
                foreach (var wheel in rearWheels) Splash(new Vector3(wheel.position.x, t.position.y + 0.15f, wheel.position.z), Mathf.Clamp01(share));
            }
        }

        void Kick(float amount)
        {
            squash = amount;
            squashVelocity = 0f;
        }

        // A damped spring on the car body's vertical scale, volume kept roughly constant.
        void UpdateSquash(float dt)
        {
            if (car == null) return;
            if (carVisual == null)
            {
                carVisual = FindVisual();
                if (carVisual == null) return;
            }
            var s = Tune.squash;
            float omega = 2f * Mathf.PI / Mathf.Max(s.period, 0.05f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 60f), 1, 30);
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                squashVelocity += (-omega * omega * squash - 2f * s.damping * omega * squashVelocity) * step;
                squash += squashVelocity * step;
            }
            if (float.IsNaN(squash) || float.IsNaN(squashVelocity) || (Mathf.Abs(squash) < 0.001f && Mathf.Abs(squashVelocity) < 0.01f)) squash = squashVelocity = 0f;
            float y = 1f + squash;
            float xz = 1f / Mathf.Sqrt(Mathf.Max(y, 0.5f));
            carVisual.localScale = new Vector3(xz, y, xz);
        }

        void UpdatePlayer(float dt)
        {
            var next = Game != null ? Game.Player : null;
            if (next != player)
            {
                player = next;
                body = player != null ? player.GetComponent<CharacterController>() : null;
            }
            if (player == null || body == null || !player.gameObject.activeInHierarchy)
            {
                wasGrounded = true;
                airTime = 0f;
                return;
            }

            var p = player.transform.position;
            float vy = (p.y - lastPlayerY) / dt;
            lastPlayerY = p.y;
            bool grounded = body.isGrounded;
            if (!grounded)
            {
                airTime += dt;
                playerFall = Mathf.Min(playerFall, vy);
            }
            else if (!wasGrounded)
            {
                if (airTime > 0.25f)
                {
                    float strength = Mathf.Clamp01(-playerFall / 9f);
                    LandBurst(p, strength, Tune.landing.onFootScale);
                    Landed?.Invoke(p, strength);
                }
                airTime = 0f;
                playerFall = 0f;
            }
            wasGrounded = grounded;

            if (!grounded || player.Speed < 1.2f)
            {
                stride = 0f;
                return;
            }
            stride += player.Speed * dt;
            float length = player.Speed > 3.8f ? 1.7f : 1.1f;
            if (stride < length) return;
            stride -= length;
            leftFoot = !leftFoot;
            Footstep(p + player.transform.right * (leftFoot ? -0.13f : 0.13f) + Vector3.up * 0.05f);
        }

        // One chunky puff off a rear wheel, carried along with the car so puffs bunch up behind the
        // wheel instead of stringing out into a trail, plus now and then a small flying shard.
        void WheelPuff(Vector3 wheel, Transform t, float size, float kick)
        {
            var p = Tune.puffs;
            var carry = car != null ? t.forward * car.Speed * p.carry : Vector3.zero;
            var origin = new Vector3(wheel.x, t.position.y + size * 0.6f, wheel.z) - t.forward * R(0.15f, 0.45f) + t.right * R(-0.15f, 0.15f);
            SoftPuff(origin, carry - t.forward * kick * R(0.6f, 1.2f) + t.right * R(-0.8f, 0.8f) + Vector3.up * R(0.5f, 1.1f),
                size, p.life * R(0.85f, 1.15f), p.alpha, PuffWhite);
            if (random.NextDouble() < p.shardChance)
                SoftPuff(origin, carry - t.forward * kick * R(1f, 2f) + t.right * R(-2f, 2f) + Vector3.up * R(1.5f, 3f),
                    p.shardSize * R(0.7f, 1.3f), R(0.25f, 0.4f), 0.95f, ShardGrey);
        }

        // Touchdown: a ring of chunky puffs rolling out along the ground, plus shards. Car landings
        // use the full ring; on-foot landings a small one.
        void LandBurst(Vector3 position, float strength, float scale)
        {
            var l = Tune.landing;
            if (strength < l.minStrength) return;
            float k = Mathf.Lerp(0.5f, 1f, strength) * scale;
            SoftBurst(position, Mathf.Max(2, Mathf.RoundToInt(l.puffs * k)), Mathf.RoundToInt(l.shards * k), PuffWhite, l.size * k, l.spread * Mathf.Lerp(0.6f, 1f, strength), l.life, Tune.puffs.alpha);
        }

        void SoftBurst(Vector3 position, int puffCount, int shardCount, Color tint, float size, float spread, float life, float alpha)
        {
            int count = EffectsSettings.Count(puffCount);
            for (int i = 0; i < count; i++)
            {
                float a = (i + R(0f, 0.7f)) / count * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                SoftPuff(position + outward * size * 0.8f + Vector3.up * size * 0.5f, outward * spread * R(0.6f, 1f) + Vector3.up * R(0.2f, 0.7f), size * R(0.8f, 1.2f), life * R(0.85f, 1.15f), alpha, tint);
            }
            if (shardCount <= 0) return;
            for (int i = 0; i < EffectsSettings.Count(shardCount); i++)
            {
                var outward = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward;
                SoftPuff(position + Vector3.up * 0.2f, outward * spread * R(0.8f, 1.5f) + Vector3.up * R(2f, 4f), Tune.puffs.shardSize * R(0.7f, 1.3f), R(0.3f, 0.45f), 0.95f, ShardGrey);
            }
        }

        void SoftPuff(Vector3 position, Vector3 velocity, float size, float life, float alpha, Color tint)
        {
            softPuffs[nextSoftPuff] = new Puff
            {
                Position = position, Velocity = velocity, Rotation = UnityEngine.Random.rotationUniform,
                Spin = UnityEngine.Random.insideUnitSphere * 240f, Size = size * EffectsSettings.Scale, Life = life,
                Alpha = Mathf.Min(alpha * Mathf.Sqrt(EffectsSettings.Scale), 0.95f), Tint = tint,
            };
            nextSoftPuff = (nextSoftPuff + 1) % MaxSoftPuffs;
            softAlive = true;
        }

        void Spray(Vector3 position, float strength)
        {
            var back = car != null ? -car.transform.forward * Mathf.Sign(car.Speed == 0f ? 1f : car.Speed) : Vector3.zero;
            var side = car != null ? car.transform.right : Vector3.right;
            AddPuff(position + back * 0.4f, back * R(1.5f, 3f) * (0.5f + strength) + side * R(-0.9f, 0.9f) + Vector3.up * R(1.2f, 2.6f),
                R(0.08f, 0.13f) * (0.7f + strength), R(0.3f, 0.45f), Mathf.Max(WorldEnvironment.SnowCover, WorldEnvironment.Ice) > WorldEnvironment.Wetness ? Powder : SprayColor);
        }

        void AddPuff(Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            size *= EffectsSettings.Scale;
            puffs[nextPuff] = new Puff { Position = position, Velocity = velocity, Rotation = UnityEngine.Random.rotationUniform, Size = size, Life = life };
            int v = nextPuff * puffShape.Count * 3;
            for (int i = 0; i < puffShape.Count * 3; i++) puffColors[v + i] = color;
            puffColorsDirty = true;
            puffsAlive = true;
            nextPuff = (nextPuff + 1) % MaxPuffs;
        }

        void Sparks(Vector3 position, Vector3 normal, float strength)
        {
            var s = Tune.sparks;
            var carry = car != null ? car.transform.forward * car.Speed * 0.45f : Vector3.zero;
            int count = EffectsSettings.Count(Mathf.Lerp(s.countMin, s.countMax, strength));
            for (int i = 0; i < count; i++)
            {
                sparks[nextSpark] = new Spark
                {
                    Position = position,
                    Velocity = normal * R(1.5f, 4f) + Vector3.up * R(2f, 5f) + UnityEngine.Random.insideUnitSphere * 3f + carry,
                    Life = R(s.lifeMin, s.lifeMax),
                };
                nextSpark = (nextSpark + 1) % MaxSparks;
            }
            glowAlive = true;
        }

        void SpawnStreaks(float dt)
        {
            if (view == null || car == null) return;
            streakDebt += Rush * Tune.streaks.rate * EffectsSettings.Intensity * dt;
            var cam = view.transform;
            var heading = car.transform.forward * Mathf.Sign(car.Speed == 0f ? 1f : car.Speed);
            while (streakDebt >= 1f)
            {
                streakDebt -= 1f;
                float side = random.Next(2) == 0 ? -1f : 1f;
                var offset = cam.right * side * R(3.5f, 8f) + Vector3.up * R(0.5f, 5f);
                streaks[nextStreak] = new Streak
                {
                    Position = cam.position + heading * R(14f, 30f) + offset,
                    Direction = heading,
                    Length = R(3f, 7f),
                    Life = R(0.3f, 0.55f),
                };
                nextStreak = (nextStreak + 1) % MaxStreaks;
                glowAlive = true;
            }
        }

        void UpdatePuffs(float dt)
        {
            if (puffsAlive)
            {
                int k = 0;
                bool any = false;
                for (int i = 0; i < MaxPuffs; i++)
                {
                    ref var p = ref puffs[i];
                    float scale = 0f;
                    if (p.Size > 0f && p.Age < p.Life)
                    {
                        any = true;
                        p.Age += dt;
                        p.Position += p.Velocity * dt;
                        p.Velocity *= 1f - 2.5f * dt;
                        float t = p.Age / p.Life;
                        scale = p.Size * (t < 0.3f ? Mathf.Lerp(0.4f, 1f, t / 0.3f) : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f)) * (1f + t * 1.2f);
                    }
                    foreach (var (a, b, c) in puffShape)
                    {
                        puffVertices[k++] = p.Position + p.Rotation * a * scale;
                        puffVertices[k++] = p.Position + p.Rotation * b * scale;
                        puffVertices[k++] = p.Position + p.Rotation * c * scale;
                    }
                }
                puffMesh.vertices = puffVertices;
                puffsAlive = any;
            }
            if (softAlive) UpdateSoftPuffs(dt);
            if (!puffColorsDirty) return;
            puffMesh.colors = puffColors;
            puffColorsDirty = false;
        }

        // Puffs pop in, swell, then shrink away while fading, so they read as cartoon puffs rather
        // than haze.
        void UpdateSoftPuffs(float dt)
        {
            int k = 0;
            bool any = false;
            for (int i = 0; i < MaxSoftPuffs; i++)
            {
                ref var p = ref softPuffs[i];
                float scale = 0f, fade = 0f;
                if (p.Size > 0f && p.Age < p.Life)
                {
                    any = true;
                    p.Age += dt;
                    p.Velocity += Vector3.down * (p.Size < 0.12f ? 12f : 0f) * dt;
                    p.Position += p.Velocity * dt;
                    p.Velocity *= 1f - 3.2f * dt;
                    p.Rotation = Quaternion.Euler(p.Spin * dt) * p.Rotation;
                    float t = Mathf.Clamp01(p.Age / p.Life);
                    scale = p.Size * (t < 0.15f ? Mathf.Lerp(0.6f, 1f, t / 0.15f) : Mathf.Lerp(1f, 1.25f, Mathf.Min((t - 0.15f) / 0.35f, 1f)) * (t > 0.6f ? Mathf.Lerp(1f, 0.35f, (t - 0.6f) / 0.4f) : 1f));
                    fade = p.Alpha * (t > 0.55f ? 1f - (t - 0.55f) / 0.45f : 1f);
                }
                for (int f = 0; f < puffShape.Count; f++)
                {
                    var (a, b, c) = puffShape[f];
                    float shade = 0.68f + 0.32f * Mathf.Clamp01(Vector3.Dot(p.Rotation * facetNormals[f], PuffLight) * 0.5f + 0.5f);
                    var color = new Color(p.Tint.r * shade, p.Tint.g * shade, p.Tint.b * shade, fade);
                    softColors[k] = softColors[k + 1] = softColors[k + 2] = color;
                    softVertices[k++] = p.Position + p.Rotation * a * scale;
                    softVertices[k++] = p.Position + p.Rotation * b * scale;
                    softVertices[k++] = p.Position + p.Rotation * c * scale;
                }
            }
            softMesh.vertices = softVertices;
            softMesh.colors = softColors;
            softAlive = any;
        }

        // Sparks and streaks are thin quads facing the camera, stretched along their motion.
        void UpdateGlow(float dt)
        {
            if (!glowAlive) return;
            var eye = view != null ? view.transform.position : Vector3.zero;
            var s = Tune.sparks;
            var w = Tune.streaks;
            bool any = false;
            int q = 0;
            for (int i = 0; i < MaxSparks; i++, q++)
            {
                ref var sp = ref sparks[i];
                if (sp.Life <= 0f || sp.Age >= sp.Life)
                {
                    Hide(q);
                    continue;
                }
                any = true;
                sp.Age += dt;
                sp.Velocity += Vector3.down * 14f * dt;
                sp.Position += sp.Velocity * dt;
                float t = sp.Age / sp.Life;
                var color = Color.Lerp(SparkHot, SparkCool, t);
                color.a = 1f - t * t;
                Line(q, sp.Position, sp.Position - sp.Velocity * s.trail, s.width * EffectsSettings.Scale, eye, color);
            }
            for (int i = 0; i < MaxStreaks; i++, q++)
            {
                ref var st = ref streaks[i];
                if (st.Life <= 0f || st.Age >= st.Life)
                {
                    Hide(q);
                    continue;
                }
                any = true;
                st.Age += dt;
                float t = st.Age / st.Life;
                Line(q, st.Position, st.Position - st.Direction * st.Length, w.width, eye, new Color(1f, 1f, 1f, Mathf.Min(w.alpha * EffectsSettings.Scale, 0.8f) * Mathf.Sin(t * Mathf.PI)));
            }
            glowMesh.vertices = glowVertices;
            glowMesh.colors = glowColors;
            glowAlive = any;
        }

        void Hide(int q)
        {
            int v = q * 4;
            glowVertices[v] = glowVertices[v + 1] = glowVertices[v + 2] = glowVertices[v + 3] = Vector3.zero;
        }

        void Line(int q, Vector3 a, Vector3 b, float halfWidth, Vector3 eye, Color color)
        {
            var side = Vector3.Cross(b - a, eye - a).normalized * halfWidth;
            int v = q * 4;
            glowVertices[v] = a + side;
            glowVertices[v + 1] = b + side;
            glowVertices[v + 2] = b - side;
            glowVertices[v + 3] = a - side;
            glowColors[v] = glowColors[v + 3] = color;
            color.a *= 0.2f;
            glowColors[v + 1] = glowColors[v + 2] = color;
        }

        void Mark(int wheel, Vector3 position, bool skidding)
        {
            var ground = new Vector3(position.x, car.transform.position.y + 0.06f, position.z);
            if (!skidding)
            {
                lastMark[wheel] = null;
                return;
            }
            if (lastMark[wheel] is Vector3 last)
            {
                var d = ground - last;
                if (d.sqrMagnitude < 0.09f) return;
                var side = Vector3.Cross(Vector3.up, d.normalized) * (MarkWidth * 0.5f * EffectsSettings.Scale);
                int v = nextMark * 4;
                markVertices[v] = last + side;
                markVertices[v + 1] = last - side;
                markVertices[v + 2] = ground - side;
                markVertices[v + 3] = ground + side;
                nextMark = (nextMark + 1) % MaxMarks;
                marksDirty = true;
            }
            lastMark[wheel] = ground;
        }
    }
}
