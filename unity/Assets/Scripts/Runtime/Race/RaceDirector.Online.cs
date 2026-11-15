using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Race
{
    // The online half: when the Convex backend answers at startup, this device joins the shared
    // world (POST /api/world/presence about 8 times a second) and sees everyone else in its shard.
    // The start ring is the mission: stepping into it joins the shard's open race room for the YC
    // mission (the server opens one when none is open), leaving the ring before the grid leaves it,
    // and about ten seconds after the results the player is back in free roam where they stopped.
    // When the backend is unreachable, or the game starts with -offline, nothing here runs and the
    // race stays local with Maya, Jun and Rosa.
    //
    // Command line, for headless checks: -convex-url <url>, -offline, -name <name>,
    // -speeddial-mission (walk into the ring on launch), -speeddial-autopilot.
    public sealed partial class RaceDirector
    {
        const string NameKey = "speeddial.name";
        const int ConnectTimeoutSeconds = 6;
        const int RequestTimeoutSeconds = 3;
        const float WorldEvery = 1f / 15f;
        const float WorldEveryRacing = 0.25f;
        const float TrafficLogEvery = 5f;
        const int MaxInFlight = 3;
        const float BackToWorldAfter = 10f;
        const float RingExitMargin = 3f;

        ConvexRaceSession convex;
        float nextMissionTry;
        bool ringArmed;

        public bool Online => convex != null;
        public string RoomCode { get; private set; }
        public bool Busy { get; private set; }
        public string LastError { get; private set; }
        public float LastErrorAt { get; private set; } = -10f;
        public RemoteRacers Remotes { get; private set; }
        public string MyName { get; private set; }
        public string ServerUrl => convex?.BaseUrl;

        // This shard's world room: everyone in the shard is a member, so voice effects called in
        // free roam reach the whole shard. Null until the first presence answer.
        public string WorldRoomId { get; private set; }
        public int WorldShard { get; private set; }
        public int WorldCount { get; private set; }

        // Requests have been failing for a moment; the HUD shows RECONNECTING and everything keeps
        // its last known state until the server answers again.
        public bool Reconnecting => convex != null && convex.Reconnecting;

        // Raised when this device enters or leaves a race room, or its world room changes: MyId,
        // RoomId and WorldRoomId are already current. OperatorRig follows it.
        public event Action RoomChanged;

        object Pause(float seconds) => Online ? (object)new WaitForSecondsRealtime(seconds) : new WaitForSeconds(seconds);

        static float Backoff(int failures) => Mathf.Min(0.25f * Mathf.Pow(2f, failures - 1), 2f) * UnityEngine.Random.Range(0.6f, 1.4f);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string PlayerKey(string url) => "speeddial.player." + url;

        // Reaches the backend and gets this device's server player, keeping the id between launches
        // so a relaunch rejoins as the same racer. Leaves Session unset when the backend is down.
        async Task ConnectAsync(string url)
        {
            var session = new ConvexRaceSession(url, ConnectTimeoutSeconds);
            MyName = Arg("-name") ?? PlayerPrefs.GetString(NameKey, "");
            if (string.IsNullOrWhiteSpace(MyName)) MyName = "Racer " + UnityEngine.Random.Range(10, 100);
            try
            {
                var id = Arg("-name") == null ? PlayerPrefs.GetString(PlayerKey(url), "") : "";
                if (string.IsNullOrEmpty(id))
                {
                    var player = await session.CreatePlayerAsync(MyName);
                    id = player.id;
                    PlayerPrefs.SetString(PlayerKey(url), id);
                    PlayerPrefs.SetString(NameKey, MyName);
                    PlayerPrefs.Save();
                }
                else
                {
                    try { await session.PollAsync("probe", null); }
                    catch (BackendException e) when (e.Code != ConvexRaceSession.Unavailable) { }
                }
                session.TimeoutSeconds = RequestTimeoutSeconds;
                session.Reconnected += () => Debug.Log("[SpeedDial] reconnected: resyncing room and world from the server");
                session.WorldPushed += ApplyWorld;
                convex = session;
                Session = session;
                MyId = id;
                Debug.Log($"[SpeedDial] online: {url} as {MyName} ({id})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpeedDial] race backend unreachable at {url} ({e.GetBaseException().Message}); racing offline with bots");
            }
        }

        // Player ids from another deployment or a wiped dev database come back NOT_FOUND: make a new one.
        async Task<T> AsPlayer<T>(Func<Task<T>> call)
        {
            try { return await call(); }
            catch (BackendException e) when (e.Code == BackendErrorCodes.NotFound && Online)
            {
                var player = await convex.CreatePlayerAsync(MyName);
                MyId = player.id;
                PlayerPrefs.SetString(PlayerKey(convex.BaseUrl), MyId);
                PlayerPrefs.Save();
                return await call();
            }
        }

        public void EnterMission() => Run(EnterMissionAsync());

        public void Leave() => Run(LeaveAsync());

        void Run(Task task) => StartCoroutine(Guard(task));

        IEnumerator Guard(Task task)
        {
            Busy = true;
            while (!task.IsCompleted) yield return null;
            Busy = false;
            if (!task.IsFaulted) yield break;
            var e = task.Exception.GetBaseException();
            LastError = e is BackendException b ? b.Code : "ERROR";
            LastErrorAt = Time.time;
            Debug.LogWarning("[SpeedDial] " + e.Message);
        }

        async Task EnterMissionAsync()
        {
            if (RoomId != null) return;
            var joined = await AsPlayer(() => convex.EnterMissionAsync(MyId, RouteWire(), Course.Data.finishRadius, AskTuning.startAsks, PickupWire(), AskTuning.pickupRadius));
            EnterRoom(joined, false);
            Debug.Log($"[SpeedDial] mission lobby {joined.room.code} ({joined.room.id}): {joined.room.playerCount} in the ring, {joined.room.status}, you are {joined.you.color} (slot {joined.you.slot})");
        }

        async Task LeaveAsync()
        {
            if (RoomId == null) return;
            string room = RoomId;
            RoomId = null;
            RoomCode = null;
            Last = null;
            cursor = null;
            Changes.ResetAll();
            Remotes.EndRace();
            ringArmed = false;
            Gridded = Started = ResultsIn = false;
            Game.ExternalInput = false;
            if (myPilot != null) myPilot.Active = false;
            if (Game.CurrentCar != null) await Game.ExitCar();
            Game.Follow.enabled = true;
            Game.Follow.Snap(Game.Player.transform, false);
            lobbyCam = false;
            RoomChanged?.Invoke();
            try { await Session.LeaveRoomAsync(MyId, room); }
            catch (BackendException e) { Debug.LogWarning($"[SpeedDial] leave: {e.Code}"); }
            Debug.Log("[SpeedDial] back in the world");
        }

        // Stepping into the start ring enters the mission; walking out of it before the grid, or
        // the results being up for a while, takes the player back to free roam.
        void MissionRing()
        {
            if (Busy || Reconnecting) return;
            var me = BodyOf(MyId).position;
            float d = Vector2.Distance(RoutePath.Flat(me), RoutePath.Flat(Course.RingCenter));
            if (RoomId == null)
            {
                if (d > Course.RingRadius + 1f) ringArmed = true;
                if (ringArmed && d < Course.RingRadius && Time.time >= nextMissionTry)
                {
                    nextMissionTry = Time.time + 2f;
                    EnterMission();
                }
                return;
            }
            if (Room == null) return;
            if (ResultsIn && Time.time - ResultsAt > BackToWorldAfter) Leave();
            else if (!Gridded && d > Course.RingRadius + RingExitMargin && (Room.status == RoomStatus.Lobby || (Room.status == RoomStatus.Countdown && MsToStart > LockMs + 500f))) Leave();
        }

        // Presence on a fixed clock: every 1/15 s (every 0.25 s while racing, when the race poll
        // carries the other racers), with up to MaxInFlight requests out at once so one slow round
        // trip never holds the next update back; the socket pushes everyone else in between. While
        // the link is down it drops to one request at a time with backoff.
        IEnumerator WorldLoop()
        {
            var pending = new List<Task<WorldPresenceResponse>>();
            int failures = 0;
            long newest = 0;
            float nextSend = 0f;
            float nextTrafficLog = Time.realtimeSinceStartup + TrafficLogEvery;
            var last = Game.Player.transform.position;
            float lastAt = Time.realtimeSinceStartup;
            while (true)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var done = pending[i];
                    if (!done.IsCompleted) continue;
                    pending.RemoveAt(i);
                    if (done.Status != TaskStatus.RanToCompletion)
                    {
                        if (++failures == 1) Debug.LogWarning("[SpeedDial] presence: " + done.Exception?.GetBaseException().Message);
                        nextSend = Time.realtimeSinceStartup + Backoff(failures);
                        continue;
                    }
                    if (failures > 0) Debug.Log($"[SpeedDial] presence back after {failures} failures");
                    failures = 0;
                    if (done.Result.serverTime < newest) continue;
                    newest = done.Result.serverTime;
                    ApplyWorld(done.Result);
                }
                float now = Time.realtimeSinceStartup;
                if ((now >= nextSend || presenceNow) && pending.Count < (failures > 0 ? 1 : MaxInFlight))
                {
                    var car = Game.CurrentCar;
                    var body = car != null ? car.transform : Game.Player.transform;
                    float speed = car != null ? Mathf.Abs(car.Speed) : Vector3.Distance(body.position, last) / Mathf.Max(now - lastAt, 1e-3f);
                    last = body.position;
                    lastAt = now;
                    var position = new Vec3(body.position.x, body.position.y, body.position.z);
                    var mode = car != null ? PlayerModes.Driving : PlayerModes.OnFoot;
                    var type = car != null ? VehicleRoster.TypeOf(car) : "";
                    float heading = body.eulerAngles.y;
                    var bump = now < outBumpUntil ? outBump : null;
                    presenceNow = false;
                    pending.Add(AsPlayer(() => convex.PresenceAsync(MyId, position, heading, speed, mode, type, bump)));
                    float every = Gridded && !ResultsIn ? WorldEveryRacing : WorldEvery;
                    nextSend = Mathf.Max(nextSend + every, now);
                }
                if (now >= nextTrafficLog)
                {
                    nextTrafficLog = now + TrafficLogEvery;
                    Debug.Log("[Net] " + convex.TakeTrafficStats() + $", remote delay {Remotes.DelayMs:F0} ms, buffers " + string.Join(" ", Remotes.All.Select(r => $"{r.Name} {r.BufferMs:F0}")));
                }
                yield return null;
            }
        }

        PresenceBump outBump;
        float outBumpUntil;
        bool presenceNow;

        // A hit this phone felt goes out at once and rides along with presence for a second, so the
        // other phone sees it even if one update is lost; it applies each bump once, by its time.
        public void SendBump(string target, Vector3 impulse)
        {
            outBump = new PresenceBump { target = target, x = impulse.x, z = impulse.z, at = NowMs };
            outBumpUntil = Time.realtimeSinceStartup + 1f;
            presenceNow = true;
        }

        // Headless bump check: sit in the race car where the player stands, so a ramming bot can hit it.
        IEnumerator SitInCar()
        {
            yield return Pause(3f);
            var p = Game.Player.transform;
            myCar.gameObject.SetActive(true);
            myCar.Place(p.position + p.right * 4f, p.eulerAngles.y);
            yield return Wait(Game.EnterCar(myCar));
            myCar.SetDriven(true);
            myCar.SetInput(Vector3.zero, 0f);
            Debug.Log("[SpeedDial] sitting in the car for the bump check");
        }

        void ApplyWorld(WorldPresenceResponse world)
        {
            if (world == null) return;
            WorldShard = world.shard;
            WorldCount = world.players.Count + 1;
            if (world.worldRoomId != WorldRoomId)
            {
                WorldRoomId = world.worldRoomId;
                Debug.Log($"[SpeedDial] in the world: shard {world.shard}, room {WorldRoomId}, {WorldCount} online, you are {world.you?.color}");
                RoomChanged?.Invoke();
            }
            Remotes.SyncWorld(world);
        }

        // Headless checks walk into the ring from the command line once the course is set up.
        void AutoRoom()
        {
            if (!Online) return;
            StartCoroutine(WorldLoop());
            if (Flag("-speeddial-mission")) Game.Player.Teleport(RingSpot(0), LobbyHeading);
            if (Flag("-speeddial-autopilot")) EnableAutopilot();
            if (Flag("-speeddial-sit-in-car")) StartCoroutine(SitInCar());
        }

        void OnApplicationQuit()
        {
            if (!Online) return;
            if (RoomId != null) _ = Session.LeaveRoomAsync(MyId, RoomId);
            _ = convex.LeaveWorldAsync(MyId);
        }

        // Racers this device does not drive, for the HUD's floating name tabs.
        public bool IsRemote(string playerId) => playerId != MyId && !bots.Any(b => b.Id == playerId);
    }
}
