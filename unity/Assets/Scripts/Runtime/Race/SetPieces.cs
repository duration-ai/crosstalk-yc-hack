using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dogpatch.Gameplay;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // One set piece from a course json's "setPieces" list. lat/lon place it; unless free, it snaps to
    // the racing line there, offset metres to the right of it, and yaw turns it from the route's
    // direction. A free piece stands exactly at lat/lon with yaw as its compass heading. The size and
    // count fields mean what each type says below; 0 takes the type's default.
    [Serializable]
    public class SetPieceData
    {
        public string type;
        public double lat;
        public double lon;
        public bool free;
        public float offset;
        public float yaw;
        public float length;
        public float width;
        public float height;
        public int count;
        public float spacing;
        public string look;
        public float period;
        public float phase;
        public GeoPoint[] path;
    }

    [Serializable]
    class SetPieceList
    {
        public SetPieceData[] setPieces;
    }

    // The race course's set pieces: ramps, boost pads, puddles and knock-over props, built fresh from
    // the course json when the racers go to the grid and removed when the race is left. They only
    // exist for players racing that route, so free roam and other routes never see them. Everything
    // on the racing line is loose and light (bots plough through it); the solid pieces stand off it.
    public sealed partial class SetPieces : MonoBehaviour
    {
        const float ClearOfGrid = 30f;

        static readonly Color Ply = PaintJob.Linear("#D99A55");
        static readonly Color PlyDark = PaintJob.Linear("#A8703A");
        static readonly Color Orange = PaintJob.Linear("#FF6A1A");
        static readonly Color White = PaintJob.Linear("#F4F4EE");
        static readonly Color Black = PaintJob.Linear("#23262E");
        static readonly Color Yellow = PaintJob.Linear("#FFCB1F");
        static readonly Color Cyan = PaintJob.Linear("#2EE6FF");
        static readonly Color Navy = PaintJob.Linear("#1C2347");
        static readonly Color Concrete = PaintJob.Linear("#A9A69C");
        static readonly Color Dirt = PaintJob.Linear("#8A5A34");
        static readonly Color DirtLight = PaintJob.Linear("#A8754A");
        static readonly Color Water = PaintJob.Linear("#3B5875");
        static readonly Color WaterLight = PaintJob.Linear("#6F92B3");
        static readonly Color Lime = PaintJob.Linear("#7BE32B");
        static readonly Color LimeDark = PaintJob.Linear("#3E9B1E");
        static readonly Color Steel = PaintJob.Linear("#8D96A3");
        static readonly Color Wood = PaintJob.Linear("#9C6B43");
        static readonly Color WoodDark = PaintJob.Linear("#7E5536");
        static readonly Color Leaf = PaintJob.Linear("#3F8F3A");
        static readonly Color Red = PaintJob.Linear("#D9372B");
        static readonly Color ScaffoldTop = PaintJob.Linear("#E2601A");
        static readonly Color[] Containers = { PaintJob.Linear("#B5442E"), PaintJob.Linear("#2F6DB5"), PaintJob.Linear("#2F8F5B"), PaintJob.Linear("#D9A32B") };

        RaceDirector race;
        RaceCourse course;
        Material material;
        Transform root;
        string builtFor;
        float gridEnd;
        readonly Dictionary<(CarController, string), float> holds = new Dictionary<(CarController, string), float>();

        public static SetPieces Instance { get; private set; }
        public Transform Root => root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Instance = new GameObject("SpeedDialSetPieces").AddComponent<SetPieces>();

        void Update()
        {
            race = RaceDirector.Instance;
            string want = race != null && race.Setup && race.Gridded ? race.RouteId : null;
            if (want == builtFor) return;
            if (root != null) Destroy(root.gameObject);
            root = null;
            builtFor = want;
            if (want != null) Build(want);
        }

        void Build(string route)
        {
            var list = JsonUtility.FromJson<SetPieceList>(Resources.Load<TextAsset>(route.Length > 0 ? "SpeedDial/course-" + route : "SpeedDial/course").text).setPieces;
            if (list == null || list.Length == 0) return;
            course = race.Course;
            material = race.Game.SharedMaterial;
            gridEnd = course.Grid.Max(g => g.Progress) + ClearOfGrid;
            root = new GameObject("SetPieces " + course.Data.routeName).transform;
            root.SetParent(race.Game.World.Root.transform, false);
            var tally = new Dictionary<string, int>();
            for (int i = 0; i < list.Length; i++)
            {
                var piece = list[i];
                try
                {
                    var frame = new Frame(this, piece);
                    var at = frame.Point(0f, 0f, out float facing);
                    var holder = new GameObject($"{i:00} {piece.type}").transform;
                    holder.SetParent(root, false);
                    holder.SetPositionAndRotation(at, Quaternion.Euler(0f, facing, 0f));
                    Spawn(piece, frame, holder, i);
                    tally[piece.type] = tally.TryGetValue(piece.type, out var n) ? n + 1 : 1;
                    Debug.Log($"[SetPieces] {piece.type} at ({at.x:F0},{at.y:F1},{at.z:F0}){(piece.free ? " free" : $", {frame.D:F0} m along the route")}");
                }
                catch (Exception e) { Debug.LogWarning($"[SetPieces] {piece.type} #{i} failed: {e.Message}"); }
            }
            GhostBots();
            Debug.Log($"[SetPieces] {course.Data.routeName}: {list.Length} pieces ({string.Join(", ", tally.Select(kv => kv.Value + " " + kv.Key))})");
            Built?.Invoke(root);
        }

        public static event Action<Transform> Built;

        // Where a piece's parts go: on the racing line, along metres further on and side metres to the
        // right, or around a free piece's spot.
        readonly struct Frame
        {
            readonly RaceCourse course;
            readonly bool free;
            readonly float offset;
            readonly float yaw;
            readonly Vector2 origin;
            public readonly float D;

            public Frame(SetPieces owner, SetPieceData piece)
            {
                course = owner.course;
                free = piece.free;
                offset = piece.offset;
                yaw = piece.yaw;
                origin = course.Project(new GeoPoint { lat = piece.lat, lon = piece.lon });
                D = free ? 0f : Mathf.Max(course.Route.Project(origin), owner.gridEnd);
            }

            public Vector3 Point(float along, float side, out float heading)
            {
                if (!free)
                {
                    float at = Mathf.Clamp(D + along, 0f, course.Route.Length);
                    var p = course.Route.PointAt(at) + course.Route.RightAt(at) * (offset + side);
                    heading = RoutePath.Heading(course.Route.DirectionAt(at)) + yaw;
                    return new Vector3(p.x, course.RouteY(at), p.y);
                }
                var w = Quaternion.Euler(0f, yaw, 0f) * new Vector3(side + offset, 0f, along);
                var q = origin + new Vector2(w.x, w.z);
                heading = yaw;
                return new Vector3(q.x, course.GroundY(q), q.y);
            }
        }

        void Spawn(SetPieceData p, Frame f, Transform holder, int index)
        {
            var rng = new System.Random(index * 7919 + 17);
            switch (p.type)
            {
                case "ramp": Ramp(f, holder, Or(p.length, 6f), Or(p.width, 6f), Or(p.height, 1.1f), 0f, false); break;
                case "dock": Ramp(f, holder, Or(p.length, 5f), Or(p.width, 5f), Or(p.height, 1.2f), Or(p.spacing, 8f), false); break;
                case "mound": Ramp(f, holder, Or(p.length, 5f), Or(p.width, 6f), Or(p.height, 1.3f), 0f, true); break;
                case "boost": Boost(f, holder, Or(p.length, 6f), Or(p.width, 3.5f)); break;
                case "puddle": Puddle(f, holder, Or(p.width, 4f), rng); break;
                case "cones": Cones(f, holder, Math.Max(p.count, 12), Or(p.spacing, 4f), p.width); break;
                case "chicane": Chicane(f, holder, Math.Max(p.count, 4), Or(p.spacing, 14f), Or(p.width, 5.5f), rng); break;
                case "scaffold": Scaffold(f, holder, Math.Max(p.count, 3), Or(p.spacing, 6f), Or(p.width, 3.6f), Or(p.height, 4.2f)); break;
                case "scooters": Scooters(f, holder, Math.Max(p.count, 6), Or(p.length, 8f), rng); break;
                case "parklet": Parklet(f, holder, Or(p.length, 7f)); break;
                case "shortcut": Shortcut(p, holder); break;
                case "crossing": Crossing(f, holder, p.look ?? "caltrain", Or(p.period, 20f), p.phase, Or(p.width, 34f)); break;
                case "waymos": Waymos(f, holder, Math.Max(p.count, 3), rng); break;
                case "crane": Crane(f, holder, p.width != 0f ? p.width : 14f, Or(p.length, 15f), Or(p.period, 14f), p.phase); break;
                case "walker": Walker(f, holder, Or(p.width, 9f), Or(p.period, 24f), p.phase); break;
                default: throw new ArgumentException("unknown set piece type " + p.type);
            }
        }

        static float Or(float v, float fallback) => v > 0f ? v : fallback;

        GameObject Static(Transform parent, string name, MeshBuilder mb, Vector3 at, float heading, bool collide, int layer = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, heading, 0f));
            go.layer = layer;
            var mesh = mb.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // A loose prop: one rigidbody with a box collider around its mesh, asleep until something hits it.
        Rigidbody Loose(Transform parent, string name, MeshBuilder mb, Vector3 at, Quaternion rotation, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, rotation);
            var mesh = mb.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.12f);
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.4f;
            body.Sleep();
            return body;
        }

        void Trigger(Transform parent, string name, Vector3 at, float heading, Vector3 size, Action<CarController, Vector3> hit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, heading, 0f));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            go.AddComponent<SetPieceTrigger>().Hit = car => hit(car, go.transform.forward);
        }

        bool Cooldown(CarController car, string kind, float seconds)
        {
            var key = (car, kind + "-cd");
            if (holds.TryGetValue(key, out var until) && Time.time < until) return false;
            holds[key] = Time.time + seconds;
            return true;
        }

        // Scales the car's top speed by k for a while; touching the same kind again only extends it.
        void HoldSpeed(CarController car, string kind, float k, float seconds)
        {
            var key = (car, kind);
            bool running = holds.TryGetValue(key, out var until) && Time.time < until;
            holds[key] = Time.time + seconds;
            if (!running) StartCoroutine(Hold(car, key, k));
        }

        IEnumerator Hold(CarController car, (CarController, string) key, float k)
        {
            car.SpeedMul *= k;
            while (holds.TryGetValue(key, out var until) && Time.time < until) yield return null;
            if (car != null) car.SpeedMul /= k;
        }

        static float ForwardSpeed(CarController car, Vector3 along)
        {
            var body = car.GetComponent<Rigidbody>();
            return body != null ? Vector3.Dot(body.linearVelocity, along) : 0f;
        }

        // ramp: a plywood wedge, length long and height high at the lip, launching cars that leave it.
        // dock (flat > 0): a concrete loading dock, the ramp up then flat metres of platform to the drop.
        // mound (twoSided): a dirt jump, up and straight back down.
        void Ramp(Frame f, Transform holder, float length, float width, float height, float flat, bool twoSided)
        {
            var at = f.Point(0f, 0f, out float heading);
            var mb = new MeshBuilder();
            float hw = width * 0.5f;
            const int bands = 4;
            for (int i = 0; i < bands; i++)
            {
                float z0 = length * i / bands, z1 = length * (i + 1) / bands;
                var c = twoSided ? (i % 2 == 0 ? Dirt : DirtLight) : flat > 0f ? (i % 2 == 0 ? Concrete : Yellow) : (i % 2 == 0 ? Ply : Orange);
                mb.Quad(new Vector3(-hw, height * z0 / length, z0), new Vector3(hw, height * z0 / length, z0), new Vector3(hw, height * z1 / length, z1), new Vector3(-hw, height * z1 / length, z1), c, Vector3.up);
            }
            var side = twoSided ? Dirt : flat > 0f ? Concrete : PlyDark;
            mb.Triangle(new Vector3(-hw, 0f, 0f), new Vector3(-hw, 0f, length), new Vector3(-hw, height, length), side, Vector3.left);
            mb.Triangle(new Vector3(hw, 0f, 0f), new Vector3(hw, 0f, length), new Vector3(hw, height, length), side, Vector3.right);
            float lip = length + flat;
            if (flat > 0f)
            {
                mb.Box(new Vector3(0f, height * 0.5f, length + flat * 0.5f), new Vector3(width, height, flat), Quaternion.identity, Concrete);
                mb.Box(new Vector3(0f, height + 0.01f, lip - 0.25f), new Vector3(width, 0.02f, 0.5f), Quaternion.identity, Yellow);
                for (int i = -1; i <= 1; i += 2) mb.Box(new Vector3(i * (hw - 0.6f), height * 0.55f, lip + 0.1f), new Vector3(0.5f, 0.4f, 0.2f), Quaternion.identity, Black);
            }
            else if (twoSided)
            {
                mb.Quad(new Vector3(-hw, height, length), new Vector3(hw, height, length), new Vector3(hw, 0f, length * 2f), new Vector3(-hw, 0f, length * 2f), DirtLight, Vector3.up + Vector3.forward);
                mb.Triangle(new Vector3(-hw, height, length), new Vector3(-hw, 0f, length), new Vector3(-hw, 0f, length * 2f), Dirt, Vector3.left);
                mb.Triangle(new Vector3(hw, height, length), new Vector3(hw, 0f, length), new Vector3(hw, 0f, length * 2f), Dirt, Vector3.right);
            }
            else
            {
                mb.Quad(new Vector3(-hw, 0f, length), new Vector3(hw, 0f, length), new Vector3(hw, height, length), new Vector3(-hw, height, length), PlyDark, Vector3.forward);
                mb.Box(new Vector3(0f, height + 0.005f, length - 0.2f), new Vector3(width, 0.02f, 0.4f), Quaternion.identity, Black);
            }
            Static(holder, twoSided ? "mound" : flat > 0f ? "dock" : "ramp", mb, at, heading, true, Freeway.DrivableLayer);
            float up = twoSided ? 5.5f : flat > 0f ? 5f : 7f;
            var lipAt = at + Quaternion.Euler(0f, heading, 0f) * new Vector3(0f, height - 0.3f, lip - 0.6f);
            Trigger(holder, "lip", lipAt, heading, new Vector3(width, 1.6f, 1.2f), (car, fwd) =>
            {
                float speed = ForwardSpeed(car, fwd);
                if (speed < 5f || !Cooldown(car, "launch", 0.8f)) return;
                CarFx.On(car).Launch(Mathf.Clamp(speed * 0.3f, 3.5f, up));
            });
        }

        void Boost(Frame f, Transform holder, float length, float width)
        {
            var at = f.Point(0f, 0f, out float heading);
            var mb = new MeshBuilder();
            float hw = width * 0.5f;
            mb.Box(new Vector3(0f, 0.02f, length * 0.5f), new Vector3(width, 0.04f, length), Quaternion.identity, Navy);
            for (int i = 0; i < 3; i++)
            {
                float z = length * (0.2f + i * 0.28f);
                var c = i == 1 ? Yellow : Cyan;
                var a = new Vector3(-hw + 0.3f, 0.05f, z);
                var b = new Vector3(0f, 0.05f, z + 1.1f);
                var e = new Vector3(hw - 0.3f, 0.05f, z);
                mb.Quad(a, a + Vector3.forward * 0.5f, b + Vector3.forward * 0.5f, b, c, Vector3.up);
                mb.Quad(b, b + Vector3.forward * 0.5f, e + Vector3.forward * 0.5f, e, c, Vector3.up);
            }
            Static(holder, "boost pad", mb, at, heading, false);
            Trigger(holder, "boost", at + Quaternion.Euler(0f, heading, 0f) * new Vector3(0f, 0f, length * 0.5f), heading, new Vector3(width, 1.8f, length), (car, fwd) =>
            {
                if (!Cooldown(car, "boost", 1f)) return;
                HoldSpeed(car, "boost", 1.4f, 2f);
                CarFx.On(car).Push(car.transform.forward * 7f);
            });
        }

        void Puddle(Frame f, Transform holder, float size, System.Random rng)
        {
            var at = f.Point(0f, 0f, out float heading);
            var mb = new MeshBuilder();
            const int n = 9;
            var rim = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float r = size * 0.5f * (0.75f + (float)rng.NextDouble() * 0.35f);
                rim[i] = new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r * 1.3f);
            }
            for (int i = 0; i < n; i++) mb.Triangle(new Vector3(0f, 0.03f, 0f), rim[i], rim[(i + 1) % n], Water, Vector3.up);
            mb.Triangle(new Vector3(-0.4f, 0.035f, 0.2f), new Vector3(0.6f, 0.035f, 0.5f), new Vector3(0.1f, 0.035f, -0.5f), WaterLight, Vector3.up);
            Static(holder, "puddle", mb, at, heading, false);
            Trigger(holder, "splash", at, heading, new Vector3(size * 0.85f, 1.5f, size * 1.1f), (car, fwd) => HoldSpeed(car, "puddle", 0.7f, 1f));
        }

        static MeshBuilder Cone()
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.04f, 0.42f), Quaternion.identity, Black);
            var top = new Vector3(0f, 0.72f, 0f);
            const float r = 0.16f;
            var c = new[] { new Vector3(-r, 0.04f, -r), new Vector3(r, 0.04f, -r), new Vector3(r, 0.04f, r), new Vector3(-r, 0.04f, r) };
            for (int i = 0; i < 4; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % 4];
                var outward = new Vector3(a.x + b.x, 0f, a.z + b.z);
                var mid0 = Vector3.Lerp(a, top, 0.45f);
                var mid1 = Vector3.Lerp(b, top, 0.45f);
                var hi0 = Vector3.Lerp(a, top, 0.62f);
                var hi1 = Vector3.Lerp(b, top, 0.62f);
                mb.Quad(a, b, mid1, mid0, Orange, outward);
                mb.Quad(mid0, mid1, hi1, hi0, White, outward);
                mb.Triangle(hi0, hi1, top, Orange, outward);
            }
            return mb;
        }

        static MeshBuilder Barricade()
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < 4; i++) mb.Box(new Vector3(-0.6f + i * 0.4f, 0.85f, 0f), new Vector3(0.4f, 0.28f, 0.06f), Quaternion.identity, i % 2 == 0 ? Orange : White);
            for (int i = -1; i <= 1; i += 2)
            {
                mb.Box(new Vector3(i * 0.7f, 0.5f, 0.15f), new Vector3(0.06f, 1f, 0.06f), Quaternion.Euler(-12f, 0f, 0f), Steel);
                mb.Box(new Vector3(i * 0.7f, 0.5f, -0.15f), new Vector3(0.06f, 1f, 0.06f), Quaternion.Euler(12f, 0f, 0f), Steel);
            }
            return mb;
        }

        // A lane closure: a taper of cones from width metres further out in to the piece's own offset,
        // barricades behind it and a ROAD WORK diamond up front. All of it knocks over.
        void Cones(Frame f, Transform holder, int count, float spacing, float taper)
        {
            int taperCount = Mathf.Max(count / 3, 1);
            for (int i = 0; i < count; i++)
            {
                float side = Mathf.Lerp(taper, 0f, Mathf.Clamp01(i / (float)taperCount));
                var at = f.Point(i * spacing, side, out float h);
                Loose(holder, "cone", Cone(), at, Quaternion.Euler(0f, h, 0f), 4f);
                if (i % 4 != 2) continue;
                var b = f.Point(i * spacing, side + 1.6f, out float bh);
                Loose(holder, "barricade", Barricade(), b, Quaternion.Euler(0f, bh, 0f), 14f);
            }
            var signAt = f.Point(-8f, taper + 1.5f, out float sh);
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), Quaternion.identity, Steel);
            mb.Box(new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 0.9f, 0.05f), Quaternion.Euler(0f, 0f, 45f), Orange);
            mb.Box(new Vector3(0f, 1.6f, -0.03f), new Vector3(0.5f, 0.12f, 0.02f), Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.6f), Quaternion.identity, Black);
            Loose(holder, "road work sign", mb, signAt, Quaternion.Euler(0f, sh + 180f, 0f), 10f);
        }

        // Shipping containers alternating sides every spacing metres, width metres off the line, with
        // loose barrels between them and the line, so the fast way through is a weave.
        void Chicane(Frame f, Transform holder, int count, float spacing, float width, System.Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float s = i % 2 == 0 ? 1f : -1f;
                var at = f.Point(i * spacing, s * width, out float h);
                var mb = new MeshBuilder();
                var color = Containers[rng.Next(Containers.Length)];
                mb.Box(new Vector3(0f, 1.3f, 0f), new Vector3(2.44f, 2.6f, 6.06f), Quaternion.identity, color);
                for (int r = -2; r <= 2; r++) mb.Box(new Vector3(0f, 1.3f, r * 1.2f), new Vector3(2.5f, 2.5f, 0.12f), Quaternion.identity, color * 0.82f);
                mb.Box(new Vector3(0f, 1.3f, 3.05f), new Vector3(2.3f, 2.4f, 0.06f), Quaternion.identity, color * 0.7f);
                Static(holder, "container", mb, at, h, true);
                for (int b = 0; b < 3; b++)
                {
                    var bAt = f.Point(i * spacing - 1.2f + b * 1.2f, s * (width - 1.9f - (b % 2) * 0.7f), out float bh);
                    Loose(holder, "barrel", Barrel(b % 2 == 0 ? Orange : Containers[1]), bAt, Quaternion.Euler(0f, bh + b * 25f, 0f), 22f);
                }
            }
        }

        static MeshBuilder Barrel(Color color)
        {
            var mb = new MeshBuilder();
            const int sides = 8;
            const float r = 0.3f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                var p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                var n = (p0 + p1).normalized;
                mb.Quad(p0, p1, p1 + Vector3.up * 0.4f, p0 + Vector3.up * 0.4f, color, n);
                mb.Quad(p0 + Vector3.up * 0.4f, p1 + Vector3.up * 0.4f, p1 + Vector3.up * 0.5f, p0 + Vector3.up * 0.5f, White, n);
                mb.Quad(p0 + Vector3.up * 0.5f, p1 + Vector3.up * 0.5f, p1 + Vector3.up * 0.9f, p0 + Vector3.up * 0.9f, color, n);
                mb.Triangle(Vector3.up * 0.9f, p0 + Vector3.up * 0.9f, p1 + Vector3.up * 0.9f, color * 0.8f, Vector3.up);
            }
            return mb;
        }

        // Scaffold towers on both sides, width metres off the line, each one loose piece that topples.
        void Scaffold(Frame f, Transform holder, int count, float spacing, float width, float height)
        {
            for (int i = 0; i < count; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    var at = f.Point(i * spacing + (s > 0 ? spacing * 0.5f : 0f), s * width, out float h);
                    var mb = new MeshBuilder();
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                            mb.Box(new Vector3(x * 0.7f, height * 0.5f, z * 1.1f), new Vector3(0.08f, height, 0.08f), Quaternion.identity, Steel);
                    for (float y = 1.4f; y < height; y += 1.4f)
                    {
                        mb.Box(new Vector3(0f, y, 0f), new Vector3(1.5f, 0.06f, 2.3f), Quaternion.identity, Wood);
                        for (int x = -1; x <= 1; x += 2) mb.Box(new Vector3(x * 0.7f, y - 0.7f, 0f), new Vector3(0.05f, 0.05f, 2.6f), Quaternion.Euler(32f, 0f, 0f), Steel);
                    }
                    mb.Box(new Vector3(0f, height - 0.2f, 0f), new Vector3(1.55f, 0.4f, 2.35f), Quaternion.identity, ScaffoldTop);
                    Loose(holder, "scaffold", mb, at, Quaternion.Euler(0f, h, 0f), 45f);
                }
        }

        static MeshBuilder Scooter()
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.06f, 0.75f), Quaternion.identity, Lime);
            mb.Box(new Vector3(0f, 0.1f, 0.42f), new Vector3(0.06f, 0.2f, 0.2f), Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.1f, -0.4f), new Vector3(0.06f, 0.2f, 0.2f), Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.62f, 0.4f), new Vector3(0.06f, 1f, 0.06f), Quaternion.Euler(-8f, 0f, 0f), LimeDark);
            mb.Box(new Vector3(0f, 1.1f, 0.47f), new Vector3(0.5f, 0.05f, 0.05f), Quaternion.identity, Black);
            mb.Box(new Vector3(0f, 0.9f, 0.45f), new Vector3(0.14f, 0.12f, 0.04f), Quaternion.identity, White);
            return mb;
        }

        // Lime-style rental scooters left in a heap: count of them over length metres, a third fallen over.
        void Scooters(Frame f, Transform holder, int count, float length, System.Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                float along = (float)rng.NextDouble() * length;
                float side = ((float)rng.NextDouble() - 0.5f) * 2.2f;
                var at = f.Point(along, side, out float h);
                float spin = h + 60f + (float)rng.NextDouble() * 60f;
                bool down = i % 3 == 0;
                Loose(holder, "scooter", Scooter(), at + (down ? Vector3.up * 0.1f : Vector3.zero), Quaternion.Euler(0f, spin, down ? 88f : 0f), 12f);
            }
        }

        // A café parklet in the parking lane: deck, railing, planters, tables, chairs and an umbrella,
        // every part loose so a car smashes straight through.
        void Parklet(Frame f, Transform holder, float length)
        {
            f.Point(0f, 0f, out float h);
            var rot = Quaternion.Euler(0f, h, 0f);
            Vector3 P(float x, float z) => f.Point(z, x, out _);
            var lift = Vector3.up * 0.16f;
            for (int i = 0; i < 3; i++)
            {
                var mb = new MeshBuilder();
                mb.Box(new Vector3(0f, 0.08f, 0f), new Vector3(2.2f, 0.16f, length / 3f - 0.05f), Quaternion.identity, i % 2 == 0 ? Wood : WoodDark);
                Loose(holder, "deck", mb, P(0f, length * (i + 0.5f) / 3f), rot, 35f);
            }
            for (int i = 0; i < 2; i++)
            {
                var mb = new MeshBuilder();
                mb.Box(new Vector3(0f, 0.6f, 0f), new Vector3(0.06f, 0.08f, length * 0.5f - 0.1f), Quaternion.identity, Black);
                for (float z = -length * 0.25f + 0.2f; z < length * 0.25f; z += 0.6f) mb.Box(new Vector3(0f, 0.38f, z), new Vector3(0.04f, 0.5f, 0.04f), Quaternion.identity, Black);
                Loose(holder, "railing", mb, P(-1.1f, length * (0.25f + i * 0.5f)) + lift, rot, 12f);
            }
            for (int i = 0; i < 2; i++)
            {
                var mb = new MeshBuilder();
                mb.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.7f, 0.6f, 0.7f), Quaternion.identity, Ply);
                mb.Box(new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 0.45f, 0.6f), Quaternion.Euler(0f, 45f, 0f), Leaf);
                Loose(holder, "planter", mb, P(0f, i == 0 ? 0.4f : length - 0.4f) + lift, rot, 20f);
            }
            for (int t = 0; t < 2; t++)
            {
                float z = length * (0.33f + t * 0.34f);
                var table = new MeshBuilder();
                table.Box(new Vector3(0f, 0.72f, 0f), new Vector3(0.7f, 0.05f, 0.7f), Quaternion.identity, White);
                table.Box(new Vector3(0f, 0.36f, 0f), new Vector3(0.06f, 0.72f, 0.06f), Quaternion.identity, Black);
                Loose(holder, "table", table, P(0.1f, z) + lift, rot, 7f);
                for (int c = -1; c <= 1; c += 2)
                {
                    var chair = new MeshBuilder();
                    chair.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.05f, 0.42f), Quaternion.identity, Red);
                    chair.Box(new Vector3(0f, 0.7f, -0.2f), new Vector3(0.42f, 0.45f, 0.04f), Quaternion.identity, Red);
                    chair.Box(new Vector3(0f, 0.22f, 0f), new Vector3(0.05f, 0.45f, 0.05f), Quaternion.identity, Black);
                    Loose(holder, "chair", chair, P(0.1f, z + c * 0.65f) + lift, Quaternion.Euler(0f, h + (c > 0 ? 180f : 0f), 0f), 3f);
                }
            }
            var umbrella = new MeshBuilder();
            umbrella.Box(new Vector3(0f, 1.1f, 0f), new Vector3(0.05f, 2.2f, 0.05f), Quaternion.identity, White);
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                umbrella.Triangle(new Vector3(0f, 2.45f, 0f), new Vector3(Mathf.Cos(a0) * 1.2f, 2.05f, Mathf.Sin(a0) * 1.2f), new Vector3(Mathf.Cos(a1) * 1.2f, 2.05f, Mathf.Sin(a1) * 1.2f), i % 2 == 0 ? Red : White, Vector3.up);
            }
            umbrella.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.1f, 0.5f), Quaternion.identity, Black);
            Loose(holder, "umbrella", umbrella, P(0.4f, length * 0.5f) + lift, rot, 6f);
        }

        // An unofficial shortcut: lime chevrons painted along path (lat/lon points) and a loose arrow
        // sign at its mouth.
        void Shortcut(SetPieceData p, Transform holder)
        {
            if (p.path == null || p.path.Length < 2) throw new ArgumentException("shortcut needs a path of two or more points");
            var pts = p.path.Select(g => course.Project(g)).ToArray();
            var mb = new MeshBuilder();
            var origin = new Vector3(pts[0].x, course.GroundY(pts[0]), pts[0].y);
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var dir = (b - a).normalized;
                var right = new Vector2(dir.y, -dir.x);
                for (float d = 2f; d < Vector2.Distance(a, b) - 1f; d += 5f)
                {
                    var c = a + dir * d;
                    float y = course.GroundY(c) + 0.05f - origin.y;
                    Vector3 W(Vector2 q) => new Vector3(q.x - origin.x, y, q.y - origin.z);
                    var tip = c + dir * 1.2f;
                    mb.Quad(W(c + right * 1.3f), W(c + right * 1.3f + dir * 0.5f), W(tip + dir * 0.5f), W(tip), Lime, Vector3.up);
                    mb.Quad(W(tip), W(tip + dir * 0.5f), W(c - right * 1.3f + dir * 0.5f), W(c - right * 1.3f), Lime, Vector3.up);
                }
            }
            Static(holder, "shortcut chevrons", mb, origin, 0f, false);
            var first = (pts[1] - pts[0]).normalized;
            var sign = new MeshBuilder();
            sign.Box(new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), Quaternion.identity, Steel);
            sign.Box(new Vector3(0f, 1.8f, 0f), new Vector3(1.4f, 0.6f, 0.06f), Quaternion.identity, Lime);
            sign.Box(new Vector3(0.1f, 1.8f, -0.04f), new Vector3(0.8f, 0.14f, 0.02f), Quaternion.identity, Black);
            sign.Triangle(new Vector3(0.5f, 2f, -0.05f), new Vector3(0.5f, 1.6f, -0.05f), new Vector3(0.75f, 1.8f, -0.05f), Black, Vector3.back);
            sign.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.08f, 0.6f), Quaternion.identity, Black);
            var at = new Vector3(pts[0].x - first.y * 3f, course.GroundY(pts[0]), pts[0].y + first.x * 3f);
            Loose(holder, "shortcut sign", sign, at, Quaternion.Euler(0f, RoutePath.Heading(first) + 90f, 0f), 10f);
        }
    }

    public sealed class SetPieceTrigger : MonoBehaviour
    {
        public Action<CarController> Hit;

        void OnTriggerEnter(Collider other)
        {
            var body = other.attachedRigidbody;
            var car = body != null ? body.GetComponent<CarController>() : null;
            if (car != null) Hit?.Invoke(car);
        }
    }
}
