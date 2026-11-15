using System;

namespace Dogpatch.Backend
{
    // Wire shapes of the selfie-to-avatar endpoints (POST /api/avatar/photo, GET /api/avatar).
    // Field names match the JSON so JsonUtility reads responses straight into them. JsonUtility
    // reads a null string as "" and a null number as 0, so "" and 0 mean "not set" here.
    // Files that also use UnityEngine need `using Avatar = Dogpatch.Backend.Avatar;`.
    [Serializable]
    public class AvatarRecolor
    {
        public string zone;
        public string from;
        public string to;
    }

    [Serializable]
    public class AvatarSwatches
    {
        public string skin;
        public string hair;
        public string top;
        public string outer;
        public string bottoms;
        public string headwear;
    }

    [Serializable]
    public class Avatar
    {
        public string playerId;
        public string @base;
        public string outfit;
        public string costume;
        public string figure;
        public string age;
        public float build;
        public string expression;
        public string glbUrl;
        public string fallbackGlbUrl;
        public AvatarRecolor[] recolor;
        public AvatarSwatches swatches;
        public long updatedAt;

        // JsonUtility turns "avatar": null into an empty object rather than null.
        public bool IsEmpty => string.IsNullOrEmpty(glbUrl) && string.IsNullOrEmpty(fallbackGlbUrl);
    }

    [Serializable]
    public class AvatarDecision
    {
        public string provider;
        public float latencyMs;
        public bool hedged;
        public float realPerson;
        public string costume;
        public float costumeProbability;
    }

    [Serializable]
    public class AvatarResult
    {
        public Avatar avatar;
        public AvatarDecision decision;
    }

    public static class AvatarErrorCodes
    {
        // 422: nobody (or no real person) in the photo; the UI offers a retake.
        public const string Retake = "RETAKE";
        // 503: the server could not decide in time; the UI offers to try again.
        public const string DecisionUnavailable = "DECISION_UNAVAILABLE";
        // Client side: no HTTP answer at all (offline, DNS, timeout).
        public const string Offline = "OFFLINE";
    }
}
