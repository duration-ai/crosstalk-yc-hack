using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Smash
{
    public enum SmashStyle { Topple, Fly, Snap }

    [Serializable]
    public sealed class SmashTuning
    {
        public string role;
        public string label;
        public int points;
        public float breakSpeed;
        public float maxMassKg;
        public string style;
        public float snapHeight;
        public bool spout;

        public SmashStyle Style => style == "snap" ? SmashStyle.Snap : style == "fly" ? SmashStyle.Fly : SmashStyle.Topple;
    }

    [Serializable]
    public sealed class VehicleMass
    {
        public string id;
        public float massKg;
    }

    [Serializable]
    sealed class PhysicsFile
    {
        public VehicleMass[] vehicles;
        public SmashTuning[] roles;
    }

    // The gameplay side of the physics model, read from Resources/Smash/physics.json (baked by
    // tools/smash/bake-physics.py): vehicle masses from Polyfork, and how each breakable role smashes.
    // Which instances are breakable, and their mass and hulls, come from the world's StaticInstanceIndex.
    public static class PhysicsCatalog
    {
        static Dictionary<string, float> vehicles;
        static Dictionary<string, SmashTuning> roles;

        static void Load()
        {
            if (roles != null) return;
            var file = JsonUtility.FromJson<PhysicsFile>(Resources.Load<TextAsset>("Smash/physics").text);
            vehicles = new Dictionary<string, float>();
            roles = new Dictionary<string, SmashTuning>();
            foreach (var v in file.vehicles) vehicles[v.id] = v.massKg;
            foreach (var r in file.roles) roles[r.role] = r;
        }

        public static float VehicleMassKg(string modelId)
        {
            Load();
            return modelId != null && vehicles.TryGetValue(modelId, out var m) ? m : 0f;
        }

        // An exact role ("prop:street_lamp"), else its family ("prop"), else the instance kind.
        public static SmashTuning Tuning(string role, string kind)
        {
            Load();
            if (role != null && roles.TryGetValue(role, out var t)) return t;
            int colon = role?.IndexOf(':') ?? -1;
            if (colon > 0 && roles.TryGetValue(role.Substring(0, colon), out t)) return t;
            if (kind != null && roles.TryGetValue(kind, out t)) return t;
            return roles["prop"];
        }
    }
}
