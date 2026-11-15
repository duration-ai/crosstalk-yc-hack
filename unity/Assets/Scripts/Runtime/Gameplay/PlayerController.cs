using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Third-person character on foot: moves toward the drag direction at a speed set by drag
    // length, eases to a stop on release, jumps on tap.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        const float RunSpeed = 7f;
        const float Acceleration = 18f;
        const float Gravity = -20f;
        const float JumpVelocity = 6.5f;

        CharacterController body;
        GlbAnimator animator;
        Vector3 desired;
        float strength;
        float speed;
        float verticalVelocity;

        public float Speed => speed;

        public static PlayerController Create(GlbAsset model, Material material)
        {
            var go = new GameObject("Player");
            var body = go.AddComponent<CharacterController>();
            body.height = 1.75f;
            body.radius = 0.35f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.stepOffset = 0.4f;
            body.slopeLimit = 50f;
            var player = go.AddComponent<PlayerController>();
            if (model != null)
            {
                var visual = model.Instantiate(material, go.transform);
                player.animator = visual.GetComponent<GlbAnimator>();
            }
            return player;
        }

        void Awake() => body = GetComponent<CharacterController>();

        public void SetInput(Vector3 direction, float amount)
        {
            desired = direction;
            strength = amount;
        }

        public void Jump()
        {
            if (body.isGrounded) verticalVelocity = JumpVelocity;
        }

        public void Teleport(Vector3 position, float heading)
        {
            body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            body.enabled = true;
            speed = 0f;
            verticalVelocity = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float target = desired.sqrMagnitude > 0.01f ? strength * RunSpeed : 0f;
            speed = Mathf.MoveTowards(speed, target, Acceleration * dt);
            if (target > 0f)
            {
                var look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * dt);
            }

            if (body.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += Gravity * dt;
            var motion = transform.forward * speed + Vector3.up * verticalVelocity;
            body.Move(motion * dt);

            if (animator == null) return;
            if (speed > 3.8f) animator.Play("Run");
            else if (speed > 0.25f) animator.Play("Walk");
            else animator.Play("Idle");
            animator.Speed = speed > 3.8f ? speed / 6f : speed > 0.25f ? Mathf.Max(speed / 1.6f, 0.6f) : 1f;
        }
    }
}
