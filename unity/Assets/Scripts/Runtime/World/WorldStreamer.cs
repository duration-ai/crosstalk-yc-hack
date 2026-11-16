using UnityEngine;

namespace Dogpatch.World
{
    // Keeps the streamed detail chunks (StaticModels.Stream) around the main camera: a few chunks a
    // frame, nearest first, so driving never hitches on a burst of mesh builds. A camera that jumps
    // (a respawn, the race grid, a capture view) gets everything around it at once, so it never
    // shows extrusions where detail belongs.
    public sealed class WorldStreamer : MonoBehaviour
    {
        const int PerFrame = 2;
        const float Jump = 150f;

        Vector3 last;

        public StaticModels Statics;
        public DistanceLod Lod;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Statics == null) return;
            var eye = cam.transform.position;
            bool jumped = (eye - last).sqrMagnitude > Jump * Jump;
            last = eye;
            if (Statics.Stream(eye, jumped ? int.MaxValue : PerFrame) > 0) Lod.Apply(eye);
        }
    }
}
