using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.Operator;
using Dogpatch.Slipstream;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // Speed Dial on one device: lays out the course, opens a room through IRaceSession, seats the
    // bots Maya, Jun and Rosa, and runs the loop from the poll: lobby on the start ring, the grid in
    // the last three seconds, the race to YC, results and the rematch. Everything a server would
    // own (rooms, readiness, countdown, places, events, results) only flows through IRaceSession and
    // IOperator, so swapping LocalRaceSession for a Convex client changes nothing here.
    public sealed partial class RaceDirector : MonoBehaviour
    {
        static readonly string[] BotNames = { "Maya", "Jun", "Rosa" };
        static readonly float[] BotSkill = { 0.95f, 0.91f, 0.87f };
        static readonly float[] RingLateral = { -0.9f, -2.7f, 0.9f, 2.7f };
        public static readonly string[] SlotHex = { "#2D8CFF", "#A35BFF", "#FF8A1E", "#2EC27E" };
        const float PollEvery = 0.1f;
        const float SendEvery = 1f / 15f;
        const float LockMs = 3000f;
        const float GhostFor = 4f;
        const float WreckHold = 1f;
        const float WreckFadeTime = 0.1f;
        const float ContactWatch = 10f;
        const float ClearRoadFor = 10f;
        const float ClearRoadMeters = 150f;
        const float ClearRoadRadius = 14f;

        public static RaceDirector Instance { get; private set; }

        public GameController Game { get; private set; }
        public RaceCourse Course { get; private set; }
        public WorldChangeCatalog Catalog { get; private set; }
        public IRaceSession Session { get; private set; }
        public IOperator Operator { get; private set; }
        public CallController Calls { get; private set; }
        public WorldChangeDirector Changes { get; private set; }
        public SlipstreamHud Hud { get; private set; }
        public ShoveInput Shove { get; private set; }
        public AskTuning AskTuning { get; private set; }
        public AskPickups Pickups { get; private set; }
        public string MyId { get; private set; }
        public string RoomId { get; private set; }
        public PollResponse Last { get; private set; }
        public bool Setup { get; private set; }
        public bool Gridded { get; private set; }
        public bool Started { get; private set; }
        public bool CrossedFinish { get; private set; }
        public string RouteId { get; private set; } = "";
        public bool Wrecking { get; private set; }
        public float WreckedAt { get; private set; } = -10f;
        public float WreckFade { get; private set; }
        public bool ResultsIn { get; private set; }
        public float StartedAt { get; private set; }
        public float ResultsAt { get; private set; }
        public bool Autopilot { get; private set; }
        public int MySlot { get; private set; }

        public event Action<RaceEvent> EventArrived;

        sealed class Bot
        {
            public string Name;
            public string Id;
            public int Slot;
            public CarController Car;
            public BotDriver Driver;
            public RacerAvatar Avatar;
            public float ReadyAt = -1f;
            public float NextCall;
            public float LastProgress;
            public float NextShove;
            public readonly HashSet<int> Considered = new HashSet<int>();
            public float DetourUntil;
            public float LaunchAt;
        }

        readonly List<Bot> bots = new List<Bot>();
        readonly System.Random random = new System.Random(2026);
        CarController myCar;
        BotDriver myPilot;
        long? cursor;
        int raceNumber;
        double epochMs;
        float stillFor;
        bool inRing;
        bool everInRing;
        float lobbySince;
        float botCallsAfter;
        bool lobbyCam;
        bool ghosted;
        bool roadCleared;
        int clearZone;
        bool contactsReported;
        readonly Dictionary<string, int> launchContacts = new Dictionary<string, int>();

        public long NowMs => convex != null ? convex.ServerNowMs : (long)(epochMs + Time.timeAsDouble * 1000.0);
        public Room Room => Last?.room;
        public Racer Me => Last?.racers?.FirstOrDefault(r => r.playerId == MyId);
        public float MsToStart => Room == null || Room.startsAt == 0 ? float.MaxValue : Room.startsAt - NowMs;
        public bool Locked => Gridded && !Started;

        // On the start ring, counting down or racing: the race owns the arrow and the route.
        public bool Joined => Setup && (Gridded || inRing || (Room != null && Room.status == RoomStatus.Countdown));
        public CarController MyCar => myCar;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance == null) new GameObject("SpeedDial").AddComponent<RaceDirector>();
        }

        void Awake()
        {
            Instance = this;
            epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        static IEnumerator Wait(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogError("[SpeedDial] " + task.Exception?.GetBaseException());
        }

        // Every route is its own mission with its own start ring, all live at once: the classic
        // course.json plus Resources/SpeedDial/course-<id>.json for each id in Routes. Online, driving
        // into another route's ring in free roam makes it the race course, and its mission (race-<id>)
        // keeps its lobby apart. "-speeddial-route b" picks the starting course for headless runs.
        public static readonly string[] Routes = { "", "a", "b", "c" };
        static string RouteChoice()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-speeddial-route");
            return i >= 0 && i + 1 < args.Length ? args[i + 1].Trim().ToLowerInvariant() : "";
        }

        readonly Dictionary<string, RaceCourse> courses = new Dictionary<string, RaceCourse>();
        Transform courseRoot;
        Transform ringsRoot;

        static string MissionOf(string route) => route.Length > 0 ? "race-" + route : "yc";

        public string RouteName(string route) => courses.TryGetValue(route, out var c) && !string.IsNullOrEmpty(c.Data.routeName) ? c.Data.routeName : "RACE";

        // Every route's ring with its name, for the HUD and the map: the race course's own and the rest.
        public IEnumerable<(string name, Vector3 at)> RouteRingSpots() => courses.Select(kv => (RouteName(kv.Key), kv.Value.RingCenter));

        // The race course's ring, finish and chevrons.
        void DressCourse()
        {
            var material = Game.SharedMaterial;
            courseRoot = new GameObject("SpeedDialCourse").transform;
            courseRoot.SetParent(Game.World.Root.transform, false);
            CourseDressing.StartRing(courseRoot, Course.RingCenter, Course.RingRadius, material);
            var finishRight = Quaternion.Euler(0f, Course.FinishHeading, 0f) * Vector3.right;
            float ycSide = Vector3.Dot(Course.YcSite - Course.Finish, finishRight) >= 0f ? 1f : -1f;
            var flagSpot = Course.Finish + finishRight * ycSide * (Course.FinishHalfWidth + 1.6f) + Quaternion.Euler(0f, Course.FinishHeading, 0f) * Vector3.forward * 3f;
            CourseDressing.Finish(courseRoot, Course.Finish, Course.FinishHeading, Course.FinishHalfWidth, flagSpot, material);
            CourseDressing.TurnChevrons(courseRoot, Course.Route, Course.RouteWorld, material);
        }

        public RaceCourse CourseOf(string route) => courses.TryGetValue(route, out var c) ? c : null;

        // True while an offline route switch rebuilds the local room.
        public bool Switching { get; private set; }

        // True while you're committed to the current route: online from entering its lobby, offline from
        // the countdown, until the results. The other routes' rings and tags hide and can't be picked.
        public bool InRace => Online ? RoomId != null : (Gridded && !ResultsIn) || Room?.status == RoomStatus.Countdown;

        readonly List<GameObject> otherRings = new List<GameObject>();

        // Every other route's start ring, beacon and all, the same as the race course's own, and a map
        // pin per route, online and offline.
        void RouteRings()
        {
            if (ringsRoot != null) Destroy(ringsRoot.gameObject);
            otherRings.Clear();
            ringsRoot = new GameObject("SpeedDialRouteRings").transform;
            ringsRoot.SetParent(Game.World.Root.transform, false);
            foreach (var kv in courses)
            {
                var pin = new GameObject(RouteName(kv.Key)).transform;
                pin.SetParent(ringsRoot, false);
                pin.position = kv.Value.RingCenter;
                Dogpatch.Map.MapMarkers.Track(pin, Dogpatch.Map.MapMarkerKind.Race);
                if (kv.Key != RouteId) otherRings.Add(CourseDressing.StartRing(ringsRoot, kv.Value.RingCenter, kv.Value.RingRadius, Game.SharedMaterial));
            }
        }

        // Driving or walking into another route's ring makes it the race course: online from free roam,
        // where the mission ring then enters its lobby as usual; offline whenever the local room isn't
        // counting down or racing, and the local room is rebuilt on the new route with its bots.
        void PickRouteByRing()
        {
            bool show = !InRace;
            foreach (var ring in otherRings) if (ring.activeSelf != show) ring.SetActive(show);
            if (Busy || Switching || InRace) return;
            var body = BodyOf(MyId);
            if (body == null) return;
            var me = RoutePath.Flat(body.position);
            foreach (var kv in courses)
            {
                if (kv.Key == RouteId || Vector2.Distance(me, RoutePath.Flat(kv.Value.RingCenter)) > kv.Value.RingRadius) continue;
                RouteId = kv.Key;
                Course = kv.Value;
                ConvexRaceSession.Mission = MissionOf(RouteId);
                Destroy(courseRoot.gameObject);
                DressCourse();
                Destroy(Pickups);
                Pickups = gameObject.AddComponent<AskPickups>();
                Pickups.Race = this;
                Pickups.Place(Course.AskPickups, AskTuning.pickupRadius, Game.SharedMaterial, courseRoot);
                RouteRings();
                RoomChanged?.Invoke();
                Debug.Log($"[SpeedDial] route {RouteName(RouteId)}: {Course.Data.startName} -> {Course.Data.finishName}, {Course.Route.Length:F0} m, mission {ConvexRaceSession.Mission}");
                if (!Online) StartCoroutine(NewLocalRoom());
                return;
            }
        }

        IEnumerator NewLocalRoom()
        {
            Switching = true;
            Changes.ResetAll();
            yield return Wait(RemoveBotsAsync());
            RoomId = null;
            var created = Session.CreateRoomAsync(MyId, RouteWire(), Course.Data.finishRadius, 2, AskTuning.startAsks, PickupWire(), AskTuning.pickupRadius);
            yield return Wait(created);
            EnterRoom(created.Result, true);
            yield return Wait(AddBotsAsync());
            Gridded = Started = ResultsIn = false;
            Game.ExternalInput = false;
            if (myPilot != null) myPilot.Active = false;
            if (Game.CurrentCar != null) yield return Wait(Game.ExitCar());
            PlaceForLobby(true);
            Switching = false;
            Debug.Log($"[SpeedDial] local room {created.Result.room.code} on {RouteName(RouteId)} with bots {string.Join("/", BotNames)}");
        }

        IEnumerator Start()
        {
            while ((Game = FindAnyObjectByType<GameController>()) == null || !Game.IsReady) yield return null;
            yield return Wait(SetupAsync());
            if (!Setup) yield break;
            StartCoroutine(PollLoop());
            StartCoroutine(SendLoop());
            AutoRoom();
        }

        async Task SetupAsync()
        {
            var material = Game.SharedMaterial;
            Catalog = WorldChangeCatalog.Load();
            var hero = Game.Cars.Values.First();
            bool Occupied(Vector2 p)
            {
                foreach (var hit in Physics.OverlapSphere(new Vector3(p.x, Dogpatch.World.RoadSurface.GroundY(p) + 1f, p.y), 1.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var car = hit.GetComponentInParent<CarController>();
                    if (car != null && car != hero) return true;
                }
                return false;
            }
            RouteId = Array.IndexOf(Routes, RouteChoice()) >= 0 ? RouteChoice() : "";
            AskTuning = JsonUtility.FromJson<AskTuning>(Resources.Load<TextAsset>("SpeedDial/asks").text);
            foreach (var id in Routes)
            {
                try
                {
                    var c = RaceCourse.Build(Game.World, Game.Data, Resources.Load<TextAsset>(id.Length > 0 ? "SpeedDial/course-" + id : "SpeedDial/course").text, Occupied);
                    c.PlaceAskPickups(AskTuning);
                    courses[id] = c;
                }
                catch (Exception e) { Debug.LogWarning($"[SpeedDial] route '{id}' failed to build: {e.Message}"); }
            }
            Course = courses[RouteId];
            ConvexRaceSession.Mission = MissionOf(RouteId);
            DressCourse();

            MyId = Game.Profile.id;
            var url = ConvexRaceSession.ConfiguredUrl();
            if (!string.IsNullOrEmpty(url)) await ConnectAsync(url);
            if (!Online) Session = new LocalRaceSession(id => Game.Backend.GetPlayerAsync(id), () => NowMs, Catalog);
            Operator = new LocalOperator(Session, Catalog);

            if (Game.CurrentCar != null) await Game.ExitCar();
            myCar = Game.Cars.Values.First();
            foreach (var car in Game.Cars.Values) CarFx.On(car);
            Remotes = gameObject.AddComponent<RemoteRacers>();
            Remotes.Race = this;

            Game.Player.gameObject.AddComponent<PlayerShoveable>();
            Dogpatch.Smash.VehicleDamage.Wrecked += OnWrecked;
            Changes = gameObject.AddComponent<WorldChangeDirector>();
            Changes.Game = Game;
            Changes.Race = this;
            Changes.Catalog = Catalog;

            Calls = gameObject.AddComponent<CallController>();
            Calls.Game = Game;
            Calls.Operator = Operator;
            Calls.PlayerId = () => MyId;
            Calls.RoomId = () => RoomId;
            Calls.Asks = () => Me?.asks ?? 0;
            Calls.Racing = () => Started && !ResultsIn;

            Pickups = gameObject.AddComponent<AskPickups>();
            Pickups.Race = this;
            Pickups.Place(Course.AskPickups, AskTuning.pickupRadius, material, courseRoot);
            RouteRings();

            Hud = gameObject.AddComponent<SlipstreamHud>();
            Hud.Race = this;
            Shove = gameObject.AddComponent<ShoveInput>();
            Shove.Game = Game;
            Shove.IgnoreTouch = Hud.HitTest;
            Game.Drag.IgnoreTouch = Hud.HitTest;

            Debug.Log($"[SpeedDial] {(Online ? "online at " + convex.BaseUrl : "offline")}: {Course.Data.startName} -> {Course.Data.finishName}, {Course.Route.Length:F0} m, ring at ({Course.RingCenter.x:F0},{Course.RingCenter.z:F0}), finish at ({Course.Finish.x:F0},{Course.Finish.z:F0})");
            if (!Online)
            {
                var created = await Session.CreateRoomAsync(MyId, RouteWire(), Course.Data.finishRadius, 2, AskTuning.startAsks, PickupWire(), AskTuning.pickupRadius);
                EnterRoom(created, true);
                await AddBotsAsync();
                Debug.Log($"[SpeedDial] local room {created.room.code} with bots {string.Join("/", BotNames)}");
            }
            PlaceForLobby(false);
            Setup = true;
        }

        List<Vec3> RouteWire() => Course.RouteWorld.Select(v => new Vec3(v.x, v.y, v.z)).ToList();

        List<Vec3> PickupWire() => Course.AskPickups.Select(v => new Vec3(v.x, v.y, v.z)).ToList();

        // Seats a room's racer, from creating or joining it: my slot, color and the poll cursor.
        void EnterRoom(RoomResponse room, bool host)
        {
            RoomId = room.room.id;
            RoomCode = room.room.code;
            raceNumber = room.room.raceNumber;
            cursor = null;
            MySlot = room.you.slot;
            PaintJob.For(myCar.gameObject, myCar.ModelId).SetBase(PaintJob.Linear(SlotHex[MySlot]));
            RoomChanged?.Invoke();
        }

        // Fills the room's empty slots with Maya, Jun and Rosa, driven on this device. Offline they
        // always race; online only when the host turns them on.
        async Task AddBotsAsync()
        {
            bool roster = Vehicles.VehicleCatalog.Has(myCar.ModelId);
            var carAsset = Game.Models.Get(roster ? Game.Models.Role("car")[0].id : myCar.ModelId);
            var avatarAsset = Game.Models.Get(Game.Models.Role("player")[0].id);
            var material = Game.SharedMaterial;
            foreach (var botName in BotNames)
            {
                if (bots.Any(b => b.Name == botName)) continue;
                if (Last != null && Last.room.id == RoomId && Last.racers.Count(r => !r.left) >= 4) break;
                var player = Online ? await convex.CreatePlayerAsync(botName) : await Game.Backend.CreatePlayerAsync(botName);
                var joined = await Session.JoinRoomAsync(player.id, RoomCode);
                var bot = new Bot { Name = botName, Id = player.id, Slot = joined.you.slot };
                var spot = Course.Grid[bot.Slot];
                bot.Car = CarController.Create("bot-" + bot.Name.ToLowerInvariant(), carAsset, material, spot.Position, spot.Heading);
                if (roster) Vehicles.VehicleRoster.SwapInto(bot.Car, myCar.ModelId);
                bot.Car.name = "Car " + bot.Name;
                bot.Car.transform.SetParent(Game.World.Root.transform, true);
                CarFx.On(bot.Car);
                PaintJob.For(bot.Car.gameObject, bot.Car.ModelId).SetBase(PaintJob.Linear(SlotHex[bot.Slot]));
                bot.Driver = bot.Car.gameObject.AddComponent<BotDriver>();
                bot.Driver.name = bot.Name;
                bot.Driver.Car = bot.Car;
                bot.Driver.Route = Course.Route;
                bot.Driver.Skill = BotSkill[Array.IndexOf(BotNames, botName)];
                bot.Driver.Lane = RaceCourse.GridLane(bot.Slot);
                bot.Avatar = RacerAvatar.Create(avatarAsset, material, PaintJob.Linear(SlotHex[bot.Slot]), bot.Name);
                bot.Avatar.transform.SetParent(Game.World.Root.transform, true);
                bot.Car.SetDriven(false);
                bot.Driver.ResetProgress(spot.Progress);
                bot.LastProgress = spot.Progress;
                bot.Avatar.Stand(RingSpot(bot.Slot), LobbyHeading);
                bots.Add(bot);
                Debug.Log($"[SpeedDial] bot {bot.Name} joined as slot {bot.Slot}");
            }
        }

        async Task RemoveBotsAsync()
        {
            foreach (var bot in bots.ToList())
            {
                bots.Remove(bot);
                if (bot.Car != null) Destroy(bot.Car.gameObject);
                if (bot.Avatar != null) Destroy(bot.Avatar.gameObject);
                try { await Session.LeaveRoomAsync(bot.Id, RoomId); }
                catch (BackendException e) { Debug.LogWarning($"[SpeedDial] {bot.Name} could not leave: {e.Code}"); }
            }
        }

        Vector3 LobbyRight => Vector3.Cross(Vector3.up, -Course.RingFacing);
        float LobbyHeading => RoutePath.Heading(new Vector2(Course.RingFacing.x, Course.RingFacing.z));

        public Vector3 RingSpot(int slot)
        {
            var p = Course.RingCenter + LobbyRight * RingLateral[slot];
            return new Vector3(p.x, Course.GroundY(new Vector2(p.x, p.z)) + 0.06f, p.z);
        }

        // Online players start 32 to 40 m from the ring, facing it, at a spot picked from their id so
        // phones launching together don't stack. Spots on the ring's open side come first.
        public Vector3 SpawnSpot(out float facing)
        {
            var center = RoutePath.Flat(Course.RingCenter);
            var open = RoutePath.Flat(Course.RingFacing);
            var spots = new List<Vector2>();
            var fallback = new List<Vector2>();
            foreach (float r in new[] { 32f, 36f, 40f })
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * Mathf.PI / 12f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var p = center + dir * r;
                    if (!Course.StandFits(p)) continue;
                    (Vector2.Dot(dir, open) > 0.5f ? spots : fallback).Add(p);
                }
            if (spots.Count == 0) spots = fallback;
            var at = center + open * 34f;
            if (spots.Count > 0) at = spots[(int)((uint)(MyId ?? "").GetHashCode() % (uint)spots.Count)];
            facing = RoutePath.Heading(center - at);
            Debug.Log($"[SpeedDial] spawned {Vector2.Distance(at, center):F0} m from the ring at ({at.x:F0},{at.y:F0}), one of {spots.Count} clear spots");
            return new Vector3(at.x, Course.GroundY(at) + 0.1f, at.y);
        }

        void PlaceForLobby(bool onRing)
        {
            foreach (var bot in bots)
            {
                var s = Course.Grid[bot.Slot];
                bot.Car.SetDriven(false);
                bot.Car.Place(s.Position, s.Heading);
                bot.Driver.Active = false;
                bot.Driver.Route = Course.Route;
                bot.Driver.ResetProgress(s.Progress);
                bot.LastProgress = s.Progress;
                bot.ReadyAt = -1f;
                bot.Considered.Clear();
                bot.Avatar.gameObject.SetActive(true);
                bot.Avatar.Stand(RingSpot(bot.Slot), LobbyHeading);
            }
            var mine = Course.Grid[MySlot];
            myCar.Place(mine.Position, mine.Heading);
            if (onRing) Game.Player.Teleport(RingSpot(MySlot), LobbyHeading);
            else if (Online) Game.Player.Teleport(SpawnSpot(out float facing), facing);
            else Game.Player.Teleport(Course.RingCenter + Course.RingFacing * (Course.RingRadius + 2.5f) + Vector3.up * 0.1f, LobbyHeading + 180f);
            Game.Follow.enabled = true;
            Game.Follow.Snap(Game.Player.transform, false);
            lobbySince = Time.time;
            everInRing = onRing;
        }

        // One poll in flight at a time; while the server can't be reached the last state stays up
        // and the next try backs off with jitter.
        IEnumerator PollLoop()
        {
            int failures = 0;
            while (true)
            {
                if (RoomId == null)
                {
                    yield return null;
                    continue;
                }
                string polled = RoomId;
                float pollStarted = Time.realtimeSinceStartup;
                var poll = Session.PollAsync(polled, cursor);
                while (!poll.IsCompleted) yield return null;
                if (poll.Status != TaskStatus.RanToCompletion)
                {
                    if (++failures == 1) Debug.LogWarning("[SpeedDial] poll: " + poll.Exception?.GetBaseException().Message);
                    yield return Pause(Backoff(failures));
                    continue;
                }
                failures = 0;
                if (polled == RoomId) yield return Handle(poll.Result);
                float pollWait = PollEvery - (Time.realtimeSinceStartup - pollStarted);
                if (pollWait > 0f) yield return Pause(pollWait);
                else yield return null;
            }
        }

        IEnumerator Handle(PollResponse poll)
        {
            Last = poll;
            Remotes.Sync(poll);
            var room = poll.room;
            bool rematch = room.raceNumber != raceNumber;
            if (rematch)
            {
                raceNumber = room.raceNumber;
                yield return BackToLobby();
            }
            foreach (var e in poll.events)
            {
                if (cursor.HasValue && e.seq <= cursor.Value) continue;
                Changes.Apply(e);
                EventArrived?.Invoke(e);
            }
            cursor = poll.cursor;
            if (rematch) yield break;

            if (room.status == RoomStatus.Lobby || room.status == RoomStatus.Countdown)
            {
                if (room.status == RoomStatus.Countdown && MsToStart <= LockMs && !Gridded) yield return ToGrid();
                bool go = (Me != null && Me.ready) || (!Online && (everInRing || Time.time - lobbySince > 25f));
                if (go && !Gridded)
                    for (int i = 0; i < bots.Count; i++)
                    {
                        var bot = bots[i];
                        var racer = poll.racers.FirstOrDefault(r => r.playerId == bot.Id);
                        if (racer != null && !racer.ready && bot.ReadyAt < 0f) bot.ReadyAt = Time.time + 0.8f + i * 1.1f;
                    }
            }
            else if (room.status == RoomStatus.Racing)
            {
                if (!Gridded) yield return ToGrid();
                if (!Started) Go();
                if (!CrossedFinish && Me != null && Me.finished) CrossedLine();
            }
            else if (room.status == RoomStatus.Finished && !ResultsIn)
            {
                ResultsIn = true;
                ResultsAt = Time.time;
                foreach (var bot in bots) bot.Driver.Active = false;
                if (myPilot != null) myPilot.Active = false;
                Game.ExternalInput = false;
                Debug.Log("[SpeedDial] results: " + string.Join(", ", poll.results.Select(r => $"{r.place}. {r.displayName} {(r.finished ? (r.finishMs / 1000f).ToString("F1") + "s" : "DNF")} {r.award} ({r.calls} calls)")));
            }
        }

        IEnumerator ToGrid()
        {
            Gridded = true;
            Game.ExternalInput = true;
            Calls.EndTalk();
            if (Game.CurrentCar != null && Game.CurrentCar != myCar) yield return Wait(Game.ExitCar());
            if (!myCar.gameObject.activeSelf)
            {
                myCar.gameObject.SetActive(true);
                Debug.Log("[SpeedDial] your race car was switched off where the last race left it; switched it back on");
            }
            Fresh(myCar);
            var spot = Course.Grid[MySlot];
            if (Game.CurrentCar != myCar)
            {
                myCar.Place(spot.Position, spot.Heading);
                var side = Quaternion.Euler(0f, spot.Heading, 0f) * Vector3.left * 2.2f;
                Game.Player.Teleport(spot.Position + side, spot.Heading);
                yield return Wait(Game.EnterCar(myCar));
            }
            myCar.Place(spot.Position, spot.Heading);
            myCar.SetDriven(true);
            myCar.SetInput(Vector3.zero, 0f);
            Game.Follow.enabled = true;
            Game.Follow.Snap(myCar.transform, true);
            lobbyCam = false;
            foreach (var bot in bots)
            {
                var s = Course.Grid[bot.Slot];
                bot.Avatar.gameObject.SetActive(false);
                Fresh(bot.Car);
                bot.Car.Place(s.Position, s.Heading);
                bot.Car.SetDriven(true);
                bot.Driver.Route = Course.Route;
                bot.Driver.ResetProgress(s.Progress);
                bot.LastProgress = s.Progress;
                bot.Driver.Active = false;
                bot.Considered.Clear();
            }
            Debug.Log("[SpeedDial] racers on the grid");
        }

        void Go()
        {
            Started = true;
            CrossedFinish = false;
            StartedAt = Time.time;
            botCallsAfter = Time.time + 4f;
            foreach (var bot in bots)
            {
                bot.LaunchAt = Time.time + 0.2f + (float)random.NextDouble() * 0.4f;
                bot.NextCall = Time.time + 5f + (float)random.NextDouble() * 6f;
            }
            Ghost(true);
            launchContacts.Clear();
            contactsReported = false;
            foreach (var car in RacerCars())
            {
                var watch = car.GetComponent<LaunchContacts>() ?? car.gameObject.AddComponent<LaunchContacts>();
                watch.Race = this;
                watch.Until = Time.time + ContactWatch;
            }
            Game.ExternalInput = Autopilot;
            Calls.Buzz();
            if (myPilot != null) myPilot.Active = Autopilot;
            Debug.Log("[SpeedDial] GO");
        }

        // Over the line: your race is done, so the car rolls to a stop while the others finish or the
        // room closes (30 s after the first finisher), and then the results come up.
        void CrossedLine()
        {
            CrossedFinish = true;
            if (myPilot != null) myPilot.Active = false;
            Game.ExternalInput = true;
            if (Game.CurrentCar != null) Game.CurrentCar.SetInput(Vector3.zero, 0f);
            else Game.Player.SetInput(Vector3.zero, 0f);
            float closesIn = Room.closesAt > 0 ? (Room.closesAt - NowMs) / 1000f : 0f;
            Debug.Log($"[SpeedDial] you finished {Me.place} in {Me.finishMs / 1000f:F1} s, results in {closesIn:F0} s at most");
        }

        // For the first seconds after GO the racers pass through each other, so a bunched launch can't
        // pile up.
        void Ghost(bool on)
        {
            ghosted = on;
            var cars = RacerCars().Where(c => c != null).Select(c => c.GetComponentsInChildren<Collider>()).ToList();
            for (int i = 0; i < cars.Count; i++)
                for (int j = i + 1; j < cars.Count; j++)
                    foreach (var a in cars[i])
                        foreach (var b in cars[j])
                            if (a != null && b != null) Physics.IgnoreCollision(a, b, on);
        }

        // Traffic stays off the grid and the first stretch of the route from the countdown until a few
        // seconds after GO; a countdown that is called off reopens it straight away.
        void ClearRoad(bool on)
        {
            roadCleared = on;
            if (!on)
            {
                if (clearZone != 0) Dogpatch.Traffic.TrafficSystem.EndZone(clearZone);
                clearZone = 0;
                return;
            }
            var path = new List<Vector3>();
            float end = Mathf.Min(Course.Grid[0].Progress + ClearRoadMeters, Course.Route.Length);
            for (float d = Mathf.Max(0f, Course.Grid[3].Progress - 6f); d <= end; d += 6f)
            {
                var p = Course.Route.PointAt(d);
                path.Add(new Vector3(p.x, 0f, p.y));
            }
            float countdown = MsToStart == float.MaxValue ? 30f : Mathf.Max(0f, MsToStart / 1000f);
            clearZone = Dogpatch.Traffic.TrafficSystem.ClearZone(path, ClearRoadRadius, countdown + ClearRoadFor + 5f);
        }

        void OnDestroy()
        {
            Dogpatch.Smash.VehicleDamage.Wrecked -= OnWrecked;
            ClearRoad(false);
        }

        // A wreck doesn't end anyone's race. A bot's car is repaired and back on the route a moment
        // later. Yours is a beat: you're thrown out and roll, then stand by the smoking wreck with the
        // camera on it and WRECKED up for 1 s, a quick fade, and you're
        // back on the route where you were in the same car, repaired. Getting out on purpose is not a
        // wreck, so it never respawns you.
        void OnWrecked(CarController car)
        {
            if (!Started || ResultsIn || !IsRacerCar(car)) return;
            StartCoroutine(car == myCar ? PlayerWreck(car) : Recover(car));
        }

        IEnumerator Recover(CarController car)
        {
            yield return new WaitForSeconds(1.5f);
            if (!ResultsIn) yield return PutBack(car);
        }

        IEnumerator PlayerWreck(CarController car)
        {
            Wrecking = true;
            WreckedAt = Time.time;
            Game.ExternalInput = true;
            Game.Follow.Target = car.transform;
            while (!ResultsIn)
            {
                if (Time.time - WreckedAt >= WreckHold) break;
                if (Game.CurrentCar == null && Game.Player.enabled) Game.Player.SetInput(Vector3.zero, 0f);
                yield return null;
            }
            for (float t = 0f; t < WreckFadeTime && !ResultsIn; t += Time.deltaTime)
            {
                WreckFade = t / WreckFadeTime;
                yield return null;
            }
            if (!ResultsIn)
            {
                WreckFade = 1f;
                yield return PutBack(car);
                Game.ExternalInput = Autopilot || CrossedFinish;
            }
            Wrecking = false;
            for (float t = 0f; t < WreckFadeTime; t += Time.deltaTime)
            {
                WreckFade = Mathf.Min(WreckFade, 1f - t / WreckFadeTime);
                yield return null;
            }
            WreckFade = 0f;
        }

        // A racer's car made race-ready at the grid and on every respawn: full health with any wreck,
        // dents and fire undone, no stun, enabled and with no input held.
        public static void Fresh(CarController car)
        {
            if (car.TryGetComponent<Dogpatch.Smash.VehicleDamage>(out var damage) && (damage.IsWrecked || damage.Damage > 0f)) damage.Repair();
            car.ClearStun();
            car.enabled = true;
            car.SetInput(Vector3.zero, 0f);
        }

        IEnumerator PutBack(CarController car)
        {
            Fresh(car);
            var bot = bots.FirstOrDefault(b => b.Car == car);
            float d = Mathf.Clamp(Course.Route.Project(RoutePath.Flat(car.transform.position)), 2f, Course.Route.Length - 3f);
            var p = Course.Route.PointAt(d) + Course.Route.RightAt(d) * RaceCourse.GridLane(bot != null ? bot.Slot : MySlot);
            var spot = new Vector3(p.x, Mathf.Max(Dogpatch.World.RoadSurface.GroundY(p), Course.RouteY(d)) + 0.4f, p.y);
            float heading = RoutePath.Heading(Course.Route.DirectionAt(d));
            if (bot != null)
            {
                MoveBody(bot.Id, spot, heading);
            }
            else
            {
                if (Game.CurrentCar != null && Game.CurrentCar != myCar) yield return Wait(Game.ExitCar());
                if (Game.CurrentCar != myCar)
                {
                    Game.Player.Teleport(spot + Quaternion.Euler(0f, heading, 0f) * Vector3.left * 2.2f, heading);
                    myCar.Place(spot, heading);
                    yield return Wait(Game.EnterCar(myCar));
                }
                MoveBody(MyId, spot, heading);
                Game.Follow.Snap(myCar.transform, true);
            }
            Debug.Log($"[SpeedDial] {(bot != null ? bot.Name : "You")} wrecked, back on the route at {d:F0} m");
        }

        public bool IsRacerCar(CarController car) => RacerCars().Contains(car);

        public void NoteLaunchContact(string kind, CarController a, CarController b)
        {
            launchContacts.TryGetValue(kind, out int n);
            launchContacts[kind] = n + 1;
            var p = RoutePath.Flat(b.transform.position);
            float along = Course.Route.Project(p);
            float off = Vector2.Dot(p - Course.Route.PointAt(along), Course.Route.RightAt(along));
            Debug.Log($"[SpeedDial] launch contact {kind}: {a.name} hit {b.name} at {Time.time - StartedAt:F1} s, {along:F0} m along the route and {off:+0.0;-0.0} m off it");
        }

        IEnumerator BackToLobby()
        {
            Debug.Log($"[SpeedDial] rematch: race {raceNumber}");
            Changes.ResetAll();
            RoomChanged?.Invoke();
            Gridded = Started = ResultsIn = false;
            Game.ExternalInput = false;
            if (myPilot != null) myPilot.Active = false;
            if (Game.CurrentCar != null) yield return Wait(Game.ExitCar());
            PlaceForLobby(true);
        }

        // This device's racers on a fixed clock, up to two rounds of updates out at once so a slow
        // round trip never holds the next position back; one at a time with backoff while failing.
        IEnumerator SendLoop()
        {
            var pending = new List<Task>();
            int failures = 0;
            float nextSend = 0f;
            while (true)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var done = pending[i];
                    if (!done.IsCompleted) continue;
                    pending.RemoveAt(i);
                    if (done.Status == TaskStatus.RanToCompletion)
                    {
                        failures = 0;
                        continue;
                    }
                    if (++failures == 1) Debug.LogWarning("[SpeedDial] update: " + done.Exception?.GetBaseException().Message);
                    nextSend = Time.realtimeSinceStartup + Backoff(failures);
                }
                float now = Time.realtimeSinceStartup;
                if (RoomId != null && now >= nextSend && pending.Count < (failures > 0 ? 1 : 2))
                {
                    var sends = new List<Task>();
                    var me = BodyOf(MyId);
                    if (me != null) sends.Add(Session.UpdateAsync(MyId, RoomId, new Vec3(me.position.x, me.position.y, me.position.z), me.eulerAngles.y));
                    foreach (var bot in bots)
                    {
                        var t = BodyOf(bot.Id);
                        sends.Add(Session.UpdateAsync(bot.Id, RoomId, new Vec3(t.position.x, t.position.y, t.position.z), t.eulerAngles.y));
                    }
                    pending.Add(Task.WhenAll(sends));
                    nextSend = Mathf.Max(nextSend + SendEvery, now);
                }
                yield return null;
            }
        }

        public void EnableAutopilot()
        {
            Autopilot = true;
            if (myPilot == null)
            {
                myPilot = myCar.gameObject.AddComponent<BotDriver>();
                myPilot.name = "You";
                myPilot.Car = myCar;
                myPilot.Route = Course.Route;
                myPilot.Skill = 1f;
                myPilot.Lane = RaceCourse.GridLane(MySlot);
                myPilot.ResetProgress(Course.Grid[MySlot].Progress);
            }
            if (Started && !ResultsIn)
            {
                Game.ExternalInput = true;
                myPilot.Active = true;
            }
        }

        // Lets go of the wheel without leaving autopilot, for the scripted play-through's detours.
        public void HoldAutopilot(bool hold)
        {
            if (myPilot != null) myPilot.Active = !hold && Started && !ResultsIn;
        }

        public void SetReady(bool ready) => StartCoroutine(Wait(SetReadyAsync(ready)));

        async Task SetReadyAsync(bool ready)
        {
            try
            {
                await Session.SetReadyAsync(MyId, RoomId, ready);
                Debug.Log(ready ? "[SpeedDial] you are ready" : "[SpeedDial] you are not ready");
            }
            catch (BackendException e)
            {
                Debug.LogWarning($"[SpeedDial] ready refused: {e.Code} {e.Message}");
            }
        }

        public IEnumerable<Transform> RacerBodies()
        {
            var me = BodyOf(MyId);
            if (me != null) yield return me;
            foreach (var bot in bots) yield return BodyOf(bot.Id);
            foreach (var remote in Remotes.All)
                if (remote.Body != null) yield return remote.Body;
        }

        // Every racer's body in the world: mine, this device's bots, and remote players.
        public Transform BodyOf(string playerId)
        {
            if (playerId == MyId) return Game.CurrentCar != null ? Game.CurrentCar.transform : Game.Player.transform;
            var bot = bots.FirstOrDefault(b => b.Id == playerId);
            if (bot == null) return Remotes.Get(playerId)?.Body;
            return Gridded ? bot.Car.transform : bot.Avatar.transform;
        }

        public bool IsLocalBot(string playerId) => bots.Any(b => b.Id == playerId);

        public void MoveBody(string playerId, Vector3 position, float heading)
        {
            if (playerId == MyId)
            {
                if (Game.CurrentCar != null)
                {
                    Game.CurrentCar.Place(position, heading);
                    Game.CurrentCar.SetDriven(true);
                }
                else Game.Player.Teleport(position, heading);
                if (myPilot != null) myPilot.ResetProgress(Course.Route.Project(RoutePath.Flat(position)));
                return;
            }
            var bot = bots.FirstOrDefault(b => b.Id == playerId);
            if (bot == null) return;
            if (!Gridded)
            {
                bot.Avatar.transform.position = position;
                return;
            }
            bot.Car.Place(position, heading);
            bot.Car.SetDriven(true);
            bot.Driver.Route = Course.Route;
            bot.Driver.ResetProgress(Course.Route.Project(RoutePath.Flat(position)));
            bot.LastProgress = bot.Driver.Progress;
        }

        public float ProgressOf(string playerId)
        {
            var racer = Last?.racers?.FirstOrDefault(r => r.playerId == playerId);
            return racer != null ? racer.progress : 0f;
        }

        public string NameOf(string playerId) => playerId == MyId ? "You" : bots.FirstOrDefault(b => b.Id == playerId)?.Name ?? Remotes.Get(playerId)?.Name ?? "";

        // Makes a bot call in a world change without spending an ask, as the scripted play-through
        // does; null picks one.
        public void BotCall(string botName, WorldChangeCommand command)
        {
            var bot = bots.FirstOrDefault(b => b.Name == botName);
            if (bot != null) StartCoroutine(Wait(BotCallAsync(bot, command)));
        }

        async Task BotCallAsync(Bot bot, WorldChangeCommand command)
        {
            if (command == null)
            {
                var roulette = Catalog.Roulette();
                command = roulette[random.Next(roulette.Count)];
            }
            command = WorldChangeCatalog.Command(command.type, command.@params);
            if (command.type == "swap")
            {
                var racers = Last.racers.Where(r => !r.left).OrderBy(r => r.place).ToList();
                int i = racers.FindIndex(r => r.playerId == bot.Id);
                var target = i > 0 ? racers[i - 1] : racers.Count > 1 ? racers[1] : null;
                if (target == null) return;
                command.@params.targetPlayerId = target.playerId;
            }
            try
            {
                var fired = await Session.FireEventAsync(bot.Id, RoomId, command);
                Debug.Log($"[SpeedDial] {bot.Name} called in {fired.@event.type}: {fired.@event.news}");
            }
            catch (BackendException e)
            {
                Debug.LogWarning($"[SpeedDial] {bot.Name}'s call refused: {e.Code} {e.Message}");
            }
        }

        void Update()
        {
            if (!Setup) return;
            if (Locked) myCar.SetInput(Vector3.zero, 0f);
            PickRouteByRing();

            foreach (var bot in bots)
            {
                if (bot.ReadyAt <= 0f || Time.time < bot.ReadyAt) continue;
                bot.ReadyAt = 0f;
                StartCoroutine(Wait(BotReadyAsync(bot)));
            }

            var player = Game.Player.transform.position;
            inRing = Game.CurrentCar == null && Vector2.Distance(RoutePath.Flat(player), RoutePath.Flat(Course.RingCenter)) < Course.RingRadius;
            if (inRing && !everInRing)
            {
                everInRing = true;
                Debug.Log("[SpeedDial] you stepped onto the start ring");
            }
            stillFor = Game.Drag.Dragging ? 0f : stillFor + Time.deltaTime;

            bool clearRoad = Room != null && (Room.status == RoomStatus.Countdown || (Started && !ResultsIn && Time.time - StartedAt < ClearRoadFor));
            if (clearRoad != roadCleared) ClearRoad(clearRoad);
            if (Started && ghosted && Time.time - StartedAt > GhostFor) Ghost(false);
            if (Started && !contactsReported && Time.time - StartedAt > ContactWatch)
            {
                contactsReported = true;
                Debug.Log($"[SpeedDial] launch contacts in the first {ContactWatch:F0} s: {launchContacts.Values.Sum()} (racer {launchContacts.GetValueOrDefault("racer")}, traffic {launchContacts.GetValueOrDefault("traffic")}, parked {launchContacts.GetValueOrDefault("parked")})");
            }
            foreach (var bot in bots)
            {
                if (!Started || ResultsIn || bot.LaunchAt <= 0f || Time.time < bot.LaunchAt) continue;
                bot.LaunchAt = 0f;
                bot.Driver.Active = true;
            }

            if (Started && !ResultsIn) BotRaceBehaviour();
            else if (!Gridded) BotLobbyBehaviour();
            if (Online) MissionRing();
        }

        // A bot on its own spends an ask for each call, like a player. Online the server only charges
        // Operator calls, so a bot's call is free there.
        async Task BotAskAsync(Bot bot)
        {
            try
            {
                if (Online)
                {
                    await BotCallAsync(bot, null);
                    return;
                }
                var spent = await Session.SpendAskAsync(bot.Id, RoomId);
                Debug.Log($"[SpeedDial] {bot.Name} spent an ask, {spent.asks} left");
            }
            catch (BackendException e)
            {
                Debug.LogWarning($"[SpeedDial] {bot.Name} could not spend an ask: {e.Code} {e.Message}");
                return;
            }
            await BotCallAsync(bot, null);
        }

        // Now and then a bot takes a pickup a little way ahead: it leaves the route on the streets to the
        // token and rejoins further on. Bots with no asks left go for them twice as often.
        void BotDetours(Bot bot, Racer racer)
        {
            var driver = bot.Driver;
            var pos = RoutePath.Flat(bot.Car.transform.position);
            if (driver.Route != Course.Route)
            {
                if (driver.Progress < driver.Route.Length - 4f && Time.time < bot.DetourUntil) return;
                driver.Route = Course.Route;
                driver.ResetProgress(Course.Route.Project(pos));
                bot.LastProgress = driver.Progress;
                return;
            }
            var pickups = Course.AskPickups;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (bot.Considered.Contains(i)) continue;
                var at = RoutePath.Flat(pickups[i]);
                float along = Course.Route.Project(at);
                if (along < driver.Progress + 10f || along > driver.Progress + 70f) continue;
                bot.Considered.Add(i);
                if (racer.collected != null && racer.collected.Contains(i)) continue;
                double chance = AskTuning.botDetourChance * (racer.asks == 0 ? 2.0 : 1.0);
                if (random.NextDouble() > chance) continue;
                float rejoin = along + 50f;
                var detour = Course.Detour(pos, at, rejoin);
                if (detour == null || detour.Length > rejoin - driver.Progress + 2f * AskTuning.maxDetour + 40f) continue;
                driver.Route = detour;
                driver.ResetProgress(0f);
                bot.DetourUntil = Time.time + 35f;
                Debug.Log($"[SpeedDial] {bot.Name} detours for ask pickup {i} ({detour.Length:F0} m)");
                return;
            }
        }

        async Task BotReadyAsync(Bot bot)
        {
            try { await Session.SetReadyAsync(bot.Id, RoomId, true); }
            catch (BackendException e) { Debug.LogWarning($"[SpeedDial] {bot.Name} could not ready: {e.Code}"); }
        }

        void BotLobbyBehaviour()
        {
            foreach (var bot in bots)
            {
                if (Time.time < bot.NextShove) continue;
                bot.NextShove = Time.time + 1f;
                if (random.NextDouble() > 0.06) continue;
                var me = Game.Player.GetComponent<PlayerShoveable>();
                var from = bot.Avatar.Position;
                IShoveable target = null;
                if (Game.CurrentCar == null && me != null && Vector3.Distance(me.Position, from) < 2.4f) target = me;
                else
                {
                    var other = bots.Where(b => b != bot && b.Avatar.CanBeShoved && Vector3.Distance(b.Avatar.Position, from) < 2.2f).OrderBy(b => random.Next()).FirstOrDefault();
                    if (other != null) target = other.Avatar;
                }
                if (target == null || !target.CanBeShoved || !bot.Avatar.CanBeShoved) continue;
                var push = target.Position - from;
                push.y = 0f;
                target.Shove(push.normalized * 6f, bot.Avatar.gameObject);
            }
        }

        void BotRaceBehaviour()
        {
            foreach (var bot in bots)
            {
                var racer = Last.racers.FirstOrDefault(r => r.playerId == bot.Id);
                if (racer == null) continue;
                if (Time.time - StartedAt > 8f) BotDetours(bot, racer);
                bool onRoute = bot.Driver.Route == Course.Route;
                float progress = bot.Driver.Progress;
                if (onRoute && racer.asks > 0 && Time.time >= bot.NextCall && Time.time >= botCallsAfter)
                {
                    bot.NextCall = Time.time + 18f + (float)random.NextDouble() * 10f;
                    botCallsAfter = Time.time + 8f;
                    StartCoroutine(Wait(BotAskAsync(bot)));
                }
                if (onRoute) bot.LastProgress = progress;

                if (Time.time < bot.NextShove) continue;
                bot.NextShove = Time.time + 0.5f;
                var t = bot.Car.transform;
                foreach (var other in RacerCars())
                {
                    if (other == bot.Car) continue;
                    var rel = other.transform.position - t.position;
                    float along = Vector3.Dot(rel, t.forward);
                    float side = Vector3.Dot(rel, t.right);
                    if (Mathf.Abs(along) > 3f || Mathf.Abs(side) > 4.2f || random.NextDouble() > 0.12) continue;
                    CarFx.On(other).Push(t.right * Mathf.Sign(side) * 6.5f);
                    CarFx.On(bot.Car).Push(-t.right * Mathf.Sign(side) * 1.5f);
                    Debug.Log($"[SpeedDial] {bot.Name} shoved {(other == myCar ? "you" : other.name)}");
                    break;
                }
            }
        }

        IEnumerable<CarController> RacerCars()
        {
            yield return myCar;
            foreach (var bot in bots) yield return bot.Car;
        }

        void LateUpdate()
        {
            if (!Setup) return;
            if (ResultsIn && Gridded)
            {
                Game.Follow.enabled = false;
                var car = myCar.transform;
                float orbit = (Time.time - ResultsAt) * 9f;
                var offset = Quaternion.Euler(0f, car.eulerAngles.y + 150f + orbit, 0f) * Vector3.forward * 7.5f;
                var target = car.position + offset + Vector3.up * 2.4f;
                var view = Game.View.transform;
                float blend = 1f - Mathf.Exp(-3f * Time.deltaTime);
                view.position = Vector3.Lerp(view.position, target, blend);
                view.rotation = Quaternion.Slerp(view.rotation, Quaternion.LookRotation(car.position + Vector3.up * 0.8f - view.position, Vector3.up), blend);
                return;
            }
            bool want = !Gridded && Game.CurrentCar == null && inRing && stillFor > 0.6f;
            if (want && !lobbyCam)
            {
                lobbyCam = true;
                Game.Follow.enabled = false;
                if (Vector3.Distance(Game.Player.transform.position, RingSpot(MySlot)) > 0.7f) Game.Player.Teleport(RingSpot(MySlot), LobbyHeading);
            }
            else if (!want && lobbyCam)
            {
                lobbyCam = false;
                Game.Follow.enabled = true;
                Game.Follow.Snap(Game.Player.transform, false);
            }
            if (!lobbyCam) return;
            var cam = Game.View.transform;
            var pos = Course.RingCenter + Course.RingFacing * 9.5f + Vector3.up * 2.2f;
            var look = Quaternion.LookRotation(Course.RingCenter + Vector3.up * 1.5f - pos, Vector3.up);
            float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
            cam.position = Vector3.Lerp(cam.position, pos, k);
            cam.rotation = Quaternion.Slerp(cam.rotation, look, k);
        }
    }
}
