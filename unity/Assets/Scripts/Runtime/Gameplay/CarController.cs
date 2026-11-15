using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Arcade car on a Rigidbody. The drag gives a world direction and a strength: the car steers
    // toward that direction, the strength sets the target speed, and dragging behind the car
    // backs it up. With no input it rolls to a stop.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        public string VehicleId;
        public string ModelId;
        public bool Driven { get; private set; }

        // False while the vehicle can't be taken, such as a delivery bike with its rider still on.
        public bool Enterable = true;
        public float Speed => forwardSpeed;
        public Vector3 HalfExtents { get; private set; }

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
            if (this.visual != null) Destroy(this.visual);
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

        void FixedUpdate()
        {
            if (body.isKinematic || Autopilot) return;
            float dt = Time.fixedDeltaTime;
            var forward = transform.forward;
            var right = transform.right;
            forwardSpeed = Vector3.Dot(body.linearVelocity, forward);

            float target = 0f;
            float steerAngle = 0f;
            bool braking = false;
            if (Driven && strength > 0f && desired.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(forward, desired, Vector3.up);
                bool pointingBack = Mathf.Abs(angle) > 125f;
                if (!reversing && pointingBack && forwardSpeed < 1.5f) reversing = true;
                else if (reversing && Mathf.Abs(angle) < 70f) reversing = false;
                if (reversing)
                {
                    target = -strength * handling.ReverseSpeed;
                    steerAngle = Vector3.SignedAngle(-forward, desired, Vector3.up);
                }
                else if (pointingBack)
                {
                    target = 0f;
                    braking = true;
                }
                else
                {
                    target = strength * handling.TopSpeed;
                    steerAngle = angle;
                }
            }

            bool opposing = Mathf.Sign(target) != Mathf.Sign(forwardSpeed) && Mathf.Abs(forwardSpeed) > 0.5f;
            float grip = Mathf.Clamp01(handling.Grip * RoadConditions.GripMultiplier);
            float rate = braking ? handling.Braking : target == 0f ? handling.Coasting : opposing ? handling.Braking : handling.Acceleration;
            rate *= Mathf.Lerp(0.5f, 1f, grip);
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, target, rate * dt);
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(steerAngle / 25f, -1f, 1f), 4f * dt);
            float yaw = steer * handling.TurnRate * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 3f) * dt;
            body.MoveRotation(body.rotation * Quaternion.Euler(0f, yaw, 0f));
            var slide = grip >= 1f ? Vector3.zero : right * (Vector3.Dot(body.linearVelocity, right) * Mathf.Pow(1f - grip, dt * 10f));
            body.linearVelocity = forward * forwardSpeed + slide + Vector3.up * Mathf.Min(body.linearVelocity.y, 0f);

            if (!Driven && Mathf.Abs(forwardSpeed) < 0.05f) body.isKinematic = true;
        }

        void Update()
        {
            wheelSpin += forwardSpeed / 0.33f * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in wheels) w.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
            foreach (var s in steering) s.localRotation = Quaternion.Euler(0f, steer * 28f * (reversing ? -1f : 1f), 0f);
        }
    }
}
