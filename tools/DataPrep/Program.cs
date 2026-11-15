using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataPrep;
using P = (double x, double z);

const double South = 37.7495, North = 37.7645, West = -122.3965, East = -122.3800;
const string Attribution = "Streets © OpenStreetMap contributors (ODbL). Building footprints: DataSF Building Footprints (ynuv-fyni). Models: polyfork.dev.";

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var refresh = args.Contains("--refresh");
var root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, ".git"))) root = Path.GetDirectoryName(root) ?? throw new Exception("repo root not found");
string Rel(params string[] p) => Path.Combine(new[] { root }.Concat(p).ToArray());
var fetch = new Fetch(Rel("data", "cache"), refresh);

Geo.OriginLat = (South + North) / 2; Geo.OriginLon = (West + East) / 2;
var sw = Geo.Proj(South, West); var ne = Geo.Proj(North, East);
double minX = Math.Round(sw.x, 1), maxX = Math.Round(ne.x, 1), minZ = Math.Round(sw.z, 1), maxZ = Math.Round(ne.z, 1);

float[] Flat(IEnumerable<P> pts) => pts.SelectMany(p => new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) }).ToArray();
List<P> Pts(JsonNode way) => Geo.Dedupe(way["geometry"].AsArray().Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])));
List<P> Ccw(List<P> r) { if (Geo.SignedArea(r) < 0) r.Reverse(); return r; }
bool Inside(P p) => p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
string Tag(JsonNode e, string k) => e["tags"]?[k]?.GetValue<string>();

// ---- OpenStreetMap
const double Pad = 0.02;
string bbox = string.Join(",", South, West, North, East);
string pbbox = string.Join(",", South - Pad, West - Pad, North + Pad, East + Pad);
var query = $"""
[out:json][timeout:240];
(
way[highway]({bbox});
way[railway]({bbox});
way[building]({bbox});
way[leisure~"^(park|pitch|playground|garden)$"]({bbox});
way[landuse~"^(grass|recreation_ground)$"]({bbox});
node[natural=tree]({bbox});
way[natural=coastline]({pbbox});
);
out geom;
""";
Console.WriteLine("Fetching OSM...");
var osm = (await fetch.Overpassql(query))["elements"].AsArray();

var roadExclude = new HashSet<string> { "motorway", "motorway_link", "trunk_link", "construction", "proposed", "platform", "steps", "corridor", "elevator" };
var roadWidth = new Dictionary<string, float> { ["primary"] = 14, ["secondary"] = 12, ["tertiary"] = 11, ["residential"] = 9, ["unclassified"] = 9, ["living_street"] = 7, ["service"] = 5.5f, ["track"] = 4 };
var pathKinds = new HashSet<string> { "footway", "path", "pedestrian", "cycleway", "bridleway" };
var railOk = new HashSet<string> { "light_rail", "tram", "rail" };

var roads = new List<Road>(); var rails = new List<Polyline>(); var parks = new List<Polygon>(); var trees = new List<float>();
var osmBuildings = new List<(List<P> ring, string kind, float? height)>();
var coast = new List<List<(double lat, double lon)>>();

foreach (var e in osm)
{
    var type = e["type"].GetValue<string>();
    if (type == "node") { var p = Geo.Proj((double)e["lat"], (double)e["lon"]); if (Inside(p)) { trees.Add((float)Math.Round(p.x, 1)); trees.Add((float)Math.Round(p.z, 1)); } continue; }
    if (e["geometry"] == null) continue;
    if (Tag(e, "natural") == "coastline") { coast.Add(e["geometry"].AsArray().Select(g => ((double)g["lat"], (double)g["lon"])).ToList()); continue; }
    var pts = Pts(e);
    var hw = Tag(e, "highway"); var rw = Tag(e, "railway"); var bld = Tag(e, "building");
    if (hw != null && !roadExclude.Contains(hw))
    {
        bool path = pathKinds.Contains(hw);
        float width = path ? 2.5f : roadWidth.GetValueOrDefault(hw, 5f);
        if (!path && float.TryParse(Tag(e, "width")?.Replace(" m", ""), out var tw) && tw >= 2 && tw <= 40) width = tw;
        var ow = Tag(e, "oneway") is "yes" or "1" or "true";
        foreach (var run in Geo.ClipLine(pts, minX, maxX, minZ, maxZ))
            roads.Add(new Road { Name = Tag(e, "name") ?? "", Kind = path ? "path" : hw, Width = width, Oneway = ow, Pts = Flat(Geo.Dedupe(run)) });
    }
    else if (rw != null && railOk.Contains(rw))
    {
        foreach (var run in Geo.ClipLine(pts, minX, maxX, minZ, maxZ)) rails.Add(new Polyline { Kind = rw, Pts = Flat(Geo.Dedupe(run)) });
    }
    else if (bld != null && pts.Count >= 4)
    {
        pts.RemoveAt(pts.Count - 1);
        float? h = null;
        if (float.TryParse(Tag(e, "height")?.Replace(" m", "").Replace("m", ""), out var hh)) h = hh;
        else if (float.TryParse(Tag(e, "building:levels"), out var lv)) h = lv * 3.5f;
        osmBuildings.Add((pts, bld, h));
    }
    else if ((Tag(e, "leisure") ?? Tag(e, "landuse")) is { } pk && pts.Count >= 4)
    {
        pts.RemoveAt(pts.Count - 1);
        var clipped = Geo.ClipPolygon(pts, minX, maxX, minZ, maxZ);
        if (clipped.Count >= 3 && Math.Abs(Geo.SignedArea(clipped)) >= 20) parks.Add(new Polygon { Kind = pk, Pts = Flat(Ccw(clipped)) });
    }
}

// ---- DataSF buildings
Console.WriteLine("Fetching DataSF footprints...");
var sodaUrl = "https://data.sf.gov/resource/ynuv-fyni.json?$select=sf16_bldgid,hgt_median_m,gnd_min_m,shape&$where=" +
    Uri.EscapeDataString($"within_box(shape, {North}, {West}, {South}, {East})") + "&$limit=50000";
var soda = (await fetch.Json(sodaUrl)).AsArray();
var buildings = new List<Building>(); int noHeight = 0, tiny = 0, outside = 0;
foreach (var f in soda)
{
    var coords = f["shape"]?["coordinates"]?.AsArray();
    if (coords == null) continue;
    List<P> best = null; double bestArea = 0;
    foreach (var poly in coords)
    {
        var ring = poly[0].AsArray().Select(c => Geo.Proj((double)c[1], (double)c[0])).ToList();
        double a = Math.Abs(Geo.SignedArea(ring));
        if (a > bestArea) { bestArea = a; best = ring; }
    }
    if (best == null) continue;
    if (best.Count > 1 && Geo.Dist(best[0], best[^1]) < 1e-6) best.RemoveAt(best.Count - 1);
    var c0 = Geo.Centroid(best);
    if (!Inside(c0)) { outside++; continue; }
    if (bestArea < 12) { tiny++; continue; }
    var ringS = Geo.SimplifyRing(best, 0.3);
    if (ringS.Count < 3) continue;
    Ccw(ringS);
    var match = osmBuildings.FirstOrDefault(b => Geo.PointInPoly(c0, b.ring));
    string source; float height;
    if (float.TryParse(f["hgt_median_m"]?.GetValue<string>(), out var dh) && dh >= 2.5f) { height = dh; source = "datasf"; }
    else if (match.ring != null && match.height is { } oh) { height = oh; source = "osm"; }
    else { height = 7; source = "default"; }
    if (source != "datasf") noHeight++;
    buildings.Add(new Building { Id = f["sf16_bldgid"]?.GetValue<string>() ?? "", Height = (float)Math.Round(Math.Clamp(height, 3, 150), 1), HeightSource = source, Kind = match.kind ?? "yes", Pts = Flat(ringS) });
}

// ---- Land from coastline (land on the left of way direction)
var land = BuildLand(coast, minX, maxX, minZ, maxZ, out var landWarn);
if (landWarn != null) Console.WriteLine("WARNING: " + landWarn);

var world = new WorldData
{
    Name = "Dogpatch", OriginLat = Geo.OriginLat, OriginLon = Geo.OriginLon,
    MinX = (float)minX, MaxX = (float)maxX, MinZ = (float)minZ, MaxZ = (float)maxZ,
    Attribution = Attribution, Roads = roads.ToArray(), Rails = rails.ToArray(), Buildings = buildings.ToArray(),
    Parks = parks.ToArray(), Land = land.Select(l => new Polygon { Kind = "land", Pts = Flat(l) }).ToArray(), Trees = trees.ToArray(),
};
var jopts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var worldPath = Rel("unity", "Assets", "Resources", "World", "dogpatch.json");
Directory.CreateDirectory(Path.GetDirectoryName(worldPath));
File.WriteAllText(worldPath, JsonSerializer.Serialize(world, jopts));

// ---- Polyfork models
Console.WriteLine("Fetching Polyfork models...");
var modelsDir = Rel("unity", "Assets", "Resources", "Polyfork");
Directory.CreateDirectory(modelsDir);
var catalog = new ModelCatalog { Source = "https://polyfork.dev" };
var entries = new List<ModelEntry>();
foreach (var (role, ids) in JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Rel("tools", "DataPrep", "models.json"))))
    foreach (var id in ids)
    {
        File.WriteAllBytes(Path.Combine(modelsDir, id + ".bytes"), await fetch.Get($"https://polyfork.dev/cdn/{id}.glb"));
        var meta = await fetch.Json($"https://polyfork.dev/api/assets/{id}");
        entries.Add(new ModelEntry { Id = id, Role = role, Title = (string)meta["title"], SizeX = (float)meta["size_m"]["x"], SizeY = (float)meta["size_m"]["y"], SizeZ = (float)meta["size_m"]["z"], Triangles = (int)meta["triangles"] });
    }
catalog.Models = entries.ToArray();
var catalogPath = Path.Combine(modelsDir, "catalog.json");
File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, jopts));

// ---- Preview
var previewPath = Rel("data", "preview.png");
Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
Render.Preview(world, previewPath);

Console.WriteLine($"""

== Summary ==
roads: {roads.Count}   rails: {rails.Count}   parks: {parks.Count}   land polygons: {land.Count}   trees: {trees.Count / 2}
buildings: {buildings.Count} (datasf {buildings.Count(b => b.HeightSource == "datasf")}, osm {buildings.Count(b => b.HeightSource == "osm")}, default {buildings.Count(b => b.HeightSource == "default")}); DataSF rows {soda.Count}, outside {outside}, <12 m2 {tiny}
models: {entries.Count}   network requests this run: {fetch.Requests}
dogpatch.json: {new FileInfo(worldPath).Length / 1024} KB   catalog.json: {new FileInfo(catalogPath).Length} B   preview.png: {new FileInfo(previewPath).Length / 1024} KB
""");

static List<List<P>> BuildLand(List<List<(double lat, double lon)>> ways, double x0, double x1, double z0, double z1, out string warn)
{
    warn = null;
    var rect = new List<P> { (x0, z0), (x1, z0), (x1, z1), (x0, z1) };
    double W = x1 - x0, H = z1 - z0, Per = 2 * (W + H);
    // merge ways into chains by shared endpoints
    string Key((double lat, double lon) p) => $"{p.lat:F7},{p.lon:F7}";
    var chains = ways.Where(w => w.Count > 1).Select(w => w.ToList()).ToList();
    bool merged = true;
    while (merged)
    {
        merged = false;
        var byStart = new Dictionary<string, int>();
        for (int i = 0; i < chains.Count; i++) byStart.TryAdd(Key(chains[i][0]), i);
        for (int i = 0; i < chains.Count && !merged; i++)
        {
            if (Key(chains[i][0]) == Key(chains[i][^1])) continue;
            if (byStart.TryGetValue(Key(chains[i][^1]), out var j) && j != i)
            {
                chains[i].AddRange(chains[j].Skip(1)); chains.RemoveAt(j); merged = true;
            }
        }
    }
    double T(P p)
    {
        double[] d = { Math.Abs(p.z - z0), Math.Abs(p.x - x1), Math.Abs(p.z - z1), Math.Abs(p.x - x0) };
        int m = Array.IndexOf(d, d.Min());
        return m switch { 0 => p.x - x0, 1 => W + (p.z - z0), 2 => W + H + (x1 - p.x), _ => 2 * W + H + (z1 - p.z) };
    }
    double[] cornerT = { 0, W, W + H, 2 * W + H };
    var pieces = new List<(List<P> pts, double ts, double te)>();
    var closed = new List<List<P>>();
    foreach (var ch in chains)
    {
        var proj = ch.Select(p => Geo.Proj(p.lat, p.lon)).ToList();
        bool isClosed = Key(ch[0]) == Key(ch[^1]);
        foreach (var run in Geo.ClipLine(proj, x0, x1, z0, z1))
        {
            bool startsOnEdge = Geo.Dist(run[0], proj[0]) > 1e-6, endsOnEdge = Geo.Dist(run[^1], proj[^1]) > 1e-6;
            if (isClosed && !startsOnEdge && !endsOnEdge) { if (Geo.SignedArea(run) > 0) closed.Add(run); continue; }
            if (!startsOnEdge || !endsOnEdge) continue;
            pieces.Add((run, T(run[0]), T(run[^1])));
        }
    }
    if (pieces.Count == 0) { warn = "coastline does not cross the bounds; emitting full rectangle"; return new() { rect }; }

    double Fwd(double from, double to) { var d = (to - from) % Per; return d < 0 ? d + Per : d; }
    var result = new List<List<P>>(closed); var used = new bool[pieces.Count];
    for (int s = 0; s < pieces.Count; s++)
    {
        if (used[s]) continue;
        var poly = new List<P>(); int cur = s;
        for (int guard = 0; guard <= pieces.Count; guard++)
        {
            used[cur] = true; poly.AddRange(pieces[cur].pts);
            double te = pieces[cur].te; int next = -1; double bd = double.MaxValue;
            for (int j = 0; j < pieces.Count; j++)
            {
                if (used[j] && j != s) continue;
                double d = Fwd(te, pieces[j].ts); if (d < 1e-6 && j == cur) d = Per;
                if (d < bd) { bd = d; next = j; }
            }
            foreach (var c in cornerT.OrderBy(c => Fwd(te, c)))
            {
                double dc = Fwd(te, c);
                if (dc > 1e-6 && dc < bd - 1e-6) poly.Add(rect[Array.IndexOf(cornerT, c)]);
            }
            if (next == s) break;
            cur = next;
        }
        if (Geo.SignedArea(poly) > 0) result.Add(Geo.Dedupe(poly));
        else warn = "stitching produced a clockwise polygon (skipped)";
    }
    if (result.Count == 0) { warn = (warn ?? "") + " land stitching failed; emitting full rectangle"; return new() { rect }; }
    return result;
}

class WorldData
{
    public string Name { get; set; } public double OriginLat { get; set; } public double OriginLon { get; set; }
    public float MinX { get; set; } public float MaxX { get; set; } public float MinZ { get; set; } public float MaxZ { get; set; }
    public string Attribution { get; set; }
    public Road[] Roads { get; set; } public Polyline[] Rails { get; set; } public Building[] Buildings { get; set; }
    public Polygon[] Parks { get; set; } public Polygon[] Land { get; set; } public float[] Trees { get; set; }
}
class Road { public string Name { get; set; } public string Kind { get; set; } public float Width { get; set; } public bool Oneway { get; set; } public float[] Pts { get; set; } }
class Polyline { public string Kind { get; set; } public float[] Pts { get; set; } }
class Building { public string Id { get; set; } public float Height { get; set; } public string HeightSource { get; set; } public string Kind { get; set; } public float[] Pts { get; set; } }
class Polygon { public string Kind { get; set; } public float[] Pts { get; set; } }
class ModelCatalog { public string Source { get; set; } public ModelEntry[] Models { get; set; } }
class ModelEntry { public string Id { get; set; } public string Role { get; set; } public string Title { get; set; } public float SizeX { get; set; } public float SizeY { get; set; } public float SizeZ { get; set; } public int Triangles { get; set; } }
