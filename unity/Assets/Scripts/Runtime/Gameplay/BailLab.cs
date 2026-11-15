using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Headless recording of bailing out of a moving car. Only active with -bailLab <dir>: on a
    // straight road (-labRoad x0,z0,x1,z1, 20th Street by default) the player drives a car up to
    // each of -bailSpeeds (5,10,15 m/s by default), gets out at that speed and is filmed through the
    // game's own follow camera for a few seconds. Writes PPM frames and labels.txt, then quits.
    public sealed class BailLab : MonoBehaviour
    {
        string dir;
        float[] speeds = { 5f, 10f, 15f };
        Vector2 a = new Vector2(-170f, 375f), b = new Vector2(-405.4f, 360.6f);
        const int Width = 960, Height = 540;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var dir = Arg("-bailLab");
            if (dir == null) return;
            var lab = new GameObject("BailLab").AddComponent<BailLab>();
            lab.dir = dir;
            if (Arg("-bailSpeeds") is string s) lab.speeds = Floats(s);
            if (Arg("-labRoad") is string r)
            {
                var f = Floats(r);
                lab.a = new Vector2(f[0], f[1]);
                lab.b = new Vector2(f[2], f[3]);
            }
        }

        static float[] Floats(string csv) => Array.ConvertAll(csv.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(dir);
            Time.captureFramerate = 30;
            GameController game = null;
            while (game == null || !game.IsReady)
            {
                game = FindAnyObjectByType<GameController>();
                yield return null;
            }
            for (int i = 0; i < 60; i++) yield return null;

            var road = (b - a).normalized;
            float heading = Mathf.Atan2(road.x, road.y) * Mathf.Rad2Deg;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var labels = new StreamWriter(Path.Combine(dir, "labels.txt"));
            int frame = 0;

            for (int take = 0; take < speeds.Length; take++)
            {
                float target = speeds[take];
                CarController car = null;
                float best = float.MaxValue;
                foreach (var c in game.Cars.Values)
                    if (!c.Driven && !c.Autopilot && c.HalfExtents.z >= 1.9f && c.HalfExtents.z < best) { best = c.HalfExtents.z; car = c; }
                if (car == null)
                {
                    Debug.Log("[BailLab] no car");
                    break;
                }
                var start = a + road * (take * 70f);
                car.Place(new Vector3(start.x, 0.05f, start.y), heading);
                var body = car.GetComponent<Rigidbody>();
                body.position = car.transform.position;
                body.rotation = car.transform.rotation;
                Physics.SyncTransforms();
                game.Player.Teleport(car.transform.position - car.transform.right * 2.5f, heading);
                yield return null;
                var enter = game.EnterCar(car);
                while (!enter.IsCompleted) yield return null;
                game.ExternalInput = true;
                game.Follow.Snap(car.transform, true);

                int first = frame;
                bool exited = false;
                float after = 0f;
                float exitSpeed = 0f;
                game.View.targetTexture = rt;
                while (after < 3.6f)
                {
                    if (!exited)
                    {
                        car.SetInput(new Vector3(road.x, 0f, road.y), Mathf.Clamp01(target / Mathf.Max(car.Handling.TopSpeed, 1f) + 0.15f));
                        if (car.Speed >= target)
                        {
                            exited = true;
                            exitSpeed = car.Speed;
                            car.SetInput(Vector3.zero, 0f);
                            game.ExternalInput = false;
                            _ = game.ExitCar();
                            Debug.Log($"[BailLab] take {take + 1}: getting out at {exitSpeed:F1} m/s");
                        }
                    }
                    else after += Time.deltaTime;

                    yield return new WaitForEndOfFrame();
                    if (exited || car.Speed > target - 4f)
                    {
                        RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        RenderTexture.active = null;
                        WritePpm(Path.Combine(dir, $"frame_{frame:D5}.ppm"), texture.GetPixels32());
                        if (frame % 5 == 0) Debug.Log($"[BailLab] f{frame} player {game.Player.transform.position} car {car.transform.position} carSpeed {car.Speed:F1}");
                        frame++;
                    }
                }
                game.View.targetTexture = null;
                labels.WriteLine($"{first} {frame - 1} bail out at {exitSpeed:F0} m/s ({exitSpeed * 3.6f:F0} km/h)");
                labels.Flush();
            }
            labels.Close();
            Debug.Log($"[BailLab] done: {frame} frames in {dir}");
            Application.Quit();
        }

        static void WritePpm(string path, Color32[] pixels)
        {
            var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
            var bytes = new byte[header.Length + Width * Height * 3];
            header.CopyTo(bytes, 0);
            int o = header.Length;
            for (int y = Height - 1; y >= 0; y--)
                for (int x = 0; x < Width; x++)
                {
                    var c = pixels[y * Width + x];
                    bytes[o++] = c.r;
                    bytes[o++] = c.g;
                    bytes[o++] = c.b;
                }
            File.WriteAllBytes(path, bytes);
        }
    }
}
