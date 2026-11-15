using System;
using System.Collections;
using System.IO;
using Dogpatch.Operator;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // Scripted play-through for headless checks: run the player build with
    //   -speeddial-demo -speeddial-out <folder>
    // and it stands on foot with the phone button buzzing, steps onto the start ring, readies up, races to YC
    // on autopilot while calling the Operator and letting the bots fire every world change, then
    // takes an ask pickup on the way, then the results and a rematch where a call with no asks left
    // is turned down, saving a screenshot of each UI state. Game time runs at a
    // fixed 30 fps so animations land on the same frames however slow the renderer is.
    // Built with the SPEEDDIAL_DEMO define (BUILD_DEFINES=SPEEDDIAL_DEMO), the player runs it on launch,
    // for the iOS simulator check where there is no command line.
    public sealed class SpeedDialDemo : MonoBehaviour
    {
        string output;
        RaceDirector race;
        int shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
#if !SPEEDDIAL_DEMO
            if (Array.IndexOf(args, "-speeddial-demo") < 0) return;
#endif
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            var demo = new GameObject("SpeedDialDemo").AddComponent<SpeedDialDemo>();
            int at = Array.IndexOf(args, "-speeddial-out");
            demo.output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "speeddial-demo");
        }

        static void Log(string message) => Debug.Log("[SpeedDial demo] " + message);

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            Time.captureDeltaTime = 1f / 30f;
            StartCoroutine(Watchdog());
            while ((race = RaceDirector.Instance) == null || !race.Setup || race.Last == null) yield return null;
            var game = race.Game;
            Log($"room ready, output {output}");
            yield return Seconds(1f);

            var toRing = race.Course.RingCenter - race.Course.RingFacing * 14f;
            game.Player.Teleport(toRing, RoutePath.Heading(new Vector2(race.Course.RingFacing.x, race.Course.RingFacing.z)));
            game.Follow.Snap(game.Player.transform, false);
            yield return Seconds(1.5f);
            yield return Shot("07-on-foot");

            race.Calls.Buzz();
            yield return Seconds(0.25f);
            yield return Shot("08-phone-buzz");

            game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            yield return Seconds(12f);
            Log(race.Room.status == RoomStatus.Lobby ? $"lobby held while you're unready: {race.Room.readyCount} of {race.Room.playerCount} ready" : "FAILED: the lobby counted down while you were unready");
            yield return Shot("10-lobby");

            var jun = GameObject.Find("Racer Jun");
            if (jun != null)
            {
                var toJun = jun.transform.position - race.Course.RingCenter;
                toJun.y = 0f;
                var beside = jun.transform.position - toJun.normalized * 1.4f;
                game.Player.Teleport(new Vector3(beside.x, jun.transform.position.y + 0.05f, beside.z), game.Player.transform.eulerAngles.y);
                yield return null;
                var dir = jun.transform.position - game.Player.transform.position;
                dir.y = 0f;
                Log(race.Shove.ShoveToward(dir.normalized) ? "shoved Jun on the ring" : "Jun out of reach");
                yield return Seconds(0.35f);
                yield return Shot("10b-lobby-shove");
            }

            race.Hud.PressReady();
            yield return Until(() => race.Room.status == RoomStatus.Countdown, 8f, "countdown once everyone is ready");
            Log($"countdown with {race.Room.readyCount} of {race.Room.playerCount} ready");
            yield return Until(() => race.Gridded, 6f, "on the grid");
            yield return Until(() => race.MsToStart <= 1900f, 6f, "countdown at 2");
            yield return Seconds(0.1f);
            yield return Shot("countdown-2");
            yield return Until(() => race.Started, 4f, "GO");
            race.EnableAutopilot();
            yield return Seconds(0.05f);
            yield return Shot("countdown-go");
            yield return Seconds(1.6f);
            yield return Shot("countdown-find-me");

            yield return Seconds(4.4f);
            yield return Shot("11a-racing");

            if (race.Calls.HasAsks)
            {
                race.Calls.BeginTalk();
                yield return Seconds(0.9f);
                yield return Shot("09a-call-live");
                race.Calls.EndTalk();
                yield return Until(() => race.Calls.State == CallState.Answered, 5f, "Operator answered");
                yield return Seconds(0.6f);
                yield return Shot("09-mid-call-driving");
                yield return Until(() => race.Calls.State == CallState.None, 6f, "Operator done");
                yield return Until(() => race.Hud.NewsSince >= 0f, 3f, "news for my call");
                yield return Seconds(0.5f);
                yield return Shot("09b-news-my-call");
                yield return Until(() => race.Hud.NewsSince < 0f, 4f, "news cleared", fatal: false);
            }
            else Log("no asks left for the mid-race call");

            yield return AskPickup();

            race.BotCall("Maya", WorldChangeCatalog.Command("gravity", new WorldChangeParams { level = "low" }));
            yield return Until(() => race.Hud.NewsSince >= 0.06f, 3f, "news shooting in");
            yield return Shot("14a-news-shooting-in");
            yield return Until(() => race.Hud.NewsSince >= 0.45f, 3f, "news landed");
            yield return Shot("11-racing-world-change");
            yield return Seconds(2.5f);

            var sequence = new (string bot, string type, WorldChangeParams p)[]
            {
                ("Jun", "ramp", new WorldChangeParams { count = 3 }),
                ("Rosa", "swap", new WorldChangeParams()),
                ("Maya", "resize", new WorldChangeParams { size = "big" }),
                ("Jun", "traffic", new WorldChangeParams { level = "heavy" }),
                ("Rosa", "ice", new WorldChangeParams { level = "on" }),
                ("Maya", "recolor", new WorldChangeParams { color = "pink" }),
                ("Jun", "fog", new WorldChangeParams { level = "thick" }),
                ("Rosa", "rain", new WorldChangeParams { level = "on" }),
                ("Maya", "snow", new WorldChangeParams { level = "on" }),
                ("Jun", "time", new WorldChangeParams { timeOfDay = "night" }),
            };
            foreach (var (bot, type, p) in sequence)
            {
                if (race.ResultsIn) break;
                race.BotCall(bot, WorldChangeCatalog.Command(type, p));
                yield return Seconds(1.8f);
                yield return Shot("wc-" + type);
                yield return Seconds(0.9f);
            }

            yield return Until(() => race.CrossedFinish || race.ResultsIn, 240f, "over the line");
            if (!race.ResultsIn)
            {
                yield return Seconds(0.6f);
                yield return Shot("11b-finished");
            }
            yield return Until(() => race.ResultsIn, 240f, "results");
            yield return Seconds(1.3f);
            yield return Shot("12-results");

            race.Hud.PressReady();
            yield return Until(() => !race.ResultsIn && !race.Gridded && race.Room.raceNumber == 2, 6f, "rematch lobby");
            yield return Seconds(2.5f);
            yield return Shot("12b-rematch-lobby");

            for (int left = race.Me.asks; left > 0;)
            {
                var spend = race.Session.SpendAskAsync(race.MyId, race.RoomId);
                yield return Wait(spend);
                left = spend.Result.asks;
            }
            yield return Until(() => race.Me.asks == 0, 3f, "out of asks");
            race.Calls.BeginTalk();
            yield return Until(() => Time.time - race.Calls.TurnedDownAt < 1f, 3f, "call turned down with no asks");
            yield return Seconds(0.5f);
            yield return Shot("08b-no-asks");
            Log($"done: {shots} screenshots");
            Quit(0);
        }

        // Drives through the next ask pickup ahead: a look at the token, then the counter going up.
        IEnumerator AskPickup()
        {
            var me = race.MyCar.transform;
            float progress = race.Course.Route.Project(RoutePath.Flat(me.position));
            int pick = -1;
            for (int i = 0; i < race.Course.AskPickups.Count && pick < 0; i++)
                if (race.Course.Route.Project(RoutePath.Flat(race.Course.AskPickups[i])) > progress + 20f) pick = i;
            if (pick < 0)
            {
                Log("no ask pickup ahead");
                yield break;
            }
            int before = race.Me.asks;
            var token = race.Course.AskPickups[pick];
            var street = race.Course.Graph.Route(RoutePath.Flat(token), race.Course.Route.PointAt(race.Course.Route.Project(RoutePath.Flat(token))));
            var approach = street != null && street.Count >= 2 ? new RoutePath(street) : null;
            var at = approach != null ? approach.PointAt(Mathf.Min(11f, approach.Length)) : RoutePath.Flat(me.position);
            var back = at - RoutePath.Flat(token);
            var from = new Vector3(at.x, token.y, at.y);
            race.HoldAutopilot(true);
            race.MoveBody(race.MyId, from + Vector3.up * 0.3f, RoutePath.Heading(-back));
            race.Game.Follow.Snap(me, true);
            yield return Seconds(0.8f);
            yield return Shot("15a-ask-token");
            race.MoveBody(race.MyId, token + Vector3.up * 0.3f, RoutePath.Heading(-back));
            yield return Until(() => race.Me.collected != null && race.Me.collected.Contains(pick), 4f, $"ask pickup {pick} collected");
            yield return Seconds(0.25f);
            yield return Shot("15b-phone-grabbed");
            float d = Mathf.Min(progress + 5f, race.Course.Route.Length - 30f);
            var p = race.Course.Route.PointAt(d);
            race.MoveBody(race.MyId, new Vector3(p.x, me.position.y + 0.3f, p.y), RoutePath.Heading(race.Course.Route.DirectionAt(d)));
            race.Game.Follow.Snap(me, true);
            race.HoldAutopilot(false);
        }

        static IEnumerator Wait(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted) yield return null;
        }

        IEnumerator Watchdog()
        {
            yield return new WaitForSeconds(600f);
            Log("timed out");
            Quit(1);
        }

        // The iOS demo build stays open after the run so the phone can still be looked at.
        static void Quit(int code)
        {
#if !SPEEDDIAL_DEMO
            Application.Quit(code);
#endif
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Until(Func<bool> condition, float timeout, string what, bool fatal = true)
        {
            float until = Time.time + timeout;
            while (!condition())
            {
                if (Time.time > until)
                {
                    Log((fatal ? "FAILED" : "gave up") + " waiting for " + what);
                    if (fatal) Quit(1);
                    yield break;
                }
                yield return null;
            }
            Log(what);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var path = Path.Combine(output, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            shots++;
            Log($"screenshot {name} at t={Time.time:F2}s");
        }
    }
}
