using System;

namespace Dogpatch.Backend
{
    // Wire shapes of the backend HTTP contract (handoff: convex-contract.md). Field names match the
    // JSON exactly so JsonUtility can read Convex responses straight into these classes.
    public static class PlayerModes
    {
        public const string OnFoot = "onFoot";
        public const string Driving = "driving";
    }

    [Serializable]
    public class Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3() { }

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    [Serializable]
    public class Player
    {
        public string id;
        public string displayName;
        public long createdAt;
    }

    [Serializable]
    public class PlayerState
    {
        public string playerId;
        public Vec3 position;
        public float heading;
        public string mode;
        public string vehicleId;
        public long updatedAt;
    }

    [Serializable]
    public class Vehicle
    {
        public string id;
        public string model;
        public Vec3 position;
        public float heading;
        public string occupiedBy;
        public long updatedAt;
    }

    [Serializable]
    public class VehicleResult
    {
        public Vehicle vehicle;
        public PlayerState state;
    }

    public static class BackendErrorCodes
    {
        public const string InvalidArgument = "INVALID_ARGUMENT";
        public const string NotFound = "NOT_FOUND";
        public const string Conflict = "CONFLICT";
        public const string Internal = "INTERNAL";
    }

    public sealed class BackendException : Exception
    {
        public readonly string Code;

        public BackendException(string code, string message) : base(message) => Code = code;
    }
}
