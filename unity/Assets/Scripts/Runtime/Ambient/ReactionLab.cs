using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Ambient
{
    // Headless recording of how pedestrians react to cars. Only active with -reactionLab <dir>: on a
    // straight stretch of road (-labRoad x0,z0,x1,z1, 20th Street by default) it stands one person
    // at a time in the lane and drives a car through them, first as plain hits at rising speeds
    // (the car reports no speed, so nobody dives), then at speed with warning so they dive. It
    // writes PPM frames, a labels.txt of frame ranges and a stats.csv, then quits. -labSlow films
    // at 60 steps per second so a 30 fps encode plays at half speed. The player is parked behind the
    // camera each take, so the parked-car streamer keeps the filmed car (and its threats) live.
    [DefaultExecutionOrder(100)]
    public sealed class ReactionLab : MonoBehaviour
    {
        static readonly float[] HitSpeeds = { 2f, 4f, 6f, 8f, 11f, 14f, 17f, 20f, 24f, 28f };
        static readonly float[] DiveSpeeds = { 9f, 14f, 19f, 25f };
        const float Spacing = 16f;
        const float Hold = 4.6f;

        string dir;
        Vector2 a = new Vector2(-170f, 375f), b = new Vector2(-405.4f, 360.6f);
        int width = 960, height = 540;
        bool slow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var dir = Arg("-reactionLab");
            if (dir == null) return;
            var lab = new GameObject("ReactionLab").AddComponent<ReactionLab>();
            lab.dir = dir;
            lab.slow = Array.IndexOf(args, "-labSlow") >= 0;
            if (Arg("-labRoad") is string r)
            {
                var f = Array.ConvertAll(r.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
                lab.a = new Vector2(f[0], f[1]);
                lab.b = new Vector2(f[2], f[3]);
            }
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(dir);
            Time.captureFramerate = slow ? 60 : 30;
            while (PedestrianSystem.Instance == null || Camera.main == null) yield return null;
            var system = PedestrianSystem.Instance;
            var road = (b - a).normalized;
            var side = new Vector2(road.y, -road.x);
            float heading = Mathf.Atan2(road.x, road.y) * Mathf.Rad2Deg;
            PedestrianSystem.FocusOverride = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

            var main = Camera.main;
            var cam = new GameObject("Lab Camera").AddComponent<Camera>();
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = main.farClipPlane;
            cam.fieldOfView = 50f;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            main.enabled = false;
            cam.tag = "MainCamera";
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);

            for (int i = 0; i < 90; i++) yield return null;

            CarController car = null;
            float best = float.MaxValue;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (c.Driven || c.Autopilot || c.HalfExtents.z < 1.9f || c.HalfExtents.x < 0.75f) continue;
                if (c.HalfExtents.z < best) { best = c.HalfExtents.z; car = c; }
            }
            if (car == null)
            {
                Debug.Log("[Lab] no parked car to drive");
                Application.Quit();
                yield break;
            }
            var body = car.GetComponent<Rigidbody>();
            Debug.Log($"[Lab] driving {car.name}, road {a} -> {b}, heading {heading:F1}");

            var labels = new StreamWriter(Path.Combine(dir, "labels.txt"));
            var stats = new StreamWriter(Path.Combine(dir, "stats.csv"));
            stats.WriteLine("frame,pedestrians,drawn,drawCalls,aiMs,renderMs");
            int frame = 0;
            var used = new HashSet<Pedestrian>();

            var takes = new List<(bool dive, float speed)>();
            foreach (var s in HitSpeeds) takes.Add((false, s));
            foreach (var s in DiveSpeeds) takes.Add((true, s));
            for (int t = 0; t < takes.Count; t++)
            {
                var (dive, speed) = takes[t];
                var spot = a + road * (24f + t * Spacing);
                var ped = Pick(system, spot, used);
                if (ped == null)
                {
                    Debug.Log("[Lab] no free pedestrian");
                    break;
                }
                used.Add(ped);
                Stand(ped, spot - side * 0.6f, heading + 90f);

                float lead = dive ? speed * 2.1f : Mathf.Max(speed * 1.1f, 4f);
                float front = car.HalfExtents.z;
                var start = spot - road * (lead + front);
                Move(car, body, start, heading);

                var eye = spot - side * 11f - road * 3f;
                cam.transform.position = new Vector3(eye.x, 5f, eye.y);
                var look = spot + road * 3.5f;
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(look.x, 0.6f, look.y) - cam.transform.position, Vector3.up);
                var player = FindAnyObjectByType<GameController>()?.Player;
                if (player != null && player.isActiveAndEnabled)
                {
                    var behind = eye - (spot - eye).normalized * 4f;
                    player.Teleport(new Vector3(behind.x, 0.3f, behind.y), heading);
                }

                string label = dive ? $"dive, car at {speed:F0} m/s" : $"hit at {speed:F0} m/s ({speed * 3.6f:F0} km/h)";
                int first = frame;
                bool struck = false;
                float travelled = 0f;
                float after = -1f;
                float v = speed;
                Debug.Log($"[Lab] take {t + 1}: {label}");
                while (after < Hold)
                {
                    float dt = Time.deltaTime;
                    travelled += v * dt;
                    Move(car, body, start + road * travelled, heading);
                    car.SetAutopilotMotion(dive ? v : 0f, 0f);
                    if (!dive && !struck && travelled >= lead - 0.3f)
                    {
                        struck = true;
                        ped.OnVehicleImpact(car.gameObject, ped.transform.position + Vector3.up * 0.9f, new Vector3(road.x, 0f, road.y) * speed);
                    }
                    if (travelled >= lead) after = after < 0f ? 0f : after + dt;
                    if (travelled > lead + 14f) v = Mathf.MoveTowards(v, 0f, 30f * dt);

                    yield return new WaitForEndOfFrame();
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.active = null;
                    WritePpm(Path.Combine(dir, $"frame_{frame:D5}.ppm"), texture.GetPixels32());
                    if (frame % 30 == 0)
                        stats.WriteLine(string.Join(",", frame, PedestrianSystem.Count, PedestrianSystem.Drawn, PedestrianSystem.DrawCalls,
                            PedestrianSystem.AiMs.ToString("F3", CultureInfo.InvariantCulture), PedestrianSystem.RenderMs.ToString("F3", CultureInfo.InvariantCulture)));
                    if (frame % 5 == 0) Debug.Log($"[Lab] f{frame} {ped.State} t={ped.StateTime:F2} pos={ped.Pos}");
                    frame++;
                }
                labels.WriteLine($"{first} {frame - 1} {label}");
                labels.Flush();
            }
            labels.Close();
            stats.Close();
            Debug.Log($"[Lab] done: {frame} frames in {dir}");
            Application.Quit();
        }

        static void Move(CarController car, Rigidbody body, Vector2 p, float heading)
        {
            car.Place(new Vector3(p.x, 0.05f, p.y), heading);
            body.position = car.transform.position;
            body.rotation = car.transform.rotation;
            Physics.SyncTransforms();
        }

        static Pedestrian Pick(PedestrianSystem system, Vector2 spot, HashSet<Pedestrian> used)
        {
            Pedestrian pick = null;
            float best = float.MaxValue;
            foreach (var ped in system.Active)
            {
                if (used.Contains(ped) || ped.Role != PedRole.Walker || ped.State != PedState.Walk) continue;
                float d = (ped.Pos - spot).sqrMagnitude;
                if (d < best) { best = d; pick = ped; }
            }
            return pick;
        }

        // Plants someone in the lane facing across the road, idle for as long as the take needs.
        static void Stand(Pedestrian ped, Vector2 spot, float heading)
        {
            if (ped.Registered)
            {
                Yielding.Crossing.Remove(ped);
                ped.Registered = false;
            }
            ped.Crossing = -1;
            ped.Path.Clear();
            ped.Pos = spot;
            ped.Vel = Vector2.zero;
            ped.Heading = heading;
            ped.State = PedState.Idle;
            ped.StateTime = 0f;
            ped.StateLength = 1e6f;
            ped.transform.position = new Vector3(spot.x, 0.03f, spot.y);
            Physics.SyncTransforms();
        }

        void WritePpm(string path, Color32[] pixels)
        {
            var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            var bytes = new byte[header.Length + width * height * 3];
            header.CopyTo(bytes, 0);
            int o = header.Length;
            for (int y = height - 1; y >= 0; y--)
                for (int x = 0; x < width; x++)
                {
                    var c = pixels[y * width + x];
                    bytes[o++] = c.r;
                    bytes[o++] = c.g;
                    bytes[o++] = c.b;
                }
            File.WriteAllBytes(path, bytes);
        }
    }
}
