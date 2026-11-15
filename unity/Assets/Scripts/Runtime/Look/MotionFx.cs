using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Speed Dial motion feel: faceted smoke from the rear wheels, tire marks, dust on landings and
    // footsteps, sparks on scrapes and wind streaks at high speed. It watches the player and the car
    // you drive and fires these itself; other systems (camera shake, hit-stop, haptics, audio) listen
    // to the static events, or call the triggers to fire an effect themselves. Every effect is a
    // fixed pool rebuilt into one dynamic mesh: puffs on the shared flat material, sparks and streaks
    // on an unlit one, tire marks on the flat one. Three draw calls in total.
    public sealed class MotionFx : MonoBehaviour
    {
        const int MaxPuffs = 96;
        const int MaxSparks = 64;
        const int MaxStreaks = 28;
        const int MaxMarks = 600;
        const float MarkWidth = 0.24f;
        const float CarTopSpeed = 22f;

        public static event Action<Vector3, float> Landed;
        public static event Action<Vector3> Stepped;
        public static event Action<Vector3, Vector3, float> Scraped;
        public static event Action<float> Boosted;

        // 0 at a crawl, 1 at the car's top speed. Drives speed lines, field of view and wind streaks.
        public static float SpeedFactor { get; private set; }

        // 1 right after a boost, decaying to 0 over about a second.
        public static float BoostFactor { get; private set; }

        public GameController Game;

        static MotionFx instance;

        static readonly Color Dust = SpeedDial.Hex("#EDE3D0").linear;
        static readonly Color SparkHot = SpeedDial.Hex("#FFF3DA");
        static readonly Color SparkCool = SpeedDial.Hex("#FFCB1F");

        public static void Land(Vector3 position, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (instance != null) instance.Burst(position, 5 + Mathf.RoundToInt(strength * 7f), Dust, 0.3f + 0.35f * strength, 2.2f + 3f * strength, 0.8f);
            Landed?.Invoke(position, strength);
        }

        public static void Footstep(Vector3 position)
        {
            if (instance != null) instance.Burst(position, 1, Dust, 0.17f, 0.7f, 0.5f);
            Stepped?.Invoke(position);
        }

        public static void Scrape(Vector3 position, Vector3 normal, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (instance != null) instance.Sparks(position, normal, strength);
            Scraped?.Invoke(position, normal, strength);
        }

        public static void Boost(float strength = 1f)
        {
            strength = Mathf.Clamp01(strength);
            BoostFactor = Mathf.Max(BoostFactor, strength);
            if (instance != null && instance.car != null)
                foreach (var wheel in instance.rearWheels) instance.Burst(wheel.position, 3, SpeedDial.Smoke, 0.55f, 1.6f, 0.9f);
            Boosted?.Invoke(strength);
        }

        struct Puff
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public float Age;
            public float Life;
            public float Size;
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
        readonly Spark[] sparks = new Spark[MaxSparks];
        readonly Streak[] streaks = new Streak[MaxStreaks];
        int nextPuff, nextSpark, nextStreak;
        Mesh puffMesh, glowMesh, markMesh;
        Vector3[] puffVertices, glowVertices, markVertices;
        Color[] puffColors, glowColors;
        bool puffColorsDirty, marksDirty;
        List<(Vector3, Vector3, Vector3)> puffShape;
        int nextMark;
        readonly Vector3?[] lastMark = new Vector3?[2];

        CarController car;
        readonly List<Transform> rearWheels = new List<Transform>();
        float lastSpeed, lastYaw, lastCarY, carFall, emitDebt, streakDebt;

        PlayerController player;
        CharacterController body;
        bool wasGrounded = true;
        float lastPlayerY, playerFall, airTime, stride;
        bool leftFoot;

        readonly System.Random random = new System.Random(70);
        Camera view;

        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Start()
        {
            var flat = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "Motion fx" };
            var glow = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Motion fx glow" };

            puffShape = SpeedDial.Icosphere(0);
            for (int i = 0; i < puffShape.Count; i++)
            {
                var (a, b, c) = puffShape[i];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) puffShape[i] = (a, c, b);
            }
            puffVertices = new Vector3[MaxPuffs * puffShape.Count * 3];
            puffColors = new Color[puffVertices.Length];
            puffMesh = Dynamic("puffs", puffVertices, puffColors, Sequential(puffVertices.Length));
            Spawn("Puffs", puffMesh, flat, false);

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
            BoostFactor = Mathf.MoveTowards(BoostFactor, 0f, dt / 1.1f);

            TrackCar(Game != null ? Game.CurrentCar : null);
            float speedTarget = car != null ? Mathf.Clamp01((Mathf.Abs(car.Speed) - 4f) / (CarTopSpeed - 4f)) : 0f;
            SpeedFactor = Mathf.MoveTowards(SpeedFactor, speedTarget, dt * 1.5f);
            if (car != null) UpdateCar(dt);
            else lastMark[0] = lastMark[1] = null;
            UpdatePlayer(dt);
            SpawnStreaks(dt);

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
            car = next;
            rearWheels.Clear();
            lastMark[0] = lastMark[1] = null;
            if (car == null) return;
            car.gameObject.AddComponent<ScrapeSensor>();
            lastSpeed = car.Speed;
            lastYaw = car.transform.eulerAngles.y;
            lastCarY = car.transform.position.y;
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

        void UpdateCar(float dt)
        {
            var t = car.transform;
            float speed = car.Speed;
            float yaw = t.eulerAngles.y;
            float accel = (Mathf.Abs(speed) - Mathf.Abs(lastSpeed)) / dt;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt * Mathf.Deg2Rad;
            lastSpeed = speed;
            lastYaw = yaw;

            float vy = (t.position.y - lastCarY) / dt;
            lastCarY = t.position.y;
            if (vy < -3f) carFall = Mathf.Min(carFall, vy);
            else if (carFall < -3f && vy > -0.5f)
            {
                Land(t.position, -carFall / 10f);
                carFall = 0f;
            }

            float lateral = Mathf.Abs(yawRate * speed);
            float launch = Mathf.Abs(speed) < 12f ? Mathf.Clamp01((accel - 3f) / 4f) : 0f;
            float drift = Mathf.Clamp01((lateral - 7f) / 8f);
            float braking = Mathf.Clamp01((-accel - 9f) / 6f);
            float cruise = Mathf.Clamp01((Mathf.Abs(speed) - 14f) / 8f) * 0.35f;
            float skid = Mathf.Max(launch, Mathf.Max(drift, braking));

            emitDebt += Mathf.Max(skid, cruise) * 26f * dt;
            while (emitDebt >= 1f)
            {
                emitDebt -= 1f;
                foreach (var wheel in rearWheels) EmitSmoke(wheel.position, t, skid);
            }
            for (int w = 0; w < rearWheels.Count && w < 2; w++) Mark(w, rearWheels[w].position, skid > 0.25f);
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
                if (airTime > 0.25f) Land(p, Mathf.Clamp01(-playerFall / 9f));
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

        void EmitSmoke(Vector3 wheel, Transform t, float skid)
        {
            AddPuff(new Vector3(wheel.x, t.position.y + 0.25f, wheel.z) - t.forward * 0.3f,
                -t.forward * R(1f, 2.5f) + t.right * R(-0.8f, 0.8f) + Vector3.up * 0.6f,
                0.35f + 0.35f * skid + R(0f, 0.15f), 0.9f, SpeedDial.Smoke);
        }

        void Burst(Vector3 position, int count, Color color, float size, float spread, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + R(0f, 0.7f)) / count * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                AddPuff(position + outward * size * 0.6f + Vector3.up * size * 0.4f,
                    outward * spread * R(0.6f, 1f) + Vector3.up * R(0.4f, 1.2f),
                    size * R(0.8f, 1.2f), life * R(0.8f, 1.1f), color);
            }
        }

        void AddPuff(Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            puffs[nextPuff] = new Puff { Position = position, Velocity = velocity, Rotation = UnityEngine.Random.rotationUniform, Size = size, Life = life };
            int v = nextPuff * puffShape.Count * 3;
            for (int i = 0; i < puffShape.Count * 3; i++) puffColors[v + i] = color;
            puffColorsDirty = true;
            nextPuff = (nextPuff + 1) % MaxPuffs;
        }

        void Sparks(Vector3 position, Vector3 normal, float strength)
        {
            var carry = car != null ? car.transform.forward * car.Speed * 0.45f : Vector3.zero;
            int count = 6 + Mathf.RoundToInt(strength * 14f);
            for (int i = 0; i < count; i++)
            {
                sparks[nextSpark] = new Spark
                {
                    Position = position,
                    Velocity = normal * R(1f, 3f) + Vector3.up * R(1.5f, 4f) + UnityEngine.Random.insideUnitSphere * 2.5f + carry,
                    Life = R(0.22f, 0.5f),
                };
                nextSpark = (nextSpark + 1) % MaxSparks;
            }
        }

        void SpawnStreaks(float dt)
        {
            if (view == null || car == null) return;
            float rate = Mathf.Clamp01((SpeedFactor - 0.5f) / 0.5f + BoostFactor) * 34f;
            streakDebt += rate * dt;
            var cam = view.transform;
            var heading = car.transform.forward * Mathf.Sign(car.Speed == 0f ? 1f : car.Speed);
            while (streakDebt >= 1f)
            {
                streakDebt -= 1f;
                float side = random.Next(2) == 0 ? -1f : 1f;
                var offset = cam.right * side * R(2.2f, 7f) + Vector3.up * R(-0.6f, 4.5f);
                streaks[nextStreak] = new Streak
                {
                    Position = cam.position + heading * R(14f, 30f) + offset,
                    Direction = heading,
                    Length = R(2.5f, 6f),
                    Life = R(0.35f, 0.6f),
                };
                nextStreak = (nextStreak + 1) % MaxStreaks;
            }
        }

        void UpdatePuffs(float dt)
        {
            int k = 0;
            for (int i = 0; i < MaxPuffs; i++)
            {
                ref var p = ref puffs[i];
                float scale = 0f;
                if (p.Size > 0f && p.Age < p.Life)
                {
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
            if (!puffColorsDirty) return;
            puffMesh.colors = puffColors;
            puffColorsDirty = false;
        }

        // Sparks and streaks are thin quads facing the camera, stretched along their motion.
        void UpdateGlow(float dt)
        {
            var eye = view != null ? view.transform.position : Vector3.zero;
            int q = 0;
            for (int i = 0; i < MaxSparks; i++, q++)
            {
                ref var s = ref sparks[i];
                if (s.Life <= 0f || s.Age >= s.Life)
                {
                    Hide(q);
                    continue;
                }
                s.Age += dt;
                s.Velocity += Vector3.down * 14f * dt;
                s.Position += s.Velocity * dt;
                float t = s.Age / s.Life;
                var color = Color.Lerp(SparkHot, SparkCool, t);
                color.a = 1f - t * t;
                Line(q, s.Position, s.Position - s.Velocity * 0.035f, 0.045f, eye, color);
            }
            for (int i = 0; i < MaxStreaks; i++, q++)
            {
                ref var s = ref streaks[i];
                if (s.Life <= 0f || s.Age >= s.Life)
                {
                    Hide(q);
                    continue;
                }
                s.Age += dt;
                float t = s.Age / s.Life;
                Line(q, s.Position, s.Position - s.Direction * s.Length, 0.035f, eye, new Color(1f, 1f, 1f, 0.55f * Mathf.Sin(t * Mathf.PI)));
            }
            glowMesh.vertices = glowVertices;
            glowMesh.colors = glowColors;
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
                var side = Vector3.Cross(Vector3.up, d.normalized) * (MarkWidth * 0.5f);
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
