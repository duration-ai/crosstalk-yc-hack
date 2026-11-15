using System;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Smash
{
    public struct VehicleSpec
    {
        public float Mass;          // kg
        public float Durability;    // crash budget: sum of impact speed x mass ratio x (1 - BargeStrength)
        public float BargeStrength; // 0..1 share of its own momentum a vehicle keeps through a collision
    }

    // Where vehicle mass and durability come from. CROS-21's HandlingProfile plugs in through
    // Provider; until then a vehicle's Polyfork mass sets all three.
    public static class VehicleSpecs
    {
        public static Func<CarController, VehicleSpec?> Provider;

        public static VehicleSpec For(CarController car)
        {
            var spec = Provider?.Invoke(car);
            if (spec.HasValue) return spec.Value;
            float mass = PhysicsCatalog.VehicleMassKg(car.ModelId);
            if (mass <= 0f) mass = 1300f;
            return new VehicleSpec
            {
                Mass = mass,
                Durability = 60f * Mathf.Sqrt(mass / 1300f),
                BargeStrength = Mathf.Clamp01((mass - 1300f) / 7000f),
            };
        }
    }

    public readonly struct VehicleHit
    {
        public readonly CarController Car;
        public readonly Collider Other;
        public readonly CarController OtherCar;
        public readonly float ImpactSpeed;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Damage;

        public VehicleHit(CarController car, Collider other, CarController otherCar, float impactSpeed, Vector3 point, Vector3 normal, float damage)
        {
            Car = car;
            Other = other;
            OtherCar = otherCar;
            ImpactSpeed = impactSpeed;
            Point = point;
            Normal = normal;
            Damage = damage;
        }
    }

    public static class VehicleImpacts
    {
        // Every hit on any vehicle above about 2 m/s: walls, props, other vehicles.
        public static event Action<VehicleHit> Hit;

        internal static void Raise(in VehicleHit hit) => Hit?.Invoke(hit);
    }
}
