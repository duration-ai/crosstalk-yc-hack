using System.Text.Json;

namespace DataPrep;

using P = (double x, double z);

// --ground: ground cover polygons for the open land between buildings and streets, written to
// unity/Assets/Resources/World/ground.json: surface parking lots, industrial and rail yards, dirt and
// construction lots, grass, sports pitches, plazas and school grounds, piers, sand and scrub. The main
// extract only keeps parks, so everything else used to render as one flat cream plane. Multipolygon
// relations contribute their closed outer member ways.
static class GroundPrep
{
    static readonly Dictionary<string, string> Kinds = new()
    {
        ["amenity=parking"] = "parking",
        ["landuse=industrial"] = "industrial", ["landuse=military"] = "industrial", ["landuse=port"] = "industrial",
        ["landuse=railway"] = "railway",
        ["landuse=construction"] = "dirt", ["landuse=brownfield"] = "dirt", ["landuse=greenfield"] = "dirt",
        ["landuse=grass"] = "grass", ["landuse=recreation_ground"] = "grass", ["landuse=village_green"] = "grass", ["landuse=meadow"] = "grass",
        ["leisure=pitch"] = "pitch", ["leisure=dog_park"] = "dirt", ["leisure=playground"] = "sand", ["leisure=sports_centre"] = "plaza",
        ["highway=pedestrian"] = "plaza", ["place=square"] = "plaza", ["leisure=outdoor_seating"] = "plaza",
        ["amenity=school"] = "plaza", ["amenity=university"] = "plaza", ["amenity=college"] = "plaza",
        ["man_made=pier"] = "pier", ["man_made=breakwater"] = "pier",
        ["natural=beach"] = "sand", ["natural=sand"] = "sand", ["natural=scrub"] = "scrub", ["natural=grassland"] = "grass",
    };

    public static async Task Run(Fetch fetch, string outPath, string bbox, double minX, double maxX, double minZ, double maxZ)
    {
        var query = $"""
        [out:json][timeout:120];
        (
        way[amenity=parking][parking!~"^(multi-storey|underground|street_side|lane|rooftop)$"]({bbox});
        way[landuse~"^(industrial|military|port|railway|construction|brownfield|greenfield|grass|recreation_ground|village_green|meadow)$"]({bbox});
        relation[landuse~"^(industrial|railway|construction|brownfield)$"]({bbox});
        way[leisure~"^(pitch|dog_park|playground|sports_centre|outdoor_seating)$"]({bbox});
        way[highway=pedestrian][area=yes]({bbox});
        way[place=square]({bbox});
        way[amenity~"^(school|university|college)$"]({bbox});
        way[man_made~"^(pier|breakwater)$"]({bbox});
        way[natural~"^(beach|sand|scrub|grassland)$"]({bbox});
        );
        out geom;
        """;
        Console.WriteLine("Fetching OSM (ground cover)...");
        var elements = (await fetch.Overpassql(query))["elements"].AsArray();
        var polys = new List<GroundPolygon>();
        foreach (var e in elements)
        {
            var tags = e["tags"]?.AsObject();
            if (tags == null) continue;
            string kind = null;
            foreach (var (key, value) in tags)
                if (Kinds.TryGetValue(key + "=" + value?.GetValue<string>(), out var k)) { kind = k; break; }
            if (kind == null) continue;
            var rings = new List<List<P>>();
            if (e["type"].GetValue<string>() == "way") rings.Add(Ring(e["geometry"]));
            else
                foreach (var m in e["members"].AsArray())
                    if (m["type"].GetValue<string>() == "way" && m["role"]?.GetValue<string>() == "outer" && m["geometry"] != null) rings.Add(Ring(m["geometry"]));
            foreach (var ring in rings)
            {
                if (ring.Count < 4 || Geo.Dist(ring[0], ring[^1]) > 0.5) continue;
                ring.RemoveAt(ring.Count - 1);
                var clipped = Geo.ClipPolygon(ring, minX, maxX, minZ, maxZ);
                if (clipped.Count < 3 || Geo.Area(clipped) < 30) continue;
                if (Geo.SignedArea(clipped) < 0) clipped.Reverse();
                polys.Add(new GroundPolygon { Kind = kind, OsmId = (long)e["id"], Pts = clipped.SelectMany(p => new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) }).ToArray() });
            }
        }
        var data = new GroundData
        {
            Attribution = "(c) OpenStreetMap contributors (ODbL)",
            Polygons = polys.OrderByDescending(p => p.Pts.Length).ThenBy(p => p.OsmId).ToArray(),
        };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(outPath, JsonSerializer.Serialize(data, opts));
        foreach (var g in polys.GroupBy(p => p.Kind).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key}: {g.Count()} polygons, {g.Sum(p => Geo.Area(Unflat(p.Pts))):F0} m2");
        Console.WriteLine($"ground: {polys.Count} polygons -> {outPath}");
    }

    static List<P> Ring(System.Text.Json.Nodes.JsonNode geometry) =>
        geometry.AsArray().Where(g => g != null).Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])).ToList();

    static List<P> Unflat(float[] f) => Enumerable.Range(0, f.Length / 2).Select(i => ((double)f[2 * i], (double)f[2 * i + 1])).ToList();
}

class GroundPolygon { public string Kind { get; set; } public long OsmId { get; set; } public float[] Pts { get; set; } }

class GroundData { public string Attribution { get; set; } public GroundPolygon[] Polygons { get; set; } }
