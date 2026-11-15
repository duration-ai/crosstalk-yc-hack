using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // How a vehicle drives under the one-drag control. Default reproduces the original hatchback.
    // Grip is the share of sideways velocity the tyres cancel each step (1 = on rails, as before);
    // RoadConditions scales it for weather. Durability is how much crash impulse (in m/s of
    // velocity change) the body takes before it is wrecked.
    [Serializable]
    public sealed class HandlingProfile
    {
        public float TopSpeed = 22f;
        public float ReverseSpeed = 7f;
        public float Acceleration = 8f;
        public float Braking = 16f;
        public float Coasting = 5f;
        public float TurnRate = 75f;
        public float Grip = 1f;
        public float Mass = 1200f;
        public float Durability = 60f;
        public bool PassengerOnly;

        public static readonly HandlingProfile Default = new HandlingProfile();

        public HandlingProfile Clone() => (HandlingProfile)MemberwiseClone();
    }
}
