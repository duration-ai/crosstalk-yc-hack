using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Smash
{
    public enum PhysicsClass { Static, Breakable, Loose, Dynamic }

    [Serializable]
    public sealed class PhysicsEntry
    {
        public string id;
        public string role;
        public string physicsClass;
        public string label;
        public int points;
        public float breakSpeed;
        public float snapHeight;
        public float massKg;
        public float gameplayMassKg;
        public string material;
        public string body;
        public float[] hull;
    }

    [Serializable]
    sealed class PhysicsFile
    {
        public PhysicsEntry[] entries;
    }

    // One physics model for every Polyfork asset, read from Resources/Smash/physics.json, which
    // tools/smash/bake-physics.py builds from Polyfork's own mass, body and convex-hull data.
    public static class PhysicsCatalog
    {
        static Dictionary<string, PhysicsEntry> byId;
        static readonly Dictionary<string, Mesh> hulls = new Dictionary<string, Mesh>();

        static Dictionary<string, PhysicsEntry> Entries
        {
            get
            {
                if (byId != null) return byId;
                byId = new Dictionary<string, PhysicsEntry>();
                var file = JsonUtility.FromJson<PhysicsFile>(Resources.Load<TextAsset>("Smash/physics").text);
                foreach (var e in file.entries) byId[e.id] = e;
                return byId;
            }
        }

        public static PhysicsEntry Get(string assetId) =>
            assetId != null && Entries.TryGetValue(assetId, out var e) ? e : null;

        public static PhysicsClass ClassOf(PhysicsEntry e)
        {
            if (e == null) return PhysicsClass.Static;
            switch (e.physicsClass)
            {
                case "breakable": return PhysicsClass.Breakable;
                case "loose": return PhysicsClass.Loose;
                case "dynamic": return PhysicsClass.Dynamic;
                default: return PhysicsClass.Static;
            }
        }

        // Polyfork's convex hull as a mesh for a convex MeshCollider, optionally only the points
        // above a height (the crown of a snapped tree).
        public static Mesh Hull(string assetId, float above = float.NegativeInfinity)
        {
            string key = float.IsNegativeInfinity(above) ? assetId : assetId + "@" + above.ToString("F2");
            if (hulls.TryGetValue(key, out var mesh)) return mesh;
            var e = Get(assetId);
            var points = new List<Vector3>();
            if (e?.hull != null)
                for (int i = 0; i + 2 < e.hull.Length; i += 3)
                {
                    var p = new Vector3(e.hull[i], e.hull[i + 1], e.hull[i + 2]);
                    if (p.y >= above) points.Add(p);
                }
            if (points.Count < 4) mesh = null;
            else
            {
                var tris = new List<int>();
                for (int i = 1; i + 1 < points.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                mesh = new Mesh { name = key + "/hull" };
                mesh.SetVertices(points);
                mesh.SetTriangles(tris, 0);
            }
            hulls[key] = mesh;
            return mesh;
        }
    }
}
