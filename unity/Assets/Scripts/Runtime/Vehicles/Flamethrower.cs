using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using Dogpatch.Smash;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // The art car's poofer. A tap while driving blasts a jet of fire off its head and sets up to three
    // cars ahead (within 30 m and 25 degrees of the nose) on fire with set_fire's burning FireFx, so they
    // lose health over a few seconds. A driven racer is never burnt below the burn floor, so nothing is
    // one-shot; another phone's racer only gets the flames to look at, because its own phone owns its
    // damage. The deck and flank LEDs chase while the car exists.
    public sealed class Flamethrower : MonoBehaviour, IVehicleAbility
    {
        const float Cooldown = 2.5f;
        const float Range = 30f;
        const float Cone = 25f;
        const int MaxTargets = 3;
        const float BurnSeconds = 8f;
        static readonly Vector3 Muzzle = new Vector3(0f, 4.3f, 2.95f);

        CarController car;
        Material material;
        readonly Transform[] leds = new Transform[3];
        float ready;
        float nextLed;
        int phase;

        void Awake() => car = GetComponent<CarController>();

        public void Use()
        {
            if (!car.Driven || car.Stunned || Time.time < ready) return;
            ready = Time.time + Cooldown;
            if (material == null) material = GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var forward = transform.forward;
            var from = transform.TransformPoint(Muzzle);
            for (int i = 0; i < 5; i++)
            {
                var at = from + forward * (1.5f + i * 2.6f) + Vector3.down * (i * 0.5f);
                var jet = FireFx.Create(material, new Bounds(at, Vector3.one * (0.8f + i * 0.35f)), 0.7f, i == 0);
                jet.transform.SetParent(transform, true);
                Destroy(jet.gameObject, 0.8f);
            }

            var hits = new List<(float distance, VehicleDamage damage)>();
            foreach (var other in VehicleDamage.All)
            {
                if (other == null || other.Car == car || other.IsWrecked) continue;
                var to = other.transform.position - transform.position;
                if (to.magnitude > Range || Vector3.Angle(forward, to) > Cone || other.GetComponentInChildren<FireFx>() != null) continue;
                hits.Add((to.magnitude, other));
            }
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Count && i < MaxTargets; i++)
            {
                var target = hits[i].damage;
                var bounds = target.TryGetComponent<BoxCollider>(out var box) ? box.bounds : new Bounds(target.transform.position + Vector3.up, Vector3.one * 2f);
                var fire = FireFx.Create(material, bounds, 1.2f, i == 0);
                fire.Burns = target.Car.VehicleId == null || !target.Car.VehicleId.StartsWith("remote-");
                fire.transform.SetParent(target.transform, true);
                Destroy(fire.gameObject, BurnSeconds);
            }
            Debug.Log($"[ArtCar] flamethrower at {transform.position}: {hits.Count} cars in the cone, lit {Mathf.Min(hits.Count, MaxTargets)}");
        }

        void Update()
        {
            if (Time.time < nextLed) return;
            nextLed = Time.time + 0.12f;
            if (leds[0] == null)
                for (int i = 0; i < leds.Length; i++) leds[i] = VehicleRoster.RigPart(car, "led-" + i);
            if (leds[0] == null || leds[1] == null || leds[2] == null) return;
            phase = (phase + 1) % leds.Length;
            for (int i = 0; i < leds.Length; i++) leds[i].gameObject.SetActive(i == phase);
        }
    }
}
