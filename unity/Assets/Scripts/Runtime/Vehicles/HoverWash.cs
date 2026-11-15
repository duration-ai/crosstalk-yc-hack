using Dogpatch.Gameplay;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Downwash under a hovering vehicle: a small puff of road dust under it every so often while it
    // flies, more often when it is slow and its fans work hardest.
    public sealed class HoverWash : MonoBehaviour
    {
        CarController car;
        float next;

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            if (car == null || !car.Driven || car.HoverAltitude < 1f || Time.time < next) return;
            next = Time.time + Mathf.Lerp(0.25f, 0.6f, Mathf.Clamp01(Mathf.Abs(car.Speed) / 20f));
            MotionFx.Downwash(car.HoverGround + Vector3.up * 0.05f);
        }
    }
}
