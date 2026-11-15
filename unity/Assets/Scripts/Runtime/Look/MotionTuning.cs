using System;
using UnityEngine;

namespace Dogpatch.Look
{
    // Every motion-feel tunable in one place, read from Resources/Look/motion.json: speed lines,
    // wind streaks, wheel puffs, landings, sparks, squash and stretch, and the camera juice.
    // Speeds are in m/s, times in seconds, sizes in metres, and widths of screen-space lines in
    // pixels on a 720 px tall screen. EffectsSettings.Intensity (FX LOW/MED/HIGH) still scales
    // particle counts and, by its square root, sizes and opacity on top of these.
    [Serializable]
    public sealed class MotionTuning
    {
        public SpeedFeel speed = new SpeedFeel();
        public SpeedLineFeel speedLines = new SpeedLineFeel();
        public StreakFeel streaks = new StreakFeel();
        public PuffFeel puffs = new PuffFeel();
        public LandingFeel landing = new LandingFeel();
        public SparkFeel sparks = new SparkFeel();
        public SquashFeel squash = new SquashFeel();
        public CameraFeelTuning camera = new CameraFeelTuning();

        // How fast feels fast. SpeedFactor is 0 at floor and 1 at the driven car's own top speed
        // (never below minTopSpeed, so slow vehicles still have to move). Rush, which drives the
        // speed lines and wind streaks, adds boost and downhill on top.
        [Serializable]
        public sealed class SpeedFeel
        {
            public float floor = 4f;
            public float minTopSpeed = 14f;
            public float rushStart = 0.35f;
            public float rushFull = 0.85f;
            public float boostRush = 1f;
            public float downhillRush = 0.5f;
            public float downhillFullDrop = 5f;
            public float boostSeconds = 1.1f;
            public float takeoffBoost = 0.6f;
            public float raceStartBoost = 1f;
        }

        // White streaks at the screen edges, flying outward from just above the middle. The middle
        // stays clear: lines start at innerRadius (1 is the screen edge) and never point straight
        // down at the car or straight up the sky.
        [Serializable]
        public sealed class SpeedLineFeel
        {
            public int count = 30;
            public float alpha = 0.8f;
            public float width = 2.6f;
            public float innerRadius = 0.62f;
            public float lengthMin = 0.18f;
            public float lengthMax = 0.42f;
            public float flow = 2.4f;
            public float bandShare = 0.2f;
            public float bandAlpha = 0.25f;
            public float bandWidth = 9f;
            public float centerY = 0.06f;
            public float fadeIn = 3f;
            public float fadeOut = 2f;
        }

        // World-space wind streaks around the camera at speed.
        [Serializable]
        public sealed class StreakFeel
        {
            public float rate = 18f;
            public float alpha = 0.45f;
            public float width = 0.03f;
        }

        // Chunky faceted puffs from the rear wheels on launches, drifts and hard braking, carried
        // along with the car so they bunch behind the wheels instead of trailing as a cloud.
        [Serializable]
        public sealed class PuffFeel
        {
            public float alpha = 0.78f;
            public float size = 0.3f;
            public float life = 0.55f;
            public float carry = 0.6f;
            public float kick = 2f;
            public float launchRate = 12f;
            public float launchMaxSpeedShare = 0.45f;
            public float launchAccelShare = 0.6f;
            public float driftRate = 14f;
            public float driftStart = 7f;
            public float driftFull = 13f;
            public float brakeRate = 10f;
            public float brakeDecelShare = 0.6f;
            public float brakeMinSpeed = 5f;
            public float shardChance = 0.35f;
            public float shardSize = 0.07f;
            public int boostPuffs = 4;
            public float boostSize = 0.4f;
        }

        // Car touchdowns: a ring of puffs and shards. Footstep and on-foot landings stay tiny.
        [Serializable]
        public sealed class LandingFeel
        {
            public float minStrength = 0.2f;
            public int puffs = 8;
            public float size = 0.4f;
            public float spread = 3.5f;
            public float life = 0.5f;
            public int shards = 6;
            public float onFootScale = 0.35f;
        }

        // Wall and car scrapes: hot streaks spraying off the contact.
        [Serializable]
        public sealed class SparkFeel
        {
            public int countMin = 5;
            public int countMax = 18;
            public float lifeMin = 0.18f;
            public float lifeMax = 0.42f;
            public float width = 0.04f;
            public float trail = 0.045f;
        }

        // The car body stretches on takeoff and squashes on touchdown, then springs back.
        [Serializable]
        public sealed class SquashFeel
        {
            public float takeoffStretch = 0.12f;
            public float landSquash = 0.2f;
            public float period = 0.32f;
            public float damping = 0.35f;
        }

        // Camera juice layered on the follow camera: field of view widens with speed and kicks on a
        // boost, the camera banks into turns, and boosts, landings and scrapes add shake.
        [Serializable]
        public sealed class CameraFeelTuning
        {
            public float fovSpeed = 14f;
            public float fovSpeedPower = 1.2f;
            public float fovBoost = 8f;
            public float fovPunch = 7f;
            public float punchSeconds = 0.35f;
            public float fovResponse = 6f;
            public float leanMax = 3.5f;
            public float leanPerLateral = 0.3f;
            public float leanResponse = 5f;
            public float boostShake = 0.25f;
            public float scrapeShake = 0.05f;
        }

        static MotionTuning current;

        public static MotionTuning Current => current ??= Load();

        static MotionTuning Load()
        {
            var file = Resources.Load<TextAsset>("Look/motion");
            return file != null ? JsonUtility.FromJson<MotionTuning>(file.text) : new MotionTuning();
        }
    }
}
