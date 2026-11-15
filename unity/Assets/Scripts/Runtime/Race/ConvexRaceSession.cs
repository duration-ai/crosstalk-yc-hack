using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Dogpatch.Backend;
using UnityEngine;
using UnityEngine.Networking;

namespace Dogpatch.Race
{
    // IRaceSession over the Convex race contract (backend/CONTRACT.md, "Speed Dial races"): one HTTP
    // call per method against https://<deployment>.convex.site. Requests and responses are the same
    // wire classes LocalRaceSession uses, so RaceDirector cannot tell the two apart. Every call runs
    // on the main thread through UnityWebRequest (safe under IL2CPP). Failures throw BackendException
    // with the contract's error code, or UNAVAILABLE when the server could not be reached.
    public sealed class ConvexRaceSession : IRaceSession
    {
        public const string DefaultUrl = "https://rapid-gnat-965.convex.site";
        public const string Unavailable = "UNAVAILABLE";

        public static string Mission = "yc";
        const double ReconnectingAfterMs = 1500.0;

        readonly string baseUrl;
        double offsetMs;
        bool synced;
        double failingSinceMs = -1.0;

        public ConvexRaceSession(string baseUrl, int timeoutSeconds = 3)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            TimeoutSeconds = timeoutSeconds;
        }

        public string BaseUrl => baseUrl;

        // Per request; a phone on a bad link fails fast and the loops retry with backoff.
        public int TimeoutSeconds { get; set; }

        // True once requests have been failing to reach the server for a moment.
        public bool Reconnecting => failingSinceMs >= 0.0 && LocalMs - failingSinceMs > ReconnectingAfterMs;

        // Raised on the first answer after Reconnecting, so callers can resync.
        public event Action Reconnected;

        sealed class Traffic
        {
            public int Count;
            public int Failed;
            public readonly List<double> Rtt = new List<double>();
        }

        readonly Dictionary<string, Traffic> traffic = new Dictionary<string, Traffic>();
        double trafficSince = -1.0;

        // Requests per second, round-trip p50/p95 and failures per endpoint since the last call.
        public string TakeTrafficStats()
        {
            double now = LocalMs;
            double seconds = trafficSince < 0.0 ? 1.0 : Math.Max(0.001, (now - trafficSince) / 1000.0);
            trafficSince = now;
            var parts = new List<string>();
            foreach (var pair in traffic)
            {
                var t = pair.Value;
                if (t.Count == 0) continue;
                t.Rtt.Sort();
                double p50 = t.Rtt.Count > 0 ? t.Rtt[t.Rtt.Count / 2] : 0.0;
                double p95 = t.Rtt.Count > 0 ? t.Rtt[Math.Min(t.Rtt.Count - 1, (int)(t.Rtt.Count * 0.95))] : 0.0;
                parts.Add($"{pair.Key} {t.Count / seconds:F1}/s rtt {p50:F0}/{p95:F0} ms{(t.Failed > 0 ? $" {t.Failed} failed" : "")}");
                t.Count = 0;
                t.Failed = 0;
                t.Rtt.Clear();
            }
            return string.Join(", ", parts);
        }

        void Count(string path, double rtt, bool ok)
        {
            if (trafficSince < 0.0) trafficSince = LocalMs;
            int q = path.IndexOf('?');
            var key = (q >= 0 ? path.Substring(0, q) : path).Replace("/api/", "");
            if (!traffic.TryGetValue(key, out var t)) traffic[key] = t = new Traffic();
            t.Count++;
            if (ok) t.Rtt.Add(rtt);
            else t.Failed++;
        }

        // The server's clock, estimated from every poll's serverTime plus half the round trip.
        public long ServerNowMs => (long)(LocalMs + offsetMs);

        static double LocalMs => Time.realtimeSinceStartupAsDouble * 1000.0;

        // The deployment from -convex-url, DOGPATCH_CONVEX_URL, or the dev deployment. Empty when the
        // game was started with -offline.
        public static string ConfiguredUrl()
        {
#if SPEEDDIAL_DEMO
            return "";
#else
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-offline") >= 0 || Array.IndexOf(args, "-speeddial-demo") >= 0) return "";
            int at = Array.IndexOf(args, "-convex-url");
            if (at >= 0 && at + 1 < args.Length) return args[at + 1];
            var env = Environment.GetEnvironmentVariable("DOGPATCH_CONVEX_URL");
            return string.IsNullOrWhiteSpace(env) ? DefaultUrl : env.Trim();
#endif
        }

        // ---- wire shapes for requests and errors ----

        [Serializable] public class CreatePlayerRequest { public string displayName; }
        [Serializable] public class PlayerResponse { public Player player; }
        [Serializable] public class CreateRoomRequest { public string playerId; public List<Vec3> route; public float finishRadius; public int minPlayers; public int asks; public List<Vec3> pickups; public float pickupRadius; }
        [Serializable] public class JoinRequest { public string playerId; public string code; }
        [Serializable] public class RoomRequest { public string playerId; public string roomId; }
        [Serializable] public class ReadyRequest { public string playerId; public string roomId; public bool ready; }
        [Serializable] public class UpdateRequest { public string playerId; public string roomId; public Vec3 position; public float heading; }
        [Serializable] public class CollectRequest { public string playerId; public string roomId; public int pickup; public Vec3 position; }
        [Serializable] public class FireRequest { public string playerId; public string roomId; public string type; public WorldChangeParams @params; }
        // Bumped whenever what presence means changes, so phones on another build can be told apart.
        public const int Protocol = 2;
        [Serializable] public class PresenceRequest { public string playerId; public Vec3 position; public float heading; public float speed; public string mode; public string vehicleType; public int protocol; public PresenceBump bump; }
        [Serializable] public class MissionRequest { public string playerId; public string mission; public List<Vec3> route; public float finishRadius; public int asks; public List<Vec3> pickups; public float pickupRadius; }
        [Serializable] public class ErrorBody { public ErrorDetail error; }
        [Serializable] public class ErrorDetail { public string code; public string message; }

        // One request as it goes on the wire; pure, so the mapping is testable without a network.
        public readonly struct Request
        {
            public readonly string Method;
            public readonly string Path;
            public readonly string Body;

            public Request(string method, string path, string body)
            {
                Method = method;
                Path = path;
                Body = body;
            }
        }

        public static Request CreatePlayer(string displayName) =>
            new Request("POST", "/api/players", JsonUtility.ToJson(new CreatePlayerRequest { displayName = displayName }));

        public static Request CreateRoom(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int minPlayers,
            int asks = 0, IReadOnlyList<Vec3> pickups = null, float pickupRadius = 0f)
        {
            var body = new CreateRoomRequest
            {
                playerId = playerId,
                route = route != null ? new List<Vec3>(route) : null,
                finishRadius = finishRadius,
                minPlayers = minPlayers,
                asks = asks,
                pickups = pickups != null ? new List<Vec3>(pickups) : null,
                pickupRadius = pickupRadius,
            };
            var json = JsonUtility.ToJson(body);
            if (route == null) json = json.Replace("\"route\":[],", "");
            if (finishRadius == 0f) json = json.Replace(",\"finishRadius\":0.0", "");
            if (minPlayers == 0) json = json.Replace(",\"minPlayers\":0", "");
            if (asks == 0) json = json.Replace(",\"asks\":0", "");
            if (pickups == null || pickups.Count == 0) json = json.Replace(",\"pickups\":[]", "");
            if (pickupRadius == 0f) json = json.Replace(",\"pickupRadius\":0.0", "");
            return new Request("POST", "/api/rooms", json);
        }

        public static Request Join(string playerId, string code) =>
            new Request("POST", "/api/rooms/join", JsonUtility.ToJson(new JoinRequest { playerId = playerId, code = (code ?? "").Trim().ToUpperInvariant() }));

        public static Request Leave(string playerId, string roomId) =>
            new Request("POST", "/api/rooms/leave", JsonUtility.ToJson(new RoomRequest { playerId = playerId, roomId = roomId }));

        public static Request Ready(string playerId, string roomId, bool ready) =>
            new Request("POST", "/api/rooms/ready", JsonUtility.ToJson(new ReadyRequest { playerId = playerId, roomId = roomId, ready = ready }));

        public static Request Poll(string roomId, long? after) =>
            new Request("GET", "/api/rooms/poll?roomId=" + Uri.EscapeDataString(roomId ?? "") + (after.HasValue ? "&after=" + after.Value.ToString(CultureInfo.InvariantCulture) : ""), null);

        public static Request Update(string playerId, string roomId, Vec3 position, float heading) =>
            new Request("POST", "/api/race/update", JsonUtility.ToJson(new UpdateRequest { playerId = playerId, roomId = roomId, position = position, heading = Mathf.Repeat(heading, 360f) }));

        public static Request Collect(string playerId, string roomId, int pickup, Vec3 position) =>
            new Request("POST", "/api/race/collect", JsonUtility.ToJson(new CollectRequest { playerId = playerId, roomId = roomId, pickup = pickup, position = position }));

        public static Request Fire(string playerId, string roomId, WorldChangeCommand command) =>
            new Request("POST", "/api/rooms/events", JsonUtility.ToJson(new FireRequest { playerId = playerId, roomId = roomId, type = command?.type, @params = command?.@params ?? new WorldChangeParams() }));

        public static Request Presence(string playerId, Vec3 position, float heading, float speed, string mode, string vehicleType, PresenceBump bump = null) =>
            new Request("POST", "/api/world/presence", JsonUtility.ToJson(new PresenceRequest { playerId = playerId, position = position, heading = Mathf.Repeat(heading, 360f), speed = speed, mode = mode, vehicleType = vehicleType ?? "", protocol = Protocol, bump = bump }));

        public static Request EnterMission(string playerId, string mission, IReadOnlyList<Vec3> route, float finishRadius, int asks, IReadOnlyList<Vec3> pickups, float pickupRadius)
        {
            var json = JsonUtility.ToJson(new MissionRequest { playerId = playerId, mission = mission, route = new List<Vec3>(route), finishRadius = finishRadius, asks = asks, pickups = pickups != null ? new List<Vec3>(pickups) : null, pickupRadius = pickupRadius });
            if (asks == 0) json = json.Replace(",\"asks\":0", "");
            if (pickups == null || pickups.Count == 0) json = json.Replace(",\"pickups\":[]", "");
            if (pickupRadius == 0f) json = json.Replace(",\"pickupRadius\":0.0", "");
            return new Request("POST", "/api/missions/enter", json);
        }

        public static Request LeaveWorld(string playerId) =>
            new Request("POST", "/api/world/leave", JsonUtility.ToJson(new JoinRequest { playerId = playerId, code = "" }));

        // Reads a response: the body as T on 2xx, the contract's error otherwise.
        public static T Parse<T>(long status, string text)
        {
            if (status >= 200 && status < 300) return JsonUtility.FromJson<T>(text);
            ErrorBody error = null;
            try { error = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<ErrorBody>(text); }
            catch (ArgumentException) { }
            var code = error?.error?.code;
            if (string.IsNullOrEmpty(code)) code = status >= 500 || status == 0 ? Unavailable : BackendErrorCodes.Internal;
            throw new BackendException(code, error?.error?.message ?? $"HTTP {status}");
        }

        async Task<T> Send<T>(Request request)
        {
            using (var web = new UnityWebRequest(baseUrl + request.Path, request.Method))
            {
                if (request.Body != null)
                {
                    web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
                    web.SetRequestHeader("Content-Type", "application/json");
                }
                web.downloadHandler = new DownloadHandlerBuffer();
                web.timeout = TimeoutSeconds;
                var done = new TaskCompletionSource<bool>();
                double sentAt = LocalMs;
                web.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;
                bool failed = web.result == UnityWebRequest.Result.ConnectionError || web.responseCode >= 500;
                Count(request.Path, LocalMs - sentAt, !failed);
                if (failed)
                {
                    if (failingSinceMs < 0.0) failingSinceMs = LocalMs;
                    if (web.result == UnityWebRequest.Result.ConnectionError) throw new BackendException(Unavailable, web.error);
                }
                else Answered();
                return Parse<T>(web.responseCode, web.downloadHandler.text);
            }
        }

        // ---- the WebSocket ----
        // With the socket up (ConvexSocket, Convex's sync protocol), presence goes over the socket as
        // the sync:presence mutation, everyone else in the shard is pushed by the sync:world
        // subscription (WorldPushed), and PollAsync returns the sync:room subscription's latest value
        // instead of polling. Any socket failure falls back to the HTTP calls above, per request.
        // On by default; DOGPATCH_CONVEX_WS=0, -no-ws or the pause menu's SOCKET chip turns it off.

        const string SocketKey = "speeddial.ws";
        const int PushWaitMs = 250;
        const double StatsEveryMs = 5000.0;

        static bool? socketEnabled;

        public static bool SocketEnabled
        {
            get
            {
                if (socketEnabled.HasValue) return socketEnabled.Value;
                var env = Environment.GetEnvironmentVariable("DOGPATCH_CONVEX_WS");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-no-ws") >= 0 || env == "0" || env == "off") socketEnabled = false;
                else socketEnabled = env == "1" || PlayerPrefs.GetInt(SocketKey, 1) == 1;
                return socketEnabled.Value;
            }
            set
            {
                socketEnabled = value;
                PlayerPrefs.SetInt(SocketKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Debug.Log("[Net] socket " + (value ? "on" : "off (HTTP polling)"));
            }
        }

        ConvexSocket socket;
        int worldQuery = -1;
        string worldArgs;
        WorldPresenceResponse lastWorld;
        List<WorldPlayer> pushedPlayers = new List<WorldPlayer>();
        int roomQuery = -1;
        string roomQueryRoom;
        PollResponse pushedRoom;
        long roomServerTime;
        TaskCompletionSource<bool> roomPushed;

        // Everyone else in the shard, raised on the main thread each time the server pushes a change.
        public event Action<WorldPresenceResponse> WorldPushed;

        public bool Pushed => socket != null && socket.Connected;
        public string Transport => Pushed ? "ws" : "http";

        ConvexSocket Socket()
        {
            if (!SocketEnabled)
            {
                if (socket == null) return null;
                socket.Dispose();
                socket = null;
                worldQuery = roomQuery = -1;
                worldArgs = roomQueryRoom = null;
                return null;
            }
            if (socket == null)
            {
                socket = new ConvexSocket(baseUrl);
                socket.QueryUpdated += OnPushed;
            }
            return socket.Connected ? socket : null;
        }

        void OnPushed(int queryId, string json)
        {
            if (queryId == worldQuery)
            {
                var world = JsonUtility.FromJson<WorldPresenceResponse>(json);
                pushedPlayers = world.players ?? new List<WorldPlayer>();
                if (lastWorld == null) return;
                world.shard = lastWorld.shard;
                world.worldRoomId = lastWorld.worldRoomId;
                world.you = lastWorld.you;
                pushes++;
                Measure(world);
                WorldPushed?.Invoke(world);
            }
            else if (queryId == roomQuery)
            {
                var poll = JsonUtility.FromJson<PollResponse>(json);
                if (poll.serverTime < roomServerTime) return;
                pushedRoom = poll;
                roomPushed?.TrySetResult(true);
            }
        }

        void Watch(WorldPresenceResponse world, string playerId)
        {
            lastWorld = world;
            string args = "{\"shard\":" + world.shard + ",\"playerId\":" + Dogpatch.Operator.JsonText.Quote(playerId) + "}";
            if (args == worldArgs || socket == null) return;
            if (worldQuery >= 0) socket.Unsubscribe(worldQuery);
            worldQuery = socket.Subscribe("sync:world", args);
            worldArgs = args;
        }

        async Task<PollResponse> PushedPollAsync(ConvexSocket s, string roomId)
        {
            if (roomQueryRoom != roomId)
            {
                if (roomQuery >= 0) s.Unsubscribe(roomQuery);
                roomQuery = s.Subscribe("sync:room", "{\"roomId\":" + Dogpatch.Operator.JsonText.Quote(roomId) + "}");
                roomQueryRoom = roomId;
                pushedRoom = null;
                roomServerTime = 0;
            }
            if (pushedRoom == null)
            {
                roomPushed = new TaskCompletionSource<bool>();
                await Task.WhenAny(roomPushed.Task, Task.Delay(PushWaitMs));
            }
            var poll = pushedRoom;
            pushedRoom = null;
            if (poll == null || roomQueryRoom != roomId) return null;
            roomServerTime = poll.serverTime;
            return poll;
        }

        // ---- update rate and age, logged every few seconds as [Net] for before/after comparisons ----

        readonly Dictionary<string, long> seenUpdate = new Dictionary<string, long>();
        int sends, pushes, remoteUpdates;
        double rttSum, ageSum, statsSinceMs = -1.0;

        void Measure(WorldPresenceResponse world)
        {
            long now = ServerNowMs;
            foreach (var p in world.players)
            {
                if (seenUpdate.TryGetValue(p.playerId, out var seen) && seen >= p.updatedAt) continue;
                seenUpdate[p.playerId] = p.updatedAt;
                remoteUpdates++;
                ageSum += now - p.updatedAt;
            }
            double local = LocalMs;
            if (statsSinceMs < 0.0) statsSinceMs = local;
            if (local - statsSinceMs < StatsEveryMs) return;
            double seconds = (local - statsSinceMs) / 1000.0;
            Debug.Log($"[Net] {Transport}: presence {sends / seconds:F1}/s sent, rtt {(sends > 0 ? rttSum / sends : 0):F0} ms; {pushes / seconds:F1} pushes/s; remote updates {remoteUpdates / seconds:F1}/s ({seenUpdate.Count} remotes), age at arrival {(remoteUpdates > 0 ? ageSum / remoteUpdates : 0):F0} ms");
            sends = pushes = remoteUpdates = 0;
            rttSum = ageSum = 0.0;
            statsSinceMs = local;
        }

        void Answered()
        {
            if (failingSinceMs < 0.0) return;
            bool was = Reconnecting;
            failingSinceMs = -1.0;
            if (!was) return;
            synced = false;
            Reconnected?.Invoke();
        }

        // ---- the contract ----

        public async Task<Player> CreatePlayerAsync(string displayName) => (await Send<PlayerResponse>(CreatePlayer(displayName))).player;

        public Task<RoomResponse> CreateRoomAsync(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int minPlayers,
            int asks = 0, IReadOnlyList<Vec3> pickups = null, float pickupRadius = 0f) =>
            Send<RoomResponse>(CreateRoom(playerId, route, finishRadius, minPlayers, asks, pickups, pickupRadius));

        public Task<RoomResponse> JoinRoomAsync(string playerId, string code) => Send<RoomResponse>(Join(playerId, code));

        public Task<LeaveResponse> LeaveRoomAsync(string playerId, string roomId) => Send<LeaveResponse>(Leave(playerId, roomId));

        public Task<ReadyResponse> SetReadyAsync(string playerId, string roomId, bool ready) => Send<ReadyResponse>(Ready(playerId, roomId, ready));

        public async Task<PollResponse> PollAsync(string roomId, long? after)
        {
            var s = roomId == "probe" ? null : Socket();
            if (s != null)
            {
                var pushed = await PushedPollAsync(s, roomId);
                if (pushed != null)
                {
                    Answered();
                    return pushed;
                }
            }
            double sent = LocalMs;
            var poll = await Send<PollResponse>(Poll(roomId, after));
            Sync(poll.serverTime, sent);
            if (roomQueryRoom == roomId) roomServerTime = Math.Max(roomServerTime, poll.serverTime);
            return poll;
        }

        public async Task<WorldPresenceResponse> PresenceAsync(string playerId, Vec3 position, float heading, float speed, string mode, string vehicleType, PresenceBump bump = null)
        {
            double sent = LocalMs;
            var request = Presence(playerId, position, heading, speed, mode, vehicleType, bump);
            WorldPresenceResponse world = null;
            var s = Socket();
            if (s != null)
            {
                try
                {
                    world = JsonUtility.FromJson<WorldPresenceResponse>(await s.MutationAsync("sync:presence", request.Body, TimeoutSeconds));
                    world.players = new List<WorldPlayer>(pushedPlayers);
                    Answered();
                }
                catch (BackendException e) when (e.Code == ConvexSocket.Unavailable)
                {
                    Debug.LogWarning("[Net] socket presence failed, using HTTP: " + e.Message);
                }
            }
            if (world == null) world = await Send<WorldPresenceResponse>(request);
            Sync(world.serverTime, sent);
            sends++;
            rttSum += LocalMs - sent;
            if (Pushed) Watch(world, playerId);
            else Measure(world);
            return world;
        }

        public Task<RoomResponse> EnterMissionAsync(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int asks, IReadOnlyList<Vec3> pickups, float pickupRadius) =>
            Send<RoomResponse>(EnterMission(playerId, Mission, route, finishRadius, asks, pickups, pickupRadius));

        public Task<LeaveResponse> LeaveWorldAsync(string playerId) => Send<LeaveResponse>(LeaveWorld(playerId));

        void Sync(long serverTime, double sent)
        {
            double arrived = LocalMs;
            double estimate = serverTime + (arrived - sent) * 0.5 - arrived;
            offsetMs = synced ? offsetMs + (estimate - offsetMs) * 0.2 : estimate;
            synced = true;
        }

        public Task<RaceUpdateResponse> UpdateAsync(string playerId, string roomId, Vec3 position, float heading) =>
            Send<RaceUpdateResponse>(Update(playerId, roomId, position, heading));

        public Task<AskResponse> CollectAskAsync(string playerId, string roomId, int pickup, Vec3 position) =>
            Send<AskResponse>(Collect(playerId, roomId, pickup, position));

        // The server spends asks inside operator.call, so there is no client endpoint for it.
        public Task<AskResponse> SpendAskAsync(string playerId, string roomId) =>
            throw new BackendException(RaceErrorCodes.NotImplemented, "Asks are spent by the Operator on the server");

        public Task<FireEventResponse> FireEventAsync(string playerId, string roomId, WorldChangeCommand command) =>
            Send<FireEventResponse>(Fire(playerId, roomId, command));
    }
}
