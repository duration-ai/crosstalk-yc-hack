using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Third-person follow camera that swings in behind whatever it follows (the walker or the
    // car) and pulls in when a building would block the view.
    public sealed class FollowCamera : MonoBehaviour
    {
        public Transform Target;
        public bool Driving;
        public bool Hold;

        float yaw;
        float distance = 7f;
        readonly RaycastHit[] hits = new RaycastHit[16];

        public float Yaw => yaw;

        public void Snap(Transform target, bool driving)
        {
            Target = target;
            Driving = driving;
            yaw = target.eulerAngles.y;
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
            if (!Hold) yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-follow * dt));

            float wanted = Driving ? 10f : 6.5f;
            float pitch = Driving ? 16f : 18f;
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
