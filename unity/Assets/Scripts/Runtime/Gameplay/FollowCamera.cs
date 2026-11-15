using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Third-person follow camera that pulls in when a building would block the view. Behind the
    // car it swings in behind the heading. On foot it trails the walker like a lazy rope: it
    // turns only as the walker moves sideways across the view, never swings round when they
    // run toward it, and never recentres on its own, so a held direction keeps its meaning. A
    // right-side drag orbits it on foot (Orbit).
    public sealed class FollowCamera : MonoBehaviour
    {
        public Transform Target;
        public bool Driving;

        const float LazyFollow = 0.5f;
        const float FootPitch = 18f;

        float yaw;
        float footPitch = FootPitch;
        float orbitYaw;
        float distance = 7f;
        readonly RaycastHit[] hits = new RaycastHit[16];

        public float Yaw => yaw;

        public void Snap(Transform target, bool driving)
        {
            Target = target;
            Driving = driving;
            yaw = target.eulerAngles.y;
            footPitch = FootPitch;
            transform.position = target.position - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
        }

        // Screen-space drag in pixels: a drag across the full screen width turns 200 degrees, and
        // dragging up looks up.
        public void Orbit(Vector2 pixels)
        {
            float k = 200f / Mathf.Max(Screen.width, 1);
            orbitYaw += pixels.x * k;
            footPitch = Mathf.Clamp(footPitch - pixels.y * k, 4f, 55f);
        }

        void LateUpdate()
        {
            if (Target == null) return;
            var cam = GetComponent<Camera>();
            cam.fieldOfView = cam.aspect < 1f ? 72f : 55f;

            float dt = Time.deltaTime;
            float targetYaw = Target.eulerAngles.y;
            float follow = Driving ? 3.5f : 2.2f;
            var car = Driving ? Target.GetComponent<CarController>() : null;
            if (car != null && car.Speed < -0.5f) follow = 0.6f;
            if (Driving)
            {
                yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-follow * dt));
            }
            else
            {
                var toTarget = Target.position - transform.position;
                if (toTarget.x * toTarget.x + toTarget.z * toTarget.z > 0.01f)
                    yaw += Mathf.DeltaAngle(yaw, Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg) * LazyFollow;
                yaw += orbitYaw;
            }
            orbitYaw = 0f;

            float wanted = Driving ? 10f : 6.5f;
            float pitch = Driving ? 16f : footPitch;
            var pivot = Target.position + Vector3.up * (Driving ? 1.6f : 1.5f);
            var dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;

            float allowed = wanted;
            int count = Physics.SphereCastNonAlloc(pivot, 0.35f, dir, hits, wanted, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(Target) || hits[i].distance <= 0f) continue;
                if (hits[i].collider.GetComponentInParent<CarController>() != null) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(hits[i].distance - 0.2f, 1.5f));
            }
            distance = allowed < distance ? allowed : Mathf.Lerp(distance, allowed, 1f - Mathf.Exp(-2f * dt));

            transform.position = pivot + dir * distance;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.3f - transform.position, Vector3.up);
        }
    }
}
