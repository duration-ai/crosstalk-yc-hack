using System;
using System.Collections;
using System.IO;
using System.Linq;
using Dogpatch.Backend;
using UnityEngine;

namespace Dogpatch.Race
{
    // Headless multiplayer check: run the player online with
    //   -speeddial-host (or -speeddial-join <code>) -speeddial-autopilot -speeddial-mp-out <folder>
    // and it writes the room's route (route.json, the bot client's --host route), readies up once
    // two other racers are in the room, races on autopilot, and saves screenshots of the lobby,
    // a remote racer on screen, each world change landing and the results, logging every remote
    // racer's drawn position against its newest server sample. It quits a few seconds after the
    // results.
    public sealed class MultiplayerCheck : MonoBehaviour
    {
        string output;
        RaceDirector race;
        int shots;
        int changes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-speeddial-mp-out");
            if (at < 0 || at + 1 >= args.Length) return;
            Application.runInBackground = true;
            new GameObject("MultiplayerCheck").AddComponent<MultiplayerCheck>().output = args[at + 1];
        }

        static void Log(string message) => Debug.Log("[MP check] " + message);

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(output, $"{++shots:00}-{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Log("screenshot " + path);
        }

        [Serializable]
        class RouteFile
        {
            public Vec3[] route;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            float deadline = Time.realtimeSinceStartup + 600f;
            StartCoroutine(Watchdog(deadline));
            while ((race = RaceDirector.Instance) == null || !race.Setup) yield return null;
            if (!race.Online)
            {
                Log("FAIL: race backend unreachable, running offline");
                Application.Quit(2);
                yield break;
            }
            File.WriteAllText(Path.Combine(output, "route.json"), JsonUtility.ToJson(new RouteFile { route = race.Course.RouteWorld.Select(v => new Vec3((float)Math.Round(v.x, 2), (float)Math.Round(v.y, 2), (float)Math.Round(v.z, 2))).ToArray() }));
            while (race.Last == null) yield return null;
            Log($"in room {race.RoomCode} as {race.MyName} ({race.MyId}), slot {race.MySlot}");
            race.EventArrived += e => StartCoroutine(OnChange(e));

            while (race.Remotes.All.Count() < 2) yield return null;
            Log("remote racers: " + string.Join(", ", race.Remotes.All.Select(r => $"{r.Name} ({r.Color})")));
            race.Game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Shot("lobby");
            race.SetReady(true);

            while (!race.Started) yield return null;
            Log("GO");
            StartCoroutine(SyncLog());
            float lastRemoteShot = -100f;
            int remoteShots = 0;
            while (!race.ResultsIn)
            {
                if (remoteShots < 3 && Time.realtimeSinceStartup - lastRemoteShot > 8f && RemoteInView(out var who))
                {
                    lastRemoteShot = Time.realtimeSinceStartup;
                    remoteShots++;
                    yield return Shot("remote-racer-" + who.ToLowerInvariant().Replace(' ', '-'));
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(2f);
            Log("results: " + string.Join(", ", race.Last.results.Select(r => $"{r.place}. {r.displayName} {(r.finished ? (r.finishMs / 1000f).ToString("F1") + " s" : "DNF")} {r.award}")));
            yield return Shot("results");
            yield return new WaitForSecondsRealtime(2f);
            Log($"PASS: {race.Remotes.All.Count()} remote racers, {changes} world changes, {race.Last.results.Count} results");
            Application.Quit(0);
        }

        bool RemoteInView(out string who)
        {
            who = null;
            var cam = race.Game.View;
            foreach (var remote in race.Remotes.All)
            {
                var body = remote.Body;
                if (body == null) continue;
                var v = cam.WorldToViewportPoint(body.position + Vector3.up);
                if (v.z < 3f || v.z > 35f || v.x < 0.2f || v.x > 0.8f || v.y < 0.15f || v.y > 0.85f) continue;
                who = remote.Name;
                return true;
            }
            return false;
        }

        IEnumerator OnChange(RaceEvent e)
        {
            changes++;
            Log($"world change landed: #{e.seq} {e.type} by {e.displayName}: {e.news}");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("world-change-" + e.type);
        }

        IEnumerator SyncLog()
        {
            while (!race.ResultsIn)
            {
                foreach (var remote in race.Remotes.All)
                {
                    var server = race.Last.racers.FirstOrDefault(r => r.playerId == remote.Id);
                    if (server == null) continue;
                    var sp = new Vector3(server.position.x, server.position.y, server.position.z);
                    Log($"sync {remote.Name}: drawn ({remote.Position.x:F1},{remote.Position.z:F1}) newest server ({sp.x:F1},{sp.z:F1}) gap {Vector3.Distance(remote.Position, sp):F1} m, server place {server.place}, progress {server.progress:F0} m, delay {race.Remotes.DelayMs:F0} ms");
                }
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        IEnumerator Watchdog(float deadline)
        {
            while (Time.realtimeSinceStartup < deadline) yield return new WaitForSecondsRealtime(1f);
            Log("FAIL: timed out");
            Application.Quit(3);
        }
    }
}
