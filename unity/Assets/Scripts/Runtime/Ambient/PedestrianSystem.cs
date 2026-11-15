using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public sealed class PedestrianSystem : MonoBehaviour
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
            ped.Radius = role == PedRole.Walker ? 0.3f : 0.5f;
            ped.Lateral = Range(0.35f, 0.9f);
            ped.Pos = pos;
            ped.Vel = Vector2.zero;
            ped.State = PedState.Walk;
            ped.StateTime = 0f;
            ped.Spot = -1;
            ped.Crossing = -1;
            ped.Tip = ped.Roll = ped.Lean = ped.Lift = ped.Hurry = 0f;
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
                case PedState.Stumble:
                    Slide(ped, dt, 5f);
                    ped.Tip = Mathf.MoveTowards(ped.Tip, ped.StateTime < 0.25f ? 18f : 0f, 90f * dt);
                    if (ped.StateTime >= ped.StateLength) Startle(ped, 0.6f);
                    break;
                case PedState.Tumble: Tumble(ped, dt); break;
                case PedState.Down:
                    Slide(ped, dt, 8f);
                    if (ped.StateTime >= ped.StateLength) Enter(ped, PedState.GetUp, 0.8f);
                    break;
                case PedState.GetUp:
                    float u = Mathf.Clamp01(ped.StateTime / ped.StateLength);
                    ped.Tip = Mathf.Lerp(90f, 0f, u * u * (3f - 2f * u));
                    ped.Lift = 0f;
                    if (u >= 1f) Startle(ped, 1.0f);
                    break;
            }

            Head(ped, dt, now);
            Animate(ped, dt);
            ped.transform.position = new Vector3(ped.Pos.x, 0.03f, ped.Pos.y);
        }

        void Walk(Pedestrian ped, float dt, float now, bool near)
        {
            if (ped.PathIndex >= ped.Path.Count)
            {
                Arrive(ped);
                return;
            }
            int node = ped.Path[ped.PathIndex];
            var target = PathPoint(ped, ped.PathIndex);
            float speed = ped.Cruise * (ped.Hurry > 0f ? 1.3f : 1f) * PlayerController.RunScale;
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