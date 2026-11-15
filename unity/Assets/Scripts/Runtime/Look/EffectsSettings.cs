using UnityEngine;

namespace Dogpatch.Look
{
    // One global intensity for every particle-style effect: wheel smoke, tire marks, dust, sparks,
    // wind streaks, spray, speed lines, and the smash debris, smoke and hydrant spout. 1 (MED) is the
    // default, tuned in MotionTuning (Resources/Look/motion.json); LOW is 0.4 and HIGH 1.6.
    // Counts and rates scale with Intensity, sizes and opacity with its square root, and nothing ever
    // drops to zero. The choice persists on the device.
    public static class EffectsSettings
    {
        public const float Low = 0.4f;
        public const float Medium = 1f;
        public const float High = 1.6f;
        const string Key = "dogpatch.effects";

        static float intensity = -1f;

        public static float Intensity
        {
            get
            {
                if (intensity < 0f) intensity = Mathf.Clamp(PlayerPrefs.GetFloat(Key, Medium), Low, High);
                return intensity;
            }
            set
            {
                intensity = Mathf.Clamp(value, 0.1f, 2f);
                PlayerPrefs.SetFloat(Key, intensity);
                PlayerPrefs.Save();
            }
        }

        // Multiplier for sizes and opacity.
        public static float Scale => Mathf.Sqrt(Intensity);

        // Particle count for a burst, never below one.
        public static int Count(float n) => Mathf.Max(1, Mathf.RoundToInt(n * Intensity));
    }
}
