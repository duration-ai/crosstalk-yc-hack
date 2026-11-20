using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Scratch probe (not committed): -effectProbe <out.txt> -effectProbeRuns "name=subject@place:fx+fx:strength;..."
    public sealed class EffectProbe : MonoBehaviour
    {
        static readonly HashSet<string> PlayerFx = new HashSet<string> { "bigger", "smaller", "flight", "slow_fall", "run_faster", "run_slower", "chunky", "skinny", "grip_up", "tougher", "jump_higher" };
        static readonly HashSet<string> ObjectFx = new HashSet<string> { "ghost", "see_through", "grow", "shrink", "bouncy", "icy", "rock_solid", "soft" };

        string output;
        string[] runs;
        GameController game;
        CarController car;
        Vector3 heading;
        float input;
        bool walking;
        string origType;
        readonly List<string> lines = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-effectProbe");
            if (i < 0 || i + 1 >= args.Length) return;
            var p = new GameObject("EffectProbe").AddComponent<EffectProbe>();
            p.output = args[i + 1];
            int j = Array.IndexOf(args, "-effectProbeRuns");
            p.runs = File.ReadAllText(args[j + 1]).Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Time.captureFramerate = 30;
            Application.runInBackground = true;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Out(string s)
        {
            Debug.Log("[EffectProbe] " + s);
            lines.Add(s);
            File.WriteAllLines(output, lines);
        }

        IEnumerator Start()
        {
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            yield return Frames(20);
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);
            var effects = gameObject.AddComponent<OperatorEffects>();
            effects.Registry = EffectRegistry.Default();
            effects.Context = new EffectContext { Game = game, Race = null, Host = effects };
            game.ExternalInput = true;
            var (start, dir, len) = LongestRun();
            Out($"street start {start} dir {dir} length {len:F0} m");
            var b = game.World.Bounds;
            float shore = float.NaN;
            for (float x = b.center.x; x < b.xMax - 20f; x += 5f)
                if (Physics.Raycast(new Vector3(x, 60f, b.center.y), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.name == "Seabed") { shore = x; break; }
            var bay = new Vector3(shore + 25f, 0.4f, b.center.y);
            Out($"bay start {bay}, world xMax {b.xMax:F0}");

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;
            origType = VehicleRoster.TypeOf(parked);
            Out($"base car {origType} top {parked.Handling.TopSpeed:F1}");

            foreach (var raw in runs)
            {
                var spec = raw.Trim();
                if (spec.Length == 0 || spec.StartsWith("#")) continue;
                var eq = spec.Split('=');
                string name = eq[0], rest = eq[1];
                var colon = rest.Split(':');
                var at = colon[0].Split('@');
                string subject = at[0], place = at.Length > 1 ? at[1] : "street";
                string[] fx = colon.Length > 1 && colon[1].Length > 0 ? colon[1].Split('+') : new string[0];
                string strength = colon.Length > 2 ? colon[2] : "medium";

                effects.ResetAll();
                input = 0f;
                walking = false;
                yield return Frames(90);
                bool onBay = place == "bay";
                heading = onBay ? Vector3.right : dir;
                var origin = onBay ? bay : start;
                if (subject == "foot")
                {
                    if (game.CurrentCar != null)
                    {
                        var exit = game.ExitCar();
                        while (!exit.IsCompleted) yield return null;
                    }
                    car = null;
                    game.Player.Teleport(origin + Vector3.up * 0.2f, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
                    walking = true;
                }
                else
                {
                    if (game.CurrentCar == null)
                    {
                        game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
                        yield return Frames(3);
                        var enter = game.EnterCar(parked);
                        while (!enter.IsCompleted) yield return null;
                    }
                    car = game.CurrentCar;
                    var type = subject == "car" ? origType : ChangeVehicleEffect.TypeFor(subject);
                    if (VehicleRoster.TypeOf(car) != type) VehicleRoster.SwapInto(car, type);
                    var dm = car.GetComponent<Smash.VehicleDamage>();
                    if (dm != null && dm.Damage > 0f) dm.Repair();
                    car.Place(origin + Vector3.up * 0.3f, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
                }
                yield return Frames(10);

                var rows = new List<ActiveEffect>();
                foreach (var part in fx)
                {
                    var row = new ActiveEffect
                    {
                        id = "probe-" + part, effect = part, scope = PlayerFx.Contains(part) ? "players" : "world", strength = strength,
                        status = "active", instant = false, startsAt = effects.ServerNow(), endsAt = effects.ServerNow() + 600000, durationS = 600, label = part, requestedBy = EntityTagger.MyPlayerId,
                    };
                    if (ObjectFx.Contains(part) && car != null)
                    {
                        var tag = car.GetComponent<EntityTag>() ?? EntityTag.Add(car.gameObject, "car:probe", "car", "probe car", "", false, "me");
                        row.scope = "object";
                        row.target = tag.Id;
                        row.targetClass = tag.Class;
                    }
                    effects.Optimistic(row);
                    rows.Add(row);
                }
                if (rows.Count > 0) effects.OnActiveList(rows.ToArray());
                yield return Frames(150);

                Transform mover = car != null ? car.transform : game.Player.transform;
                var damage = car != null ? car.GetComponent<Smash.VehicleDamage>() : null;
                float dmg0 = damage != null ? damage.Damage : 0f;
                input = 1f;
                int n = 240;
                var pos = new Vector3[n + 1];
                pos[0] = mover.position;
                float minY = float.MaxValue, maxY = float.MinValue;
                int grounded = 0, afloat = 0;
                for (int f = 1; f <= n; f++)
                {
                    yield return null;
                    pos[f] = mover.position;
                    minY = Mathf.Min(minY, pos[f].y);
                    maxY = Mathf.Max(maxY, pos[f].y);
                    if (car != null && car.OnGround) grounded++;
                    if (car != null && car.Afloat) afloat++;
                }
                input = 0f;
                float best = 0f;
                for (int f = 30; f <= n; f++)
                {
                    var d = pos[f] - pos[f - 30];
                    d.y = 0f;
                    best = Mathf.Max(best, d.magnitude);
                }
                var total = pos[n] - pos[0];
                total.y = 0f;
                string extra = car != null
                    ? $"type {VehicleRoster.TypeOf(car)} top {car.Handling.TopSpeed:F1} SpeedMul {car.SpeedMul:F2} scale {car.transform.localScale.y:F2} onGround {grounded}/{n} afloat {afloat}/{n} dmg +{(damage != null ? damage.Damage - dmg0 : 0f):F1}"
                    : $"RunScale {PlayerController.RunScale:F2} WadeScale {typeof(PlayerController).GetField("WadeScale")?.GetValue(null) ?? "n/a"} scale {game.Player.transform.localScale.y:F2}";
                Out($"RUN {name}: top {best:F2} m/s (1 s window), 8 s distance {total.magnitude:F0} m, y {minY:F2}..{maxY:F2}, flood {FloodEffect.Level:F2}, {extra}");
            }
            effects.ResetAll();
            Out("done");
            Application.Quit();
        }

        void LateUpdate()
        {
            if (game == null) return;
            if (walking && game.Player != null && game.Player.isActiveAndEnabled) game.Player.SetInput(heading, input);
            if (!walking && car != null) car.SetInput(heading, input);
        }

        (Vector3, Vector3, float) LongestRun()
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
            return (bestStart, bestDir, best);
        }
    }
}
