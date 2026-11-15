using UnityEngine;

namespace Dogpatch.Ambient
{
    // Anything on foot that reacts to vehicles: pedestrians (CROS-20). CROS-23's physics calls it.
    public interface IVehicleReactive
    {
        // Each physics step while a vehicle is predicted to reach this body within the warning window (about 1.5 s).
        void OnVehicleThreat(GameObject vehicle, Vector3 vehicleVelocity, float timeToImpact);

        // Once per hit, when a vehicle actually strikes this body. relativeVelocity is in m/s.
        void OnVehicleImpact(GameObject vehicle, Vector3 point, Vector3 relativeVelocity);
    }
}
