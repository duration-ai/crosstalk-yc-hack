using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // On-device implementation of IRaceSession that runs the race contract's rules: colors and
    // slots by join order, ready and auto-start countdowns with the locked last 3 s, progress along
    // the route, place ordering, finish and close-out, awards, world-change validation and news,
    // and rematch. It makes no network calls; players resolve through IGameBackend like the server
    // resolves them from its players table. The clock is injected so the race runs on game time.
    public sealed class LocalRaceSession : IRaceSession
    {
        const int MaxPlayers = 4;
        const long AllReadyMs = 3000;
        const long AutoStartMs = 10000;
        const long LockMs = 3000;
        const long CloseAfterFirstFinishMs = 60000;
        const int DefaultAsks = 100;
        const int MaxAsks = 100;
        const int MaxPickups = 64;
        const float DefaultPickupRadius = 4f;
        const float CollectSlack = 3f;

        static readonly string[] Colors = { "blue", "purple", "orange", "green" };
        static readonly string[] ColorHex = { "#2D8CFF", "#A35BFF", "#FF8A1E", "#2EC27E" };

        sealed class RoomState
        {
            public Room Room;
            public List<Vec3> RouteWire;
            public RoutePath Route;
            public readonly List<Racer> Racers = new List<Racer>();
            public readonly List<RaceEvent> Events = new List<RaceEvent>();
            public readonly Dictionary<string, int> Calls = new Dictionary<string, int>();
            public List<RaceResult> Results = new List<RaceResult>();
            public long Seq;
        }

        readonly Func<string, Task<Player>> getPlayer;
        readonly Func<long> clock;
        readonly WorldChangeCatalog catalog;
        readonly Dictionary<string, RoomState> rooms = new Dictionary<string, RoomState>();
        readonly System.Random random = new System.Random();
        int nextRoom = 1;

        public LocalRaceSession(Func<string, Task<Player>> getPlayer, Func<long> clock, WorldChangeCatalog catalog)
        {
            this.getPlayer = getPlayer;
            this.clock = clock;
            this.catalog = catalog;
        }

        static T Copy<T>(T value) => value == null ? default : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        static BackendException Error(string code, string message) => new BackendException(code, message);

        RoomState RoomById(string roomId)
        {
            if (string.IsNullOrEmpty(roomId) || !rooms.TryGetValue(roomId, out var state)) throw Error(BackendErrorCodes.NotFound, $"Room {roomId} not found");
            Tick(state);
            return state;
        }

        static Racer RacerOf(RoomState state, string playerId) => state.Racers.FirstOrDefault(r => r.playerId == playerId);

        Racer Member(RoomState state, string playerId)
        {
            var racer = RacerOf(state, playerId);
            if (racer == null) throw Error(BackendErrorCodes.Conflict, "Not in the room");
            return racer;
        }

        bool Locked(RoomState state, long now) => state.Room.status == RoomStatus.Countdown && state.Room.startsAt - now <= LockMs;

        public async Task<RoomResponse> CreateRoomAsync(string playerId, IReadOnlyList<Vec3> route, float finishRadius, int minPlayers,
            int asks = 0, IReadOnlyList<Vec3> pickups = null, float pickupRadius = 0f)
        {
            var player = await getPlayer(playerId);
            if (route != null && (route.Count < 2 || route.Count > 512)) throw Error(BackendErrorCodes.InvalidArgument, "route must have 2 to 512 points");
            if (finishRadius == 0f) finishRadius = 15f;
            if (minPlayers == 0) minPlayers = 2;
            if (finishRadius < 1f || finishRadius > 100f) throw Error(BackendErrorCodes.InvalidArgument, "finishRadius must be 1 to 100");
            if (minPlayers < 1 || minPlayers > MaxPlayers) throw Error(BackendErrorCodes.InvalidArgument, "minPlayers must be 1 to 4");
            if (asks == 0) asks = DefaultAsks;
            if (pickupRadius == 0f) pickupRadius = DefaultPickupRadius;
            if (asks < 1 || asks > MaxAsks) throw Error(BackendErrorCodes.InvalidArgument, $"asks must be 1 to {MaxAsks}");
            if (pickups != null && pickups.Count > MaxPickups) throw Error(BackendErrorCodes.InvalidArgument, $"pickups must have at most {MaxPickups} points");
            if (pickupRadius < 1f || pickupRadius > 20f) throw Error(BackendErrorCodes.InvalidArgument, "pickupRadius must be 1 to 20");
            LeaveAll(playerId);

            var wire = route != null ? route.Select(Copy).ToList() : new List<Vec3> { new Vec3(0, 0, 0), new Vec3(0, 0, 800) };
            var path = new RoutePath(wire.Select(v => new Vector2(v.x, v.z)).ToList());
            var last = wire[wire.Count - 1];
            var state = new RoomState
            {
                RouteWire = wire,
                Route = path,
                Room = new Room
                {
                    id = "room" + nextRoom++,
                    code = NewCode(),
                    status = RoomStatus.Lobby,
                    raceNumber = 1,
                    minPlayers = minPlayers,
                    routeLength = path.Length,
                    finish = Copy(last),
                    finishRadius = finishRadius,
                    createdAt = clock(),
                    asks = asks,
                    pickups = pickups != null ? pickups.Select(Copy).ToList() : new List<Vec3>(),
                    pickupRadius = pickupRadius,
                },
            };
            rooms[state.Room.id] = state;
            var you = AddRacer(state, player);
            Refresh(state);
            return new RoomResponse { room = Copy(state.Room), route = state.RouteWire.Select(Copy).ToList(), you = Copy(you) };
        }

        string NewCode()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            while (true)
            {
                var code = new string(Enumerable.Range(0, 4).Select(_ => letters[random.Next(letters.Length)]).ToArray());
                if (rooms.Values.All(r => r.Room.code != code)) return code;
            }
        }

        Racer AddRacer(RoomState state, Player player)
        {
            int slot = Enumerable.Range(0, MaxPlayers).First(s => state.Racers.All(r => r.slot != s));
            var racer = new Racer
            {
                playerId = player.id,
                displayName = player.displayName,
                color = Colors[slot],
                colorHex = ColorHex[slot],
                slot = slot,
                position = Copy(state.RouteWire[0]),
                metersToFinish = state.Route.Length,
                updatedAt = clock(),
                asks = state.Room.asks,
                collected = new List<int>(),
            };
            state.Racers.Add(racer);
            return racer;
        }

        void LeaveAll(string playerId)
        {
            foreach (var state in rooms.Values)
            {
                var racer = RacerOf(state, playerId);
                if (racer == null) continue;
                if (state.Room.status == RoomStatus.Racing) racer.left = true;
                else state.Racers.Remove(racer);
                UpdateCountdown(state, clock());
                Refresh(state);
            }
        }

        public async Task<RoomResponse> JoinRoomAsync(string playerId, string code)
        {
            var player = await getPlayer(playerId);
            var state = rooms.Values.FirstOrDefault(r => string.Equals(r.Room.code, code, StringComparison.OrdinalIgnoreCase));
            if (state == null) throw Error(BackendErrorCodes.NotFound, $"Room {code} not found");
            Tick(state);
            long now = clock();
            var you = RacerOf(state, playerId);
            if (you == null)
            {
                if (state.Room.status == RoomStatus.Racing || state.Room.status == RoomStatus.Finished) throw Error(BackendErrorCodes.Conflict, "Race already started");
                if (Locked(state, now)) throw Error(BackendErrorCodes.Conflict, "Countdown locked");
                if (state.Racers.Count >= MaxPlayers) throw Error(BackendErrorCodes.Conflict, "Room is full");
                foreach (var other in rooms.Values)
                    if (other != state && RacerOf(other, playerId) != null) LeaveAll(playerId);
                you = AddRacer(state, player);
                UpdateCountdown(state, now);
                Refresh(state);
            }
            return new RoomResponse { room = Copy(state.Room), route = state.RouteWire.Select(Copy).ToList(), you = Copy(you) };
        }

        public Task<LeaveResponse> LeaveRoomAsync(string playerId, string roomId)
        {
            var state = RoomById(roomId);
            var racer = RacerOf(state, playerId);
            if (racer == null) return Task.FromResult(new LeaveResponse { left = true });
            long now = clock();
            if (Locked(state, now)) throw Error(BackendErrorCodes.Conflict, "Countdown locked");
            if (state.Room.status == RoomStatus.Racing || state.Room.status == RoomStatus.Finished) racer.left = true;
            else state.Racers.Remove(racer);
            UpdateCountdown(state, now);
            Tick(state);
            Refresh(state);
            return Task.FromResult(new LeaveResponse { left = true });
        }

        public Task<ReadyResponse> SetReadyAsync(string playerId, string roomId, bool ready)
        {
            var state = RoomById(roomId);
            var racer = Member(state, playerId);
            long now = clock();
            var room = state.Room;
            if (room.status == RoomStatus.Racing) throw Error(BackendErrorCodes.Conflict, "Race is running");
            if (room.status == RoomStatus.Finished)
            {
                if (!ready) throw Error(BackendErrorCodes.Conflict, "Race is over");
                Rematch(state);
            }
            else if (Locked(state, now) && !ready) throw Error(BackendErrorCodes.Conflict, "Countdown locked");
            racer.ready = ready;
            UpdateCountdown(state, now);
            Refresh(state);
            return Task.FromResult(new ReadyResponse { room = Copy(state.Room) });
        }

        void Rematch(RoomState state)
        {
            var room = state.Room;
            room.status = RoomStatus.Lobby;
            room.raceNumber++;
            room.startsAt = 0;
            room.closesAt = 0;
            state.Racers.RemoveAll(r => r.left);
            foreach (var r in state.Racers)
            {
                r.ready = false;
                r.progress = 0f;
                r.metersToFinish = state.Route.Length;
                r.finished = false;
                r.finishMs = 0;
                r.asks = room.asks;
                r.collected.Clear();
            }
            state.Calls.Clear();
            state.Results = new List<RaceResult>();
        }

        void UpdateCountdown(RoomState state, long now)
        {
            var room = state.Room;
            if (room.status != RoomStatus.Lobby && room.status != RoomStatus.Countdown) return;
            if (Locked(state, now)) return;
            int players = state.Racers.Count;
            int ready = state.Racers.Count(r => r.ready);
            if (players >= room.minPlayers && players > 0 && ready == players)
            {
                room.status = RoomStatus.Countdown;
                room.startsAt = room.startsAt == 0 ? now + AllReadyMs : Math.Min(room.startsAt, now + AllReadyMs);
            }
            else if (ready >= Math.Max(2, room.minPlayers))
            {
                room.status = RoomStatus.Countdown;
                if (room.startsAt == 0) room.startsAt = now + AutoStartMs;
            }
            else
            {
                room.status = RoomStatus.Lobby;
                room.startsAt = 0;
            }
        }

        void Tick(RoomState state)
        {
            var room = state.Room;
            long now = clock();
            if (room.status == RoomStatus.Countdown && now >= room.startsAt) room.status = RoomStatus.Racing;
            if (room.status != RoomStatus.Racing) return;
            var active = state.Racers.Where(r => !r.left).ToList();
            bool allDone = active.Count > 0 && active.All(r => r.finished);
            if (allDone || (room.closesAt > 0 && now >= room.closesAt)) Finish(state);
        }

        void Finish(RoomState state)
        {
            state.Room.status = RoomStatus.Finished;
            Refresh(state);
            var ordered = state.Racers.OrderBy(r => r.place).ToList();
            int maxCalls = ordered.Count == 0 ? 0 : ordered.Max(r => state.Calls.TryGetValue(r.playerId, out int c) ? c : 0);
            string caller = null;
            if (maxCalls > 0)
            {
                var top = ordered.Where(r => state.Calls.TryGetValue(r.playerId, out int c) && c == maxCalls).ToList();
                caller = top[0].playerId;
            }
            var results = new List<RaceResult>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                state.Calls.TryGetValue(r.playerId, out int calls);
                bool photo = r.finished && ((i > 0 && ordered[i - 1].finished && Math.Abs(r.finishMs - ordered[i - 1].finishMs) <= 1000)
                    || (i + 1 < ordered.Count && ordered[i + 1].finished && Math.Abs(r.finishMs - ordered[i + 1].finishMs) <= 1000));
                string award = r.playerId == caller ? "CHAOS CALLER"
                    : photo ? "PHOTO FINISH"
                    : !r.finished ? "STILL DRIVING"
                    : r.place == 1 ? "FIRST TO YC" : r.place == 2 ? "SO CLOSE" : r.place == 3 ? "PODIUM FINISH" : "SCENIC ROUTE";
                results.Add(new RaceResult
                {
                    place = r.place,
                    playerId = r.playerId,
                    displayName = r.displayName,
                    color = r.color,
                    finished = r.finished,
                    finishMs = r.finishMs,
                    calls = calls,
                    award = award,
                });
            }
            state.Results = results;
        }

        static void Refresh(RoomState state)
        {
            var room = state.Room;
            room.playerCount = state.Racers.Count;
            room.readyCount = state.Racers.Count(r => r.ready);
            var ordered = state.Racers
                .OrderBy(r => r.left ? 2 : r.finished ? 0 : 1)
                .ThenBy(r => r.finished ? r.finishMs : 0)
                .ThenByDescending(r => r.finished ? 0f : r.progress)
                .ThenBy(r => r.slot)
                .ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].place = i + 1;
        }

        public Task<PollResponse> PollAsync(string roomId, long? after)
        {
            var state = RoomById(roomId);
            Refresh(state);
            var events = state.Events.Where(e => after.HasValue ? e.seq > after.Value : e.raceNumber == state.Room.raceNumber).Take(100).Select(Copy).ToList();
            long cursor = events.Count > 0 ? events[events.Count - 1].seq : after ?? (state.Events.Count > 0 ? state.Events[state.Events.Count - 1].seq : 0);
            return Task.FromResult(new PollResponse
            {
                serverTime = clock(),
                room = Copy(state.Room),
                racers = state.Racers.OrderBy(r => r.place).Select(Copy).ToList(),
                events = events,
                cursor = cursor,
                results = state.Room.status == RoomStatus.Finished ? state.Results.Select(Copy).ToList() : new List<RaceResult>(),
            });
        }

        public Task<RaceUpdateResponse> UpdateAsync(string playerId, string roomId, Vec3 position, float heading)
        {
            if (position == null) throw Error(BackendErrorCodes.InvalidArgument, "position is required");
            var state = RoomById(roomId);
            var racer = Member(state, playerId);
            if (racer.left) throw Error(BackendErrorCodes.Conflict, "Player left the room");
            long now = clock();
            racer.position = Copy(position);
            racer.heading = heading;
            racer.updatedAt = now;
            var room = state.Room;
            if (!racer.finished && room.status != RoomStatus.Finished)
            {
                racer.progress = state.Route.Project(new Vector2(position.x, position.z));
                racer.metersToFinish = Mathf.Max(0f, state.Route.Length - racer.progress);
                var end = state.Route.End;
                if (room.status == RoomStatus.Racing && Vector2.Distance(new Vector2(position.x, position.z), end) <= room.finishRadius)
                {
                    racer.finished = true;
                    racer.finishMs = now - room.startsAt;
                    racer.progress = state.Route.Length;
                    racer.metersToFinish = 0f;
                    if (room.closesAt == 0) room.closesAt = now + CloseAfterFirstFinishMs;
                }
            }
            Tick(state);
            Refresh(state);
            return Task.FromResult(new RaceUpdateResponse
            {
                status = room.status,
                progress = racer.progress,
                metersToFinish = racer.metersToFinish,
                finished = racer.finished,
                finishMs = racer.finishMs,
            });
        }

        public Task<AskResponse> CollectAskAsync(string playerId, string roomId, int pickup, Vec3 position)
        {
            if (position == null) throw Error(BackendErrorCodes.InvalidArgument, "position is required");
            var state = RoomById(roomId);
            var racer = Member(state, playerId);
            if (racer.left) throw Error(BackendErrorCodes.Conflict, "Player left the room");
            var room = state.Room;
            if (room.status != RoomStatus.Racing) throw Error(BackendErrorCodes.Conflict, "Pickups only count while racing");
            if (pickup < 0 || pickup >= room.pickups.Count) throw Error(BackendErrorCodes.InvalidArgument, "pickup must index room.pickups");
            if (racer.collected.Contains(pickup)) return Task.FromResult(new AskResponse { collected = false, asks = racer.asks });
            var at = room.pickups[pickup];
            if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(at.x, at.z)) > room.pickupRadius + CollectSlack)
                throw Error(BackendErrorCodes.Conflict, "Too far from the pickup");
            racer.collected.Add(pickup);
            racer.asks++;
            return Task.FromResult(new AskResponse { collected = true, asks = racer.asks });
        }

        public Task<AskResponse> SpendAskAsync(string playerId, string roomId)
        {
            var state = RoomById(roomId);
            var racer = Member(state, playerId);
            if (racer.left) throw Error(BackendErrorCodes.Conflict, "Player left the room");
            if (state.Room.status == RoomStatus.Finished) throw Error(BackendErrorCodes.Conflict, "Race is over");
            if (racer.asks <= 0) throw Error(BackendErrorCodes.Conflict, "No asks left");
            racer.asks--;
            return Task.FromResult(new AskResponse { collected = false, asks = racer.asks });
        }

        public Task<FireEventResponse> FireEventAsync(string playerId, string roomId, WorldChangeCommand command)
        {
            var state = RoomById(roomId);
            var racer = Member(state, playerId);
            if (racer.left) throw Error(BackendErrorCodes.Conflict, "Player left the room");
            if (state.Room.status == RoomStatus.Finished) throw Error(BackendErrorCodes.Conflict, "Race is over");
            var cmd = new WorldChangeCommand { type = command?.type, @params = WorldChangeCatalog.Copy(command?.@params) };
            if (cmd.type == "swap") cmd.@params.playerId = playerId;
            if (cmd.type == "ramp" && cmd.@params.count == 0) cmd.@params.count = 3;
            if (!catalog.Validate(cmd, out string error)) throw Error(BackendErrorCodes.InvalidArgument, error);
            Racer target = null;
            if (cmd.type == "swap")
            {
                target = RacerOf(state, cmd.@params.targetPlayerId);
                if (target == null || target == racer || target.left) throw Error(BackendErrorCodes.InvalidArgument, "Swap target must be another racer in the room");
            }
            if (cmd.type == "recolor") cmd.@params.hex = catalog.Hex(cmd.@params.color);

            state.Calls.TryGetValue(playerId, out int calls);
            state.Calls[playerId] = calls + 1;
            var e = new RaceEvent
            {
                seq = ++state.Seq,
                raceNumber = state.Room.raceNumber,
                type = cmd.type,
                @params = cmd.@params,
                playerId = playerId,
                displayName = racer.displayName,
                color = racer.color,
                serverTime = clock(),
                news = catalog.News(cmd, racer.displayName, target?.displayName),
            };
            state.Events.Add(e);
            return Task.FromResult(new FireEventResponse { @event = Copy(e) });
        }
    }
}
