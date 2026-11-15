using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DataPrep;
using P = (double x, double z);

// Dogpatch data extract: OSM (Overpass) + DataSF -> unity/Assets/Resources/World/dogpatch.json, data/extract-stats.json, data/preview.png, data/validation/*.png
// One command:  dotnet run --project tools/DataPrep -c Release -- [--refresh] [--models] [--no-validate]
// Freeway graph only (I-280 and its ramps -> unity/Assets/Resources/World/freeway.json):  ... -- --freeway
// Ground cover only (parking, yards, lots, grass, plazas, piers -> unity/Assets/Resources/World/ground.json):  ... -- --ground
// Scenery region beyond the map (footprints, streets, water -> unity/Assets/Resources/World/city-<name>.json):  ... -- --city missionbay
//
// Bounds (kept from the first extract): 37.7495..37.7645 N, -122.3965..-122.3800 W. YC (580 20th St, OSM node 11657169276) is >=400 m from every edge
// and the whole Pier 70 site up to the bay edge (coastline at lon -122.3816 at lat 37.7626) is inside, so the box was not expanded.
const double South = 37.7495, North = 37.7645, West = -122.3965, East = -122.3800;
const string BoundsReason = "Unchanged from the first extract. Contains the whole Pier 70 site to the bay edge and the YC campus (580 20th St, 37.76078 -122.38664) with >= 413 m margin to the nearest edge; no clipping to the official Dogpatch outline.";
const string Attribution = "Streets, props, landmarks (c) OpenStreetMap contributors (ODbL). DataSF (PDDL / CC0): Building Footprints ynuv-fyni, Parcels acdm-wktn, Land Use c5ge-t6pj, Assessor Secured Rolls wv5m-vpq2, Street Tree Inventory tkzw-k3nq, Traffic Signals ybh5-27n2, Muni Stops i28k-bkz6, Parking Meters 8vzz-qzz9, Bicycle Parking Racks hn4j-6fx5, Trash Can Locations 5buk-jp7b, Bay Area Bike Share Stations 7jbp-yzp3, Stop Signs 4542-gpa3, Rec & Park Assets e3jj-mbb3. Models: polyfork.dev.";

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var refresh = args.Contains("--refresh");
var root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, ".git"))) root = Path.GetDirectoryName(root) ?? throw new Exception("repo root not found");
string Rel(params string[] p) => Path.Combine(new[] { root }.Concat(p).ToArray());
var fetch = new Fetch(Rel("data", "cache"), refresh);
var warnings = new List<string>();

Geo.OriginLat = (South + North) / 2; Geo.OriginLon = (West + East) / 2;
var sw = Geo.Proj(South, West); var ne = Geo.Proj(North, East);
double minX = Math.Round(sw.x, 1), maxX = Math.Round(ne.x, 1), minZ = Math.Round(sw.z, 1), maxZ = Math.Round(ne.z, 1);
Geo.MinX = minX; Geo.MaxX = maxX; Geo.MinZ = minZ; Geo.MaxZ = maxZ;

float[] Flat(IEnumerable<P> pts) => pts.SelectMany(p => new[] { (float)Math.Round(p.x, 1), (float)Math.Round(p.z, 1) }).ToArray();
List<P> Pts(JsonNode way) => Geo.Dedupe(way["geometry"].AsArray().Where(g => g != null).Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])));
List<P> Ccw(List<P> r) { if (Geo.SignedArea(r) < 0) r.Reverse(); return r; }
bool Inside(P p) => Geo.Inside(p);
string Tag(JsonNode e, string k) => e["tags"]?[k]?.GetValue<string>();
int ParseInt(string s) => int.TryParse(s?.Split(';')[0].Trim(), out var v) ? v : 0;
string[] Kv(Dictionary<string, string> t, params string[] keys) => keys.Where(k => t.ContainsKey(k) && t[k] != "").Select(k => k + "=" + t[k]).ToArray();

// ---- OpenStreetMap, query 1: roads, rails, buildings, parks, trees, coastline (unchanged from the first extract)
const double Pad = 0.02;
string bbox = string.Join(",", South, West, North, East);
if (args.Contains("--freeway"))
{
    await FreewayPrep.Run(fetch, Rel("unity", "Assets", "Resources", "World", "freeway.json"), bbox, minX, maxX, minZ, maxZ);
    return;
}
if (args.Contains("--city"))
{
    var cityName = args[Array.IndexOf(args, "--city") + 1];
    await CityPrep.Run(fetch, cityName, Rel("unity", "Assets", "Resources", "World", $"city-{cityName}.json"));
    return;
}
if (args.Contains("--ground"))
{
    await GroundPrep.Run(fetch, Rel("unity", "Assets", "Resources", "World", "ground.json"), bbox, minX, maxX, minZ, maxZ);
    return;
}
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
Console.WriteLine("Fetching OSM (roads, buildings, trees)...");
var osm = (await fetch.Overpassql(query))["elements"].AsArray();

// ---- OpenStreetMap, query 2: props, landmarks, platforms, water, bridges
var query2 = $"""
[out:json][timeout:300];
(
node[highway~"^(street_lamp|traffic_signals|stop|give_way|crossing|bus_stop)$"]({bbox});
node[emergency=fire_hydrant]({bbox});
node[amenity~"^(bench|bicycle_parking|waste_basket|waste_disposal|recycling|post_box|drinking_water|bicycle_rental|charging_station|vending_machine|telephone|toilets|clock|fountain)$"]({bbox});
node[leisure=picnic_table]({bbox});
node[barrier=bollard]({bbox});
node[public_transport=platform]({bbox});
node[railway~"^(level_crossing|tram_stop|station|halt)$"]({bbox});
node[tourism=artwork]({bbox});
node[historic]({bbox});
node[man_made]({bbox});
node[advertising]({bbox});
node[power~"^(pole|tower|portal)$"]({bbox});
node[vending]({bbox});
node[name~"Y Combinator",i]({bbox});
way[man_made~"^(crane|chimney|smoke_stack|tower|water_tower|silo|storage_tank|mast|bridge|dry_dock|flagpole|gasometer)$"]({bbox});
way[historic][!building]({bbox});
way[tourism=artwork]({bbox});
way[advertising]({bbox});
way[amenity=bicycle_rental]({bbox});
way[waterway~"^(dock|dockyard)$"]({bbox});
way[natural~"^(water|bay)$"]({bbox});
way[railway~"^(platform|station|halt)$"]({bbox});
way[public_transport~"^(platform|station)$"]({bbox});
relation[natural=water]({bbox});
relation[man_made=bridge]({bbox});
);
out geom;
""";
Console.WriteLine("Fetching OSM (props, landmarks, platforms, water)...");
var osm2 = (await fetch.Overpassql(query2))["elements"].AsArray();

// tagged nodes that sit on road ways -> markIdx/markKind on roads
var marks = new Dictionary<(double, double), string>();
foreach (var e in osm2.Where(e => e["type"].GetValue<string>() == "node"))
{
    var k = PropBuilder.NodeKind(PropBuilder.Tags(e));
    if (k is "crossing" or "traffic_signal" or "stop_sign" or "give_way" or "level_crossing")
    {
        var p = Geo.Proj((double)e["lat"], (double)e["lon"]);
        marks[(Math.Round(p.x, 2), Math.Round(p.z, 2))] = k;
    }
}

var roadExclude = new HashSet<string> { "motorway", "motorway_link", "trunk_link", "construction", "proposed", "platform", "steps", "corridor", "elevator" };
var viaductKinds = new HashSet<string> { "motorway", "motorway_link", "trunk_link" };
var roadWidth = new Dictionary<string, float> { ["primary"] = 14, ["secondary"] = 12, ["tertiary"] = 11, ["residential"] = 9, ["unclassified"] = 9, ["living_street"] = 7, ["service"] = 5.5f, ["track"] = 4 };
var pathKinds = new HashSet<string> { "footway", "path", "pedestrian", "cycleway", "bridleway" };
var railOk = new HashSet<string> { "light_rail", "tram", "rail" };

var roads = new List<Road>(); var rails = new List<Polyline>(); var parks = new List<Polygon>();
var bridges = new List<Bridge>(); var viaducts = new List<Viaduct>(); var platforms = new List<Platform>(); var water = new List<Polygon>(); var coastLines = new List<Polyline>();
var osmTrees = new List<(P p, long id, Dictionary<string, string> tags)>();
var osmBuildings = new List<OsmBldg>();
var coast = new List<List<(double lat, double lon)>>();
var nodeWays = new Dictionary<long, List<(RoadWay w, int i)>>();

foreach (var e in osm)
{
    var type = e["type"].GetValue<string>();
    if (type == "node") { var p = Geo.Proj((double)e["lat"], (double)e["lon"]); if (Inside(p)) osmTrees.Add((p, e["id"].GetValue<long>(), PropBuilder.Tags(e))); continue; }
    if (e["geometry"] == null) continue;
    if (Tag(e, "natural") == "coastline")
    {
        var cl = e["geometry"].AsArray().Select(g => ((double)g["lat"], (double)g["lon"])).ToList();
        coast.Add(cl);
        foreach (var run in Geo.ClipLine(cl.Select(c => Geo.Proj(c.Item1, c.Item2)).ToList(), minX, maxX, minZ, maxZ)) coastLines.Add(new Polyline { Kind = "coastline", Pts = Flat(Geo.Dedupe(run)) });
        continue;
    }
    var pts = Pts(e);
    var hw = Tag(e, "highway"); var rw = Tag(e, "railway"); var bld = Tag(e, "building");
    long wayId = e["id"].GetValue<long>();
    string bridgeTag = Tag(e, "bridge"); bool isBridge = bridgeTag != null && bridgeTag != "no";
    int layer = ParseInt(Tag(e, "layer"));
    if (hw != null && !roadExclude.Contains(hw))
    {
        bool path = pathKinds.Contains(hw);
        float width = path ? 2.5f : roadWidth.GetValueOrDefault(hw, 5f);
        if (!path && float.TryParse(Tag(e, "width")?.Replace(" m", ""), out var tw) && tw >= 2 && tw <= 40) width = tw;
        var onew = Tag(e, "oneway");
        var ow = onew is "yes" or "1" or "true";
        var raw = e["geometry"].AsArray();
        if (raw.All(g => g != null) && e["nodes"] != null)
        {
            var rpts = raw.Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])).ToList(); var ids = e["nodes"].AsArray().Select(n => n.GetValue<long>()).ToList();
            if (onew == "-1") { rpts.Reverse(); ids.Reverse(); }
            var rw0 = new RoadWay { Id = wayId, Name = Tag(e, "name") ?? "", Kind = path ? "path" : hw, Oneway = ow || onew == "-1", Pts = rpts };
            for (int i = 0; i < ids.Count && i < rpts.Count; i++)
            {
                if (!nodeWays.TryGetValue(ids[i], out var l)) nodeWays[ids[i]] = l = new();
                l.Add((rw0, i));
            }
        }
        string cyc = Tag(e, "cycleway") ?? Tag(e, "cycleway:both") ?? Tag(e, "cycleway:right") ?? Tag(e, "cycleway:left");
        string sidewalk = Tag(e, "sidewalk") ?? Tag(e, "sidewalk:both");
        foreach (var run in Geo.ClipLine(pts, minX, maxX, minZ, maxZ))
        {
            var dd = Geo.Dedupe(run);
            var mi = new List<int>(); var mk = new List<string>();
            for (int i = 0; i < dd.Count; i++) if (marks.TryGetValue((Math.Round(dd[i].x, 2), Math.Round(dd[i].z, 2)), out var mkind)) { mi.Add(i); mk.Add(mkind); }
            roads.Add(new Road
            {
                Name = Tag(e, "name") ?? "", Kind = path ? "path" : hw, Width = width, Oneway = ow, Pts = Flat(dd),
                OsmId = wayId, Lanes = ParseInt(Tag(e, "lanes")), LanesForward = ParseInt(Tag(e, "lanes:forward")), LanesBackward = ParseInt(Tag(e, "lanes:backward")),
                TurnLanes = Tag(e, "turn:lanes") ?? "", TurnLanesForward = Tag(e, "turn:lanes:forward") ?? "", TurnLanesBackward = Tag(e, "turn:lanes:backward") ?? "",
                Maxspeed = Tag(e, "maxspeed") ?? "", Bridge = isBridge, Tunnel = Tag(e, "tunnel") is { } tn && tn != "no", Layer = layer,
                Surface = Tag(e, "surface") ?? "", Sidewalk = sidewalk ?? "", Cycleway = cyc ?? "", MarkIdx = mi.ToArray(), MarkKind = mk.ToArray(),
            });
            if (isBridge) bridges.Add(new Bridge { OsmId = wayId, Kind = "highway", Name = Tag(e, "name") ?? "", Layer = layer, Width = width, Tags = Kv(PropBuilder.Tags(e), "bridge", "bridge:structure", "bridge:support", "ref"), Pts = Flat(dd) });
        }
    }
    else if (hw != null && viaductKinds.Contains(hw) && (isBridge || layer >= 1))
    {
        var t = PropBuilder.Tags(e);
        int lanes = ParseInt(Tag(e, "lanes")); if (lanes == 0) lanes = hw == "motorway" ? 2 : 1;
        float vw = float.TryParse(Tag(e, "width")?.Replace(" m", ""), out var vww) && vww > 0 ? vww : lanes * 3.6f;
        foreach (var run in Geo.ClipLine(pts, minX, maxX, minZ, maxZ))
            viaducts.Add(new Viaduct { OsmId = wayId, Name = Tag(e, "name") ?? "", Kind = hw, Lanes = lanes, Width = vw, Layer = layer, Bridge = isBridge, Oneway = Tag(e, "oneway") is "yes" or "1" or "true", Maxspeed = Tag(e, "maxspeed") ?? "", Ref = Tag(e, "ref") ?? "", Pts = Flat(Geo.Dedupe(run)) });
    }
    else if (rw != null && railOk.Contains(rw))
    {
        foreach (var run in Geo.ClipLine(pts, minX, maxX, minZ, maxZ))
        {
            var dd = Geo.Dedupe(run);
            rails.Add(new Polyline { Kind = rw, Pts = Flat(dd) });
            if (isBridge) bridges.Add(new Bridge { OsmId = wayId, Kind = "railway", Name = Tag(e, "name") ?? "", Layer = layer, Width = 4, Tags = Kv(PropBuilder.Tags(e), "bridge", "bridge:structure", "railway", "ref"), Pts = Flat(dd) });
        }
    }
    else if (bld != null && pts.Count >= 4)
    {
        pts.RemoveAt(pts.Count - 1);
        float? h = null;
        if (float.TryParse(Tag(e, "height")?.Replace(" m", "").Replace("m", ""), out var hh)) h = hh;
        else if (float.TryParse(Tag(e, "building:levels"), out var lv)) h = lv * 3.5f;
        osmBuildings.Add(new OsmBldg { Id = wayId, Ring = pts, Kind = bld, Height = h, Tags = PropBuilder.Tags(e), Centroid = Geo.Centroid(pts), Area = Geo.Area(pts) });
    }
    else if ((Tag(e, "leisure") ?? Tag(e, "landuse")) is { } pk && pts.Count >= 4)
    {
        pts.RemoveAt(pts.Count - 1);
        var clipped = Geo.ClipPolygon(pts, minX, maxX, minZ, maxZ);
        if (clipped.Count >= 3 && Math.Abs(Geo.SignedArea(clipped)) >= 20) parks.Add(new Polygon { Kind = pk, Pts = Flat(Ccw(clipped)) });
    }
}

// ---- DataSF parcels / land use / assessor, joined to footprints by centroid containment
var parcels = await ParcelData.Load(fetch, North, West, South, East);

// ---- DataSF building footprints
Console.WriteLine("Fetching DataSF footprints...");
var sodaUrl = "https://data.sf.gov/resource/ynuv-fyni.json?$select=sf16_bldgid,hgt_median_m,gnd_min_m,shape&$where=" +
    Uri.EscapeDataString($"within_box(shape, {North}, {West}, {South}, {East})") + "&$limit=50000";
var soda = (await fetch.Json(sodaUrl)).AsArray();
var buildings = new List<Building>(); int noHeight = 0, tiny = 0, outside = 0;
var fpIndex = new PolyIndex<Building>(); var fpRing = new Dictionary<Building, List<P>>();
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
    // OSM match: the smallest OSM building polygon containing the centroid, else the largest OSM building whose centroid lies inside the footprint
    var match = osmBuildings.Where(b => Geo.PointInPoly(c0, b.Ring)).OrderBy(b => b.Area).FirstOrDefault()
             ?? osmBuildings.Where(b => Geo.PointInPoly(b.Centroid, best)).OrderByDescending(b => b.Area).FirstOrDefault();
    string source; float height;
    if (float.TryParse(f["hgt_median_m"]?.GetValue<string>(), out var dh) && dh >= 2.5f) { height = dh; source = "datasf"; }
    else if (match != null && match.Height is { } oh) { height = oh; source = "osm"; }
    else { height = 7; source = "default"; }
    if (source != "datasf") noHeight++;
    height = (float)Math.Round(Math.Clamp(height, 3, 150), 1);

    var pr = parcels.Lookup(c0);
    var ot = match?.Tags ?? new Dictionary<string, string>();
    float stories = pr.Stories > 0 ? pr.Stories : (float.TryParse(ot.GetValueOrDefault("building:levels"), out var lv2) ? lv2 : 0);
    float effStories = stories > 0 ? stories : (float)Math.Max(1, Math.Round(height / 3.3));
    bool retail = pr.Lu != null && pr.Lu.Retail > 0;
    var (btype, brule) = BuildingTypes.Classify(height, bestArea, effStories, pr.Year, pr.Use, pr.UseCode, pr.Units, retail, ot);
    var b = new Building
    {
        Id = f["sf16_bldgid"]?.GetValue<string>() ?? "", Height = height, HeightSource = source, Kind = match?.Kind ?? "yes", Pts = Flat(ringS),
        Type = btype, TypeRule = brule, Use = pr.Use, UseCode = pr.UseCode, UseDef = pr.UseDef, YearBuilt = pr.Year, Stories = stories, Units = pr.Units,
        Parcel = pr.Parcel?.Blklot ?? "", Zoning = pr.Zoning, Street = pr.Parcel?.Street ?? "", LandUseRaw = pr.LuRaw, AreaM2 = (float)Math.Round(bestArea, 1),
        OsmName = match?.Name ?? "", OsmId = match?.Id ?? 0,
        OsmTags = Kv(ot, "building", "building:levels", "roof:shape", "building:material", "building:colour", "roof:colour", "roof:material", "historic", "amenity", "shop", "office", "craft", "man_made", "power",
            "start_date", "height", "addr:housename", "addr:housenumber", "addr:street", "operator", "tourism", "leisure", "religion", "denomination", "wikidata", "layer"),
        Cottage = btype == "victorian" && effStories <= 2.01 && (pr.Units <= 2),
    };
    buildings.Add(b); fpIndex.Add(ringS, b); fpRing[b] = ringS;
}
Building FootprintAt(P p, double maxEdge = 25)
{
    var inside = fpIndex.Containing(p).Select(x => x.item).FirstOrDefault();
    if (inside != null) return inside;
    Building near = null; double nd = maxEdge;
    foreach (var (b, ring) in fpRing)
        for (int i = 0; i < ring.Count; i++)
        {
            var d = Geo.Nearest(p, ring[i], ring[(i + 1) % ring.Count]).dist;
            if (d < nd) { nd = d; near = b; }
        }
    return near;
}

// ---- Land from coastline (land on the left of way direction); bay water = the same stitching on reversed ways
var land = BuildLand(coast, minX, maxX, minZ, maxZ, out var landWarn);
if (landWarn != null) { Console.WriteLine("WARNING: " + landWarn); warnings.Add(landWarn); }
var bay = BuildLand(coast.Select(c => Enumerable.Reverse(c).ToList()).ToList(), minX, maxX, minZ, maxZ, out var bayWarn);
if (bayWarn != null) { Console.WriteLine("WARNING (bay): " + bayWarn); warnings.Add("bay: " + bayWarn); }
double rectArea = (maxX - minX) * (maxZ - minZ), landArea = land.Sum(l => Geo.Area(l)), bayArea = bay.Sum(l => Geo.Area(l));
if (Math.Abs(landArea + bayArea - rectArea) > rectArea * 0.02) { var w = $"land ({landArea:F0}) + bay ({bayArea:F0}) m2 differ from the bounds area ({rectArea:F0}); bay polygons dropped"; Console.WriteLine("WARNING: " + w); warnings.Add(w); bay = new(); }
foreach (var bp in bay) water.Add(new Polygon { Kind = "bay", Pts = Flat(bp) });

// ---- OSM query 2: platforms, bridges, water, props
var props = new PropBuilder(nodeWays, Inside);
List<List<P>> RelRings(JsonNode rel) => rel["members"]?.AsArray().Where(m => m["type"]?.GetValue<string>() == "way" && m["role"]?.GetValue<string>() == "outer" && m["geometry"] != null)
    .Select(m => Geo.Dedupe(m["geometry"].AsArray().Select(g => Geo.Proj((double)g["lat"], (double)g["lon"])))).ToList() ?? new();
foreach (var e in osm2)
{
    var type = e["type"].GetValue<string>(); var t = PropBuilder.Tags(e); long id = e["id"].GetValue<long>();
    if (type == "node") { props.AddOsmNode(e); continue; }
    string g(string k) => t.GetValueOrDefault(k) ?? "";
    if (type == "relation")
    {
        foreach (var ring in RelRings(e))
        {
            if (g("natural") == "water" && ring.Count >= 4 && Geo.Dist(ring[0], ring[^1]) < 1e-6) { var r2 = ring.Take(ring.Count - 1).ToList(); var cl = Geo.ClipPolygon(r2, minX, maxX, minZ, maxZ); if (cl.Count >= 3) water.Add(new Polygon { Kind = "water", Pts = Flat(Ccw(cl)) }); }
            else if (g("man_made") == "bridge") bridges.Add(new Bridge { OsmId = id, Kind = "man_made", Name = g("name"), Layer = ParseInt(g("layer")), Closed = ring.Count >= 4 && Geo.Dist(ring[0], ring[^1]) < 1e-6, Tags = Kv(t, "man_made", "ref", "start_date", "bridge:structure", "bridge:support"), Pts = Flat(ring) });
        }
        continue;
    }
    if (e["geometry"] == null) continue;
    var pts = Pts(e);
    bool closed = pts.Count >= 4 && Geo.Dist(pts[0], pts[^1]) < 1e-6;
    if (g("natural") is "water" or "bay")
    {
        if (closed) { var cl = Geo.ClipPolygon(pts.Take(pts.Count - 1).ToList(), minX, maxX, minZ, maxZ); if (cl.Count >= 3) water.Add(new Polygon { Kind = g("natural"), Pts = Flat(Ccw(cl)) }); }
        continue;
    }
    if (g("man_made") == "bridge")
    {
        bridges.Add(new Bridge { OsmId = id, Kind = "man_made", Name = g("name"), Layer = ParseInt(g("layer")), Closed = closed, Tags = Kv(t, "man_made", "ref", "start_date", "bridge:structure", "bridge:support"), Pts = Flat(pts) });
        continue;
    }
    if (g("railway") is "platform" or "station" or "halt" || g("public_transport") is "platform" or "station")
    {
        if (g("railway") == "platform" || g("public_transport") == "platform")
            platforms.Add(new Platform { OsmId = id, Name = g("name"), Operator = g("operator"), Network = g("network"), Kind = g("railway") == "platform" ? "railway" : "public_transport", Closed = closed,
                Tags = Kv(t, "ref", "shelter", "bench", "bin", "lit", "wheelchair", "tactile_paving", "height", "light_rail", "train", "cutting", "area"), Pts = Flat(pts) });
        continue;
    }
    props.AddOsmWay(e, pts);
}

// ---- Street trees
Console.WriteLine("Fetching DataSF street trees (tkzw-k3nq)...");
var treeRows = await fetch.Soda("tkzw-k3nq", "treeid,species,mapdbh,plotsize,siteinfo,legalstatus,planttype,latitude,longitude,onstreet",
    $"latitude > {South} AND latitude < {North} AND longitude > {West} AND longitude < {East}", "treeid");
var treeRecs = new List<TreeRecord>(); var treeDrops = new Dictionary<string, int>();
void Drop(string why) => treeDrops[why] = treeDrops.GetValueOrDefault(why) + 1;
foreach (var r in treeRows)
{
    if (!double.TryParse(r["latitude"]?.ToString(), out var la) || !double.TryParse(r["longitude"]?.ToString(), out var lo)) { Drop("no position"); continue; }
    var p = Geo.Proj(la, lo);
    if (!Inside(p)) { Drop("outside bounds"); continue; }
    var raw = r["species"]?.ToString() ?? "";
    var parts = raw.Split(" :: ", 2);
    string latin = parts[0].Trim(), common = parts.Length > 1 ? parts[1].Trim() : "";
    if (Regex.IsMatch(raw, "^(planting site|vacant|stump)", RegexOptions.IgnoreCase)) { Drop("empty planting site / stump (" + latin + ")"); continue; }
    double.TryParse(r["mapdbh"]?.ToString(), out var dbh);
    treeRecs.Add(new TreeRecord
    {
        Id = "datasf:tkzw-k3nq/" + r["treeid"], X = (float)Math.Round(p.x, 1), Z = (float)Math.Round(p.z, 1),
        Species = common != "" ? $"{common} ({latin})" : latin, Common = common, Latin = latin, Dbh = (float)dbh, DbhKnown = dbh > 0, PlotSize = r["plotsize"]?.ToString() ?? "", Source = "datasf",
        Tags = Kv(new Dictionary<string, string> { ["legalstatus"] = r["legalstatus"]?.ToString() ?? "", ["siteinfo"] = r["siteinfo"]?.ToString() ?? "", ["onstreet"] = r["onstreet"]?.ToString() ?? "", ["planttype"] = r["planttype"]?.ToString() ?? "" },
            "legalstatus", "siteinfo", "onstreet", "planttype"),
    });
}
int dsTrees = treeRecs.Count, osmTreeDup = 0;
var treeGrid = new Dictionary<(int, int), List<TreeRecord>>();
foreach (var tr in treeRecs) { var k = ((int)Math.Floor(tr.X / 4), (int)Math.Floor(tr.Z / 4)); if (!treeGrid.TryGetValue(k, out var l)) treeGrid[k] = l = new(); l.Add(tr); }
foreach (var (p, id, t) in osmTrees)
{
    int cx = (int)Math.Floor(p.x / 4), cz = (int)Math.Floor(p.z / 4);
    bool dup = false;
    for (int i = cx - 1; i <= cx + 1 && !dup; i++)
        for (int j = cz - 1; j <= cz + 1 && !dup; j++)
            if (treeGrid.TryGetValue((i, j), out var l)) dup = l.Any(o => Geo.Dist((o.X, o.Z), p) <= 2.0);
    if (dup) { osmTreeDup++; continue; }
    string latin = t.GetValueOrDefault("species") ?? t.GetValueOrDefault("taxon") ?? t.GetValueOrDefault("genus") ?? "";
    string common = t.GetValueOrDefault("species:en") ?? t.GetValueOrDefault("name") ?? "";
    double dbhIn = 0;
    if (double.TryParse(t.GetValueOrDefault("circumference")?.Replace("m", "").Trim(), out var circ) && circ > 0) dbhIn = circ / Math.PI * 39.3701;
    if (double.TryParse(t.GetValueOrDefault("diameter")?.Replace("m", "").Trim(), out var dia) && dia > 0 && dbhIn == 0) dbhIn = dia * 39.3701;
    treeRecs.Add(new TreeRecord
    {
        Id = "osm:node/" + id, X = (float)Math.Round(p.x, 1), Z = (float)Math.Round(p.z, 1), Species = common != "" && latin != "" ? $"{common} ({latin})" : (common + latin),
        Common = common, Latin = latin, Dbh = (float)Math.Round(dbhIn, 1), DbhKnown = dbhIn > 0, PlotSize = "", Source = "osm",
        Tags = Kv(t, "height", "leaf_type", "leaf_cycle", "genus", "denotation", "circumference"),
    });
}

// ---- DataSF point datasets -> props
async Task Ds(string dataset, string select, string geo, string idCol, Func<JsonNode, (string kind, string name, (string, string)[] tags, double? heading, string hsrc)?> map)
{
    Console.WriteLine($"Fetching DataSF {dataset}...");
    JsonArray rows;
    try { rows = await fetch.Soda(dataset, select, $"within_box({geo}, {North}, {West}, {South}, {East})", idCol); }
    catch (Exception ex) { Console.WriteLine("  failed: " + ex.Message); warnings.Add($"DataSF {dataset} failed: {ex.Message}"); return; }
    foreach (var r in rows)
    {
        var c = r[geo]?["coordinates"]?.AsArray();
        var m = map(r);
        if (c == null || m == null) continue;
        var pc = props.AddDataSf(dataset, r[idCol]?.ToString() ?? Guid.NewGuid().ToString("N")[..8], m.Value.kind, (double)c[1], (double)c[0], m.Value.name, m.Value.tags);
        if (pc != null && m.Value.heading != null) { pc.Heading = m.Value.heading; pc.HeadingSrc = m.Value.hsrc; }
    }
}
string S(JsonNode r, string k) => r[k]?.ToString() ?? "";
await Ds("ybh5-27n2", "objectid,street1,street2,type,ped_signal,startyear,sig_num,shape", "shape", "objectid",
    r => ("traffic_signal", S(r, "street1") + " & " + S(r, "street2"), new[] { ("street1", S(r, "street1")), ("street2", S(r, "street2")), ("type", S(r, "type")), ("ped_signal", S(r, "ped_signal")), ("startyear", S(r, "startyear")), ("onRoad", "yes") }, null, null));
await Ds("i28k-bkz6", "objectid,stopname,onstreet,atstreet,position,orientation,shelter,stopid,shape", "shape", "objectid",
    r => ("bus_stop", S(r, "stopname"), new[] { ("stopid", S(r, "stopid")), ("onstreet", S(r, "onstreet")), ("atstreet", S(r, "atstreet")), ("position", S(r, "position")), ("orientation", S(r, "orientation")), ("shelter", S(r, "shelter")), ("network", "Muni") }, null, null));
await Ds("8vzz-qzz9", "objectid,post_id,street_name,street_num,meter_type,cap_color,active_meter_flag,on_offstreet_type,shape", "shape", "objectid",
    r => ("parking_meter", "", new[] { ("post_id", S(r, "post_id")), ("street", S(r, "street_name")), ("street_num", S(r, "street_num")), ("meter_type", S(r, "meter_type")), ("cap_color", S(r, "cap_color")), ("active", S(r, "active_meter_flag")), ("onoff", S(r, "on_offstreet_type")) }, null, null));
await Ds("hn4j-6fx5", "objectid,address,location,placement,racks,spaces,install_yr,shape", "shape", "objectid",
    r => ("bike_rack", S(r, "location"), new[] { ("address", S(r, "address")), ("placement", S(r, "placement")), ("racks", S(r, "racks")), ("spaces", S(r, "spaces")), ("install_yr", S(r, "install_yr")) }, null, null));
await Ds("5buk-jp7b", "objectid,objectcode,objectdescription,cantype,cancolor,onstreet,streetcorner,point", "point", "objectid",
    r => ("trash_can", S(r, "objectdescription"), new[] { ("can_type", S(r, "cantype")), ("color", S(r, "cancolor")), ("onstreet", S(r, "onstreet")), ("corner", S(r, "streetcorner")) }, null, null));
await Ds("7jbp-yzp3", "objectid,station_name,dock_count,has_kiosk,shape", "shape", "objectid",
    r => ("bicycle_rental", S(r, "station_name"), new[] { ("capacity", S(r, "dock_count")), ("kiosk", S(r, "has_kiosk")), ("network", "Bay Area Bike Share (legacy, 2024 snapshot)") }, null, null));
await Ds("4542-gpa3", "objectid,street,x_street,direction,st_facing,status,shape", "shape", "objectid",
    r =>
    {
        double? h = S(r, "st_facing") switch { "NB" => 180, "SB" => 0, "EB" => 270, "WB" => 90, _ => null };
        return ("stop_sign", S(r, "street") + " & " + S(r, "x_street"), new[] { ("street", S(r, "street")), ("x_street", S(r, "x_street")), ("corner", S(r, "direction")), ("facing", S(r, "st_facing")), ("status", S(r, "status")) }, h, "datasf_st_facing");
    });
await Ds("e3jj-mbb3", "objectid,asset_id,asset_type,asset_subtype,tma_asset_name,shape", "shape", "objectid",
    r =>
    {
        string kind = S(r, "asset_type") switch
        {
            "Bench" => "bench", "Lamp Post" => "street_lamp", "Drinking Fountain" => "drinking_water", "Bicycle Rack" => "bike_rack", "Trash Can" => "trash_can",
            "Bag Dispenser" => "dog_bag_dispenser", "Table" => "picnic_table", _ => null,
        };
        return kind == null ? null : (kind, S(r, "tma_asset_name"), new[] { ("asset_type", S(r, "asset_type")), ("asset_subtype", S(r, "asset_subtype")), ("asset_id", S(r, "asset_id")), ("owner", "SF Recreation and Parks") }, null, null);
    });
int deduped = props.DedupeAcrossSources(1.5);

// ---- Landmarks built from buildings (YC, Pier 70 machine shop, cream stone offices)
PropC BuildingLandmark(string kind, OsmBldg ob, string notes)
{
    var fps = fpIndex.Containing(ob.Centroid).Select(x => x.item).ToList();
    if (fps.Count == 0) fps = buildings.Where(b => Geo.PointInPoly(Geo.Centroid(fpRing[b]), ob.Ring)).ToList();
    var main = fps.OrderByDescending(b => b.AreaM2).FirstOrDefault();
    var ring = ob.Ring;
    var cen = ob.Centroid;
    var c = new PropC { Kind = kind, Source = "osm", Id = "osm:way/" + ob.Id, OsmId = "way/" + ob.Id, X = cen.x, Z = cen.z, Landmark = true, Name = ob.Name ?? "", Closed = true,
        Pts = Flat(ring), FootprintIds = fps.Select(b => b.Id).ToArray(), HeightM = main?.Height ?? ob.Height, Notes = notes };
    c.Heading = Geo.LongAxis(ring).bearing; c.HeadingSrc = "footprint_long_axis";
    foreach (var k in new[] { "addr:housename", "addr:housenumber", "addr:street", "historic", "start_date", "wikidata" }) c.Tag(k, ob.Tags.GetValueOrDefault(k));
    return c;
}
foreach (var ob in osmBuildings)
{
    string hn = ob.Tags.GetValueOrDefault("addr:housename") ?? "";
    if (ob.Name == "Machine Shop") props.Items.Add(BuildingLandmark("machine_shop", ob, "Union Iron Works machine shop, " + hn + "; OSM way " + ob.Id));
    else if (hn is "Building #101" or "Building #104") props.Items.Add(BuildingLandmark("cream_stone", ob, "Pier 70 " + hn + " (" + ob.Name + "); OSM way " + ob.Id));
}
foreach (var c in props.Items.Where(c => c.Kind == "yc"))
{
    var p = (c.X, c.Z);
    var fp = FootprintAt(p, 25);
    var ob = osmBuildings.Where(b => Geo.PointInPoly(p, b.Ring)).OrderBy(b => b.Area).FirstOrDefault();
    if (fp != null) { c.FootprintIds = new[] { fp.Id }; c.HeightM = fp.Height; c.Tag("footprintLongAxis", Math.Round(Geo.LongAxis(fpRing[fp]).bearing).ToString()); }
    c.Pts = Flat(ob?.Ring ?? (fp != null ? fpRing[fp] : new List<P>())); c.Closed = c.Pts.Length > 0;
    if (ob != null) { c.Tag("osmBuilding", "way/" + ob.Id); c.Tag("osmBuildingName", ob.Name); c.Tag("osmHousename", ob.Tags.GetValueOrDefault("addr:housename")); }
    c.Notes = "Y Combinator campus (race finish). OSM office node; address 580 20th Street. " + (ob != null ? "Inside OSM building '" + ob.Name + "' (" + ob.Tags.GetValueOrDefault("addr:housename") + ", way " + ob.Id + "); pts = that OSM outline. " : "") + (fp != null ? "DataSF footprint " + fp.Id + " (" + fp.AreaM2 + " m2) is a merged polygon that also covers neighbouring Pier 70 buildings." : "");
}
foreach (var c in props.Items.Where(c => c.Kind == "caltrain_22nd"))
{
    var fp = FootprintAt((c.X, c.Z), 25);
    if (fp != null) { c.FootprintIds = new[] { fp.Id }; c.Tag("nearestFootprintId", fp.Id); }
    var pl = platforms.Where(p => p.Network == "Caltrain").Select(p => p.OsmId).ToList();
    c.Notes = "22nd Street Caltrain station (race start). Platforms (platforms[]): way " + string.Join(", ", pl) + ". Tracks: rails[] kind=rail.";
}
foreach (var c in props.Items.Where(c => c.Kind == "crane"))
    c.Notes = c.Source == "osm" && c.Id.Contains("way/") ? "Crane Cove Park historic gantry crane (OSM does not say whether Crane 14 or Crane 30); OSM maps only the roughly square crane base (6-10 m), so heading is the base outline orientation (ambiguous modulo 90 degrees), the jib direction is not in OSM" : "Port crane (OSM node)";
foreach (var c in props.Items.Where(c => c.Kind == "smokestack"))
{
    var fp = FootprintAt((c.X, c.Z), 10);
    if (fp != null) { c.FootprintIds = new[] { fp.Id }; }
    c.Notes = "Potrero Power Station boiler stack (OSM man_made=smoke_stack)";
}
foreach (var c in props.Items.Where(c => c.Kind == "drydock"))
    c.Notes = "OSM has no man_made=dry_dock at Pier 70; this is a waterway=dock way (floating dock / Pier 2 area)";
foreach (var c in props.Items.Where(c => c.Kind == "bicycle_rental" && c.Source == "osm")) c.Landmark = true;
foreach (var c in props.Items.Where(c => c.Kind == "billboard")) c.Landmark = true;

// ---- Finalize props: nearest road, heading fallback = face the street
var roadIndex = new RoadIndex(roads);
var propsOut = new List<Prop>(); var landmarks = new List<Landmark>();
foreach (var c in props.Items.OrderBy(c => c.Kind).ThenBy(c => c.Source).ThenBy(c => c.Id))
{
    var near = roadIndex.Nearest((c.X, c.Z));
    float dist = near != null ? (float)near.Value.dist : -1; string nname = near?.name ?? "";
    if (c.Heading == null && near != null && !PropBuilder.NoFacing.Contains(c.Kind))
    {
        if (near.Value.dist < 0.3) { c.Heading = near.Value.segBearing; c.HeadingSrc = "road_dir"; }
        else { c.Heading = Geo.Bearing((c.X, c.Z), near.Value.pt); c.HeadingSrc = "faces_road"; }
    }
    var prop = new Prop
    {
        Id = c.Id, AltId = c.AltId, Kind = c.Kind, Name = c.Name, X = (float)Math.Round(c.X, 2), Z = (float)Math.Round(c.Z, 2), Heading = (float)Math.Round(c.Heading ?? 0, 1), HeadingKnown = c.Heading != null,
        HeadingSource = c.HeadingSrc, Source = c.Source, DistToRoad = (float)Math.Round(dist, 1), NearestRoad = nname, Tags = c.Tags.ToArray(),
    };
    propsOut.Add(prop);
    if (c.Landmark)
    {
        var ll = Geo.Unproj((c.X, c.Z));
        landmarks.Add(new Landmark
        {
            Kind = c.Kind == "bicycle_rental" ? "bike_share" : c.Kind, Name = c.Name, X = prop.X, Z = prop.Z, Lat = Math.Round(ll.lat, 6), Lon = Math.Round(ll.lon, 6), Heading = prop.Heading, HeadingKnown = prop.HeadingKnown,
            FootprintId = c.FootprintIds?.FirstOrDefault() ?? "", FootprintIds = c.FootprintIds ?? new string[0], OsmId = c.OsmId, HeightM = (float)(c.HeightM ?? 0), HeightKnown = c.HeightM != null,
            Pts = c.Pts ?? new float[0], Closed = c.Closed, Notes = c.Notes,
        });
    }
}
// Landmark view of viaducts / platforms are separate top-level arrays (viaducts[], platforms[])

var world = new WorldData
{
    Name = "Dogpatch", OriginLat = Geo.OriginLat, OriginLon = Geo.OriginLon,
    MinX = (float)minX, MaxX = (float)maxX, MinZ = (float)minZ, MaxZ = (float)maxZ, South = South, North = North, West = West, East = East,
    Attribution = Attribution, Roads = roads.ToArray(), Rails = rails.ToArray(), Buildings = buildings.ToArray(),
    Parks = parks.ToArray(), Land = land.Select(l => new Polygon { Kind = "land", Pts = Flat(l) }).ToArray(),
    Trees = treeRecs.SelectMany(t => new[] { t.X, t.Z }).ToArray(), TreeRecords = treeRecs.ToArray(), Props = propsOut.ToArray(), Landmarks = landmarks.ToArray(),
    Bridges = bridges.ToArray(), Viaducts = viaducts.ToArray(), Platforms = platforms.ToArray(), Water = water.ToArray(), Coastline = coastLines.ToArray(),
};
var jopts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var worldPath = Rel("unity", "Assets", "Resources", "World", "dogpatch.json");
Directory.CreateDirectory(Path.GetDirectoryName(worldPath));
File.WriteAllText(worldPath, JsonSerializer.Serialize(world, jopts));

// ---- Polyfork models (only with --models; the parent agent owns model choice and unity/Assets/Resources/Polyfork)
ModelEntry[] entries = Array.Empty<ModelEntry>();
if (args.Contains("--models"))
{
    Console.WriteLine("Fetching Polyfork models...");
    var modelsDir = Rel("unity", "Assets", "Resources", "Polyfork");
    Directory.CreateDirectory(modelsDir);
    var catalog = new ModelCatalog { Source = "https://polyfork.dev" };
    var list = new List<ModelEntry>();
    foreach (var (role, ids) in JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Rel("tools", "DataPrep", "models.json"))))
        foreach (var id in ids)
        {
            File.WriteAllBytes(Path.Combine(modelsDir, id + ".bytes"), await fetch.Get($"https://polyfork.dev/cdn/{id}.glb"));
            var meta = await fetch.Json($"https://polyfork.dev/api/assets/{id}");
            list.Add(new ModelEntry { Id = id, Role = role, Title = (string)meta["title"], SizeX = (float)meta["size_m"]["x"], SizeY = (float)meta["size_m"]["y"], SizeZ = (float)meta["size_m"]["z"], Triangles = (int)meta["triangles"] });
        }
    entries = list.ToArray(); catalog.Models = entries;
    File.WriteAllText(Path.Combine(modelsDir, "catalog.json"), JsonSerializer.Serialize(catalog, jopts));
}

// ---- Preview + validation overlays
var previewPath = Rel("data", "preview.png");
Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
Render.Preview(world, previewPath);
var validation = new List<string>();
if (!args.Contains("--no-validate"))
{
    try { validation = await Validate.Run(world, fetch, Rel("data", "validation")); }
    catch (Exception ex) { Console.WriteLine("Validation overlays failed: " + ex.Message); warnings.Add("validation overlays failed: " + ex.Message); }
}

// ---- Stats
var jo = new JsonSerializerOptions(jopts) { WriteIndented = true };
JsonObject Counts<T>(IEnumerable<T> src, Func<T, string> key) { var o = new JsonObject(); foreach (var g in src.GroupBy(key).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)) o[g.Key] = g.Count(); return o; }
var topSpecies = new JsonArray();
foreach (var g in treeRecs.GroupBy(t => t.Species == "" ? "(unknown)" : t.Species).OrderByDescending(g => g.Count()).Take(15)) topSpecies.Add(new JsonObject { ["species"] = g.Key, ["count"] = g.Count() });
var propKinds = new JsonObject();
foreach (var g in propsOut.GroupBy(p => p.Kind).OrderByDescending(g => g.Count()))
    propKinds[g.Key] = new JsonObject { ["total"] = g.Count(), ["datasf"] = g.Count(p => p.Source == "datasf"), ["osm"] = g.Count(p => p.Source == "osm") };
var stats = new JsonObject
{
    ["bounds"] = new JsonObject
    {
        ["south"] = South, ["north"] = North, ["west"] = West, ["east"] = East, ["minX"] = minX, ["maxX"] = maxX, ["minZ"] = minZ, ["maxZ"] = maxZ,
        ["originLat"] = Geo.OriginLat, ["originLon"] = Geo.OriginLon, ["reason"] = BoundsReason,
    },
    ["sources"] = new JsonObject
    {
        ["osm"] = "Overpass API (ODbL)",
        ["datasf"] = new JsonObject
        {
            ["ynuv-fyni"] = "Building Footprints", ["acdm-wktn"] = "Parcels - Active and Retired", ["c5ge-t6pj"] = "San Francisco Land Use", ["wv5m-vpq2"] = "Assessor Historical Secured Property Tax Rolls (2025)",
            ["tkzw-k3nq"] = "San Francisco Street Tree Inventory", ["ybh5-27n2"] = "Traffic Signals", ["i28k-bkz6"] = "Muni Stops", ["8vzz-qzz9"] = "Parking Meters", ["hn4j-6fx5"] = "Bicycle Parking Racks",
            ["5buk-jp7b"] = "Trash Can Locations", ["7jbp-yzp3"] = "Bay Area Bike Share Stations", ["4542-gpa3"] = "Stop Signs", ["e3jj-mbb3"] = "Assets Maintained by the Recreation and Parks Department",
        },
    },
    ["world"] = new JsonObject
    {
        ["roads"] = roads.Count, ["rails"] = rails.Count, ["parks"] = parks.Count, ["landPolygons"] = land.Count, ["bridges"] = bridges.Count, ["viaducts"] = viaducts.Count, ["platforms"] = platforms.Count,
        ["waterPolygons"] = water.Count, ["coastlineRuns"] = coastLines.Count, ["landmarks"] = landmarks.Count,
    },
    ["buildings"] = new JsonObject
    {
        ["total"] = buildings.Count, ["datasfRows"] = soda.Count, ["outsideBounds"] = outside, ["under12m2"] = tiny,
        ["heightSource"] = Counts(buildings, b => b.HeightSource), ["byType"] = Counts(buildings, b => b.Type), ["byTypeRule"] = Counts(buildings, b => b.TypeRule), ["byUse"] = Counts(buildings, b => b.Use),
        ["byAssessorUse"] = Counts(buildings, b => b.UseCode == "" ? "(none)" : b.UseCode),
        ["withParcel"] = buildings.Count(b => b.Parcel != ""), ["withYearBuilt"] = buildings.Count(b => b.YearBuilt > 0), ["withStories"] = buildings.Count(b => b.Stories > 0), ["withUnits"] = buildings.Count(b => b.Units > 0),
        ["withOsmMatch"] = buildings.Count(b => b.OsmId != 0), ["withOsmName"] = buildings.Count(b => b.OsmName != ""), ["cottages"] = buildings.Count(b => b.Cottage),
        ["parcelRows"] = parcels.ParcelCount, ["landUseRows"] = parcels.LandUseCount, ["assessorRows"] = parcels.AssessorCount, ["assessorRowsUnmatched"] = parcels.AssessorUnmatched,
    },
    ["trees"] = new JsonObject
    {
        ["total"] = treeRecs.Count, ["bySource"] = Counts(treeRecs, t => t.Source), ["osmDroppedAsDuplicateOfDatasf"] = osmTreeDup, ["osmNodes"] = osmTrees.Count, ["withDbh"] = treeRecs.Count(t => t.DbhKnown),
        ["dropped"] = Counts(treeDrops, kv => kv.Key), ["top15Species"] = topSpecies,
    },
    ["props"] = new JsonObject { ["total"] = propsOut.Count, ["bySource"] = Counts(propsOut, p => p.Source), ["byKind"] = propKinds, ["dedupedAcrossSources"] = deduped, ["droppedOutsideBounds"] = props.Dropped,
        ["headingSource"] = Counts(propsOut, p => p.HeadingSource) },
    ["landmarks"] = new JsonArray(landmarks.Select(l => (JsonNode)new JsonObject { ["kind"] = l.Kind, ["name"] = l.Name, ["x"] = l.X, ["z"] = l.Z, ["lat"] = l.Lat, ["lon"] = l.Lon, ["footprintId"] = l.FootprintId, ["osmId"] = l.OsmId, ["heightM"] = l.HeightKnown ? l.HeightM : null }).ToArray()),
    ["validationOverlays"] = new JsonArray(validation.Select(v => (JsonNode)v).ToArray()),
    ["warnings"] = new JsonArray(warnings.Concat(fetch.Failures).Select(w => (JsonNode)w).ToArray()),
};
var statsPath = Rel("data", "extract-stats.json");
File.WriteAllText(statsPath, stats.ToJsonString(jo) + "\n");

Console.WriteLine($"""

== Summary ==
bounds: S {South} N {North} W {West} E {East}  ({BoundsReason})
roads: {roads.Count}   rails: {rails.Count}   parks: {parks.Count}   land polygons: {land.Count}   bay polygons: {bay.Count}   water polys: {water.Count - bay.Count}
bridges: {bridges.Count}   viaducts: {viaducts.Count}   platforms: {platforms.Count}   landmarks: {landmarks.Count}
buildings: {buildings.Count} (height datasf {buildings.Count(b => b.HeightSource == "datasf")}, osm {buildings.Count(b => b.HeightSource == "osm")}, default {buildings.Count(b => b.HeightSource == "default")}); DataSF rows {soda.Count}, outside {outside}, <12 m2 {tiny}
  with parcel {buildings.Count(b => b.Parcel != "")}, year {buildings.Count(b => b.YearBuilt > 0)}, stories {buildings.Count(b => b.Stories > 0)}, OSM match {buildings.Count(b => b.OsmId != 0)}, cottages {buildings.Count(b => b.Cottage)}
  by type: {string.Join(", ", buildings.GroupBy(b => b.Type).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count()))}
trees: {treeRecs.Count} (datasf {dsTrees}, osm {treeRecs.Count - dsTrees}; {osmTreeDup} OSM trees within 2 m of a DataSF tree dropped; dropped DataSF rows: {string.Join(", ", treeDrops.Select(kv => kv.Key + " " + kv.Value))})
  top species: {string.Join("; ", topSpecies.Select(s => s["species"] + " " + s["count"]))}
props: {propsOut.Count} (datasf {propsOut.Count(p => p.Source == "datasf")}, osm {propsOut.Count(p => p.Source == "osm")}; {deduped} merged across sources; {props.Dropped} outside bounds)
  by kind: {string.Join(", ", propsOut.GroupBy(p => p.Kind).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count() + " (ds " + g.Count(p => p.Source == "datasf") + ")"))}
models: {entries.Length}   network requests this run: {fetch.Requests}   warnings: {warnings.Count + fetch.Failures.Count}
dogpatch.json: {new FileInfo(worldPath).Length / 1024} KB   preview.png: {new FileInfo(previewPath).Length / 1024} KB   extract-stats.json: {new FileInfo(statsPath).Length / 1024} KB
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
