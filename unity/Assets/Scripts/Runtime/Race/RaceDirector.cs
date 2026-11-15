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
    public sealed class RaceDirector : MonoBehaviour
    {
        static readonly string[] BotNames = { "Maya", "Jun", "Rosa" };
        static readonly float[] BotSkill = { 0.95f, 0.91f, 0.87f };
        static readonly float[] RingLateral = { -0.9f, -2.7f, 0.9f, 2.7f };
        public static readonly string[] SlotHex = { "#2D8CFF", "#A35BFF", "#FF8A1E", "#2EC27E" };
        const float PollEvery = 0.12f;
        const float SendEvery = 0.1f;
        const float LockMs = 3000f;

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
        public string MyId { get; private set; }
        public string RoomId { get; private set; }
        public PollResponse Last { get; private set; }
        public bool Setup { get; private set; }
        public bool Gridded { get; private set; }
        public bool Started { get; private set; }
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

        public long NowMs => (long)(epochMs + Time.timeAsDouble * 1000.0);
        public Room Room => Last?.room;
        public Racer Me => Last?.racers?.FirstOrDefault(r => r.playerId == MyId);
        public float MsToStart => Room == null || Room.startsAt == 0 ? float.MaxValue : Room.startsAt - NowMs;
        public bool Locked => Gridded && !Started;
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

        IEnumerator Start()
        {
            while ((Game = FindAnyObjectByType<GameController>()) == null || !Game.IsReady) yield return null;
            yield return Wait(SetupAsync());
            if (!Setup) yield break;
            StartCoroutine(PollLoop());
            StartCoroutine(SendLoop());
        }

        async Task SetupAsync()
        {
            var material = Game.SharedMaterial;
            Catalog = WorldChangeCatalog.Load();
            Course = RaceCourse.Build(Game.World, Game.Data, Resources.Load<TextAsset>("SpeedDial/course").text);

            var root = new GameObject("SpeedDialCourse").transform;
            root.SetParent(Game.World.Root.transform, false);
            CourseDressing.StartRing(root, Course.RingCenter, Course.RingRadius, material);
            var finishRight = Quaternion.Euler(0f, Course.FinishHeading, 0f) * Vector3.right;
            float ycSide = Vector3.Dot(Course.YcSite - Course.Finish, finishRight) >= 0f ? 1f : -1f;
            var flagSpot = Course.Finish + finishRight * ycSide * (Course.FinishHalfWidth + 1.6f) + Quaternion.Euler(0f, Course.FinishHeading, 0f) * Vector3.forward * 3f;
            CourseDressing.Finish(root, Course.Finish, Course.FinishHeading, Course.FinishHalfWidth, flagSpot, material);
            CourseDressing.TurnChevrons(root, Course.Route, Course.RouteWorld, material);

            var phoneAsset = GlbAsset.Load("payphone-stand-f97728", Resources.Load<TextAsset>("SpeedDial/Models/payphone-stand-f97728").bytes);
            var phones = new List<Payphone>();
            foreach (var spot in Course.Payphones)
            {
                var phone = CourseDressing.Payphone(root, phoneAsset, spot, material).AddComponent<Payphone>();
                phone.Spot = spot;
                phones.Add(phone);
            }

            Session = new LocalRaceSession(id => Game.Backend.GetPlayerAsync(id), () => NowMs, Catalog);
            Operator = new LocalOperator(Session, Catalog);
            MyId = Game.Profile.id;
            var route = Course.RouteWorld.Select(v => new Vec3(v.x, v.y, v.z)).ToList();
            var created = await Session.CreateRoomAsync(MyId, route, Course.Data.finishRadius, 2);
            RoomId = created.room.id;
            raceNumber = created.room.raceNumber;
            MySlot = created.you.slot;
            for (int i = 0; i < BotNames.Length; i++)
            {
                var player = await Game.Backend.CreatePlayerAsync(BotNames[i]);
                var joined = await Session.JoinRoomAsync(player.id, created.room.code);
                bots.Add(new Bot { Name = BotNames[i], Id = player.id, Slot = joined.you.slot });
            }

            if (Game.CurrentCar != null) await Game.ExitCar();
            myCar = Game.Cars.Values.First();
            foreach (var car in Game.Cars.Values) CarFx.On(car);
            PaintJob.For(myCar.gameObject, myCar.ModelId).SetBase(PaintJob.Linear(SlotHex[MySlot]));
            var carAsset = Game.Models.Get(myCar.ModelId);
            var avatarAsset = Game.Models.Get(Game.Models.Role("player")[0].id);
            foreach (var bot in bots)
            {
                var spot = Course.Grid[bot.Slot];
                bot.Car = CarController.Create("bot-" + bot.Name.ToLowerInvariant(), carAsset, material, spot.Position, spot.Heading);
                bot.Car.name = "Car " + bot.Name;
                bot.Car.transform.SetParent(Game.World.Root.transform, true);
                CarFx.On(bot.Car);
                PaintJob.For(bot.Car.gameObject, bot.Car.ModelId).SetBase(PaintJob.Linear(SlotHex[bot.Slot]));
                bot.Driver = bot.Car.gameObject.AddComponent<BotDriver>();
                bot.Driver.name = bot.Name;
                bot.Driver.Car = bot.Car;
                bot.Driver.Route = Course.Route;
                bot.Driver.Skill = BotSkill[bots.IndexOf(bot)];
                bot.Driver.Lane = bot.Slot % 2 == 0 ? -1.9f : 1.9f;
                bot.Avatar = RacerAvatar.Create(avatarAsset, material, PaintJob.Linear(SlotHex[bot.Slot]), bot.Name);
                bot.Avatar.transform.SetParent(Game.World.Root.transform, true);
            }

            Game.Player.gameObject.AddComponent<PlayerShoveable>();
            Changes = gameObject.AddComponent<WorldChangeDirector>();
            Changes.Game = Game;
            Changes.Race = this;
            Changes.Catalog = Catalog;

            Calls = gameObject.AddComponent<CallController>();
            Calls.Game = Game;
            Calls.Operator = Operator;
            Calls.PlayerId = () => MyId;
            Calls.RoomId = () => RoomId;
            Calls.Phones = phones;

            Hud = gameObject.AddComponent<SlipstreamHud>();
            Hud.Race = this;
            Shove = gameObject.AddComponent<ShoveInput>();
            Shove.Game = Game;
            Shove.IgnoreTouch = Hud.HitTest;
            Game.Drag.IgnoreTouch = Hud.HitTest;

            PlaceForLobby(false);
            Setup = true;
            Debug.Log($"[SpeedDial] room {created.room.code}: {Course.Data.startName} -> {Course.Data.finishName}, {Course.Route.Length:F0} m, ring at ({Course.RingCenter.x:F0},{Course.RingCenter.z:F0}), finish at ({Course.Finish.x:F0},{Course.Finish.z:F0}), {phones.Count} payphones, racers You/{string.Join("/", BotNames)}");
        }

        Vector3 LobbyRight => Vector3.Cross(Vector3.up, -Course.RingFacing);
        float LobbyHeading => RoutePath.Heading(new Vector2(Course.RingFacing.x, Course.RingFacing.z));

        public Vector3 RingSpot(int slot) => Course.RingCenter + LobbyRight * RingLateral[slot] + Vector3.up * 0.06f;

        void PlaceForLobby(bool onRing)
        {
            foreach (var bot in bots)
            {
                var s = Course.Grid[bot.Slot];
                bot.Car.SetDriven(false);
                bot.Car.Place(s.Position, s.Heading);
                bot.Driver.Active = false;
                bot.Driver.ResetProgress(s.Progress);
                bot.LastProgress = s.Progress;
                bot.ReadyAt = -1f;
                bot.Avatar.gameObject.SetActive(true);
                bot.Avatar.Stand(RingSpot(bot.Slot), LobbyHeading);
            }
            var mine = Course.Grid[MySlot];
            myCar.Place(mine.Position, mine.Heading);
            if (onRing) Game.Player.Teleport(RingSpot(MySlot), LobbyHeading);
            else Game.Player.Teleport(Course.RingCenter + Course.RingFacing * (Course.RingRadius + 2.5f) + Vector3.up * 0.1f, LobbyHeading + 180f);
            Game.Follow.enabled = true;
            Game.Follow.Snap(Game.Player.transform, false);
            lobbySince = Time.time;
            everInRing = onRing;
        }

        IEnumerator PollLoop()
        {
            while (true)
            {
                var poll = Session.PollAsync(RoomId, cursor);
                yield return Wait(poll);
                if (poll.Status == TaskStatus.RanToCompletion) yield return Handle(poll.Result);
                yield return new WaitForSeconds(PollEvery);
            }
        }

        IEnumerator Handle(PollResponse poll)
        {
            Last = poll;
            foreach (var e in poll.events)
            {
                if (cursor.HasValue && e.seq <= cursor.Value) continue;
                Changes.Apply(e);
                EventArrived?.Invoke(e);
            }
            cursor = poll.cursor;
            var room = poll.room;
            if (room.raceNumber != raceNumber)
            {
                raceNumber = room.raceNumber;
                yield return BackToLobby();
                yield break;
            }

            if (room.status == RoomStatus.Lobby || room.status == RoomStatus.Countdown)
            {
                if (room.status == RoomStatus.Countdown && MsToStart <= LockMs && !Gridded) yield return ToGrid();
                bool go = everInRing || (Me != null && Me.ready) || Time.time - lobbySince > 25f;
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
                bot.Car.Place(s.Position, s.Heading);
                bot.Car.SetDriven(true);
                bot.Driver.ResetProgress(s.Progress);
                bot.LastProgress = s.Progress;
                bot.Driver.Active = false;
            }
            Debug.Log("[SpeedDial] racers on the grid");
        }

        void Go()
        {
            Started = true;
            StartedAt = Time.time;
            botCallsAfter = Time.time + 4f;
            foreach (var bot in bots)
            {
                bot.Driver.Active = true;
                bot.NextCall = Time.time + 5f + (float)random.NextDouble() * 6f;
            }
            Game.ExternalInput = Autopilot;
            if (myPilot != null) myPilot.Active = Autopilot;
            Debug.Log("[SpeedDial] GO");
        }

        IEnumerator BackToLobby()
        {
            Debug.Log($"[SpeedDial] rematch: race {raceNumber}");
            Changes.ResetAll();
            Gridded = Started = ResultsIn = false;
            Game.ExternalInput = false;
            if (myPilot != null) myPilot.Active = false;
            if (Game.CurrentCar != null) yield return Wait(Game.ExitCar());
            PlaceForLobby(true);
        }

        IEnumerator SendLoop()
        {
            while (true)
            {
                var me = BodyOf(MyId);
                if (me != null) yield return Wait(Session.UpdateAsync(MyId, RoomId, new Vec3(me.position.x, me.position.y, me.position.z), me.eulerAngles.y));
                foreach (var bot in bots)
                {
                    var t = BodyOf(bot.Id);
                    var update = Session.UpdateAsync(bot.Id, RoomId, new Vec3(t.position.x, t.position.y, t.position.z), t.eulerAngles.y);
                    yield return Wait(update);
                }
                yield return new WaitForSeconds(SendEvery);
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
                myPilot.Lane = MySlot % 2 == 0 ? -1.9f : 1.9f;
                myPilot.ResetProgress(Course.Grid[MySlot].Progress);
            }
            if (Started && !ResultsIn)
            {
                Game.ExternalInput = true;
                myPilot.Active = true;
            }
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
        }

        public Transform BodyOf(string playerId)
        {
            if (playerId == MyId) return Game.CurrentCar != null ? Game.CurrentCar.transform : Game.Player.transform;
            var bot = bots.FirstOrDefault(b => b.Id == playerId);
            if (bot == null) return null;
            return Gridded ? bot.Car.transform : bot.Avatar.transform;
        }

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
            bot.Driver.ResetProgress(Course.Route.Project(RoutePath.Flat(position)));
            bot.LastProgress = bot.Driver.Progress;
        }

        public float ProgressOf(string playerId)
        {
            var racer = Last?.racers?.FirstOrDefault(r => r.playerId == playerId);
            return racer != null ? racer.progress : 0f;
        }

        public string NameOf(string playerId) => playerId == MyId ? "You" : bots.FirstOrDefault(b => b.Id == playerId)?.Name ?? "";

        // Makes a bot call in a world change, as the scripted play-through does; null picks one.
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

            if (Started && !ResultsIn) BotRaceBehaviour();
            else if (!Gridded) BotLobbyBehaviour();
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
                float progress = bot.Driver.Progress;
                if (Time.time >= bot.NextCall && Time.time >= botCallsAfter)
                    foreach (var phone in Calls.Phones)
                    {
                        float at = phone.Spot.Progress;
                        if (at <= bot.LastProgress || at > progress) continue;
                        if (Vector3.Distance(phone.transform.position, bot.Car.transform.position) > 22f || random.NextDouble() > 0.7) continue;
                        bot.NextCall = Time.time + 18f;
                        botCallsAfter = Time.time + 8f;
                        StartCoroutine(Wait(BotCallAsync(bot, null)));
                        break;
                    }
                bot.LastProgress = progress;

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
