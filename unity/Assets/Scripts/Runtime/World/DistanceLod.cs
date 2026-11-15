using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    // Distance culling per merged chunk mesh: detailed buildings swap to plain extrusions past
    // BuildingDetail metres, trees and props disappear past their own ranges. Checked a few times a
    // second against the main camera, which keeps an iPhone's triangle count bounded.
    public sealed class DistanceLod : MonoBehaviour
    {
        public float BuildingDetail = 420f;
        public float Trees = 380f;
        public float Props = 170f;

        readonly List<(StaticModels.Layer layer, Renderer renderer, Bounds bounds)> entries = new List<(StaticModels.Layer, Renderer, Bounds)>();
        float next;

        public void Add(StaticModels.Layer layer, Renderer renderer) => entries.Add((layer, renderer, renderer.bounds));

        public void Apply(Vector3 eye)
        {
            foreach (var (layer, renderer, bounds) in entries)
            {
                float d = Mathf.Sqrt(bounds.SqrDistance(eye));
                renderer.enabled = layer switch
                {
                    StaticModels.Layer.Buildings => d < BuildingDetail,
                    StaticModels.Layer.BuildingsFar => d >= BuildingDetail,
                    StaticModels.Layer.Trees => d < Trees,
                    StaticModels.Layer.Props => d < Props,
                    _ => true,
                };
            }
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.2f;
            var cam = Camera.main;
            if (cam != null) Apply(cam.transform.position);
        }
    }
}
