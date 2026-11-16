using System;
using System.Collections.Generic;
using System.Linq;
using Dogpatch.Audio;
using Dogpatch.Gameplay;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // The moving set pieces. Every one runs off the race clock (seconds since the room's startsAt on
    // the server's time), so every phone in a race sees the same train at the same moment. Bots on
    // this phone ghost through them; people collide.
    public sealed partial class SetPieces
    {
        static readonly Color Silver = PaintJob.Linear("#BFC4CA");
        static readonly Color SilverDark = PaintJob.Linear("#8E949B");
        static readonly Color Glass = PaintJob.Linear("#26303D");
        static readonly Color CaltrainRed = PaintJob.Linear("#C8102E");
        static readonly Color MuniRed = PaintJob.Linear("#D52B1E");
        static readonly Color Cream = PaintJob.Linear("#EFE8D8");
        static readonly Color Amber = PaintJob.Linear("#FFB000");
        static readonly Color AmberOff = PaintJob.Linear("#6B4A10");
        static readonly Color SignalRed = PaintJob.Linear("#FF2A1A");
        static readonly Color CraneYellow = PaintJob.Linear("#F2B705");
        static readonly Color Skin = PaintJob.Linear("#C68863");
        static readonly Color Coat = PaintJob.Linear("#3A6EA5");
        static readonly Color Jeans = PaintJob.Linear("#2C3E66");
        static readonly Color[] DogCoats = { PaintJob.Linear("#C8954E"), PaintJob.Linear("#2B2B2B"), PaintJob.Linear("#EDE6DA") };

        readonly List<Collider> hazardColliders = new List<Collider>();

        public static float RaceSeconds
        {
            get
            {
                var race = RaceDirector.Instance;
                return race != null && race.Room != null && race.Room.startsAt != 0 ? (race.NowMs - race.Room.startsAt) / 1000f : Time.time;
            }
        }

        static float Mod(float a, float b) => a - b * Mathf.Floor(a / b);

        // Set by the headless stills, which pose the moving pieces outside the physics step.
        public static bool SnapMoves;

        static void Move(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            if (SnapMoves) body.transform.SetPositionAndRotation(position, rotation);
            else
            {
                body.MovePosition(position);
                body.MoveRotation(rotation);
            }
        }

        // Bots on this phone can't dodge a train, so they pass through the moving pieces.
        void GhostBots()
        {
            hazardColliders.RemoveAll(c => c == null);
            if (hazardColliders.Count == 0) return;
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (car == race.MyCar || !race.IsRacerCar(car)) continue;
                foreach (var a in car.GetComponentsInChildren<Collider>())
                    foreach (var b in hazardColliders)
                        Physics.IgnoreCollision(a, b, true);
            }
        }

        Rigidbody Kinematic(Transform parent, string name, Vector3 at, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, rotation);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return body;
        }

        // A mesh child of a moving body with a box collider around it.
        GameObject Part(Transform parent, string name, MeshBuilder mb, Vector3 local, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var mesh = mb.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (!collide) return go;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
            hazardColliders.Add(box);
            return go;
        }

        static MeshBuilder RailCar(string look, float length)
        {
            var mb = new MeshBuilder();
            bool caltrain = look == "caltrain";
            float w = caltrain ? 3.1f : 2.7f, h = caltrain ? 4.3f : 3.4f;
            var body = caltrain ? Silver : Cream;
            var stripe = caltrain ? CaltrainRed : MuniRed;
            mb.Box(new Vector3(0f, 0.55f, 0f), new Vector3(w * 0.8f, 0.6f, length - 1.5f), Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.85f + (h - 0.85f) * 0.5f, 0f), new Vector3(w, h - 0.85f, length), Quaternion.identity, body);
            mb.Box(new Vector3(0f, 1.25f, 0f), new Vector3(w + 0.02f, 0.35f, length + 0.02f), Quaternion.identity, stripe);
            mb.Box(new Vector3(0f, h - 1.25f, 0f), new Vector3(w + 0.03f, 0.75f, length - 1.2f), Quaternion.identity, Glass);
            mb.Box(new Vector3(0f, h - 1.25f, length * 0.5f), new Vector3(w - 0.4f, 0.8f, 0.05f), Quaternion.identity, Glass);
            mb.Box(new Vector3(0f, h + 0.05f, 0f), new Vector3(w - 0.3f, 0.1f, length - 0.6f), Quaternion.identity, SilverDark);
            if (!caltrain)
            {
                mb.Box(new Vector3(0f, h + 0.45f, 0f), new Vector3(0.08f, 0.8f, 1.6f), Quaternion.Euler(30f, 0f, 0f), Black);
                mb.Box(new Vector3(0f, h + 0.85f, 0.3f), new Vector3(1.4f, 0.06f, 0.1f), Quaternion.identity, Black);
            }
            return mb;
        }

        // A level crossing: rails across the road at the piece's spot, gates and red lights on both
        // approaches, and a train (caltrain: three Caltrain cars; streetcar: one T Third LRV; muni:
        // a two-car Muni train out of the yard) that crosses once every period seconds.
        void Crossing(Frame f, Transform holder, string look, float period, float phase, float span)
        {
            f.Point(0f, 0f, out float heading);
            int cars = look == "caltrain" ? 3 : look == "muni" ? 2 : 1;
            float carLength = look == "caltrain" ? 18f : 14f;
            float speed = look == "caltrain" ? 16f : 9f;
            float trainLength = cars * (carLength + 0.6f);
            float pass = (2f * span + trainLength) / speed;
            const float warn = 3f;

            var rails = new MeshBuilder();
            var origin = f.Point(0f, 0f, out _);
            foreach (float a in new[] { -0.75f, 0.75f })
            {
                var p0 = f.Point(a, -span, out _) - origin;
                var p1 = f.Point(a, span, out _) - origin;
                var dir = (p1 - p0).normalized;
                var across = Vector3.Cross(Vector3.up, dir) * 0.06f;
                rails.Quad(p0 + across + Vector3.up * 0.06f, p1 + across + Vector3.up * 0.06f, p1 - across + Vector3.up * 0.06f, p0 - across + Vector3.up * 0.06f, SilverDark, Vector3.up);
            }
            for (float s = -span; s <= span; s += 1.2f)
                rails.Box(f.Point(0f, s, out _) - origin + Vector3.up * 0.03f, new Vector3(0.25f, 0.04f, 2.2f), Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(0f, 90f, 0f), Wood);
            Static(holder, "rails", rails, origin, 0f, false).transform.rotation = Quaternion.identity;

            var arms = new List<Transform>();
            var lamps = new List<(GameObject on, GameObject off)>();
            foreach (int side in new[] { 1, -1 })
            {
                var postAt = f.Point(-6f * side, 5.5f * side, out _);
                var post = new MeshBuilder();
                post.Box(new Vector3(0f, 1.6f, 0f), new Vector3(0.18f, 3.2f, 0.18f), Quaternion.identity, White);
                post.Box(new Vector3(0f, 3.1f, 0f), new Vector3(1.1f, 0.12f, 0.12f), Quaternion.Euler(0f, 0f, 45f), White);
                post.Box(new Vector3(0f, 3.1f, 0f), new Vector3(1.1f, 0.12f, 0.12f), Quaternion.Euler(0f, 0f, -45f), White);
                post.Box(new Vector3(0f, 1.1f, 0f), new Vector3(0.5f, 0.5f, 0.3f), Quaternion.identity, Black);
                Static(holder, "crossing post", post, postAt, heading + (side > 0 ? 0f : 180f), false);
                var lampOn = new MeshBuilder();
                lampOn.Box(new Vector3(-0.42f, 2.45f, -0.12f), new Vector3(0.28f, 0.28f, 0.1f), Quaternion.identity, SignalRed);
                lampOn.Box(new Vector3(0.42f, 2.45f, -0.12f), new Vector3(0.28f, 0.28f, 0.1f), Quaternion.identity, SignalRed);
                lampOn.Box(new Vector3(0f, 2.45f, 0f), new Vector3(0.6f, 0.08f, 0.08f), Quaternion.identity, Black);
                var lampOff = new MeshBuilder();
                lampOff.Box(new Vector3(-0.42f, 2.45f, -0.12f), new Vector3(0.28f, 0.28f, 0.1f), Quaternion.identity, Black);
                lampOff.Box(new Vector3(0.42f, 2.45f, -0.12f), new Vector3(0.28f, 0.28f, 0.1f), Quaternion.identity, Black);
                lampOff.Box(new Vector3(0f, 2.45f, 0f), new Vector3(0.6f, 0.08f, 0.08f), Quaternion.identity, Black);
                lamps.Add((Static(holder, "lamps on", lampOn, postAt, heading + (side > 0 ? 0f : 180f), false), Static(holder, "lamps off", lampOff, postAt, heading + (side > 0 ? 0f : 180f), false)));
                var pivot = new GameObject("gate pivot").transform;
                pivot.SetParent(holder, false);
                pivot.SetPositionAndRotation(postAt + Vector3.up * 1.1f, Quaternion.Euler(0f, heading + (side > 0 ? 0f : 180f), 0f));
                var arm = new MeshBuilder();
                for (int i = 0; i < 6; i++) arm.Box(new Vector3(-0.3f - i * 0.8f - 0.4f, 0f, 0f), new Vector3(0.8f, 0.14f, 0.1f), Quaternion.identity, i % 2 == 0 ? White : SignalRed);
                Part(pivot, "gate arm", arm, Vector3.zero, false);
                arms.Add(pivot);
            }

            var train = Kinematic(holder, look + " train", origin, Quaternion.identity);
            for (int i = 0; i < cars; i++) Part(train.transform, "car", RailCar(look, carLength), new Vector3(0f, 0f, -i * (carLength + 0.6f) - carLength * 0.5f), true);
            var start = f.Point(0f, -span, out _);
            var end = f.Point(0f, span, out _);
            var travel = (end - start).normalized;
            var trainRotation = Quaternion.LookRotation(travel, Vector3.up);
            var renderers = train.GetComponentsInChildren<Renderer>();
            var colliders = train.GetComponentsInChildren<Collider>();
            bool shown = true, sounded = false;
            var clock = holder.gameObject.AddComponent<HazardClock>();
            clock.StillAt = phase + warn + pass * 0.4f;
            clock.Tick = t =>
            {
                float u = Mod(t - phase, period);
                bool passing = u >= warn && u <= warn + pass;
                if (passing != shown)
                {
                    shown = passing;
                    foreach (var r in renderers) r.enabled = passing;
                    foreach (var c in colliders) c.enabled = passing;
                }
                float front = (u - warn) * speed;
                Move(train, start + travel * Mathf.Clamp(front, 0f, 2f * span + trainLength), trainRotation);
                float down = Mathf.Clamp01(Mathf.Min(u - 1f, warn + pass + 1.5f - u));
                foreach (var a in arms) a.localRotation = Quaternion.Euler(0f, a.localEulerAngles.y, 0f) * Quaternion.Euler(0f, 0f, 0f) * Quaternion.Euler(0f, 0f, Mathf.Lerp(80f, 0f, down));
                bool flashing = u >= 0.5f && u <= warn + pass + 1.5f;
                bool lit = flashing && Mod(t, 1f) < 0.5f;
                foreach (var (on, off) in lamps)
                {
                    on.SetActive(lit);
                    off.SetActive(!lit);
                }
                if (u < warn || u > warn + 1f) sounded = false;
                else if (!sounded)
                {
                    sounded = true;
                    SoundSystem.Play(look == "caltrain" ? "caltrain_horn" : "tram_bell", origin + Vector3.up * 3f, Bus.Ambience, 1f, 1f, 128, 90f);
                }
            };
        }

        // Stalled robotaxis: count white self-driving cars with roof sensors, hazard lights blinking and
        // the odd honk. Solid, and parked off the racing line.
        void Waymos(Frame f, Transform holder, int count, System.Random rng)
        {
            var lights = new List<(GameObject on, GameObject off)>();
            for (int i = 0; i < count; i++)
            {
                var at = f.Point(i * 6.5f, ((float)rng.NextDouble() - 0.5f) * 1.2f, out float h);
                float yaw = h + ((float)rng.NextDouble() - 0.5f) * 24f;
                var mb = new MeshBuilder();
                mb.Box(new Vector3(0f, 0.6f, 0f), new Vector3(1.95f, 0.75f, 4.7f), Quaternion.identity, White);
                mb.Box(new Vector3(0f, 1.25f, -0.2f), new Vector3(1.75f, 0.6f, 2.6f), Quaternion.identity, Glass);
                mb.Box(new Vector3(0f, 1.58f, -0.2f), new Vector3(1.6f, 0.06f, 2.3f), Quaternion.identity, White);
                for (int s = 0; s < 8; s++)
                {
                    float a0 = s * Mathf.PI / 4f, a1 = (s + 1) * Mathf.PI / 4f;
                    var p0 = new Vector3(Mathf.Cos(a0) * 0.32f, 1.62f, Mathf.Sin(a0) * 0.32f - 0.2f);
                    var p1 = new Vector3(Mathf.Cos(a1) * 0.32f, 1.62f, Mathf.Sin(a1) * 0.32f - 0.2f);
                    mb.Quad(p0, p1, p1 + Vector3.up * 0.38f, p0 + Vector3.up * 0.38f, Black, (p0 + p1 - 2f * new Vector3(0f, 1.62f, -0.2f)).normalized);
                    mb.Triangle(new Vector3(0f, 2.0f, -0.2f), p0 + Vector3.up * 0.38f, p1 + Vector3.up * 0.38f, SilverDark, Vector3.up);
                }
                for (int x = -1; x <= 1; x += 2)
                {
                    mb.Box(new Vector3(x * 1.0f, 1.0f, 1.6f), new Vector3(0.18f, 0.22f, 0.18f), Quaternion.identity, Black);
                    for (int z = -1; z <= 1; z += 2) mb.Box(new Vector3(x * 0.85f, 0.3f, z * 1.5f), new Vector3(0.3f, 0.6f, 0.65f), Quaternion.identity, Black);
                }
                var car = Static(holder, "stalled robotaxi", mb, at, yaw, false);
                var box = car.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.9f, 0f);
                box.size = new Vector3(1.95f, 1.8f, 4.7f);
                var on = new MeshBuilder();
                var off = new MeshBuilder();
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        on.Box(new Vector3(x * 0.8f, 0.85f, z * 2.36f), new Vector3(0.3f, 0.14f, 0.04f), Quaternion.identity, Amber);
                        off.Box(new Vector3(x * 0.8f, 0.85f, z * 2.36f), new Vector3(0.3f, 0.14f, 0.04f), Quaternion.identity, AmberOff);
                    }
                lights.Add((Static(holder, "hazards on", on, at, yaw, false), Static(holder, "hazards off", off, at, yaw, false)));
            }
            var spot = f.Point(count * 3f, 0f, out _);
            float nextHonk = 0f;
            holder.gameObject.AddComponent<HazardClock>().Tick = t =>
            {
                bool lit = Mod(t, 0.9f) < 0.45f;
                foreach (var (on, off) in lights)
                {
                    on.SetActive(lit);
                    off.SetActive(!lit);
                }
                if (Time.time < nextHonk) return;
                nextHonk = Time.time + 2.5f + Mod(t * 7.3f, 2f);
                SoundSystem.Play("horn_sedan", spot + Vector3.up, Bus.Vehicles, 0.8f, 1f, 128, 50f);
            };
        }

        // A dockside crane beside the road, side metres off the line, swinging a container on a cable
        // back and forth across the road once every period seconds.
        void Crane(Frame f, Transform holder, float side, float reach, float period, float phase)
        {
            var baseAt = f.Point(0f, side, out float heading);
            var roadAt = f.Point(0f, 0f, out _);
            var toRoad = roadAt - baseAt;
            toRoad.y = 0f;
            float facing = RoutePath.Heading(new Vector2(toRoad.x, toRoad.z));
            const float height = 16f;
            var tower = new MeshBuilder();
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    tower.Box(new Vector3(x * 1.1f, height * 0.5f, z * 1.1f), new Vector3(0.3f, height, 0.3f), Quaternion.identity, CraneYellow);
            for (float y = 2f; y < height; y += 2.5f)
            {
                tower.Box(new Vector3(0f, y, 1.1f), new Vector3(2.4f, 0.15f, 0.15f), Quaternion.Euler(0f, 0f, 40f), CraneYellow);
                tower.Box(new Vector3(0f, y, -1.1f), new Vector3(2.4f, 0.15f, 0.15f), Quaternion.Euler(0f, 0f, -40f), CraneYellow);
                tower.Box(new Vector3(1.1f, y, 0f), new Vector3(0.15f, 0.15f, 2.4f), Quaternion.Euler(40f, 0f, 0f), CraneYellow);
                tower.Box(new Vector3(-1.1f, y, 0f), new Vector3(0.15f, 0.15f, 2.4f), Quaternion.Euler(-40f, 0f, 0f), CraneYellow);
            }
            tower.Box(new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 3.2f), Quaternion.identity, Concrete);
            var towerGo = Static(holder, "crane tower", tower, baseAt, facing, false);
            var towerBox = towerGo.AddComponent<BoxCollider>();
            towerBox.center = new Vector3(0f, height * 0.5f, 0f);
            towerBox.size = new Vector3(3.2f, height, 3.2f);

            var jib = Kinematic(holder, "crane jib", baseAt + Vector3.up * height, Quaternion.Euler(0f, facing, 0f));
            var arm = new MeshBuilder();
            arm.Box(new Vector3(0f, 0.6f, reach * 0.5f), new Vector3(1.2f, 1.2f, reach + 4f), Quaternion.identity, CraneYellow);
            arm.Box(new Vector3(0f, 0.6f, -3.5f), new Vector3(2.4f, 1.6f, 3f), Quaternion.identity, Concrete);
            arm.Box(new Vector3(0f, 2.2f, 0f), new Vector3(1.8f, 2f, 1.8f), Quaternion.identity, CraneYellow);
            arm.Box(new Vector3(0f, 2.4f, 0.95f), new Vector3(1.4f, 0.9f, 0.05f), Quaternion.identity, Glass);
            const float hang = 1.1f;
            arm.Box(new Vector3(0f, -(height - hang - 2.6f) * 0.5f, reach), new Vector3(0.06f, height - hang - 2.6f, 0.06f), Quaternion.identity, Black);
            Part(jib.transform, "jib", arm, Vector3.zero, false);
            var box = new MeshBuilder();
            box.Box(new Vector3(0f, 1.3f, 0f), new Vector3(6.06f, 2.6f, 2.44f), Quaternion.identity, Containers[0]);
            for (int r = -2; r <= 2; r++) box.Box(new Vector3(r * 1.2f, 1.3f, 0f), new Vector3(0.12f, 2.5f, 2.5f), Quaternion.identity, Containers[0] * 0.82f);
            Part(jib.transform, "swinging container", box, new Vector3(0f, -height + hang, reach), true);
            const float swing = 70f;
            var clock = holder.gameObject.AddComponent<HazardClock>();
            clock.StillAt = phase + period * 0.04f;
            clock.Tick = t => Move(jib, jib.transform.position, Quaternion.Euler(0f, facing + swing * Mathf.Sin(2f * Mathf.PI * (t - phase) / period), 0f));
        }

        static MeshBuilder Person()
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.16f, 0.84f, 0.18f), Quaternion.identity, Jeans);
            mb.Box(new Vector3(0.12f, 0.42f, 0f), new Vector3(0.16f, 0.84f, 0.18f), Quaternion.identity, Jeans);
            mb.Box(new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.65f, 0.28f), Quaternion.identity, Coat);
            mb.Box(new Vector3(0f, 1.62f, 0f), new Vector3(0.26f, 0.28f, 0.26f), Quaternion.identity, Skin);
            mb.Box(new Vector3(0f, 1.8f, 0f), new Vector3(0.28f, 0.1f, 0.28f), Quaternion.identity, Red);
            mb.Box(new Vector3(0.32f, 1.05f, 0.25f), new Vector3(0.1f, 0.1f, 0.5f), Quaternion.Euler(20f, 0f, 0f), Coat);
            return mb;
        }

        static MeshBuilder Dog(Color coat, float size)
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0.35f, 0f) * size, new Vector3(0.28f, 0.25f, 0.6f) * size, Quaternion.identity, coat);
            mb.Box(new Vector3(0f, 0.5f, 0.36f) * size, new Vector3(0.22f, 0.22f, 0.25f) * size, Quaternion.identity, coat);
            mb.Box(new Vector3(0f, 0.45f, 0.52f) * size, new Vector3(0.12f, 0.1f, 0.12f) * size, Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.5f, -0.38f) * size, new Vector3(0.05f, 0.05f, 0.25f) * size, Quaternion.Euler(-35f, 0f, 0f), coat);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    mb.Box(new Vector3(x * 0.09f, 0.12f, z * 0.2f) * size, new Vector3(0.07f, 0.24f, 0.07f) * size, Quaternion.identity, coat * 0.85f);
            return mb;
        }

        // A dog walker and three dogs crossing the road: over in nine seconds, a pause on the far kerb,
        // back again. Touching them slows you to half speed for a moment while they leap clear.
        void Walker(Frame f, Transform holder, float halfWidth, float period, float phase)
        {
            f.Point(0f, 0f, out float heading);
            var group = Kinematic(holder, "dog walker", f.Point(0f, -halfWidth, out _), Quaternion.identity);
            var figures = new GameObject("figures").transform;
            figures.SetParent(group.transform, false);
            Part(figures, "walker", Person(), Vector3.zero, false);
            var dogSpots = new[] { new Vector3(-0.9f, 0f, 1.6f), new Vector3(0.3f, 0f, 1.9f), new Vector3(1.1f, 0f, 1.4f) };
            for (int i = 0; i < 3; i++)
            {
                Part(figures, "dog", Dog(DogCoats[i], i == 1 ? 1.4f : 1f), dogSpots[i], false);
                var leash = new MeshBuilder();
                var hand = new Vector3(0.32f, 1.0f, 0.5f);
                var collar = dogSpots[i] + new Vector3(0f, 0.5f, 0.3f);
                leash.Box((hand + collar) * 0.5f, new Vector3(0.03f, 0.03f, Vector3.Distance(hand, collar)), Quaternion.LookRotation(collar - hand), Red);
                Part(figures, "leash", leash, Vector3.zero, false);
            }
            var trigger = group.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.8f, 0.8f);
            trigger.size = new Vector3(3.6f, 1.6f, 3.6f);
            float hopUntil = -1f;
            group.gameObject.AddComponent<SetPieceTrigger>().Hit = car =>
            {
                if (!Cooldown(car, "walker", 1.5f)) return;
                HoldSpeed(car, "walker", 0.5f, 1f);
                hopUntil = Time.time + 0.7f;
                SoundSystem.Play("jump_hup", group.position + Vector3.up * 1.5f, Bus.Sfx);
            };
            const float walk = 9f;
            float half = period * 0.5f;
            var clock = holder.gameObject.AddComponent<HazardClock>();
            clock.StillAt = phase + walk * 0.45f;
            clock.Tick = t =>
            {
                float u = Mod(t - phase, period);
                float s;
                float dir;
                if (u < walk) { s = Mathf.Lerp(-halfWidth, halfWidth, u / walk); dir = 1f; }
                else if (u < half) { s = halfWidth; dir = -1f; }
                else if (u < half + walk) { s = Mathf.Lerp(halfWidth, -halfWidth, (u - half) / walk); dir = -1f; }
                else { s = -halfWidth; dir = 1f; }
                bool moving = u < walk || (u >= half && u < half + walk);
                var p = f.Point(0f, s, out _);
                float hop = Time.time < hopUntil ? Mathf.Sin((hopUntil - Time.time) / 0.7f * Mathf.PI) * 1.2f : 0f;
                figures.localPosition = new Vector3(0f, (moving ? Mathf.Abs(Mathf.Sin(t * 7f)) * 0.06f : 0f) + hop, 0f);
                Move(group, p, Quaternion.Euler(0f, heading + 90f * dir, 0f));
            };
        }
    }

    public sealed class HazardClock : MonoBehaviour
    {
        public Action<float> Tick;
        public float StillAt;

        void FixedUpdate() => Tick?.Invoke(SetPieces.RaceSeconds);
    }
}
