using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Look
{
    // Camera juice on the car camera: the field of view widens with speed, holds wider while
    // boosting and punches out on the boost itself, and the camera banks a few degrees into turns.
    // Runs after FollowCamera, which rewrites the base field of view and rotation every frame, so
    // this only ever adds on top; it does nothing while the follow camera is not driving it (stills,
    // cutscenes), so nothing accumulates. Shake comes from Feel.CameraFeel, which runs later.
    // Tunables: MotionTuning.camera.
    [DefaultExecutionOrder(1100)]
    public sealed class CameraFx : MonoBehaviour
    {
        Camera view;
        FollowCamera follow;
        float widen, lean, punch;

        void Awake()
        {
            view = GetComponent<Camera>();
            MotionFx.Boosted += OnBoosted;
        }

        void OnDestroy() => MotionFx.Boosted -= OnBoosted;

        void OnBoosted(float strength) => punch = Mathf.Max(punch, strength);

        void LateUpdate()
        {
            if (follow == null) follow = GetComponent<FollowCamera>();
            if (follow == null || !follow.enabled || follow.Target == null) return;
            var c = MotionTuning.Current.camera;
            float dt = Time.deltaTime;
            punch = Mathf.MoveTowards(punch, 0f, dt / Mathf.Max(c.punchSeconds, 0.05f));

            float target = c.fovSpeed * Mathf.Pow(MotionFx.SpeedFactor, c.fovSpeedPower) + c.fovBoost * MotionFx.BoostFactor;
            widen = Mathf.Lerp(widen, target, 1f - Mathf.Exp(-c.fovResponse * dt));
            view.fieldOfView += widen + c.fovPunch * punch * punch;

            float bank = follow.Driving ? Mathf.Clamp(MotionFx.Lateral * c.leanPerLateral, -c.leanMax, c.leanMax) : 0f;
            lean = Mathf.Lerp(lean, bank, 1f - Mathf.Exp(-c.leanResponse * dt));
            if (Mathf.Abs(lean) > 0.01f) transform.rotation *= Quaternion.Euler(0f, 0f, -lean);
        }
    }
}
