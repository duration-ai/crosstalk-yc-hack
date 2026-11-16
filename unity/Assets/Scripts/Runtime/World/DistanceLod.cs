using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    // Distance culling per merged chunk mesh: detailed buildings swap to plain extrusions past
    // BuildingDetail metres and hide past BuildingFar (deep in the fog), trees and props disappear past
    // their own ranges. Checked a few times a
    // second against the main camera. A building's detail and its extrusion share one chunk, and
    // both layers of a chunk take one decision from their combined bounds, so exactly one shows.
    public sealed class DistanceLod : MonoBehaviour
    {
        public float BuildingDetail = 240f;
        public float BuildingFar = 1500f;
        public float Trees = 500f;
        public float Props = 150f;

        readonly List<(StaticModels.Layer layer, Vector2Int chunk, Renderer renderer, Bounds bounds)> entries = new List<(StaticModels.Layer, Vector2Int, Renderer, Bounds)>();
        readonly Dictionary<Vector2Int, Bounds> buildingChunks = new Dictionary<Vector2Int, Bounds>();
        float next;

        public void Add(StaticModels.Layer layer, Vector2Int chunk, Renderer renderer)
        {
            var bounds = renderer.bounds;
            entries.Add((layer, chunk, renderer, bounds));
            if (layer != StaticModels.Layer.Buildings && layer != StaticModels.Layer.BuildingsFar) return;
            if (buildingChunks.TryGetValue(chunk, out var shared)) shared.Encapsulate(bounds);
            else shared = bounds;
            buildingChunks[chunk] = shared;
        }

        public void Apply(Vector3 eye)
        {
            foreach (var (layer, chunk, renderer, bounds) in entries)
            {
                float d = Mathf.Sqrt(bounds.SqrDistance(eye));
                bool detail = layer == StaticModels.Layer.Buildings || layer == StaticModels.Layer.BuildingsFar
                    ? Mathf.Sqrt(buildingChunks[chunk].SqrDistance(eye)) < BuildingDetail
                    : false;
                renderer.enabled = layer switch
                {
                    StaticModels.Layer.Buildings => detail,
                    StaticModels.Layer.BuildingsFar => !detail && d < BuildingFar,
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
