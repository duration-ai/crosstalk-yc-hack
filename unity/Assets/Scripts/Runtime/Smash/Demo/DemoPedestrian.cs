using Dogpatch.Ambient;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Smash.Demo
{
    // A stand-in pedestrian for the smash demo only, until CROS-20's pedestrians land: a cartoon
    // dive out of the way on a threat, a tumble and get-up on a hit, and a hop out of a wreck.
    public sealed class DemoPedestrian : MonoBehaviour, IVehicleReactive
    {
        enum State { Idle, Diving, Down, GettingUp, Tumbling }

        State state;
        float t0;
        Vector3 from, to, velocity, spin;
        float yaw;
        GlbAnimator animator;

        void Awake()
        {
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = 0.3f;
            capsule.height = 1.75f;
            capsule.center = new Vector3(0f, 0.875f, 0f);
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            animator = GetComponent<GlbAnimator>();
        }

        public void OnVehicleThreat(GameObject vehicle, Vector3 vehicleVelocity, float timeToImpact)
        {
            if (state != State.Idle || timeToImpact > 0.8f) return;
            var dir = vehicleVelocity.normalized;
            var right = Vector3.Cross(Vector3.up, dir);
            float lateral = Vector3.Dot(transform.position - vehicle.transform.position, right);
            var away = right * (lateral >= 0f ? 1f : -1f);
            Begin(State.Diving, transform.position + away * 3.4f, Quaternion.LookRotation(away).eulerAngles.y);
            animator?.Play("Run");
        }

        public void OnVehicleImpact(GameObject vehicle, Vector3 point, Vector3 relativeVelocity)
        {
            if (state == State.Tumbling) return;
            Begin(State.Tumbling, transform.position, transform.eulerAngles.y);
            velocity = relativeVelocity * 0.45f + Vector3.up * 5f;
            spin = Random.onUnitSphere * 540f;
        }

        public void HopOut(Vector3 direction)
        {
            Begin(State.Diving, transform.position + direction.normalized * 2.4f, Quaternion.LookRotation(direction).eulerAngles.y);
            from += Vector3.up * 0.6f;
            animator?.Play("Run");
        }

        void Begin(State next, Vector3 target, float heading)
        {
            state = next;
            t0 = Time.time;
            from = transform.position;
            to = target;
            yaw = heading;
        }

        void Update()
        {
            float t = Time.time - t0;
            switch (state)
            {
                case State.Diving:
                {
                    float k = Mathf.Clamp01(t / 0.55f);
                    float ease = 1f - (1f - k) * (1f - k);
                    transform.position = Vector3.Lerp(from, to, ease) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.8f;
                    transform.rotation = Quaternion.Euler(Mathf.Lerp(0f, 82f, Mathf.Clamp01(k * 1.6f)), yaw, 0f);
                    if (k >= 1f) Begin(State.Down, to, yaw);
                    break;
                }
                case State.Down:
                    transform.rotation = Quaternion.Euler(82f, yaw, Mathf.Sin(Mathf.Min(t, 0.5f) / 0.5f * Mathf.PI) * 25f);
                    if (t > 1.1f) Begin(State.GettingUp, to, yaw);
                    break;
                case State.GettingUp:
                {
                    float k = Mathf.Clamp01(t / 0.45f);
                    transform.rotation = Quaternion.Euler(Mathf.Lerp(82f, 0f, k), yaw, 0f);
                    transform.position = to + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                    if (k >= 1f)
                    {
                        state = State.Idle;
                        animator?.Play("Idle");
                    }
                    break;
                }
                case State.Tumbling:
                {
                    velocity += Physics.gravity * Time.deltaTime;
                    var p = transform.position + velocity * Time.deltaTime;
                    if (p.y < 0.03f)
                    {
                        p.y = 0.03f;
                        velocity = new Vector3(velocity.x * 0.5f, -velocity.y * 0.35f, velocity.z * 0.5f);
                        spin *= 0.5f;
                    }
                    transform.position = p;
                    transform.Rotate(spin * Time.deltaTime, Space.World);
                    if (t > 1.6f)
                    {
                        to = new Vector3(p.x, 0.03f, p.z);
                        Begin(State.GettingUp, to, transform.eulerAngles.y);
                    }
                    break;
                }
            }
        }
    }
}
