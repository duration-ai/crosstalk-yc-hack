using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Backend;

namespace Dogpatch.Race
{
    // Rooms, the lobby, the countdown, positions, places, world-change events and results: every
    // piece of race state a server owns, one method per endpoint of the race contract
    // (POST /api/rooms, /rooms/join, /rooms/leave, /rooms/ready, GET /rooms/poll, POST /race/update,
    // POST /rooms/events). LocalRaceSession runs the same rules on the device; a Convex HTTP client
    // implements this interface to make the race multiplayer. Failures throw BackendException with
    // the contract's error code.
    public interface IRaceSession
    {
        // route: the street path from the start ring (first) to YC (last), 2 to 512 points.
        Task<RoomResponse> CreateRoomAsync(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int minPlayers);

        Task<RoomResponse> JoinRoomAsync(string playerId, string code);

        Task<LeaveResponse> LeaveRoomAsync(string playerId, string roomId);

        // ready during "finished" is the rematch.
        Task<ReadyResponse> SetReadyAsync(string playerId, string roomId, bool ready);

        // after: the last cursor, or null for every event of the current race so far.
        Task<PollResponse> PollAsync(string roomId, long? after);

        Task<RaceUpdateResponse> UpdateAsync(string playerId, string roomId, Vec3 position, float heading);

        // A world change that does not go through the Operator (bots, pickups, debug).
        Task<FireEventResponse> FireEventAsync(string playerId, string roomId, WorldChangeCommand command);
    }
}
