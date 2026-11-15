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
    // and it walks to a payphone, takes a call, steps onto the start ring, readies up, races to YC
    // on autopilot while calling the Operator and letting the bots fire every world change, then
    // takes the results and a rematch, saving a screenshot of each UI state. Game time runs at a
    // fixed 30 fps so animations land on the same frames however slow the renderer is.
    public sealed class SpeedDialDemo : MonoBehaviour
    {
        string output;
        RaceDirector race;
        int shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-speeddial-demo") < 0) return;
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

            var phone = race.Calls.Phones[0];
            var facing = -phone.transform.forward;
            var stand = phone.HandsetPoint + phone.transform.forward * 3.6f + phone.transform.right * 1.2f;
            game.Player.Teleport(stand, RoutePath.Heading(new Vector2(facing.x, facing.z)));
            game.Follow.Snap(game.Player.transform, false);
            yield return Seconds(1.5f);
            yield return Shot("07-on-foot");

            game.Player.Teleport(phone.HandsetPoint + phone.transform.forward * 0.5f, RoutePath.Heading(new Vector2(facing.x, facing.z)));
            yield return Until(() => race.Calls.State == CallState.Holding, 5f, "handset grabbed");
            yield return Seconds(0.6f);
            yield return Shot("08-holding-a-call");

            game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            yield return Until(() => race.Room.status == RoomStatus.Countdown, 15f, "auto-start countdown running");
            yield return Until(() => race.MsToStart <= 6900f, 15f, "count at 07");
            yield return Shot("10-lobby");

            var jun = GameObject.Find("Racer Jun");
            if (jun != null)
            {
                var dir = jun.transform.position - game.Player.transform.position;
                dir.y = 0f;
                Log(race.Shove.ShoveToward(dir.normalized) ? "shoved Jun on the ring" : "Jun out of reach");
                yield return Seconds(0.35f);
                yield return Shot("10b-lobby-shove");
            }

            race.Hud.PressReady();
            yield return Until(() => race.Gridded, 6f, "on the grid");
            yield return Until(() => race.MsToStart <= 1900f, 6f, "countdown at 2");
            yield return Seconds(0.1f);
            yield return Shot("countdown-2");
            yield return Until(() => race.Started, 4f, "GO");
            race.EnableAutopilot();
            yield return Seconds(0.05f);
            yield return Shot("countdown-go");

            yield return Seconds(6f);
            yield return Shot("11a-racing");

            if (race.Calls.State == CallState.Holding)
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
                yield return Until(() => race.Hud.NewsSince < 0f, 4f, "news cleared");
            }
            else Log("no call in hand for the mid-race call (state " + race.Calls.State + ")");

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

            yield return Until(() => race.ResultsIn, 240f, "results");
            yield return Seconds(1.3f);
            yield return Shot("12-results");

            race.Hud.PressReady();
            yield return Until(() => !race.ResultsIn && !race.Gridded && race.Room.raceNumber == 2, 6f, "rematch lobby");
            yield return Seconds(2.5f);
            yield return Shot("12b-rematch-lobby");
            Log($"done: {shots} screenshots");
            Application.Quit(0);
        }

        IEnumerator Watchdog()
        {
            yield return new WaitForSeconds(600f);
            Log("timed out");
            Application.Quit(1);
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.time + timeout;
            while (!condition())
            {
                if (Time.time > until)
                {
                    Log("FAILED waiting for " + what);
                    Application.Quit(1);
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
