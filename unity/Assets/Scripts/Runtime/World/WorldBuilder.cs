using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    public struct Pose2D
    {
        public Vector3 Position;
        public float Heading;
        public string Street;
    }

    // Everything the game needs from the generated city.
    public sealed class WorldContext
    {
        public GameObject Root;
        public RoadNetwork Roads;
        public BuildingIndex Buildings;
        public Pose2D PlayerSpawn;
        public Pose2D HeroCar;
        public List<Pose2D> ParkedCars = new List<Pose2D>();
        public Rect Bounds;
        public StaticModels Statics;
        public BuildingFitter Fitter;
        public StreetFurniture Furniture;
        public Landmarks Landmarks;
        public RoadSurface Surface;
        public StaticInstanceIndex Instances => Statics?.Index;
    }

    // Builds Dogpatch from the committed OSM/DataSF extract: land, water, parks, streets with
    // sidewalks and markings, extruded building footprints, and Polyfork street dressing. All
    // generated geometry is vertex-coloured and shares the one flat-shaded material.
    public static class WorldBuilder
    {
        const float ChunkSize = 200f;
        const string HeroStreet = "Tennessee Street";

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c.linear;
        }

        static readonly Color Water = Look.SpeedDial.Bay;
        static readonly Color Park = Look.SpeedDial.Park;

        // The Speed Dial palette (STYLE-GUIDE.md) by name, for generated landmark and roof geometry.
        static readonly Dictionary<string, Color> SpeedDial = new Dictionary<string, Color>
        {
            ["sky"] = C("#1C9BF2"), ["bay"] = C("#17B2D0"), ["cloud"] = C("#FFFFFF"), ["asphalt"] = C("#A9ADB4"),
            ["shadow"] = C("#6C758B"), ["shade"] = C("#A3AEC4"), ["brick"] = C("#E2603F"), ["brickDeep"] = C("#C94A32"),
            ["sunshine"] = C("#FFCB1F"), ["crane"] = C("#FFC21A"), ["lemon"] = C("#FFE27A"), ["mint"] = C("#8CE3BF"),
            ["coral"] = C("#FF9C86"), ["lilac"] = C("#BBA6F7"), ["powder"] = C("#9ED7FF"), ["cream"] = C("#FFF3DA"),
            ["hotline"] = C("#FF3E8E"), ["ink"] = C("#1C2347"), ["navy"] = C("#2C4FA3"), ["sidewalk"] = C("#EEE7DA"),
            ["tree"] = C("#48C26A"), ["treeDeep"] = C("#2FA35A"), ["bark"] = C("#8B5A3C"),
        };

        public static Color Palette(string name) => SpeedDial.TryGetValue(name, out var c) ? c : Color.magenta;

        // measure computes per-footprint IoU for the fit report; the game skips it.
        public static WorldContext Build(WorldData data, ModelLibrary models, Material material, bool measure = false)
        {
            var random = new System.Random(1849);
            var ctx = new WorldContext
            {
                Root = new GameObject("Dogpatch"),
                Roads = new RoadNetwork(data.roads),
                Buildings = new BuildingIndex(),
                Bounds = Rect.MinMaxRect(data.minX, data.minZ, data.maxX, data.maxZ),
            };
            CityRegions.Edge = data.edge != null && data.edge.Length >= 6 ? Geometry.ToPoints(data.edge) : null;
            ctx.Statics = new StaticModels(models);

            var ground = BuildGround(data, ctx, material);
            ctx.Surface = new RoadSurface(ctx, data);
            ctx.Surface.Build(material);
            BuildBuildings(data, ctx, models, measure);
            ctx.Roads.AttachFreeway(Freeway.Build(data, ctx, material));
            ctx.Furniture = new StreetFurniture(ctx, ctx.Statics);
            ctx.Furniture.PlaceTrees(data.treeRecords);
            ctx.Furniture.PlaceProps(data.props);
            ctx.Landmarks = new Landmarks(ctx, ctx.Statics);
            ctx.Landmarks.Build(data);
            var rails = new List<Vector2[]>();
            foreach (var rail in data.rails) rails.Add(Geometry.ToPoints(rail.pts));
            var keepClear = new List<Vector2>();
            foreach (var landmark in data.landmarks)
                if (landmark.kind == "yc" || landmark.kind == "caltrain_22nd") keepClear.Add(new Vector2(landmark.x, landmark.z));
            YardClutter.Build(ctx, ground, rails, keepClear.ToArray(), material);
            ChooseSpawns(ctx, random);

            var lod = ctx.Root.AddComponent<DistanceLod>();
            ctx.Statics.Emit(ctx.Root.transform, material, lod);
            return ctx;
        }

        static List<(string kind, Vector2[] poly)> BuildGround(WorldData data, WorldContext ctx, Material material)
        {
            var b = ctx.Bounds;
            var water = new MeshBuilder();
            float pad = 2500f;
            water.Quad(new Vector3(b.xMin - pad, -0.6f, b.yMin - pad), new Vector3(b.xMin - pad, -0.6f, b.yMax + pad),
                new Vector3(b.xMax + pad, -0.6f, b.yMax + pad), new Vector3(b.xMax + pad, -0.6f, b.yMin - pad), Water, Vector3.up);
            Emit(ctx.Root.transform, "Water", water.ToMesh("water"), material, false);

            var ground = RoadSurface.GroundMaterial(material);
            var land = new Vector2[data.land.Length][];
            for (int i = 0; i < land.Length; i++) land[i] = Geometry.ToPoints(data.land[i].pts);
            var cover = GroundCover.Build(ctx.Root.transform, land, ground, b);

            float coastNorthX = b.xMax, coastEastZ = b.yMin;
            foreach (var line in data.coastline)
                foreach (var p in Geometry.ToPoints(line.pts))
                {
                    if (Mathf.Abs(p.y - b.yMax) < 1f) coastNorthX = Mathf.Min(coastNorthX, p.x);
                    if (Mathf.Abs(p.x - b.xMax) < 1f) coastEastZ = Mathf.Max(coastEastZ, p.y);
                }
            Outskirts.Build(ctx.Root.transform, b, coastNorthX, coastEastZ, ground, material);

            var parks = new MeshBuilder();
            foreach (var poly in data.parks) parks.Polygon(Geometry.ToPoints(poly.pts), 0f, Park);
            Emit(ctx.Root.transform, "Parks", parks.ToMesh("parks"), GroundCover.Layer(ground, GroundCover.ParkLayer), false);

            var seabed = new GameObject("Seabed");
            seabed.transform.SetParent(ctx.Root.transform, false);
            var box = seabed.AddComponent<BoxCollider>();
            box.center = new Vector3(b.center.x, -3.5f, b.center.y);
            box.size = new Vector3(b.width + pad * 2f, 1f, b.height + pad * 2f);

            var walls = new GameObject("MapEdge");
            walls.transform.SetParent(ctx.Root.transform, false);
            void Wall(Vector3 center, Vector3 size)
            {
                var w = walls.AddComponent<BoxCollider>();
                w.center = center;
                w.size = size;
            }
            var edge = CityRegions.Edge;
            if (edge != null)
            {
                for (int i = 0; i < edge.Length; i++)
                {
                    var a = edge[i];
                    var d = edge[(i + 1) % edge.Length] - a;
                    var outward = new Vector2(d.y, -d.x).normalized;
                    var mid = a + d * 0.5f + outward;
                    var segment = new GameObject("MapEdge " + i);
                    segment.transform.SetParent(walls.transform, false);
                    segment.transform.SetPositionAndRotation(new Vector3(mid.x, 10f, mid.y), Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)));
                    segment.AddComponent<BoxCollider>().size = new Vector3(2f, 40f, d.magnitude + 4f);
                }
                return cover;
            }
            Wall(new Vector3(b.xMin - 1f, 10f, b.center.y), new Vector3(2f, 40f, b.height + 4f));
            Wall(new Vector3(b.xMax + 1f, 10f, b.center.y), new Vector3(2f, 40f, b.height + 4f));
            Wall(new Vector3(b.center.x, 10f, b.yMin - 1f), new Vector3(b.width + 4f, 40f, 2f));
            Wall(new Vector3(b.center.x, 10f, b.yMax + 1f), new Vector3(b.width + 4f, 40f, 2f));
            return cover;
        }

        // Every footprint becomes Polyfork models fitted by BuildingFitter; the extruded footprint
        // is kept as the collider so cars and the player hit the real outline.
        static void BuildBuildings(WorldData data, WorldContext ctx, ModelLibrary models, bool measure)
        {
            ctx.Fitter = new BuildingFitter(ctx, models, ctx.Statics, data.landmarks, measure);
            var colliders = new ChunkedMeshBuilder(ChunkSize);
            foreach (var building in data.buildings)
            {
                var poly = Geometry.ToPoints(building.pts);
                if (poly.Length < 3) continue;
                if (Geometry.SignedArea(poly) < 0f) System.Array.Reverse(poly);
                float h = Mathf.Clamp(building.height, 3f, 150f);
                ctx.Buildings.Add(poly);
                ctx.Fitter.Add(building, poly, h);

                var cb = colliders.At(Geometry.Centroid(poly));
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var outward = new Vector3(poly[i].y - poly[j].y, 0f, poly[j].x - poly[i].x).normalized;
                    WallBand(cb, poly[j], poly[i], 0f, h, Color.white, outward);
                }
                cb.Polygon(poly, h, Color.white);
            }

            foreach (var (key, builder) in colliders.All())
            {
                var go = new GameObject("BuildingCollider " + key);
                go.transform.SetParent(ctx.Root.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = builder.ToMesh("building collider " + key);
            }
        }

        static void WallBand(MeshBuilder mb, Vector2 a, Vector2 b, float y0, float y1, Color color, Vector3 outward)
        {
            if (y1 - y0 < 0.01f) return;
            mb.Quad(MeshBuilder.At(a, y0), MeshBuilder.At(a, y1), MeshBuilder.At(b, y1), MeshBuilder.At(b, y0), color, outward);
        }

        // The player starts on the sidewalk of Tennessee Street with a hatchback parked just ahead,
        // so walking to a car and driving down a real street takes a few seconds. Every other curb in
        // Dogpatch gets its parked cars from RoadSurface.PlanParking.
        static void ChooseSpawns(WorldContext ctx, System.Random random)
        {
            var net = ctx.Roads;
            int bestRoad = -1, bestSeg = -1;
            float bestLen = 0f;
            for (int pass = 0; pass < 2 && bestRoad < 0; pass++)
            {
                for (int r = 0; r < net.Roads.Length; r++)
                {
                    var road = net.Roads[r];
                    if (road.kind == "path" || road.kind == "service") continue;
                    if (pass == 0 && road.name != HeroStreet) continue;
                    var pts = net.Points[r];
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        float len = Vector2.Distance(pts[i], pts[i + 1]);
                        var mid = (pts[i] + pts[i + 1]) * 0.5f;
                        if (len > bestLen && Mathf.Abs(mid.x) < 500f && Mathf.Abs(mid.y) < 600f)
                        {
                            bestLen = len;
                            bestRoad = r;
                            bestSeg = i;
                        }
                    }
                }
            }

            var p = net.Points[bestRoad];
            var a = p[bestSeg];
            var b = p[bestSeg + 1];
            var dir = (b - a).normalized;
            if (dir.y < 0f)
            {
                (a, b) = (b, a);
                dir = -dir;
            }
            var right = new Vector2(dir.y, -dir.x);
            float hw = RoadNetwork.HalfWidth(net.Roads[bestRoad]);
            var start = Vector2.Lerp(a, b, 0.3f);
            float heading = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            string street = net.Roads[bestRoad].name;

            bool curbed = RoadNetwork.IsCurbed(net.Roads[bestRoad]);
            var heroAt = start + right * (hw - 1.15f) + dir * 7f;
            ctx.PlayerSpawn = new Pose2D { Position = MeshBuilder.At(start + right * (hw + 1.2f), (curbed ? RoadSurface.SidewalkY : 0f) + 0.05f), Heading = heading, Street = street };
            ctx.HeroCar = new Pose2D { Position = MeshBuilder.At(heroAt, (curbed ? RoadSurface.RoadY : 0f) + 0.05f), Heading = heading, Street = street };
            ctx.Surface.PlanParking(random, ctx.ParkedCars, new List<Vector2> { heroAt });
            int beyond = 0;
            ctx.ParkedCars.RemoveAll(car => car.Position.z > CityRegions.DogpatchNorth && beyond++ % 3 != 0);
        }

        public static GameObject Emit(Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return go;
        }
    }
}
