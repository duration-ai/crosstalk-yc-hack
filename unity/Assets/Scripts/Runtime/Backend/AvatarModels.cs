using System;
using System.Threading.Tasks;

namespace Dogpatch.Backend
{
    // Wire shapes of the selfie-to-avatar endpoints (POST /api/avatar/photo, GET /api/avatar).
    // Field names match the JSON exactly so JsonUtility can read responses straight into them.
    // Named PlayerAvatar so it never collides with UnityEngine.Avatar.
    [Serializable]
    public class AvatarRecolor
    {
        public string zone;
        public string from;
        public string to;
    }

    [Serializable]
    public class PlayerAvatar
    {
        public string playerId;
        public string @base;
        public string costume;
        public string figure;
        public string age;
        public float build;
        public string expression;
        public string glbUrl;
        public string fallbackGlbUrl;
        public AvatarRecolor[] recolor;
        public long updatedAt;

        // JsonUtility turns "avatar": null into an empty object rather than null.
        public bool IsEmpty => string.IsNullOrEmpty(glbUrl) && string.IsNullOrEmpty(fallbackGlbUrl);
    }

    [Serializable]
    public class AvatarDecision
    {
        public string provider;
        public string model;
        public float latencyMs;
        public bool hedged;
        public float personProbability;
    }

    [Serializable]
    public class AvatarUploadResult
    {
        public PlayerAvatar avatar;
        public AvatarDecision decision;
    }

    public static class AvatarErrorCodes
    {
        // No person in the photo: show the retake prompt.
        public const string Retake = "RETAKE";
        // The server couldn't decide in time: also a retake.
        public const string DecisionUnavailable = "DECISION_UNAVAILABLE";
        // Client-side: the request never got an HTTP answer (offline, DNS, timeout).
        public const string Offline = "OFFLINE";
    }

    // The two avatar endpoints. IGameBackend includes them; ConvexAvatarClient implements just these.
    public interface IAvatarBackend
    {
        // jpeg is the raw photo (at most 8 MB). Throws BackendException RETAKE or
        // DECISION_UNAVAILABLE when the photo is refused, NOT_FOUND for an unknown player.
        Task<AvatarUploadResult> UploadAvatarPhotoAsync(string playerId, byte[] jpeg);

        // Null when the player has never taken a photo.
        Task<PlayerAvatar> GetAvatarAsync(string playerId);
    }
}
