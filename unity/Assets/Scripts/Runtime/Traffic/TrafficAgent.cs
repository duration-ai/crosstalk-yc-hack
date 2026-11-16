using System.Collections.Generic;
using Dogpatch.Ambient;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // Something a traffic agent must not drive into: another agent, the player, a parked car.
    public struct Obstacle
    {
        public Vector2 Position;
        public Vector2 Forward;
        public Vector2 Velocity;
        public Vector2 Half;
        public object Owner;
        public bool Player;
        public bool Agent;
    }

    // A stop on a fixed route: a bus stop on route lane Index, or a platform on rail track Index.
    public sealed class RouteStop
    {
        public int Index;
        public float S;
        public string Name;
    }

    // One AI vehicle. It drives its CarController kinematically (Autopilot) along the lane graph, or a
    // rail track: lane-keeping on the lane centreline, following distance to whatever is ahead,
    // full stops at stop signs, signals, pedestrians at crossings, bus stops and platforms, and
    // reactions to the player (brake, swerve, honk) and to being hit.
    public sealed partial class TrafficAgent : MonoBehaviour, ITrafficParticipant, IShoveable
    {
        public CarController Car;
        public VehicleSpec Spec;
        public StreetGraph.Lane Lane;
        public StreetGraph.Link Link;
        public TrackPath[] Rails;
        public int RailIndex;
        public List<StreetGraph.Lane> Route;
        public int RouteIndex;
        public List<RouteStop> Stops;
        public float S;
        public float Speed;
        public bool Far;
        public bool Knocked;
        public float Idle;
        public string State = "";

        Rigidbody body;
        VehicleAudio audioSource;
        StreetGraph.Link nextLink;
        int nextStop;
        bool stopCleared;
        float hold;
        float dwellUntil;
        float stunUntil;
        float creepUntil;
        float blocked;
        float lateral;
        float swerve;
        float baseLateral;
        float prevYaw;
        Vector2 rear;
        bool hasRear;
        float accel;
        float brake;
        float headway;
        float minGap;
        float stopHold;

        public Vector3 Position => transform.position;

        public Vector3 Velocity => transform.forward * Speed;

        public bool CanBeShoved => Spec != null && Spec.Rider && !Knocked && enabled;

        public bool OnRail => Rails != null;

        public TrackPath CurrentPath => OnRail ? Rails[RailIndex] : Link != null ? Link.Path : Lane.Path;

        public void Init(CarController car, VehicleSpec spec)
        {
            Car = car;
            Spec = spec;
            body = car.GetComponent<Rigidbody>();
            audioSource = car.GetComponent<VehicleAudio>();
            Link = null;
            nextLink = null;
            Rails = null;
            Route = null;
            Stops = null;
            nextStop = 0;
            stopCleared = false;
            hold = dwellUntil = stunUntil = creepUntil = blocked = swerve = Idle = 0f;
            Knocked = false;
            hasRear = false;
            bool cautious = spec.Robotaxi;
            accel = cautious ? 1.8f : spec.TwoWheeler ? 1.6f : spec.Id == VehicleTypes.MuniBus || spec.Id == VehicleTypes.TechShuttle ? 1.1f : spec.Id == VehicleTypes.TThird ? 1.0f : 2.6f;
            brake = cautious ? 3.5f : spec.Id == VehicleTypes.TThird ? 1.4f : 5f;
            headway = cautious ? 2.0f : 1.3f;
            minGap = cautious ? 3.5f : spec.TwoWheeler ? 1.5f : 2.5f;
            stopHold = cautious ? 1.6f : 0.6f;
            car.Autopilot = true;
            car.Enterable = !spec.Rider;
            body.isKinematic = true;
            enabled = true;
        }

        public void PlaceOnLane(StreetGraph.Lane lane, float s, float speed)
        {
            Lane = lane;
            S = s;
            Speed = speed;
            baseLateral = Spec.Rider || Spec.TwoWheeler ? Mathf.Max(0f, lane.Width * 0.5f - 0.55f) * (lane.Index == lane.Count - 1 ? 1f : 0f) : 0f;
            lateral = baseLateral;
            hasRear = false;
            Pose(0.02f, true);
        }

        public void PlaceOnRail(TrackPath[] rails, int index, float s, List<RouteStop> stops)
        {
            Rails = rails;
            RailIndex = index;
            S = s;
            Speed = 0f;
            Stops = stops;
            nextStop = 0;
            while (nextStop < Stops.Count && (Stops[nextStop].Index != RailIndex || Stops[nextStop].S < S - 2f)) nextStop++;
            baseLateral = lateral = 0f;
            hasRear = false;
            Pose(0.02f, true);
        }

        // Moves along the current track or lane without changing it (used to stage recordings).
        public void JumpTo(float s, float speed)
        {
            S = Mathf.Clamp(s, 0f, CurrentPath.Length);
            Speed = speed;
            dwellUntil = 0f;
            if (Stops != null)
            {
                nextStop = 0;
                int index = OnRail ? RailIndex : RouteIndex;
                while (nextStop < Stops.Count && (Stops[nextStop].Index < index || (Stops[nextStop].Index == index && Stops[nextStop].S < S - 2f))) nextStop++;
            }
            hasRear = false;
            Pose(0.02f, true);
        }

        // A ride (CabRide): follow the route at a brisk pace through lights and stop signs and pull
        // up on the goal lane at goalS. A null route ends the ride and the agent drives freely again.
        public bool Express;
        public StreetGraph.Lane GoalLane;
        public float GoalS;
        float pass;
        float passUntil;
        float boxed;
        float ghostUntil;

        public void DriveTo(List<StreetGraph.Lane> route, StreetGraph.Lane goal, float goalS)
        {
            if (route == null) Route = null;
            else FollowRoute(route, 0, new List<RouteStop>());
            nextLink = null;
            Express = route != null;
            GoalLane = goal;
            GoalS = goalS;
        }

        public void FollowRoute(List<StreetGraph.Lane> route, int index, List<RouteStop> stops)
        {
            Route = route;
            RouteIndex = index;
            Stops = stops;
            nextStop = 0;
            while (nextStop < stops.Count && stops[nextStop].Index < index) nextStop++;
        }

        void OnEnable() => Yielding.Vehicles.Add(this);

        void OnDisable() => Yielding.Vehicles.Remove(this);

        void Limit(ref float target, float distance, float rate)
        {
            if (distance < 0.35f) target = 0f;
            else target = Mathf.Min(target, Mathf.Sqrt(2f * rate * (distance - 0.3f)));
        }

        // A point on the road distance metres ahead, following the lane into the chosen turn.
        Vector2 Ahead(float distance)
        {
            var path = CurrentPath;
            float s = S + distance;
            if (s <= path.Length || OnRail) return path.Sample(s, out _);
            s -= path.Length;
            if (Link == null)
            {
                if (nextLink == null) nextLink = ChooseLink();
                if (nextLink == null) return path.Sample(path.Length, out _);
                if (s <= nextLink.Path.Length) return nextLink.Path.Sample(s, out _);
                return nextLink.To.Path.Sample(s - nextLink.Path.Length, out _);
            }
            return Link.To.Path.Sample(s, out _);
        }

        StreetGraph.Link ChooseLink()
        {
            if (Lane.Next.Count == 0) return null;
            if (Route != null)
            {
                var want = Route[(RouteIndex + 1) % Route.Count];
                foreach (var link in Lane.Next)
                    if (link.To == want) return link;
                Debug.LogWarning("[Traffic] " + Spec.Id + " lost its route at lane " + Lane.Id + "; driving freely");
                Route = null;
            }
            float total = 0f;
            foreach (var link in Lane.Next) total += Weight(link);
            float pick = Random.value * total;
            foreach (var link in Lane.Next)
            {
                pick -= Weight(link);
                if (pick <= 0f) return link;
            }
            return Lane.Next[Lane.Next.Count - 1];
        }

        float Weight(StreetGraph.Link link)
        {
            float turn = Mathf.Abs(link.Turn);
            float weight = turn > 150f ? (Lane.Next.Count == 1 ? 1f : 0.02f) : turn < 30f ? 3f : 1f;
            return TrafficSystem.Crosses(link.To, Car.HalfExtents.x) ? weight * 0.001f : weight;
        }

        public void Step(float dt, TrafficSystem system)
        {
            if (Car == null || !Car.enabled)
            {
                system.Release(this);
                return;
            }
            if (Car.Stunned)
            {
                Speed = 0f;
                State = "knocked";
                return;
            }
            if (!body.isKinematic)
            {
                body.isKinematic = true;
                var snap = CurrentPath.Sample(S, out _);
                if (Vector2.Distance(snap, new Vector2(transform.position.x, transform.position.z)) > 2.5f + Mathf.Abs(lateral))
                {
                    system.Release(this);
                    return;
                }
            }

            var path = CurrentPath;
            path.Sample(S, out var forward);
            var right = new Vector2(forward.y, -forward.x);
            var here = new Vector2(transform.position.x, transform.position.z);
            float halfLength = Car.HalfExtents.z;
            float halfWidth = Car.HalfExtents.x;
            float now = Time.time;

            float cruise = OnRail ? Spec.CruiseSpeed
                : Express ? Mathf.Max(Car.Handling.TopSpeed, 25f)
                : Mathf.Min(Spec.CruiseSpeed, Lane.Speed * (Spec.Robotaxi ? 0.8f : 1f));
            float target = cruise * Mathf.Clamp(Pace * (1f + Rage * 0.6f), 0.3f, 2.5f);
            if (!OnRail && Link != null && Mathf.Abs(Link.Turn) > 30f) target = Mathf.Min(target, Express ? 9f : Spec.Robotaxi ? 4.5f : 6f);
            if (AllStop) target = 0f;
            float rate = brake;
            State = "cruising";

            if (Express && GoalLane != null)
            {
                float toGoal = float.MaxValue;
                if (Link == null && Lane == GoalLane) toGoal = GoalS - S;
                else if (Link != null && Link.To == GoalLane) toGoal = Link.Path.Length - S + GoalS;
                else if (Link == null && Route != null && Route[(RouteIndex + 1) % Route.Count] == GoalLane)
                    toGoal = Lane.Path.Length - S + (nextLink != null ? nextLink.Path.Length : 0f) + GoalS;
                if (toGoal < float.MaxValue)
                {
                    Limit(ref target, toGoal, brake * 2f);
                    State = "pulling up";
                }
            }

            if (!OnRail && Link == null && !Express)
            {
                float stopDistance = Lane.Path.Length - S - halfLength - 0.6f;
                var node = Lane.To;
                if (node.Control == NodeControl.Signal)
                {
                    int signal = node.SignalState(Lane.EndDirection, now);
                    bool canStop = stopDistance > Speed * Speed / (2f * brake) * (Spec.Robotaxi ? 0.6f : 0.9f);
                    if ((signal == 2 && Rage < 0.75f) || (signal == 1 && canStop && Rage == 0f))
                    {
                        Limit(ref target, stopDistance, brake);
                        State = signal == 2 ? "red light" : "yellow light";
                    }
                }
                else if (Lane.StopAtEnd && !stopCleared)
                {
                    Limit(ref target, stopDistance, brake);
                    State = "approaching stop sign";
                    if (stopDistance < 1.6f && Speed < 0.1f)
                    {
                        hold += dt;
                        State = "stopped at stop sign";
                        if (hold >= stopHold && (node.Occupants == 0 || hold > stopHold + 4f)) stopCleared = true;
                    }
                }
            }

            if (Stops != null && nextStop < Stops.Count && Stops[nextStop].Index == (OnRail ? RailIndex : RouteIndex) && (OnRail || Link == null))
            {
                var stop = Stops[nextStop];
                float d = stop.S - S;
                if (d < -3f) nextStop++;
                else
                {
                    Limit(ref target, d, brake);
                    if (d < 3f) State = "at stop " + stop.Name;
                    if (d < 2.5f && Speed < 0.15f)
                    {
                        if (dwellUntil <= 0f)
                        {
                            dwellUntil = now + (OnRail ? 14f : 7f);
                            audioSource?.Signal(1f);
                        }
                        if (now >= dwellUntil)
                        {
                            dwellUntil = 0f;
                            nextStop++;
                            audioSource?.Signal(1f);
                        }
                    }
                }
            }
            else if (Stops != null && nextStop >= Stops.Count && OnRail == false && Route != null && RouteIndex == 0) nextStop = 0;

            if (!OnRail)
            {
                float reach = Spec.Robotaxi ? 18f : 12f;
                float radius = Lane.Width * 0.5f + 1f;
                for (float d = 2f; d <= reach; d += 3f)
                {
                    var p = Ahead(halfLength + d);
                    if (!Yielding.PedestrianNear(new Vector3(p.x, 0f, p.y), radius)) continue;
                    Limit(ref target, d - 1.5f, brake * 1.4f);
                    State = "yielding to pedestrian";
                    break;
                }
            }

            if (TrafficSystem.HasZones)
            {
                if (!OnRail && Route == null && Link == null && nextLink != null && TrafficSystem.Crosses(nextLink.To, halfWidth)) nextLink = ChooseLink();
                for (float d = 2f; d <= 26f; d += 3f)
                {
                    if (!TrafficSystem.InZone(Ahead(halfLength + d), halfWidth)) continue;
                    Limit(ref target, d - 2f, brake * 1.4f);
                    State = "holding for closed road";
                    break;
                }
            }

            bool blockedByAgent = false;
            bool playerAhead = false;
            float look = OnRail ? 60f : 40f;
            foreach (var ob in system.Obstacles)
            {
                if (ob.Owner == (object)this || ob.Owner == (object)Car) continue;
                if (ob.Agent && now < creepUntil) continue;
                var rel = ob.Position - here;
                if (rel.sqrMagnitude > look * look) continue;
                float f = Vector2.Dot(rel, forward);
                if (f <= 0f) continue;
                float l = Vector2.Dot(rel, right);
                float c = Mathf.Abs(Vector2.Dot(ob.Forward, forward));
                float sn = Mathf.Abs(Vector2.Dot(ob.Forward, right));
                float obLong = ob.Half.y * c + ob.Half.x * sn;
                float obLat = ob.Half.y * sn + ob.Half.x * c;
                if (Mathf.Abs(l) > halfWidth + obLat + 0.35f) continue;
                float gap = f - halfLength - obLong;
                if (Express && !ob.Player)
                {
                    if (now < ghostUntil) continue;
                    if (gap < 30f && Link == null)
                    {
                        pass = -(Lane.Width + 0.4f);
                        passUntil = now + 2.5f;
                        State = "overtaking";
                    }
                    float tight = gap - 0.8f;
                    float before2 = target;
                    Limit(ref target, tight, brake * 1.5f);
                    if (target < before2) blockedByAgent = true;
                    continue;
                }
                float lead = Mathf.Max(0f, Vector2.Dot(ob.Velocity, forward));
                float allowed = gap - minGap * (1f - Rage * 0.6f) - headway * (1f - Rage * 0.7f) * Mathf.Max(0f, Speed - lead) * 0.5f + lead * lead / (2f * brake);
                float before = target;
                Limit(ref target, allowed, Mathf.Max(brake, 4f));
                if (target >= before) continue;
                if (ob.Agent) blockedByAgent = true;
                if (Rage > 0f && gap < 14f) audioSource?.Signal(2.5f - Rage * 1.5f);
                State = ob.Player ? "braking for player" : "following";
                if (!ob.Player) continue;
                playerAhead = true;
                if (gap < 9f && audioSource != null && audioSource.Signal(Spec.Robotaxi ? 6f : 4f)) State = "honking at player";
                if (!OnRail && Link == null && gap < 16f && Mathf.Abs(l) > obLat * 0.25f)
                    swerve = Mathf.Clamp(-Mathf.Sign(l) * 1.3f, -Lane.Width * 0.4f, Lane.Width * 0.4f);
            }

            if (Express)
            {
                boxed = blockedByAgent && Speed < 8f ? boxed + dt : 0f;
                if (boxed > 1f)
                {
                    ghostUntil = now + 3f;
                    boxed = 0f;
                    State = "ghosting through";
                }
                if (now > passUntil) pass = Mathf.MoveTowards(pass, 0f, 2.5f * dt);
            }
            else pass = 0f;

            if (blockedByAgent && Speed < 0.2f)
            {
                blocked += dt;
                if (blocked > 10f)
                {
                    creepUntil = now + 2.5f;
                    blocked = 0f;
                }
            }
            else blocked = 0f;

            if (now < stunUntil)
            {
                target = 0f;
                rate = brake * 2f;
                State = "hit";
            }

            Speed = Mathf.MoveTowards(Speed, target, (target > Speed ? (Express ? accel * 2.5f : accel) : Express ? rate * 2f : rate) * dt);
            Idle = Speed < 0.05f ? Idle + dt : 0f;
            if (!playerAhead) swerve = Mathf.MoveTowards(swerve, 0f, 0.6f * dt);

            S += Speed * dt;
            for (int guard = 0; guard < 4 && S > CurrentPath.Length; guard++)
            {
                S -= CurrentPath.Length;
                if (OnRail)
                {
                    RailIndex = (RailIndex + 1) % Rails.Length;
                    nextStop = 0;
                    while (nextStop < Stops.Count && Stops[nextStop].Index != RailIndex) nextStop++;
                    continue;
                }
                if (Link == null)
                {
                    var link = nextLink ?? ChooseLink();
                    nextLink = null;
                    if (link == null)
                    {
                        system.Despawn(this);
                        return;
                    }
                    stopCleared = false;
                    hold = 0f;
                    if (Spec.Id == VehicleTypes.Zoox && link.To.Piece == Lane.Piece)
                    {
                        Lane = link.To;
                        S = 0f;
                        if (Route != null)
                        {
                            RouteIndex = (RouteIndex + 1) % Route.Count;
                            if (Route[RouteIndex] != Lane) Route = null;
                        }
                        continue;
                    }
                    Link = link;
                    Lane.To.Occupants++;
                }
                else
                {
                    Link.From.To.Occupants--;
                    Lane = Link.To;
                    Link = null;
                    if (Route != null)
                    {
                        RouteIndex = (RouteIndex + 1) % Route.Count;
                        if (RouteIndex == 0) nextStop = 0;
                        if (Route[RouteIndex] != Lane) Route = null;
                    }
                }
            }

            Pose(dt, Far);
        }

        // Off the rails the body steers like a real vehicle: the front axle follows the road half a
        // wheelbase ahead of S, into the chosen turn, and the rear axle is dragged after it at a fixed
        // wheelbase. Heading turns smoothly through polyline corners and lane-to-link handoffs instead
        // of snapping to each segment, its rate is bounded by speed over wheelbase, and long vehicles
        // swing wide at the front while the rear cuts in, rather than pivoting about their middle.
        void Pose(float dt, bool teleport)
        {
            var p = CurrentPath.Sample(S, out var t);
            var right = new Vector2(t.y, -t.x);
            bool dwelling = dwellUntil > 0f && !OnRail;
            float wantLateral = baseLateral + swerve + pass + (dwelling ? 0.8f : 0f) + Weave;
            lateral = Mathf.MoveTowards(lateral, wantLateral, (Express ? 5f : 1.5f) * dt);
            p += right * lateral;
            float yaw = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;
            if (!OnRail)
            {
                float wheelbase = Mathf.Max(1.2f, Car.HalfExtents.z * 1.2f);
                var front = Ahead(wheelbase * 0.5f) + right * lateral;
                var arm = front - rear;
                if (!hasRear || arm.magnitude < wheelbase * 0.5f || arm.magnitude > wheelbase * 1.5f) arm = t * wheelbase;
                hasRear = true;
                rear = front - arm.normalized * wheelbase;
                p = (front + rear) * 0.5f;
                yaw = Mathf.Atan2(arm.x, arm.y) * Mathf.Rad2Deg;
            }
            var position = new Vector3(p.x, Dogpatch.World.RoadSurface.GroundY(p) + 0.05f, p.y);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            if (teleport || body == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                if (body != null)
                {
                    body.position = position;
                    body.rotation = rotation;
                }
            }
            else
            {
                body.MovePosition(position);
                body.MoveRotation(rotation);
            }
            float steer = dt > 0f ? Mathf.DeltaAngle(prevYaw, yaw) / dt / 35f : 0f;
            prevYaw = yaw;
            Car.SetAutopilotMotion(Speed, steer);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!enabled || Car == null || Express) return;
            float impact = collision.relativeVelocity.magnitude;
            if (impact < 2f) return;
            if (Spec.Rider)
            {
                KnockOff(collision.relativeVelocity);
                return;
            }
            stunUntil = Time.time + Mathf.Lerp(1f, 3f, impact / 15f);
            Speed *= 0.3f;
            var away = transform.InverseTransformPoint(collision.GetContact(0).point).x;
            if (!OnRail && Link == null) swerve = Mathf.Clamp(-Mathf.Sign(away) * 1.2f, -Lane.Width * 0.4f, Lane.Width * 0.4f);
            audioSource?.Signal(1.5f);
        }

        public void Shove(Vector3 impulse, GameObject by) => KnockOff(impulse);

        // The courier tumbles off, the bike stops where it is and anyone can take it.
        public void KnockOff(Vector3 impulse)
        {
            if (!CanBeShoved) return;
            Knocked = true;
            var identity = Car.GetComponent<VehicleIdentity>();
            if (identity != null && identity.Rider != null)
            {
                var rider = identity.Rider;
                identity.Rider = null;
                rider.transform.SetParent(null, true);
                rider.AddComponent<FallenRider>().Push = new Vector3(impulse.x, 0f, impulse.z);
            }
            audioSource?.Signal(0.5f);
            Speed = 0f;
            Car.Enterable = true;
            TrafficSystem.Instance?.Release(this);
        }
    }

    // A courier knocked off their bike: tips over, slides a little, lies there, then shrinks away.
    public sealed class FallenRider : MonoBehaviour
    {
        public Vector3 Push;
        float t;
        Quaternion start;
        Quaternion fallen;

        void Start()
        {
            start = transform.rotation;
            var push = Push.sqrMagnitude > 0.01f ? Push.normalized : transform.right;
            fallen = Quaternion.AngleAxis(85f, Vector3.Cross(Vector3.up, push)) * start;
            Push = push * Mathf.Clamp(Push.magnitude * 0.25f, 1.5f, 4f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.45f);
            transform.rotation = Quaternion.Slerp(start, fallen, k * k);
            transform.position += Push * Mathf.Max(0f, 1f - t / 0.8f) * Time.deltaTime;
            var p = transform.position;
            transform.position = new Vector3(p.x, Mathf.Max(0.05f, p.y - Time.deltaTime * 1.2f), p.z);
            if (t > 6f) transform.localScale = Vector3.one * Mathf.Max(0f, 1f - (t - 6f));
            if (t > 7f) Destroy(gameObject);
        }
    }
}
