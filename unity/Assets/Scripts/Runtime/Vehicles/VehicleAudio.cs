using Dogpatch.Audio;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // A vehicle's own signal (horn, robotaxi chime, bus and tram bell, bike bell). CROS-24's audio
    // owns every vehicle sound and picks the clip from the vehicle's model id, so this only rate-limits
    // and raises GameEvents.Horn; the cart's squeak is its engine sound there.
    public sealed class VehicleAudio : MonoBehaviour
    {
        float nextSound;

        // Signals unless this vehicle signalled within cooldown seconds; true when it did.
        public bool Signal(float cooldown = 3f)
        {
            if (Time.time < nextSound) return false;
            nextSound = Time.time + cooldown;
            GameEvents.RaiseHorn(transform);
            return true;
        }
    }
}
