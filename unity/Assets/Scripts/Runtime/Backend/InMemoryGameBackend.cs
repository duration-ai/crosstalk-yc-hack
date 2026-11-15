using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Dogpatch.Backend
{
    // Local, process-lifetime implementation of IGameBackend that enforces the same rules and
    // error codes as the Convex contract. It makes no network calls. Records are copied in and
    // out so callers can never mutate stored state by accident.
    public sealed class InMemoryGameBackend : IGameBackend
    {
        readonly Dictionary<string, Player> players = new Dictionary<string, Player>();
        readonly Dictionary<string, PlayerState> states = new Dictionary<string, PlayerState>();
        readonly Dictionary<string, Vehicle> vehicles = new Dictionary<string, Vehicle>();
        int nextId = 1;

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

        public Task<Player> CreatePlayerAsync(string displayName)
        {
            string name = displayName?.Trim() ?? "";
            if (name.Length < 1 || name.Length > 32) return Fail<Player>(BackendErrorCodes.InvalidArgument, "displayName must be 1 to 32 characters");
            var player = new Player { id = "p" + nextId++, displayName = name, createdAt = Now };
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
