using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dogpatch.Audio;
using Dogpatch.Data;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace Dogpatch.Ambient
{
    // Ambient pedestrians around the player: spawns them out of view on the walk graph, sends them
    // on errands (benches, bus stops, doors, Pier 70) or strolls, steers them along the right-hand
    // side of the sidewalk with simple predictive avoidance, makes them wait and look both ways at
    // crosswalks, dive away from cars, tumble and get up when hit or shoved, and draws them all
    // through the instanced crowd renderer. Distant people think less often and animate coarser.
    public sealed partial class PedestrianSystem : MonoBehaviour
    {
        public static PedestrianSystem Instance { get; private set; }
        public static Vector3? FocusOverride;
        public static bool ShowStats;

        public static int Count, Near, Drawn, DrawCalls;
        public static float AiMs, RenderMs;

        public int TargetCount = 48;
        public PedestrianGraph Graph { get; private set; }

        const float SpawnMin = 35f;
        const float SpawnMax = 95f;
        const float DespawnFar = 125f;
        const float DespawnHidden = 85f;
        const float HashCell = 4f;
        const int PathsPerFrame = 3;
        const float Gravity = 16f;
        const int MaxLaunched = 4;
        const float StumbleBelow = 3.2f;
        const float LaunchAbove = 8f;
        const float RollAbove = 2.2f;
        const float Pace = 1.15f;
        const float WarnSeconds = 1.9f;
        const float ThreatRange = 45f;

        [Serializable]
        sealed class Manifest
        {
            public CharacterEntry[] characters;
            public PropEntry[] props;
        }

        [Serializable]
        sealed class CharacterEntry
        {
            public string id;
            public string clips;
            public string role;
            public string age;
            public float weight;
            public string[] top;
            public string[] under;
            public string[] bottom;
            public string[] hair;
            public string[] skin;
        }

        [Serializable]
        sealed class PropEntry
        {
            public string id;
            public string role;
            public string[] from;
            public string[] to;
        }

        sealed class Cast
        {
            public CrowdModel Model;
            public CharacterEntry Entry;
        }

        static readonly string[] TopColors = { "#FFE27A", "#8CE3BF", "#FF9C86", "#BBA6F7", "#9ED7FF", "#FFF3DA", "#2C4FA3", "#E2603F", "#FFC21A", "#48C26A", "#1C2347", "#F7F8F9" };
        static readonly string[] UnderColors = { "#FFF3DA", "#F7F8F9", "#2C4FA3", "#FFE27A", "#9ED7FF", "#1C2347" };
        static readonly string[] BottomColors = { "#2C4FA3", "#1C2347", "#6C758B", "#FFF3DA", "#A9ADB4", "#C94A32", "#2FA35A" };
        static readonly string[] HairColors = { "#2B2A29", "#4A3825", "#6E3613", "#B8BFC5", "#F0CC78", "#8A4A2C", "#1C1714" };
        static readonly string[] SkinColors = { "#F2CBA0", "#E0A96D", "#D8A57E", "#B9825A", "#8D5A3B", "#5E3B26" };

        WorldContext world;
        CrowdRenderer crowd;
        readonly List<Cast> walkers = new List<Cast>();
        readonly List<Cast> skaters = new List<Cast>();
        CrowdModel skateboard;
        CrowdModel cart;
        readonly List<Pedestrian> active = new List<Pedestrian>();
        internal IReadOnlyList<Pedestrian> Active => active;
        readonly Stack<Pedestrian> pool = new Stack<Pedestrian>();
        readonly Dictionary<Vector2Int, List<Pedestrian>> hash = new Dictionary<Vector2Int, List<Pedestrian>>();
        readonly Stack<List<Pedestrian>> hashLists = new Stack<List<Pedestrian>>();
        readonly Dictionary<Vector2Int, List<int>> spotGrid = new Dictionary<Vector2Int, List<int>>();
        readonly List<int> scratch = new List<int>();
        readonly Random rng = new Random(2026);
        readonly Plane[] planes = new Plane[6];
        CarController[] cars = Array.Empty<CarController>();
        PlayerController player;
        Vector2 playerPos;
        Vector2 playerVel;
        bool playerOnFoot;
        Vector2 focus;
        Vector3 cameraPos;
        float nextRefresh;
        float nextSpawn;
        float nextLog;
        int pathsThisFrame;
        int nextId;
        bool filled;
        float aiAccum, renderAccum;
        int statFrames;
        readonly Stopwatch watch = new Stopwatch();

        Color32[] TopPalette, UnderPalette, BottomPalette, HairPalette, SkinPalette;

        public static PedestrianSystem Create(WorldContext world, WorldData data)
        {
            var go = new GameObject("Pedestrians");
            go.transform.SetParent(world.Root.transform, false);
            var system = go.AddComponent<PedestrianSystem>();
            system.Init(world, data);
            return system;
        }

        void Init(WorldContext ctx, WorldData data)
        {
            Instance = this;
            world = ctx;
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg == "-ambientstats") ShowStats = true;

            float t0 = Time.realtimeSinceStartup;
            Graph = new PedestrianGraph(data, ctx);
            float t1 = Time.realtimeSinceStartup;
            crowd = new CrowdRenderer();
            StartCoroutine(LoadCast());

            for (int i = 0; i < Graph.Spots.Count; i++)
            {
                var key = SpotCell(Graph.Spots[i].Position);
                if (!spotGrid.TryGetValue(key, out var list)) spotGrid[key] = list = new List<int>();
                list.Add(i);
            }
            int signals = 0, fromData = 0;
            foreach (var c in Graph.Crossings)
            {
                if (c.Signal) signals++;
                if (c.FromData) fromData++;
            }
            var kinds = new int[4];
            foreach (var s in Graph.Spots) kinds[(int)s.Kind]++;
            Debug.Log($"[Ambient] walk graph {Graph.Nodes.Count} nodes, {Graph.Crossings.Count} crossings ({fromData} from OSM footways, {signals} signalised), spots: {kinds[0]} bench seats, {kinds[1]} bus stop places, {kinds[2]} doors, {kinds[3]} pier; built in {(t1 - t0) * 1000f:F0} ms");
        }

        static Vector2Int SpotCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / 40f), Mathf.FloorToInt(p.y / 40f));

        static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Color32[] Palette(string[] hexes)
        {
            var result = new Color32[hexes.Length];
            for (int i = 0; i < hexes.Length; i++) result[i] = Hex(hexes[i]);
            return result;
        }

        // Bakes one character per frame so startup never stalls on the whole cast; people start
        // appearing as soon as the first model is ready.
        System.Collections.IEnumerator LoadCast()
        {
            var clock = Stopwatch.StartNew();
            float longest = 0f;
            TopPalette = Palette(TopColors);
            UnderPalette = Palette(UnderColors);
            BottomPalette = Palette(BottomColors);
            HairPalette = Palette(HairColors);
            SkinPalette = Palette(SkinColors);

            var manifest = JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("Ambient/pedestrians").text);
            var assets = new Dictionary<string, GlbAsset>();
            GlbAsset Load(string id)
            {
                if (assets.TryGetValue(id, out var a)) return a;
                var bytes = Resources.Load<TextAsset>("Ambient/" + id);
                a = bytes == null ? null : GlbAsset.Load(id, bytes.bytes);
                assets[id] = a;
                return a;
            }

            foreach (var entry in manifest.characters)
            {
                var shape = Load(entry.id);
                var clips = Load(entry.clips);
                if (shape == null || clips == null) continue;
                var zones = new Dictionary<Color32, Slot>();
                void Zone(string[] hexes, Slot slot)
                {
                    if (hexes == null) return;
                    foreach (var h in hexes) zones[Hex(h)] = slot;
                }
                Zone(entry.skin, Slot.Skin);
                Zone(entry.hair, Slot.Hair);
                Zone(entry.bottom, Slot.Bottom);
                Zone(entry.under, Slot.Under);
                Zone(entry.top, Slot.Top);
                var cast = new Cast { Model = CrowdModel.FromCharacter(shape, clips, zones), Entry = entry };
                (entry.role == "skater" ? skaters : walkers).Add(cast);
                longest = Mathf.Max(longest, (float)clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                yield return null;
            }

            foreach (var prop in manifest.props)
            {
                var asset = Load(prop.id);
                if (asset == null) continue;
                var recolor = new Dictionary<Color32, Color>();
                for (int i = 0; i < prop.from.Length && i < prop.to.Length; i++) recolor[Hex(prop.from[i])] = ((Color)Hex(prop.to[i])).linear;
                var model = CrowdModel.FromStatic(prop.id, asset.BakeStatic(), recolor);
                if (prop.role == "skateboard") skateboard = model;
                else if (prop.role == "cart") cart = model;
            }
            foreach (var asset in assets.Values)
                if (asset != null)
                    foreach (var mesh in asset.Meshes) Destroy(mesh);
            Debug.Log($"[Ambient] {walkers.Count + skaters.Count} crowd models baked, one per frame, longest step {longest:F0} ms");
        }

        // --- frame loop ------------------------------------------------------------------------

        void Update()
        {
            watch.Restart();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float now = Time.time;
            pathsThisFrame = 0;

            if (now >= nextRefresh)
            {
                nextRefresh = now + 1f;
                cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
                if (player == null) player = FindAnyObjectByType<PlayerController>();
            }
            playerOnFoot = player != null && player.isActiveAndEnabled;
            if (playerOnFoot)
            {
                var p = new Vector2(player.transform.position.x, player.transform.position.z);
                playerVel = dt > 0f ? Vector2.Lerp(playerVel, (p - playerPos) / dt, 0.5f) : playerVel;
                playerPos = p;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cameraPos = cam.transform.position;
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
            }
            var f = FocusOverride ?? (playerOnFoot ? player.transform.position : cameraPos);
            focus = new Vector2(f.x, f.z);

            if (now >= nextSpawn)
            {
                nextSpawn = now + 0.2f;
                ManageSpawns();
            }

            BuildHash();
            int frame = Time.frameCount;
            for (int i = 0; i < active.Count; i++)
            {
                var ped = active[i];
                ped.Pending += dt;
                float d = Vector2.Distance(ped.Pos, new Vector2(cameraPos.x, cameraPos.z));
                int interval = d < 30f ? 1 : d < 60f ? 2 : 4;
                if (ped.State >= PedState.Dive) interval = 1;
                if ((frame + ped.Id) % interval != 0) continue;
                Tick(ped, ped.Pending, now, interval == 1);
                ped.Pending = 0f;
            }

            watch.Stop();
            aiAccum += (float)watch.Elapsed.TotalMilliseconds;
        }

        void LateUpdate()
        {
            watch.Restart();
            Render();
            watch.Stop();
            renderAccum += (float)watch.Elapsed.TotalMilliseconds;

            statFrames++;
            if (Time.time >= nextLog)
            {
                AiMs = aiAccum / Mathf.Max(statFrames, 1);
                RenderMs = renderAccum / Mathf.Max(statFrames, 1);
                aiAccum = renderAccum = 0f;
                statFrames = 0;
                nextLog = Time.time + 2f;
                Count = active.Count;
                Near = 0;
                foreach (var p in active) if (Vector2.Distance(p.Pos, focus) < 50f) Near++;
                Debug.Log($"[Ambient] pedestrians={Count} near50m={Near} drawn={Drawn} drawCalls={DrawCalls} ai={AiMs:F3}ms render={RenderMs:F3}ms total={AiMs + RenderMs:F3}ms/frame");
            }
        }

        void OnGUI()
        {
            if (!ShowStats) return;
            GUI.Label(new Rect(12, Screen.height - 34, 900, 30), $"pedestrians {Count}  within 50 m {Near}  drawn {Drawn}  draw calls {DrawCalls}  CPU {AiMs + RenderMs:F2} ms/frame (AI {AiMs:F2}, render {RenderMs:F2})");
        }

        void OnDestroy()
        {
            foreach (var p in active) if (p.Registered) Yielding.Crossing.Remove(p);
            if (Instance == this) Instance = null;
        }

        void BuildHash()
        {
            foreach (var list in hash.Values)
            {
                list.Clear();
                hashLists.Push(list);
            }
            hash.Clear();
            foreach (var p in active)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.Pos.x / HashCell), Mathf.FloorToInt(p.Pos.y / HashCell));
                if (!hash.TryGetValue(key, out var list)) hash[key] = list = hashLists.Count > 0 ? hashLists.Pop() : new List<Pedestrian>();
                list.Add(p);
            }
        }

        bool InView(Vector2 p, float distance)
        {
            if (distance > 140f) return false;
            return GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(p.x, 1f, p.y), new Vector3(2.5f, 2.6f, 2.5f)));
        }

        // --- spawning ------------------------------------------------------------------------------

        void ManageSpawns()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                float d = Vector2.Distance(p.Pos, focus);
                if (d > DespawnFar || (d > DespawnHidden && !InView(p.Pos, d) && p.State < PedState.Dive)) Despawn(p);
            }
            int budget = filled ? 2 : 12;
            for (int tries = 0; active.Count < TargetCount && budget > 0 && tries < 40; tries++)
                if (TrySpawn(!filled)) budget--;
            if (active.Count >= TargetCount) filled = true;
        }

        bool TrySpawn(bool initial)
        {
            if (walkers.Count == 0) return false;
            double a = rng.NextDouble() * Math.PI * 2.0;
            float min = initial ? 6f : SpawnMin;
            float r = Mathf.Lerp(min, SpawnMax, Mathf.Sqrt((float)rng.NextDouble()));
            var guess = focus + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
            int node = Graph.Nearest(guess, 14f, false);
            if (node < 0 || Graph.ComponentSize[Graph.Component[node]] < 40) return false;
            var pos = Graph.Nodes[node];
            float d = Vector2.Distance(pos, focus);
            if (d > SpawnMax + 10f || (!initial && d < 60f && InView(pos, d))) return false;
            if (Crowded(pos, 2f)) return false;

            double roll = rng.NextDouble();
            var kind = Graph.Kinds[node];
            var role = PedRole.Walker;
            if (roll < 0.06 && skaters.Count > 0 && skateboard != null && kind != WalkKind.Plaza) role = PedRole.Skater;
            else if (roll < 0.11 && cart != null && kind == WalkKind.Sidewalk) role = PedRole.Cart;

            var ped = Spawn(role, pos);
            ped.Heading = (float)rng.NextDouble() * 360f;

            if (initial && role == PedRole.Walker && rng.NextDouble() < 0.25 && SeatAtSpot(ped))
                return true;
            NewTrip(ped, node);
            return true;
        }

        bool Crowded(Vector2 p, float radius)
        {
            foreach (var q in active) if ((q.Pos - p).sqrMagnitude < radius * radius) return true;
            return false;
        }

        Pedestrian Spawn(PedRole role, Vector2 pos)
        {
            var ped = pool.Count > 0 ? pool.Pop() : CreateBody();
            ped.gameObject.SetActive(true);
            ped.Id = nextId++;
            ped.Role = role;
            var cast = role == PedRole.Skater ? skaters[rng.Next(skaters.Count)] : PickWalker();
            ped.Model = cast.Model;
            ped.Outfit = new CrowdRenderer.Outfit
            {
                Top = Linear(TopPalette),
                Under = Linear(UnderPalette),
                Bottom = Linear(BottomPalette),
                Hair = Linear(HairPalette),
                Skin = Linear(SkinPalette),
            };
            ped.Cruise = role switch
            {
                PedRole.Skater => Range(3.4f, 4.8f),
                PedRole.Cart => Range(0.9f, 1.15f),
                _ => cast.Entry.age switch
                {
                    "old" => Range(0.85f, 1.15f),
                    "young" => Range(1.3f, 1.7f),
                    "kid" => Range(1.1f, 1.6f),
                    _ => Range(1.15f, 1.55f),
                },
            };
            ped.Cruise *= Pace;
            ped.Radius = role == PedRole.Walker ? 0.3f : 0.5f;
            ped.Lateral = Range(0.35f, 0.9f);
            ped.Pos = pos;
            ped.Vel = Vector2.zero;
            ped.State = PedState.Walk;
            ped.StateTime = 0f;
            ped.Spot = -1;
            ped.Crossing = -1;
            ped.Lean = ped.Hurry = 0f;
            ped.Posed = false;
            ped.FadeCap = 1f;
            ped.FadeSpeed = 1f / 0.22f;
            ped.HeadYaw = ped.HeadTarget = 0f;
            ped.Clip = ped.PrevClip = Clip.Idle;
            ped.ClipTime = (float)rng.NextDouble() * 2f;
            ped.Fade = 1f;
            ped.Path.Clear();
            ped.transform.position = new Vector3(pos.x, 0.03f, pos.y);
            active.Add(ped);
            return ped;
        }

        Pedestrian CreateBody()
        {
            var go = new GameObject("Pedestrian");
            go.transform.SetParent(transform, false);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = 0.3f;
            capsule.height = 1.75f;
            capsule.center = new Vector3(0f, 0.875f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var ped = go.AddComponent<Pedestrian>();
            ped.Owner = this;
            return ped;
        }

        Cast PickWalker()
        {
            float total = 0f;
            foreach (var c in walkers) total += c.Entry.weight;
            float pick = (float)rng.NextDouble() * total;
            foreach (var c in walkers)
            {
                pick -= c.Entry.weight;
                if (pick <= 0f) return c;
            }
            return walkers[walkers.Count - 1];
        }

        Vector4 Linear(Color32[] palette)
        {
            var c = ((Color)palette[rng.Next(palette.Length)]).linear;
            return new Vector4(c.r, c.g, c.b, 1f);
        }

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        void Despawn(Pedestrian ped)
        {
            Unregister(ped);
            ReleaseSpot(ped);
            ped.gameObject.SetActive(false);
            active.Remove(ped);
            pool.Push(ped);
        }

        void Unregister(Pedestrian ped)
        {
            if (!ped.Registered) return;
            Yielding.Crossing.Remove(ped);
            ped.Registered = false;
        }

        void Register(Pedestrian ped)
        {
            if (ped.Registered) return;
            Yielding.Crossing.Add(ped);
            ped.Registered = true;
        }

        void ReleaseSpot(Pedestrian ped)
        {
            if (ped.Spot < 0) return;
            var s = Graph.Spots[ped.Spot];
            if (s.Occupant == ped.Id)
            {
                s.Occupant = -1;
                Graph.Spots[ped.Spot] = s;
            }
            ped.Spot = -1;
        }

        bool Reserve(Pedestrian ped, int spot)
        {
            var s = Graph.Spots[spot];
            if (s.Kind != SpotKind.Door && s.Kind != SpotKind.Pier && s.Occupant >= 0) return false;
            if (s.Kind == SpotKind.Bench || s.Kind == SpotKind.BusStop) s.Occupant = ped.Id;
            Graph.Spots[spot] = s;
            ped.Spot = spot;
            return true;
        }

        // Start already seated on a bench or waiting at a bus stop, so the street looks lived in.
        bool SeatAtSpot(Pedestrian ped)
        {
            var spot = RandomSpot(ped.Pos, 0f, 70f, rng.NextDouble() < 0.6 ? SpotKind.Bench : SpotKind.BusStop);
            if (spot < 0 || !Reserve(ped, spot)) return false;
            var s = Graph.Spots[spot];
            ped.Pos = s.Position;
            ped.Heading = Mathf.Atan2(s.Facing.x, s.Facing.y) * Mathf.Rad2Deg;
            Enter(ped, s.Kind == SpotKind.Bench ? PedState.Sit : PedState.BusWait, s.Kind == SpotKind.Bench ? Range(10f, 50f) : Range(10f, 40f));
            return true;
        }

        int RandomSpot(Vector2 from, float minDistance, float maxDistance, SpotKind kind)
        {
            int best = -1;
            int seen = 0;
            var c = SpotCell(from);
            int reach = Mathf.CeilToInt(maxDistance / 40f);
            for (int x = c.x - reach; x <= c.x + reach; x++)
                for (int z = c.y - reach; z <= c.y + reach; z++)
                {
                    if (!spotGrid.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        var s = Graph.Spots[i];
                        if (s.Kind != kind || s.Occupant >= 0) continue;
                        float d = Vector2.Distance(s.Position, from);
                        if (d < minDistance || d > maxDistance) continue;
                        if (rng.Next(++seen) == 0) best = i;
                    }
                }
            return best;
        }

        // --- errands ---------------------------------------------------------------------------------

        void NewTrip(Pedestrian ped, int startNode = -1)
        {
            ReleaseSpot(ped);
            if (startNode < 0) startNode = Graph.Nearest(ped.Pos, 12f, false);
            if (startNode < 0)
            {
                Enter(ped, PedState.Idle, 1f);
                return;
            }
            ped.Path.Clear();
            ped.PathIndex = 0;
            if (ped.Role == PedRole.Walker && rng.NextDouble() < 0.7)
            {
                if (pathsThisFrame >= PathsPerFrame)
                {
                    Enter(ped, PedState.Idle, 0.25f);
                    return;
                }
                double roll = rng.NextDouble();
                bool nearPier = PedestrianGraph.Pier70.Contains(ped.Pos) || Vector2.Distance(ped.Pos, PedestrianGraph.Pier70.center) < 260f;
                var kind = roll < 0.4 ? SpotKind.Door : roll < 0.62 ? SpotKind.Bench : roll < 0.8 ? SpotKind.BusStop : nearPier ? SpotKind.Pier : SpotKind.Door;
                int spot = RandomSpot(ped.Pos, 25f, 140f, kind);
                if (spot >= 0)
                {
                    pathsThisFrame++;
                    if (Graph.FindPath(startNode, Graph.Spots[spot].Node, ped.Path) && Reserve(ped, spot))
                    {
                        Enter(ped, PedState.Walk, 0f);
                        return;
                    }
                    ped.Path.Clear();
                }
            }
            Stroll(ped, startNode);
            Enter(ped, PedState.Walk, 0f);
        }

        // A stroll: keep going along the network, mostly straight on, sometimes turning or crossing.
        void Stroll(Pedestrian ped, int start)
        {
            ped.Path.Clear();
            ped.Path.Add(start);
            int prev = -1, cur = start;
            var dir = new Vector2(Mathf.Sin(ped.Heading * Mathf.Deg2Rad), Mathf.Cos(ped.Heading * Mathf.Deg2Rad));
            int length = rng.Next(12, 30);
            for (int k = 0; k < length; k++)
            {
                var edges = Graph.Edges[cur];
                int pick = -1;
                float best = float.MinValue;
                foreach (var e in edges)
                {
                    if (e.To == prev) continue;
                    if (e.Kind == WalkKind.Crossing && ped.Role == PedRole.Cart) continue;
                    var d = (Graph.Nodes[e.To] - Graph.Nodes[cur]).normalized;
                    float score = Vector2.Dot(d, dir) * 2f + (float)rng.NextDouble() * 1.6f;
                    if (e.Kind == WalkKind.Crossing) score -= 0.8f;
                    if (score > best)
                    {
                        best = score;
                        pick = e.To;
                    }
                }
                if (pick < 0) break;
                dir = (Graph.Nodes[pick] - Graph.Nodes[cur]).normalized;
                prev = cur;
                cur = pick;
                ped.Path.Add(cur);
            }
        }

        void Enter(Pedestrian ped, PedState state, float length)
        {
            ped.State = state;
            ped.StateTime = 0f;
            ped.StateLength = length;
        }

        void Arrive(Pedestrian ped)
        {
            if (ped.Spot < 0)
            {
                if (rng.NextDouble() < 0.3) Enter(ped, PedState.Idle, Range(2f, 6f));
                else NewTrip(ped);
                return;
            }
            var s = Graph.Spots[ped.Spot];
            ped.Target = s.Position;
            ped.TargetHeading = Mathf.Atan2(s.Facing.x, s.Facing.y) * Mathf.Rad2Deg;
            ped.Then = s.Kind switch
            {
                SpotKind.Bench => PedState.Sit,
                SpotKind.BusStop => PedState.BusWait,
                SpotKind.Pier => PedState.Idle,
                _ => PedState.Approach,
            };
            Enter(ped, PedState.Approach, 0f);
        }

        // --- behaviour -------------------------------------------------------------------------------

        void Tick(Pedestrian ped, float dt, float now, bool near)
        {
            ped.StateTime += dt;
            if (ped.State < PedState.Dive) CheckThreats(ped);

            switch (ped.State)
            {
                case PedState.Walk: Walk(ped, dt, now, near); break;
                case PedState.Approach: Approach(ped, dt, near); break;
                case PedState.Idle:
                    Brake(ped, dt);
                    if (ped.StateTime >= ped.StateLength)
                    {
                        if (ped.Path.Count > 0 && ped.PathIndex < ped.Path.Count) Enter(ped, PedState.Walk, 0f);
                        else NewTrip(ped);
                    }
                    break;
                case PedState.WaitCurb: WaitCurb(ped, dt, now); break;
                case PedState.Sit:
                case PedState.BusWait:
                    ped.Vel = Vector2.zero;
                    if (ped.StateTime >= ped.StateLength)
                    {
                        if (ped.State == PedState.BusWait && rng.NextDouble() < 0.5 && !InView(ped.Pos, Vector2.Distance(ped.Pos, focus)))
                        {
                            Despawn(ped);
                            return;
                        }
                        var s = Graph.Spots[ped.Spot >= 0 ? ped.Spot : 0];
                        if (ped.State == PedState.Sit) ped.Pos = s.Position + s.Facing * 0.6f;
                        NewTrip(ped);
                    }
                    break;
                case PedState.Dive: Dive(ped, dt); break;
                case PedState.Startled:
                    Brake(ped, dt);
                    ped.Lean = Mathf.MoveTowards(ped.Lean, 0f, 20f * dt);
                    if (ped.StateTime >= ped.StateLength)
                    {
                        ped.Hurry = 4f;
                        Enter(ped, PedState.Walk, 0f);
                        if (ped.Path.Count == 0 || ped.PathIndex >= ped.Path.Count) NewTrip(ped);
                        else Resume(ped);
                    }
                    break;
                case PedState.Stumble: Stumble(ped, dt); break;
                case PedState.Tumble: Tumble(ped, dt); break;
                case PedState.Down: Down(ped, dt); break;
                case PedState.GetUp: GetUp(ped); break;
                case PedState.Dazed: Dazed(ped); break;
            }

            Head(ped, dt, now);
            Animate(ped, dt);
            ped.transform.position = new Vector3(ped.Pos.x, 0.03f, ped.Pos.y);
        }

        void Walk(Pedestrian ped, float dt, float now, bool near)
        {
            if (Mood != CrowdMood.Normal && MoodWalk(ped, dt, now, near)) return;
            if (ped.PathIndex >= ped.Path.Count)
            {
                Arrive(ped);
                return;
            }
            int node = ped.Path[ped.PathIndex];
            var target = PathPoint(ped, ped.PathIndex);
            float speed = ped.Cruise * (ped.Hurry > 0f ? 1.3f : 1f) * PlayerController.RunScale * MoodPace;
            ped.Hurry = Mathf.Max(0f, ped.Hurry - dt);
            bool crossing = ped.Crossing >= 0;
            if (crossing)
            {
                speed *= 1.12f;
                if (!GapClear(Graph.Crossings[ped.Crossing], 2.5f)) speed = Mathf.Max(speed, 2.8f);
            }
            Steer(ped, target, speed, dt, near);

            float reach = Graph.Kinds[node] == WalkKind.Curb ? 0.45f : 0.9f;
            if ((ped.Pos - target).sqrMagnitude > reach * reach && !Passed(ped, target)) return;

            if (crossing && Graph.Kinds[node] == WalkKind.Curb && ped.PathIndex > 0 && IsCrossing(ped.Path[ped.PathIndex - 1], node, out _))
            {
                ped.Crossing = -1;
                Unregister(ped);
            }
            ped.PathIndex++;
            if (ped.PathIndex < ped.Path.Count && IsCrossing(node, ped.Path[ped.PathIndex], out int c))
            {
                ped.Crossing = c;
                Register(ped);
                var across = Graph.Nodes[ped.Path[ped.PathIndex]] - Graph.Nodes[node];
                ped.TargetHeading = Mathf.Atan2(across.x, across.y) * Mathf.Rad2Deg;
                Enter(ped, PedState.WaitCurb, 0f);
                return;
            }
            if (Graph.Kinds[node] == WalkKind.Sidewalk && ped.Role == PedRole.Walker && rng.NextDouble() < 0.025)
                Enter(ped, PedState.Idle, Range(1.5f, 6f));
        }

        bool Passed(Pedestrian ped, Vector2 target)
        {
            if (ped.PathIndex + 1 >= ped.Path.Count) return false;
            var next = PathPoint(ped, ped.PathIndex + 1);
            var seg = next - target;
            return Vector2.Dot(ped.Pos - target, seg) > 0f && (ped.Pos - target).sqrMagnitude < 4f;
        }

        bool IsCrossing(int a, int b, out int crossing)
        {
            foreach (var e in Graph.Edges[a])
                if (e.To == b && e.Kind == WalkKind.Crossing)
                {
                    crossing = e.Crossing;
                    return true;
                }
            crossing = -1;
            return false;
        }

        // The path node shifted to the walker's right-hand side of the direction of travel.
        Vector2 PathPoint(Pedestrian ped, int index)
        {
            var path = ped.Path;
            var p = Graph.Nodes[path[index]];
            var kind = Graph.Kinds[path[index]];
            if (kind == WalkKind.Curb) return p;
            var a = index > 0 ? Graph.Nodes[path[index - 1]] : p;
            var b = index + 1 < path.Count ? Graph.Nodes[path[index + 1]] : p;
            var dir = (p - a).normalized + (b - p).normalized;
            if (dir.sqrMagnitude < 1e-4f) return p;
            dir.Normalize();
            float lateral = ped.Lateral * (kind == WalkKind.Plaza ? 0.5f : 1f);
            var q = p + new Vector2(dir.y, -dir.x) * lateral;
            return Graph.OnStreet(q, 0.1f) || Graph.InBuilding(q, 0.3f) ? p : q;
        }

        void Resume(Pedestrian ped)
        {
            int best = ped.PathIndex;
            float bestD = float.MaxValue;
            for (int i = Mathf.Max(0, ped.PathIndex - 2); i < Mathf.Min(ped.Path.Count, ped.PathIndex + 4); i++)
            {
                float d = (Graph.Nodes[ped.Path[i]] - ped.Pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            ped.PathIndex = best;
            if (bestD > 64f) NewTrip(ped);
        }

        void Approach(Pedestrian ped, float dt, bool near)
        {
            bool door = ped.Then == PedState.Approach;
            float d = Vector2.Distance(ped.Pos, ped.Target);
            if (d > 0.3f)
            {
                Steer(ped, ped.Target, Mathf.Min(ped.Cruise, 0.6f + d), dt, near && d > 1.5f);
                if (ped.StateTime < 15f) return;
            }
            if (door)
            {
                Despawn(ped);
                return;
            }
            Brake(ped, dt * 4f);
            ped.Heading = Mathf.MoveTowardsAngle(ped.Heading, ped.TargetHeading, 300f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(ped.Heading, ped.TargetHeading)) > 5f) return;
            ped.Pos = ped.Target;
            float length = ped.Then switch
            {
                PedState.Sit => Range(12f, 45f),
                PedState.BusWait => Range(15f, 50f),
                _ => Range(4f, 12f),
            };
            Enter(ped, ped.Then, length);
        }

        void WaitCurb(Pedestrian ped, float dt, float now)
        {
            Brake(ped, dt);
            ped.Heading = Mathf.MoveTowardsAngle(ped.Heading, ped.TargetHeading, 240f * dt);
            var crossing = Graph.Crossings[ped.Crossing];
            float t = ped.StateTime;
            bool looked = t > 1.9f;
            if (!looked) ped.HeadTarget = t < 0.65f ? -68f : t < 1.35f ? 68f : -45f;
            else ped.HeadTarget = crossing.MayStart(now) ? 0f : (Mathf.Repeat(t, 4f) < 1f ? -55f : 0f);
            if (looked && crossing.MayStart(now) && GapClear(crossing, 5.5f))
            {
                ped.HeadTarget = 0f;
                Enter(ped, PedState.Walk, 0f);
                return;
            }
            if (t > 45f)
            {
                ped.Crossing = -1;
                Unregister(ped);
                NewTrip(ped);
            }
        }

        bool GapClear(Crossing c, float horizon)
        {
            foreach (var car in cars)
            {
                if (car == null) continue;
                var p = car.transform.position;
                var v = car.transform.forward * car.Speed;
                if (!GapClear(c, new Vector2(p.x, p.z), new Vector2(v.x, v.z), horizon)) return false;
            }
            foreach (var vehicle in Yielding.Vehicles)
            {
                var p = vehicle.Position;
                var v = vehicle.Velocity;
                if (!GapClear(c, new Vector2(p.x, p.z), new Vector2(v.x, v.z), horizon)) return false;
            }
            return true;
        }

        static bool GapClear(Crossing c, Vector2 p, Vector2 v, float horizon)
        {
            if ((p - c.Center).sqrMagnitude > 90f * 90f) return true;
            float d = DistanceToSegment(p, c.A, c.B);
            if (d < 2.5f) return false;
            if (d < 4.5f && v.sqrMagnitude > 0.04f) return false;
            if (v.sqrMagnitude < 0.25f) return true;
            for (float t = 0.5f; t <= horizon; t += 0.5f)
                if (DistanceToSegment(p + v * t, c.A, c.B) < 3f) return false;
            return true;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) =>
            Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, a, b, out _));

        void Steer(Pedestrian ped, Vector2 target, float speed, float dt, bool avoid)
        {
            var to = target - ped.Pos;
            float dist = to.magnitude;
            var desired = dist > 0.01f ? to / dist * Mathf.Min(speed, dist * 2f + 0.3f) : Vector2.zero;
            var fwd = Forward(ped);
            if (dist > 0.01f && Vector2.Dot(fwd, to / dist) < 0.2f) desired *= 0.4f;
            if (avoid) desired += Avoidance(ped, fwd);
            float accel = ped.Role == PedRole.Skater ? 2f : 3f;
            ped.Vel = Vector2.MoveTowards(ped.Vel, desired, accel * dt);
            Move(ped, ped.Vel * dt);
            if (ped.Vel.sqrMagnitude > 0.02f)
            {
                float h = Mathf.Atan2(ped.Vel.x, ped.Vel.y) * Mathf.Rad2Deg;
                float delta = Mathf.Abs(Mathf.DeltaAngle(ped.Heading, h));
                ped.Heading = Mathf.MoveTowardsAngle(ped.Heading, h, (90f + delta * 4f) * dt);
            }
        }

        static Vector2 Forward(Pedestrian ped) => new Vector2(Mathf.Sin(ped.Heading * Mathf.Deg2Rad), Mathf.Cos(ped.Heading * Mathf.Deg2Rad));

        void Brake(Pedestrian ped, float dt) => ped.Vel = Vector2.MoveTowards(ped.Vel, Vector2.zero, 4f * dt);

        bool Move(Pedestrian ped, Vector2 delta)
        {
            var next = ped.Pos + delta;
            if (Graph.InBuilding(next, 0.25f))
            {
                var a = new Vector2(next.x, ped.Pos.y);
                var b = new Vector2(ped.Pos.x, next.y);
                if (!Graph.InBuilding(a, 0.25f)) ped.Pos = a;
                else if (!Graph.InBuilding(b, 0.25f)) ped.Pos = b;
                return false;
            }
            ped.Pos = next;
            return true;
        }

        // Predictive avoidance: steer away from where each neighbour will be at closest approach,
        // passing on the right when meeting head-on, plus a short-range push to keep personal space.
        Vector2 Avoidance(Pedestrian ped, Vector2 fwd)
        {
            var force = Vector2.zero;
            var right = new Vector2(fwd.y, -fwd.x);
            var c = new Vector2Int(Mathf.FloorToInt(ped.Pos.x / HashCell), Mathf.FloorToInt(ped.Pos.y / HashCell));
            for (int x = c.x - 1; x <= c.x + 1; x++)
                for (int z = c.y - 1; z <= c.y + 1; z++)
                {
                    if (!hash.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (var other in list)
                        if (other != ped) force += AvoidOne(ped, fwd, right, other.Pos, other.Vel, other.Radius);
                }
            if (playerOnFoot) force += AvoidOne(ped, fwd, right, playerPos, playerVel, 0.45f);
            return Vector2.ClampMagnitude(force, 1.6f);
        }

        static Vector2 AvoidOne(Pedestrian ped, Vector2 fwd, Vector2 right, Vector2 pos, Vector2 vel, float radius)
        {
            var rp = pos - ped.Pos;
            float d2 = rp.sqrMagnitude;
            if (d2 > 3.5f * 3.5f || d2 < 1e-6f) return Vector2.zero;
            float d = Mathf.Sqrt(d2);
            float comb = ped.Radius + radius;
            var force = Vector2.zero;
            if (d < comb + 0.25f) force -= rp / d * (comb + 0.25f - d) * 3f;
            var rv = ped.Vel - vel;
            float rv2 = rv.sqrMagnitude;
            if (rv2 < 0.01f) return force;
            float t = Vector2.Dot(rp, rv) / rv2;
            if (t <= 0f || t > 2.5f) return force;
            var closest = rp - rv * t;
            float cd = closest.magnitude;
            float clearance = comb + 0.35f;
            if (cd > clearance) return force;
            var away = cd > 0.01f ? -closest / cd : right;
            bool headOn = Vector2.Dot(vel, ped.Vel) < 0f && Vector2.Dot(rp, fwd) > 0f;
            if (headOn && Vector2.Dot(away, right) < 0f) away = right;
            force += away * (clearance - cd) * 1.6f / Mathf.Max(t, 0.35f);
            if (headOn && t < 0.6f) force -= ped.Vel * 0.4f;
            return force;
        }

        void Head(Pedestrian ped, float dt, float now)
        {
            if (ped.State == PedState.Walk || ped.State == PedState.Sit || ped.State == PedState.Idle || ped.State == PedState.BusWait)
            {
                if (now >= ped.NextGlance)
                {
                    ped.NextGlance = now + Range(2.5f, 7f);
                    bool busy = ped.State == PedState.BusWait;
                    ped.HeadTarget = rng.NextDouble() < 0.55 ? 0f : busy ? Range(-70f, -30f) : Range(-40f, 40f);
                }
            }
            if (ped.State == PedState.Startled || (ped.State == PedState.Dive && ped.Phase == 0))
            {
                var to = ped.ThreatFrom - ped.Pos;
                float yaw = Mathf.DeltaAngle(ped.Heading, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
                ped.HeadTarget = Mathf.Clamp(yaw, -75f, 75f);
            }
            else if (ped.State == PedState.Dazed) ped.HeadTarget = 35f * Mathf.Sin(ped.StateTime * 6f);
            else if (ped.State >= PedState.Dive && ped.State != PedState.Startled) ped.HeadTarget = 0f;
            ped.HeadYaw = Mathf.MoveTowards(ped.HeadYaw, Mathf.Clamp(ped.HeadTarget, -75f, 75f), 240f * dt);
        }

        void Animate(Pedestrian ped, float dt)
        {
            float speed = ped.Vel.magnitude;
            Clip clip;
            float rate = 1f;
            switch (ped.State)
            {
                case PedState.Walk:
                case PedState.Approach:
                    if (ped.Role == PedRole.Skater) clip = Clip.Idle;
                    else if (speed < 0.15f) clip = Clip.Idle;
                    else if (speed > 2.4f)
                    {
                        clip = Clip.Run;
                        rate = Mathf.Clamp(speed / ped.Model.RunSpeed, 0.7f, 1.6f);
                    }
                    else
                    {
                        clip = ped.Role == PedRole.Cart ? Clip.Push : Clip.Walk;
                        rate = Mathf.Clamp(speed / ped.Model.WalkSpeed, 0.6f, 1.7f);
                    }
                    break;
                case PedState.Sit: clip = Clip.Sit; break;
                case PedState.Stumble: rate = 1.7f; goto case PedState.Dive;
                case PedState.Dive:
                case PedState.Tumble:
                case PedState.Down:
                case PedState.GetUp:
                    clip = ped.Clip;
                    break;
                default: clip = Clip.Idle; break;
            }
            if (clip != ped.Clip)
            {
                bool cycle = (clip == Clip.Walk || clip == Clip.Push) && (ped.Clip == Clip.Walk || ped.Clip == Clip.Push);
                float time = ped.ClipTime;
                Pose(ped, clip, 0.22f);
                if (cycle) ped.ClipTime = time;
            }
            ped.ClipTime += dt * rate;
            ped.PrevClipTime += dt * rate;
            ped.Fade = Mathf.MoveTowards(ped.Fade, ped.FadeCap, dt * ped.FadeSpeed);
        }

        // Cross-fades to a clip over the given time; cap < 1 holds the blend part-way, so two
        // clips mix (running legs under half-raised flailing arms, for a stumble).
        static void Pose(Pedestrian ped, Clip clip, float fade, float cap = 1f)
        {
            ped.FadeSpeed = 1f / Mathf.Max(fade, 0.01f);
            ped.FadeCap = cap;
            if (clip == ped.Clip) return;
            if (ped.Fade >= 0.5f)
            {
                ped.PrevClip = ped.Clip;
                ped.PrevClipTime = ped.ClipTime;
            }
            ped.Clip = clip;
            ped.ClipTime = 0f;
            ped.Fade = 0f;
        }

        // --- cars, the player and shoves -------------------------------------------------------

        void CheckThreats(Pedestrian ped)
        {
            foreach (var car in cars)
            {
                if (car == null || Mathf.Abs(car.Speed) < 3f) continue;
                var t = car.transform;
                if ((new Vector2(t.position.x, t.position.z) - ped.Pos).sqrMagnitude > ThreatRange * ThreatRange) continue;
                var v = new Vector2(t.forward.x, t.forward.z) * car.Speed;
                if (Predict(ped, new Vector2(t.position.x, t.position.z), v, car.HalfExtents.x + 0.6f, out float tti))
                {
                    Threat(ped, new Vector2(t.position.x, t.position.z), v, tti);
                    return;
                }
            }
            foreach (var vehicle in Yielding.Vehicles)
            {
                var v = new Vector2(vehicle.Velocity.x, vehicle.Velocity.z);
                if (v.sqrMagnitude < 9f) continue;
                var p = new Vector2(vehicle.Position.x, vehicle.Position.z);
                if ((p - ped.Pos).sqrMagnitude > ThreatRange * ThreatRange) continue;
                if (Predict(ped, p, v, 1.6f, out float tti))
                {
                    Threat(ped, p, v, tti);
                    return;
                }
            }
            if (playerOnFoot && playerVel.sqrMagnitude > 20f && Predict(ped, playerPos, playerVel, 0.7f, out float ttp) && ttp < 0.6f)
            {
                ped.ThreatFrom = playerPos;
                Sidestep(ped, playerPos, playerVel);
            }
        }

        static bool Predict(Pedestrian ped, Vector2 p, Vector2 v, float halfWidth, out float tti)
        {
            var rel = ped.Pos - p;
            float v2 = v.sqrMagnitude;
            tti = Vector2.Dot(rel, v) / v2;
            if (tti < 0f || tti > WarnSeconds) return false;
            float lateral = Mathf.Abs(rel.x * v.y - rel.y * v.x) / Mathf.Sqrt(v2);
            return lateral < halfWidth + ped.Radius;
        }

        void Sidestep(Pedestrian ped, Vector2 from, Vector2 v)
        {
            if (ped.State == PedState.Startled || ped.State == PedState.Sit) return;
            var side = Side(ped, from, v);
            ped.Vel = side * 2.2f;
            Move(ped, side * 0.25f);
            ped.Lean = -10f;
            Startle(ped, 0.7f);
        }

        Vector2 Side(Pedestrian ped, Vector2 from, Vector2 v)
        {
            var n = new Vector2(-v.y, v.x).normalized;
            if (Vector2.Dot(ped.Pos - from, n) < 0f) n = -n;
            if (Graph.InBuilding(ped.Pos + n * 1.5f, 0.2f)) n = -n;
            return n;
        }

        internal void Threat(Pedestrian ped, Vector2 from, Vector2 v, float tti)
        {
            if (ped.State >= PedState.Dive && ped.State != PedState.Startled && ped.State != PedState.Dazed) return;
            if (ped.State == PedState.Startled && ped.StateTime < 0.3f) return;
            ped.ThreatFrom = from;
            if (ped.State == PedState.Sit) ped.Pos += Graph.Spots[ped.Spot >= 0 ? ped.Spot : 0].Facing * 0.5f;
            LeaveSpotState(ped);
            ped.FallDir = Side(ped, from, v);
            Stand(ped);
            ped.Phase = 0;
            ped.Pitch = 0f;
            Pose(ped, Clip.Brace, 0.12f);
            Enter(ped, PedState.Dive, 0f);
            GameEvents.RaisePedestrianDive(new Vector3(ped.Pos.x, 0f, ped.Pos.y), ped.Id);
        }

        void LeaveSpotState(Pedestrian ped)
        {
            if (ped.Crossing >= 0 && ped.State == PedState.WaitCurb) ped.Crossing = -1;
            if (ped.Crossing < 0) Unregister(ped);
            if (ped.State == PedState.Sit || ped.State == PedState.BusWait) ReleaseSpot(ped);
        }

        // Switches to the posed body, starting from however the person stands now.
        static void Stand(Pedestrian ped)
        {
            if (ped.Posed) return;
            ped.Posed = true;
            ped.Orient = Quaternion.Euler(0f, ped.Heading + (ped.Role == PedRole.Skater ? 90f : 0f), 0f) * Quaternion.AngleAxis(ped.Lean, Vector3.right);
            ped.Height = ped.Model.StandHeight;
            ped.Climb = 0f;
            ped.Spin = Vector3.zero;
            ped.Lean = 0f;
        }

        static float Clearance(Pedestrian ped)
        {
            float now = ped.Model.Clearance(ped.Orient, ped.Clip);
            if (ped.Fade >= 1f) return now;
            return Mathf.Lerp(ped.Model.Clearance(ped.Orient, ped.PrevClip), now, ped.Fade);
        }

        static Quaternion Upright(float heading) => Quaternion.Euler(0f, heading, 0f);

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        // A crouch and a flinch, a leap away from the car with arms out front, a forward roll and
        // up again, then a startled look back at the car.
        void Dive(Pedestrian ped, float dt)
        {
            float target = Mathf.Atan2(ped.FallDir.x, ped.FallDir.y) * Mathf.Rad2Deg;
            float t = ped.StateTime - ped.Mark;
            switch (ped.Phase)
            {
                case 0:
                    ped.Vel = Vector2.zero;
                    ped.Heading = Mathf.MoveTowardsAngle(ped.Heading, target, 900f * dt);
                    ped.Orient = Quaternion.Slerp(ped.Orient, Upright(ped.Heading), 1f - Mathf.Exp(-25f * dt));
                    ped.Height = Mathf.MoveTowards(ped.Height, Clearance(ped), 3f * dt);
                    if (t < 0.16f) return;
                    ped.Heading = target;
                    ped.Vel = ped.FallDir * 6f;
                    ped.Climb = 3.2f;
                    Pose(ped, Clip.Leap, 0.1f);
                    NextPhase(ped);
                    return;
                case 1:
                    Move(ped, ped.Vel * dt);
                    ped.Climb -= Gravity * dt;
                    ped.Height += ped.Climb * dt;
                    ped.Pitch = Mathf.MoveTowards(ped.Pitch, 82f, 420f * dt);
                    ped.Orient = Upright(ped.Heading) * Quaternion.AngleAxis(ped.Pitch, Vector3.right);
                    float floor = Clearance(ped);
                    if (ped.Height > floor || ped.Climb > 0f) return;
                    ped.Height = floor;
                    ped.Climb = 0f;
                    Pose(ped, Clip.Tuck, 0.1f);
                    NextPhase(ped);
                    return;
                case 2:
                    ped.Vel = Vector2.MoveTowards(ped.Vel, ped.FallDir * 1.6f, 6f * dt);
                    Move(ped, ped.Vel * dt);
                    ped.Pitch += 600f * dt;
                    if (ped.Pitch >= 290f && ped.Clip == Clip.Tuck) Pose(ped, Clip.Brace, 0.15f);
                    if (ped.Pitch >= 360f)
                    {
                        ped.Pitch = 0f;
                        Pose(ped, Clip.Idle, 0.4f);
                        NextPhase(ped);
                    }
                    ped.Orient = Upright(ped.Heading) * Quaternion.AngleAxis(ped.Pitch, Vector3.right);
                    ped.Height = Clearance(ped);
                    return;
                default:
                    ped.Vel = Vector2.MoveTowards(ped.Vel, Vector2.zero, 6f * dt);
                    Move(ped, ped.Vel * dt);
                    ped.Orient = Upright(ped.Heading);
                    ped.Height = Clearance(ped) + Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI) * 0.04f;
                    if (t >= 0.4f) Startle(ped, 0.8f);
                    return;
            }
        }

        static void NextPhase(Pedestrian ped)
        {
            ped.Phase++;
            ped.Mark = ped.StateTime;
        }

        void Startle(Pedestrian ped, float length)
        {
            ped.Posed = false;
            Enter(ped, PedState.Startled, length);
        }

        // A hit scaled to its speed: a light bump stumbles, a firmer one knocks the person off their
        // feet, and a fast one launches them into an arc with spin that bounces, rolls in a ball and
        // settles. At most MaxLaunched people are in the air at once; extra hits knock down instead.
        // from is where the hit came from (the vehicle's centre); people are thrown forward and off
        // to the side of its line, so the car doesn't run straight on over them.
        internal void Hit(Pedestrian ped, Vector2 impulse, Vector2 from)
        {
            if (ped.State == PedState.Tumble) return;
            float speed = impulse.magnitude;
            bool down = ped.State == PedState.Down || ped.State == PedState.GetUp;
            if (speed < 1f || (down && speed < 6f)) return;
            LeaveSpotState(ped);
            ped.Crossing = -1;
            Unregister(ped);
            var dir = impulse / speed;
            var aside = new Vector2(-dir.y, dir.x);
            float offset = Vector2.Dot(ped.Pos - from, aside);
            if (offset < 0f || (Mathf.Abs(offset) < 0.05f && rng.NextDouble() < 0.5)) aside = -aside;
            if (speed < StumbleBelow)
            {
                StartStumble(ped, dir * (1f + speed * 0.4f) + aside * 1.8f);
                return;
            }
            Stand(ped);
            ped.Impact = speed;
            ped.FallDir = dir;
            ped.Launched = speed >= LaunchAbove && Airborne() < MaxLaunched;
            float flips, twist;
            if (ped.Launched)
            {
                ped.Vel = (dir + aside * 0.3f) * Mathf.Clamp(0.55f * speed, 4f, 13f);
                ped.Climb = Mathf.Clamp(1.4f + 0.27f * speed, 3.5f, 8.5f);
                flips = Range(300f, 480f);
                twist = Range(90f, 220f);
            }
            else
            {
                ped.Vel = (dir + aside * 0.45f) * Mathf.Clamp(0.5f * speed, 2f, 5f);
                ped.Climb = Mathf.Clamp(0.8f + 0.25f * speed, 2f, 3.4f);
                flips = Range(100f, 130f);
                twist = Range(20f, 70f);
            }
            float flight = 2f * ped.Climb / Gravity;
            var d3 = new Vector3(dir.x, 0f, dir.y);
            ped.Spin = Vector3.Cross(d3, Vector3.up) * Mathf.Min(flips / flight, 1000f)
                + Vector3.up * twist * (rng.NextDouble() < 0.5 ? -1f : 1f)
                + d3 * Range(-50f, 50f);
            Pose(ped, Clip.Flail, 0.1f);
            Enter(ped, PedState.Tumble, 0f);
            if (speed >= LaunchAbove) GameEvents.RaisePedestrianDive(new Vector3(ped.Pos.x, 0f, ped.Pos.y), ped.Id);
        }

        int Airborne()
        {
            int n = 0;
            foreach (var p in active) if (p.State == PedState.Tumble && p.Launched) n++;
            return n;
        }

        internal void Shove(Pedestrian ped, Vector2 impulse, GameObject by)
        {
            if (!ped.CanBeShoved) return;
            if (by != null) ped.ThreatFrom = new Vector2(by.transform.position.x, by.transform.position.z);
            GameEvents.RaiseShove(new Vector3(ped.Pos.x, 0f, ped.Pos.y));
            if (impulse.magnitude > 3.5f)
            {
                Hit(ped, impulse * 1.3f, ped.Pos - impulse.normalized);
                return;
            }
            LeaveSpotState(ped);
            StartStumble(ped, Vector2.ClampMagnitude(impulse, 3f));
        }

        // Knocked back a few steps: legs scrambling, arms half up and windmilling, the body tipping
        // with the push and swaying back upright.
        void StartStumble(Pedestrian ped, Vector2 push)
        {
            Stand(ped);
            ped.Slide = Vector2.ClampMagnitude(push, 3.2f);
            ped.FallDir = ped.Slide.sqrMagnitude > 0.01f ? ped.Slide.normalized : Forward(ped);
            ped.Mark = ped.Heading + (ped.Role == PedRole.Skater ? 90f : 0f);
            Pose(ped, Clip.Run, 0.08f);
            Pose(ped, Clip.Flail, 0.12f, 0.5f);
            Enter(ped, PedState.Stumble, 0.7f + 0.12f * ped.Slide.magnitude);
        }

        void Stumble(Pedestrian ped, float dt)
        {
            Move(ped, ped.Slide * dt);
            ped.Slide = Vector2.MoveTowards(ped.Slide, Vector2.zero, 4.5f * dt);
            ped.Vel = ped.Slide;
            float t = ped.StateTime;
            float tip = 26f * Mathf.Exp(-3.5f * t) * Mathf.Sin(10f * t + 0.25f);
            var axis = Vector3.Cross(Vector3.up, new Vector3(ped.FallDir.x, 0f, ped.FallDir.y));
            ped.Orient = Quaternion.AngleAxis(tip, axis) * Upright(ped.Mark);
            ped.Height = ped.Model.StandHeight - Mathf.Abs(tip) * 0.003f;
            if (t >= ped.StateLength - 0.2f && ped.Clip != Clip.Idle) Pose(ped, Clip.Idle, 0.2f);
            if (t < ped.StateLength) return;
            ped.Heading = ped.Mark - (ped.Role == PedRole.Skater ? 90f : 0f);
            Startle(ped, 0.5f);
        }

        // Ballistic flight with spin; on touching down it bounces, then rolls in a tuck while it has
        // speed, and opens out to settle once it slows.
        void Tumble(Pedestrian ped, float dt)
        {
            if (!Move(ped, ped.Vel * dt)) ped.Vel *= -0.3f;
            ped.Climb -= Gravity * dt;
            ped.Height += ped.Climb * dt;
            float spin = ped.Spin.magnitude;
            if (spin > 1f) ped.Orient = Quaternion.AngleAxis(spin * dt, ped.Spin / spin) * ped.Orient;
            float floor = Clearance(ped);
            if (ped.Height > floor) return;
            ped.Height = floor;
            float speed = ped.Vel.magnitude;
            if (ped.Climb < -3f)
            {
                ped.Climb = -ped.Climb * 0.3f;
                ped.Vel *= 0.7f;
                ped.Spin *= 0.6f;
                if (speed > RollAbove) Pose(ped, Clip.Tuck, 0.12f);
                return;
            }
            ped.Climb = 0f;
            if (speed > RollAbove)
            {
                var d = ped.Vel / speed;
                var roll = Vector3.Cross(Vector3.up, new Vector3(d.x, 0f, d.y)) * (speed / 0.42f * Mathf.Rad2Deg);
                ped.Spin = Vector3.MoveTowards(ped.Spin, roll, 2500f * dt);
                ped.Vel = Vector2.MoveTowards(ped.Vel, Vector2.zero, 5.5f * dt);
                if (ped.Clip != Clip.Tuck) Pose(ped, Clip.Tuck, 0.15f);
                return;
            }
            ped.LieOrient = LieFlat(ped.Orient, ped.FallDir);
            Pose(ped, Clip.Sprawl, 0.3f);
            Enter(ped, PedState.Down, ped.Launched ? Range(1.0f, 1.5f) : Range(0.6f, 0.9f));
        }

        // The nearest way to lie flat: face down or up, whichever the body is closer to, with the
        // head toward wherever it points now.
        static Quaternion LieFlat(Quaternion orient, Vector2 fallback)
        {
            var up = orient * Vector3.up;
            var head = new Vector3(up.x, 0f, up.z);
            if (head.sqrMagnitude < 0.04f) head = new Vector3(fallback.x, 0f, fallback.y) * (up.y > 0f ? 1f : -1f);
            if (head.sqrMagnitude < 1e-4f) head = Vector3.forward;
            bool faceDown = (orient * Vector3.forward).y < 0f;
            return Quaternion.LookRotation(faceDown ? Vector3.down : Vector3.up, head.normalized);
        }

        void Down(Pedestrian ped, float dt)
        {
            ped.Vel = Vector2.MoveTowards(ped.Vel, Vector2.zero, 9f * dt);
            Move(ped, ped.Vel * dt);
            ped.Orient = Quaternion.Slerp(ped.Orient, ped.LieOrient, 1f - Mathf.Exp(-9f * dt));
            ped.Height = Mathf.Max(Clearance(ped), ped.Height - 3f * dt);
            if (ped.StateTime < ped.StateLength) return;
            ped.Vel = Vector2.zero;
            ped.LieOrient = ped.Orient;
            var head = ped.Orient * Vector3.up;
            bool faceDown = (ped.Orient * Vector3.forward).y < 0f;
            var facing = faceDown ? head : -head;
            ped.Heading = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            Pose(ped, Clip.Kneel, 0.45f);
            Enter(ped, PedState.GetUp, 1.05f);
        }

        // Rolls or pushes up onto one knee, then stands, with a little bob at the top.
        void GetUp(Pedestrian ped)
        {
            float u = ped.StateTime / ped.StateLength;
            if (u < 0.5f)
            {
                ped.Orient = Quaternion.Slerp(ped.LieOrient, Upright(ped.Heading), Smooth(u / 0.5f));
                ped.Height = Clearance(ped);
                return;
            }
            if (ped.Clip == Clip.Kneel) Pose(ped, Clip.Idle, 0.4f);
            ped.Orient = Upright(ped.Heading);
            ped.Height = Clearance(ped) + Mathf.Sin(Mathf.Clamp01((u - 0.5f) / 0.5f) * Mathf.PI) * 0.05f;
            if (u >= 1f) Enter(ped, PedState.Dazed, Range(1.1f, 1.6f));
        }

        // Back on their feet but woozy: a fading sway and a shake of the head, then on their way.
        void Dazed(Pedestrian ped)
        {
            float t = ped.StateTime;
            float a = 9f * (1f - Smooth(t / ped.StateLength));
            ped.Orient = Upright(ped.Heading)
                * Quaternion.AngleAxis(a * Mathf.Sin(t * 6.5f), Vector3.forward)
                * Quaternion.AngleAxis(a * 0.6f * Mathf.Sin(t * 4.3f + 1f), Vector3.right);
            ped.Height = ped.Model.StandHeight;
            ped.Vel = Vector2.zero;
            if (t < ped.StateLength) return;
            ped.Posed = false;
            ped.Hurry = 3f;
            Enter(ped, PedState.Walk, 0f);
            if (ped.Path.Count == 0 || ped.PathIndex >= ped.Path.Count) NewTrip(ped);
            else Resume(ped);
        }

        internal void Contact(Pedestrian ped, Collider other)
        {
            if (other.GetComponentInParent<Pedestrian>() != null) return;
            var car = other.GetComponentInParent<CarController>();
            if (car != null)
            {
                if (Mathf.Abs(car.Speed) > 1.5f)
                {
                    var v = car.transform.forward * car.Speed;
                    Hit(ped, new Vector2(v.x, v.z) - ped.Vel, new Vector2(car.transform.position.x, car.transform.position.z));
                    if (Busy) Bump(car);
                }
                return;
            }
            var traffic = other.GetComponentInParent<ITrafficParticipant>();
            if (traffic != null)
            {
                var v = new Vector2(traffic.Velocity.x, traffic.Velocity.z);
                if (v.sqrMagnitude > 2.25f) Hit(ped, v - ped.Vel, new Vector2(traffic.Position.x, traffic.Position.z));
                return;
            }
            if (other.GetComponentInParent<PlayerController>() == null || !CanStumble(ped)) return;
            ped.ThreatFrom = playerPos;
            if (playerVel.magnitude > 4.5f)
            {
                GameEvents.RaiseShove(new Vector3(ped.Pos.x, 0f, ped.Pos.y));
                Hit(ped, playerVel * 1.1f, playerPos);
            }
            else Shove(ped, (ped.Pos - playerPos).normalized * 1.6f + playerVel * 0.4f, other.gameObject);
        }

        static bool CanStumble(Pedestrian ped) => ped.State != PedState.Stumble && ped.CanBeShoved;

        // While an Operator crowd effect is on, running into people costs a quarter of the car's speed
        // and a short stun, so crowds become hazards to steer around.
        readonly Dictionary<CarController, float> bumped = new Dictionary<CarController, float>();

        void Bump(CarController car)
        {
            if (!car.Driven || car.Autopilot || !car.TryGetComponent<Rigidbody>(out var body) || body.isKinematic) return;
            if (bumped.TryGetValue(car, out var until) && Time.time < until) return;
            bumped[car] = Time.time + 0.6f;
            var v = body.linearVelocity;
            body.linearVelocity = new Vector3(v.x * 0.75f, v.y, v.z * 0.75f);
            car.Stun(0.3f);
        }

        // --- drawing -----------------------------------------------------------------------------

        void Render()
        {
            Drawn = 0;
            var cam2 = new Vector2(cameraPos.x, cameraPos.z);
            foreach (var ped in active)
            {
                var pos = ped.Pos + ped.Vel * ped.Pending;
                float d = Vector2.Distance(pos, cam2);
                if (!InView(pos, d)) continue;
                Drawn++;
                bool far = d > 45f;
                bool shadows = d < 40f;

                float heading = ped.Heading + (ped.Role == PedRole.Skater ? 90f : 0f);
                var rot = Quaternion.Euler(0f, heading, 0f);
                var ground = new Vector3(pos.x, 0.03f, pos.y);
                if (ped.Role == PedRole.Skater) ground.y += 0.12f;
                var model = ped.Model;
                Matrix4x4 matrix;
                if (ped.Posed)
                {
                    var hip = new Vector3(pos.x, 0.03f + ped.Height - model.StandHeight + model.HipHeight, pos.y);
                    matrix = Matrix4x4.TRS(hip, ped.Orient, Vector3.one) * Matrix4x4.Translate(Vector3.down * model.HipHeight);
                }
                else if (ped.Lean != 0f)
                {
                    var tilt = Quaternion.AngleAxis(ped.Lean, rot * Vector3.right) * rot;
                    matrix = Matrix4x4.TRS(ground + Vector3.up * model.HipHeight, tilt, Vector3.one) * Matrix4x4.Translate(Vector3.down * model.HipHeight);
                }
                else
                {
                    matrix = Matrix4x4.TRS(ground, rot, Vector3.one);
                }

                float time = far ? Mathf.Floor(ped.ClipTime * 15f) / 15f : ped.ClipTime;
                int current = model.Frame(ped.Clip, time);
                int previous = far ? current : model.Frame(ped.PrevClip, ped.PrevClipTime);
                var anim = new Vector4(previous, current, far ? 1f : ped.Fade, ped.HeadYaw * Mathf.Deg2Rad);
                crowd.Add(model, matrix, anim, ped.Outfit, shadows);

                var travel = Quaternion.Euler(0f, ped.Heading, 0f);
                if (ped.Role == PedRole.Skater && skateboard != null && !ped.Posed)
                    crowd.Add(skateboard, Matrix4x4.TRS(new Vector3(pos.x, 0.03f, pos.y), travel, Vector3.one), Vector4.zero, ped.Outfit, shadows);
                else if (ped.Role == PedRole.Cart && cart != null)
                    crowd.Add(cart, Matrix4x4.TRS(new Vector3(pos.x, 0.03f, pos.y) + travel * Vector3.forward * 0.95f, travel, Vector3.one), Vector4.zero, ped.Outfit, shadows);
            }
            crowd.Submit();
            DrawCalls = crowd.DrawCalls;
        }
    }
}
