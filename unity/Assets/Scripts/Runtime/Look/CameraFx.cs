using UnityEngine;

namespace Dogpatch.Look
{
    // Widens the field of view with speed and kicks it on a boost. Runs after FollowCamera, which
    // sets the base field of view every frame, so this only ever adds on top.
    [DefaultExecutionOrder(1100)]
    public sealed class CameraFx : MonoBehaviour
    {
        const float SpeedWiden = 13f;
        const float BoostWiden = 9f;

        Camera view;
        float widen;

        void Awake() => view = GetComponent<Camera>();

        void LateUpdate()
        {
            float target = SpeedWiden * Mathf.Pow(MotionFx.SpeedFactor, 1.3f) + BoostWiden * MotionFx.BoostFactor;
            widen = Mathf.Lerp(widen, target, 1f - Mathf.Exp(-5f * Time.deltaTime));
            view.fieldOfView += widen;
        }
    }
}
