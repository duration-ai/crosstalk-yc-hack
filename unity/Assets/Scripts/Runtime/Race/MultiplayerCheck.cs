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

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
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
            int want = int.TryParse(Arg("-speeddial-remotes"), out var n) ? n : 1;
            while (race.WorldRoomId == null || race.Remotes.All.Count() < want) yield return null;
            Log($"in the world: shard {race.WorldShard}, {race.WorldCount} online, race room {race.RoomId ?? "none"}, {Vector3.Distance(race.Game.Player.transform.position, race.Course.RingCenter):F0} m from the ring, world room {race.WorldRoomId}; others: " + string.Join(", ", race.Remotes.All.Select(r => $"{r.Name} ({r.WorldColor}, {r.WorldMode})")));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("world");

            race.Game.Player.Teleport(race.RingSpot(0), 0f);
            while (race.Last == null) yield return null;
            Log($"entered the ring: mission room {race.RoomCode} as {race.MyName} ({race.MyId}), slot {race.MySlot}, {race.Room.playerCount} in the lobby, {race.Room.status}");
            race.EventArrived += e => StartCoroutine(OnChange(e));
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("lobby");
            while (race.Room == null || race.Room.status != RoomStatus.Countdown) yield return null;
            Log($"countdown: {race.MsToStart / 1000f:F1} s to go, {race.Room.playerCount} racers");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("countdown");

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
            int results = race.Last.results.Count;
            while (race.RoomId != null) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            Log($"back in the world at ({race.Game.Player.transform.position.x:F0},{race.Game.Player.transform.position.z:F0}), {race.WorldCount} online");
            yield return Shot("back-in-world");
            Log($"PASS: {race.Remotes.All.Count()} remote players, {changes} world changes, {results} results, back in the world");
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
