using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Ambient;
using Dogpatch.Backend;
using Dogpatch.Data;
using Dogpatch.Glb;
using Dogpatch.Smash;
using Dogpatch.Vehicles;
using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Gameplay
{
    // Builds the city, owns the player and the cars, turns drags and taps into movement, and
    // keeps the backend in sync. Backend-owned state (player, position, mode, vehicles) only
    // ever flows through IGameBackend.
    public sealed class GameController : MonoBehaviour
    {
        const string PlayerIdKey = "dogpatch.playerId";
        const float EnterReach = 12f;
        const float SyncSeconds = 2f;
        const float ParkedBumperGap = 0.6f;

        IGameBackend backend;
        WorldContext world;
        ModelLibrary models;
        Material material;
        DragInput input;
        float? reverseYaw;
        FollowCamera follow;
        Camera view;
        Hud hud;
        PlayerController player;
        Player profile;
        readonly Dictionary<string, CarController> cars = new Dictionary<string, CarController>();
        CarController current;
        bool ready;
        bool busy;
        float nextSync;
        float nextLog;
        Vector3 lastSafe;

        // Raised once the city, player and parked cars exist; vehicle and traffic systems start here.
        public static event Action<GameController> WorldReady;
        public static event Action<CarController> VehicleEntered;
        public static event Action<CarController> VehicleExited;

        public bool Driving => current != null;
        public bool IsReady => ready;
        public bool Riding => current != null && current.Handling.PassengerOnly;
        public CarController Current => current;
        public Transform Focus => current != null ? current.transform : player != null ? player.transform : null;
        public CarController NearestCar { get; private set; }
        public Camera View => view;
        public bool ExternalInput;
        public IGameBackend Backend => backend;
        public Player Profile => profile;
        public WorldContext World => world;
        public WorldData Data { get; private set; }
        public ModelLibrary Models => models;
        public Material SharedMaterial => material;
        public PlayerController Player => player;
        public CarController CurrentCar => current;
        public IReadOnlyDictionary<string, CarController> Cars => cars;
        public FollowCamera Follow => follow;
        public DragInput Drag => input;

        public Task EnterCar(CarController car) => RunAsync(() => EnterCarAsync(car));

        public Task ExitCar() => current == null ? Task.CompletedTask : RunAsync(ExitCarAsync);
        public string StreetName { get; private set; } = "";
        public string StatusLine { get; private set; } = "Loading Dogpatch…";

        async void Start()
        {
            Application.targetFrameRate = 60;
            gameObject.AddComponent<ScreenShape>();
            material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
            SetupRendering();

            var data = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("World/dogpatch").text);
            Data = data;
            models = new ModelLibrary();
            float t0 = Time.realtimeSinceStartup;
            world = WorldBuilder.Build(data, models, material);
            PerfReadout.WorldBuildSeconds = Time.realtimeSinceStartup - t0;
            gameObject.AddComponent<PerfReadout>().Game = this;
            Debug.Log($"[Dogpatch] world built in {Time.realtimeSinceStartup - t0:F1}s: {data.roads.Length} roads, {data.buildings.Length} buildings, {world.ParkedCars.Count + 1} cars");
            PedestrianSystem.Create(world, data);

            input = gameObject.AddComponent<DragInput>();
            input.Tapped += OnTap;
            hud = gameObject.AddComponent<Hud>();
            hud.Game = this;
            hud.Input = input;
            gameObject.AddComponent<Look.MotionFx>().Game = this;

            var local = new InMemoryGameBackend();
            var seed = new List<(string, Vec3, float)> { (models.Role("car")[0].id, ToVec(world.HeroCar.Position), world.HeroCar.Heading) };
            var carModels = models.Role("car");
            var rng = new System.Random(7);
            string[] curbside = { VehicleTypes.Sedan, VehicleTypes.Sedan, VehicleTypes.Sedan, VehicleTypes.Hatchback, VehicleTypes.Hatchback, VehicleTypes.YellowCab, VehicleTypes.Cybertruck };
            var lot = new VehicleSpacing.Lot();
            lot.Add(VehicleSpacing.At(VehicleSpacing.BoundsOf(seed[0].Item1, models), new Vector2(world.HeroCar.Position.x, world.HeroCar.Position.z), world.HeroCar.Heading));
            int crowded = 0, thinned = 0;
            float keep = Pace.Current.parkedKeepShare;
            for (int i = 0; i < world.ParkedCars.Count; i++)
            {
                var spot = world.ParkedCars[i];
                if (Mathf.FloorToInt((i + 1) * keep) == Mathf.FloorToInt(i * keep))
                {
                    thinned++;
                    continue;
                }
                string type = curbside[rng.Next(curbside.Length)];
                string model = rng.NextDouble() < 0.7 && VehicleCatalog.Has(type) ? type : carModels[rng.Next(carModels.Count)].id;
                var at = new Vector2(spot.Position.x, spot.Position.z);
                var foot = VehicleSpacing.At(VehicleSpacing.BoundsOf(model, models), at, spot.Heading);
                if (lot.Blocked(foot, ParkedBumperGap))
                {
                    model = VehicleTypes.Hatchback;
                    foot = VehicleSpacing.At(VehicleSpacing.BoundsOf(model, models), at, spot.Heading);
                    if (lot.Blocked(foot, ParkedBumperGap))
                    {
                        crowded++;
                        continue;
                    }
                }
                lot.Add(foot);
                seed.Add((model, ToVec(spot.Position), spot.Heading));
            }
            Debug.Log($"[Dogpatch] parked {seed.Count - 1} cars with a {ParkedBumperGap} m bumper gap; {thinned} spaces thinned out, {crowded} left empty because the car would not fit");
            local.SeedVehicles(seed);
            backend = local;

            try
            {
                await LoadFromBackendAsync();
                ready = true;
                StatusLine = "On foot";
                WorldReady?.Invoke(this);
            }
            catch (Exception e)
            {
                Debug.LogError("[Dogpatch] startup failed: " + e);
                StatusLine = "Startup failed: " + e.Message;
            }
        }

        async Task LoadFromBackendAsync()
        {
            profile = null;
            string savedId = PlayerPrefs.GetString(PlayerIdKey, "");
            if (!string.IsNullOrEmpty(savedId))
            {
                try { profile = await backend.GetPlayerAsync(savedId); }
                catch (BackendException e) when (e.Code == BackendErrorCodes.NotFound) { }
            }
            if (profile == null)
            {
                profile = await backend.CreatePlayerAsync("Dogpatch Driver");
                PlayerPrefs.SetString(PlayerIdKey, profile.id);
                PlayerPrefs.Save();
            }

            VehicleRoster.Configure(material, models.Role("player").Count > 0 ? models.Get(models.Role("player")[0].id) : null);
            float spawnStart = Time.realtimeSinceStartup;
            foreach (var vehicle in await backend.ListVehiclesAsync())
            {
                CarController car;
                if (VehicleCatalog.Has(vehicle.model))
                {
                    car = VehicleRoster.Spawn(vehicle.model, ToVector(vehicle.position), vehicle.heading, world.Root.transform, id: vehicle.id);
                }
                else
                {
                    var asset = models.Get(vehicle.model) ?? models.Get(models.Role("car")[0].id);
                    car = CarController.Create(vehicle.id, asset, material, ToVector(vehicle.position), vehicle.heading);
                    car.transform.SetParent(world.Root.transform, true);
                }
                cars[vehicle.id] = car;
            }
            Debug.Log($"[Dogpatch] {cars.Count} vehicles spawned in {Time.realtimeSinceStartup - spawnStart:F1}s");

            var playerModel = models.Role("player").Count > 0 ? models.Get(models.Role("player")[0].id) : null;
            player = PlayerController.Create(playerModel, material);
            AvatarCapture.Install(gameObject, player, playerModel, material);
            follow = view.gameObject.AddComponent<FollowCamera>();

            var state = await backend.LoadPlayerStateAsync(profile.id);
            if (state == null)
            {
                player.Teleport(world.PlayerSpawn.Position, world.PlayerSpawn.Heading);
            }
            else
            {
                player.Teleport(ToVector(state.position), state.heading);
                if (state.mode == PlayerModes.Driving && cars.TryGetValue(state.vehicleId ?? "", out var car))
                {
                    var result = await backend.EnterVehicleAsync(profile.id, car.VehicleId);
                    TakeWheel(car, result);
                }
            }
            if (current == null) follow.Snap(player.transform, false);
            lastSafe = player.transform.position;

            SmashWorld.Install(gameObject, world.Instances, models, material, view);
            VehicleDamage.Wrecked += OnWrecked;
            gameObject.AddComponent<ParkedCarStreamer>().Init(this, cars.Values);
        }

        void OnDestroy() => VehicleDamage.Wrecked -= OnWrecked;

        void OnWrecked(CarController car)
        {
            if (car != current) return;
            hud.Toast("WRECKED! Hop out.");
            _ = RunAsync(ExitCarAsync);
        }

        void SetupRendering()
        {
            view = Camera.main;
            if (view == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                view = go.AddComponent<Camera>();
            }
            Look.SpeedDial.Setup(view);
        }

        void Update()
        {
            if (!ready) return;

            input.Driving = current != null;
            follow.Orbit(input.TakeLook());
            var stick = input.Direction;
            if (current == null || !current.Reversing || !input.Dragging) reverseYaw = null;
            else reverseYaw ??= follow.Yaw;
            var dir = stick == Vector2.zero ? Vector3.zero
                : current != null && !DragInput.DragAnywhere ? CarStick(current.transform, stick)
                : Quaternion.Euler(0f, reverseYaw ?? follow.Yaw, 0f) * new Vector3(stick.x, 0f, stick.y);
            if (ExternalInput) { }
            else if (current != null) current.SetInput(dir, input.Strength);
            else player.SetInput(dir, input.Strength);

            var focus = current != null ? current.transform : player.transform;
            NearestCar = current == null ? FindNearestCar(player.transform.position, 40f) : null;
            StreetName = world.Roads.StreetNameAt(focus.position);
            float kmh = Mathf.Abs(current != null ? current.Speed : player.Speed) * 3.6f;
            StatusLine = (Riding ? "Riding" : current != null ? "Driving" : "On foot") + $" · {kmh:F0} km/h";

            if (focus.position.y < -1.5f) Respawn(focus);
            else if (focus.position.y > -0.2f && Time.frameCount % 30 == 0) lastSafe = focus.position;

            if (Time.time >= nextSync && !busy) _ = SyncAsync();
            if (Time.time >= nextLog)
            {
                nextLog = Time.time + 2f;
                Debug.Log($"[Dogpatch] mode={(current != null ? "driving" : "onFoot")} pos=({focus.position.x:F1},{focus.position.z:F1}) heading={focus.eulerAngles.y:F0} speed={kmh:F0}km/h street={StreetName}");
            }
        }

        // At the wheel the stick is the car's own frame, so panning the camera never steers: up is
        // throttle, down is reverse, and sideways steers in proportion (full sideways is full lock
        // while still driving forward). Pulled back, sideways swings the tail that way.
        static Vector3 CarStick(Transform car, Vector2 stick)
        {
            bool back = stick.y < -0.35f && -stick.y > Mathf.Abs(stick.x) * 0.5f;
            float angle = back ? 180f - stick.x * CarController.FullLockAngle : stick.x * CarController.FullLockAngle;
            return Quaternion.Euler(0f, car.eulerAngles.y + angle, 0f) * Vector3.forward;
        }

        CarController FindNearestCar(Vector3 position, float range)
        {
            CarController best = null;
            float bestDistance = range;
            foreach (var car in cars.Values)
            {
                float d = Vector3.Distance(car.transform.position, position);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = car;
            }
            return best;
        }

        void Respawn(Transform focus)
        {
            Vector2 p = new Vector2(lastSafe.x, lastSafe.z);
            if (world.Roads.Nearest(p, 200f, out var hit)) p = hit.Point;
            var pos = new Vector3(p.x, 0.3f, p.y);
            if (current != null) current.Place(pos, focus.eulerAngles.y);
            else player.Teleport(pos, focus.eulerAngles.y);
            hud.Toast("Splash! Back to the street.");
        }

        async void OnTap(Vector2 screen)
        {
            if (!ready || busy) return;
            var ray = view.ScreenPointToRay(screen);
            CarController tapped = null;
            if (Physics.SphereCast(ray, 0.4f, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                tapped = hit.collider.GetComponentInParent<CarController>();
            if (tapped != null && tapped != current && !tapped.Enterable) tapped = null;
            Debug.Log($"[Dogpatch] tap at ({screen.x:F0},{screen.y:F0}) hit={(tapped != null ? "car " + tapped.VehicleId : "nothing")}");

            if (current != null)
            {
                if (tapped == current) await RunAsync(ExitCarAsync);
                return;
            }
            if (tapped == null)
            {
                player.Jump();
                return;
            }
            if (tapped.GetComponent<VehicleDamage>()?.IsWrecked == true)
            {
                hud.Toast("That one's wrecked");
                return;
            }
            var closest = tapped.GetComponent<BoxCollider>().ClosestPoint(player.transform.position);
            if (Vector3.Distance(closest, player.transform.position) > EnterReach)
            {
                hud.Toast("Walk closer to get in");
                return;
            }
            await RunAsync(() => EnterCarAsync(tapped));
        }

        async Task RunAsync(Func<Task> action)
        {
            busy = true;
            try { await action(); }
            catch (BackendException e)
            {
                Debug.LogWarning($"[Dogpatch] backend {e.Code}: {e.Message}");
                hud.Toast(e.Code == BackendErrorCodes.Conflict ? "Someone else has that car" : "Couldn't reach the server");
            }
            finally { busy = false; }
        }

        async Task EnterCarAsync(CarController car)
        {
            if (!cars.ContainsKey(car.VehicleId))
            {
                if (!(backend is InMemoryGameBackend local)) return;
                local.SeedVehicle(car.VehicleId, car.ModelId, ToVec(car.transform.position), car.transform.eulerAngles.y);
                cars[car.VehicleId] = car;
            }
            var result = await backend.EnterVehicleAsync(profile.id, car.VehicleId);
            TakeWheel(car, result);
            Debug.Log($"[Dogpatch] entered car {car.VehicleId} ({car.ModelId}) on {world.Roads.StreetNameAt(car.transform.position)}");
        }

        void TakeWheel(CarController car, VehicleResult result)
        {
            current = car;
            car.SetDriven(true);
            player.SetInput(Vector3.zero, 0f);
            player.gameObject.SetActive(false);
            follow.Target = car.transform;
            follow.Driving = true;
            hud.Toast(car.Handling.PassengerOnly ? "Sit back, it drives itself" : "Drag to drive");
            VehicleEntered?.Invoke(car);
        }

        async Task ExitCarAsync()
        {
            var car = current;
            var t = car.transform;
            if (car.Hovers)
            {
                car.Grounding = true;
                float until = Time.time + 3f;
                while (t.position.y - car.HoverGround.y > 0.3f && Time.time < until) await Task.Yield();
            }
            var momentum = t.forward * car.Speed;
            float side = car.HalfExtents.x + 0.9f;
            var exit = t.position - t.right * side;
            if (world.Buildings.Blocked(new Vector2(exit.x, exit.z), 0.5f)) exit = t.position + t.right * side;
            var door = exit - t.position;
            exit.y = Mathf.Max(t.position.y, 0f) + 0.1f;

            await backend.UpdateVehicleAsync(profile.id, car.VehicleId, ToVec(t.position), t.eulerAngles.y);
            var result = await backend.ExitVehicleAsync(profile.id, car.VehicleId, ToVec(exit), t.eulerAngles.y);
            car.SetDriven(false);
            current = null;
            player.gameObject.SetActive(true);
            player.Teleport(ToVector(result.state.position), result.state.heading);
            BailOut.Begin(player, momentum, door);
            follow.Target = player.transform;
            follow.Driving = false;
            VehicleExited?.Invoke(car);
            Debug.Log($"[Dogpatch] exited car {car.VehicleId} at ({t.position.x:F1},{t.position.z:F1})");
        }

        async Task SyncAsync()
        {
            nextSync = Time.time + SyncSeconds;
            busy = true;
            try
            {
                if (current != null)
                {
                    var t = current.transform;
                    await backend.UpdateVehicleAsync(profile.id, current.VehicleId, ToVec(t.position), t.eulerAngles.y);
                }
                else
                {
                    var t = player.transform;
                    await backend.SavePlayerStateAsync(new PlayerState { playerId = profile.id, position = ToVec(t.position), heading = t.eulerAngles.y, mode = PlayerModes.OnFoot, vehicleId = "" });
                }
            }
            catch (BackendException e)
            {
                Debug.LogWarning($"[Dogpatch] sync failed {e.Code}: {e.Message}");
            }
            finally { busy = false; }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && ready && !busy) _ = SyncAsync();
        }

        static Vec3 ToVec(Vector3 v) => new Vec3(v.x, v.y, v.z);

        static Vector3 ToVector(Vec3 v) => new Vector3(v.x, v.y, v.z);
    }
}
