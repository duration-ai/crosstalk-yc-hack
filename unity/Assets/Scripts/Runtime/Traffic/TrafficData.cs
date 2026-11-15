using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // The traffic-relevant part of the area extract, read with CROS-19's field names (lanes,
    // lanesForward/Backward, maxspeed, props with traffic_signal/stop_sign/bicycle_rental, platforms).
    // Unknown fields are ignored, so the older extract loads too; anything missing falls back to
    // transit-fallback.json (OSM positions of the Bay Wheels docks, the Muni 55 loop, the T Third stops).
    [Serializable]
    public class TrafficWorld
    {
        public TrafficRoad[] roads;
        public TrafficProp[] props;
        public TrafficPlatform[] platforms;
        public TrafficRail[] rails;

        public static TrafficWorld Load()
        {
            var world = JsonUtility.FromJson<TrafficWorld>(Resources.Load<TextAsset>("World/dogpatch").text);
            world.props = world.props ?? Array.Empty<TrafficProp>();
            world.platforms = world.platforms ?? Array.Empty<TrafficPlatform>();
            world.rails = world.rails ?? Array.Empty<TrafficRail>();
            return world;
        }

        public bool HasControls
        {
            get
            {
                foreach (var p in props)
                    if (p.kind == "traffic_signal" || p.kind == "stop_sign") return true;
                return false;
            }
        }

        public IEnumerable<TrafficProp> Props(string kind)
        {
            foreach (var p in props)
                if (p.kind == kind) yield return p;
        }
    }

    [Serializable]
    public class TrafficRoad
    {
        public string name;
        public string kind;
        public float width;
        public bool oneway;
        public int lanes;
        public int lanesForward;
        public int lanesBackward;
        public string maxspeed;
        public float[] pts;

        // OSM maxspeed ("25 mph", "40") in metres per second, 0 when untagged.
        public float MaxSpeed
        {
            get
            {
                if (string.IsNullOrEmpty(maxspeed)) return 0f;
                var parts = maxspeed.Trim().Split(' ');
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return 0f;
                return parts.Length > 1 && parts[1] == "mph" ? v * 0.44704f : v / 3.6f;
            }
        }
    }

    [Serializable]
    public class TrafficProp
    {
        public string id;
        public string kind;
        public string name;
        public float x;
        public float z;
        public float heading;
        public bool headingKnown;
        public string[] tags;

        public Vector2 Position => new Vector2(x, z);

        public int Capacity => int.TryParse(Tag("capacity"), out int c) ? c : 0;

        public string Tag(string key)
        {
            if (tags == null) return null;
            foreach (var t in tags)
                if (t.StartsWith(key + "=")) return t.Substring(key.Length + 1);
            return null;
        }
    }

    [Serializable]
    public class TrafficPlatform
    {
        public string name;
        public string kind;
        public string[] tags;
        public float[] pts;

        public bool LightRail => tags != null && Array.IndexOf(tags, "light_rail=yes") >= 0;

        public Vector2 Center
        {
            get
            {
                var c = Vector2.zero;
                int n = pts.Length / 2;
                for (int i = 0; i < n; i++) c += new Vector2(pts[i * 2], pts[i * 2 + 1]);
                return n > 0 ? c / n : c;
            }
        }
    }

    [Serializable]
    public class TrafficRail
    {
        public string kind;
        public string name;
        public float[] pts;
    }

    [Serializable]
    public class TransitFallback
    {
        public NamedPoint[] docks;
        public FallbackRoute busRoute;
        public NamedPoint[] railStops;

        public static TransitFallback Load() => JsonUtility.FromJson<TransitFallback>(Resources.Load<TextAsset>("Vehicles/transit-fallback").text);
    }

    [Serializable]
    public class NamedPoint
    {
        public string name;
        public float x;
        public float z;
        public int capacity;

        public Vector2 Position => new Vector2(x, z);
    }

    [Serializable]
    public class FallbackRoute
    {
        public string @ref;
        public string name;
        public float[] waypoints;
        public NamedPoint[] stops;
    }
}
