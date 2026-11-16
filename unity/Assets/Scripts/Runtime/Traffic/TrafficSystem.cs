using System.Collections.Generic;
using System.Diagnostics;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Dogpatch.Traffic
{
    // Ambient traffic around the player: robotaxis, everyday cars and delivery riders spawned out of
    // view on the real lane graph and despawned (pooled) when far or behind the camera; two Muni 55
    // buses on their real loop with stops; two T Third trains on the Third Street tracks with their
    // platforms; e-bikes at the Bay Wheels docks; and a row of parked roster vehicles by the start.
    // Near agents move their kinematic bodies every physics step with colliders on; far ones step at
    // a third of the rate with colliders and shadows off.
    public sealed partial class TrafficSystem : MonoBehaviour
    {
        const float NearRadius = 90f;
        const float SpawnMin = 80f;
        const float SpawnMax = 210f;
        const float KeepRadius = 150f;
        static int RobotaxiQuota => Vehicles.Pace.Current.robotaxiQuota;
        static int EverydayQuota => Vehicles.Pace.Current.everydayQuota;
        static int RiderQuota => Vehicles.Pace.Current.riderQuota;

        static readonly string[] RobotaxiTypes = { VehicleTypes.WaymoSuv, VehicleTypes.WaymoSuv, VehicleTypes.WaymoSuv, VehicleTypes.WaymoOjai, VehicleTypes.WaymoOjai, VehicleTypes.WaymoVan, VehicleTypes.Zoox, VehicleTypes.Zoox, VehicleTypes.Zoox };
        static readonly string[] EverydayTypes = { VehicleTypes.Hatchback, VehicleTypes.Hatchback, VehicleTypes.Sedan, VehicleTypes.Sedan, VehicleTypes.YellowCab, VehicleTypes.YellowCab, VehicleTypes.Cybertruck, VehicleTypes.TechShuttle };
        static readonly string[] RiderTypes = { VehicleTypes.DoorDashBike, VehicleTypes.UberEatsBike };

        public static TrafficSystem Instance { get; private set; }

        sealed class Zone
        {
            public int Id;
            public Vector2[] Points;
            public float Radius;
            public float Until;
        }

        static readonly List<Zone> zones = new List<Zone>();
        static int nextZoneId = 1;

        // Closes part of the map to AI traffic for a while (a race start and its first stretch):
        // nothing spawns inside, AI cars inside despawn, the bus and the trains hide until it
        // reopens, and everything else turns away from it or holds at its edge. The zone is every
        // point within radius of the path (one point for a circle). Returns an id for EndZone.
        public static int ClearZone(Vector3 center, float radius, float seconds) => ClearZone(new[] { center }, radius, seconds);

        public static int ClearZone(IReadOnlyList<Vector3> path, float radius, float seconds)
        {
            var points = new Vector2[path.Count];
            for (int i = 0; i < path.Count; i++) points[i] = new Vector2(path[i].x, path[i].z);
            var zone = new Zone { Id = nextZoneId++, Points = points, Radius = radius, Until = Time.time + seconds };
            zones.Add(zone);
            return zone.Id;
        }

        // Reopens a zone before its time is up (a countdown that was called off).
        public static void EndZone(int id) => zones.RemoveAll(z => z.Id == id);

        public static bool HasZones => zones.Count > 0;

        public static bool InZone(Vector2 p, float pad = 0f)
        {
            for (int z = zones.Count - 1; z >= 0; z--)
            {
                var zone = zones[z];
                if (Time.time >= zone.Until)
                {
                    zones.RemoveAt(z);
                    continue;
                }
                float r = zone.Radius + pad;
                var pts = zone.Points;
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[Mathf.Min(i + 1, pts.Length - 1)];
                    var ab = b - a;
                    float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    if ((a + ab * t - p).sqrMagnitude <= r * r) return true;
                }
            }
            return false;
        }

        public static bool Crosses(StreetGraph.Lane lane, float pad)
        {
            if (zones.Count == 0) return false;
            var path = lane.Path;
            for (float s = 0f; s < path.Length + 4f; s += 4f)
                if (InZone(path.Sample(Mathf.Min(s, path.Length), out _), pad)) return true;
            return false;
        }

        public StreetGraph Graph { get; private set; }
        public readonly List<TrafficAgent> Agents = new List<TrafficAgent>();
        public CabRide Ride { get; private set; }
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public float FrameMs { get; private set; }
        public float AverageMs { get; private set; }
        public float MaxMs { get; private set; }

        // Parked or abandoned cars count as obstacles only when they sit within reach of a lane
        // centreline; the thousands parked along the curbs never block a lane.
        GameController game;
        Transform root;
        TrafficWorld data;
        readonly Dictionary<string, Stack<CarController>> pool = new Dictionary<string, Stack<CarController>>();
        readonly List<CarController> parked = new List<CarController>();
        CarController[] allCars = System.Array.Empty<CarController>();
        float nextCarListing;
        readonly List<StreetGraph.Lane> candidates = new List<StreetGraph.Lane>();
        readonly Stopwatch watch = new Stopwatch();
        readonly Dictionary<CarController, GameObject> playerRiders = new Dictionary<CarController, GameObject>();
        float frameAccumulator;
        float sumMs;
        int sampleFrames;
        float nextParkedScan;
        float nextCandidates;
        float nextLog;
        float nextOverlapLog = 60f;
        Vector3 lastFocus;
        int tick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            GameController.WorldReady -= Start;
            GameController.WorldReady += Start;
        }

        static void Start(GameController game)
        {
            if (Instance != null) return;
            game.gameObject.AddComponent<TrafficSystem>().Init(game);
        }

        void Init(GameController controller)
        {
            Instance = this;
            game = controller;
            root = new GameObject("Traffic").transform;
            var playerRole = game.Models.Role("player");
            VehicleRoster.Configure(game.SharedMaterial, playerRole.Count > 0 ? game.Models.Get(playerRole[0].id) : null);

            var t0 = Time.realtimeSinceStartup;
            data = TrafficWorld.Load();
            Graph = new StreetGraph(data);
            int stops = 0, signals = 0;
            foreach (var n in Graph.Nodes)
            {
                if (n.Control == NodeControl.Stop) stops++;
                if (n.Control == NodeControl.Signal) signals++;
            }
            Debug.Log($"[Traffic] lane graph: {Graph.Lanes.Count} lanes, {Graph.Nodes.Count} nodes, {signals} signals, {stops} stop junctions ({(data.HasControls ? "extract signs and signals" : "derived controls")}) in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");

            var fallback = TransitFallback.Load();
            SpawnTrains(fallback);
            SpawnBuses(fallback);
            SpawnDockBikes(fallback);
            SpawnShowroom();
            for (int i = 0; i < 600 && Deficit(out _) > 0; i++) TrySpawn(true);
            lastFocus = Focus;

            Ride = gameObject.AddComponent<CabRide>();
            Ride.System = this;
            GameController.VehicleEntered += OnEntered;
            GameController.VehicleExited += OnExited;
            VehicleRoster.Swapped += OnSwapped;
            LogOverlaps("startup", true);
            Debug.Log($"[Traffic] started with {Agents.Count} agents in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
        }

        void OnDestroy()
        {
            GameController.VehicleEntered -= OnEntered;
            GameController.VehicleExited -= OnExited;
            VehicleRoster.Swapped -= OnSwapped;
            if (Instance == this) Instance = null;
        }

        Vector3 Focus => game.Focus != null ? game.Focus.position : Vector3.zero;

        bool Visible(Vector3 p)
        {
            var cam = game.View;
            if (cam == null) return false;
            var v = cam.WorldToViewportPoint(p + Vector3.up);
            return v.z > 0f && v.x > -0.15f && v.x < 1.15f && v.y > -0.15f && v.y < 1.15f;
        }

        void FixedUpdate()
        {
            watch.Restart();
            tick++;
            GatherObstacles();
            var focus = Focus;
            float dt = Time.fixedDeltaTime;
            for (int i = Agents.Count - 1; i >= 0; i--)
            {
                if (i >= Agents.Count) continue;
                var agent = Agents[i];
                float d = Vector3.Distance(agent.transform.position, focus);
                bool fixedRoute = agent.OnRail || agent.Route != null || agent.Car == game.Current;
                if (zones.Count > 0 && agent.Car != game.Current)
                {
                    var p = agent.transform.position;
                    bool closed = InZone(new Vector2(p.x, p.z), agent.Car.HalfExtents.z);
                    if (closed && !fixedRoute)
                    {
                        Despawn(agent);
                        continue;
                    }
                    if (agent.gameObject.activeSelf == closed) agent.gameObject.SetActive(!closed);
                    if (closed) continue;
                }
                else if (!agent.gameObject.activeSelf) agent.gameObject.SetActive(true);
                if (!fixedRoute && (d > 240f || (d > KeepRadius && !Visible(agent.transform.position)) || (agent.Idle > 40f && !Visible(agent.transform.position))))
                {
                    Despawn(agent);
                    continue;
                }
                bool far = d > NearRadius && agent.Car != game.Current;
                if (far != agent.Far) SetFar(agent, far);
                if (far && (tick + i) % 3 != 0) continue;
                agent.Step(far ? dt * 3f : dt, this);
            }
            if ((focus - lastFocus).sqrMagnitude > SpawnMin * SpawnMin)
            {
                nextCandidates = 0f;
                for (int i = 0; i < 600 && Deficit(out _) > 0; i++) TrySpawn(true);
            }
            else if (tick % 2 == 0) TrySpawn(false);
            if (tick % 25 == 0) Separate();
            lastFocus = focus;
            watch.Stop();
            frameAccumulator += (float)watch.Elapsed.TotalMilliseconds;
        }

        void Update()
        {
            FrameMs = frameAccumulator;
            frameAccumulator = 0f;
            sumMs += FrameMs;
            sampleFrames++;
            MaxMs = Mathf.Max(MaxMs, FrameMs);
            if (Time.time >= nextOverlapLog)
            {
                nextOverlapLog = Time.time + 60f;
                if (Time.time > 1f) LogOverlaps($"after {Time.time / 60f:F0} min", false);
            }
            if (Time.time < nextLog) return;
            nextLog = Time.time + 5f;
            AverageMs = sumMs / Mathf.Max(1, sampleFrames);
            int near = 0, trafficInView = 0, parkedInView = 0;
            foreach (var a in Agents)
            {
                if (!a.Far) near++;
                if (a.gameObject.activeSelf && Visible(a.transform.position)) trafficInView++;
            }
            foreach (var car in allCars)
                if (car != null && car.gameObject.activeInHierarchy && !car.Autopilot && car != game.Current && Visible(car.transform.position)) parkedInView++;
            Debug.Log($"[Traffic] agents={Agents.Count} near={near} parked={parked.Count} inView={trafficInView}+{parkedInView} cost avg={AverageMs:F3} ms/frame max={MaxMs:F3} ms over {sampleFrames} frames");
            sumMs = 0f;
            sampleFrames = 0;
            MaxMs = 0f;
        }

        void GatherObstacles()
        {
            Obstacles.Clear();
            foreach (var a in Agents)
            {
                var f = a.transform.forward;
                var fwd = new Vector2(f.x, f.z).normalized;
                Obstacles.Add(new Obstacle
                {
                    Position = new Vector2(a.transform.position.x, a.transform.position.z), Forward = fwd, Velocity = fwd * a.Speed,
                    Half = new Vector2(a.Car.HalfExtents.x, a.Car.HalfExtents.z), Owner = a, Agent = true, Player = a.Car == game.Current,
                });
            }

            var current = game.Current;
            if (current != null)
            {
                if (current.GetComponent<TrafficAgent>() is TrafficAgent riding && riding.enabled) { }
                else Obstacles.Add(CarObstacle(current, true));
            }
            else if (game.Focus != null)
            {
                var t = game.Focus;
                Obstacles.Add(new Obstacle { Position = new Vector2(t.position.x, t.position.z), Forward = new Vector2(t.forward.x, t.forward.z), Half = new Vector2(0.4f, 0.4f), Owner = t, Player = true });
            }

            if (Time.time >= nextParkedScan)
            {
                nextParkedScan = Time.time + 1f;
                parked.Clear();
                var focus = Focus;
                if (Time.time >= nextCarListing)
                {
                    nextCarListing = Time.time + 10f;
                    allCars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
                }
                foreach (var car in allCars)
                {
                    if (car == null || car == current || !car.enabled) continue;
                    var agent = car.GetComponent<TrafficAgent>();
                    if (agent != null && agent.enabled) continue;
                    var p = car.transform.position;
                    if ((p - focus).sqrMagnitude < 260f * 260f && Graph.Nearest(new Vector2(p.x, p.z), Vector2.zero, 1.4f + car.HalfExtents.z, out _, out _) != null) parked.Add(car);
                }
            }
            foreach (var car in parked)
                if (car != null && car.gameObject.activeInHierarchy && car != current) Obstacles.Add(CarObstacle(car, false));
        }

        static Obstacle CarObstacle(CarController car, bool player)
        {
            var f = car.transform.forward;
            var fwd = new Vector2(f.x, f.z).normalized;
            return new Obstacle
            {
                Position = new Vector2(car.transform.position.x, car.transform.position.z), Forward = fwd, Velocity = fwd * car.Speed,
                Half = new Vector2(car.HalfExtents.x, car.HalfExtents.z), Owner = car, Player = player,
            };
        }

        void SetFar(TrafficAgent agent, bool far)
        {
            agent.Far = far;
            foreach (var c in agent.Car.GetComponents<Collider>()) c.enabled = !far;
            agent.GetComponent<Rigidbody>().interpolation = far ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            var shadows = far ? ShadowCastingMode.Off : ShadowCastingMode.On;
            foreach (var r in agent.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = shadows;
        }

        int Deficit(out string type)
        {
            int robotaxis = 0, everyday = 0, riders = 0;
            foreach (var a in Agents)
            {
                if (a.OnRail || a.Route != null) continue;
                if (a.Spec.Robotaxi) robotaxis++;
                else if (a.Spec.Rider) riders++;
                else everyday++;
            }
            int dr = Quota(RobotaxiQuota) - robotaxis, de = Quota(EverydayQuota) - everyday, dd = Quota(RiderQuota) - riders;
            int best = Mathf.Max(dr, Mathf.Max(de, dd));
            type = best == dr ? RobotaxiTypes[Random.Range(0, RobotaxiTypes.Length)]
                : best == de ? EverydayTypes[Random.Range(0, EverydayTypes.Length)]
                : RiderTypes[Random.Range(0, RiderTypes.Length)];
            return best;
        }

        void TrySpawn(bool initial)
        {
            if (Deficit(out string type) <= 0) return;
            var focus = Focus;
            var f2 = new Vector2(focus.x, focus.z);
            if (Time.time >= nextCandidates || candidates.Count == 0)
            {
                nextCandidates = Time.time + 2f;
                candidates.Clear();
                candidates.AddRange(Graph.LanesNear(f2, SpawnMax));
            }
            if (candidates.Count == 0) return;
            var lane = candidates[Random.Range(0, candidates.Count)];
            var spec = VehicleCatalog.Get(type);
            if (spec.Rider && lane.Index != lane.Count - 1) return;
            if (lane.Path.Length < 12f) return;
            float s = Random.Range(4f, lane.Path.Length - 6f);
            var p = lane.Path.Sample(s, out _);
            var world = new Vector3(p.x, RoadSurface.GroundY(p) + 0.05f, p.y);
            if (InZone(p, 8f)) return;
            float d = Vector2.Distance(p, f2);
            if (d > (initial ? KeepRadius : SpawnMax) || d < (initial ? 25f : SpawnMin)) return;
            if (!initial && d < 170f && Visible(world)) return;
            foreach (var a in Agents)
                if ((a.transform.position - world).sqrMagnitude < 14f * 14f) return;
            foreach (var car in parked)
                if (car != null && (car.transform.position - world).sqrMagnitude < 6f * 6f) return;
            var agent = Spawn(type, lane, s, Mathf.Min(spec.CruiseSpeed, lane.Speed) * 0.7f);
            if (Overlapping(agent.Car, 1f)) Despawn(agent);
        }

        static readonly Collider[] overlapHits = new Collider[16];
        readonly List<CarController> agentCars = new List<CarController>();

        // Kinematic traffic never pushes itself apart, so two AI cars that end up inside each other
        // (a merge into the same lane) are parted by removing one, the hidden one when there is a choice.
        void Separate()
        {
            agentCars.Clear();
            foreach (var a in Agents)
                if (a.gameObject.activeSelf && a.Car != game.Current) agentCars.Add(a.Car);
            foreach (var (a, b) in VehicleSpacing.OverlappingPairs(agentCars, -0.1f))
            {
                var ta = a.GetComponent<TrafficAgent>();
                var tb = b.GetComponent<TrafficAgent>();
                bool fixedA = ta.OnRail || ta.Route != null, fixedB = tb.OnRail || tb.Route != null;
                if (fixedA && fixedB) continue;
                var drop = fixedB || (!fixedA && Visible(b.transform.position) && !Visible(a.transform.position)) ? ta : tb;
                if (Agents.Contains(drop)) Despawn(drop);
            }
        }

        // Whether a car's body overlaps another vehicle's, gap included: parked and player cars through
        // physics (their colliders are on), traffic through footprints (far agents have colliders off).
        bool Overlapping(CarController car, float gap)
        {
            var mine = VehicleSpacing.Of(car);
            foreach (var a in Agents)
                if (a.Car != car && VehicleSpacing.Overlap(mine, VehicleSpacing.Of(a.Car), gap)) return true;
            var box = car.GetComponent<BoxCollider>();
            var t = car.transform;
            int n = Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), box.size * 0.5f + new Vector3(gap, 0f, gap), overlapHits, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var body = overlapHits[i].attachedRigidbody;
                var other = body != null ? body.GetComponent<CarController>() : null;
                if (other != null && other != car) return true;
            }
            return false;
        }

        // Every pair of vehicles whose bodies are inside one another by more than a few centimetres.
        void LogOverlaps(string when, bool includeInactive)
        {
            var cars = new List<CarController>(FindObjectsByType<CarController>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            var pairs = VehicleSpacing.OverlappingPairs(cars, -0.1f);
            int traffic = 0, mixed = 0, parkedPairs = 0, player = 0;
            foreach (var (a, b) in pairs)
            {
                if (a == game.Current || b == game.Current) player++;
                else
                {
                    bool ta = a.Autopilot, tb = b.Autopilot;
                    if (ta && tb) traffic++;
                    else if (ta || tb) mixed++;
                    else parkedPairs++;
                }
            }
            Debug.Log($"[Overlap] {when}: {pairs.Count} overlapping pairs among {cars.Count} vehicles (traffic-traffic {traffic}, traffic-parked {mixed}, parked-parked {parkedPairs}, with the player {player})");
            foreach (var (a, b) in pairs.GetRange(0, Mathf.Min(pairs.Count, 5)))
                Debug.Log($"[Overlap]   {a.VehicleId} ({a.ModelId}) at {a.transform.position} with {b.VehicleId} ({b.ModelId}) at {b.transform.position}");
        }

        public TrafficAgent Spawn(string type, StreetGraph.Lane lane, float s, float speed)
        {
            var car = Take(type);
            var agent = Attach(car, type);
            agent.PlaceOnLane(lane, s, speed);
            return agent;
        }

        CarController Take(string type)
        {
            if (pool.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            return VehicleRoster.Spawn(type, Vector3.zero, 0f, root);
        }

        TrafficAgent Attach(CarController car, string type)
        {
            var agent = car.GetComponent<TrafficAgent>();
            if (agent == null) agent = car.gameObject.AddComponent<TrafficAgent>();
            agent.Init(car, VehicleCatalog.Get(type));
            Agents.Add(agent);
            SetFar(agent, false);
            return agent;
        }

        // The AI lets go of a car for good: it was hit off its route, wrecked, or the player took it.
        public void Release(TrafficAgent agent)
        {
            if (!Agents.Remove(agent)) return;
            if (agent.Link != null) agent.Link.From.To.Occupants--;
            agent.enabled = false;
            agent.Car.Autopilot = false;
            agent.Speed = 0f;
            agent.Car.SetAutopilotMotion(0f, 0f);
            foreach (var c in agent.Car.GetComponents<Collider>()) c.enabled = true;
        }

        public void Despawn(TrafficAgent agent)
        {
            if (!Agents.Remove(agent)) return;
            if (agent.Link != null) agent.Link.From.To.Occupants--;
            agent.Link = null;
            agent.enabled = false;
            agent.gameObject.SetActive(false);
            if (!pool.TryGetValue(agent.Spec.Id, out var stack)) pool[agent.Spec.Id] = stack = new Stack<CarController>();
            stack.Push(agent.Car);
        }

        void OnEntered(CarController car)
        {
            var agent = car.GetComponent<TrafficAgent>();
            bool riding = agent != null && agent.enabled && CabRide.IsCab(agent.Spec) && Ride.Begin(agent);
            if (!riding && agent != null && agent.enabled && !car.Handling.PassengerOnly)
            {
                Release(agent);
                car.SetDriven(true);
            }
            var rider = VehicleRoster.MountPlayer(car);
            if (rider != null) playerRiders[car] = rider;
        }

        void OnExited(CarController car)
        {
            if (playerRiders.TryGetValue(car, out var rider))
            {
                Destroy(rider);
                playerRiders.Remove(car);
            }
        }

        void OnSwapped(CarController car)
        {
            var agent = car.GetComponent<TrafficAgent>();
            if (agent == null || !agent.enabled) return;
            agent.Spec = VehicleRoster.SpecOf(car);
            car.Autopilot = true;
        }

        // Two T Third trains, one each way, on the two longest light-rail lines (Third Street's tracks),
        // northbound on the east track as on the real line, dwelling at the platforms along them.
        void SpawnTrains(TransitFallback fallback)
        {
            var lines = new List<TrackPath>();
            foreach (var rail in data.rails)
            {
                if (rail.kind != "light_rail" && rail.kind != "tram" || rail.pts == null || rail.pts.Length < 4) continue;
                var pts = new Vector2[rail.pts.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(rail.pts[i * 2], rail.pts[i * 2 + 1]);
                var path = new TrackPath(pts);
                if (path.Length > 800f) lines.Add(path);
            }
            lines.Sort((a, b) => b.Length.CompareTo(a.Length));
            if (lines.Count == 0)
            {
                Debug.LogWarning("[Traffic] no T Third track in the extract");
                return;
            }
            var a = lines[0];
            var b = lines.Count > 1 ? lines[1] : Reverse(a);
            if (Vector2.Distance(a.Points[a.Points.Length - 1], b.Points[0]) > Vector2.Distance(a.Points[a.Points.Length - 1], b.Points[b.Points.Length - 1])) b = Reverse(b);
            var mid = a.Sample(a.Length * 0.5f, out var t);
            var other = b.Sample(b.Closest(mid, out _), out _);
            if (Vector2.Dot(other - mid, new Vector2(t.y, -t.x)) > 0f)
            {
                var ra = Reverse(b);
                b = Reverse(a);
                a = ra;
            }
            var rails = new[] { a, b };

            var names = new List<(Vector2 p, string name)>();
            foreach (var s in fallback.railStops) names.Add((s.Position, s.name));
            var centers = new List<Vector2>();
            foreach (var platform in data.platforms)
                if (platform.LightRail && platform.pts != null && platform.pts.Length >= 6) centers.Add(platform.Center);
            if (centers.Count == 0) foreach (var s in fallback.railStops) centers.Add(s.Position);

            var stops = new List<RouteStop>();
            foreach (var c in centers)
            {
                float sa = rails[0].Closest(c, out float da);
                float sb = rails[1].Closest(c, out float db);
                int index = da <= db ? 0 : 1;
                if (Mathf.Min(da, db) > 9f) continue;
                string name = "T Third";
                float best = 60f;
                foreach (var (p, n) in names)
                    if (Vector2.Distance(p, c) < best) { best = Vector2.Distance(p, c); name = n; }
                float s0 = index == 0 ? sa : sb;
                if (stops.Exists(x => x.Index == index && Mathf.Abs(x.S - s0) < 60f)) continue;
                stops.Add(new RouteStop { Index = index, S = s0, Name = name });
            }
            stops.Sort((x, y) => x.Index != y.Index ? x.Index.CompareTo(y.Index) : x.S.CompareTo(y.S));

            for (int i = 0; i < 2; i++)
            {
                var car = Take(VehicleTypes.TThird);
                var agent = Attach(car, VehicleTypes.TThird);
                agent.PlaceOnRail(rails, i, rails[i].Length * (i == 0 ? 0.12f : 0.4f), stops);
            }
            Debug.Log($"[Traffic] T Third: tracks {a.Length:F0} m and {b.Length:F0} m, {stops.Count} platform stops: {string.Join(", ", stops.ConvertAll(s => s.Name + (s.Index == 0 ? " (N)" : " (S)")))}");
        }

        static TrackPath Reverse(TrackPath path)
        {
            var pts = (Vector2[])path.Points.Clone();
            System.Array.Reverse(pts);
            return new TrackPath(pts);
        }

        // The Muni 55 loop through Dogpatch, routed on the lane graph through its OSM waypoints, with
        // its OSM stops; two buses half a loop apart.
        void SpawnBuses(TransitFallback fallback)
        {
            var w = fallback.busRoute.waypoints;
            var lanes = new List<StreetGraph.Lane>();
            for (int i = 0; i + 3 < w.Length; i += 2)
            {
                var p = new Vector2(w[i], w[i + 1]);
                var q = new Vector2(w[i + 2], w[i + 3]);
                var lane = Graph.Nearest(p, (q - p).normalized, 35f, out _, out _);
                if (lane == null) lane = Graph.Nearest(p, Vector2.zero, 35f, out _, out _);
                if (lane != null) lanes.Add(lane);
            }
            var route = new List<StreetGraph.Lane>();
            for (int i = 0; i < lanes.Count; i++)
            {
                var leg = Graph.Route(lanes[i], lanes[(i + 1) % lanes.Count]);
                if (leg == null)
                {
                    Debug.LogWarning($"[Traffic] Muni {fallback.busRoute.@ref}: no lane route from lane {lanes[i].Id} to {lanes[(i + 1) % lanes.Count].Id}");
                    return;
                }
                for (int k = route.Count == 0 ? 0 : 1; k < leg.Count; k++) route.Add(leg[k]);
            }
            if (route.Count > 1 && route[route.Count - 1] == route[0]) route.RemoveAt(route.Count - 1);
            if (route.Count < 2) return;

            var stops = new List<RouteStop>();
            int from = 0;
            foreach (var stop in fallback.busRoute.stops)
            {
                for (int k = 0; k < route.Count; k++)
                {
                    int idx = (from + k) % route.Count;
                    float s = route[idx].Path.Closest(stop.Position, out float d);
                    if (d > 12f || s < 8f) continue;
                    stops.Add(new RouteStop { Index = idx, S = s, Name = stop.name });
                    from = idx;
                    break;
                }
            }
            stops.Sort((x, y) => x.Index != y.Index ? x.Index.CompareTo(y.Index) : x.S.CompareTo(y.S));

            float total = 0f;
            foreach (var l in route) total += l.Path.Length;
            for (int b = 0; b < 2; b++)
            {
                int index = b == 0 ? 0 : route.Count / 2;
                var agent = Spawn(VehicleTypes.MuniBus, route[index], 0.5f, 0f);
                agent.FollowRoute(route, index, stops);
            }
            Debug.Log($"[Traffic] Muni {fallback.busRoute.@ref}: {route.Count} lanes, {total:F0} m loop, {stops.Count} stops");
        }

        // E-bikes standing in the Bay Wheels docks. CROS-19's extract draws the docks themselves; with
        // the older extract the dock is drawn here too, at its OSM position.
        void SpawnDockBikes(TransitFallback fallback)
        {
            var docks = new List<(Vector2 p, float heading, int capacity, bool draw)>();
            foreach (var prop in data.Props("bicycle_rental"))
            {
                if (docks.Exists(d => Vector2.Distance(d.p, prop.Position) < 25f)) continue;
                docks.Add((prop.Position, prop.headingKnown ? prop.heading : RoadHeading(prop.Position), prop.Capacity, false));
            }
            if (docks.Count == 0)
                foreach (var dock in fallback.docks) docks.Add((dock.Position, RoadHeading(dock.Position), dock.capacity, true));

            int bikes = 0;
            foreach (var (p, heading, capacity, draw) in docks)
            {
                var axis = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
                int count = Mathf.Clamp(capacity / 6, 2, 5);
                if (draw)
                {
                    var model = ProceduralModels.BikeDock(game.SharedMaterial, count + 1);
                    model.transform.SetParent(root, false);
                    model.transform.SetPositionAndRotation(new Vector3(p.x - axis.x * 1.4f, 0.03f, p.y - axis.y * 1.4f), Quaternion.Euler(0f, heading - 90f, 0f));
                }
                for (int i = 0; i < count; i++)
                {
                    var q = p + axis * ((i - (count - 1) * 0.5f) * 0.9f);
                    VehicleRoster.Spawn(VehicleTypes.BayWheels, new Vector3(q.x, 0.05f, q.y), heading + 90f, root).gameObject.AddComponent<FarCull>();
                    bikes++;
                }
            }
            Debug.Log($"[Traffic] Bay Wheels: {bikes} e-bikes at {docks.Count} docks");
        }

        float RoadHeading(Vector2 p)
        {
            if (!game.World.Roads.Nearest(p, 40f, out var hit)) return 0f;
            return Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg;
        }

        // A few roster vehicles parked by the start so every type is a short walk away: a steel wedge
        // truck and a race car at the curb, a shopping cart and a scooter on the sidewalk.
        void SpawnShowroom()
        {
            var hero = game.World.HeroCar;
            var spawn = game.World.PlayerSpawn;
            var fwd = new Vector3(Mathf.Sin(hero.Heading * Mathf.Deg2Rad), 0f, Mathf.Cos(hero.Heading * Mathf.Deg2Rad));
            void Park(string type, Vector3 at, float heading)
            {
                if (game.World.Buildings.Blocked(new Vector2(at.x, at.z), 0.6f)) return;
                var car = VehicleRoster.Spawn(type, at, heading, root);
                Physics.SyncTransforms();
                if (Overlapping(car, 0.4f)) Destroy(car.gameObject);
            }
            Park(VehicleTypes.RaceCar, hero.Position - fwd * 8.5f, hero.Heading);
            Park(VehicleTypes.Cybertruck, hero.Position + fwd * 9.5f, hero.Heading);
            Park(VehicleTypes.ShoppingCart, spawn.Position + fwd * 3f, hero.Heading + 20f);
            Park(VehicleTypes.Scooter, spawn.Position + fwd * 5f, hero.Heading - 15f);
        }
    }
}
