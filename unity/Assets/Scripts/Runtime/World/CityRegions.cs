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
    // swapped by distance from the camera.
    public sealed class CityRegions : MonoBehaviour
    {
        // The world-bounds setting: regions stream in, in this order, up to and including the one named
        // here; "dogpatch" switches the expansion off. The command line -world-bounds <name> overrides it.
        public const string WorldBounds = "southbeach";

        static readonly (string name, Rect rect)[] Regions =
        {
            ("missionbay", Rect.MinMaxRect(-1120.9f, 834f, 725.3f, 2446.3f)),
            ("southbeach", Rect.MinMaxRect(-1120.9f, 2446.3f, 725.3f, 3447f)),
        };

        const float Chunk = 400f;
        const float NearRange = 700f;
        const float FarRange = 6000f;
        const float FarHeight = 18f;

        static readonly Color[] Walls = { L("#FFF3DA"), L("#DCD6CC"), L("#9ED7FF"), L("#FFE27A"), L("#8CE3BF"), L("#FF9C86"), L("#BBA6F7"), L("#C9C6C0") };
        static readonly Color[] TowerWalls = { L("#FFF3DA"), L("#9ED7FF"), L("#DCD6CC"), L("#C94A32") };
        static readonly Color Roof = L("#6C758B");
        static Color L(string hex) => SpeedDial.Hex(hex).linear;

        static CityRegions instance;
        readonly List<(Renderer near, Renderer far)> chunks = new List<(Renderer, Renderer)>();
        float next;

        public static List<(string name, Rect rect)> Active()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-world-bounds");
            string bounds = i >= 0 && i + 1 < args.Length ? args[i + 1] : WorldBounds;
            var active = new List<(string, Rect)>();
            if (bounds == "dogpatch") return active;
            foreach (var region in Regions)
            {
                active.Add(region);
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
            instance.StartCoroutine(instance.Stream(game.SharedMaterial));
        }

        [Serializable] class Shape { public float h; public float w; public string kind; public float[] pts; }
        [Serializable] class Region { public string name; public Shape[] buildings; public Shape[] roads; public Shape[] water; public Shape[] parks; }

        IEnumerator Stream(Material material)
        {
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
                var bake = Task.Run(() => Bake(JsonUtility.FromJson<Region>(text)));
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
        }

        static Vector2 Center(Vector2Int key) => (new Vector2(key.x, key.y) + Vector2.one * 0.5f) * Chunk;

        static (ChunkedMeshBuilder near, ChunkedMeshBuilder far, (int buildings, int roads) count) Bake(Region region)
        {
            var near = new ChunkedMeshBuilder(Chunk);
            var far = new ChunkedMeshBuilder(Chunk);
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
            }
            return (near, far, (region.buildings.Length, region.roads.Length));
        }

        static void Extrude(MeshBuilder mesh, Vector2[] pts, float height, Color wall)
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
            if (instance == this) instance = null;
        }
    }
}
