using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Backend;

namespace Dogpatch.Race
{
    // Rooms, the lobby, the countdown, positions, places, world-change events and results: every
    // piece of race state a server owns, one method per endpoint of the race contract
    // (POST /api/rooms, /rooms/join, /rooms/leave, /rooms/ready, GET /rooms/poll, POST /race/update,
    // POST /rooms/events, /race/collect). LocalRaceSession runs the same rules on the device; a Convex HTTP client
    // implements this interface to make the race multiplayer. Failures throw BackendException with
    // the contract's error code.
    public interface IRaceSession
    {
        // route: the street path from the start ring (first) to YC (last), 2 to 512 points.
        // asks: Operator requests each racer starts every race with, 0 for the default 2.
        // pickups: ask pickups off the route, 0 to 64 points; each racer can collect each one once a race.
        Task<RoomResponse> CreateRoomAsync(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int minPlayers,
            int asks = 0, IReadOnlyList<Vec3> pickups = null, float pickupRadius = 0f);

        Task<RoomResponse> JoinRoomAsync(string playerId, string code);

        Task<LeaveResponse> LeaveRoomAsync(string playerId, string roomId);

        // ready during "finished" is the rematch.
        Task<ReadyResponse> SetReadyAsync(string playerId, string roomId, bool ready);

        // after: the last cursor, or null for every event of the current race so far.
        Task<PollResponse> PollAsync(string roomId, long? after);

        Task<RaceUpdateResponse> UpdateAsync(string playerId, string roomId, Vec3 position, float heading);

        // Collects pickup `pickup` (an index into room.pickups) while racing, from a position within its
        // radius. Collecting one the racer already has changes nothing and answers collected false.
        Task<AskResponse> CollectAskAsync(string playerId, string roomId, int pickup, Vec3 position);

        // Spends one ask; CONFLICT when the racer has none. The on-device Operator and bots call it; the
        // Convex Operator spends inside operator.call (internal.race.spendAsk), so clients never call it there.
        Task<AskResponse> SpendAskAsync(string playerId, string roomId);

        // A world change that does not go through the Operator and costs no ask (debug, scripted demo).
        Task<FireEventResponse> FireEventAsync(string playerId, string roomId, WorldChangeCommand command);
    }
}
