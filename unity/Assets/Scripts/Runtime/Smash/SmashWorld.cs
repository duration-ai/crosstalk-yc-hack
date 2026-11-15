using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Owns everything that breaks. A breakable instance in the world's merged chunks (CROS-19's
    // StaticInstanceIndex) that a car hits hard enough is detached and replaced by a pooled dynamic
    // copy: poles topple over their base, loose props fly with spin, trees snap at the trunk. It
    // scores, puts things back out of view later, wakes parked cars near moving ones, and repairs
    // wrecks nobody is looking at. Only things near a moving car are ever dynamic.
    public sealed class SmashWorld : MonoBehaviour
    {
        public const int MaxPieces = 28;
        const float RespawnSeconds = 25f;
        const float RespawnDistance = 45f;
        const float WakeRadius = 30f;

        public static SmashWorld Instance { get; private set; }

        public StaticInstanceIndex Index { get; private set; }
        public SmashFx Fx { get; private set; }
        public Camera View { get; set; }
        public int ActivePieces => active.Count;
        public int BrokenCount { get; private set; }

        ModelLibrary models;
        Material material;
        public Material Material => material;
        Plane[] frustum = new Plane[6];
        readonly List<Piece> active = new List<Piece>();
        readonly Stack<Piece> pool = new Stack<Piece>();
        readonly List<(int id, float at, GameObject stump)> broken = new List<(int, float, GameObject)>();
        readonly Dictionary<string, (Mesh stump, Mesh crown)> splits = new Dictionary<string, (Mesh, Mesh)>();
        readonly Dictionary<string, Mesh> hulls = new Dictionary<string, Mesh>();
        float nextWake;
        float nextScan;

        sealed class Piece
        {
            public GameObject Go;
            public MeshFilter Filter;
            public Rigidbody Body;
            public MeshCollider Hull;
            public BoxCollider Box;
            public float Spawned;
            public float SettledAt;
            public float Life;
            public Vector3 Scale;
            public Collider[] Ignoring;
        }

        public static SmashWorld Install(GameObject host, StaticInstanceIndex index, ModelLibrary models, Material material, Camera view)
        {
            var world = host.AddComponent<SmashWorld>();
            world.Index = index;
            world.models = models;
            world.material = material;
            world.View = view;
            world.Fx = new SmashFx(host.transform, material);
            host.AddComponent<ThreatMonitor>();
            Instance = world;
            world.AttachToVehicles();
            world.PrecookHulls();
            return world;
        }

        // Every vehicle gets damage and crash physics, whoever spawned it (parked, AI traffic, bots).
        void AttachToVehicles()
        {
            nextScan = Time.time + 1f;
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.GetComponent<VehicleDamage>() == null) car.gameObject.AddComponent<VehicleDamage>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Cooks each breakable variant's convex hull up front, so the first smash of a kind doesn't
        // pay for PhysX cooking mid-crash.
        void PrecookHulls()
        {
            if (Index == null) return;
            var seen = new HashSet<string>();
            foreach (var instance in Index.All)
                if (instance.Class == World.PhysicsClass.Breakable && seen.Add(instance.Variant))
                    HullOf(instance, float.NegativeInfinity);
        }

        public SmashTuning TuningOf(StaticInstance instance) =>
            PhysicsCatalog.Tuning(models?.Entry(instance.Variant)?.role ?? instance.Variant, instance.Kind);

        // Called by a car that hit a breakable's collider. Returns false when the hit was too soft,
        // so the car stops as at a wall. massShare is how much of the car's speed it costs.
        public bool TryBreak(StaticInstance instance, CarController car, CarController scorer, Vector3 point, Vector3 carVelocity, float impactSpeed, float carMass, out float massShare)
        {
            massShare = 0f;
            if (instance.Class != World.PhysicsClass.Breakable || Index.IsDetached(instance.Id)) return false;
            var tuning = TuningOf(instance);
            float threshold = tuning.breakSpeed * Mathf.Clamp(1300f / Mathf.Max(carMass, 1f), 0.4f, 1.6f);
            if (impactSpeed < threshold) return false;

            var mesh = Index.Detach(instance.Id);
            if (mesh == null) return false;
            float mass = Mathf.Clamp(instance.MassKg > 0f ? instance.MassKg : tuning.maxMassKg, 5f, tuning.maxMassKg);
            var style = tuning.Style;
            massShare = mass / (mass + carMass) * (style == SmashStyle.Fly ? 0.6f : 1.1f);
            var flat = new Vector3(carVelocity.x, 0f, carVelocity.z);
            float speed = flat.magnitude;
            var dir = speed > 0.1f ? flat / speed : car.transform.forward;
            var m = instance.Transform;
            var basePos = (Vector3)m.GetColumn(3);
            GameObject stump = null;

            if (style == SmashStyle.Snap)
            {
                float cut = mesh.bounds.min.y + mesh.bounds.size.y * tuning.snapHeight;
                var (stumpMesh, crownMesh) = Split(instance.Variant + "@" + tuning.snapHeight, mesh, cut);
                stump = Stump(stumpMesh, m);
                var piece = Spawn(crownMesh, HullOf(instance, cut), m, mass, Vector3.zero, 12f);
                IgnoreCar(piece, car);
                if (piece != null) Topple(piece, dir, speed * 0.8f, m.MultiplyPoint3x4(new Vector3(0f, cut, 0f)), crownMesh);
                Fx.Debris(basePos + Vector3.up * (cut * m.lossyScale.y + 3f), flat * 0.4f, SmashFx.PaletteOf(crownMesh), 18, 0.42f);
                Fx.Poof(point, 1.4f, SmashFx.SmokeLight, 7);
            }
            else
            {
                var piece = Spawn(mesh, HullOf(instance, float.NegativeInfinity), m, mass, Vector3.zero, style == SmashStyle.Fly ? 9f : 10f);
                IgnoreCar(piece, car);
                if (piece != null && style == SmashStyle.Topple) Topple(piece, dir, speed, basePos, mesh);
                if (piece != null && style == SmashStyle.Fly)
                {
                    piece.Body.linearVelocity = flat * 1.1f + Vector3.up * (3f + speed * 0.3f) + Vector3.Cross(Vector3.up, dir) * Random.Range(-2f, 2f);
                    piece.Body.angularVelocity = Random.onUnitSphere * Random.Range(7f, 12f);
                }
                Fx.Debris(point + Vector3.up * 0.5f, flat * 0.6f, SmashFx.PaletteOf(mesh), style == SmashStyle.Fly ? 8 : 12);
                Fx.Poof(point, 0.9f, SmashFx.SmokeLight, 6);
            }
            if (tuning.spout) Fx.Spout(basePos + Vector3.up * 0.15f, 9f);

            broken.Add((instance.Id, Time.time, stump));
            BrokenCount++;
            if (scorer != null) SmashScore.Add(scorer, SmashKind.Break, tuning.label, tuning.points, point + Vector3.up * 1.2f);
            return true;
        }

        // Tips a piece over its base, away from the car: it spins about the horizontal axis across
        // the car's path, and its centre moves as if hinged at the base, which slides a little.
        static void Topple(Piece piece, Vector3 dir, float speed, Vector3 basePos, Mesh mesh)
        {
            var com = piece.Go.transform.TransformPoint(mesh.bounds.center);
            float h = Mathf.Max(com.y - basePos.y, 0.4f);
            var axis = Vector3.Cross(Vector3.up, dir).normalized;
            var omega = axis * Mathf.Clamp(speed * 0.5f / h, 0.9f, 4.5f) + Vector3.up * Random.Range(-0.8f, 0.8f);
            piece.Body.angularVelocity = omega;
            piece.Body.linearVelocity = Vector3.Cross(omega, com - basePos) + dir * speed * 0.3f + Vector3.up * 0.6f;
        }

        // The instance's Polyfork hull (union of its parts) as a convex collider mesh, optionally only
        // the points above a height: a snapped tree's crown.
        Mesh HullOf(StaticInstance instance, float above)
        {
            var key = instance.Variant + "@" + above;
            if (hulls.TryGetValue(key, out var mesh)) return mesh;
            var parts = Index.HullsOf(instance)?.parts;
            var pts = new List<Vector3>();
            if (parts != null)
                foreach (var part in parts)
                    for (int i = 0; i + 2 < part.pts.Length; i += 3)
                    {
                        var p = new Vector3(part.pts[i], part.pts[i + 1], part.pts[i + 2]);
                        if (p.y >= above) pts.Add(p);
                    }
            if (pts.Count >= 4)
            {
                var tris = new List<int>();
                for (int i = 1; i + 1 < pts.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                mesh = new Mesh { name = key + " hull", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(pts);
                mesh.SetTriangles(tris, 0);
                Physics.BakeMesh(mesh.GetInstanceID(), true);
            }
            hulls[key] = mesh;
            return mesh;
        }

        // A freshly detached piece starts inside the car that broke it; without this, depenetration
        // would fling the car sideways. The car and the piece ignore each other for a moment.
        static void IgnoreCar(Piece piece, CarController car)
        {
            if (piece == null || car == null) return;
            piece.Ignoring = car.GetComponents<Collider>();
            foreach (var c in piece.Ignoring)
            {
                Physics.IgnoreCollision(piece.Hull, c, true);
                Physics.IgnoreCollision(piece.Box, c, true);
            }
        }

        static void StopIgnoring(Piece p)
        {
            if (p.Ignoring == null) return;
            foreach (var c in p.Ignoring)
            {
                if (c == null) continue;
                Physics.IgnoreCollision(p.Hull, c, false);
                Physics.IgnoreCollision(p.Box, c, false);
            }
            p.Ignoring = null;
        }

        (Mesh stump, Mesh crown) Split(string key, Mesh mesh, float cut)
        {
            if (splits.TryGetValue(key, out var cached)) return cached;
            var verts = mesh.vertices;
            var colors = mesh.colors32;
            var tris = mesh.triangles;
            var below = new List<int>();
            var above = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                float y = (verts[tris[i]].y + verts[tris[i + 1]].y + verts[tris[i + 2]].y) / 3f;
                var list = y < cut ? below : above;
                list.Add(tris[i]);
                list.Add(tris[i + 1]);
                list.Add(tris[i + 2]);
            }
            cached = (Sub(key + "/stump", verts, colors, below), Sub(key + "/crown", verts, colors, above));
            splits[key] = cached;
            return cached;
        }

        static Mesh Sub(string name, Vector3[] verts, Color32[] colors, List<int> tris)
        {
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var c = new List<Color32>();
            var t = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                if (!map.TryGetValue(tris[i], out int k))
                {
                    k = v.Count;
                    map[tris[i]] = k;
                    v.Add(verts[tris[i]]);
                    if (colors.Length > 0) c.Add(colors[tris[i]]);
                }
                t[i] = k;
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            if (c.Count == v.Count) mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        GameObject Stump(Mesh mesh, Matrix4x4 m)
        {
            var go = new GameObject("Stump");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        // A free-flying copy of a mesh with a convex collider (Polyfork's hull when there is one,
        // else its bounds), drawn from a pool. Past MaxPieces the oldest piece is recycled. Its body
        // is lighter than the real thing (the car already paid the real mass as a speed penalty), so
        // driving over a fallen pole nudges it aside instead of deflecting the car.
        // round: collide with the mesh's own convex hull (a wheel that should roll) instead of its bounds.
        public GameObject SpawnPiece(Mesh mesh, Matrix4x4 m, float mass, Vector3 velocity, Vector3 angular, float life, bool round = false) =>
            Spawn(mesh, round ? mesh : null, m, mass, velocity, life, angular)?.Go;

        Piece Spawn(Mesh mesh, Mesh hull, Matrix4x4 m, float mass, Vector3 velocity, float life, Vector3? angular = null)
        {
            if (mesh == null || mesh.vertexCount == 0) return null;
            if (active.Count >= MaxPieces) Recycle(active[0], false);
            var p = pool.Count > 0 ? pool.Pop() : CreatePiece();
            p.Go.SetActive(true);
            p.Go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            p.Scale = m.lossyScale;
            p.Go.transform.localScale = p.Scale;
            p.Filter.sharedMesh = mesh;
            if (hull != null)
            {
                p.Hull.sharedMesh = hull;
                p.Hull.enabled = true;
                p.Box.enabled = false;
            }
            else
            {
                p.Hull.enabled = false;
                p.Box.center = mesh.bounds.center;
                p.Box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.1f);
                p.Box.enabled = true;
            }
            p.Body.mass = Mathf.Min(mass * 0.25f, 80f);
            p.Body.linearVelocity = velocity;
            p.Body.angularVelocity = angular ?? Vector3.zero;
            p.Body.WakeUp();
            p.Spawned = Time.time;
            p.SettledAt = -1f;
            p.Life = life;
            active.Add(p);
            return p;
        }

        Piece CreatePiece()
        {
            var go = new GameObject("Piece");
            go.transform.SetParent(transform, false);
            var p = new Piece
            {
                Go = go,
                Filter = go.AddComponent<MeshFilter>(),
                Hull = go.AddComponent<MeshCollider>(),
                Box = go.AddComponent<BoxCollider>(),
                Body = go.AddComponent<Rigidbody>(),
            };
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            p.Hull.convex = true;
            p.Body.interpolation = RigidbodyInterpolation.Interpolate;
            p.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            p.Body.linearDamping = 0.05f;
            p.Body.angularDamping = 0.4f;
            p.Body.sleepThreshold = 0.08f;
            p.Body.solverIterations = 4;
            return p;
        }

        void Recycle(Piece p, bool poof)
        {
            if (poof) Fx.Poof(p.Go.transform.position + Vector3.up * 0.3f, 0.7f, SmashFx.SmokeLight, 5);
            active.Remove(p);
            StopIgnoring(p);
            p.Body.linearVelocity = Vector3.zero;
            p.Body.angularVelocity = Vector3.zero;
            p.Go.SetActive(false);
            pool.Push(p);
        }

        void FixedUpdate()
        {
            if (Time.time >= nextScan) AttachToVehicles();
            if (Time.time >= nextWake)
            {
                nextWake = Time.time + 0.2f;
                WakeNearMovingCars();
            }
        }

        // Parked cars sit kinematic (free for physics) until a moving car comes near, then they
        // turn into real bodies so a barge shoves them by mass instead of hitting a wall.
        static void WakeNearMovingCars()
        {
            var all = VehicleDamage.All;
            for (int i = 0; i < all.Count; i++)
            {
                var mover = all[i];
                if (mover.Body.isKinematic || mover.LastVelocity.sqrMagnitude < 4f) continue;
                var p = mover.transform.position;
                for (int j = 0; j < all.Count; j++)
                {
                    var other = all[j];
                    if (other == mover || !other.Body.isKinematic || other.Car.Driven || other.Car.Autopilot) continue;
                    if ((other.transform.position - p).sqrMagnitude < WakeRadius * WakeRadius) other.Loosen(0.6f);
                }
            }
        }

        void Update()
        {
            Fx.Tick();
            if (View != null) GeometryUtility.CalculateFrustumPlanes(View, frustum);
            var eye = View != null ? View.transform.position : Vector3.zero;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                float age = Time.time - p.Spawned;
                if (age > 0.6f) StopIgnoring(p);
                if (p.SettledAt < 0f && age > 1f && (p.Body.IsSleeping() || p.Body.linearVelocity.sqrMagnitude < 0.04f)) p.SettledAt = Time.time;
                bool expired = age > p.Life || (p.SettledAt > 0f && Time.time - p.SettledAt > 3.5f) || p.Go.transform.position.y < -5f;
                if (!expired) continue;
                float shrink = p.Go.transform.localScale.x / Mathf.Max(p.Scale.x, 1e-3f) - Time.deltaTime * 3f;
                if (shrink <= 0.05f) Recycle(p, true);
                else p.Go.transform.localScale = p.Scale * shrink;
            }

            for (int i = broken.Count - 1; i >= 0; i--)
            {
                var (id, at, stump) = broken[i];
                if (Time.time - at < RespawnSeconds) continue;
                var bounds = Index.Get(id).WorldBounds;
                if ((bounds.center - eye).sqrMagnitude < RespawnDistance * RespawnDistance) continue;
                if (View != null && GeometryUtility.TestPlanesAABB(frustum, bounds)) continue;
                Index.Restore(id);
                if (stump != null) Destroy(stump);
                broken.RemoveAt(i);
            }

            var all = VehicleDamage.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d.Car.Driven || d.Health01 >= 1f || Time.time - d.LastHitTime < RespawnSeconds) continue;
                var b = new Bounds(d.transform.position, d.Car.HalfExtents * 2f);
                if ((b.center - eye).sqrMagnitude < 80f * 80f) continue;
                if (View != null && GeometryUtility.TestPlanesAABB(frustum, b)) continue;
                d.Repair();
            }
        }
    }
}
