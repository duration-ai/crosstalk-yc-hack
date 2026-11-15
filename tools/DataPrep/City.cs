using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataPrep;

using P = (double x, double z);

// --city <name>: a scenery region beyond the playable map, written to
// unity/Assets/Resources/World/city-<name>.json in the same local frame as dogpatch.json (same origin).
// DataSF footprints with their median height, OSM streets with a width, water and parks. The game
// streams it in after the first frame as flat-shaded chunks (CityRegions.cs).
static class CityPrep
{
    // south, north, west, east
    static readonly Dictionary<string, (double s, double n, double w, double e)> Regions = new()
    {
        ["missionbay"] = (37.7645, 37.7790, -122.4010, -122.3800),
        ["southbeach"] = (37.7790, 37.7880, -122.4010, -122.3800),
    };

    static readonly Dictionary<string, float> Widths = new()
    {
        ["motorway"] = 16, ["trunk"] = 16, ["primary"] = 15, ["secondary"] = 13, ["tertiary"] = 12,
        ["residential"] = 10, ["unclassified"] = 10, ["living_street"] = 7, ["service"] = 6,
        ["motorway_link"] = 8, ["trunk_link"] = 8, ["primary_link"] = 8, ["secondary_link"] = 8, ["tertiary_link"] = 8,
        ["pedestrian"] = 6, ["cycleway"] = 3, ["path"] = 2.5f,
    };

    public static async Task Run(Fetch fetch, string name, string outPath)
    {
        var (s, n, w, e) = Regions[name];
        var sw = Geo.Proj(s, w); var ne = Geo.Proj(n, e);
        double minX = Math.Round(sw.x, 1), maxX = Math.Round(ne.x, 1), minZ = Math.Round(sw.z, 1), maxZ = Math.Round(ne.z, 1);
        bool In(P p) => p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
        float[] Flat(IEnumerable<P> pts) => pts.SelectMany(p => new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) }).ToArray();
        List<P> Ring(JsonNode geometry) => geometry.AsArray().Where(g => g != null).Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])).ToList();

        Console.WriteLine($"Fetching DataSF footprints for {name}...");
        var url = "https://data.sf.gov/resource/ynuv-fyni.json?$select=sf16_bldgid,hgt_median_m,shape&$where=" +
            Uri.EscapeDataString($"within_box(shape, {n}, {w}, {s}, {e})") + "&$limit=50000";
        var buildings = new List<CityShape>();
        foreach (var f in (await fetch.Json(url)).AsArray())
        {
            var coords = f["shape"]?["coordinates"]?.AsArray();
            if (coords == null) continue;
            List<P> best = null; double bestArea = 0;
            foreach (var poly in coords)
            {
                var ring = poly[0].AsArray().Select(c => Geo.Proj((double)c[1], (double)c[0])).ToList();
                double a = Geo.Area(ring);
                if (a > bestArea) { bestArea = a; best = ring; }
            }
            if (best == null || bestArea < 20) continue;
            if (best.Count > 1 && Geo.Dist(best[0], best[^1]) < 1e-6) best.RemoveAt(best.Count - 1);
            if (!In(Geo.Centroid(best))) continue;
            var ringS = Geo.SimplifyRing(best, 0.5);
            if (ringS.Count < 3) continue;
            if (Geo.SignedArea(ringS) < 0) ringS.Reverse();
            double h = double.TryParse(f["hgt_median_m"]?.GetValue<string>(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (h < 2) h = 6;
            buildings.Add(new CityShape { H = (float)Math.Round(h, 1), Pts = Flat(ringS) });
        }

        string bbox = string.Join(",", s, w, n, e);
        var query = $"""
        [out:json][timeout:180];
        (
        way[highway~"^(motorway|trunk|primary|secondary|tertiary|residential|unclassified|living_street|service|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|pedestrian|cycleway|path)$"]({bbox});
        way[natural=water]({bbox});
        way[waterway=riverbank]({bbox});
        way[leisure~"^(park|garden|pitch|playground)$"]({bbox});
        way[landuse~"^(grass|recreation_ground)$"]({bbox});
        );
        out geom;
        """;
        Console.WriteLine($"Fetching OSM for {name}...");
        var roads = new List<CityShape>(); var water = new List<CityShape>(); var parks = new List<CityShape>();
        foreach (var el in (await fetch.Overpassql(query))["elements"].AsArray())
        {
            var tags = el["tags"];
            var hw = tags?["highway"]?.GetValue<string>();
            var pts = Ring(el["geometry"]);
            if (hw != null)
            {
                if (tags["area"]?.GetValue<string>() == "yes" || tags["tunnel"] != null) continue;
                float width = Widths[hw];
                if (float.TryParse(tags["width"]?.GetValue<string>()?.Split(' ')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv) && wv > 1) width = wv;
                float layer = tags["bridge"] != null ? 1 : 0;
                foreach (var part in Geo.ClipLine(pts, minX, maxX, minZ, maxZ))
                    if (part.Count >= 2) roads.Add(new CityShape { W = width, H = layer, Kind = hw, Pts = Flat(part) });
                continue;
            }
            if (pts.Count < 4 || Geo.Dist(pts[0], pts[^1]) > 0.5) continue;
            pts.RemoveAt(pts.Count - 1);
            var clipped = Geo.ClipPolygon(pts, minX, maxX, minZ, maxZ);
            if (clipped.Count < 3 || Geo.Area(clipped) < 30) continue;
            if (Geo.SignedArea(clipped) < 0) clipped.Reverse();
            bool wet = tags?["natural"]?.GetValue<string>() == "water" || tags?["waterway"] != null;
            (wet ? water : parks).Add(new CityShape { Pts = Flat(clipped) });
        }

        var data = new CityData
        {
            Name = name, MinX = (float)minX, MaxX = (float)maxX, MinZ = (float)minZ, MaxZ = (float)maxZ,
            Attribution = "Footprints and heights: DataSF Building Footprints ynuv-fyni (PDDL). Streets, water, parks: (c) OpenStreetMap contributors (ODbL).",
            Buildings = buildings.ToArray(), Roads = roads.ToArray(), Water = water.ToArray(), Parks = parks.ToArray(),
        };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };
        File.WriteAllText(outPath, JsonSerializer.Serialize(data, opts));
        Console.WriteLine($"city {name}: {buildings.Count} buildings (tallest {buildings.Max(b => b.H)} m), {roads.Count} roads, {water.Count} water, {parks.Count} parks -> {outPath}");
    }
}

class CityShape { public float H { get; set; } public float W { get; set; } public string Kind { get; set; } public float[] Pts { get; set; } }

class CityData
{
    public string Name { get; set; }
    public float MinX { get; set; } public float MaxX { get; set; } public float MinZ { get; set; } public float MaxZ { get; set; }
    public string Attribution { get; set; }
    public CityShape[] Buildings { get; set; } public CityShape[] Roads { get; set; } public CityShape[] Water { get; set; } public CityShape[] Parks { get; set; }
}
