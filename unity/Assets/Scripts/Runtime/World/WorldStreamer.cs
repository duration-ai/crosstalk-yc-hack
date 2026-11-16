using UnityEngine;

namespace Dogpatch.World
{
    // Keeps the streamed detail chunks (StaticModels.Stream) around the main camera: a few chunks a
    // frame, nearest first, so driving never hitches on a burst of mesh builds.
    public sealed class WorldStreamer : MonoBehaviour
    {
        const int PerFrame = 2;

        public StaticModels Statics;
        public DistanceLod Lod;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Statics == null) return;
            if (Statics.Stream(cam.transform.position, PerFrame) > 0) Lod.Apply(cam.transform.position);
        }
    }
}
