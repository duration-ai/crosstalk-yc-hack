using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Map
{
    public enum MapMarkerKind { Ask, Race }

    // Things other systems put on the minimap. CROS-17's ask pickups call Track when they spawn
    // and Untrack when they are collected; destroyed transforms drop out on their own.
    public static class MapMarkers
    {
        static readonly List<(Transform target, MapMarkerKind kind)> tracked = new List<(Transform, MapMarkerKind)>();

        public static void Track(Transform target, MapMarkerKind kind)
        {
            Untrack(target);
            tracked.Add((target, kind));
        }

        public static void Untrack(Transform target) => tracked.RemoveAll(m => m.target == target || m.target == null);

        public static IReadOnlyList<(Transform target, MapMarkerKind kind)> All
        {
            get
            {
                tracked.RemoveAll(m => m.target == null);
                return tracked;
            }
        }
    }
}
