using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Third-person follow camera that pulls in when a building would block the view. Behind the
    // car it swings in behind the heading, at the same rate in reverse so it stays behind the
    // nose, low and close like the style art (ref 01), set back further for long vehicles. On foot it trails the walker like a lazy rope: it turns only as the walker moves
    // sideways across the view, never swings round when they run toward it, and never recentres
    // on its own, so a held direction keeps its meaning. A drag off the stick orbits it on foot
    // and looks around in a car, where it swings back behind the car shortly after you let go
    // (Orbit).
    public sealed class FollowCamera : MonoBehaviour
    {
        public Transform Target;
        public bool Driving;

        const float LazyFollow = 0.5f;
        const float FootPitch = 18f;

        float yaw;
        float footPitch = FootPitch;
        float orbitYaw;
        float driveLook;
        float lookedAt = -10f;
        float distance = 7f;
        readonly RaycastHit[] hits = new RaycastHit[16];

        public float Yaw => yaw;

        // Drag turn queued since the camera last ran; it builds up while the camera is switched off.
        public float PendingOrbit => orbitYaw;

        public void Snap(Transform target, bool driving)
        {
            Target = target;
            Driving = driving;
            yaw = target.eulerAngles.y;
            footPitch = FootPitch;
            driveLook = 0f;
            transform.position = target.position - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
        }

        // Screen-space drag in pixels: a drag across the full screen width turns 200 degrees, and
        // dragging up looks up.
        public void Orbit(Vector2 pixels)
        {
            float k = 200f / Mathf.Max(Screen.width, 1);
            if (pixels == Vector2.zero) return;
            if (Driving)
            {
                driveLook = Mathf.Clamp(driveLook + pixels.x * k, -160f, 160f);
                lookedAt = Time.unscaledTime;
                return;
            }
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
            if (Driving)
            {
                yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-follow * dt));
                if (Time.unscaledTime - lookedAt > 0.8f) driveLook = Mathf.Lerp(driveLook, 0f, 1f - Mathf.Exp(-3f * dt));
            }
            else
            {
                var toTarget = Target.position - transform.position;
                if (toTarget.x * toTarget.x + toTarget.z * toTarget.z > 0.01f)
                    yaw += Mathf.DeltaAngle(yaw, Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg) * LazyFollow;
                yaw += orbitYaw;
            }
            orbitYaw = 0f;

            float grown = Target.lossyScale.y;
            var size = Driving && Target.TryGetComponent<CarController>(out var car) ? car.HalfExtents : new Vector3(0.9f, 0.75f, 2.3f);
            bool portrait = cam.aspect < 1f;
            float wanted = (Driving ? Mathf.Max(5.8f, size.z + 3.6f) * (portrait ? 1.6f : 1f) : 6.5f) * grown;
            float pitch = Driving ? (portrait ? 12f : 8f) : footPitch;
            var pivot = Target.position + Vector3.up * ((Driving ? Mathf.Max(1.4f, size.y * 1.5f) : 1.5f) * grown);
            var dir = Quaternion.Euler(pitch, yaw + (Driving ? driveLook : 0f), 0f) * Vector3.back;

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
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * ((Driving ? 0.5f : 0.3f) * grown) - transform.position, Vector3.up);
        }
    }
}
