using Dogpatch.Gameplay;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Lowrider hydraulics. A tap while driving hops the car about 3 m, high enough to clear a car,
    // keeping its speed. At a standstill the body bounces on its pumps, front and back out of step,
    // and while cruising it sits laid out low over the wheels.
    public sealed class Hydraulics : MonoBehaviour, IVehicleAbility
    {
        const float HopSpeed = 8f;
        const float Cooldown = 1.2f;

        CarController car;
        Rigidbody body;
        Transform frame;
        float ready;
        float kick;
        float idle;

        void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();
        }

        public void Use()
        {
            if (!car.Driven || body.isKinematic || car.Stunned || Time.time < ready) return;
            if (!Physics.Raycast(transform.position + Vector3.up * 0.4f, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore)) return;
            ready = Time.time + Cooldown;
            kick = 1f;
            CarFx.On(car).Launch(HopSpeed);
            Debug.Log($"[Lowrider] hop at {transform.position} speed {car.Speed:F1}");
        }

        void Update()
        {
            if (frame == null) frame = VehicleRoster.RigPart(car, "lowrider-frame");
            if (frame == null) return;
            float dt = Time.deltaTime;
            idle = Mathf.MoveTowards(idle, Mathf.Abs(car.Speed) < 1.5f ? 1f : 0f, 2f * dt);
            kick = Mathf.MoveTowards(kick, 0f, 1.5f * dt);
            float t = Time.time * 5.5f;
            float front = Mathf.Abs(Mathf.Sin(t)) * 0.3f * idle;
            float rear = Mathf.Abs(Mathf.Sin(t + 1.6f)) * 0.3f * idle;
            float lift = (front + rear) * 0.5f - 0.05f * (1f - idle) + kick * 0.2f;
            float pitch = Mathf.Atan2(front - rear, 3.4f) * Mathf.Rad2Deg + kick * 10f;
            frame.localPosition = new Vector3(0f, lift, 0f);
            frame.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        }
    }
}
