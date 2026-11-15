using System;
using System.Collections.Generic;
using Dogpatch.Backend;
using Dogpatch.Race;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Contract tests for ConvexRaceSession's client mapping and RemoteRacers' interpolation, run
    // headless with no network:
    //   Unity -batchmode -nographics -quit -projectPath unity -executeMethod Dogpatch.EditorTools.ConvexRaceSessionTests.Run
    // Fixtures are verbatim responses from rapid-gnat-965 (2026-11-15). Exits 1 on any failure.
    public static class ConvexRaceSessionTests
    {
        const string PollJson = "{\"cursor\":3,\"events\":[{\"color\":\"purple\",\"displayName\":\"Rosa\",\"news\":\"ROSA MADE IT RAIN\",\"params\":{\"level\":\"on\"},\"playerId\":\"jd7b3hawrnmfsz1xhcx27gdkk98j905y\",\"raceNumber\":1,\"seq\":3,\"serverTime\":1794775392068,\"type\":\"rain\"}],\"racers\":[{\"color\":\"blue\",\"colorHex\":\"#2D8CFF\",\"displayName\":\"Probe\",\"finishMs\":16400,\"finished\":true,\"heading\":90.5,\"left\":false,\"metersToFinish\":0,\"place\":1,\"playerId\":\"jd753r3qdfcsq2r10rptde2x298j8ta8\",\"position\":{\"x\":148.2,\"y\":0.3,\"z\":201.9},\"progress\":350,\"ready\":true,\"slot\":0,\"smash\":0,\"updatedAt\":1794775232978}],\"results\":[{\"award\":\"PHOTO FINISH\",\"calls\":0,\"color\":\"blue\",\"displayName\":\"Probe\",\"finishMs\":16400,\"finished\":true,\"place\":1,\"playerId\":\"jd753r3qdfcsq2r10rptde2x298j8ta8\",\"smash\":0}],\"room\":{\"closesAt\":0,\"code\":\"CCGK\",\"createdAt\":1794775232978,\"finish\":{\"x\":0,\"y\":0,\"z\":800},\"finishRadius\":15,\"id\":\"js73t12b2jvjp8fneycdhhre1x8j9f1p\",\"minPlayers\":1,\"playerCount\":1,\"raceNumber\":1,\"readyCount\":0,\"routeLength\":800,\"startsAt\":1794775236000,\"status\":\"finished\"},\"serverTime\":1794775233117}";
        const string RoomJson = "{\"room\":{\"closesAt\":0,\"code\":\"CCGK\",\"createdAt\":1794775232978,\"finish\":{\"x\":0,\"y\":0,\"z\":800},\"finishRadius\":15,\"id\":\"js73t12b2jvjp8fneycdhhre1x8j9f1p\",\"minPlayers\":1,\"playerCount\":1,\"raceNumber\":1,\"readyCount\":0,\"routeLength\":800,\"startsAt\":0,\"status\":\"lobby\"},\"route\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":0,\"y\":0,\"z\":800}],\"you\":{\"color\":\"blue\",\"colorHex\":\"#2D8CFF\",\"displayName\":\"Probe\",\"finishMs\":0,\"finished\":false,\"heading\":0,\"left\":false,\"metersToFinish\":800,\"place\":1,\"playerId\":\"jd753r3qdfcsq2r10rptde2x298j8ta8\",\"position\":{\"x\":0,\"y\":0,\"z\":0},\"progress\":0,\"ready\":false,\"slot\":0,\"smash\":0,\"updatedAt\":1794775232978}}";

        static int passed;
        static readonly List<string> failures = new List<string>();

        static void Check(bool ok, string what)
        {
            if (ok) passed++;
            else failures.Add(what);
        }

        static void Throws(string code, Action action, string what)
        {
            try
            {
                action();
                failures.Add(what + ": no exception");
            }
            catch (BackendException e)
            {
                Check(e.Code == code, $"{what}: expected {code}, got {e.Code}");
            }
        }

        public static void Run()
        {
            var route = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(0, 0, 400), new Vec3(300, 0, 400) };

            var create = ConvexRaceSession.CreateRoom("p1", route, 15f, 2);
            Check(create.Method == "POST" && create.Path == "/api/rooms", "create: POST /api/rooms");
            Check(create.Body.Contains("\"playerId\":\"p1\"") && create.Body.Contains("\"route\":[{\"x\":0.0,\"y\":0.0,\"z\":0.0}") && create.Body.Contains("\"minPlayers\":2") && create.Body.Contains("\"finishRadius\":15.0"), "create body: " + create.Body);
            var bare = ConvexRaceSession.CreateRoom("p1", null, 0f, 0);
            Check(!bare.Body.Contains("route") && !bare.Body.Contains("finishRadius") && !bare.Body.Contains("minPlayers"), "create without options omits them: " + bare.Body);

            var join = ConvexRaceSession.Join("p1", " qmtx ");
            Check(join.Path == "/api/rooms/join" && join.Body == "{\"playerId\":\"p1\",\"code\":\"QMTX\"}", "join body: " + join.Body);
            Check(ConvexRaceSession.Leave("p1", "r1").Body == "{\"playerId\":\"p1\",\"roomId\":\"r1\"}", "leave body");
            Check(ConvexRaceSession.Ready("p1", "r1", true).Body == "{\"playerId\":\"p1\",\"roomId\":\"r1\",\"ready\":true}", "ready body");

            var first = ConvexRaceSession.Poll("k57 x", null);
            Check(first.Method == "GET" && first.Path == "/api/rooms/poll?roomId=k57%20x" && first.Body == null, "first poll omits after: " + first.Path);
            Check(ConvexRaceSession.Poll("k57", 7).Path == "/api/rooms/poll?roomId=k57&after=7", "poll passes the cursor back as after");

            var update = ConvexRaceSession.Update("p1", "r1", new Vec3(1.5f, 0.5f, 140f), -10f);
            Check(update.Path == "/api/race/update" && update.Body.Contains("\"position\":{\"x\":1.5,\"y\":0.5,\"z\":140.0}") && update.Body.Contains("\"heading\":350.0"), "update body, heading in 0..360: " + update.Body);

            var fire = ConvexRaceSession.Fire("p1", "r1", new WorldChangeCommand { type = "rain", @params = new WorldChangeParams { level = "on" } });
            Check(fire.Path == "/api/rooms/events" && fire.Body.Contains("\"type\":\"rain\"") && fire.Body.Contains("\"params\":{\"level\":\"on\""), "fire body: " + fire.Body);
            var ramp = ConvexRaceSession.Fire("p1", "r1", new WorldChangeCommand { type = "ramp" });
            Check(ramp.Body.Contains("\"params\":{"), "fire without params still sends the flat params object: " + ramp.Body);

            var poll = ConvexRaceSession.Parse<PollResponse>(200, PollJson);
            Check(poll.serverTime == 1794775233117L && poll.cursor == 3, "poll serverTime and cursor");
            Check(poll.room.code == "CCGK" && poll.room.status == RoomStatus.Finished && poll.room.startsAt == 1794775236000L && poll.room.finish.z == 800f, "poll room");
            Check(poll.racers.Count == 1 && poll.racers[0].color == "blue" && poll.racers[0].position.x == 148.2f && poll.racers[0].heading == 90.5f && poll.racers[0].finished && poll.racers[0].finishMs == 16400 && poll.racers[0].updatedAt == 1794775232978L, "poll racer");
            Check(poll.events.Count == 1 && poll.events[0].seq == 3 && poll.events[0].type == "rain" && poll.events[0].@params.level == "on" && poll.events[0].news == "ROSA MADE IT RAIN", "poll event");
            Check(poll.results.Count == 1 && poll.results[0].award == "PHOTO FINISH" && poll.results[0].place == 1, "poll result");

            var room = ConvexRaceSession.Parse<RoomResponse>(201, RoomJson);
            Check(room.room.id == "js73t12b2jvjp8fneycdhhre1x8j9f1p" && room.route.Count == 2 && room.you.slot == 0 && room.you.colorHex == "#2D8CFF", "create/join response");

            Throws(BackendErrorCodes.NotFound, () => ConvexRaceSession.Parse<RoomResponse>(404, "{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"No room with code ZZZZ\"}}"), "404 maps to NOT_FOUND");
            Throws(BackendErrorCodes.Conflict, () => ConvexRaceSession.Parse<RoomResponse>(409, "{\"error\":{\"code\":\"CONFLICT\",\"message\":\"Room is full\"}}"), "409 maps to CONFLICT");
            Throws(ConvexRaceSession.Unavailable, () => ConvexRaceSession.Parse<RoomResponse>(502, "<html>bad gateway</html>"), "a non-JSON 5xx is UNAVAILABLE");
            Throws(BackendErrorCodes.Internal, () => ConvexRaceSession.Parse<RoomResponse>(418, ""), "an unknown 4xx without a body is INTERNAL");

            var samples = new List<RemoteRacers.Sample>
            {
                new RemoteRacers.Sample { T = 1000, P = new Vector3(0, 0, 0), H = 350 },
                new RemoteRacers.Sample { T = 1100, P = new Vector3(10, 0, 0), H = 10 },
            };
            RemoteRacers.Pose(samples, 1050, out var mid, out float midH);
            Check(Vector3.Distance(mid, new Vector3(5, 0, 0)) < 1e-3f && Mathf.Abs(Mathf.DeltaAngle(midH, 0f)) < 1e-3f, $"interpolates position and heading across 0 deg: {mid} {midH}");
            RemoteRacers.Pose(samples, 1150, out var ahead, out _);
            Check(Vector3.Distance(ahead, new Vector3(15, 0, 0)) < 1e-3f, "extrapolates a late sample: " + ahead);
            RemoteRacers.Pose(samples, 2000, out var capped, out _);
            Check(Vector3.Distance(capped, new Vector3(35, 0, 0)) < 1e-3f, "extrapolation stops after 250 ms: " + capped);
            RemoteRacers.Pose(samples, 900, out var before, out _);
            Check(before == Vector3.zero, "holds the oldest sample before it");

            foreach (var f in failures) Debug.LogError("[RaceContractTests] FAIL " + f);
            Debug.Log($"[RaceContractTests] {passed} passed, {failures.Count} failed");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
