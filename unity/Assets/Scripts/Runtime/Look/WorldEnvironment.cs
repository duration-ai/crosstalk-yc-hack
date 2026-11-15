using System;
using UnityEngine;

namespace Dogpatch.Look
{
    public enum TimePreset { Dawn, Day, Golden, Dusk, Night }

    public enum WeatherKind { Sun, Rain, Snow }

    // Time of day, weather and fog for the whole game. Static so gameplay (vehicle grip), world
    // changes (CROS-17) and debug tools can call it from anywhere, before or after the world is built:
    // the calls set targets and EnvironmentRig blends the live values toward them every frame and
    // applies them to the sun, sky, ambient, fog bank and shaders.
    public static class WorldEnvironment
    {
        public const float DefaultFog = 0.35f;

        public static event Action Changed;

        // Targets, as last requested.
        public static WeatherKind Weather { get; private set; } = WeatherKind.Sun;
        public static float WeatherIntensity { get; private set; } = 1f;

        // Live, blended values.
        public static float TimeOfDay { get; internal set; } = PresetHours(TimePreset.Day);
        public static float FogDensity { get; internal set; } = DefaultFog;
        public static float Rain { get; internal set; }
        public static float Snow { get; internal set; }
        public static float Wetness { get; internal set; }
        public static float SnowCover { get; internal set; }
        public static float NightFactor { get; internal set; }
        public static bool IsNight => NightFactor > 0.5f;

        // Multiplier on tire grip for vehicle handling: 1 dry, about 0.8 on wet asphalt, about 0.6
        // on snow.
        public static float GripMultiplier => Mathf.Min(Mathf.Lerp(1f, 0.8f, Wetness), Mathf.Lerp(1f, 0.6f, SnowCover));

        public static float GetSurfaceGrip(Vector3 worldPosition) => GripMultiplier;

        internal static float TargetTime = PresetHours(TimePreset.Day);
        internal static float TimeRate;
        internal static float TargetFog = DefaultFog;
        internal static float FogBlend;
        internal static float TargetRain;
        internal static float TargetSnow;
        internal static float WeatherBlend;

        public static float PresetHours(TimePreset preset)
        {
            switch (preset)
            {
                case TimePreset.Dawn: return 6.3f;
                case TimePreset.Golden: return 17.8f;
                case TimePreset.Dusk: return 19.4f;
                case TimePreset.Night: return 23f;
                default: return 14f;
            }
        }

        public static void SetTime(TimePreset preset, float blendSeconds = 2f) => SetTime(PresetHours(preset), blendSeconds);

        public static void SetTime(float hours, float blendSeconds = 2f)
        {
            TargetTime = Mathf.Repeat(hours, 24f);
            float gap = Mathf.Abs(Mathf.DeltaAngle(TimeOfDay * 15f, TargetTime * 15f) / 15f);
            TimeRate = blendSeconds > 0f ? gap / blendSeconds : float.PositiveInfinity;
            if (blendSeconds <= 0f) TimeOfDay = TargetTime;
            Changed?.Invoke();
        }

        // 0 clear, 0.35 the default bank on the bay, 1 thick.
        public static void SetFog(float density, float blendSeconds = 3f)
        {
            TargetFog = Mathf.Clamp01(density);
            FogBlend = Mathf.Max(blendSeconds, 0f);
            if (FogBlend == 0f) FogDensity = TargetFog;
            Changed?.Invoke();
        }

        public static void SetWeather(WeatherKind kind, float intensity = 1f, float blendSeconds = 3f)
        {
            Weather = kind;
            WeatherIntensity = Mathf.Clamp01(intensity);
            TargetRain = kind == WeatherKind.Rain ? WeatherIntensity : 0f;
            TargetSnow = kind == WeatherKind.Snow ? WeatherIntensity : 0f;
            WeatherBlend = Mathf.Max(blendSeconds, 0f);
            if (WeatherBlend == 0f)
            {
                Rain = Wetness = TargetRain;
                Snow = SnowCover = TargetSnow;
            }
            Changed?.Invoke();
        }

        // Debug and test hook: "time=night&weather=rain&intensity=0.8&fog=0.5", as passed by the
        // pause menu, a launch argument (-dogpatch-env "...") or DOGPATCH_ENV. Time takes a preset
        // name or hours; weather takes sun, rain or snow. Applies instantly.
        public static void Apply(string query)
        {
            if (string.IsNullOrEmpty(query)) return;
            string weather = null;
            float intensity = 1f;
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                string key = kv[0].Trim().ToLowerInvariant(), value = kv[1].Trim().ToLowerInvariant();
                if (key == "time")
                {
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours)) SetTime(hours, 0f);
                    else if (Enum.TryParse<TimePreset>(value, true, out var preset)) SetTime(preset, 0f);
                }
                else if (key == "fog" && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fog)) SetFog(fog, 0f);
                else if (key == "weather") weather = value;
                else if (key == "intensity") float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out intensity);
            }
            if (weather != null && Enum.TryParse<WeatherKind>(weather, true, out var kind)) SetWeather(kind, intensity, 0f);
            Debug.Log($"[Environment] time={TimeOfDay:F1} weather={Weather} intensity={WeatherIntensity:F2} fog={FogDensity:F2}");
        }
    }
}
