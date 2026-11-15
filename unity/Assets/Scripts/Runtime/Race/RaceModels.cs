using System;
using System.Collections.Generic;
using Dogpatch.Backend;

namespace Dogpatch.Race
{
    // Wire shapes of the Speed Dial race contract (handoff: convex-race-contract.md, CROS-18). Field
    // names match the JSON exactly so JsonUtility can read Convex responses straight into them.
    public static class RoomStatus
    {
        public const string Lobby = "lobby";
        public const string Countdown = "countdown";
        public const string Racing = "racing";
        public const string Finished = "finished";
    }

    public static class RaceErrorCodes
    {
        public const string NotImplemented = "NOT_IMPLEMENTED";
    }

    [Serializable]
    public class Room
    {
        public string id;
        public string code;
        public string status;
        public int raceNumber;
        public int minPlayers;
        public int playerCount;
        public int readyCount;
        public long startsAt;
        public long closesAt;
        public float routeLength;
        public Vec3 finish;
        public float finishRadius;
        public long createdAt;
    }

    [Serializable]
    public class Racer
    {
        public string playerId;
        public string displayName;
        public string color;
        public string colorHex;
        public int slot;
        public bool ready;
        public Vec3 position;
        public float heading;
        public float progress;
        public float metersToFinish;
        public int place;
        public bool finished;
        public long finishMs;
        public bool left;
        public long updatedAt;
    }

    // One flat class for every world change's params; fields a type does not use stay default.
    [Serializable]
    public class WorldChangeParams
    {
        public string level;
        public string timeOfDay;
        public string color;
        public string hex;
        public string playerId;
        public string targetPlayerId;
        public string size;
        public int count;
    }

    [Serializable]
    public class WorldChangeCommand
    {
        public string type;
        public WorldChangeParams @params;
    }

    [Serializable]
    public class RaceEvent
    {
        public long seq;
        public int raceNumber;
        public string type;
        public WorldChangeParams @params;
        public string playerId;
        public string displayName;
        public string color;
        public long serverTime;
        public string news;
    }

    [Serializable]
    public class RaceResult
    {
        public int place;
        public string playerId;
        public string displayName;
        public string color;
        public bool finished;
        public long finishMs;
        public int calls;
        public string award;
    }

    [Serializable]
    public class RoomResponse
    {
        public Room room;
        public List<Vec3> route;
        public Racer you;
    }

    [Serializable]
    public class ReadyResponse
    {
        public Room room;
    }

    [Serializable]
    public class LeaveResponse
    {
        public bool left;
    }

    [Serializable]
    public class PollResponse
    {
        public long serverTime;
        public Room room;
        public List<Racer> racers;
        public List<RaceEvent> events;
        public long cursor;
        public List<RaceResult> results;
    }

    [Serializable]
    public class RaceUpdateResponse
    {
        public string status;
        public float progress;
        public float metersToFinish;
        public bool finished;
        public long finishMs;
    }

    [Serializable]
    public class FireEventResponse
    {
        public RaceEvent @event;
    }

    [Serializable]
    public class OperatorResponse
    {
        public string line;
        public WorldChangeCommand command;
        public RaceEvent @event;
        public string provider;
    }
}
