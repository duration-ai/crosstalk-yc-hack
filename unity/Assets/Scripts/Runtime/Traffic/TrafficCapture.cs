using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // Headless recordings for review, inert unless the player is launched with
    //   -dogpatchCapture stop|tthird|perf -captureOut <dir> [-captureFrames N]
    // It fixes the game clock at 30 fps (Time.captureFramerate), stages the scene, points its own
    // camera, writes numbered PNG frames and a log of what the subject was doing, then quits.
    public sealed class TrafficCapture : MonoBehaviour
    {
        string scenario;
        string output;
        int frames;
        GameController game;
        TrafficAgent subject;
        StreetGraph.Lane lane;
        Camera cam;
        readonly StringBuilder log = new StringBuilder();
        string overlay = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string scenario = Arg("-dogpatchCapture");
            if (scenario == null) return;
            var capture = new GameObject("TrafficCapture").AddComponent<TrafficCapture>();
            capture.scenario = scenario;
            capture.output = Arg("-captureOut") ?? "capture";
            capture.frames = int.TryParse(Arg("-captureFrames"), out int n) ? n : scenario == "tthird" ? 900 : scenario == "perf" ? 1800 : 360;
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while (TrafficSystem.Instance == null) yield return null;
            game = FindAnyObjectByType<GameController>();
            cam = game.View;
            var hud = game.GetComponent<Hud>();
            if (hud != null && scenario != "perf") hud.enabled = false;
            yield return null;

            if (scenario == "zone") StageZone();
            if (scenario == "stop") StageStop();
            else if (scenario == "tthird") StageTrain();
            if (scenario != "perf") cam.GetComponent<FollowCamera>().enabled = false;

            for (int f = 0; f < frames; f++)
            {
                yield return new WaitForEndOfFrame();
                bool save = scenario != "perf" || f % 150 == 0;
                if (save)
                {
                    var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                    tex.Apply();
                    WritePpm(Path.Combine(output, $"frame_{f:D4}.ppm"), tex);
                    Destroy(tex);
                }
                if (f % 15 == 0) Note(f);
            }
            var system = TrafficSystem.Instance;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[Capture] done: agents={0} traffic cost avg={1:F3} ms/frame (last 5 s window) last frame={2:F3} ms", system.Agents.Count, system.AverageMs, system.FrameMs));
            File.WriteAllText(Path.Combine(output, "capture.log"), log.ToString());
            Debug.Log(log.ToString());
            Application.Quit();
        }

        // Binary PPM, top row first, so no image-encoding module ships with the game.
        static void WritePpm(string path, Texture2D tex)
        {
            var raw = tex.GetRawTextureData();
            int w = tex.width, h = tex.height, row = w * 3;
            var header = Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
            var bytes = new byte[header.Length + raw.Length];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, (h - 1 - y) * row, bytes, header.Length + y * row, row);
            File.WriteAllBytes(path, bytes);
        }

        void Note(int frame)
        {
            var system = TrafficSystem.Instance;
            string line = subject != null
                ? string.Format(CultureInfo.InvariantCulture, "t={0:F1}s {1} speed={2:F2} m/s state={3}", frame / 30f, subject.Spec.Title, subject.Speed, subject.State)
                : string.Format(CultureInfo.InvariantCulture, "t={0:F1}s agents={1} traffic={2:F3} ms", frame / 30f, system.Agents.Count, system.FrameMs);
            if (scenario == "zone")
            {
                int inside = 0, holding = 0, hidden = 0;
                foreach (var a in system.Agents)
                {
                    var p = a.transform.position;
                    if (!a.gameObject.activeSelf) hidden++;
                    else if (TrafficSystem.InZone(new Vector2(p.x, p.z))) inside++;
                    if (a.State == "holding for closed road") holding++;
                }
                line += string.Format(CultureInfo.InvariantCulture, " zoneOpen={0} inside={1} holding={2} hidden={3}", TrafficSystem.HasZones, inside, holding, hidden);
            }
            if (subject != null && lane != null) line += string.Format(CultureInfo.InvariantCulture, " to stop line={0:F1} m", lane.Path.Length - subject.S - subject.Car.HalfExtents.z - 0.6f);
            log.AppendLine(line);
            overlay = line;
        }

        // A 60 m zone around the player closed to traffic for 10 s, filmed from above.
        void StageZone()
        {
            var focus = game.Focus.position;
            int id = TrafficSystem.ClearZone(focus, 60f, 10f);
            cam.GetComponent<FollowCamera>().enabled = false;
            cam.transform.position = focus + new Vector3(0f, 110f, -60f);
            cam.transform.LookAt(focus);
            cam.fieldOfView = 60f;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[Capture] zone scenario: zone {0}, 60 m around ({1:F1},{2:F1}) for 10 s", id, focus.x, focus.z));
        }

        // A robotaxi SUV 48 m before the stop-controlled junction nearest the player, filmed from the corner.
        void StageStop()
        {
            var system = TrafficSystem.Instance;
            var spawn = game.Focus != null ? game.Focus.position : game.World.PlayerSpawn.Position;
            float best = float.MaxValue;
            foreach (var l in system.Graph.Lanes)
            {
                if (!l.StopAtEnd || l.To.Control != NodeControl.Stop || l.Path.Length < 55f || l.Index != l.Count - 1) continue;
                var end = l.Path.Points[l.Path.Points.Length - 1];
                float d = Vector2.Distance(end, new Vector2(spawn.x, spawn.z));
                if (d < best)
                {
                    best = d;
                    lane = l;
                }
            }
            if (lane == null) throw new InvalidOperationException("no stop-controlled lane found");
            var e = lane.Path.Points[lane.Path.Points.Length - 1];
            foreach (var a in system.Agents.ToArray())
                if (!a.OnRail && a.Route == null && Vector2.Distance(new Vector2(a.transform.position.x, a.transform.position.z), e) < 90f) system.Despawn(a);
            subject = system.Spawn(VehicleTypes.WaymoSuv, lane, lane.Path.Length - 48f, 8.5f);

            lane.Path.Sample(lane.Path.Length, out var dir);
            var right = new Vector2(dir.y, -dir.x);
            var d3 = new Vector3(dir.x, 0f, dir.y);
            var r3 = new Vector3(right.x, 0f, right.y);
            var end3 = new Vector3(e.x, 0f, e.y);
            if (!HasSignData()) Sign(end3 + r3 * (lane.Width * 0.5f + 1.8f) - d3 * 1.2f, -d3);
            cam.transform.position = end3 + r3 * 10f + d3 * 9f + Vector3.up * 6f;
            cam.transform.LookAt(end3 - d3 * 14f + Vector3.up * 0.8f);
            cam.fieldOfView = 55f;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[Capture] stop scenario: lane {0} on {1}, junction at ({2:F1},{3:F1}), robotaxi starts 48 m back at 8.5 m/s", lane.Id, system.Graph.Roads[lane.Road].name, e.x, e.y));
        }

        static bool HasSignData()
        {
            var world = TrafficWorld.Load();
            foreach (var p in world.Props("stop_sign")) return true;
            return false;
        }

        // The extract this build carries has no stop-sign models (they come with CROS-19's world), so
        // the recording marks the controlled approach with a plain octagon on a pole.
        void Sign(Vector3 at, Vector3 facing)
        {
            var lp = new LowPoly();
            lp.Box(new Vector3(0f, 1.1f, 0f), new Vector3(0.07f, 2.2f, 0.07f), LowPoly.Hex("#8d969e"));
            for (int i = 0; i < 8; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f;
                var c = new Vector3(0f, 2.45f, 0.05f);
                lp.Tri(c, c + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.38f, c + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.38f, LowPoly.Hex("#d63b2b"));
                lp.Tri(c + Vector3.forward * 0.001f, c + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.42f, c + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.42f, LowPoly.Hex("#f7f8f9"));
            }
            var go = new GameObject("stop sign (capture marker)");
            go.AddComponent<MeshFilter>().sharedMesh = lp.ToMesh("stop sign");
            go.AddComponent<MeshRenderer>().sharedMaterial = game.SharedMaterial;
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing, Vector3.up));
        }

        // The northbound T Third 130 m before its next platform, tracked from the side of Third Street.
        void StageTrain()
        {
            foreach (var a in TrafficSystem.Instance.Agents)
                if (a.OnRail && a.RailIndex == 0) subject = a;
            if (subject == null) throw new InvalidOperationException("no T Third train");
            RouteStop next = null;
            foreach (var s in subject.Stops)
                if (s.Index == 0 && s.S > 260f && (next == null || s.S < next.S)) next = s;
            if (next != null) subject.JumpTo(next.S - 130f, 11f);
            log.AppendLine("[Capture] T Third scenario: northbound train, next platform " + (next != null ? next.Name : "none"));
        }

        void LateUpdate()
        {
            if (subject == null || scenario != "tthird" || cam == null) return;
            var t = subject.transform;
            var target = t.position + t.forward * 2f + Vector3.up * 1.5f;
            var want = target + t.right * 24f + Vector3.up * 9f - t.forward * 6f;
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, cam.transform.position.y < 1f ? 1f : 0.2f);
            cam.transform.LookAt(target);
            cam.fieldOfView = 50f;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(overlay) || scenario == "perf") return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10, 10, 1000, 40), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 14, 990, 34), overlay, style);
        }
    }
}
