using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // How a vehicle drives under the one-drag control. Default reproduces the original hatchback.
    // Grip is the share of sideways velocity the tyres cancel each step (1 = on rails, as before);
    // RoadConditions scales it for weather. Mass, Durability (crash budget in m/s of impact,
    // scaled by the other body's mass over this one's) and BargeStrength (0..1 share of a
    // collision's push it resists) feed CROS-23's damage and barging.
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
        public float BargeStrength = 0.3f;
        public bool PassengerOnly;

        public static readonly HandlingProfile Default = new HandlingProfile();

        public HandlingProfile Clone() => (HandlingProfile)MemberwiseClone();
    }
}
