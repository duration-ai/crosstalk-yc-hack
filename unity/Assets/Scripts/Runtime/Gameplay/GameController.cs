using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Data;
using Dogpatch.Glb;
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

        IGameBackend backend;
        WorldContext world;
        ModelLibrary models;
        Material material;
        DragInput input;
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

        public bool Driving => current != null;
        public CarController NearestCar { get; private set; }
        public Camera View => view;
        public CarController CurrentCar => current;
        public PlayerController Player => player;
        public string StreetName { get; private set; } = "";
        public string StatusLine { get; private set; } = "Loading Dogpatch…";

        async void Start()
        {
            Application.targetFrameRate = 60;
            if (Application.isMobilePlatform && Screen.height > 1400)
            {
                float scale = 1400f / Screen.height;
                Screen.SetResolution(Mathf.RoundToInt(Screen.width * scale), 1400, true);
            }
            material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
            SetupRendering();

            var data = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("World/dogpatch").text);
            models = new ModelLibrary();
            float t0 = Time.realtimeSinceStartup;
            world = WorldBuilder.Build(data, models, material);
            Debug.Log($"[Dogpatch] world built in {Time.realtimeSinceStartup - t0:F1}s: {data.roads.Length} roads, {data.buildings.Length} buildings, {world.ParkedCars.Count + 1} cars");

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
            foreach (var spot in world.ParkedCars) seed.Add((carModels[rng.Next(carModels.Count)].id, ToVec(spot.Position), spot.Heading));
            local.SeedVehicles(seed);
            backend = local;

            try
            {
                await LoadFromBackendAsync();
                ready = true;
                StatusLine = "On foot";
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

            foreach (var vehicle in await backend.ListVehiclesAsync())
            {
                var asset = models.Get(vehicle.model) ?? models.Get(models.Role("car")[0].id);
                var car = CarController.Create(vehicle.id, asset, material, ToVector(vehicle.position), vehicle.heading);
                car.transform.SetParent(world.Root.transform, true);
                cars[vehicle.id] = car;
            }

            var playerModel = models.Role("player").Count > 0 ? models.Get(models.Role("player")[0].id) : null;
            player = PlayerController.Create(playerModel, material);
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

            follow.Hold = input.Dragging && current == null;
            var dir2 = input.Direction;
            var dir = dir2 == Vector2.zero ? Vector3.zero : Quaternion.Euler(0f, follow.Yaw, 0f) * new Vector3(dir2.x, 0f, dir2.y);
            if (current != null) current.SetInput(dir, input.Strength);
            else player.SetInput(dir, input.Strength);

            var focus = current != null ? current.transform : player.transform;
            NearestCar = current == null ? FindNearestCar(player.transform.position, 40f) : null;
            StreetName = world.Roads.StreetNameAt(new Vector2(focus.position.x, focus.position.z));
            float kmh = Mathf.Abs(current != null ? current.Speed : player.Speed) * 3.6f;
            StatusLine = (current != null ? "Driving" : "On foot") + $" · {kmh:F0} km/h";

            if (focus.position.y < -1.5f) Respawn(focus);
            else if (focus.position.y > -0.2f && Time.frameCount % 30 == 0) lastSafe = focus.position;

            if (Time.time >= nextSync && !busy) _ = SyncAsync();
            if (Time.time >= nextLog)
            {
                nextLog = Time.time + 2f;
                Debug.Log($"[Dogpatch] mode={(current != null ? "driving" : "onFoot")} pos=({focus.position.x:F1},{focus.position.z:F1}) heading={focus.eulerAngles.y:F0} speed={kmh:F0}km/h street={StreetName}");
            }
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
            var result = await backend.EnterVehicleAsync(profile.id, car.VehicleId);
            TakeWheel(car, result);
            Debug.Log($"[Dogpatch] entered car {car.VehicleId} ({car.ModelId}) on {world.Roads.StreetNameAt(new Vector2(car.transform.position.x, car.transform.position.z))}");
        }

        void TakeWheel(CarController car, VehicleResult result)
        {
            current = car;
            car.SetDriven(true);
            player.SetInput(Vector3.zero, 0f);
            player.gameObject.SetActive(false);
            follow.Target = car.transform;
            follow.Driving = true;
            hud.Toast("Drag to drive");
        }

        async Task ExitCarAsync()
        {
            var car = current;
            var t = car.transform;
            float side = car.HalfExtents.x + 0.9f;
            var exit = t.position - t.right * side;
            if (world.Buildings.Blocked(new Vector2(exit.x, exit.z), 0.5f)) exit = t.position + t.right * side;
            exit.y = Mathf.Max(t.position.y, 0f) + 0.1f;

            await backend.UpdateVehicleAsync(profile.id, car.VehicleId, ToVec(t.position), t.eulerAngles.y);
            var result = await backend.ExitVehicleAsync(profile.id, car.VehicleId, ToVec(exit), t.eulerAngles.y);
            car.SetDriven(false);
            current = null;
            player.gameObject.SetActive(true);
            player.Teleport(ToVector(result.state.position), result.state.heading);
            follow.Target = player.transform;
            follow.Driving = false;
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
