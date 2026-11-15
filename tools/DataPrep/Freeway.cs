using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataPrep;

using P = (double x, double z);

// --freeway: every motorway and motorway_link way touching the bbox, with OSM node ids, written to
// unity/Assets/Resources/World/freeway.json. The main extract only keeps the bridge pieces of I-280 as
// landmarks; the drivable freeway needs the at-grade stretches, the street ends of the ramps and the
// shared nodes that join ways into one graph. Points created by clipping to the bbox carry node id 0.
static class FreewayPrep
{
    public static async Task Run(Fetch fetch, string outPath, string bbox, double minX, double maxX, double minZ, double maxZ)
    {
        var query = $"""
        [out:json][timeout:120];
        way[highway~"^(motorway|motorway_link)$"]({bbox});
        out body geom;
        """;
        Console.WriteLine("Fetching OSM (freeway)...");
        var elements = (await fetch.Overpassql(query))["elements"].AsArray();
        var ways = new List<FreewayWay>();
        foreach (var e in elements)
        {
            if (e?["type"]?.GetValue<string>() != "way") continue;
            string Tag(string k) => e["tags"]?[k]?.GetValue<string>();
            var nodes = e["nodes"].AsArray().Select(n => (long)n).ToArray();
            var pts = e["geometry"].AsArray().Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])).ToArray();
            int.TryParse(Tag("lanes")?.Split(';')[0], out int lanes);
            int.TryParse(Tag("layer"), out int layer);
            foreach (var (runPts, runIds) in Clip(pts, nodes, minX, maxX, minZ, maxZ))
                ways.Add(new FreewayWay
                {
                    OsmId = (long)e["id"], Name = Tag("name") ?? "", Ref = Tag("ref") ?? "", Kind = Tag("highway"),
                    Lanes = lanes > 0 ? lanes : Tag("highway") == "motorway" ? 2 : 1, Oneway = Tag("oneway") is "yes" or "1" or "true",
                    Maxspeed = Tag("maxspeed") ?? "", Layer = layer, Bridge = Tag("bridge") is { } b && b != "no",
                    Destination = Tag("destination") ?? "", Nodes = runIds.ToArray(),
                    Pts = runPts.SelectMany(p => new[] { (float)Math.Round(p.x, 2), (float)Math.Round(p.z, 2) }).ToArray(),
                });
        }
        var data = new FreewayData
        {
            Source = "OpenStreetMap via Overpass: " + query.Replace("\n", " ").Trim(),
            Attribution = "(c) OpenStreetMap contributors (ODbL)",
            Ways = ways.OrderBy(w => w.OsmId).ToArray(),
        };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(outPath, JsonSerializer.Serialize(data, opts));
        Console.WriteLine($"freeway: {data.Ways.Length} way runs ({data.Ways.Count(w => w.Kind == "motorway")} motorway, {data.Ways.Count(w => w.Kind == "motorway_link")} motorway_link) -> {outPath}");
    }

    // Clips a way to the bbox, keeping each kept vertex's node id; boundary crossings become id-0 points.
    static List<(List<P>, List<long>)> Clip(P[] pts, long[] ids, double x0, double x1, double z0, double z1)
    {
        bool In(P p) => p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1;
        var runs = new List<(List<P>, List<long>)>();
        var run = (pts: new List<P>(), ids: new List<long>());
        void Close() { if (run.pts.Count > 1) runs.Add(run); run = (new List<P>(), new List<long>()); }
        for (int i = 0; i < pts.Length; i++)
        {
            bool inside = In(pts[i]);
            if (i > 0 && inside != In(pts[i - 1]))
            {
                var seg = Geo.ClipLine(new[] { pts[i - 1], pts[i] }, x0, x1, z0, z1);
                if (seg.Count > 0) { run.pts.Add(inside ? seg[0][0] : seg[0][^1]); run.ids.Add(0); }
                if (!inside) Close();
            }
            if (inside) { run.pts.Add(pts[i]); run.ids.Add(ids[i]); }
        }
        Close();
        return runs;
    }
}

// JSON shape of unity/Assets/Resources/World/freeway.json (camelCase). Mirrored by unity/Assets/Scripts/Runtime/Data/FreewayData.cs.
class FreewayData { public string Source { get; set; } public string Attribution { get; set; } public FreewayWay[] Ways { get; set; } }
class FreewayWay
{
    public long OsmId { get; set; } public string Name { get; set; } public string Ref { get; set; } public string Kind { get; set; }
    public int Lanes { get; set; } public bool Oneway { get; set; } public string Maxspeed { get; set; } public int Layer { get; set; } public bool Bridge { get; set; }
    public string Destination { get; set; } public long[] Nodes { get; set; } public float[] Pts { get; set; }
}
