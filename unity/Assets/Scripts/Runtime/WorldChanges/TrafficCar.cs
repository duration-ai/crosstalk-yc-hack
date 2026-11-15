using Dogpatch.Gameplay;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // A car called in by the Traffic change: it cruises along the race route in a lane, kinematic,
    // so racers have to go around it.
    [RequireComponent(typeof(CarController))]
    public sealed class TrafficCar : MonoBehaviour
    {
        public RoutePath Route;
        public float Distance;
        public float Speed;
        public float Lane;
        public bool Oncoming;

        Rigidbody body;

        void Awake() => body = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            if (Route == null) return;
            Distance += (Oncoming ? -Speed : Speed) * CarController.SpeedScale * Time.fixedDeltaTime;
            if (Distance < 0f || Distance > Route.Length)
            {
                Destroy(gameObject);
                return;
            }
            var p = Route.PointAt(Distance) + Route.RightAt(Distance) * Lane;
            var dir = Route.DirectionAt(Distance) * (Oncoming ? -1f : 1f);
            float y = transform.position.y;
            body.MovePosition(new Vector3(p.x, y, p.y));
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.Euler(0f, RoutePath.Heading(dir), 0f), 6f * Time.fixedDeltaTime));
        }
    }
}
