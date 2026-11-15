using Dogpatch.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Look
{
    // Applies WorldEnvironment every frame: blends the live values toward their targets, then drives
    // the sun (or moon), the three-way ambient, the sky gradient, distance fog, the fog bank on the
    // bay, cloud tint, rain and snow, the night glow and wet/snow surfaces in the shared shader, and
    // the driven car's headlights at night. Speed Dial stays bright: rain and snow lift the fill as
    // they dim the sun, and night is deep blue, never black.
    [DefaultExecutionOrder(-50)]
    public sealed class EnvironmentRig : MonoBehaviour
    {
        struct Key
        {
            public float Hour;
            public Color Top, Mid, Horizon, Sun, Fill, Side, Bounce;
            public float SunIntensity;
        }

        static Color L(string hex) => SpeedDial.Hex(hex).linear;

        static Key K(float hour, string top, string mid, string horizon, string sun, float intensity, Color fill, Color side, Color bounce) => new Key
        {
            Hour = hour, Top = L(top), Mid = L(mid), Horizon = L(horizon), Sun = SpeedDial.Hex(sun), SunIntensity = intensity,
            Fill = fill, Side = side, Bounce = bounce,
        };

        static readonly Color NightFill = new Color(0.09f, 0.13f, 0.30f);
        static readonly Color NightSide = new Color(0.12f, 0.16f, 0.34f);
        static readonly Color NightBounce = new Color(0.08f, 0.09f, 0.18f);

        static readonly Key[] Keys =
        {
            K(0f, "#081848", "#122C6A", "#24418A", "#9DB4FF", 0.34f, NightFill, NightSide, NightBounce),
            K(4.8f, "#081848", "#122C6A", "#24418A", "#9DB4FF", 0.34f, NightFill, NightSide, NightBounce),
            K(6.3f, "#1A2E78", "#5A6CC0", "#FFB08A", "#FFB38A", 0.42f, new Color(0.26f, 0.27f, 0.46f), new Color(0.36f, 0.33f, 0.5f), new Color(0.42f, 0.3f, 0.3f)),
            K(8f, "#0D66CF", "#3E9DEE", "#D6EAFB", "#FFE2B4", 0.66f, new Color(0.35f, 0.4f, 0.55f), new Color(0.5f, 0.53f, 0.68f), new Color(0.5f, 0.42f, 0.35f)),
            K(10.5f, "#0B6FD6", "#1C9BF2", "#B4E2FC", "#FFEBB0", 0.76f, SpeedDial.SkyFill, SpeedDial.SideFill, SpeedDial.Bounce),
            K(16.2f, "#0B6FD6", "#1C9BF2", "#B4E2FC", "#FFEBB0", 0.76f, SpeedDial.SkyFill, SpeedDial.SideFill, SpeedDial.Bounce),
            K(17.8f, "#1660C8", "#54A0EA", "#FFD48E", "#FFB45C", 1.05f, new Color(0.36f, 0.38f, 0.52f), new Color(0.56f, 0.5f, 0.6f), new Color(0.62f, 0.43f, 0.3f)),
            K(19.4f, "#26308A", "#7E5CBC", "#FF9C86", "#FF8A5A", 0.46f, new Color(0.27f, 0.26f, 0.46f), new Color(0.4f, 0.32f, 0.5f), new Color(0.46f, 0.3f, 0.33f)),
            K(20.8f, "#081848", "#122C6A", "#24418A", "#9DB4FF", 0.34f, NightFill, NightSide, NightBounce),
            K(24f, "#081848", "#122C6A", "#24418A", "#9DB4FF", 0.34f, NightFill, NightSide, NightBounce),
        };

        static readonly Color RainTop = L("#4F6A95"), RainMid = L("#7890B4"), RainHorizon = L("#B5C3D8");
        static readonly Color SnowTop = L("#9FB2CC"), SnowMid = L("#C6D2E2"), SnowHorizon = L("#EEF2F7");

        Camera view;
        Light sun;
        SkyRig sky;
        GameController game;
        Light headlights;
        Material rain, snow;
        float fogBankScale = 1f;

        public static EnvironmentRig Create(Camera view, Light sun, SkyRig sky)
        {
            var rig = new GameObject("Environment").AddComponent<EnvironmentRig>();
            rig.view = view;
            rig.sun = sun;
            rig.sky = sky;
            rig.rain = Precipitation.Create(rig.transform, false);
            rig.snow = Precipitation.Create(rig.transform, true);
            ApplyLaunchOverrides();
            rig.Step(0f);
            return rig;
        }

        // -dogpatch-env "time=night&weather=rain", or the DOGPATCH_ENV environment variable.
        static void ApplyLaunchOverrides()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-dogpatch-env") WorldEnvironment.Apply(args[i + 1]);
            WorldEnvironment.Apply(System.Environment.GetEnvironmentVariable("DOGPATCH_ENV"));
        }

        void Update() => Step(Time.deltaTime);

        static float Toward(float current, float target, float seconds, float dt) =>
            seconds <= 0f ? target : Mathf.MoveTowards(current, target, dt / seconds);

        void Step(float dt)
        {
            float tod = WorldEnvironment.TimeOfDay;
            float gap = Mathf.DeltaAngle(tod * 15f, WorldEnvironment.TargetTime * 15f) / 15f;
            float hourStep = WorldEnvironment.TimeRate * dt;
            WorldEnvironment.TimeOfDay = tod = Mathf.Repeat(tod + Mathf.Clamp(gap, -hourStep, hourStep), 24f);
            WorldEnvironment.FogDensity = Toward(WorldEnvironment.FogDensity, WorldEnvironment.TargetFog, WorldEnvironment.FogBlend, dt);
            WorldEnvironment.Rain = Toward(WorldEnvironment.Rain, WorldEnvironment.TargetRain, WorldEnvironment.WeatherBlend, dt);
            WorldEnvironment.Snow = Toward(WorldEnvironment.Snow, WorldEnvironment.TargetSnow, WorldEnvironment.WeatherBlend, dt);
            WorldEnvironment.Wetness = Toward(WorldEnvironment.Wetness, WorldEnvironment.TargetRain, WorldEnvironment.WeatherBlend * 2f, dt);
            WorldEnvironment.SnowCover = Toward(WorldEnvironment.SnowCover, WorldEnvironment.TargetSnow, WorldEnvironment.WeatherBlend * 3f, dt);
            float rainAmount = WorldEnvironment.Rain, snowAmount = WorldEnvironment.Snow;

            int k = 0;
            while (k < Keys.Length - 2 && tod >= Keys[k + 1].Hour) k++;
            var a = Keys[k];
            var b = Keys[k + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.Hour, b.Hour, tod));

            float elevation = SunElevation(tod);
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 6f, elevation));
            WorldEnvironment.NightFactor = night;

            var top = Color.Lerp(a.Top, b.Top, t);
            var mid = Color.Lerp(a.Mid, b.Mid, t);
            var horizon = Color.Lerp(a.Horizon, b.Horizon, t);
            float overcast = rainAmount * 0.8f * (1f - night * 0.6f);
            float whiteout = snowAmount * 0.85f * (1f - night * 0.6f);
            top = Color.Lerp(Color.Lerp(top, RainTop * Lum(top, 1.4f), overcast), SnowTop * Lum(top, 1.1f), whiteout);
            mid = Color.Lerp(Color.Lerp(mid, RainMid * Lum(mid, 1.2f), overcast), SnowMid * Lum(mid, 1f), whiteout);
            horizon = Color.Lerp(Color.Lerp(horizon, RainHorizon * Lum(horizon, 1f), overcast), SnowHorizon * Lum(horizon, 0.95f), whiteout);
            Shader.SetGlobalColor("_SkyTop", top);
            Shader.SetGlobalColor("_SkyMid", mid);
            Shader.SetGlobalColor("_SkyHorizon", horizon);

            float dim = (1f - 0.55f * rainAmount) * (1f - 0.4f * snowAmount);
            float lift = 1f + 0.18f * rainAmount + 0.22f * snowAmount;
            var cool = new Color(0.94f, 0.97f, 1.04f);
            Shader.SetGlobalColor("_SkyAmbient", Color.Lerp(a.Fill, b.Fill, t) * lift);
            Shader.SetGlobalColor("_SideAmbient", Color.Lerp(a.Side, b.Side, t) * lift * Color.Lerp(Color.white, cool, rainAmount));
            Shader.SetGlobalColor("_GroundAmbient", Color.Lerp(a.Bounce, b.Bounce, t) * Mathf.Lerp(1f, 1.35f, snowAmount));
            Shader.SetGlobalFloat("_DogpatchNight", night);
            Shader.SetGlobalFloat("_DogpatchWet", WorldEnvironment.Wetness);
            Shader.SetGlobalFloat("_DogpatchSnow", WorldEnvironment.SnowCover);

            float sunYaw = (tod - 12f) * 22.5f;
            var sunRotation = Quaternion.Euler(Mathf.Max(elevation, 8f), sunYaw, 0f);
            var moonRotation = Quaternion.Euler(48f, -30f, 0f);
            sun.transform.rotation = Quaternion.Slerp(sunRotation, moonRotation, night);
            sun.color = Color.Lerp(a.Sun, b.Sun, t);
            sun.intensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t) * dim;
            sun.shadowStrength = Mathf.Lerp(1f, 0.55f, Mathf.Max(rainAmount, snowAmount) * 0.8f) * Mathf.Lerp(1f, 0.7f, night);

            float fog = WorldEnvironment.FogDensity;
            float reach = fog <= WorldEnvironment.DefaultFog
                ? Mathf.Lerp(1.4f, 1f, fog / WorldEnvironment.DefaultFog)
                : Mathf.Lerp(1f, 0.1f, (fog - WorldEnvironment.DefaultFog) / (1f - WorldEnvironment.DefaultFog));
            reach *= (1f - 0.4f * rainAmount) * (1f - 0.5f * snowAmount);
            reach *= 1f + Mathf.Max(view.transform.position.y - 5f, 0f) / 60f;
            RenderSettings.fogColor = horizon.gamma;
            RenderSettings.fogStartDistance = 220f * reach;
            RenderSettings.fogEndDistance = 1900f * reach;
            view.backgroundColor = horizon.gamma;

            if (sky != null)
            {
                fogBankScale = 0.6f + fog * 1.15f;
                sky.Bank.localScale = new Vector3(1f, fogBankScale, 1f);
                sky.Props.SetColor("_Tint", Color.Lerp(Color.white, SpeedDial.Hex("#C9D1DE"), rainAmount * 0.8f));
            }

            rain.SetFloat("_Amount", rainAmount);
            rain.SetColor("_Color", new Color(0.86f, 0.92f, 1f, Mathf.Lerp(0.38f, 0.5f, night)));
            snow.SetFloat("_Amount", snowAmount);
            Shader.SetGlobalVector("_DogpatchWind", new Vector4(rainAmount * 1.5f + snowAmount * 0.8f, 0f, 0.6f, 0f));

            UpdateHeadlights(night);
        }

        static float Lum(Color c, float scale) => Mathf.Clamp(((c.r + c.g + c.b) / 3f) * scale / 0.45f, 0.18f, 1f);

        // Degrees above the horizon: up by 6:00, about 57 degrees at 14:00, down by 19:30.
        public static float SunElevation(float hours)
        {
            float day = Mathf.Repeat(hours - 6f, 24f);
            return day <= 13.5f ? 60f * Mathf.Sin(Mathf.PI * day / 13.5f) : -18f * Mathf.Sin(Mathf.PI * (day - 13.5f) / 10.5f);
        }

        // One pixel light at most, and only at night: the driven car's headlights.
        void UpdateHeadlights(float night)
        {
            if (game == null) game = FindAnyObjectByType<GameController>();
            var car = game != null ? game.CurrentCar : null;
            bool on = car != null && night > 0.05f;
            QualitySettings.pixelLightCount = on ? 2 : 1;
            if (!on)
            {
                if (headlights != null) headlights.enabled = false;
                return;
            }
            if (headlights == null)
            {
                headlights = new GameObject("Headlights").AddComponent<Light>();
                headlights.type = LightType.Spot;
                headlights.range = 32f;
                headlights.spotAngle = 75f;
                headlights.color = SpeedDial.Hex("#FFF3DA");
                headlights.shadows = LightShadows.None;
                headlights.renderMode = LightRenderMode.ForcePixel;
            }
            if (headlights.transform.parent != car.transform)
            {
                headlights.transform.SetParent(car.transform, false);
                headlights.transform.localPosition = new Vector3(0f, 0.9f, car.HalfExtents.z + 0.2f);
                headlights.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            }
            headlights.enabled = true;
            headlights.intensity = 2.6f * night;
        }
    }
}
