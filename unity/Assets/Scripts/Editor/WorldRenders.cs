using System;
using System.Collections.Generic;
using System.IO;
using Dogpatch.Data;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.EditorTools
{
    // Headless renders of the four reference views (Tennessee St, 3rd St, 22nd St, Pier 70) plus an
    // aerial, lit like the game. Uses only WorldBuilder.Build, so it also runs on older branches for
    // before/after comparisons. Run under a virtual display (xvfb-run), not -nographics:
    //   RENDER_DIR=out Unity -batchmode -quit -projectPath unity -executeMethod Dogpatch.EditorTools.WorldRenders.Run
    public static class WorldRenders
    {
        public struct View
        {
            public string Name;
            public Vector3 Eye;
            public Vector3 Target;
            public float Fov;
        }

        public static void Run()
        {
            try
            {
                var material = Material();
                var data = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("World/dogpatch").text);
                var models = new ModelLibrary();
                var ctx = WorldBuilder.Build(data, models, material);
                Park(ctx, models, material);
                Render(ctx, data, Environment.GetEnvironmentVariable("RENDER_DIR") ?? "../Builds/renders", ViewsFrom(Environment.GetEnvironmentVariable("RENDER_VIEWS")));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldRenders] " + e);
                EditorApplication.Exit(1);
            }
        }

        // The parked cars the game spawns, with the same mix: mostly roster types, the rest GLB cars.
        public static void Park(WorldContext ctx, ModelLibrary models, Material material)
        {
            Vehicles.VehicleRoster.Configure(material, null);
            string[] curbside = { Vehicles.VehicleTypes.Sedan, Vehicles.VehicleTypes.Hatchback, Vehicles.VehicleTypes.YellowCab };
            var glb = models.Role("car");
            var random = new System.Random(7);
            for (int i = 0; i < ctx.ParkedCars.Count; i++)
            {
                var spot = ctx.ParkedCars[i];
                if (random.NextDouble() < 0.7) Vehicles.VehicleRoster.Spawn(curbside[random.Next(curbside.Length)], spot.Position, spot.Heading, ctx.Root.transform, id: "parked-" + i);
                else Gameplay.CarController.Create("parked-" + i, models.Get(glb[random.Next(glb.Count)].id), material, spot.Position, spot.Heading).transform.SetParent(ctx.Root.transform, true);
            }
        }

        public static Material Material() => new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };

        public static Vector2 Local(WorldData data, double lat, double lon)
        {
            const double MPerDeg = 111194.93;
            return new Vector2((float)((lon - data.originLon) * Math.Cos(data.originLat * Math.PI / 180) * MPerDeg), (float)((lat - data.originLat) * MPerDeg));
        }

        // A chase-camera view down a named street: the segment nearest 'near', looking along 'toward'.
        static View Street(WorldContext ctx, string name, string street, Vector2 near, Vector2 toward, float height, float back)
        {
            var net = ctx.Roads;
            float best = float.MaxValue;
            Vector2 a = near, d = toward.normalized;
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (net.Roads[r].name != street) continue;
                var pts = net.Points[r];
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    var mid = (pts[i] + pts[i + 1]) * 0.5f;
                    float dist = Vector2.Distance(mid, near);
                    if (dist >= best || Vector2.Distance(pts[i], pts[i + 1]) < 15f) continue;
                    best = dist;
                    a = mid;
                    d = (pts[i + 1] - pts[i]).normalized;
                    if (Vector2.Dot(d, toward) < 0f) d = -d;
                }
            }
            var eye = a - d * back;
            return new View { Name = name, Eye = new Vector3(eye.x, height, eye.y), Target = new Vector3(a.x + d.x * 60f, 2f, a.y + d.y * 60f), Fov = 60f };
        }

        // Extra views from a file, one per line: name eyeX eyeY eyeZ targetX targetY targetZ fov.
        static List<View> ViewsFrom(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var views = new List<View>();
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 8) continue;
                float F(int i) => float.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
                views.Add(new View { Name = f[0], Eye = new Vector3(F(1), F(2), F(3)), Target = new Vector3(F(4), F(5), F(6)), Fov = F(7) });
            }
            return views;
        }

        public static List<View> Views(WorldContext ctx, WorldData data)
        {
            var yc = Local(data, 37.76078, -122.38664);
            var caltrain = Local(data, 37.75761, -122.39263);
            return new List<View>
            {
                Street(ctx, "tennessee-st", "Tennessee Street", new Vector2(yc.x - 330f, yc.y - 260f), Vector2.up, 7f, 22f),
                Street(ctx, "3rd-st", "3rd Street", new Vector2(yc.x - 150f, yc.y - 120f), Vector2.up, 9f, 26f),
                Street(ctx, "22nd-st", "22nd Street", caltrain + new Vector2(120f, 0f), Vector2.right, 7f, 22f),
                new View { Name = "pier-70", Eye = new Vector3(yc.x + 150f, 55f, yc.y - 120f), Target = new Vector3(yc.x - 10f, 4f, yc.y + 10f), Fov = 50f },
                new View { Name = "aerial", Eye = new Vector3(yc.x + 260f, 260f, yc.y - 520f), Target = new Vector3(yc.x - 180f, 0f, yc.y - 60f), Fov = 55f },
            };
        }

        public static void Render(WorldContext ctx, WorldData data, string dir, List<View> views = null)
        {
            Directory.CreateDirectory(dir);
            var cam = SetupScene();
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var lod = ctx.Root.GetComponent("DistanceLod");
            var apply = lod?.GetType().GetMethod("Apply");
            foreach (var view in views ?? Views(ctx, data))
            {
                cam.transform.position = view.Eye;
                cam.transform.LookAt(view.Target);
                cam.fieldOfView = view.Fov;
                apply?.Invoke(lod, new object[] { view.Eye });
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, view.Name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Debug.Log($"[WorldRenders] {view.Name} eye={view.Eye} target={view.Target}");
            }
            cam.targetTexture = null;
        }

        // Same camera, sun, fog and ambient as GameController.SetupRendering.
        static Camera SetupScene()
        {
            var sky = new Color(0.66f, 0.81f, 0.92f);
            var cam = new GameObject("Render Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1400f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.55f;
            sun.shadowBias = 0.08f;
            sun.shadowNormalBias = 0.6f;

            QualitySettings.shadowDistance = 90f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 1100f;
            Shader.SetGlobalColor("_SkyAmbient", new Color(0.36f, 0.4f, 0.47f));
            Shader.SetGlobalColor("_GroundAmbient", new Color(0.27f, 0.25f, 0.22f));
            return cam;
        }
    }
}
