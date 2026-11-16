using System.Collections.Generic;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // A full-side AI-startup ad wrap on a Muni bus, like the vinyl wraps that cover Muni's buses
    // window line and all: a panel on each flank carrying one of the parody ads, a new ad per bus.
    public static class AdWrap
    {
        static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        static int next;

        public static void Add(GameObject visual, Material material, Bounds bounds)
        {
            int index = next++ % LandmarkMeshes.Ads.Length;
            if (!meshes.TryGetValue(index, out var mesh)) meshes[index] = mesh = Build(bounds, LandmarkMeshes.Ads[(index * 3 + 1) % LandmarkMeshes.Ads.Length]);
            var go = new GameObject("ad-wrap");
            go.transform.SetParent(visual.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Mesh Build(Bounds bounds, AdCopy ad)
        {
            var mb = new MeshBuilder();
            bool alongZ = bounds.size.z >= bounds.size.x;
            float length = alongZ ? bounds.size.z : bounds.size.x;
            float half = (alongZ ? bounds.extents.x : bounds.extents.z) + 0.03f;
            float y0 = bounds.min.y + bounds.size.y * 0.34f;
            float y1 = bounds.min.y + bounds.size.y * 0.9f;
            float a0 = -length * 0.5f + length * 0.07f;
            float a1 = length * 0.5f - length * 0.1f;
            foreach (float side in new[] { 1f, -1f })
            {
                var normal = alongZ ? new Vector3(side, 0f, 0f) : new Vector3(0f, 0f, side);
                var right = new Vector3(-normal.z, 0f, normal.x);
                var along = alongZ ? Vector3.forward : Vector3.right;
                var centre = bounds.center + normal * half + along * ((a0 + a1) * 0.5f);
                centre.y = (y0 + y1) * 0.5f;
                LandmarkMeshes.AdFace(mb, centre, right, normal, a1 - a0, y1 - y0, ad, 0.1f);
            }
            return mb.ToMesh("muni ad wrap " + ad.Brand);
        }
    }
}
