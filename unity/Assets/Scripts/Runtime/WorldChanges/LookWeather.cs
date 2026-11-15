using System;
using System.Reflection;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // IWeather over CROS-16's controller (Dogpatch.Look.WorldEnvironment, branch
    // capy/speed-dial-weather). That branch is not under this one yet, so the controller is found at
    // runtime; once both branches are in, Create returns this and LocalWeather is no longer used.
    public sealed class LookWeather : IWeather
    {
        readonly Type env;
        readonly Type timePreset;
        readonly Type weatherKind;

        LookWeather(Type env, Type timePreset, Type weatherKind)
        {
            this.env = env;
            this.timePreset = timePreset;
            this.weatherKind = weatherKind;
        }

        public static IWeather Create()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var env = assembly.GetType("Dogpatch.Look.WorldEnvironment");
                if (env == null) continue;
                var time = assembly.GetType("Dogpatch.Look.TimePreset");
                var kind = assembly.GetType("Dogpatch.Look.WeatherKind");
                if (time == null || kind == null) return null;
                Debug.Log("[SpeedDial] weather goes through Dogpatch.Look.WorldEnvironment");
                return new LookWeather(env, time, kind);
            }
            return null;
        }

        void Call(string method, Type first, object a, float blend)
        {
            var m = env.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, new[] { first, typeof(float) }, null);
            if (m == null) Debug.LogWarning($"[SpeedDial] WorldEnvironment.{method}({first.Name}, float) not found");
            else m.Invoke(null, new[] { a, blend });
        }

        public void SetTimeOfDay(string timeOfDay, float blendSeconds)
        {
            string preset = timeOfDay == "golden" ? "Golden" : timeOfDay == "dusk" ? "Dusk" : timeOfDay == "night" ? "Night" : "Day";
            Call("SetTime", timePreset, Enum.Parse(timePreset, preset), blendSeconds);
        }

        public void SetFog(bool thick, float blendSeconds) => Call("SetFog", typeof(float), thick ? 1f : 0.35f, blendSeconds);

        void Weather(string kind, float blendSeconds)
        {
            var m = env.GetMethod("SetWeather", BindingFlags.Public | BindingFlags.Static, null, new[] { weatherKind, typeof(float), typeof(float) }, null);
            if (m == null) Debug.LogWarning("[SpeedDial] WorldEnvironment.SetWeather(WeatherKind, float, float) not found");
            else m.Invoke(null, new object[] { Enum.Parse(weatherKind, kind), 1f, blendSeconds });
        }

        public void SetRain(bool on, float blendSeconds) => Weather(on ? "Rain" : "Sun", blendSeconds);

        public void SetSnow(bool on, float blendSeconds) => Weather(on ? "Snow" : "Sun", blendSeconds);

        public void ResetAll(float blendSeconds)
        {
            SetTimeOfDay("day", blendSeconds);
            SetFog(false, blendSeconds);
            Weather("Sun", blendSeconds);
        }
    }
}
