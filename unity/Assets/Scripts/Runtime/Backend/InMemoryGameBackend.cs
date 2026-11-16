using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Dogpatch.Backend
{
    // Local, process-lifetime implementation of IGameBackend that enforces the same rules and
    // error codes as the Convex contract. Only the avatar calls touch the network. Records are
    // copied in and out so callers can never mutate stored state by accident.
    public sealed class InMemoryGameBackend : IGameBackend
    {
        readonly Dictionary<string, Player> players = new Dictionary<string, Player>();
        readonly Dictionary<string, PlayerState> states = new Dictionary<string, PlayerState>();
        readonly Dictionary<string, Vehicle> vehicles = new Dictionary<string, Vehicle>();
        readonly Dictionary<string, Avatar> avatars = new Dictionary<string, Avatar>();
        readonly AvatarHttpClient avatarHttp;
        int nextId = 1;

        public InMemoryGameBackend() : this(AvatarHttpClient.ConfiguredBaseUrl()) { }

        // avatarBaseUrl null: avatars come from the stub only.
        public InMemoryGameBackend(string avatarBaseUrl)
        {
            if (!string.IsNullOrEmpty(avatarBaseUrl)) avatarHttp = new AvatarHttpClient(avatarBaseUrl);
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static T Copy<T>(T value) => value == null ? default : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        static Task<T> Fail<T>(string code, string message) => Task.FromException<T>(new BackendException(code, message));

        static bool Empty(string s) => string.IsNullOrEmpty(s);

        // Mirrors the server-side vehicles:seed internal function; not part of IGameBackend.
        public void SeedVehicles(IEnumerable<(string model, Vec3 position, float heading)> seed)
        {
            foreach (var (model, position, heading) in seed)
            {
                string id = "v" + nextId++;
                vehicles[id] = new Vehicle { id = id, model = model, position = position, heading = heading, occupiedBy = null, updatedAt = Now };
            }
        }

        // A vehicle that appears at runtime (traffic, dock bikes, a felled rider's bike) under the id
        // the client already gave it. Not part of IGameBackend: the Convex contract only seeds server-side.
        public void SeedVehicle(string id, string model, Vec3 position, float heading)
        {
            if (vehicles.ContainsKey(id)) return;
            vehicles[id] = new Vehicle { id = id, model = model, position = position, heading = heading, occupiedBy = null, updatedAt = Now };
        }

        // Not part of IGameBackend: the server renames through players:rename.
        public void RenamePlayer(string playerId, string displayName)
        {
            if (players.TryGetValue(playerId ?? "", out var player)) player.displayName = displayName;
        }

        public Task<Player> CreatePlayerAsync(string displayName)
        {
            string name = displayName?.Trim() ?? "";
            if (name.Length < 1 || name.Length > 32) return Fail<Player>(BackendErrorCodes.InvalidArgument, "displayName must be 1 to 32 characters");
            var player = new Player { id = NewPlayerId(), displayName = name, createdAt = Now };
            players[player.id] = player;
            return Task.FromResult(Copy(player));
        }

        public Task<Player> GetPlayerAsync(string playerId)
        {
            if (Empty(playerId)) return Fail<Player>(BackendErrorCodes.InvalidArgument, "id is required");
            return players.TryGetValue(playerId, out var player)
                ? Task.FromResult(Copy(player))
                : Fail<Player>(BackendErrorCodes.NotFound, "Player " + playerId + " not found");
        }

        public Task<PlayerState> LoadPlayerStateAsync(string playerId)
        {
            if (!players.ContainsKey(playerId ?? "")) return Fail<PlayerState>(BackendErrorCodes.NotFound, "Player " + playerId + " not found");
            return Task.FromResult(states.TryGetValue(playerId, out var state) ? Copy(state) : null);
        }

        public Task<PlayerState> SavePlayerStateAsync(PlayerState request)
        {
            if (request == null || request.position == null || (request.mode != PlayerModes.OnFoot && request.mode != PlayerModes.Driving))
                return Fail<PlayerState>(BackendErrorCodes.InvalidArgument, "playerId, position and mode are required");
            if (!players.ContainsKey(request.playerId ?? "")) return Fail<PlayerState>(BackendErrorCodes.NotFound, "Player " + request.playerId + " not found");

            var driving = DrivenBy(request.playerId);
            if (request.mode == PlayerModes.OnFoot)
            {
                if (!Empty(request.vehicleId) || driving != null) return Fail<PlayerState>(BackendErrorCodes.Conflict, "Player is driving");
                return Task.FromResult(Copy(WriteState(request.playerId, request.position, request.heading, PlayerModes.OnFoot, null)));
            }

            if (Empty(request.vehicleId)) return Fail<PlayerState>(BackendErrorCodes.InvalidArgument, "vehicleId is required when driving");
            if (driving == null || driving.id != request.vehicleId) return Fail<PlayerState>(BackendErrorCodes.Conflict, "Player is not driving " + request.vehicleId);
            MoveVehicle(driving, request.position, request.heading);
            return Task.FromResult(Copy(WriteState(request.playerId, request.position, request.heading, PlayerModes.Driving, driving.id)));
        }

        public Task<IReadOnlyList<Vehicle>> ListVehiclesAsync() =>
            Task.FromResult((IReadOnlyList<Vehicle>)vehicles.Values.Select(Copy).ToList());

        public Task<VehicleResult> EnterVehicleAsync(string playerId, string vehicleId)
        {
            if (!players.ContainsKey(playerId ?? "")) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Player " + playerId + " not found");
            if (!vehicles.TryGetValue(vehicleId ?? "", out var vehicle)) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Vehicle " + vehicleId + " not found");
            if (!Empty(vehicle.occupiedBy) && vehicle.occupiedBy != playerId) return Fail<VehicleResult>(BackendErrorCodes.Conflict, "Vehicle is taken");
            var current = DrivenBy(playerId);
            if (current != null && current.id != vehicleId) return Fail<VehicleResult>(BackendErrorCodes.Conflict, "Player is in another vehicle");

            vehicle.occupiedBy = playerId;
            vehicle.updatedAt = Now;
            var state = WriteState(playerId, vehicle.position, vehicle.heading, PlayerModes.Driving, vehicle.id);
            return Task.FromResult(new VehicleResult { vehicle = Copy(vehicle), state = Copy(state) });
        }

        public Task<VehicleResult> ExitVehicleAsync(string playerId, string vehicleId, Vec3 position, float? heading)
        {
            if (!players.ContainsKey(playerId ?? "")) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Player " + playerId + " not found");
            if (!vehicles.TryGetValue(vehicleId ?? "", out var vehicle)) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Vehicle " + vehicleId + " not found");
            if (vehicle.occupiedBy != playerId) return Fail<VehicleResult>(BackendErrorCodes.Conflict, "Player is not in this vehicle");

            vehicle.occupiedBy = null;
            vehicle.updatedAt = Now;
            var state = WriteState(playerId, position ?? vehicle.position, heading ?? vehicle.heading, PlayerModes.OnFoot, null);
            return Task.FromResult(new VehicleResult { vehicle = Copy(vehicle), state = Copy(state) });
        }

        public Task<VehicleResult> UpdateVehicleAsync(string playerId, string vehicleId, Vec3 position, float heading)
        {
            if (position == null) return Fail<VehicleResult>(BackendErrorCodes.InvalidArgument, "position is required");
            if (!players.ContainsKey(playerId ?? "")) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Player " + playerId + " not found");
            if (!vehicles.TryGetValue(vehicleId ?? "", out var vehicle)) return Fail<VehicleResult>(BackendErrorCodes.NotFound, "Vehicle " + vehicleId + " not found");
            if (vehicle.occupiedBy != playerId) return Fail<VehicleResult>(BackendErrorCodes.Conflict, "Player is not the driver");

            MoveVehicle(vehicle, position, heading);
            var state = WriteState(playerId, position, heading, PlayerModes.Driving, vehicle.id);
            return Task.FromResult(new VehicleResult { vehicle = Copy(vehicle), state = Copy(state) });
        }

        // Avatars come from the real server (AvatarHttpClient.ConfiguredBaseUrl) so phones get real
        // ones. With the URL set to "stub", or when the request gets no HTTP answer at all, the
        // stub answers instead: any non-empty photo becomes the stock commuter, an empty one is a
        // RETAKE. Server refusals (RETAKE, DECISION_UNAVAILABLE) always reach the caller.
        public async Task<AvatarResult> AnalyzeAvatarPhotoAsync(string playerId, byte[] jpeg)
        {
            if (Empty(playerId)) throw new BackendException(BackendErrorCodes.InvalidArgument, "playerId is required");
            if (avatarHttp != null)
            {
                try
                {
                    var result = await avatarHttp.AnalyzePhotoAsync(playerId, jpeg);
                    avatars[playerId] = Copy(result.avatar);
                    return result;
                }
                catch (BackendException e) when (e.Code == AvatarErrorCodes.Offline)
                {
                    Debug.LogWarning("[Avatar] server unreachable, using the stub: " + e.Message);
                }
            }
            if (jpeg == null || jpeg.Length == 0) throw new BackendException(AvatarErrorCodes.Retake, "No person in the photo");
            var avatar = StubAvatar(playerId);
            avatars[playerId] = avatar;
            return new AvatarResult
            {
                avatar = Copy(avatar),
                decision = new AvatarDecision { provider = "stub", realPerson = 1f, costume = "none" },
            };
        }

        public async Task<Avatar> GetAvatarAsync(string playerId)
        {
            if (Empty(playerId)) throw new BackendException(BackendErrorCodes.InvalidArgument, "playerId is required");
            if (avatarHttp != null)
            {
                try { return await avatarHttp.GetAvatarAsync(playerId); }
                catch (BackendException e) when (e.Code == AvatarErrorCodes.Offline)
                {
                    Debug.LogWarning("[Avatar] server unreachable, using the stub: " + e.Message);
                }
            }
            return avatars.TryGetValue(playerId, out var avatar) ? Copy(avatar) : null;
        }

        // The stub's answer: the bundled player model (Polyfork's commuter) as it ships, no recolour.
        public static Avatar StubAvatar(string playerId) => new Avatar
        {
            playerId = playerId,
            @base = "commuter-381e7f",
            outfit = "",
            costume = "",
            glbUrl = "https://polyfork.dev/cdn/commuter-381e7f.glb",
            fallbackGlbUrl = "https://polyfork.dev/cdn/commuter-381e7f.glb",
            recolor = new AvatarRecolor[0],
            swatches = new AvatarSwatches(),
            updatedAt = Now,
        };

        // Player ids are unique per install and stable across launches ("p-<install id>" for the
        // first player of a run, "-2", "-3" for the race bots after it), because the avatar server
        // keys on them: a plain counter would give every phone the same "p1".
        string NewPlayerId()
        {
            const string installKey = "dogpatch.installId";
            string install = PlayerPrefs.GetString(installKey, "");
            if (install.Length == 0)
            {
                install = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(installKey, install);
                PlayerPrefs.Save();
            }
            return players.Count == 0 ? "p-" + install : $"p-{install}-{players.Count + 1}";
        }

        Vehicle DrivenBy(string playerId) => vehicles.Values.FirstOrDefault(v => v.occupiedBy == playerId);

        void MoveVehicle(Vehicle vehicle, Vec3 position, float heading)
        {
            vehicle.position = Copy(position);
            vehicle.heading = heading;
            vehicle.updatedAt = Now;
        }

        PlayerState WriteState(string playerId, Vec3 position, float heading, string mode, string vehicleId)
        {
            var state = new PlayerState { playerId = playerId, position = Copy(position), heading = heading, mode = mode, vehicleId = vehicleId, updatedAt = Now };
            states[playerId] = state;
            return state;
        }
    }
}
