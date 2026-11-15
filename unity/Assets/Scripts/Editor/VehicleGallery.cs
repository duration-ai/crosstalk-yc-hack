using System;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.EditorTools
{
    // Renders every roster vehicle on a sidewalk-coloured ground under the style guide's light (warm
    // sun from the southwest, sky fill), one PNG per type plus a contact sheet:
    //   xvfb-run Unity -batchmode -force-glcore -projectPath unity -executeMethod Dogpatch.EditorTools.VehicleGallery.Render
    // GALLERY_OUTPUT names the folder (default ../Builds/gallery). NIGHT=1 renders with the lamps lit.
    public static class VehicleGallery
    {
        const int Width = 1200;
        const int Height = 800;

        public static void Render()
        {
            string output = Path.GetFullPath(Environment.GetEnvironmentVariable("GALLERY_OUTPUT") ?? "../Builds/gallery");
            bool night = Environment.GetEnvironmentVariable("NIGHT") == "1";
            Directory.CreateDirectory(output);
            var material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
            VehicleRoster.Configure(material, VehicleModels.Asset("Polyfork/commuter-381e7f"));

            Shader.SetGlobalColor("_SkyAmbient", night ? new Color(0.09f, 0.11f, 0.2f) : new Color(0.42f, 0.5f, 0.68f));
            Shader.SetGlobalColor("_GroundAmbient", night ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.36f, 0.3f, 0.24f));
            Shader.SetGlobalFloat("_DogpatchNight", night ? 1f : 0f);
            RenderSettings.fog = false;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52f, 40f, 0f);
            sun.color = night ? new Color(0.35f, 0.4f, 0.6f) : new Color(1f, 0.93f, 0.8f);
            sun.intensity = night ? 0.3f : 1.1f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.5f;
            QualitySettings.shadowDistance = 60f;

            var ground = new LowPoly();
            ground.Quad(new Vector3(-40f, 0f, -40f), new Vector3(-40f, 0f, 40f), new Vector3(40f, 0f, 40f), new Vector3(40f, 0f, -40f), LowPoly.Hex("#eee7da"));
            var groundGo = new GameObject("ground");
            groundGo.AddComponent<MeshFilter>().sharedMesh = ground.ToMesh("ground");
            groundGo.AddComponent<MeshRenderer>().sharedMaterial = material;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = night ? new Color(0.06f, 0.08f, 0.18f) : new Color(0.71f, 0.89f, 0.99f);
            cam.fieldOfView = 30f;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;

            foreach (var type in VehicleTypes.All)
            {
                var car = VehicleRoster.Spawn(type, Vector3.zero, 0f, null, 1);
                var b = new Bounds(car.transform.position, Vector3.zero);
                foreach (var r in car.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float size = Mathf.Max(b.size.x, b.size.y * 1.4f, b.size.z);
                float distance = size / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.25f + 0.5f;
                var dir = Quaternion.Euler(18f, 145f, 0f) * Vector3.back;
                cam.transform.position = b.center + dir * distance;
                cam.transform.LookAt(b.center);
                cam.Render();
                Save(rt, Path.Combine(output, type + (night ? "-night" : "") + ".png"));
                UnityEngine.Object.DestroyImmediate(car.gameObject);
                Debug.Log("[Gallery] " + type + " size " + b.size.ToString("F2"));
            }
            Debug.Log("[Gallery] wrote " + output);
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
