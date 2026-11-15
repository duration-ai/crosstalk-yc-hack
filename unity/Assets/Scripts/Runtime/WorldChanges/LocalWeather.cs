using System.Collections.Generic;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // Stand-in for the weather controller: blends the sun, sky color, ambient and fog between
    // presets, and draws rain streaks or snow flakes in a box that follows the camera. It starts
    // from whatever lighting the scene already has, so "day" is always the scene's own look.
    public sealed class LocalWeather : MonoBehaviour, IWeather
    {
        struct Look
        {
            public Quaternion Sun;
            public Color SunColor;
            public float SunIntensity;
            public Color Sky;
            public Color SkyAmbient;
            public Color GroundAmbient;
            public Color FogColor;
            public float FogStart;
            public float FogEnd;

            public static Look Lerp(Look a, Look b, float t) => new Look
            {
                Sun = Quaternion.Slerp(a.Sun, b.Sun, t),
                SunColor = Color.Lerp(a.SunColor, b.SunColor, t),
                SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                Sky = Color.Lerp(a.Sky, b.Sky, t),
                SkyAmbient = Color.Lerp(a.SkyAmbient, b.SkyAmbient, t),
                GroundAmbient = Color.Lerp(a.GroundAmbient, b.GroundAmbient, t),
                FogColor = Color.Lerp(a.FogColor, b.FogColor, t),
                FogStart = Mathf.Lerp(a.FogStart, b.FogStart, t),
                FogEnd = Mathf.Lerp(a.FogEnd, b.FogEnd, t),
            };
        }

        Light sun;
        Camera view;
        Look baseline;
        Look current;
        Look from;
        float blendStart;
        float blendLength = 1f;
        string timeOfDay = "day";
        bool fog;
        bool rain;
        bool snow;
        float rainAmount;
        float snowAmount;
        float rainTarget;
        float snowTarget;
        Mesh precipitation;
        Transform precipitationRoot;
        readonly List<Vector3> drops = new List<Vector3>();
        bool captured;

        public void Init(Camera camera)
        {
            view = camera;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) { sun = light; break; }
            baseline = Capture();
            current = baseline;
            captured = true;

            var go = new GameObject("Precipitation");
            precipitationRoot = go.transform;
            precipitation = new Mesh { name = "precipitation" };
            precipitation.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = precipitation;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Resources.Load<Shader>("SpeedDial/UnlitVertexColor")) { name = "Precipitation" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var rng = new System.Random(5);
            for (int i = 0; i < 600; i++) drops.Add(new Vector3((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 22f, (float)rng.NextDouble() * 40f - 20f));
        }

        Look Capture() => new Look
        {
            Sun = sun != null ? sun.transform.rotation : Quaternion.Euler(50f, -30f, 0f),
            SunColor = sun != null ? sun.color : Color.white,
            SunIntensity = sun != null ? sun.intensity : 1f,
            Sky = view != null ? view.backgroundColor : Color.cyan,
            SkyAmbient = Shader.GetGlobalColor("_SkyAmbient"),
            GroundAmbient = Shader.GetGlobalColor("_GroundAmbient"),
            FogColor = RenderSettings.fogColor,
            FogStart = RenderSettings.fogStartDistance,
            FogEnd = RenderSettings.fogEndDistance,
        };

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        Look Target()
        {
            var look = baseline;
            float yaw = baseline.Sun.eulerAngles.y;
            switch (timeOfDay)
            {
                case "golden":
                    look.Sun = Quaternion.Euler(13f, yaw, 0f);
                    look.SunColor = Hex("#FFB25E");
                    look.SunIntensity = baseline.SunIntensity * 1.1f;
                    look.Sky = Hex("#F5A65B");
                    look.SkyAmbient = baseline.SkyAmbient * new Color(1.05f, 0.85f, 0.7f);
                    look.FogColor = Hex("#F7C08A");
                    break;
                case "dusk":
                    look.Sun = Quaternion.Euler(5f, yaw, 0f);
                    look.SunColor = Hex("#FF7E6B");
                    look.SunIntensity = baseline.SunIntensity * 0.7f;
                    look.Sky = Hex("#6E4FA0");
                    look.SkyAmbient = baseline.SkyAmbient * new Color(0.8f, 0.65f, 0.95f);
                    look.GroundAmbient = baseline.GroundAmbient * 0.7f;
                    look.FogColor = Hex("#9A6FB0");
                    break;
                case "night":
                    look.Sun = Quaternion.Euler(38f, yaw + 40f, 0f);
                    look.SunColor = Hex("#9DB2FF");
                    look.SunIntensity = baseline.SunIntensity * 0.28f;
                    look.Sky = Hex("#0F1B3D");
                    look.SkyAmbient = baseline.SkyAmbient * new Color(0.35f, 0.42f, 0.7f);
                    look.GroundAmbient = baseline.GroundAmbient * 0.35f;
                    look.FogColor = Hex("#18264F");
                    break;
            }
            if (rain || snow)
            {
                var grey = snow ? Hex("#DCE3EC") : Hex("#8E9AAE");
                look.Sky = Color.Lerp(look.Sky, grey, 0.75f);
                look.SunIntensity *= snow ? 0.75f : 0.55f;
                look.FogColor = Color.Lerp(look.FogColor, grey, 0.8f);
                look.FogStart = Mathf.Min(look.FogStart, 60f);
                look.FogEnd = Mathf.Min(look.FogEnd, snow ? 260f : 320f);
                if (snow) look.GroundAmbient = Color.Lerp(look.GroundAmbient, look.SkyAmbient, 0.6f);
            }
            if (fog)
            {
                var white = Color.Lerp(Hex("#DDE6EE"), look.Sky, timeOfDay == "day" ? 0.1f : 0.55f);
                look.FogColor = white;
                look.Sky = white;
                look.FogStart = 3f;
                look.FogEnd = 75f;
            }
            return look;
        }

        void Blend(float seconds)
        {
            from = current;
            blendStart = Time.time;
            blendLength = Mathf.Max(seconds, 0.01f);
        }

        public void SetTimeOfDay(string value, float blendSeconds)
        {
            timeOfDay = string.IsNullOrEmpty(value) ? "day" : value;
            Blend(blendSeconds);
        }

        public void SetFog(bool thick, float blendSeconds)
        {
            fog = thick;
            Blend(blendSeconds);
        }

        public void SetRain(bool on, float blendSeconds)
        {
            rain = on;
            if (on) snow = false;
            rainTarget = on ? 1f : 0f;
            if (on) snowTarget = 0f;
            Blend(blendSeconds);
        }

        public void SetSnow(bool on, float blendSeconds)
        {
            snow = on;
            if (on) rain = false;
            snowTarget = on ? 1f : 0f;
            if (on) rainTarget = 0f;
            Blend(blendSeconds);
        }

        public void ResetAll(float blendSeconds)
        {
            timeOfDay = "day";
            fog = rain = snow = false;
            rainTarget = snowTarget = 0f;
            Blend(blendSeconds);
        }

        void Update()
        {
            if (!captured) return;
            float t = Mathf.Clamp01((Time.time - blendStart) / blendLength);
            current = Look.Lerp(from, Target(), Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f) from = current;
            if (sun != null)
            {
                sun.transform.rotation = current.Sun;
                sun.color = current.SunColor;
                sun.intensity = current.SunIntensity;
            }
            if (view != null) view.backgroundColor = current.Sky;
            Shader.SetGlobalColor("_SkyAmbient", current.SkyAmbient);
            Shader.SetGlobalColor("_GroundAmbient", current.GroundAmbient);
            RenderSettings.fogColor = current.FogColor;
            RenderSettings.fogStartDistance = current.FogStart;
            RenderSettings.fogEndDistance = current.FogEnd;

            rainAmount = Mathf.MoveTowards(rainAmount, rainTarget, Time.deltaTime / 1.5f);
            snowAmount = Mathf.MoveTowards(snowAmount, snowTarget, Time.deltaTime / 1.5f);
            DrawPrecipitation();
        }

        void DrawPrecipitation()
        {
            if (rainAmount <= 0f && snowAmount <= 0f)
            {
                if (precipitation.vertexCount > 0) precipitation.Clear();
                return;
            }
            var center = view.transform.position + view.transform.forward * 10f;
            precipitationRoot.position = Vector3.zero;
            float dt = Time.deltaTime;
            bool snowing = snowAmount >= rainAmount;
            float amount = Mathf.Max(rainAmount, snowAmount);
            int count = Mathf.RoundToInt(drops.Count * amount);
            var mb = new MeshBuilder();
            var color = snowing ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.8f, 0.86f, 0.95f, 0.7f);
            var right = view.transform.right;
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                d.y -= (snowing ? 2.2f : 22f) * dt;
                if (snowing) d.x += Mathf.Sin(Time.time * 1.3f + i) * 0.6f * dt;
                if (d.y < 0f) d.y += 22f;
                drops[i] = d;
                if (i >= count) continue;
                var p = new Vector3(Mathf.Repeat(d.x - center.x, 40f) - 20f + center.x, center.y - 6f + d.y, Mathf.Repeat(d.z - center.z, 40f) - 20f + center.z);
                if (snowing)
                {
                    float s = 0.06f;
                    mb.Quad(p - right * s + Vector3.up * s, p + right * s + Vector3.up * s, p + right * s - Vector3.up * s, p - right * s - Vector3.up * s, color, -view.transform.forward);
                }
                else
                {
                    var top = p + new Vector3(0.12f, 0.9f, 0f);
                    float w = 0.022f;
                    mb.Quad(p - right * w, top - right * w, top + right * w, p + right * w, color, -view.transform.forward);
                }
            }
            var mesh = mb.ToMesh("precipitation");
            precipitation.Clear();
            precipitation.SetVertices(mesh.vertices);
            precipitation.SetColors(mesh.colors);
            precipitation.SetTriangles(mesh.triangles, 0);
            precipitation.RecalculateBounds();
            Destroy(mesh);
        }
    }
}
