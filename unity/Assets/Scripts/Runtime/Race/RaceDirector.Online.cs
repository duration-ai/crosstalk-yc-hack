using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Backend;
using UnityEngine;

namespace Dogpatch.Race
{
    // The online half of the race: when the Convex race backend answers at startup the race runs
    // on ConvexRaceSession, and the player hosts a room (its 4-letter code goes up big) or joins one
    // by typing a code. Bots fill empty slots only when the host turns them on. When the backend is
    // unreachable, or the game starts with -offline, nothing here runs and the race stays local with
    // Maya, Jun and Rosa as before.
    //
    // Command line, for headless checks: -convex-url <url>, -offline, -name <name>,
    // -speeddial-host (host on launch), -speeddial-join <code>, -speeddial-bots (host fills slots),
    // -speeddial-min <n> (minPlayers for the hosted room), -speeddial-code-file <path> (write the code).
    public sealed partial class RaceDirector
    {
        const string NameKey = "speeddial.name";
        const int ConnectTimeoutSeconds = 6;

        ConvexRaceSession convex;

        public bool Online => convex != null;
        public string RoomCode { get; private set; }
        public bool IsHost { get; private set; }
        public bool BotsOn => bots.Count > 0;
        public bool Busy { get; private set; }
        public string LastError { get; private set; }
        public float LastErrorAt { get; private set; } = -10f;
        public RemoteRacers Remotes { get; private set; }
        public string MyName { get; private set; }
        public string ServerUrl => convex?.BaseUrl;

        // Raised when this device creates, joins, leaves or rematches a room: MyId and RoomId (null
        // when not in a room) are already current. OperatorRig follows it to watch the room's
        // activeEffects.
        public event Action RoomChanged;

        object Pause(float seconds) => Online ? (object)new WaitForSecondsRealtime(seconds) : new WaitForSeconds(seconds);

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

        public void Host(bool withBots = false) => Run(HostAsync(withBots, Flag("-speeddial-min") ? int.Parse(Arg("-speeddial-min")) : 2));

        public void Join(string code) => Run(JoinAsync(code));

        public void Leave() => Run(LeaveAsync());

        public void SetBots(bool on) => Run(on ? AddBotsAsync() : RemoveBotsAsync());

        public void Rename(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name == MyName || !Online || RoomId != null) return;
            if (name.Length > 12) name = name.Substring(0, 12);
            Run(RenameAsync(name));
        }

        async Task RenameAsync(string name)
        {
            var player = await convex.CreatePlayerAsync(name);
            MyName = name;
            MyId = player.id;
            PlayerPrefs.SetString(NameKey, name);
            PlayerPrefs.SetString(PlayerKey(convex.BaseUrl), MyId);
            PlayerPrefs.Save();
        }

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

        async Task HostAsync(bool withBots, int minPlayers)
        {
            if (RoomId != null) return;
            var created = await AsPlayer(() => Session.CreateRoomAsync(MyId, RouteWire(), Course.Data.finishRadius, minPlayers));
            EnterRoom(created, true);
            Debug.Log($"[SpeedDial] hosting room {created.room.code} ({created.room.id}), route {created.room.routeLength:F0} m, min {created.room.minPlayers}");
            var file = Arg("-speeddial-code-file");
            if (file != null) System.IO.File.WriteAllText(file, created.room.code + "\n");
            if (withBots) await AddBotsAsync();
            PlaceForLobby(false);
        }

        async Task JoinAsync(string code)
        {
            if (RoomId != null) return;
            var joined = await AsPlayer(() => Session.JoinRoomAsync(MyId, code));
            EnterRoom(joined, false);
            Debug.Log($"[SpeedDial] joined room {joined.room.code} as {joined.you.color} (slot {joined.you.slot})");
            PlaceForLobby(false);
        }

        async Task LeaveAsync()
        {
            if (RoomId == null) return;
            string room = RoomId;
            await RemoveBotsAsync();
            RoomId = null;
            RoomCode = null;
            IsHost = false;
            Last = null;
            cursor = null;
            Remotes.Clear();
            Changes.ResetAll();
            Gridded = Started = ResultsIn = false;
            Game.ExternalInput = false;
            if (myPilot != null) myPilot.Active = false;
            if (Game.CurrentCar != null) await Game.ExitCar();
            PlaceForLobby(false);
            RoomChanged?.Invoke();
            try { await Session.LeaveRoomAsync(MyId, room); }
            catch (BackendException e) { Debug.LogWarning($"[SpeedDial] leave: {e.Code}"); }
            Debug.Log("[SpeedDial] left the room");
        }

        // Headless checks host or join from the command line once the course is set up.
        void AutoRoom()
        {
            if (!Online) return;
            var join = Arg("-speeddial-join");
            if (join != null) Join(join);
            else if (Flag("-speeddial-host")) Host(Flag("-speeddial-bots"));
            if (Flag("-speeddial-autopilot")) EnableAutopilot();
        }

        void OnApplicationQuit()
        {
            if (!Online || RoomId == null) return;
            _ = Session.LeaveRoomAsync(MyId, RoomId);
        }

        // Racers this device does not drive, for the HUD's floating name tabs.
        public bool IsRemote(string playerId) => playerId != MyId && !bots.Any(b => b.Id == playerId);
    }
}
