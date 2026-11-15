using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Headless freeway drive for review, inert unless the player is launched with
    //   -freewayCapture <dir> [-captureEvery N]
    // Takes the hatchback, drives it up the Indiana St on-ramp, north along the I-280 deck, down the
    // Mariposa St off-ramp and west along Mariposa under the southbound deck, steering by the freeway
    // graph through the normal CarController input, with the game's own follow camera. Writes numbered
    // PPM frames, stills from above and below the deck and a CSV trace, then quits.
    public sealed class FreewayCapture : MonoBehaviour
    {
        static readonly long[] Route = { 681178980, 48570001, 32930401, 48570004, 8916043, 513698886, 394691477, 658558315 };

        string output;
        int every;
        GameController game;
        CarController car;
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> speedAt = new List<float>();
        readonly StringBuilder trace = new StringBuilder("t,x,y,z,speed_ms,street,on_deck\n");
        int cursor;
        bool driving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-freewayCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var capture = new GameObject("FreewayCapture").AddComponent<FreewayCapture>();
            capture.output = args[i + 1];
            int j = Array.IndexOf(args, "-captureEvery");
            capture.every = j >= 0 && j + 1 < args.Length && int.TryParse(args[j + 1], out int n) ? n : 2;
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            for (int k = 0; k < 10; k++) yield return null;
            var freeway = game.World.Roads.Freeway;
            if (freeway == null) throw new InvalidOperationException("no freeway graph");
            BuildPath(freeway);

            car = game.NearestCar;
            if (car == null) throw new InvalidOperationException("no car near the player");
            var enter = typeof(GameController).GetMethod("EnterCarAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)enter.Invoke(game, new object[] { car });
            while (!task.IsCompleted) yield return null;
            var hud = game.GetComponent<Hud>();
            var start = path[0];
            var heading = path[1] - path[0];
            car.Place(start + Vector3.up * 0.3f, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
            var follow = game.View.GetComponent<FollowCamera>();
            follow.Snap(car.transform, true);
            yield return null;
            driving = true;

            int frame = 0;
            float started = Time.time;
            while (cursor < path.Count - 3 && Time.time - started < 240f)
            {
                yield return new WaitForEndOfFrame();
                if (frame % every == 0) Save(Path.Combine(output, $"frame_{frame / every:D5}.ppm"));
                if (frame % 15 == 0) Note(frame / 30f);
                frame++;
            }
            driving = false;
            car.SetInput(Vector3.zero, 0f);
            File.WriteAllText(Path.Combine(output, "trace.csv"), trace.ToString());
            Debug.Log($"[FreewayCapture] drive done in {Time.time - started:F1}s, {frame} frames, reached {cursor}/{path.Count}");

            follow.enabled = false;
            if (hud != null) hud.enabled = false;
            var cam = game.View;
            foreach (var (name, eye, look) in Stills())
            {
                cam.transform.position = eye;
                cam.transform.LookAt(look);
                cam.fieldOfView = 60f;
                for (int k = 0; k < 4; k++) yield return null;
                yield return new WaitForEndOfFrame();
                Save(Path.Combine(output, name + ".ppm"));
            }
            Application.Quit();
        }

        static IEnumerable<(string, Vector3, Vector3)> Stills()
        {
            yield return ("still_above_interchange", new Vector3(-250f, 110f, 330f), new Vector3(-365f, 10f, 520f));
            yield return ("still_above_south_ramps", new Vector3(-230f, 95f, -250f), new Vector3(-330f, 8f, -380f));
            yield return ("still_below_20th_st", new Vector3(-300f, 1.7f, 362f), new Vector3(-360f, 13f, 366f));
            yield return ("still_below_mariposa", new Vector3(-380f, 1.7f, 790f), new Vector3(-430f, 12f, 784f));
            yield return ("still_ramp_indiana", new Vector3(-200f, 9f, -490f), new Vector3(-245f, 4f, -400f));
        }

        void BuildPath(FreewayGraph freeway)
        {
            var ways = new List<FreewayGraph.Way>();
            foreach (long id in Route)
                foreach (var w in freeway.Ways)
                    if (w.OsmId == id) { ways.Add(w); break; }
            var first = ways[0].Points[0];
            var into = (ways[0].Points[1] - first).normalized;
            for (int k = 6; k >= 1; k--) Add(first + new Vector3(0f, 0f, -12f * k) + new Vector3(into.x, 0f, 0f) * 0f, 9f);
            foreach (var w in ways)
            {
                var lane = freeway.Lane(w.Index, 0);
                foreach (var p in lane) Add(p, w.Ramp ? 14f : 22f);
            }
            var end = path[path.Count - 1];
            var net = freeway;
            var roads = game.World.Roads;
            for (int r = 0; r < roads.Roads.Length; r++)
            {
                if (roads.Roads[r].name != "Mariposa Street") continue;
                var pts = roads.Points[r];
                if (Vector2.Distance(pts[0], new Vector2(end.x, end.z)) > 3f && Vector2.Distance(pts[pts.Length - 1], new Vector2(end.x, end.z)) > 3f) continue;
                var far = Vector2.Distance(pts[0], new Vector2(end.x, end.z)) < 3f ? pts[pts.Length - 1] : pts[0];
                if (far.x > end.x) continue;
                var dir = (far - new Vector2(end.x, end.z)).normalized;
                for (float s = 8f; s <= 110f; s += 6f)
                {
                    var q = new Vector2(end.x, end.z) + dir * s + new Vector2(dir.y, -dir.x) * 1.8f;
                    Add(new Vector3(q.x, 0f, q.y), 9f);
                }
                break;
            }
        }

        void Add(Vector3 p, float speed)
        {
            if (path.Count > 0 && Vector3.Distance(path[path.Count - 1], p) < 2f) return;
            path.Add(p);
            speedAt.Add(speed);
        }

        void LateUpdate()
        {
            if (!driving || car == null) return;
            var pos = car.transform.position;
            float best = float.MaxValue;
            for (int i = cursor; i < Mathf.Min(cursor + 20, path.Count); i++)
            {
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(path[i].x, path[i].z));
                if (d < best) { best = d; cursor = i; }
            }
            float look = Mathf.Clamp(Mathf.Abs(car.Speed) * 0.7f, 8f, 18f);
            int target = cursor;
            float walked = 0f;
            while (target < path.Count - 1 && walked < look)
            {
                walked += Vector3.Distance(path[target], path[target + 1]);
                target++;
            }
            var to = path[target] - pos;
            to.y = 0f;
            float wanted = speedAt[cursor];
            float strength = Mathf.Clamp01(wanted / Mathf.Max(car.Handling.TopSpeed, 1f));
            car.SetInput(to.normalized, strength);
        }

        void Note(float t)
        {
            var p = car.transform.position;
            bool onDeck = game.World.Roads.Freeway.OnDeck(p, out _);
            trace.Append(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},\"{5}\",{6}\n", t, p.x, p.y, p.z, car.Speed, game.StreetName, onDeck ? 1 : 0));
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
