using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // The duck boat's amphibious half. Whenever the nearest static surface under it lies below the
    // water (the bay's seabed, or streets under a flood), the car is Afloat: CarController holds it
    // on the surface instead of letting it sink, and the model sits Draft lower so the hull rides in
    // the water. Drive back onto any shore or ramp and the ground under it is above the water again,
    // so it rolls out on its wheels. Runs on every phone, so other players' duck boats float too.
    // A tap while driving (its IVehicleAbility) quacks the horn and porpoises it out of the water.
    public sealed class DuckBoat : MonoBehaviour, IVehicleAbility
    {
        float ready;

        public void Use()
        {
            if (!car.Driven || car.Stunned || Time.time < ready) return;
            ready = Time.time + 1.2f;
            GetComponent<VehicleAudio>()?.Signal(0.5f);
            var body = GetComponent<Rigidbody>();
            if (!body.isKinematic) body.linearVelocity += Vector3.up * (car.Afloat ? 6f : 4f);
            if (car.Afloat) MotionFx.Splash(transform.position, 1f);
            Debug.Log("[DuckBoat] quack hop, afloat " + car.Afloat);
        }

        // Top of the drivable water: the bay surface sits just under the 0 m land, the same height
        // cars_on_water uses, so leaving and climbing back out is a lip, not a wall.
        const float SeaTop = -0.02f;

        CarController car;
        readonly RaycastHit[] hits = new RaycastHit[16];
        float nextWake;

        void Awake() => car = GetComponent<CarController>();

        void OnDestroy()
        {
            if (car != null) car.Afloat = false;
        }

        void Update()
        {
            if (car == null) return;
            float top = Mathf.Max(SeaTop, FloodEffect.Level - 0.05f);
            var p = transform.position;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1f, Vector3.down, hits, 40f, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.attachedRigidbody != null || c is CharacterController || c.isTrigger) continue;
                ground = Mathf.Max(ground, hits[i].point.y);
            }
            bool afloat = ground < top - 0.35f;
            if (afloat && !car.Afloat) MotionFx.Splash(p + transform.forward * car.HalfExtents.z * 0.6f, 1f);
            car.Afloat = afloat;
            car.WaterTop = top;

            float speed = Mathf.Abs(car.Speed);
            if (!afloat || speed < 1.5f || Time.time < nextWake) return;
            nextWake = Time.time + Mathf.Lerp(0.2f, 0.08f, speed / 12f);
            var f = transform.forward;
            var r = transform.right;
            MotionFx.Splash(p + f * car.HalfExtents.z * 0.9f, Mathf.Clamp01(speed / 14f));
            MotionFx.Splash(p - f * car.HalfExtents.z + r * car.HalfExtents.x * 0.8f, Mathf.Clamp01(speed / 20f));
            MotionFx.Splash(p - f * car.HalfExtents.z - r * car.HalfExtents.x * 0.8f, Mathf.Clamp01(speed / 20f));
        }
    }
}
