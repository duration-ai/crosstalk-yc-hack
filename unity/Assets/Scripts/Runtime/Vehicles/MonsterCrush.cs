using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Smash;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // The monster truck's signature: a car it hits at speed is driven over. The car is squashed flat
    // and takes a heavy hit through VehicleDamage (a racer is floored like fire damage, never wrecked
    // by this alone), stops colliding with the truck, and the truck hops up over it. Hard landings
    // bounce back up. A tap while driving hops the truck (its IVehicleAbility).
    public sealed class MonsterCrush : MonoBehaviour, IVehicleAbility
    {
        const float HopSpeed = 7f;
        const float Cooldown = 1f;

        CarController car;
        Rigidbody body;
        float lastVy;
        float ready;

        void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable() => VehicleImpacts.Hit += OnHit;

        void OnDisable() => VehicleImpacts.Hit -= OnHit;

        public void Use()
        {
            if (!car.Driven || body.isKinematic || car.Stunned || Time.time < ready) return;
            if (!Physics.Raycast(transform.position + Vector3.up * 0.6f, Vector3.down, 1.6f, ~0, QueryTriggerInteraction.Ignore)) return;
            ready = Time.time + Cooldown;
            CarFx.On(car).Launch(HopSpeed);
            Debug.Log($"[Monster] hop at {transform.position} speed {car.Speed:F1}");
        }

        void OnHit(VehicleHit hit)
        {
            var victim = hit.Car == car ? hit.OtherCar : hit.OtherCar == car ? hit.Car : null;
            if (victim == null || hit.ImpactSpeed < 3f || victim.GetComponent<CrushedFlat>() != null) return;
            victim.gameObject.AddComponent<CrushedFlat>().Crush(this);
            if (body.isKinematic) return;
            car.Stun(0.5f);
            var v = body.linearVelocity;
            v.y = Mathf.Max(v.y, 4.5f);
            body.linearVelocity = v;
            Debug.Log($"[Monster] crushed {victim.name} at {hit.ImpactSpeed:F1} m/s");
        }

        void FixedUpdate()
        {
            float vy = body.isKinematic ? 0f : body.linearVelocity.y;
            if (lastVy < -5f && vy > -1f && !body.isKinematic)
            {
                car.Stun(0.25f);
                var v = body.linearVelocity;
                v.y = -lastVy * 0.4f;
                body.linearVelocity = v;
            }
            lastVy = vy;
        }
    }

    // A car the monster truck has flattened. It pops back to shape and collides with the truck again
    // once it is repaired or its model is rebuilt; collisions also come back if the truck stops being
    // a monster truck.
    public sealed class CrushedFlat : MonoBehaviour
    {
        readonly List<(Transform part, Vector3 scale)> squashed = new List<(Transform, Vector3)>();
        readonly List<(Collider mine, Collider theirs)> ignored = new List<(Collider, Collider)>();
        MonsterCrush truck;
        VehicleDamage damage;
        float damageAtCrush;

        public void Crush(MonsterCrush by)
        {
            truck = by;
            foreach (Transform child in transform)
                if (child.GetComponentInChildren<MeshRenderer>() != null && child.GetComponent<Collider>() == null)
                {
                    squashed.Add((child, child.localScale));
                    child.localScale = Vector3.Scale(child.localScale, new Vector3(1.1f, 0.35f, 1.05f));
                }
            damage = GetComponent<VehicleDamage>();
            if (damage != null)
            {
                damage.Burn(0.6f);
                damageAtCrush = damage.Damage;
            }
            foreach (var mine in by.GetComponentsInChildren<Collider>())
                foreach (var theirs in GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(mine, theirs, true);
                    ignored.Add((mine, theirs));
                }
        }

        void Update()
        {
            bool repaired = damage != null && damage.Damage < damageAtCrush * 0.5f;
            bool rebuilt = squashed.Exists(s => s.part == null);
            if (!repaired && !rebuilt && truck != null) return;
            foreach (var (mine, theirs) in ignored)
                if (mine != null && theirs != null) Physics.IgnoreCollision(mine, theirs, false);
            ignored.Clear();
            if (!repaired && !rebuilt) return;
            foreach (var (part, scale) in squashed)
                if (part != null) part.localScale = scale;
            Destroy(this);
        }
    }
}
