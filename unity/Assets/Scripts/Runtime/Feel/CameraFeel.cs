using Dogpatch.Audio;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Feel
{
    // Layered on the gameplay camera after it has positioned itself: trauma-based shake scaled by
    // impacts, and an FOV kick at high speed and on boost. Adds offsets on top of whatever the
    // follow camera wrote this frame and never accumulates.
    [DefaultExecutionOrder(10000)]
    public sealed class CameraFeel : MonoBehaviour
    {
        const float MaxOffset = 0.45f;
        const float MaxAngle = 3f;

        Camera cam;
        FollowCamera follow;
        float trauma;
        float speedKick;
        float boostKick;
        float boostVelocity;
        float seed;
        float writtenFov = -1f;
        float baseFov;
        Vector3 writtenPos;
        Vector3 lastOffset;
        Quaternion lastRot = Quaternion.identity;

        public static CameraFeel Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            follow = GetComponent<FollowCamera>();
            seed = Random.value * 100f;
        }

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        public void Kick(float degrees) => boostVelocity += degrees * 12f;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (follow == null) follow = GetComponent<FollowCamera>();

            if (transform.position == writtenPos) transform.SetPositionAndRotation(transform.position - lastOffset, transform.rotation * Quaternion.Inverse(lastRot));
            baseFov = Mathf.Approximately(cam.fieldOfView, writtenFov) ? baseFov : cam.fieldOfView;

            float speed = 0f;
            var car = follow != null && follow.Driving && follow.Target != null ? follow.Target.GetComponent<CarController>() : null;
            if (car != null) speed = Mathf.Abs(car.Speed);
            speedKick = Mathf.Lerp(speedKick, Mathf.SmoothStep(0f, 8f, Mathf.InverseLerp(11f, 24f, speed)), 1f - Mathf.Exp(-3f * dt));
            float boostTarget = GameEvents.Boosting ? 10f : 0f;
            boostVelocity += ((boostTarget - boostKick) * 60f - boostVelocity * 11f) * dt;
            boostKick += boostVelocity * dt;
            cam.fieldOfView = baseFov + speedKick + Mathf.Max(boostKick, -2f);
            writtenFov = cam.fieldOfView;

            trauma = Mathf.MoveTowards(trauma, 0f, 1.5f * dt);
            float shake = SoundSettings.CameraShake ? trauma * trauma : 0f;
            float t = Time.unscaledTime * 24f;
            lastOffset = new Vector3(Noise(t, 0f), Noise(t, 1f), Noise(t, 2f)) * (MaxOffset * shake);
            lastRot = Quaternion.Euler(Noise(t, 3f) * MaxAngle * shake * 0.5f, Noise(t, 4f) * MaxAngle * shake * 0.5f, Noise(t, 5f) * MaxAngle * shake);
            transform.SetPositionAndRotation(transform.position + transform.rotation * lastOffset, transform.rotation * lastRot);
            writtenPos = transform.position;
        }

        float Noise(float t, float channel) => Mathf.PerlinNoise(seed + channel * 17.3f, t) * 2f - 1f;
    }
}
