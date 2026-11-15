using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.Traffic;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Smash.Demo
{
    // Crash run in the real city, inert unless the player is launched with
    //   -crashRun <dir> [-crashCollisions N] [-crashFrameEvery N]
    // The player's car rams AI traffic, parked cars, walls, oncoming cars and small pile-ups,
    // switching vehicle every five attempts, while every car near it has its peak upward speed and
    // height above the road logged, with what it was doing when it took off.
    public sealed class CrashRun : MonoBehaviour
    {
        sealed class Flight
        {
            public string Name;
            public float MaxVy;
            public float MaxHeight;
            public float MaxSpin;
            public string TakeOff;
            public bool WasKinematic;
        }

        static readonly string[] Rides = { VehicleTypes.Hatchback, VehicleTypes.Cybertruck, VehicleTypes.WaymoSuv, VehicleTypes.RaceCar, VehicleTypes.Sedan };
        static readonly string[] Kinds = { "traffic", "parked", "wall", "pileup", "headon" };

        string output;
        int wanted;
        int frameEvery;
        GameController game;
        CarController ram;
        Transform ramTarget;
        Vector3 ramPoint;
        bool ramming;
        bool reversing;
        bool finished;
        int collisions;
        int ramHits;
        readonly Dictionary<CarController, Flight> flights = new Dictionary<CarController, Flight>();
        readonly Dictionary<CarController, (float time, float speed, string what)> lastHit = new Dictionary<CarController, (float, float, string)>();
        readonly Dictionary<string, int> attempts = new Dictionary<string, int>();
        readonly List<CarController> tracked = new List<CarController>();
        CarController[] everyCar = Array.Empty<CarController>();
        float nextCarList;
        float nextTrack;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string dir = Arg("-crashRun");
            if (dir == null) return;
            var run = new GameObject("CrashRun").AddComponent<CrashRun>();
            run.output = dir;
            run.wanted = int.TryParse(Arg("-crashCollisions"), out int n) ? n : 60;
            run.frameEvery = int.TryParse(Arg("-crashFrameEvery"), out int e) ? e : 3;
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while ((game = FindAnyObjectByType<GameController>()) == null || game.Player == null || TrafficSystem.Instance == null) yield return null;
            for (int k = 0; k < 10; k++) yield return null;
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);
            game.ExternalInput = true;

            CarController start = null;
            var at = game.Player.transform.position;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && (start == null || (c.transform.position - at).sqrMagnitude < (start.transform.position - at).sqrMagnitude))
                    start = c;
            game.Player.Teleport(start.transform.position - start.transform.right * (start.HalfExtents.x + 1f), 0f);
            for (int k = 0; k < 3; k++) yield return null;
            var enter = typeof(GameController).GetMethod("EnterCarAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)enter.Invoke(game, new object[] { game.NearestCar ?? start });
            while (!task.IsCompleted) yield return null;
            ram = game.Current;
            VehicleImpacts.Hit += OnHit;
            StartCoroutine(Record());

            float began = Time.time;
            for (int attempt = 0; collisions < wanted && Time.time - began < 300f; attempt++)
            {
                if (attempt % 5 == 0)
                {
                    ram = VehicleRoster.SwapInto(ram, Rides[attempt / 5 % Rides.Length]);
                    yield return new WaitForFixedUpdate();
                }
                var damage = ram.GetComponent<VehicleDamage>();
                if (damage != null && damage.IsWrecked) damage.Repair();
                string kind = Kinds[attempt % Kinds.Length];
                if (!Aim(kind)) continue;
                attempts.TryGetValue(kind, out int count);
                attempts[kind] = count + 1;
                int before = ramHits;
                ramming = true;
                float t0 = Time.time;
                while (Time.time - t0 < 7f && ramHits == before) yield return new WaitForFixedUpdate();
                ramming = false;
                yield return new WaitForSeconds(0.6f);
                reversing = true;
                yield return new WaitForSeconds(1.2f);
                reversing = false;
                yield return new WaitForSeconds(0.4f);
            }
            for (float t = 0f; t < 3f; t += Time.fixedDeltaTime) yield return new WaitForFixedUpdate();
            VehicleImpacts.Hit -= OnHit;
            finished = true;
        }

        IEnumerator Record()
        {
            var cam = game.View;
            for (int f = 0; !finished; f++)
            {
                yield return new WaitForEndOfFrame();
                if (f % frameEvery != 0) continue;
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();
                var raw = tex.GetRawTextureData();
                int w = tex.width, h = tex.height, row = w * 3;
                var header = Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
                var bytes = new byte[header.Length + raw.Length];
                Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
                for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, (h - 1 - y) * row, bytes, header.Length + y * row, row);
                File.WriteAllBytes(Path.Combine(output, $"frame_{f / frameEvery:D5}.ppm"), bytes);
                Destroy(tex);
            }
            Summarize();
            Application.Quit();
        }

        bool Aim(string kind)
        {
            ramTarget = null;
            var p = ram.transform.position;
            var fwd = ram.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            var system = TrafficSystem.Instance;
            switch (kind)
            {
                case "traffic":
                {
                    TrafficAgent best = null;
                    foreach (var a in system.Agents)
                        if (a.enabled && a.gameObject.activeInHierarchy && !a.OnRail && a.Car != ram && (best == null || (a.transform.position - p).sqrMagnitude < (best.transform.position - p).sqrMagnitude))
                            best = a;
                    if (best == null || (best.transform.position - p).sqrMagnitude > 150f * 150f) return false;
                    ramTarget = best.transform;
                    return true;
                }
                case "parked":
                {
                    CarController best = null;
                    foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                    {
                        if (c == ram || c.Driven) continue;
                        var agent = c.GetComponent<TrafficAgent>();
                        if (agent != null && agent.enabled) continue;
                        float d = (c.transform.position - p).sqrMagnitude;
                        if (d > 12f * 12f && (best == null || d < (best.transform.position - p).sqrMagnitude)) best = c;
                    }
                    if (best == null) return false;
                    ramTarget = best.transform;
                    return true;
                }
                case "wall":
                {
                    float bestD = float.MaxValue;
                    for (int i = 0; i < 12; i++)
                    {
                        var dir = Quaternion.Euler(0f, i * 30f, 0f) * fwd;
                        foreach (var hit in Physics.RaycastAll(p + Vector3.up * 1.2f, dir, 70f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (hit.collider.attachedRigidbody != null || Mathf.Abs(hit.normal.y) > 0.3f || hit.distance < 15f) continue;
                            if (hit.distance < bestD)
                            {
                                bestD = hit.distance;
                                ramPoint = hit.point;
                            }
                        }
                    }
                    return bestD < float.MaxValue;
                }
                case "pileup":
                {
                    var center = p + fwd * 32f;
                    var right = Vector3.Cross(Vector3.up, fwd);
                    CarController middle = null;
                    for (int i = 0; i < 3; i++)
                    {
                        var q = center + right * ((i - 1) * 2.3f) + fwd * (i % 2 * 1.6f);
                        q.y = World.RoadSurface.GroundY(new Vector2(q.x, q.z)) + 0.05f;
                        var car = VehicleRoster.Spawn(i == 1 ? VehicleTypes.YellowCab : VehicleTypes.Sedan, q, Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg + i * 25f);
                        if (i == 1) middle = car;
                    }
                    ramTarget = middle.transform;
                    return true;
                }
                case "headon":
                {
                    var ahead = p + fwd * 55f;
                    var lane = system.Graph.Nearest(new Vector2(ahead.x, ahead.z), new Vector2(-fwd.x, -fwd.z), 30f, out float s, out _);
                    if (lane == null) return Aim("traffic");
                    ramTarget = system.Spawn(VehicleTypes.Sedan, lane, s, 9f).transform;
                    return true;
                }
            }
            return false;
        }

        void OnHit(VehicleHit hit)
        {
            if (hit.ImpactSpeed < 3f) return;
            collisions++;
            if (hit.Car == ram) ramHits++;
            lastHit[hit.Car] = (Time.time, hit.ImpactSpeed, hit.OtherCar != null ? "car" : hit.Other != null ? hit.Other.name : "?");
        }

        void FixedUpdate()
        {
            if (ram == null) return;
            if (ramming)
            {
                var to = (ramTarget != null ? ramTarget.position : ramPoint) - ram.transform.position;
                to.y = 0f;
                ram.SetInput(to.sqrMagnitude > 0.01f ? to.normalized : ram.transform.forward, 1f);
            }
            else if (reversing) ram.SetInput(-ram.transform.forward, 0.7f);
            else ram.SetInput(Vector3.zero, 0f);
            Track();
        }

        void Track()
        {
            if (Time.time >= nextCarList)
            {
                nextCarList = Time.time + 2f;
                everyCar = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            }
            var c = ram.transform.position;
            if (Time.time >= nextTrack)
            {
                nextTrack = Time.time + 0.25f;
                tracked.Clear();
                foreach (var car in everyCar)
                    if (car != null && (car.transform.position - c).sqrMagnitude < 90f * 90f) tracked.Add(car);
            }
            foreach (var car in tracked)
            {
                if (car == null || !car.gameObject.activeInHierarchy) continue;
                var body = car.GetComponent<Rigidbody>();
                var p = car.transform.position;
                float height = p.y - World.RoadSurface.GroundY(new Vector2(p.x, p.z));
                float vy = body.isKinematic ? 0f : body.linearVelocity.y;
                float spin = body.isKinematic ? 0f : new Vector2(body.angularVelocity.x, body.angularVelocity.z).magnitude;
                if (!flights.TryGetValue(car, out var f))
                    flights[car] = f = new Flight { Name = Describe(car), WasKinematic = body.isKinematic };
                if (vy > 3f && f.TakeOff == null) f.TakeOff = Context(car, body, f.WasKinematic);
                f.MaxVy = Mathf.Max(f.MaxVy, vy);
                f.MaxHeight = Mathf.Max(f.MaxHeight, height);
                f.MaxSpin = Mathf.Max(f.MaxSpin, spin);
                f.WasKinematic = body.isKinematic;
            }
        }

        string Describe(CarController car)
        {
            if (car == ram) return "player";
            var agent = car.GetComponent<TrafficAgent>();
            if (agent != null && agent.enabled) return "AI " + agent.Spec.Id;
            var id = car.GetComponent<VehicleIdentity>();
            return "parked " + (id != null ? VehicleRoster.TypeOf(car) : car.ModelId);
        }

        string Context(CarController car, Rigidbody body, bool wasKinematic)
        {
            var damage = car.GetComponent<VehicleDamage>();
            string hit = lastHit.TryGetValue(car, out var h) ? string.Format(CultureInfo.InvariantCulture, "{0:F2}s after a {1:F1} m/s hit on {2}", Time.time - h.time, h.speed, h.what) : "no hit of its own";
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}{4} tilt={5:F0} constraints={6} {7}",
                car == ram ? "player " : car.Driven ? "driven " : "",
                car.Stunned ? "stunned " : "",
                wasKinematic ? "was-kinematic " : "",
                damage != null && damage.IsWrecked ? "wrecked " : "",
                car.Autopilot ? "autopilot " : "",
                Vector3.Angle(car.transform.up, Vector3.up), body.constraints, hit);
        }

        void Summarize()
        {
            var all = flights.Values.ToList();
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[CrashRun] collisions>=3m/s={0} player hits={1} attempts: {2}", collisions, ramHits, string.Join(", ", attempts.Select(kv => kv.Key + "=" + kv.Value))));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[CrashRun] cars tracked={0} max vy={1:F2} m/s max height={2:F2} m max roll/pitch spin={3:F2} rad/s; vy>4: {4} cars, height>1 m: {5} cars, height>0.5 m: {6} cars",
                all.Count, all.Count > 0 ? all.Max(f => f.MaxVy) : 0f, all.Count > 0 ? all.Max(f => f.MaxHeight) : 0f, all.Count > 0 ? all.Max(f => f.MaxSpin) : 0f,
                all.Count(f => f.MaxVy > 4f), all.Count(f => f.MaxHeight > 1f), all.Count(f => f.MaxHeight > 0.5f)));
            foreach (var f in all.OrderByDescending(f => f.MaxVy).Take(20))
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "[CrashRun]   {0}: vy {1:F2} m/s, height {2:F2} m, spin {3:F2} rad/s{4}", f.Name, f.MaxVy, f.MaxHeight, f.MaxSpin, f.TakeOff != null ? " | take-off: " + f.TakeOff : ""));
            var csv = new StringBuilder("car,max_vy,max_height,max_spin,take_off\n");
            foreach (var f in all) csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F3},{3:F3},\"{4}\"", f.Name, f.MaxVy, f.MaxHeight, f.MaxSpin, f.TakeOff));
            File.WriteAllText(Path.Combine(output, "flights.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(output, "crash.log"), log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
