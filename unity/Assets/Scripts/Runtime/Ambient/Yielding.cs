using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Ambient
{
    // Anything on the street that others must yield to: AI vehicles (CROS-21) and pedestrians on a crossing (CROS-20).
    public interface ITrafficParticipant
    {
        Vector3 Position { get; }

        // metres per second on the ground plane
        Vector3 Velocity { get; }
    }

    // Shared yield registry: vehicles wait for pedestrians on crossings, pedestrians wait for vehicles before stepping off the curb.
    public static class Yielding
    {
        // AI vehicles add themselves in OnEnable and remove themselves in OnDisable.
        public static readonly List<ITrafficParticipant> Vehicles = new List<ITrafficParticipant>();

        // Pedestrians add themselves while waiting at the curb to cross or on a crossing, and remove themselves once across.
        public static readonly List<ITrafficParticipant> Crossing = new List<ITrafficParticipant>();

        // True when a pedestrian who is crossing, or about to, is within radius metres of point on the ground plane.
        public static bool PedestrianNear(Vector3 point, float radius)
        {
            float r2 = radius * radius;
            foreach (var p in Crossing)
            {
                var d = p.Position - point;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) return true;
            }
            return false;
        }
    }
}
