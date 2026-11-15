using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Operator.Voice;
using Dogpatch.Race;
using Dogpatch.WorldChanges;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dogpatch.Operator
{
    // Wires the voice Operator into the Speed Dial race once RaceDirector is set up: tags the
    // city for view snapshots, adds the effect layer and its HUD, and, when a Convex deployment is
    // configured and reachable, swaps the phone's LocalOperator for ConvexOperator with a
    // Scribe microphone, a Convex player and room of its own, and the room's activeEffects
    // subscription. Offline (or -operator-local, or the scripted demo) LocalOperator stays.
    // When the race itself is online (RaceDirector's session is not the local one), the Operator
    // follows the race's own player and room instead of making a solo room, so every racer's
    // calls land in, and are applied from, the same activeEffects list. This is the only place
    // activeEffects are applied; the race applies its world-change log and nothing else.
    // Also handles speeddial://operator?say=...&room=CODE, and -operator-say "<words>" or the
    // OPERATOR_SAY environment variable (SIMCTL_CHILD_OPERATOR_SAY on the iOS Simulator).
    public sealed class OperatorRig : MonoBehaviour
    {
        const string PlayerKey = "dogpatch.convexPlayerId";

        public static OperatorRig Instance { get; private set; }

        public OperatorConfig Config { get; private set; }
        public ConvexOperator Convex { get; private set; }
        public OperatorEffects Effects { get; private set; }
        public OperatorHud Hud { get; private set; }
        public string PlayerId { get; private set; } = "";
        public string RoomId { get; private set; } = "";
        public string RoomCode { get; private set; } = "";

        RaceDirector race;
        IDisposable watch;
        string queuedLink;
        bool gridded;

        [Serializable]
        class PlayerRow
        {
            public string id;
        }

        [Serializable]
        class RoomRow
        {
            public string id;
            public string code;
        }

        [Serializable]
        class RoomResult
        {
            public RoomRow room;
        }

        [Serializable]
        class CreatePlayerArgs
        {
            public string displayName;
        }

        [Serializable]
        class SoloRoomArgs
        {
            public string playerId;
            public bool freeAsks;
        }

        [Serializable]
        class JoinArgs
        {
            public string playerId;
            public string code;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance == null) new GameObject("Operator").AddComponent<OperatorRig>();
        }

        void Awake()
        {
            Instance = this;
            Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL)) queuedLink = Application.absoluteURL;
        }

        void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLink;
            watch?.Dispose();
        }

        IEnumerator Start()
        {
            while ((race = RaceDirector.Instance) == null || !race.Setup) yield return null;
            Config = OperatorConfig.Load();
            var game = race.Game;

            var clock = Stopwatch.StartNew();
            EntityTagger.TagWorld(game);
            EntityTagger.TagDynamic(game, race);
            Debug.Log($"[Operator] world tagged in {clock.ElapsedMilliseconds} ms");

            Effects = gameObject.AddComponent<OperatorEffects>();
            Effects.Registry = EffectRegistry.Default();
            Effects.Context = new EffectContext { Game = game, Race = race, Host = this };
            Hud = gameObject.AddComponent<OperatorHud>();
            Hud.Calls = race.Calls;
            Hud.Effects = Effects;
            Hud.DebugInput = Config.debugInput;
            Hud.Room = () => string.IsNullOrEmpty(RoomCode) ? "" : $"OPERATOR ROOM {RoomCode} · {Convex?.Client.Transport}";
            Hud.Confirmed = OnConfirm;
            var dragIgnore = game.Drag.IgnoreTouch;
            game.Drag.IgnoreTouch = p => Hud.HitTest(p) || (dragIgnore != null && dragIgnore(p));
            var shoveIgnore = race.Shove.IgnoreTouch;
            race.Shove.IgnoreTouch = p => Hud.HitTest(p) || (shoveIgnore != null && shoveIgnore(p));

            race.Calls.View = Snapshot;
            race.Calls.Answered += OnAnswered;
            Effects.Started += OnEffectStarted;

            if (Config.UseConvex)
            {
                var connect = ConnectAsync();
                while (!connect.IsCompleted) yield return null;
                if (connect.IsFaulted) Debug.LogWarning("[Operator] Convex setup failed, keeping LocalOperator: " + connect.Exception?.GetBaseException().Message);
            }
            else Debug.Log("[Operator] no Convex deployment configured; LocalOperator answers the phone");

            if (queuedLink != null) OnDeepLink(queuedLink);
            var args = Environment.GetCommandLineArgs();
            int pickupSay = Array.IndexOf(args, "-operator-pickup-say");
            if (pickupSay >= 0 && pickupSay + 1 < args.Length) race.Calls.PickupSay = args[pickupSay + 1];
            int say = Array.IndexOf(args, "-operator-say");
            var words = say >= 0 && say + 1 < args.Length ? args[say + 1] : Environment.GetEnvironmentVariable("OPERATOR_SAY");
            if (!string.IsNullOrWhiteSpace(words))
            {
                yield return new WaitForSeconds(2f);
                race.Calls.Say(words);
            }
        }

        async Task ConnectAsync()
        {
            var clock = Stopwatch.StartNew();
            var client = await ConvexClients.ConnectAsync(Config.convexUrl, Config.transport, "operator:activeEffects", JsonUtility.ToJson(new RoomArgs { roomId = "probe" }), Config.connectTimeoutS);
            if (client == null) return;
            var convex = new ConvexOperator(client, Config);
            Convex = convex;
            if (RaceOnline) PlayerId = race.MyId;
            else
            {
                PlayerId = await EnsurePlayerAsync(client);
                var json = string.IsNullOrEmpty(Config.roomCode)
                    ? await client.MutationAsync("rooms:create", JsonUtility.ToJson(new SoloRoomArgs { playerId = PlayerId, freeAsks = true }))
                    : await client.MutationAsync("rooms:join", JsonUtility.ToJson(new JoinArgs { playerId = PlayerId, code = Config.roomCode }));
                var room = JsonUtility.FromJson<RoomResult>(json).room;
                UseRoom(room.id, room.code);
            }

            var voice = gameObject.AddComponent<ScribeVoice>();
            voice.Config = Config;
            voice.AskPermission();
            voice.MintToken = () => convex.MintScribeTokenAsync(PlayerId);
            var calls = race.Calls;
            calls.Operator = convex;
            calls.PlayerId = () => PlayerId;
            calls.RoomId = () => RoomId;
            calls.Voice = voice;
            EntityTagger.MyPlayerId = PlayerId;
            EntityTagger.TagDynamic(race.Game, race);
            Effects.ServerNow = () => convex.ServerNowMs;
            Debug.Log($"[Operator] voice Operator on Convex ({client.Transport}) in {clock.ElapsedMilliseconds} ms: player {PlayerId}, room {RoomCode} ({RoomId})");
        }

        async Task<string> EnsurePlayerAsync(IConvexClient client)
        {
            var saved = PlayerPrefs.GetString(PlayerKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                try
                {
                    await client.QueryAsync("players:get", JsonUtility.ToJson(new PlayerArgs { playerId = saved }));
                    return saved;
                }
                catch (ConvexException e) when (e.Code != "NETWORK")
                {
                    Debug.Log($"[Operator] saved Convex player {saved} is gone ({e.Code}); making a new one");
                }
            }
            var name = race.Game.Profile?.displayName;
            var json = await client.MutationAsync("players:create", JsonUtility.ToJson(new CreatePlayerArgs { displayName = string.IsNullOrEmpty(name) ? "Dogpatch Driver" : name }));
            var id = JsonUtility.FromJson<PlayerRow>(json).id;
            PlayerPrefs.SetString(PlayerKey, id);
            PlayerPrefs.Save();
            return id;
        }

        bool RaceOnline => race != null && race.Session != null && !(race.Session is LocalRaceSession);

        void UseRoom(string id, string code)
        {
            watch?.Dispose();
            RoomId = id;
            RoomCode = code;
            watch = Convex.WatchEffects(RoomId, Effects.OnActiveList);
        }

        void Update()
        {
            if (race.Gridded && !gridded)
            {
                Effects.ClearUntilNow();
                DemolishEffect.RebuildAll();
                Debug.Log("[Operator] race locked: every effect cleared for a clean start");
            }
            else if (!race.Gridded && gridded) Effects.StopClearing();
            gridded = race.Gridded;
            bool racing = RaceOnline && !string.IsNullOrEmpty(race.RoomId);
            var wanted = racing ? race.RoomId : race.WorldRoomId;
            if (Convex == null || string.IsNullOrEmpty(wanted) || (wanted == RoomId && race.MyId == PlayerId)) return;
            PlayerId = race.MyId;
            EntityTagger.MyPlayerId = PlayerId;
            Effects.ResetAll();
            Effects.StopClearing();
            if (racing) DemolishEffect.RebuildAll();
            UseRoom(wanted, racing ? race.Room?.code ?? "" : "world");
            Debug.Log($"[Operator] following the {(racing ? "race" : "world")} room {RoomCode} ({RoomId}) as {PlayerId}");
        }

        // Every voice effect, from any racer, reaches the news bar once: Started fires once per row id,
        // from the caller's own answer or from the subscription on everyone else's device.
        void OnEffectStarted(ActiveEffect row)
        {
            var hud = race.Hud;
            if (hud == null) return;
            var racer = race.Last?.racers?.FirstOrDefault(r => r.playerId == row.requestedBy);
            var who = row.requestedBy == PlayerId ? "YOU" : racer != null ? WorldChangeCatalog.ShortName(racer.displayName) : "SOMEONE";
            bool ride = row.effect == "change_vehicle";
            var words = (ride ? $"{who} GOT A {row.vehicle}" : $"{who} CALLED {row.label ?? row.effect}").Replace('_', ' ').ToUpperInvariant().Split(' ');
            var news = string.Join(" ", words.Take(ride ? 5 : 4));
            hud.PushNews(news, NewsPicto(row.effect), racer?.color ?? "blue");
            Debug.Log($"[Operator] news \"{news}\" for {row.effect} {row.id}");
        }

        static string NewsPicto(string effect)
        {
            switch (effect)
            {
                case "rain":
                case "storm":
                    return "rain";
                case "snow":
                case "fog":
                case "recolor":
                    return effect;
                case "night":
                case "sunset":
                    return "time";
                case "gravity_lower":
                case "gravity_higher":
                case "gravity_normal":
                    return "gravity";
                case "more_traffic":
                    return "traffic";
                default:
                    return "call";
            }
        }

        public async void JoinRoom(string code)
        {
            if (Convex == null || string.IsNullOrWhiteSpace(code)) return;
            if (RaceOnline)
            {
                Debug.Log($"[Operator] ignoring room {code}: the Operator follows the online race's room");
                return;
            }
            try
            {
                var json = await Convex.Client.MutationAsync("rooms:join", JsonUtility.ToJson(new JoinArgs { playerId = PlayerId, code = code.Trim() }));
                Effects.ResetAll();
                var room = JsonUtility.FromJson<RoomResult>(json).room;
                UseRoom(room.id, room.code);
                Debug.Log($"[Operator] joined room {RoomCode} ({RoomId})");
            }
            catch (ConvexException e)
            {
                Debug.LogWarning($"[Operator] join {code} failed {e.Code}: {e.Message}");
                Hud.Toast("No such room.");
            }
        }

        OperatorView Snapshot()
        {
            var clock = Stopwatch.StartNew();
            var game = race.Game;
            EntityTagger.TagDynamic(game, race);
            var car = game.CurrentCar;
            var focus = car != null ? car.transform.position : game.Player.transform.position;
            var view = ViewSnapshot.Take(game.View, focus, car != null, new[] { game.Player.transform, car != null ? car.transform : null }, Effects.TimeOfDay, Effects.Weather);
            Debug.Log($"[Operator] view in {clock.Elapsed.TotalMilliseconds:F1} ms: {JsonUtility.ToJson(view)}");
            return view;
        }

        void OnAnswered(OperatorResponse reply)
        {
            if (reply.outcome == null) return;
            if (reply.outcome == "applied" && reply.effect != null)
            {
                Effects.Optimistic(reply.effect);
                SpendLocalAsk();
            }
            else if (reply.outcome == "pending" && reply.effect != null) Hud.Confirm(reply.effect);
            else if (reply.outcome == "refused") Hud.Toast(reply.refusal?.message ?? reply.line);
        }

        async void OnConfirm(ActiveEffect row, bool yes)
        {
            if (!yes || Convex == null) return;
            if (!RaceOnline && !race.Calls.HasAsks)
            {
                Hud.Toast(CallController.NoAsksLine);
                return;
            }
            try
            {
                Effects.Optimistic(await Convex.ConfirmAsync(PlayerId, row.id));
                SpendLocalAsk();
            }
            catch (ConvexException e)
            {
                Debug.LogWarning($"[Operator] confirm {row.id} failed {e.Code}: {e.Message}");
                Hud.Toast(e.Message == CallController.NoAsksLine ? e.Message : "Too late, sugar.");
            }
        }

        // Online, operator:call spends the caller's ask on the race server and the poll brings the new
        // balance. In the solo room (a free-asks room) the local race session keeps the count instead.
        async void SpendLocalAsk()
        {
            if (RaceOnline || !race.Calls.CountsAsks) return;
            try
            {
                var spent = await race.Session.SpendAskAsync(race.MyId, race.RoomId);
                Debug.Log($"[Operator] spent a local ask, {spent.asks} left");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Operator] local ask spend failed: " + e.Message);
            }
        }

        // speeddial://operator?say=make%20it%20rain  and  speeddial://operator?room=ABCD
        void OnDeepLink(string url)
        {
            if (Effects == null)
            {
                queuedLink = url;
                return;
            }
            queuedLink = null;
            Debug.Log("[Operator] deep link " + url);
            int q = url.IndexOf('?');
            if (q < 0 || !url.StartsWith("speeddial://operator", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var pair in url.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var key = pair.Substring(0, eq);
                var value = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                if (key == "room") JoinRoom(value);
                else if (key == "say" && Config.debugInput) race.Calls.Say(value);
            }
        }
    }
}
