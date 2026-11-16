using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Race
{
    // Drives a car down the race route the way a decent player would: aims at a point ahead on its
    // lane, brakes early for corners, steps around a car blocking its lane, and backs out when
    // stuck. Skill caps its throttle. The route already follows one-way streets.
    public sealed class BotDriver : MonoBehaviour
    {
        public CarController Car;
        public RoutePath Route;
        public float Skill = 0.9f;
        public float Lane;

        // How far a lane plus a sidestep may pull a bot off the route: clear of the cars parked along
        // the kerbs, which sit some 4.3 m either side of it on the course's streets.
        const float MaxOffset = 2.2f;
        public bool Active;

        public float Progress { get; private set; }

        float stuckFor;
        float reverseUntil;
        float sidestep;
        float sidestepUntil;
        int recoveries;
        float lastRecovery;
        int streak;
        float lastRespawn = -100f;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public void ResetProgress(float progress)
        {
            Progress = progress;
            stuckFor = 0f;
            reverseUntil = 0f;
            sidestep = 0f;
            recoveries = 0;
            streak = 0;
            lastRespawn = -100f;
        }

        void Update()
        {
            if (Car == null || Route == null) return;
            var t = Car.transform;
            var pos = RoutePath.Flat(t.position);
            Progress = Route.Project(pos, Progress, 45f);
            if (!Active || !Car.Driven)
            {
                if (Car.Driven) Car.SetInput(Vector3.zero, 0f);
                return;
            }
            float dt = Time.deltaTime;
            float speed = Mathf.Abs(Car.Speed);
            if (Progress >= Route.Length - 2f)
            {
                Car.SetInput(Vector3.zero, 0f);
                return;
            }
            if (Time.time < reverseUntil)
            {
                Car.SetInput(-t.forward, 0.7f);
                return;
            }

            float probe = 8f + speed * 0.6f;
            int count = Physics.SphereCastNonAlloc(t.position + Vector3.up * 0.9f + t.forward * 2.6f, 0.9f, t.forward, hits, probe, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var other = hits[i].collider.GetComponentInParent<CarController>();
                if (other == null || other == Car) continue;
                var side = Vector3.Dot(other.transform.position - t.position, t.right);
                sidestep = side > 0f ? -3f : 3f;
                if (Mathf.Abs(Lane + sidestep) > MaxOffset) sidestep = -sidestep;
                sidestepUntil = Time.time + 1.2f;
                break;
            }
            if (Time.time > sidestepUntil) sidestep = Mathf.MoveTowards(sidestep, 0f, 2f * dt);

            float look = Mathf.Clamp(6f + speed * 0.55f, 6f, 18f);
            float at = Mathf.Min(Progress + look, Route.Length);
            var target = Route.PointAt(at) + Route.RightAt(at) * Mathf.Clamp(Lane + sidestep, -MaxOffset, MaxOffset);
            var desired = new Vector3(target.x - pos.x, 0f, target.y - pos.y);

            float turn = Route.TurnAhead(Progress, 10f + speed * speed / 13f);
            float corner = Mathf.Lerp(22f, 8f, Mathf.InverseLerp(12f, 85f, turn));
            float strength = Mathf.Min(Skill, corner / 22f * (0.9f + Skill * 0.1f));

            stuckFor = speed < 0.8f ? stuckFor + dt : Mathf.Max(0f, stuckFor - dt);
            if (stuckFor > 1.6f)
            {
                stuckFor = 0f;
                reverseUntil = Time.time + 1.1f;
                recoveries = Time.time - lastRecovery < 12f ? recoveries + 1 : 1;
                lastRecovery = Time.time;
                if (recoveries >= 3) Respawn();
            }
            Car.SetInput(desired.normalized, strength);
        }

        // Puts the car back on its lane a little further on. Stuck again at the same spot, it goes
        // further each time and drops to the middle of the road, so a blocked lane never holds it.
        void Respawn()
        {
            var at = Car.transform.position;
            string blocker = Physics.SphereCast(at + Vector3.up * 0.9f, 0.9f, Car.transform.forward, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.collider.name : "nothing";
            Debug.Log($"[SpeedDial] bot {name} stuck at {Progress:F0} m, ({at.x:F1},{at.y:F1},{at.z:F1}), {Mathf.Abs(Car.Speed):F1} m/s, ahead {blocker}");
            recoveries = 0;
            streak = Mathf.Abs(Progress - lastRespawn) < 15f ? streak + 1 : 0;
            lastRespawn = Progress;
            float d = Mathf.Min(Progress + 4f + streak * 10f, Route.Length - 3f);
            var p = Route.PointAt(d) + Route.RightAt(d) * (streak > 0 ? 0f : Lane);
            Car.Place(new Vector3(p.x, Car.transform.position.y + 0.5f, p.y), RoutePath.Heading(Route.DirectionAt(d)));
            Car.SetDriven(true);
            Debug.Log($"[SpeedDial] bot {name} unstuck at {d:F0} m");
        }
    }
}
