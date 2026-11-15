using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Getting out of a car that is still moving: the player leaps out of the door side carrying the
    // car's momentum, tuck-rolls along the road (one roll per 5 m/s or so, up to three) and comes up
    // running. Cartoon only, no damage. PlayerController is paused while it plays and gets the
    // remaining speed back at the end.
    [DefaultExecutionOrder(50)]
    public sealed class BailOut : MonoBehaviour
    {
        public const float MinSpeed = 3f;
        const float Gravity = 20f;
        const float LeapTime = 0.28f;
        const float RollTime = 0.4f;
        const float Pivot = 0.55f;

        static readonly (string bone, Quaternion left)[] Tuck =
        {
            ("UpLeg", Quaternion.Euler(-125f, 0f, -10f)),
            ("Leg", Quaternion.Euler(145f, 0f, 0f)),
            ("Arm", Quaternion.AngleAxis(50f, Vector3.right) * Quaternion.AngleAxis(90f, Vector3.up)),
            ("ForeArm", Quaternion.AngleAxis(80f, Vector3.up)),
        };
        static readonly (string bone, Quaternion rotation)[] Curl =
        {
            ("Spine", Quaternion.Euler(25f, 0f, 0f)),
            ("Spine1", Quaternion.Euler(25f, 0f, 0f)),
            ("Spine2", Quaternion.Euler(15f, 0f, 0f)),
            ("Neck", Quaternion.Euler(25f, 0f, 0f)),
        };

        PlayerController player;
        CharacterController body;
        Transform visual;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector3 velocity;
        float climb;
        float t;
        float length;
        int rolls;
        readonly Transform[] bones = new Transform[12];
        readonly Quaternion[] targets = new Quaternion[12];

        // carVelocity is the car's velocity as the player lets go; side points out of the door.
        public static void Begin(PlayerController player, Vector3 carVelocity, Vector3 side)
        {
            carVelocity.y = 0f;
            float speed = carVelocity.magnitude;
            if (speed < MinSpeed) return;
            if (!player.TryGetComponent<BailOut>(out var bail)) bail = player.gameObject.AddComponent<BailOut>();
            bail.Play(player, carVelocity * 0.8f + side.normalized * 3f, Mathf.Clamp(Mathf.RoundToInt(speed / 5f), 1, 3));
        }

        void Play(PlayerController owner, Vector3 launch, int count)
        {
            player = owner;
            body = owner.GetComponent<CharacterController>();
            visual = owner.Visual;
            if (visual != null)
            {
                restPosition = visual.localPosition;
                restRotation = visual.localRotation;
            }
            velocity = launch;
            climb = 3.2f;
            rolls = count;
            t = 0f;
            length = LeapTime + rolls * RollTime + 0.12f;
            owner.enabled = false;
            transform.rotation = Quaternion.LookRotation(new Vector3(launch.x, 0f, launch.z), Vector3.up);
            FindBones();
            enabled = true;
        }

        void FindBones()
        {
            System.Array.Clear(bones, 0, bones.Length);
            if (visual == null) return;
            int i = 0;
            foreach (var (bone, left) in Tuck)
            {
                bones[i] = Find("Left" + bone);
                targets[i++] = left;
                bones[i] = Find("Right" + bone);
                targets[i++] = new Quaternion(left.x, -left.y, -left.z, left.w);
            }
            foreach (var (bone, rotation) in Curl)
            {
                bones[i] = Find(bone);
                targets[i++] = rotation;
            }
        }

        Transform Find(string name)
        {
            foreach (var child in visual.GetComponentsInChildren<Transform>())
                if (child.name == name || child.name.EndsWith(":" + name)) return child;
            return null;
        }

        void Update()
        {
            if (player.enabled)
            {
                enabled = false;
                return;
            }
            float dt = Time.deltaTime;
            t += dt;
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            if (t > LeapTime) flat = Vector3.MoveTowards(flat, flat.normalized * Mathf.Min(flat.magnitude, 7f), 14f * dt);
            velocity = new Vector3(flat.x, 0f, flat.z);
            if (body.isGrounded && climb < 0f) climb = -2f;
            climb -= Gravity * dt;
            body.Move((velocity + Vector3.up * climb) * dt);

            if (visual != null)
            {
                float angle = t < LeapTime ? Mathf.Lerp(0f, 40f, t / LeapTime) : 40f + (360f * rolls - 40f) * Mathf.Clamp01((t - LeapTime) / (rolls * RollTime));
                var spin = Quaternion.AngleAxis(angle, Vector3.right);
                var pivot = Vector3.up * Pivot;
                visual.localRotation = spin * restRotation;
                visual.localPosition = restPosition + pivot - spin * pivot;
            }
            if (t < length) return;
            Finish();
        }

        void LateUpdate()
        {
            float w = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((length - t) / 0.18f);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, targets[i], w);
        }

        void Finish()
        {
            Restore();
            player.CarryOn(Mathf.Min(velocity.magnitude, 7f));
            enabled = false;
        }

        void Restore()
        {
            if (visual != null)
            {
                visual.localRotation = restRotation;
                visual.localPosition = restPosition;
            }
            player.enabled = true;
        }

        // Getting back into a car mid-roll deactivates the player; stand them up straight.
        void OnDisable()
        {
            if (player != null && !player.enabled) Restore();
        }
    }
}
