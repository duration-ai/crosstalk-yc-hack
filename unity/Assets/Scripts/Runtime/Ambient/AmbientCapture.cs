using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Ambient
{
    // Headless verification of the pedestrians. Only active with -ambientCapture <dir>: it centres
    // the crowd on -captureAt x,z,height,yaw,pitch, steps the game at a fixed 30 fps, writes
    // -captureFrames PPM frames from a camera there and a stats.csv, then quits. Optional
    // -captureCar x,z,heading,throttle,firstFrame,lastFrame drives a parked car along a line so the
    // dive and tumble reactions can be filmed. -ambientCount n overrides the crowd size.
    public sealed class AmbientCapture : MonoBehaviour
    {
        string dir;
        float[] at = { 0f, 0f, 6f, 0f, 18f };
        float[] car;
        int frames = 240;
        int warmup = 120;
        int width = 1280, height = 720;
        int count = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var dir = Arg("-ambientCapture");
            if (dir == null) return;
            var capture = new GameObject("AmbientCapture").AddComponent<AmbientCapture>();
            capture.dir = dir;
            if (Arg("-captureAt") is string a) capture.at = Floats(a);
            if (Arg("-captureCar") is string c) capture.car = Floats(c);
            if (Arg("-captureFrames") is string f) capture.frames = int.Parse(f, CultureInfo.InvariantCulture);
            if (Arg("-captureWarmup") is string w) capture.warmup = int.Parse(w, CultureInfo.InvariantCulture);
            if (Arg("-ambientCount") is string n) capture.count = int.Parse(n, CultureInfo.InvariantCulture);
        }

        static float[] Floats(string csv) => Array.ConvertAll(csv.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            Time.captureFramerate = 30;
            while (PedestrianSystem.Instance == null || Camera.main == null) yield return null;
            var system = PedestrianSystem.Instance;
            if (count > 0) system.TargetCount = count;
            DumpGraph(system.Graph);
            PedestrianSystem.FocusOverride = new Vector3(at[0], 0f, at[1]);
            PedestrianSystem.ShowStats = true;

            var main = Camera.main;
            var cam = new GameObject("Capture Camera").AddComponent<Camera>();
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.fieldOfView = 55f;
            cam.transform.SetPositionAndRotation(new Vector3(at[0], at[2], at[1]), Quaternion.Euler(at[4], at[3], 0f));
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            main.enabled = false;
            cam.tag = "MainCamera";
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);

            CarController driven = null;
            var stats = new StreamWriter(Path.Combine(dir, "stats.csv"));
            stats.WriteLine("frame,pedestrians,within50m,drawn,drawCalls,aiMs,renderMs");
            for (int f = -warmup; f < frames; f++)
            {
                if (car != null && f == (int)car[4]) driven = Drive();
                if (driven != null && f == (int)car[5]) driven.SetInput(Vector3.zero, 0f);
                if (driven != null && f < (int)car[5]) Aim(driven);
                if (driven != null)
                {
                    var ct = driven.transform;
                    var back = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
                    cam.transform.SetPositionAndRotation(ct.position - back * 10f + Vector3.up * 4.5f, Quaternion.LookRotation(back, Vector3.up) * Quaternion.Euler(14f, 0f, 0f));
                }
                if (driven != null && f % 10 == 0) Debug.Log($"[Ambient] capture: frame {f} car at {driven.transform.position} speed {driven.Speed:F1} reacting {Reacting()}");
                yield return new WaitForEndOfFrame();
                if (f < 0) continue;
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;
                WritePpm(Path.Combine(dir, $"frame_{f:D4}.ppm"), texture.GetPixels32());
                if (f % 30 == 0)
                    stats.WriteLine(string.Join(",", f, PedestrianSystem.Count, PedestrianSystem.Near, PedestrianSystem.Drawn, PedestrianSystem.DrawCalls,
                        PedestrianSystem.AiMs.ToString("F3", CultureInfo.InvariantCulture), PedestrianSystem.RenderMs.ToString("F3", CultureInfo.InvariantCulture)));
            }
            stats.Close();
            Debug.Log("[Ambient] capture done: " + frames + " frames in " + dir);
            Application.Quit();
        }

        // The walk graph as text (node lines, edge lines, crossing lines) for plotting outside Unity.
        void DumpGraph(PedestrianGraph graph)
        {
            using var w = new StreamWriter(Path.Combine(dir, "graph.txt"));
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < graph.Nodes.Count; i++)
                w.WriteLine(string.Format(inv, "n {0:F1} {1:F1} {2}", graph.Nodes[i].x, graph.Nodes[i].y, (int)graph.Kinds[i]));
            for (int i = 0; i < graph.Nodes.Count; i++)
                foreach (var e in graph.Edges[i])
                    if (e.To > i) w.WriteLine(string.Format(inv, "e {0} {1} {2}", i, e.To, (int)e.Kind));
            foreach (var c in graph.Crossings)
                w.WriteLine(string.Format(inv, "c {0:F1} {1:F1} {2:F1} {3:F1} {4} {5}", c.A.x, c.A.y, c.B.x, c.B.y, c.Signal ? 1 : 0, c.FromData ? 1 : 0));
            foreach (var s in graph.Spots)
                w.WriteLine(string.Format(inv, "s {0:F1} {1:F1} {2}", s.Position.x, s.Position.y, (int)s.Kind));
        }

        // Binary PPM, top row first (Unity's pixels start at the bottom); ffmpeg reads it directly.
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

        // Steers the filmed car at the nearest pedestrian ahead of it, so every take has an encounter.
        void Aim(CarController driven)
        {
            var t = driven.transform;
            var p = new Vector2(t.position.x, t.position.z);
            var fwd = new Vector2(t.forward.x, t.forward.z);
            Pedestrian target = null;
            float best = 40f;
            foreach (var ped in PedestrianSystem.Instance.Active)
            {
                var to = ped.Pos - p;
                float d = to.magnitude;
                if (d < 0.1f || d > best || Vector2.Dot(to / d, fwd) < 0.82f) continue;
                if (d < 15f) return;
                if (ped.State != PedState.Walk) continue;
                best = d;
                target = ped;
            }
            if (target == null) return;
            var dir = target.Pos - p;
            driven.SetInput(new Vector3(dir.x, 0f, dir.y).normalized, car[3]);
        }

        static string Reacting()
        {
            int dive = 0, tumble = 0;
            foreach (var ped in PedestrianSystem.Instance.Active)
            {
                if (ped.State == PedState.Dive)
                {
                    dive++;
                    Debug.Log($"[Ambient] capture: diver {ped.Pos} t={ped.StateTime:F2} phase={ped.Phase} pitch={ped.Pitch:F0} from {ped.ThreatFrom}");
                }
                if (ped.State == PedState.Tumble || ped.State == PedState.Down || ped.State == PedState.GetUp) tumble++;
            }
            return $"dive={dive} tumble={tumble}";
        }

        CarController Drive()
        {
            CarController best = null;
            var start = new Vector3(car[0], 0.05f, car[1]);
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (best == null || (c.transform.position - start).sqrMagnitude < (best.transform.position - start).sqrMagnitude) best = c;
            if (best == null)
            {
                Debug.Log("[Ambient] capture: no car to drive");
                return null;
            }
            Debug.Log($"[Ambient] capture: driving {best.name} from {best.transform.position} to {start}");
            best.Place(start, car[2]);
            var body = best.GetComponent<Rigidbody>();
            body.position = start;
            body.rotation = Quaternion.Euler(0f, car[2], 0f);
            Physics.SyncTransforms();
            best.SetDriven(true);
            best.SetInput(Quaternion.Euler(0f, car[2], 0f) * Vector3.forward, car[3]);
            return best;
        }
    }
}
