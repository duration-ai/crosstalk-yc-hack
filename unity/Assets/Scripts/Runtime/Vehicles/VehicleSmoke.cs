using System;
using System.Collections;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Headless smoke run for the service vehicles, inert unless the player is launched with
    //   -vehicleSmoke <dir> -vehicleSmokeType duck_boat|fire_truck   (with -offline -operator-local)
    // It asks for the ride through a change_vehicle row (the voice path), then:
    //   duck_boat: drives east off the waterfront into the bay, floats for a while, turns back and
    //              climbs out onto the shore, logging height, Afloat and speed every second.
    //   fire_truck: drives down the longest street at a burning parked car and a second parked car,
    //              tapping the cannon (its IVehicleAbility) every 3 s, logging when it puts the fire
    //              out and blasts cars.
    // Chase-cam stills go to <dir>/<type>-*.png.
    public sealed class VehicleSmoke : MonoBehaviour
    {
        string output;
        string type;
        GameController game;
        CarController car;
        Vector3 heading;
        float throttle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-vehicleSmoke");
            if (i < 0 || i + 1 >= args.Length) return;
            var smoke = new GameObject("VehicleSmoke").AddComponent<VehicleSmoke>();
            smoke.output = args[i + 1];
            int j = Array.IndexOf(args, "-vehicleSmokeType");
            smoke.type = j >= 0 && j + 1 < args.Length ? args[j + 1] : "duck_boat";
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            yield return Frames(20);
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);
            var effects = gameObject.AddComponent<OperatorEffects>();
            effects.Registry = EffectRegistry.Default();
            effects.Context = new EffectContext { Game = game, Race = null, Host = effects };
            game.ExternalInput = true;

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;
            game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
            yield return Frames(3);
            var enter = game.EnterCar(game.NearestCar ?? parked);
            while (!enter.IsCompleted) yield return null;
            car = game.CurrentCar;
            effects.Optimistic(new ActiveEffect
            {
                id = "smoke-" + type, effect = "change_vehicle", scope = "players", strength = "medium", status = "active", instant = true,
                startsAt = effects.ServerNow(), endsAt = effects.ServerNow() + 1000, label = type, requestedBy = EntityTagger.MyPlayerId, vehicle = type,
            });
            yield return Frames(5);
            Debug.Log($"[VehicleSmoke] swapped to {VehicleRoster.TypeOf(car)} (top {car.Handling.TopSpeed:F1} m/s)");
            yield return type == "fire_truck" ? FireTruckRun() : DuckBoatRun();
            Debug.Log("[VehicleSmoke] done");
            Application.Quit();
        }

        IEnumerator DuckBoatRun()
        {
            var b = game.World.Bounds;
            float shore = float.NaN;
            float z = b.center.y;
            for (float x = b.center.x; x < b.xMax - 20f; x += 5f)
            {
                if (!Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.name == "Seabed") { shore = x; break; }
            }
            if (float.IsNaN(shore)) { Debug.LogError("[VehicleSmoke] no shoreline found"); yield break; }
            heading = Vector3.right;
            car.Place(new Vector3(shore - 30f, 0.4f, z), 90f);
            game.Follow.Snap(car.transform, true);
            throttle = 1f;
            for (int s = 0; s < 6; s++)
            {
                if (s == 3)
                {
                    float peak = car.transform.position.y;
                    for (int k = 0; k < 30; k++) { peak = Mathf.Max(peak, car.transform.position.y); yield return null; }
                    Debug.Log($"[VehicleSmoke] hop peak y={peak:F2}");
                }
                yield return Frames(30);
                Log(s);
                if (s == 4) yield return Shot("duck_boat-afloat");
                if (s == 2) car.GetComponent<IVehicleAbility>()?.Use();
            }
            heading = Vector3.left;
            for (int s = 6; s < 20; s++)
            {
                yield return Frames(30);
                Log(s);
                if (s == 8) yield return Shot("duck_boat-turning");
            }
            yield return Shot("duck_boat-ashore");
        }

        IEnumerator FireTruckRun()
        {
            var (start, dir) = LongestRun();
            heading = dir;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            car.Place(start + Vector3.up * 0.3f, yaw);
            game.Follow.Snap(car.transform, true);
            int n = 0;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (c == car || c.Driven || c.Autopilot || n >= 2) continue;
                c.Place(start + dir * (n == 0 ? 26f : 55f) + Vector3.Cross(Vector3.up, dir) * (n == 0 ? 1.5f : -1.0f), yaw + 70f);
                if (n == 0)
                {
                    var bounds = new Bounds(c.transform.position + Vector3.up * 0.8f, new Vector3(2f, 1.6f, 4.5f));
                    var fire = FireFx.Create(VehicleRoster.SharedMaterial, bounds, 1.2f, true);
                    fire.transform.SetParent(c.transform, true);
                }
                n++;
            }
            throttle = 0.35f;
            for (int s = 0; s < 14; s++)
            {
                if (s % 3 == 0 || s == 8) car.GetComponent<IVehicleAbility>()?.Use();
                yield return Frames(30);
                Log(s);
                if (s == 2) yield return Shot("fire_truck-spraying");
                if (s == 9) yield return Shot("fire_truck-blast");
            }
            Debug.Log($"[VehicleSmoke] fires left: {FindObjectsByType<FireFx>(FindObjectsSortMode.None).Length}");
        }

        void Log(int s) =>
            Debug.Log($"[VehicleSmoke] t={s + 1}s pos={car.transform.position} y={car.transform.position.y:F2} speed={car.Speed:F1} afloat={car.Afloat} type={VehicleRoster.TypeOf(car)}");

        void LateUpdate()
        {
            if (car != null && throttle > 0f) car.SetInput(heading, throttle);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[VehicleSmoke] shot " + name);
        }

        (Vector3, Vector3) LongestRun()
        {
            var roads = game.World.Roads;
            Vector3 bestStart = Vector3.zero, bestDir = Vector3.forward;
            float best = 0f;
            for (int r = 0; r < roads.Points.Length; r++)
            {
                var pts = roads.Points[r];
                if (pts == null || pts.Length < 2 || roads.Roads[r].kind == "footway" || roads.Roads[r].kind == "path") continue;
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
                    bestStart = new Vector3(pts[from].x, 0f, pts[from].y) + Vector3.Cross(Vector3.up, bestDir) * 2.2f;
                }
            }
            return (bestStart, bestDir);
        }
    }
}
