using System;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Third-person character on foot: moves toward the drag direction at a speed set by drag
    // length, eases to a stop on release, jumps on tap.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        const float RunSpeed = 8.6f;
        const float Acceleration = 22f;
        const float Gravity = -20f;
        const float JumpVelocity = 7f;

        public static float RunScale = 1f;
        public static float JumpScale = 1f;
        public static float GravityScale = 1f;

        // Wading through a flood on foot. It never slows a body in the air on flight or slow_fall, or a
        // giant (scaled GiantScale or more), who stands above the water.
        public static float WadeScale = 1f;
        public const float GiantScale = 1.5f;
        public bool Flying;

        CharacterController body;
        GlbAnimator animator;
        Vector3 desired;
        float strength;
        float speed;
        float verticalVelocity;
        float airTime;
        Transform visual;

        public float Speed => speed;
        public float VerticalVelocity { get => verticalVelocity; set => verticalVelocity = value; }
        public Transform Visual => visual;
        public event Action Jumped;
        public event Action<float> Landed;

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
            player.SetVisual(model, material);
            return player;
        }

        // Swaps the character model, e.g. for the player's selfie avatar; movement is untouched.
        public void SetVisual(GlbAsset model, Material material)
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = null;
            animator = null;
            if (model == null) return;
            var instance = model.Instantiate(material, transform);
            visual = instance.transform;
            animator = instance.GetComponent<GlbAnimator>();
        }

        void Awake() => body = GetComponent<CharacterController>();

        public void SetInput(Vector3 direction, float amount)
        {
            desired = direction;
            strength = amount;
        }

        public void Jump()
        {
            if (!body.isGrounded) return;
            verticalVelocity = JumpVelocity * JumpScale;
            Jumped?.Invoke();
        }

        // Every placement (spawn, respawn, getting out of a car, race lobby and grid) lands standing on
        // the walkable surface under the requested spot: the highest upward-facing solid no more than
        // StepUp above it (a sidewalk, a station platform, a freeway deck), with the capsule bottom a
        // skin above it. With nothing below, the spot is used as given.
        public void Teleport(Vector3 position, float heading)
        {
            const float StepUp = 1.5f;
            const float Skin = 0.02f;
            Physics.SyncTransforms();
            float ground = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * StepUp, Vector3.down, StepUp + 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < 0.6f || hit.collider == body || hit.collider.attachedRigidbody != null) continue;
                ground = Mathf.Max(ground, hit.point.y);
            }
            if (!float.IsNegativeInfinity(ground)) position.y = ground + Skin - (body.center.y - body.height * 0.5f);
            body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            body.enabled = true;
            speed = 0f;
            verticalVelocity = 0f;
        }

        // Hands back control mid-stride, e.g. coming up running out of a bail-out roll.
        public void CarryOn(float runSpeed) => speed = runSpeed;

        void Update()
        {
            float dt = Time.deltaTime;
            bool aboveWater = Flying || (GravityScale < 1f && !body.isGrounded) || transform.localScale.y >= GiantScale;
            float run = RunScale * (aboveWater ? 1f : WadeScale);
            float target = desired.sqrMagnitude > 0.01f ? strength * RunSpeed * run : 0f;
            speed = Mathf.MoveTowards(speed, target, Acceleration * run * dt);
            if (target > 0f)
            {
                var look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * dt);
            }

            if (body.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += Gravity * GravityScale * dt;
            float falling = verticalVelocity;
            var motion = transform.forward * speed + Vector3.up * verticalVelocity;
            body.Move(motion * dt);
            if (body.isGrounded && airTime > 0.15f) Landed?.Invoke(-falling);

            bool airborne = !body.isGrounded && verticalVelocity > -6f && airTime > 0.08f;
            airTime = body.isGrounded ? 0f : airTime + dt;
            if (visual != null)
            {
                float stretch = airborne ? 1f + Mathf.Clamp(verticalVelocity, -6f, 6f) * 0.012f : 1f;
                visual.localScale = Vector3.Lerp(visual.localScale, new Vector3(2f - stretch, stretch, 2f - stretch), 1f - Mathf.Exp(-20f * dt));
            }

            if (animator == null) return;
            if (airborne)
            {
                // Polyfork characters ship Idle/Walk/Run only; without a Jump clip, a held mid-stride
                // Run pose plus the stretch above reads as a leap.
                if (animator.Has("Jump"))
                {
                    animator.Play("Jump");
                    animator.Speed = 1f;
                }
                else
                {
                    animator.Play("Run");
                    animator.Speed = 0.15f;
                }
                return;
            }
            float stride = speed / Mathf.Max(transform.lossyScale.y, 0.1f);
            if (stride > 3.8f) animator.Play("Run");
            else if (stride > 0.25f) animator.Play("Walk");
            else animator.Play("Idle");
            animator.Speed = stride > 3.8f ? stride / 6f : stride > 0.25f ? Mathf.Max(stride / 1.6f, 0.6f) : 1f;
        }
    }
}
