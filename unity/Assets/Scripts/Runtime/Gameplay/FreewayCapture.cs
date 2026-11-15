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
        bool underpasses;
        Vector2? underAt;
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
            int u = Array.IndexOf(args, "-freewayUnder");
            if ((i < 0 || i + 1 >= args.Length) && (u < 0 || u + 1 >= args.Length)) return;
            var capture = new GameObject("FreewayCapture").AddComponent<FreewayCapture>();
            capture.underpasses = u >= 0;
            capture.output = args[(u >= 0 ? u : i) + 1];
            int a = Array.IndexOf(args, "-underAt");
            if (a >= 0 && a + 1 < args.Length)
            {
                var xz = args[a + 1].Split(',');
                capture.underAt = new Vector2(float.Parse(xz[0], CultureInfo.InvariantCulture), float.Parse(xz[1], CultureInfo.InvariantCulture));
            }
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
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);
            var freeway = game.World.Roads.Freeway;
            if (freeway == null) throw new InvalidOperationException("no freeway graph");
            if (!underpasses) BuildPath(freeway);

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;
            if (parked == null) throw new InvalidOperationException("no parked car");
            game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
            for (int k = 0; k < 3; k++) yield return null;
            car = game.NearestCar ?? parked;
            var enter = typeof(GameController).GetMethod("EnterCarAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)enter.Invoke(game, new object[] { car });
            while (!task.IsCompleted) yield return null;
            var hud = game.GetComponent<Hud>();
            if (underpasses)
            {
                game.View.GetComponent<FollowCamera>().Snap(car.transform, true);
                yield return Underpasses(freeway);
                Application.Quit();
                yield break;
            }
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

        // Every street that passes under a deck or ramp more than 3 m up, driven through in both directions
        // from 35 m before to 35 m after the crossing. A pass that stalls logs what the car is touching.
        // -underAt x,z keeps only crossings within 15 m of that point and drives each three times each way.
        IEnumerator Underpasses(FreewayGraph freeway)
        {
            var roads = game.World.Roads;
            var report = new StringBuilder("street,road,crossing_x,crossing_z,deck_h,direction,result,min_speed,stop_x,stop_z,touching\n");
            var done = new List<Vector2>();
            int passes = 0, blocked = 0;
            for (int r = 0; r < roads.Roads.Length; r++)
            {
                var road = roads.Roads[r];
                if (!RoadNetwork.IsDriveable(road) || road.kind == "service" || road.kind == "track") continue;
                var pts = roads.Points[r];
                for (int i = 0; i + 1 < pts.Length; i++)
                    foreach (var way in freeway.Ways)
                        for (int j = 0; j + 1 < way.Points.Length; j++)
                        {
                            var a = way.Points[j];
                            var b = way.Points[j + 1];
                            if (!Cross(pts[i], pts[i + 1], new Vector2(a.x, a.z), new Vector2(b.x, b.z), out float t, out float k)) continue;
                            float h = Mathf.Lerp(a.y, b.y, k);
                            var at = Vector2.Lerp(pts[i], pts[i + 1], t);
                            if (h < 3f || done.Exists(d => Vector2.Distance(d, at) < 12f) || (underAt.HasValue && Vector2.Distance(underAt.Value, at) > 15f)) continue;
                            done.Add(at);
                            foreach (int dir in underAt.HasValue ? new[] { 1, -1, 1, -1, 1, -1 } : new[] { 1, -1 })
                            {
                                var line = StreetLine(pts, i, t, dir, roads, r);
                                float minSpeed = float.MaxValue;
                                bool reached = false;
                                path.Clear();
                                speedAt.Clear();
                                foreach (var q in line) Add(new Vector3(q.x, RoadSurface.GroundY(q), q.y), 9f);
                                cursor = 0;
                                var heading = path[1] - path[0];
                                car.Place(path[0] + Vector3.up * 0.1f, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
                                driving = true;
                                float started = Time.time, slowSince = -1f;
                                while (Time.time - started < 25f)
                                {
                                    yield return null;
                                    var p = car.transform.position;
                                    if (Time.time - started > 2f) minSpeed = Mathf.Min(minSpeed, car.Speed);
                                    if (Vector2.Distance(new Vector2(p.x, p.z), line[line.Count - 1]) < 5f) { reached = true; break; }
                                    if (Time.time - started > 2f && Mathf.Abs(car.Speed) < 1f) { if (slowSince < 0f) slowSince = Time.time; else if (Time.time - slowSince > 2f) break; }
                                    else slowSince = -1f;
                                }
                                driving = false;
                                car.SetInput(Vector3.zero, 0f);
                                var stop = car.transform.position;
                                string touching = reached ? "" : Touching();
                                passes++;
                                if (!reached)
                                {
                                    blocked++;
                                    yield return new WaitForEndOfFrame();
                                    Save(Path.Combine(output, $"blocked_{blocked:D2}.ppm"));
                                }
                                Debug.Log($"[FreewayCapture] pass {road.name} at ({at.x:F0},{at.y:F0}) {(dir > 0 ? "forward" : "backward")}: {(reached ? "through" : "BLOCKED at " + stop + " touching " + touching)}");
                                report.Append(string.Format(CultureInfo.InvariantCulture, "\"{0}\",{1},{2:F1},{3:F1},{4:F1},{5},{6},{7:F1},{8:F1},{9:F1},\"{10}\"\n",
                                    road.name, road.osmId, at.x, at.y, h, dir > 0 ? "forward" : "backward", reached ? "through" : "BLOCKED", minSpeed, stop.x, stop.z, touching));
                            }
                        }
            }
            File.WriteAllText(Path.Combine(output, "underpasses.csv"), report.ToString());
            Debug.Log($"[FreewayCapture] underpasses: {passes} passes, {blocked} blocked");
        }

        static bool Cross(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out float t, out float k)
        {
            t = k = 0f;
            var d1 = p2 - p1;
            var d2 = p4 - p3;
            float den = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            var w = p3 - p1;
            t = (w.x * d2.y - w.y * d2.x) / den;
            k = (w.x * d1.y - w.y * d1.x) / den;
            return t >= 0f && t <= 1f && k >= 0f && k <= 1f;
        }

        // 35 m of the street either side of the crossing, in the right-hand lane for the travel direction,
        // continuing into the next road of the same name when the way ends.
        static List<Vector2> StreetLine(Vector2[] pts, int i, float t, int dir, RoadNetwork roads, int road)
        {
            var poly = new List<Vector2>(pts);
            var at = Vector2.Lerp(pts[i], pts[i + 1], t);
            if (dir < 0) { poly.Reverse(); i = pts.Length - 2 - i; }
            var arc = new List<float> { 0f };
            for (int n = 1; n < poly.Count; n++) arc.Add(arc[n - 1] + Vector2.Distance(poly[n - 1], poly[n]));
            float sAt = arc[i] + Vector2.Distance(poly[i], at);
            var result = new List<Vector2>();
            float offset = RoadNetwork.Lanes(roads.Roads[road]).Width * 0.5f;
            for (float s = sAt - 35f; s <= sAt + 35f; s += 3f)
            {
                float c = Mathf.Clamp(s, 0f, arc[arc.Count - 1]);
                int n = 1;
                while (n < poly.Count - 1 && arc[n] < c) n++;
                var d = (poly[n] - poly[n - 1]).normalized;
                var q = Vector2.Lerp(poly[n - 1], poly[n], Mathf.InverseLerp(arc[n - 1], arc[n], c)) + d * (s - c);
                result.Add(q + new Vector2(d.y, -d.x) * (roads.Roads[road].oneway ? 0f : offset));
            }
            return result;
        }

        string Touching()
        {
            var box = car.GetComponent<BoxCollider>();
            var hits = Physics.OverlapBox(car.transform.TransformPoint(box.center), box.size * 0.5f + Vector3.one * 0.4f, car.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            var names = new List<string>();
            foreach (var h in hits)
                if (!h.transform.IsChildOf(car.transform) && !names.Contains(h.name)) names.Add(h.name + "@" + h.gameObject.layer);
            return string.Join(" | ", names);
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
            var roads = game.World.Roads;
            var at = new Vector2(end.x, end.z);
            float walked = 0f;
            for (int guard = 0; guard < 8 && walked < 110f; guard++)
            {
                int road = -1, vertex = -1;
                for (int r = 0; r < roads.Roads.Length && road < 0; r++)
                {
                    if (roads.Roads[r].name != "Mariposa Street") continue;
                    var pts = roads.Points[r];
                    for (int k = 0; k < pts.Length; k++)
                        if (Vector2.Distance(pts[k], at) < 6f && (k > 0 && pts[k - 1].x < at.x - 1f || k < pts.Length - 1 && pts[k + 1].x < at.x - 1f)) { road = r; vertex = k; break; }
                }
                if (road < 0) break;
                var line = roads.Points[road];
                int step = vertex > 0 && line[vertex - 1].x < at.x - 1f ? -1 : 1;
                for (int k = vertex + step; k >= 0 && k < line.Length && walked < 110f; k += step)
                {
                    var from = line[k - step];
                    float len = Vector2.Distance(from, line[k]);
                    var dir = (line[k] - from) / Mathf.Max(len, 1e-3f);
                    for (float s = 6f; s <= len; s += 6f)
                    {
                        var q = from + dir * s + new Vector2(dir.y, -dir.x) * 1.8f;
                        Add(new Vector3(q.x, RoadSurface.GroundY(q), q.y), 9f);
                    }
                    walked += len;
                    at = line[k];
                }
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
