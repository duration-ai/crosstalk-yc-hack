using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // A bot racer on foot while the room waits on the start ring: stands on its slot in its player
    // color, stumbles when shoved, then walks back.
    public sealed class RacerAvatar : MonoBehaviour, IShoveable
    {
        public Vector3 Home;
        public float HomeHeading;

        GlbAnimator animator;
        Vector3 velocity;
        float hop;
        float hopVelocity;
        float stumbleUntil;
        bool tracked;

        public Vector3 Position => transform.position;
        public bool CanBeShoved => gameObject.activeInHierarchy && Time.time > stumbleUntil;

        public static RacerAvatar Create(GlbAsset model, Material material, Color linearColor, string name)
        {
            var go = model.Instantiate(material);
            go.name = "Racer " + name;
            PaintJob.For(go, model.Id).SetBase(linearColor);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = 0.35f;
            cap.height = 1.75f;
            cap.center = new Vector3(0f, 0.88f, 0f);
            var avatar = go.AddComponent<RacerAvatar>();
            avatar.animator = go.GetComponent<GlbAnimator>();
            return avatar;
        }

        public void Stand(Vector3 home, float heading)
        {
            Home = home;
            HomeHeading = heading;
            transform.SetPositionAndRotation(home, Quaternion.Euler(0f, heading, 0f));
            velocity = Vector3.zero;
            hop = 0f;
        }

        // Follows a pose from outside (a remote player), playing Idle, Walk or Run by speed.
        public void Track(Vector3 position, float heading, float speed)
        {
            tracked = true;
            if (Time.time < stumbleUntil) return;
            Home = position;
            HomeHeading = heading;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            animator?.Play(speed > 3.5f ? "Run" : speed > 0.4f ? "Walk" : "Idle");
        }

        public void Shove(Vector3 impulse, GameObject by)
        {
            velocity = new Vector3(impulse.x, 0f, impulse.z);
            hopVelocity = 3.2f;
            stumbleUntil = Time.time + 0.9f;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(-velocity.normalized, Vector3.up);
            Debug.Log($"[SpeedDial] {name} shoved by {(by != null ? by.name : "someone")}");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var pos = transform.position;
            if (hop > 0f || hopVelocity > 0f)
            {
                hopVelocity -= 14f * dt;
                hop = Mathf.Max(0f, hop + hopVelocity * dt);
                if (hop == 0f) hopVelocity = 0f;
            }
            if (Time.time < stumbleUntil)
            {
                pos += velocity * dt;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 9f * dt);
                animator?.Play("Run");
            }
            else if (tracked) return;
            else
            {
                var flat = new Vector3(Home.x - pos.x, 0f, Home.z - pos.z);
                if (flat.magnitude > 0.15f)
                {
                    pos += flat.normalized * Mathf.Min(2.2f * dt, flat.magnitude);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat, Vector3.up), 400f * dt);
                    animator?.Play("Walk");
                }
                else
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, HomeHeading, 0f), 300f * dt);
                    animator?.Play("Idle");
                }
            }
            pos.y = Home.y + hop;
            transform.position = pos;
        }
    }

    // Lets swipes and bots shove the local player while on foot.
    public sealed class PlayerShoveable : MonoBehaviour, IShoveable
    {
        CharacterController body;
        Vector3 velocity;
        float until;

        public Vector3 Position => transform.position;
        public bool CanBeShoved => isActiveAndEnabled && Time.time > until;

        void Awake() => body = GetComponent<CharacterController>();

        public void Shove(Vector3 impulse, GameObject by)
        {
            velocity = new Vector3(impulse.x, 0f, impulse.z);
            until = Time.time + 0.4f;
            Debug.Log($"[SpeedDial] you got shoved by {(by != null ? by.name : "someone")}");
        }

        void Update()
        {
            if (velocity.sqrMagnitude < 0.01f || body == null || !body.enabled) return;
            body.Move(velocity * Time.deltaTime);
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, 12f * Time.deltaTime);
        }
    }
}
