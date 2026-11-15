using Dogpatch.Audio;
using UnityEngine;

namespace Dogpatch.Feel
{
    // Trauma-based camera shake scaled by impacts, layered on the gameplay camera after it has
    // positioned itself (and after CROS-16's CameraFx, which owns the speed and boost FOV kick).
    // Adds an offset on top of whatever the follow camera wrote this frame and never accumulates.
    [DefaultExecutionOrder(10000)]
    public sealed class CameraFeel : MonoBehaviour
    {
        const float MaxOffset = 0.45f;
        const float MaxAngle = 3f;

        float trauma;
        float seed;
        Vector3 writtenPos;
        Vector3 lastOffset;
        Quaternion lastRot = Quaternion.identity;

        public static CameraFeel Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            seed = Random.value * 100f;
        }

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (transform.position == writtenPos) transform.SetPositionAndRotation(transform.position - transform.rotation * Quaternion.Inverse(lastRot) * lastOffset, transform.rotation * Quaternion.Inverse(lastRot));

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
