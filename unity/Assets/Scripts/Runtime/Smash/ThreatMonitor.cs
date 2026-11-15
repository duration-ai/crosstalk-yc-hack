using System.Collections.Generic;
using Dogpatch.Ambient;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Warns anything on foot (IVehicleReactive, CROS-20's pedestrians) about vehicles heading at
    // it with a time to impact, passes on actual hits, and scores a NEAR MISS for the driver when
    // someone had to dive out of the way and wasn't hit.
    public sealed class ThreatMonitor : MonoBehaviour
    {
        public const float WarningSeconds = 1.5f;
        public const float NearMissSeconds = 1f;
        const float MinSpeed = 3f;
        const float Margin = 0.6f;
        const int NearMissPoints = 100;

        static ThreatMonitor instance;
        readonly Collider[] hits = new Collider[64];
        readonly Dictionary<Collider, IVehicleReactive> receivers = new Dictionary<Collider, IVehicleReactive>();
        readonly HashSet<IVehicleReactive> warned = new HashSet<IVehicleReactive>();
        readonly Dictionary<(VehicleDamage car, IVehicleReactive target), (float until, Vector3 at)> pending = new Dictionary<(VehicleDamage, IVehicleReactive), (float, Vector3)>();
        readonly Dictionary<IVehicleReactive, float> scoredUntil = new Dictionary<IVehicleReactive, float>();
        readonly List<(VehicleDamage, IVehicleReactive)> done = new List<(VehicleDamage, IVehicleReactive)>();

        public static int ThreatsThisStep { get; private set; }

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        internal static void NotifyImpact(VehicleDamage car, IVehicleReactive target, Vector3 point, Vector3 relativeVelocity)
        {
            if (instance != null) instance.pending.Remove((car, target));
            target.OnVehicleImpact(car.gameObject, point, relativeVelocity);
        }

        void FixedUpdate()
        {
            ThreatsThisStep = 0;
            var cars = VehicleDamage.All;
            for (int c = 0; c < cars.Count; c++)
            {
                var car = cars[c];
                if (car.Body.isKinematic || car.IsWrecked) continue;
                var v = car.Body.linearVelocity;
                v.y = 0f;
                float speed = v.magnitude;
                if (speed < MinSpeed) continue;
                var dir = v / speed;
                var origin = car.transform.position;
                var ext = car.Car.HalfExtents;
                float reach = speed * WarningSeconds + ext.z;
                int n = Physics.OverlapSphereNonAlloc(origin + dir * reach * 0.5f, reach * 0.5f + 2f, hits, ~0, QueryTriggerInteraction.Collide);
                warned.Clear();
                for (int i = 0; i < n; i++)
                {
                    var col = hits[i];
                    if (!receivers.TryGetValue(col, out var target))
                    {
                        target = col.GetComponentInParent<IVehicleReactive>();
                        receivers[col] = target;
                    }
                    if (target == null || !warned.Add(target)) continue;
                    var to = col.bounds.center - origin;
                    to.y = 0f;
                    float along = Vector3.Dot(to, dir);
                    if (along <= 0f) continue;
                    float lateral = (to - dir * along).magnitude;
                    float halfWidth = Mathf.Abs(Vector3.Dot(car.transform.right, dir)) * ext.z + Mathf.Abs(Vector3.Dot(car.transform.forward, dir)) * ext.x;
                    if (lateral > halfWidth + Margin + col.bounds.extents.x) continue;
                    float frontGap = Mathf.Abs(Vector3.Dot(car.transform.forward, dir)) * ext.z + Mathf.Abs(Vector3.Dot(car.transform.right, dir)) * ext.x;
                    float tti = Mathf.Max(0f, along - frontGap) / speed;
                    if (tti > WarningSeconds) continue;
                    target.OnVehicleThreat(car.gameObject, v, tti);
                    ThreatsThisStep++;
                    if (tti <= NearMissSeconds && !pending.ContainsKey((car, target)))
                        pending[(car, target)] = (Time.time + tti + 0.9f, col.bounds.center);
                }
            }

            done.Clear();
            foreach (var pair in pending)
                if (Time.time >= pair.Value.until) done.Add(pair.Key);
            foreach (var key in done)
            {
                var at = pending[key].at;
                pending.Remove(key);
                var (car, target) = key;
                if (car == null || (target is Object o && o == null)) continue;
                if (scoredUntil.TryGetValue(target, out float until) && Time.time < until) continue;
                scoredUntil[target] = Time.time + 4f;
                SmashScore.Add(car.Car, SmashKind.NearMiss, "NEAR MISS", NearMissPoints, at + Vector3.up * 2f);
            }
        }
    }
}
