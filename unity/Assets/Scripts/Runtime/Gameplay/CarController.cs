using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Arcade car on a Rigidbody. The drag gives a world direction and a strength: the car steers
    // toward that direction, the strength sets the target speed, and dragging behind the car
    // backs it up. At speed, a drag more than 90 degrees off the nose pulls the handbrake: the tail
    // swings round toward it in a slide. With no input it rolls to a stop.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        public string VehicleId;
        public string ModelId;
        public bool Driven { get; private set; }

        // False while the vehicle can't be taken, such as a delivery bike with its rider still on.
        public bool Enterable = true;

        // Scales every car's top speed; the voice Operator's faster/slower/nitro effects set it.
        public static float SpeedScale = 1f;
        public float SpeedMul = 1f;
        public float Speed => forwardSpeed;
        public bool Reversing => reversing;
        public Vector3 HalfExtents { get; private set; }
        public bool Stunned => Time.time < stunnedUntil;

        // Hover vehicles (HandlingProfile.HoverHeight > 0) ride that high above the static ground under
        // them while driven, and settle onto it when not driven or while Grounding (getting out).
        public bool Hovers => handling.HoverHeight > 0f;
        public bool Grounding;
        public float HoverAltitude { get; private set; }
        public Vector3 HoverGround { get; private set; }
        readonly RaycastHit[] hoverHits = new RaycastHit[16];

        // While true, something else (the traffic AI) moves this car and FixedUpdate leaves the body
        // alone, even with the player aboard as a passenger.
        public bool Autopilot { get; set; }

        public HandlingProfile Handling
        {
            get => handling;
            set
            {
                handling = value ?? HandlingProfile.Default;
                if (body != null) body.mass = handling.Mass;
            }
        }

        HandlingProfile handling = HandlingProfile.Default;

        Rigidbody body;
        GameObject visual;
        Vector3 desired;
        float strength;
        float forwardSpeed;
        float steer;
        bool reversing;
        readonly List<Transform> wheels = new List<Transform>();
        readonly List<Transform> steering = new List<Transform>();
        float wheelSpin;
        float stunnedUntil;
        bool swinging;
        float swingSign;
        float recoverUntil;

        public const float FullLockAngle = 12f;
        const float SwingSpeed = 6f;
        const float SwingRate = 260f;

        void EndSwing()
        {
            swinging = false;
            recoverUntil = Time.time + 0.6f;
        }
        Vector3 groundNormal = Vector3.up;

        public static CarController Create(string vehicleId, GlbAsset model, Material material, Vector3 position, float heading) =>
            Create(vehicleId, model.Id, model.Instantiate(material), model.ComputeBounds(), position, heading);

        // Any vehicle body: a Polyfork model or one built in code, with its model-space bounds.
        public static CarController Create(string vehicleId, string modelId, GameObject visual, Bounds bounds, Vector3 position, float heading)
        {
            var go = new GameObject("Car " + vehicleId);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));

            var body = go.AddComponent<Rigidbody>();
            body.mass = HandlingProfile.Default.Mass;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.isKinematic = true;

            var car = go.AddComponent<CarController>();
            car.VehicleId = vehicleId;
            car.Rebuild(modelId, visual, bounds);
            return car;
        }

        // Swaps the body (model, colliders, wheels) in place; position, speed and driver are kept.
        public void Rebuild(string modelId, GameObject visual, Bounds bounds)
        {
            if (this.visual != null)
            {
                this.visual.SetActive(false);
                this.visual.transform.SetParent(null, false);
                Destroy(this.visual);
            }
            this.visual = visual;
            visual.transform.SetParent(transform, false);

            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            float lift = Mathf.Min(0.25f, bounds.size.y * 0.25f);
            box.center = new Vector3(bounds.center.x, (bounds.max.y + lift) * 0.5f, bounds.center.z);
            box.size = new Vector3(bounds.size.x * 0.95f, Mathf.Max(bounds.max.y - lift, 0.5f), bounds.size.z * 0.97f);
            if (box.sharedMaterial == null)
                box.sharedMaterial = new PhysicsMaterial("car") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };

            var wheel = GetComponent<SphereCollider>();
            if (wheel == null) wheel = gameObject.AddComponent<SphereCollider>();
            wheel.radius = lift;
            wheel.center = new Vector3(bounds.center.x, lift, bounds.center.z);
            wheel.sharedMaterial = box.sharedMaterial;

            ModelId = modelId;
            HalfExtents = bounds.extents;
            wheels.Clear();
            steering.Clear();
            foreach (var t in visual.GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("wheel-") && !t.name.EndsWith("-mesh")) wheels.Add(t);
                if (t.name.StartsWith("steer-f")) steering.Add(t);
            }
        }

        void Awake() => body = GetComponent<Rigidbody>();

        public void SetDriven(bool driven)
        {
            Driven = driven;
            Grounding = false;
            if (driven && !Autopilot) body.isKinematic = false;
            desired = Vector3.zero;
            strength = 0f;
        }

        public void SetInput(Vector3 direction, float amount)
        {
            desired = direction;
            strength = amount;
        }

        // Lets the autopilot report its motion so speed readouts and wheel animation stay right.
        public void SetAutopilotMotion(float speed, float steerInput)
        {
            forwardSpeed = speed;
            steer = Mathf.Clamp(steerInput, -1f, 1f);
            reversing = speed < -0.1f;
        }

        public void Place(Vector3 position, float heading)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = !Driven;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            body.position = position;
            body.rotation = transform.rotation;
            forwardSpeed = 0f;
        }

        // Hands the body to physics for a moment so knockback, launches and shoves play out
        // instead of being overwritten by the next step's steering. A parked car becomes dynamic.
        public void Stun(float seconds)
        {
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            body.isKinematic = false;
        }

        void FixedUpdate()
        {
            bool hover = Hovers;
            body.useGravity = !hover || Stunned;
            if (body.isKinematic || Stunned || Autopilot) return;
            float dt = Time.fixedDeltaTime;
            var forward = transform.forward;
            var right = transform.right;

            // Ground-follow on freeway decks and ramps only (the Drivable layer): drive along the
            // smoothed surface so ramps climb and descend instead of stair-stepping. Streets, curbs and
            // steep faces never match, and then today's flat behaviour runs unchanged.
            bool onDeck = Physics.Raycast(body.position + Vector3.up * 0.5f, Vector3.down, out var ground, 1.2f, World.Freeway.DrivableMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(ground.normal, Vector3.up) < 20f && !hover;
            groundNormal = onDeck ? Vector3.Slerp(groundNormal, ground.normal, 1f - Mathf.Exp(-12f * dt)).normalized : Vector3.up;
            var along = onDeck ? Vector3.ProjectOnPlane(forward, groundNormal).normalized : forward;
            forwardSpeed = Vector3.Dot(body.linearVelocity, along);

            // The drag becomes abstract controls first (steer -1..1, throttle -1..1 with negative for
            // reverse, brake, handbrake); everything after drives from those alone.
            float steerInput = 0f, throttle = 0f;
            bool brake = false;
            var planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (Driven && strength > 0f && desired.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(forward, desired, Vector3.up);
                if (!swinging && !reversing && forwardSpeed > SwingSpeed && Mathf.Abs(angle) > 90f)
                {
                    swinging = true;
                    swingSign = Mathf.Sign(angle);
                }
                if (swinging && (Mathf.Abs(angle) < 30f || planar.magnitude < 1.5f)) EndSwing();
                if (!swinging)
                {
                    bool pointingBack = Mathf.Abs(angle) > 125f;
                    if (!reversing && pointingBack && forwardSpeed < 1.5f) reversing = true;
                    else if (reversing && Mathf.Abs(angle) < 70f) reversing = false;
                    if (reversing)
                    {
                        throttle = -strength;
                        steerInput = Mathf.Clamp(Vector3.SignedAngle(-forward, desired, Vector3.up) / FullLockAngle, -1f, 1f);
                    }
                    else if (pointingBack) brake = true;
                    else
                    {
                        throttle = strength;
                        steerInput = Mathf.Clamp(angle / FullLockAngle, -1f, 1f);
                    }
                }
            }
            else if (swinging) EndSwing();
            bool handbrake = swinging;
            if (handbrake) steerInput = swingSign;

            float target = throttle >= 0f ? throttle * handling.TopSpeed * SpeedScale * SpeedMul : throttle * handling.ReverseSpeed;
            bool opposing = Mathf.Sign(target) != Mathf.Sign(forwardSpeed) && Mathf.Abs(forwardSpeed) > 0.5f;
            float grip = Mathf.Clamp01(handling.Grip * RoadConditions.GripMultiplier);
            float rate = handbrake ? handling.Braking * 0.3f : brake ? handling.Braking : target == 0f ? handling.Coasting : opposing ? handling.Braking : handling.Acceleration;
            rate *= Mathf.Lerp(0.5f, 1f, grip);
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, handbrake ? 0f : target, rate * dt);
            steer = Mathf.MoveTowards(steer, steerInput, 6f * dt);

            // Full-lock yaw rate: a turning circle of minRadius up to 10 m/s, tightening toward a pivot at
            // a crawl, easing to the profile's TurnRate by 20 m/s. The handbrake swings the tail out
            // at a fixed rate instead, a little slower for long vehicles.
            float minRadius = Mathf.Max(3.2f, HalfExtents.z * 1.1f);
            float v = Mathf.Abs(forwardSpeed);
            float yawRate;
            if (handbrake) yawRate = SwingRate * Mathf.Min(1f, 3.2f / minRadius) * Mathf.Clamp01(planar.magnitude / 4f);
            else if (v <= 10f) yawRate = v / Mathf.Lerp(1.5f, minRadius, Mathf.InverseLerp(1f, 5f, v)) * Mathf.Rad2Deg;
            else yawRate = Mathf.Max(handling.TurnRate, Mathf.Lerp(10f / minRadius * Mathf.Rad2Deg, handling.TurnRate, (v - 10f) / 10f));
            body.MoveRotation(body.rotation * Quaternion.Euler(0f, steer * yawRate * dt, 0f));

            // Sideways velocity: kept through a swing so the car slides, shed over a moment after it, and
            // otherwise cancelled by the tyres' grip.
            float lateral = Vector3.Dot(body.linearVelocity, right);
            float keep = handbrake ? Mathf.Exp(-1.2f * dt) : Time.time < recoverUntil ? Mathf.Exp(-5f * dt) : grip >= 1f ? 0f : Mathf.Pow(1f - grip, dt * 10f);
            var slide = right * (lateral * keep);
            var velocity = onDeck
                ? along * forwardSpeed + slide - groundNormal * (Mathf.Max(ground.distance - 0.5f, 0f) * 10f)
                : forward * forwardSpeed + slide + Vector3.up * Mathf.Min(body.linearVelocity.y, 0f);
            if (hover) velocity.y = HoverVelocity(dt);
            body.linearVelocity = velocity;

            if (!Driven && Mathf.Abs(forwardSpeed) < 0.05f && (!hover || HoverAltitude < 0.2f)) body.isKinematic = true;
        }

        // The nearest static surface straight below (anything with a rigidbody, such as cars, or a
        // character is flown over), then a springy climb or sink toward the hover height with a slow bob.
        float HoverVelocity(float dt)
        {
            int n = Physics.RaycastNonAlloc(body.position + Vector3.up * 0.5f, Vector3.down, hoverHits, 40f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var hit = hoverHits[i];
                if (hit.collider.attachedRigidbody != null || hit.collider is CharacterController || hit.distance >= nearest) continue;
                nearest = hit.distance;
                HoverGround = hit.point;
            }
            HoverAltitude = body.position.y - HoverGround.y;
            float lift = Driven && !Grounding ? handling.HoverHeight + Mathf.Sin(Time.time * 2.1f) * 0.12f : 0f;
            float wanted = Mathf.Clamp((HoverGround.y + lift - body.position.y) * 3f, -5f, 5f);
            return Mathf.Lerp(body.linearVelocity.y, wanted, 1f - Mathf.Exp(-8f * dt));
        }

        void Update()
        {
            wheelSpin += forwardSpeed / 0.33f * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in wheels) w.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
            foreach (var s in steering) s.localRotation = Quaternion.Euler(0f, steer * 28f * (reversing ? -1f : 1f), 0f);
            if (visual != null)
            {
                var tilt = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(groundNormal));
                if (Hovers) tilt *= Quaternion.Euler(0f, 0f, -steer * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 15f) * 12f);
                visual.transform.localRotation = Quaternion.Slerp(visual.transform.localRotation, tilt, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
        }
    }
}
