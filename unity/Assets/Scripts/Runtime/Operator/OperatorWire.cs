using System;

namespace Dogpatch.Operator
{
    // The Convex operator module's wire shapes (backend/convex/operator.ts). Field names are the
    // contract's, so JsonUtility reads and writes them directly; strings are "" rather than null.

    [Serializable]
    public class ActiveEffect
    {
        public string id;
        public string effect;
        public string scope;
        public string target;
        public string targetClass;
        public string strength;
        public string color;
        public string theme;
        public string vehicle;
        public bool instant;
        public string status;
        public double startsAt;
        public double endsAt;
        public float durationS;
        public string label;
        public string requestedBy;
        public string transcript;

        public bool IsSet => !string.IsNullOrEmpty(id);
    }

    [Serializable]
    public class OperatorRefusal
    {
        public string reason;
        public string message;
    }

    [Serializable]
    public class OperatorTimings
    {
        public float jevMs;
        public float totalMs;
        public int attempts;
    }

    [Serializable]
    public class OperatorCallResult
    {
        public string outcome;
        public ActiveEffect effect;
        public OperatorRefusal refusal;
        public string line;
        public double serverNow;
        public OperatorTimings timings;
    }

    [Serializable]
    public class ActiveEffectList
    {
        public ActiveEffect[] effects;
    }

    [Serializable]
    public class ScribeTokenResult
    {
        public string token;
        public double serverNow;
    }

    [Serializable]
    public class ViewEntity
    {
        public string id;
        public string @class;
        public string desc;
        public string color;
        public string screen;
        public float distance_m;
        public int view_rank;
        public bool @protected;
        public string controlledBy;
    }

    [Serializable]
    public class OperatorView
    {
        public string mode;
        public string timeOfDay;
        public string weather;
        public ViewEntity[] entities;
    }

    [Serializable]
    public class ClientTimings
    {
        public float sttMs;
    }

    [Serializable]
    public class OperatorCallArgs
    {
        public string roomId;
        public string playerId;
        public string transcript;
        public OperatorView view;
        public ClientTimings clientTimings;
    }

    [Serializable]
    public class ConfirmArgs
    {
        public string playerId;
        public string effectId;
    }

    [Serializable]
    public class RoomArgs
    {
        public string roomId;
    }

    [Serializable]
    public class PlayerArgs
    {
        public string playerId;
    }
}
