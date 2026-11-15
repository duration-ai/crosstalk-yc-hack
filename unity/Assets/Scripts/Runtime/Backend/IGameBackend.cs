using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dogpatch.Backend
{
    // Every piece of state a server owns sits behind this interface, one method per endpoint of
    // the backend HTTP contract (POST /api/players, GET /api/players, GET and PUT
    // /api/player-state, GET /api/vehicles, POST /api/vehicles/enter|exit|update). Game code
    // depends only on it, so a Convex HTTP client can replace InMemoryGameBackend without
    // touching gameplay. Failures throw BackendException carrying the contract's error code.
    public interface IGameBackend
    {
        Task<Player> CreatePlayerAsync(string displayName);

        Task<Player> GetPlayerAsync(string playerId);

        // Null when the player has never saved; the client then spawns at its default point.
        Task<PlayerState> LoadPlayerStateAsync(string playerId);

        // On-foot movement and checkpoints. Mode changes go through Enter/ExitVehicleAsync.
        Task<PlayerState> SavePlayerStateAsync(PlayerState state);

        Task<IReadOnlyList<Vehicle>> ListVehiclesAsync();

        Task<VehicleResult> EnterVehicleAsync(string playerId, string vehicleId);

        // position and heading are where the player stands after getting out; null keeps the vehicle's.
        Task<VehicleResult> ExitVehicleAsync(string playerId, string vehicleId, Vec3 position, float? heading);

        // Moves the vehicle and its driver's state in one write; only the driver may call it.
        Task<VehicleResult> UpdateVehicleAsync(string playerId, string vehicleId, Vec3 position, float heading);
    }
}
