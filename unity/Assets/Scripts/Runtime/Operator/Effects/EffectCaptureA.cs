using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Headless effect review, inert unless the player is launched with
    //   -worldEffectCapture <dir> [-worldEffectIds a,b,c] [-worldEffectStrength small|medium|large]
    // For each effect it drives (or walks) down the longest straight street, saves a still before
    // the effect, two while it runs and one after it is reverted, as <dir>/<id>-1-before.png ...
    // Effects run through a local OperatorEffects with synthetic activeEffects rows, so the code
    // path is the one every device uses. Combine with -offline -operator-local.
    public sealed class EffectCaptureA : MonoBehaviour
    {
        static readonly HashSet<string> OnFoot = new HashSet<string> { "chunky", "skinny", "flight", "floor_is_lava", "thick_air", "tougher" };
        static readonly string[] Players = { "chunky", "skinny", "flight", "grip_up", "tougher" };

        string output;
        string[] ids;
        string strength;
        GameController game;
        CarController car;
        Vector3 heading;
        float throttle;
        bool walking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-worldEffectCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var capture = new GameObject("WorldEffectCapture").AddComponent<EffectCaptureA>();
            capture.output = args[i + 1];
            int j = Array.IndexOf(args, "-worldEffectIds");
            capture.ids = (j >= 0 && j + 1 < args.Length ? args[j + 1] : "chunky,skinny,flight,grip_up,tougher,cars_fly,fast_day_cycle,sandstorm,heatwave,blackout,slow_motion,fast_forward,thick_air,flood,floor_is_lava,earthquake,cars_on_water,floaty_crashes").Split(',');
            int k = Array.IndexOf(args, "-worldEffectStrength");
            capture.strength = k >= 0 && k + 1 < args.Length ? args[k + 1] : "medium";
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
            var (start, dir) = LongestRun();

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;

            foreach (var id in ids)
            {
                bool foot = OnFoot.Contains(id);
                heading = dir;
                throttle = 0f;
                walking = false;
                if (foot)
                {
                    if (game.CurrentCar != null)
                    {
                        var exit = game.ExitCar();
                        while (!exit.IsCompleted) yield return null;
                    }
                    game.Player.Teleport(start + Vector3.Cross(Vector3.up, dir) * 3.5f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                    game.Follow.Snap(game.Player.transform, false);
                    walking = true;
                }
                else
                {
                    if (game.CurrentCar == null)
                    {
                        game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
                        yield return Frames(3);
                        var enter = game.EnterCar(game.NearestCar ?? parked);
                        while (!enter.IsCompleted) yield return null;
                    }
                    car = game.CurrentCar;
                    car.Place(start + Vector3.up * 0.3f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                    game.Follow.Snap(car.transform, true);
                    throttle = 0.5f;
                }
                yield return Frames(45);
                yield return Shot(id + "-1-before");

                var row = new ActiveEffect
                {
                    id = "capture-" + id, effect = id, scope = Array.IndexOf(Players, id) >= 0 ? "players" : "world", strength = strength,
                    status = "active", instant = false, startsAt = effects.ServerNow(), endsAt = effects.ServerNow() + 120000, durationS = 120, label = id, requestedBy = id == "change_vehicle" ? EntityTagger.MyPlayerId : "capture",
                    vehicle = id == "change_vehicle" ? Environment.GetEnvironmentVariable("EFFECT_VEHICLE") ?? "sports_car" : null,
                };
                effects.Optimistic(row);
                if (id == "cars_on_water") yield return ToWater();
                if (id == "floaty_crashes") Obstacles();
                yield return Frames(45);
                var shape = game.Player.Visual != null ? game.Player.Visual.Find("Shape (operator)") : null;
                Debug.Log($"[WorldEffectCapture] {id}: player at {game.Player.transform.position}, visual {game.Player.Visual?.name}, shape {(shape != null ? shape.localScale.ToString() : "none")}, car {(game.CurrentCar != null ? $"{game.CurrentCar.name} {game.CurrentCar.transform.position} speed {game.CurrentCar.Speed:F1} driven {game.CurrentCar.Driven} kinematic {game.CurrentCar.GetComponent<Rigidbody>().isKinematic} visual {Racers.CarVisual(game.CurrentCar)?.lossyScale}" : "none")}, camera {game.View.transform.position}");
                yield return Shot(id + "-2-during");
                yield return Frames(75);
                yield return Shot(id + "-3-during-late");
                effects.ResetAll();
                yield return Frames(100);
                yield return Shot(id + "-4-after");
                Debug.Log($"[WorldEffectCapture] {id} done (timeScale {Time.timeScale:F2})");
            }
            Debug.Log("[WorldEffectCapture] done");
            Application.Quit();
        }

        // Puts the car on the bay just off the shore nearest YC, heading out to sea.
        IEnumerator ToWater()
        {
            yield return Frames(15);
            if (car == null) yield break;
            var b = game.World.Bounds;
            Vector3 best = Vector3.zero;
            for (float x = b.xMax - 40f; x > b.center.x; x -= 20f)
            {
                var p = new Vector3(x, 0f, b.center.y);
                if (!Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.name != "Drivable water (operator)") break;
                best = p;
            }
            if (best == Vector3.zero) yield break;
            heading = Vector3.right;
            car.Place(best + Vector3.up * 0.4f - Vector3.right * 30f, 90f);
            game.Follow.Snap(car.transform, true);
        }

        // Two parked cars dropped in the lane ahead to crash into.
        void Obstacles()
        {
            int n = 0;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (c == car || c.Driven || c.Autopilot || n >= 2) continue;
                var at = car.transform.position + heading * (14f + n * 6f) + Vector3.Cross(Vector3.up, heading) * (n == 0 ? 0.3f : -0.6f);
                c.Place(at, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg + 70f);
                n++;
            }
            throttle = 1f;
        }

        void LateUpdate()
        {
            if (walking && game != null && game.Player != null && game.Player.isActiveAndEnabled) game.Player.SetInput(heading, 0.6f);
            if (!walking && car != null && throttle > 0f) car.SetInput(heading, throttle);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[WorldEffectCapture] " + name);
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
