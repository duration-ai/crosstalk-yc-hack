using System;
using System.Collections.Generic;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Renders the Operator's fire and explosion effects headless at a few moments of their life,
    // day and night, so their look can be checked without a device build. Output: Builds/fire-renders.
    public static class FireRenders
    {
        const int Width = 1200;
        const int Height = 800;

        public static void Run()
        {
            string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FIRE_OUTPUT") ?? "../Builds/fire-renders");
            Directory.CreateDirectory(output);
            var material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
            VehicleRoster.Configure(material, VehicleModels.Asset("Polyfork/commuter-381e7f"));

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52f, 40f, 0f);
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.5f;
            RenderSettings.fog = false;

            var ground = new LowPoly();
            ground.Quad(new Vector3(-60f, 0f, -60f), new Vector3(-60f, 0f, 60f), new Vector3(60f, 0f, 60f), new Vector3(60f, 0f, -60f), LowPoly.Hex("#d9d2c4"));
            var groundGo = new GameObject("ground");
            groundGo.AddComponent<MeshFilter>().sharedMesh = ground.ToMesh("ground");
            groundGo.AddComponent<MeshRenderer>().sharedMaterial = material;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 35f;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var car = VehicleRoster.Spawn(VehicleTypes.All[0], Vector3.zero, 0f, null, 1);
            var carBounds = new Bounds(car.transform.position, Vector3.zero);
            foreach (var r in car.GetComponentsInChildren<Renderer>()) carBounds.Encapsulate(r.bounds);
            var tree = new Bounds(new Vector3(7f, 3.5f, 2f), new Vector3(3f, 7f, 3f));
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.position = new Vector3(7f, 1.5f, 2f);
            trunk.transform.localScale = new Vector3(0.6f, 1.5f, 0.6f);

            foreach (bool night in new[] { false, true })
            {
                Shader.SetGlobalColor("_SkyAmbient", night ? new Color(0.09f, 0.11f, 0.2f) : new Color(0.42f, 0.5f, 0.68f));
                Shader.SetGlobalColor("_GroundAmbient", night ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.36f, 0.3f, 0.24f));
                Shader.SetGlobalFloat("_DogpatchNight", night ? 1f : 0f);
                sun.color = night ? new Color(0.35f, 0.4f, 0.6f) : new Color(1f, 0.93f, 0.8f);
                sun.intensity = night ? 0.3f : 1.1f;
                cam.backgroundColor = night ? new Color(0.06f, 0.08f, 0.18f) : new Color(0.71f, 0.89f, 0.99f);
                string tag = night ? "night" : "day";

                var fires = new List<FireFx> { FireFx.Create(material, carBounds, 1f, true), FireFx.Create(material, tree, 1f, true) };
                cam.transform.position = new Vector3(-6f, 6f, -14f);
                cam.transform.LookAt(new Vector3(3.5f, 3f, 1f));
                foreach (float t in new[] { 0.4f, 1.5f, 3f })
                {
                    foreach (var f in fires) foreach (var ps in f.Systems) ps.Simulate(t, true, true);
                    cam.Render();
                    Save(rt, Path.Combine(output, $"fire-{tag}-{t:F1}s.png"));
                }
                foreach (var f in fires) UnityEngine.Object.DestroyImmediate(f.gameObject);

                var blast = FireFx.Explosion(material, carBounds, 1.3f, true);
                cam.transform.position = new Vector3(-10f, 5f, -16f);
                cam.transform.LookAt(carBounds.center + Vector3.up * 2f);
                foreach (float t in new[] { 0.12f, 0.4f, 1f, 2f })
                {
                    foreach (var ps in blast.Systems) ps.Simulate(t, true, true);
                    cam.Render();
                    Save(rt, Path.Combine(output, $"explode-{tag}-{t:F2}s.png"));
                }
                UnityEngine.Object.DestroyImmediate(blast.gameObject);
            }
            Debug.Log("[FireRenders] wrote " + output);
        }

        static void Save(RenderTexture rt, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
