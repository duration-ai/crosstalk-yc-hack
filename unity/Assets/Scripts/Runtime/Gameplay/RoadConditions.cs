using System;
using System.Reflection;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Weather and time of day as vehicles see them. Reads CROS-16's Dogpatch.Look.WorldEnvironment
    // (GripMultiplier, NightFactor) when that class is in the build, and falls back to a dry day.
    public static class RoadConditions
    {
        static Func<float> grip;
        static Func<float> night;
        static bool resolved;

        public static float GripMultiplier
        {
            get
            {
                Resolve();
                return grip != null ? Mathf.Clamp(grip(), 0.1f, 1f) : 1f;
            }
        }

        public static float NightFactor
        {
            get
            {
                Resolve();
                return night != null ? Mathf.Clamp01(night()) : 0f;
            }
        }

        static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Dogpatch.Look.WorldEnvironment");
                if (type == null) continue;
                grip = Getter(type, "GripMultiplier");
                night = Getter(type, "NightFactor");
                return;
            }
        }

        static Func<float> Getter(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null && property.PropertyType == typeof(float)) return (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), property.GetGetMethod());
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(float)) return () => (float)field.GetValue(null);
            return null;
        }
    }
}
