using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.World
{
    // Real San Francisco beyond the playable map: DataSF footprints at their real heights, OSM streets,
    // water and parks (tools/DataPrep --city), streamed in after the world is ready so nothing new
    // loads before the first frame. Scenery only (no colliders, no traffic, behind the map-edge walls).
    // Each 400 m chunk has a near mesh and a cheap far silhouette (tall buildings and water only),
    // swapped by distance from the camera. Past the outskirts' land the ground is laid in 50 m cells
    // under the buildings and streets. Towers of SkylineHeight and up also go into one skyline mesh
    // that every perspective camera draws pulled in along its own sight lines (same direction, nearer)
    // so the skyline shows from Dogpatch inside the far clip plane, lightly fogged.
    public sealed class CityRegions : MonoBehaviour
    {
        // The world-bounds setting: regions stream in, in this order, up to and including the one named
        // here; "dogpatch" switches the expansion off. "drivable" loads Dogpatch, Mission Bay, South Beach
        // and the SoMa annex as one playable world (World/dogpatch-drivable, extracted with DataPrep
        // --north 37.7880 --soma) and streams the regions beyond it as scenery. The command line
        // -world-bounds <name> overrides it.
        public const string WorldBounds = "drivable";

        public static string Bounds
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-world-bounds");
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : WorldBounds;
            }
        }

        public static bool Drivable => Bounds == "drivable";

        // The world data every system loads (GameController, TrafficData).
        public static string WorldResource => Drivable ? "World/dogpatch-drivable" : "World/dogpatch";

        static readonly HashSet<string> DrivableRegions = new HashSet<string> { "missionbay", "southbeach" };

        // The north edge of the original Dogpatch map. Beyond it (the drivable expansion) only one parked
        // car in three is kept, so the expansion doesn't double the parked fleet.
        public const float DogpatchNorth = 834f;

        // The playable world's outline when it isn't the box (the SoMa annex, DataPrep --soma), set by
        // WorldBuilder from WorldData.edge.
        public static Vector2[] Edge;

        public static bool InEdge(Vector2 p) => Edge != null && Geometry.Contains(Edge, p);

        // The part of the playable world west of the box (the SoMa annex), or an empty rect.
        public static Rect Annex(Rect map)
        {
            if (Edge == null) return Rect.zero;
            float x0 = map.xMin, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in Edge)
            {
                if (p.x >= map.xMin - 0.5f) continue;
                x0 = Mathf.Min(x0, p.x);
                z0 = Mathf.Min(z0, p.y);
                z1 = Mathf.Max(z1, p.y);
            }
            return x0 < map.xMin ? Rect.MinMaxRect(x0, z0, map.xMin, z1) : Rect.zero;
        }

        static readonly (string name, Rect rect)[] Regions =
        {
            ("missionbay", Rect.MinMaxRect(-1120.9f, 834f, 725.3f, 2446.3f)),
            ("southbeach", Rect.MinMaxRect(-1120.9f, 2446.3f, 725.3f, 3447f)),
            ("soma", Rect.MinMaxRect(-2175.8f, 3447f, 22f, 4670.2f)),
        };

        const float Chunk = 400f;
        const float NearRange = 700f;
        const float FarRange = 2000f;
        const float FarHeight = 18f;
        const float SkylineHeight = 60f;
        const float SkylineDistance = 2800f;
        const float GroundCell = 50f;

        internal static readonly Color[] Walls = { L("#FFF3DA"), L("#DCD6CC"), L("#9ED7FF"), L("#FFE27A"), L("#8CE3BF"), L("#FF9C86"), L("#BBA6F7"), L("#C9C6C0") };
        static readonly Color[] TowerWalls = { L("#FFF3DA"), L("#9ED7FF"), L("#DCD6CC"), L("#C94A32") };
        static readonly Color Roof = L("#6C758B");
        static Color L(string hex) => SpeedDial.Hex(hex).linear;

        static CityRegions instance;
        readonly List<(Renderer near, Renderer far)> chunks = new List<(Renderer, Renderer)>();
        Renderer skyline;
        Bounds skylineBounds;
        float next;

        public static List<(string name, Rect rect)> Active()
        {
            string bounds = Bounds;
            var active = new List<(string, Rect)>();
            if (bounds == "dogpatch") return active;
            foreach (var region in Regions)
            {
                if (!(Drivable && DrivableRegions.Contains(region.name))) active.Add(region);
                if (region.name == bounds) break;
            }
            return active;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            GameController.WorldReady -= Begin;
            GameController.WorldReady += Begin;
        }

        static void Begin(GameController game)
        {
            if (instance != null || Active().Count == 0) return;
            instance = new GameObject("City regions").AddComponent<CityRegions>();
            instance.StartCoroutine(instance.Stream(game.SharedMaterial, Outskirts.LandTop(game.World.Bounds)));
            HitchLogger.StreamingChunks++;
        }

        [Serializable] class Shape { public float h; public float w; public string kind; public float[] pts; }
        [Serializable] class Region { public string name; public Shape[] buildings; public Shape[] roads; public Shape[] water; public Shape[] parks; }

        IEnumerator Stream(Material material, float landTop)
        {
            var towers = new MeshBuilder();
            yield return null;
            yield return null;
            foreach (var (name, _) in Active())
            {
                float t0 = Time.realtimeSinceStartup;
                var request = Resources.LoadAsync<TextAsset>("World/city-" + name);
                yield return request;
                if (!(request.asset is TextAsset asset))
                {
                    Debug.LogWarning($"[City] {name}: World/city-{name} missing");
                    continue;
                }
                string text = asset.text;
                var bake = Task.Run(() => Bake(JsonUtility.FromJson<Region>(text), landTop, towers));
                while (!bake.IsCompleted) yield return null;
                if (bake.IsFaulted)
                {
                    Debug.LogException(bake.Exception);
                    continue;
                }
                var (near, far, count) = bake.Result;
                var eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var keys = new List<Vector2Int>();
                foreach (var (key, _) in near.All()) keys.Add(key);
                keys.Sort((a, b) => (Center(a) - new Vector2(eye.x, eye.z)).sqrMagnitude.CompareTo((Center(b) - new Vector2(eye.x, eye.z)).sqrMagnitude));
                int tris = 0;
                foreach (var key in keys)
                {
                    var nearBuilder = near.Get(key);
                    var farBuilder = far.Get(key);
                    tris += nearBuilder.IndexCount / 3;
                    var nearRenderer = WorldBuilder.Emit(transform, $"City {name} {key}", nearBuilder.ToMesh($"city {name} {key}"), material, false).GetComponent<Renderer>();
                    Renderer farRenderer = null;
                    if (farBuilder != null && farBuilder.IndexCount > 0)
                        farRenderer = WorldBuilder.Emit(transform, $"City {name} {key} far", farBuilder.ToMesh($"city {name} {key} far"), material, false).GetComponent<Renderer>();
                    chunks.Add((nearRenderer, farRenderer));
                    Apply(nearRenderer, farRenderer, eye);
                    yield return null;
                }
                Debug.Log($"[City] {name}: {count.buildings} buildings, {count.roads} streets, {keys.Count} chunks, {tris} tris, streamed in {Time.realtimeSinceStartup - t0:F2}s");
            }
            HitchLogger.StreamingChunks--;
            if (towers.IndexCount == 0) yield break;
            var hazy = new Material(material) { name = "City skyline" };
            hazy.SetFloat("_FogAmount", 0.35f);
            skyline = WorldBuilder.Emit(transform, "City skyline", towers.ToMesh("city skyline"), hazy, false).GetComponent<Renderer>();
            skylineBounds = skyline.bounds;
            Camera.onPreCull += PullSkyline;
            Debug.Log($"[City] skyline: {towers.IndexCount / 3} tris");
        }

        // Scales the skyline about the camera so its farthest point sits at SkylineDistance: every
        // vertex keeps its direction from the eye, so it looks the same, only nearer. Hidden once the
        // camera is close enough to see the real towers, and from top-down orthographic views.
        void PullSkyline(Camera cam)
        {
            if (skyline == null) return;
            var eye = cam.transform.position;
            var c = skylineBounds.center;
            var e = skylineBounds.extents;
            float farthest = Vector3.Distance(eye, new Vector3(eye.x < c.x ? c.x + e.x : c.x - e.x, eye.y < c.y ? c.y + e.y : c.y - e.y, eye.z < c.z ? c.z + e.z : c.z - e.z));
            float scale = SkylineDistance / farthest;
            skyline.enabled = !cam.orthographic && scale < 0.98f;
            if (!skyline.enabled) return;
            skyline.transform.localScale = Vector3.one * scale;
            skyline.transform.position = eye * (1f - scale);
        }

        static Vector2 Center(Vector2Int key) => (new Vector2(key.x, key.y) + Vector2.one * 0.5f) * Chunk;

        static (ChunkedMeshBuilder near, ChunkedMeshBuilder far, (int buildings, int roads) count) Bake(Region region, float landTop, MeshBuilder towers)
        {
            var near = new ChunkedMeshBuilder(Chunk);
            var far = new ChunkedMeshBuilder(Chunk);
            var cells = new HashSet<Vector2Int>();
            void Mark(float[] flat)
            {
                for (int i = 0; i + 1 < flat.Length; i += 2)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            cells.Add(new Vector2Int(Mathf.FloorToInt(flat[i] / GroundCell) + dx, Mathf.FloorToInt(flat[i + 1] / GroundCell) + dz));
            }
            foreach (var building in region.buildings) Mark(building.pts);
            foreach (var road in region.roads) Mark(road.pts);
            foreach (var cell in cells)
            {
                if (cell.y * GroundCell < landTop) continue;
                var a = new Vector2(cell.x, cell.y) * GroundCell;
                var b = a + new Vector2(GroundCell, GroundCell);
                var mid = (a + b) * 0.5f;
                near.At(mid).Quad(new Vector3(a.x, 0f, a.y), new Vector3(a.x, 0f, b.y), new Vector3(b.x, 0f, b.y), new Vector3(b.x, 0f, a.y), SpeedDial.Land, Vector3.up);
                far.At(mid).Quad(new Vector3(a.x, 0f, a.y), new Vector3(a.x, 0f, b.y), new Vector3(b.x, 0f, b.y), new Vector3(b.x, 0f, a.y), SpeedDial.Land, Vector3.up);
            }
            foreach (var water in region.water)
            {
                var pts = Geometry.ToPoints(water.pts);
                var c = Centroid(pts);
                near.At(c).Polygon(pts, 0.1f, SpeedDial.Bay);
                far.At(c).Polygon(pts, 0.1f, SpeedDial.Bay);
            }
            foreach (var park in region.parks)
            {
                var pts = Geometry.ToPoints(park.pts);
                near.At(Centroid(pts)).Polygon(pts, 0.15f, SpeedDial.Park);
            }
            foreach (var road in region.roads)
            {
                var pts = Geometry.ToPoints(road.pts);
                bool foot = road.kind == "path" || road.kind == "cycleway" || road.kind == "pedestrian";
                near.At(pts[pts.Length / 2]).Ribbon(pts, road.w * 0.5f, foot ? 0.2f : road.h > 0f ? 0.3f : 0.25f, foot ? SpeedDial.Sidewalk : SpeedDial.Asphalt, true);
            }
            var random = new System.Random(415);
            foreach (var building in region.buildings)
            {
                var pts = Geometry.ToPoints(building.pts);
                var c = Centroid(pts);
                bool tower = building.h >= 30f;
                var wall = tower ? TowerWalls[random.Next(TowerWalls.Length)] : Walls[random.Next(Walls.Length)];
                Extrude(near.At(c), pts, building.h, wall);
                if (building.h >= FarHeight) Extrude(far.At(c), pts, building.h, wall);
                if (building.h >= SkylineHeight) lock (towers) Extrude(towers, pts, building.h, Color.Lerp(wall, SpeedDial.SkyHorizon, 0.3f));
            }
            return (near, far, (region.buildings.Length, region.roads.Length));
        }

        internal static void Extrude(MeshBuilder mesh, Vector2[] pts, float height, Color wall)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                var edge = b - a;
                var outward = new Vector3(edge.y, 0f, -edge.x);
                mesh.Quad(MeshBuilder.At(a, 0f), MeshBuilder.At(b, 0f), MeshBuilder.At(b, height), MeshBuilder.At(a, height), wall, outward);
            }
            mesh.Polygon(pts, height, Roof);
        }

        static Vector2 Centroid(Vector2[] pts)
        {
            var sum = Vector2.zero;
            foreach (var p in pts) sum += p;
            return sum / pts.Length;
        }

        static void Apply(Renderer near, Renderer far, Vector3 eye)
        {
            float d = Mathf.Sqrt(near.bounds.SqrDistance(eye));
            near.enabled = d < NearRange;
            if (far != null) far.enabled = d >= NearRange && d < FarRange;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;
            foreach (var (near, far) in chunks) Apply(near, far, eye);
        }

        void OnDestroy()
        {
            Camera.onPreCull -= PullSkyline;
            if (instance == this) instance = null;
        }
    }
}
