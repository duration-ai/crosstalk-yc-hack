using UnityEngine;

namespace Dogpatch.Traffic
{
    // Hides a standing vehicle's renderers past Range from the camera. A docked e-bike is eight
    // renderers and the docks are in view from across the map, so dozens of them cost hundreds of draw
    // calls for specks. Only renderers that were on at start are toggled.
    public sealed class FarCull : MonoBehaviour
    {
        public float Range = 200f;

        Renderer[] renderers;
        bool shown = true;
        float next;

        void Start()
        {
            renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.enabled);
            next = Random.value * 0.25f;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam == null) return;
            bool show = (cam.transform.position - transform.position).sqrMagnitude < Range * Range;
            if (show == shown) return;
            shown = show;
            foreach (var r in renderers) if (r != null) r.enabled = show;
        }
    }
}
