using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // World-change physics layered on any car without touching CarController: it runs after the
    // car's own FixedUpdate and adjusts the velocity the car just set. Airtime (ramps, low gravity),
    // ice (the velocity lags behind the heading so the car slides), shoves, size and the floating
    // of parked cars under low gravity.
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(CarController))]
    public sealed class CarFx : MonoBehaviour
    {
        public static float Traction = 1f;
        public static float SizeTarget = 1f;
        public static float Float;

        public float Grip = 1f;
        public Vector3 Wind;

        Rigidbody body;
        float vy;
        bool airborne;
        float airTime;
        Vector3 push;
        Vector3 lastPlanar;
        float size = 1f;
        float hover;
        float seed;
        float restY;

        public static CarFx On(CarController car)
        {
            var fx = car.GetComponent<CarFx>();
            return fx != null ? fx : car.gameObject.AddComponent<CarFx>();
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            seed = Random.value * 10f;
        }

        public void Launch(float up)
        {
            if (body.isKinematic) return;
            vy = Mathf.Max(vy, up);
            airborne = true;
            airTime = 0f;
        }

        public void Push(Vector3 velocity)
        {
            push += new Vector3(velocity.x, 0f, velocity.z);
            if (velocity.y > 0f) Launch(velocity.y);
        }

        bool Grounded() => Physics.Raycast(transform.position + Vector3.up * 0.4f, Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore);

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (body.isKinematic)
            {
                push = Vector3.zero;
                lastPlanar = Vector3.zero;
                airborne = false;
                return;
            }
            var v = body.linearVelocity;
            var planar = new Vector3(v.x, 0f, v.z);
            float traction = Traction * Grip;
            if (traction < 0.999f) planar = Vector3.Lerp(lastPlanar, planar, traction);
            planar += push + Wind;
            push = Vector3.MoveTowards(push, Vector3.zero, 14f * dt);

            float y = v.y;
            if (airborne)
            {
                airTime += dt;
                vy += Physics.gravity.y * dt;
                y = vy;
                if (vy <= 0f && airTime > 0.15f && Grounded()) airborne = false;
            }
            body.linearVelocity = planar + Vector3.up * y;
            lastPlanar = planar;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Mathf.Approximately(size, SizeTarget))
            {
                size = Mathf.MoveTowards(size, SizeTarget, 2.5f * dt);
                transform.localScale = Vector3.one * size;
            }

            if (body.isKinematic)
            {
                if (Float > 0f && hover == 0f) restY = transform.position.y;
                float target = Float * (2.2f + Mathf.Sin(Time.time * 0.9f + seed) * 0.8f + seed * 0.12f);
                float before = hover;
                hover = Mathf.MoveTowards(hover, target, 3f * dt);
                if (hover != before || hover > 0f)
                {
                    var p = transform.position;
                    p.y = restY + hover;
                    transform.position = p;
                    if (hover > 0.01f) transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.7f + seed) * 9f * Float, transform.eulerAngles.y, Mathf.Cos(Time.time * 0.6f + seed) * 7f * Float);
                    else transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                }
            }
            else if (hover > 0f)
            {
                hover = 0f;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
        }
    }
}
