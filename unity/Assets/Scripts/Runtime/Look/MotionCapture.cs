using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.WorldChanges;
using Unity.Profiling;
using UnityEngine;

namespace Dogpatch.Look
{
    // Headless motion-feel drive for review, inert unless the player is launched with
    //   -motionCapture <dir>
    // Takes the nearest parked car to the longest straight street run and drives it through the
    // normal CarController input: a standing launch, top speed, a boost, a launch ramp (jump and
    // landing), an S-shaped drift and a scrape along a barrier it drops beside the lane. Writes a PPM
    // per frame at 30 fps, a CSV trace with the motion signals and draw calls, then quits.
    public sealed class MotionCapture : MonoBehaviour
    {
        string output;
        GameController game;
        CarController car;
        Vector3 heading, runStart;
        float steerOffset;
        float throttle;
        bool driving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-motionCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var capture = new GameObject("MotionCapture").AddComponent<MotionCapture>();
            capture.output = args[i + 1];
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            for (int k = 0; k < 10; k++) yield return null;
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);

            var (start, dir, length) = LongestRun();
            Debug.Log($"[MotionCapture] run at {start} heading {dir} length {length:F0} m");

            foreach (var hud in FindObjectsByType<Operator.OperatorHud>(FindObjectsSortMode.None)) hud.DebugInput = false;

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && c.HalfExtents.z < 2.6f && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;
            if (parked == null) throw new InvalidOperationException("no parked car");
            game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
            for (int k = 0; k < 3; k++) yield return null;
            car = game.NearestCar ?? parked;
            var enter = typeof(GameController).GetMethod("EnterCarAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)enter.Invoke(game, new object[] { car });
            while (!task.IsCompleted) yield return null;

            ClearLane(start, dir, length);
            heading = dir;
            runStart = start;
            car.Place(start + Vector3.up * 0.3f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            game.View.GetComponent<FollowCamera>().Snap(car.transform, true);
            for (int k = 0; k < 20; k++) yield return null;
            driving = true;

            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var trace = new StringBuilder("frame,t,phase,speed_ms,y,speed_factor,rush,boost,lateral,fov,draw_calls\n");
            var material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor"));
            int frame = 0;
            string phase = "";
            bool boosted = false, ramped = false, drifted = false, barrier = false;
            while (frame < 30 * 17)
            {
                float t = frame / 30f;
                throttle = t < 0.6f ? 0f : 1f;
                steerOffset = 0f;
                if (t < 0.6f) phase = "idle";
                else if (t < 4.5f) phase = "launch";
                else if (t < 6f) phase = "top_speed";
                else if (t < 8.5f) phase = "boost";
                else if (t < 10.5f) phase = "jump";
                else if (t < 13f) phase = "drift";
                else phase = "scrape";

                if (phase == "boost" && !boosted)
                {
                    boosted = true;
                    MotionFx.Boost(1f);
                }
                if (t >= 7.2f && !ramped)
                {
                    ramped = true;
                    var at = car.transform.position + heading * (Mathf.Abs(car.Speed) * 1.6f);
                    at.y = 0f;
                    Ramp.Create(null, at, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg, material);
                }
                if (frame % 15 == 0) ClearTraffic();
                if (phase == "drift" && !drifted)
                {
                    drifted = true;
                    Relaunch(20f);
                }
                if (phase == "drift")
                {
                    float d = t - 10.5f;
                    steerOffset = d < 0.3f ? 0f : d < 1f ? -35f : d < 1.8f ? 35f : 0f;
                }
                if (phase == "scrape" && !barrier)
                {
                    barrier = true;
                    Relaunch(17f);
                    var right = Vector3.Cross(Vector3.up, heading);
                    var lane = runStart + heading * Vector3.Dot(car.transform.position - runStart, heading);
                    Barrier(lane + heading * 6f + right * (car.HalfExtents.x + 0.8f), heading, material);
                }
                if (phase == "scrape") steerOffset = 12f;

                yield return new WaitForEndOfFrame();
                Save(Path.Combine(output, $"frame_{frame:D5}.ppm"));
                trace.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2},{3:F2},{4:F2},{5:F3},{6:F3},{7:F3},{8:F2},{9:F1},{10}\n",
                    frame, t, phase, car.Speed, car.transform.position.y, MotionFx.SpeedFactor, MotionFx.Rush, MotionFx.BoostFactor, MotionFx.Lateral, game.View.fieldOfView, draws.Valid ? draws.LastValue : -1));
                frame++;
            }
            driving = false;
            draws.Dispose();
            File.WriteAllText(Path.Combine(output, "trace.csv"), trace.ToString());
            Debug.Log($"[MotionCapture] done, {frame} frames");
            Application.Quit();
        }

        // Parked and passing cars sitting on the drive line would end the run early, so the capture
        // parks them out of the way.
        void ClearLane(Vector3 start, Vector3 dir, float length)
        {
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (c == car) continue;
                var d = c.transform.position - start;
                float along = Vector3.Dot(d, dir);
                if (along < -20f || along > length + 50f) continue;
                if (Vector3.Cross(dir, d).magnitude < 3.5f) c.gameObject.SetActive(false);
            }
            foreach (var agent in FindObjectsByType<Traffic.TrafficAgent>(FindObjectsSortMode.None))
                if (Vector3.Distance(agent.transform.position, start) < length + 50f) agent.gameObject.SetActive(false);
        }

        // Puts the car back on the run line where it is along it, at a given speed, so each maneuver
        // starts clean.
        void Relaunch(float speed)
        {
            float along = Vector3.Dot(car.transform.position - runStart, heading);
            car.Place(runStart + heading * along + Vector3.up * 0.3f, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
            car.GetComponent<Rigidbody>().linearVelocity = heading * speed;
        }

        // Moving traffic would crash into the scripted maneuvers, so the capture parks it.
        void ClearTraffic()
        {
            foreach (var agent in FindObjectsByType<Traffic.TrafficAgent>(FindObjectsSortMode.None)) agent.gameObject.SetActive(false);
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c != car && (c.Autopilot || c.Driven)) c.gameObject.SetActive(false);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        // A capture-only concrete barrier, angled a few degrees into the lane so the car grinds along it.
        static void Barrier(Vector3 nearEnd, Vector3 along, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Capture barrier";
            var mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = SpeedDial.Hex("#EEE7DA").linear;
            mesh.colors = colors;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var rotation = Quaternion.LookRotation(Quaternion.Euler(0f, -2f, 0f) * along);
            go.transform.SetPositionAndRotation(nearEnd + rotation * Vector3.forward * 20f + Vector3.up * 0.5f, rotation);
            go.transform.localScale = new Vector3(0.6f, 1f, 40f);
        }

        void LateUpdate()
        {
            if (!driving || car == null) return;
            car.SetInput(Quaternion.Euler(0f, steerOffset, 0f) * heading, throttle);
        }

        // The longest run of road centreline whose direction stays within a few degrees.
        (Vector3, Vector3, float) LongestRun()
        {
            var roads = game.World.Roads;
            Vector3 bestStart = Vector3.zero, bestDir = Vector3.forward;
            float best = 0f;
            for (int r = 0; r < roads.Points.Length; r++)
            {
                var pts = roads.Points[r];
                if (pts == null || pts.Length < 2) continue;
                int from = 0;
                float run = 0f;
                for (int i = 1; i < pts.Length; i++)
                {
                    var d0 = (pts[from + 1] - pts[from]).normalized;
                    var d = (pts[i] - pts[i - 1]).normalized;
                    if (Vector2.Angle(d0, d) > 4f)
                    {
                        from = i - 1;
                        run = 0f;
                    }
                    run += Vector2.Distance(pts[i - 1], pts[i]);
                    if (run <= best) continue;
                    best = run;
                    var dir = (pts[i] - pts[from]).normalized;
                    bestDir = new Vector3(dir.x, 0f, dir.y);
                    bestStart = new Vector3(pts[from].x, 0f, pts[from].y) + Vector3.Cross(Vector3.up, bestDir) * 1.2f;
                }
            }
            return (bestStart, bestDir, best);
        }

        static void Save(string path)
        {
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            var raw = tex.GetRawTextureData();
            int w = tex.width, h = tex.height, row = w * 3;
            var header = Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
            var bytes = new byte[header.Length + raw.Length];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, (h - 1 - y) * row, bytes, header.Length + y * row, row);
            File.WriteAllBytes(path, bytes);
            Destroy(tex);
        }
    }
}
