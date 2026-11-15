using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Dogpatch.Backend;
using Dogpatch.Glb;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Avatar = Dogpatch.Backend.Avatar;

namespace Dogpatch.EditorTools
{
    // Headless avatar renders: for every avatar JSON in AVATAR_DIR (a bare avatar, {"avatar": …}
    // or a full upload response) writes RENDER_DIR/<name>.png showing the avatar (remix GLB,
    // recoloured, retargeted Idle) on the left and the stock model on the right, lit like the game.
    // A photo next to the JSON with the same name (.jpg/.jpeg/.png) is placed to the left of both.
    // AVATAR_DIR=stub runs the offline path instead: the bundled test photo goes through
    // InMemoryGameBackend's stub (AnalyzeAvatarPhotoAsync, then GetAvatarAsync) and the avatar it
    // returns is loaded and rendered as RENDER_DIR/stub.png.
    // Run under a virtual display (xvfb-run), not -nographics; see scripts/avatar-renders.sh.
    public static class AvatarRenders
    {
        [Serializable] class Envelope { public Avatar avatar; }

        const int Height = 900;
        const int FigureWidth = 1000;

        public static void Run()
        {
            try
            {
                string input = Environment.GetEnvironmentVariable("AVATAR_DIR") ?? "../Builds/avatars";
                string output = Environment.GetEnvironmentVariable("RENDER_DIR") ?? "../Builds/avatar-renders";
                Directory.CreateDirectory(output);
                var library = new ModelLibrary();
                var clipSource = library.Get(library.Role("player")[0].id);
                var material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
                var cam = SetupScene();
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                var jobs = input == "stub" ? new[] { ("stub", StubAvatar(), (string)null) } : Jobs(input);
                foreach (var (name, avatar, file) in jobs)
                {
                    var (remixBytes, remixNote) = Fetch(http, avatar.glbUrl);
                    var (stockBytes, _) = Fetch(http, avatar.fallbackGlbUrl);
                    if (remixBytes == null) { remixBytes = stockBytes; remixNote += " -> fallback"; }
                    if (remixBytes == null) throw new Exception(name + ": neither GLB could be fetched");

                    var figure = AvatarLoader.Prepare(GlbAsset.Load(name, remixBytes), avatar, clipSource);
                    var stock = stockBytes != null ? GlbAsset.Load(name + "-stock", stockBytes) : null;
                    int changed = AvatarLoader.Recolor(GlbAsset.Load("count", remixBytes), avatar.recolor);

                    var root = new GameObject("Avatars");
                    if (stock != null) Pose(stock.Instantiate(material, root.transform), stock, new Vector3(0.7f, 0f, 0f), -18f);
                    Pose(figure.Instantiate(material, root.transform), figure, stock != null ? new Vector3(-0.7f, 0f, 0f) : Vector3.zero, 18f);
                    var shot = Shoot(cam);
                    UnityEngine.Object.DestroyImmediate(root);

                    var photo = file != null ? Photo(file) : TestPhoto();
                    var final = photo != null ? SideBySide(photo, shot) : shot;
                    string path = Path.Combine(output, name + ".png");
                    File.WriteAllBytes(path, final.EncodeToPNG());
                    Debug.Log($"[AvatarRenders] {name}: base={avatar.@base} remix={remixNote} recoloured={changed} verts clips={string.Join(",", figure.Clips.Select(c => c.Name))} -> {path}");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[AvatarRenders] " + e);
                EditorApplication.Exit(1);
            }
        }

        static (string, Avatar, string)[] Jobs(string input)
        {
            var files = Directory.GetFiles(input, "*.json").OrderBy(f => f).ToArray();
            if (files.Length == 0) throw new Exception("no avatar JSON in " + Path.GetFullPath(input));
            return files.Select(f => (Path.GetFileNameWithoutExtension(f), Parse(File.ReadAllText(f)), f)).ToArray();
        }

        // The game's stub path, as a phone with no server would run it.
        static Avatar StubAvatar()
        {
            var backend = new InMemoryGameBackend(null);
            string id = backend.CreatePlayerAsync("Dogpatch Driver").Result.id;
            var bytes = Resources.Load<TextAsset>("Avatar/test-photo").bytes;
            var result = backend.AnalyzeAvatarPhotoAsync(id, bytes).Result;
            var avatar = backend.GetAvatarAsync(id).Result;
            Debug.Log($"[AvatarRenders] stub: player={id} photo={bytes.Length / 1024} KB provider={result.decision.provider} base={avatar.@base} glb={avatar.glbUrl}");
            return avatar;
        }

        static Texture2D TestPhoto()
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(Resources.Load<TextAsset>("Avatar/test-photo").bytes);
            return tex;
        }

        static Avatar Parse(string json)
        {
            var wrapped = JsonUtility.FromJson<Envelope>(json)?.avatar;
            if (wrapped != null && !wrapped.IsEmpty) return wrapped;
            var bare = JsonUtility.FromJson<Avatar>(json);
            if (bare == null || bare.IsEmpty) throw new Exception("not an avatar JSON");
            return bare;
        }

        static (byte[], string) Fetch(HttpClient http, string url)
        {
            if (string.IsNullOrEmpty(url)) return (null, "none");
            var bundled = AvatarLoader.Bundled(url);
            if (bundled != null) return (bundled, "bundled");
            var response = http.GetAsync(url).Result;
            var bytes = response.Content.ReadAsByteArrayAsync().Result;
            string remix = response.Headers.TryGetValues("x-remix", out var values) ? values.First() : "-";
            string note = $"{(int)response.StatusCode} x-remix={remix}";
            return ((int)response.StatusCode == 200 && AvatarLoader.IsGlb(bytes) ? bytes : null, note);
        }

        // Idle at half a second, sampled the way GlbAnimator samples it.
        static void Pose(GameObject instance, GlbAsset glb, Vector3 position, float yaw)
        {
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var clip = glb.Clips.FirstOrDefault(c => c.Name == "Idle") ?? glb.Clips.FirstOrDefault();
            if (clip == null) return;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in instance.GetComponentsInChildren<Transform>()) bones.TryAdd(t.name, t);
            float time = Mathf.Min(0.5f, clip.Length);
            foreach (var channel in clip.Channels)
            {
                if (!bones.TryGetValue(glb.Nodes[channel.Node].Name, out var bone)) continue;
                var v = Sample(channel, time);
                if (channel.IsRotation) bone.localRotation = new Quaternion(v.x, v.y, v.z, v.w);
                else bone.localPosition = new Vector3(v.x, v.y, v.z);
            }
        }

        static Vector4 Sample(GlbChannel channel, float t)
        {
            var times = channel.Times;
            if (times.Length == 1 || t <= times[0]) return channel.Values[0];
            int last = times.Length - 1;
            if (t >= times[last]) return channel.Values[last];
            int i = 0;
            while (times[i + 1] < t) i++;
            float u = (t - times[i]) / Mathf.Max(times[i + 1] - times[i], 1e-5f);
            var a = channel.Values[i];
            var b = channel.Values[i + 1];
            if (!channel.IsRotation) return Vector4.Lerp(a, b, u);
            var q = Quaternion.Slerp(new Quaternion(a.x, a.y, a.z, a.w), new Quaternion(b.x, b.y, b.z, b.w), u);
            return new Vector4(q.x, q.y, q.z, q.w);
        }

        static Texture2D Shoot(Camera cam)
        {
            var rt = new RenderTexture(FigureWidth, Height, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(FigureWidth, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, FigureWidth, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            return tex;
        }

        static Texture2D Photo(string jsonPath)
        {
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                string path = Path.ChangeExtension(jsonPath, ext);
                if (!File.Exists(path)) continue;
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(path));
                return tex;
            }
            return null;
        }

        static Texture2D SideBySide(Texture2D photo, Texture2D shot)
        {
            int pw = Mathf.RoundToInt(photo.width * (float)Height / photo.height);
            var rt = RenderTexture.GetTemporary(pw, Height, 0);
            Graphics.Blit(photo, rt);
            RenderTexture.active = rt;
            var scaled = new Texture2D(pw, Height, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, pw, Height), 0, 0);
            scaled.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            var result = new Texture2D(pw + shot.width, Height, TextureFormat.RGB24, false);
            result.SetPixels(0, 0, pw, Height, scaled.GetPixels());
            result.SetPixels(pw, 0, shot.width, Height, shot.GetPixels());
            result.Apply();
            return result;
        }

        // Same sun, fill and sky as GameController.SetupRendering, on a sidewalk-coloured ground.
        static Camera SetupScene()
        {
            var sky = new Color(0.66f, 0.81f, 0.92f);
            var cam = new GameObject("Avatar Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.position = new Vector3(0f, 1.25f, 5.6f);
            cam.transform.LookAt(new Vector3(0f, 0.88f, 0f));

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.55f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.2f;
            QualitySettings.shadowDistance = 30f;
            QualitySettings.shadowCascades = 1;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = false;
            Shader.SetGlobalColor("_SkyAmbient", new Color(0.36f, 0.4f, 0.47f));
            Shader.SetGlobalColor("_GroundAmbient", new Color(0.27f, 0.25f, 0.22f));

            var ground = new Mesh { name = "Ground" };
            ground.SetVertices(new List<Vector3> { new Vector3(-20, 0, -20), new Vector3(20, 0, -20), new Vector3(20, 0, 20), new Vector3(-20, 0, 20) });
            var sidewalk = new Color32(0xEE, 0xE7, 0xDA, 0xFF);
            ground.SetColors(Enumerable.Repeat(((Color)sidewalk).linear, 4).ToList());
            ground.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            ground.RecalculateNormals();
            var go = new GameObject("Ground");
            go.AddComponent<MeshFilter>().sharedMesh = ground;
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor"));
            return cam;
        }
    }
}
