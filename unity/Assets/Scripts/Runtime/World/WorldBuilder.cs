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
        public StaticInstanceIndex Instances => Statics?.Index;
    }

    // Builds Dogpatch from the committed OSM/DataSF extract: land, water, parks, streets with
    // sidewalks and markings, extruded building footprints, and Polyfork street dressing. All
    // generated geometry is vertex-coloured and shares the one flat-shaded material.
    public static class WorldBuilder
    {
        const float ChunkSize = 200f;
        const float SidewalkWidth = 3f;
        const string HeroStreet = "Tennessee Street";

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c.linear;
        }

        static readonly Color Land = Look.SpeedDial.Land;
        static readonly Color Water = Look.SpeedDial.Bay;
        static readonly Color Park = Look.SpeedDial.Park;
        static readonly Color Sidewalk = Look.SpeedDial.Sidewalk;
        static readonly Color Asphalt = Look.SpeedDial.Asphalt;
        static readonly Color ServiceAsphalt = Look.SpeedDial.Asphalt;
        static readonly Color PathColor = Look.SpeedDial.Sidewalk;
        static readonly Color LaneYellow = Look.SpeedDial.DoubleYellow;
        static readonly Color LaneWhite = Look.SpeedDial.LaneWhite;
        static readonly Color RailBed = Look.SpeedDial.RailBed;
        static readonly Color Rail = Look.SpeedDial.Rail;

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
            ctx.Statics = new StaticModels(models);

            BuildGround(data, ctx, material);
            BuildStreets(data, ctx, material);
            BuildBuildings(data, ctx, models, measure);
            ctx.Furniture = new StreetFurniture(ctx, ctx.Statics);
            ctx.Furniture.PlaceTrees(data.treeRecords);
            ctx.Furniture.PlaceProps(data.props);
            ctx.Landmarks = new Landmarks(ctx, ctx.Statics);
            ctx.Landmarks.Build(data);
            ChooseSpawns(ctx, models, random);

            var lod = ctx.Root.AddComponent<DistanceLod>();
            ctx.Statics.Emit(ctx.Root.transform, material, lod);
            new PropPlacer(ctx, models, random).PlaceNpcs(ctx, material);
            return ctx;
        }

        static void BuildGround(WorldData data, WorldContext ctx, Material material)
        {
            var b = ctx.Bounds;
            var water = new MeshBuilder();
            float pad = 2500f;
            water.Quad(new Vector3(b.xMin - pad, -0.6f, b.yMin - pad), new Vector3(b.xMin - pad, -0.6f, b.yMax + pad),
                new Vector3(b.xMax + pad, -0.6f, b.yMax + pad), new Vector3(b.xMax + pad, -0.6f, b.yMin - pad), Water, Vector3.up);
            Emit(ctx.Root.transform, "Water", water.ToMesh("water"), material, false);

            var land = new MeshBuilder();
            foreach (var poly in data.land) land.Polygon(Geometry.ToPoints(poly.pts), 0f, Land);
            var landMesh = land.ToMesh("land");
            var landGo = Emit(ctx.Root.transform, "Land", landMesh, material, false);
            landGo.AddComponent<MeshCollider>().sharedMesh = landMesh;

            var parks = new MeshBuilder();
            foreach (var poly in data.parks) parks.Polygon(Geometry.ToPoints(poly.pts), 0.01f, Park);
            Emit(ctx.Root.transform, "Parks", parks.ToMesh("parks"), material, false);

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
            Wall(new Vector3(b.xMin - 1f, 10f, b.center.y), new Vector3(2f, 40f, b.height + 4f));
            Wall(new Vector3(b.xMax + 1f, 10f, b.center.y), new Vector3(2f, 40f, b.height + 4f));
            Wall(new Vector3(b.center.x, 10f, b.yMin - 1f), new Vector3(b.width + 4f, 40f, 2f));
            Wall(new Vector3(b.center.x, 10f, b.yMax + 1f), new Vector3(b.width + 4f, 40f, 2f));
        }

        static void BuildStreets(WorldData data, WorldContext ctx, Material material)
        {
            var chunks = new ChunkedMeshBuilder(ChunkSize);
            var net = ctx.Roads;
            for (int r = 0; r < data.roads.Length; r++)
            {
                var road = data.roads[r];
                var pts = net.Points[r];
                if (pts.Length < 2) continue;
                var mb = chunks.At(pts[pts.Length / 2]);
                float hw = RoadNetwork.HalfWidth(road);
                if (road.kind == "path")
                {
                    mb.Ribbon(pts, hw, 0.02f, PathColor, true);
                    continue;
                }
                bool major = road.kind != "service" && road.kind != "track";
                if (major) mb.Ribbon(pts, hw + SidewalkWidth, 0.025f, Sidewalk, true);
                mb.Ribbon(pts, hw, 0.04f, major ? Asphalt : ServiceAsphalt, true);
                if (road.kind == "primary" || road.kind == "secondary" || road.kind == "tertiary" || (road.kind == "residential" && hw >= 4.5f))
                {
                    Stripe(mb, pts, net, 0.17f, 0.065f, LaneYellow, 0f, 0f);
                    Stripe(mb, pts, net, -0.17f, 0.065f, LaneYellow, 0f, 0f);
                }
                if (hw >= 7f)
                {
                    Stripe(mb, pts, net, hw * 0.5f, 0.08f, LaneWhite, 3f, 6f);
                    Stripe(mb, pts, net, -hw * 0.5f, 0.08f, LaneWhite, 3f, 6f);
                }
            }

            foreach (var rail in data.rails)
            {
                var pts = Geometry.ToPoints(rail.pts);
                if (pts.Length < 2) continue;
                var mb = chunks.At(pts[pts.Length / 2]);
                mb.Ribbon(pts, 1.4f, 0.045f, RailBed, false);
                mb.Ribbon(Offset(pts, 0.72f), 0.07f, 0.065f, Rail, false);
                mb.Ribbon(Offset(pts, -0.72f), 0.07f, 0.065f, Rail, false);
            }

            foreach (var (key, builder) in chunks.All())
                Emit(ctx.Root.transform, "Streets " + key, builder.ToMesh("streets " + key), material, false);
        }

        // A road marking along the polyline at a lateral offset: solid when dash is 0, else dashed.
        // Markings stop short of intersections.
        static void Stripe(MeshBuilder mb, Vector2[] pts, RoadNetwork net, float offset, float halfWidth, Color color, float dash, float gap)
        {
            const float y = 0.055f;
            const float step = 4f;
            var line = Offset(pts, offset);
            for (int i = 0; i < line.Length - 1; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                float len = Vector2.Distance(a, b);
                var dir = (b - a) / Mathf.Max(len, 1e-3f);
                var n = new Vector2(-dir.y, dir.x) * halfWidth;
                float piece = dash > 0f ? dash : step;
                float stride = dash > 0f ? dash + gap : step;
                for (float t = 0f; t < len; t += stride)
                {
                    var p0 = a + dir * t;
                    var p1 = a + dir * Mathf.Min(t + piece, len);
                    if (net.DistanceToIntersection((p0 + p1) * 0.5f, 12f) < 11f) continue;
                    mb.Quad(MeshBuilder.At(p0 + n, y), MeshBuilder.At(p1 + n, y), MeshBuilder.At(p1 - n, y), MeshBuilder.At(p0 - n, y), color, Vector3.up);
                }
            }
        }

        static Vector2[] Offset(Vector2[] pts, float offset)
        {
            var result = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var d = i < pts.Length - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
                d.Normalize();
                result[i] = pts[i] + new Vector2(-d.y, d.x) * offset;
            }
            return result;
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
        // so walking to a car and driving down a real street takes a few seconds.
        static void ChooseSpawns(WorldContext ctx, ModelLibrary models, System.Random random)
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

            ctx.PlayerSpawn = new Pose2D { Position = MeshBuilder.At(start + right * (hw + 1.2f), 0.05f), Heading = heading, Street = street };
            ctx.HeroCar = new Pose2D { Position = MeshBuilder.At(start + right * (hw - 1.4f) + dir * 7f, 0.05f), Heading = heading, Street = street };

            var taken = new List<Vector2> { start + right * (hw - 1.4f) + dir * 7f };
            var order = new List<int>();
            for (int r = 0; r < net.Roads.Length; r++) order.Add(r);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            foreach (int r in order)
            {
                if (ctx.ParkedCars.Count >= 40) break;
                var road = net.Roads[r];
                if (road.kind != "residential" && road.kind != "tertiary" && road.kind != "secondary" && road.kind != "unclassified") continue;
                var pts = net.Points[r];
                float rhw = RoadNetwork.HalfWidth(road);
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float len = Vector2.Distance(pts[i], pts[i + 1]);
                    var d = (pts[i + 1] - pts[i]) / Mathf.Max(len, 1e-3f);
                    for (float t = 15f + (float)random.NextDouble() * 20f; t < len - 15f; t += 35f + (float)random.NextDouble() * 50f)
                    {
                        float side = random.Next(2) == 0 ? 1f : -1f;
                        var n = new Vector2(d.y, -d.x) * side;
                        var pos = pts[i] + d * t + n * (rhw - 1.4f);
                        if (net.DistanceToIntersection(pos, 20f) < 16f) continue;
                        if (ctx.Buildings.Blocked(pos, 1.5f)) continue;
                        if (net.Nearest(pos, 1f, out _, r)) continue;
                        bool clear = true;
                        foreach (var q in taken) if (Vector2.Distance(q, pos) < 9f) clear = false;
                        if (!clear) continue;
                        taken.Add(pos);
                        float h = Mathf.Atan2(d.x * side, d.y * side) * Mathf.Rad2Deg;
                        ctx.ParkedCars.Add(new Pose2D { Position = MeshBuilder.At(pos, 0.05f), Heading = h, Street = road.name });
                    }
                }
            }
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
