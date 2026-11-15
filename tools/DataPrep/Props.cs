using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using P = (double x, double z);

namespace DataPrep;

class RoadWay { public long Id; public string Name, Kind; public bool Oneway; public List<P> Pts; }

class PropC
{
    public string Id, AltId = "", Kind, Name = "", Source, HeadingSrc = "none", Notes = "", OsmId = "";
    public double X, Z; public double? Heading;
    public List<string> Tags = new();
    public bool Landmark, Closed; public float[] Pts; public string[] FootprintIds; public double? HeightM;
    public void Tag(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) Tags.Add(k + "=" + v.Trim()); }
}

/// <summary>Collects prop candidates from OSM nodes/ways and DataSF point datasets, dedupes across sources, derives headings.</summary>
class PropBuilder
{
    public readonly List<PropC> Items = new();
    public int Dropped;
    readonly Dictionary<long, List<(RoadWay w, int i)>> nodeWays;
    readonly Func<P, bool> inside;

    public PropBuilder(Dictionary<long, List<(RoadWay w, int i)>> nodeWays, Func<P, bool> inside) { this.nodeWays = nodeWays; this.inside = inside; }

    static readonly HashSet<string> TagKeys = new()
    {
        "name", "ref", "operator", "network", "brand", "capacity", "direction", "camera:direction", "support", "lit", "shelter", "bench", "bin", "covered", "material", "colour", "height", "artwork_type",
        "artist_name", "crossing", "crossing:markings", "crossing:island", "traffic_signals", "traffic_signals:direction", "button_operated", "fire_hydrant:type", "fire_hydrant:position",
        "fire_hydrant:diameter", "vending", "historic", "man_made", "start_date", "wheelchair", "tactile_paving", "light_rail", "route_ref", "bicycle_parking", "socket:type2", "advertising",
        "visibility", "bicycle_rental", "dock", "station", "train", "cutting", "frequency", "voltage", "description", "bus", "public_transport", "highway", "amenity", "stop", "traffic_calming",
    };
    static readonly string[] CompassNames = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

    public static Dictionary<string, string> Tags(JsonNode e)
    {
        var d = new Dictionary<string, string>();
        if (e["tags"] is JsonObject o) foreach (var kv in o) d[kv.Key] = kv.Value?.ToString() ?? "";
        return d;
    }

    static double? ParseDirection(Dictionary<string, string> t)
    {
        foreach (var k in new[] { "direction", "camera:direction" })
        {
            if (!t.TryGetValue(k, out var v)) continue;
            v = v.Split(';')[0].Trim();
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return Geo.Norm360(d);
            int idx = Array.IndexOf(CompassNames, v.ToUpperInvariant());
            if (idx >= 0) return idx * 22.5;
        }
        return null;
    }

    public static string NodeKind(Dictionary<string, string> t)
    {
        string g(string k) => t.GetValueOrDefault(k) ?? "";
        if (Regex.IsMatch(g("name"), "Y Combinator", RegexOptions.IgnoreCase) && g("office") != "") return "yc";
        switch (g("highway"))
        {
            case "street_lamp": return "street_lamp";
            case "traffic_signals": return "traffic_signal";
            case "stop": return "stop_sign";
            case "give_way": return "give_way";
            case "crossing": return "crossing";
            case "bus_stop": return "bus_stop";
        }
        if (g("emergency") == "fire_hydrant") return "fire_hydrant";
        switch (g("amenity"))
        {
            case "bench": return "bench";
            case "bicycle_parking": return "bike_rack";
            case "waste_basket": return "trash_can";
            case "waste_disposal": return "dumpster";
            case "recycling": return "recycling";
            case "post_box": return "post_box";
            case "drinking_water": return "drinking_water";
            case "bicycle_rental": return "bicycle_rental";
            case "charging_station": return "charging_station";
            case "vending_machine": return "vending";
            case "telephone": return "telephone";
            case "toilets": return "toilets";
            case "clock": return "clock";
            case "fountain": return "fountain";
        }
        if (g("leisure") == "picnic_table") return "picnic_table";
        if (g("barrier") == "bollard") return "bollard";
        if (g("railway") == "level_crossing") return "level_crossing";
        if (g("railway") is "station" or "halt" or "tram_stop")
            return g("network") == "Caltrain" && g("railway") == "station" ? "caltrain_22nd" : "transit_station";
        if (g("public_transport") == "platform") return "bus_stop";
        if (g("tourism") == "artwork") return "artwork";
        if (g("advertising") == "billboard") return "billboard";
        if (g("advertising") != "") return "ad_column";
        if (g("power") == "pole" || g("man_made") == "utility_pole") return "power_pole";
        if (g("power") is "tower" or "portal") return "power_tower";
        if (g("vending") != "") return "vending";
        switch (g("man_made"))
        {
            case "crane": return "crane";
            case "chimney": case "smoke_stack": return "smokestack";
            case "tower": case "water_tower": case "silo": case "storage_tank": case "mast": case "flagpole": case "pumping_station": case "street_cabinet": return g("man_made");
            case "dry_dock": return "drydock";
        }
        if (g("historic") != "") return "historic";
        return null;
    }

    public static string WayKind(Dictionary<string, string> t)
    {
        string g(string k) => t.GetValueOrDefault(k) ?? "";
        if (g("man_made") == "crane" || g("historic") == "crane") return "crane";
        if (g("man_made") is "chimney" or "smoke_stack") return "smokestack";
        if (g("man_made") == "dry_dock" || g("waterway") == "dock" || g("waterway") == "dockyard") return "drydock";
        if (g("man_made") is "tower" or "water_tower" or "silo" or "storage_tank" or "mast" or "flagpole" or "gasometer") return g("man_made");
        if (g("amenity") == "bicycle_rental") return "bicycle_rental";
        if (g("advertising") == "billboard") return "billboard";
        if (g("advertising") != "") return "ad_column";
        if (g("tourism") == "artwork") return "artwork";
        if (g("historic") != "" && g("building") == "") return "historic";
        return null;
    }

    public static readonly HashSet<string> NoFacing = new() { "crane", "smokestack", "drydock", "historic", "power_tower", "pumping_station", "tower", "water_tower", "silo", "storage_tank", "mast", "street_cabinet", "transit_station" };
    static readonly HashSet<string> LandmarkKinds = new() { "yc", "crane", "smokestack", "caltrain_22nd", "drydock", "bicycle_rental", "billboard" };

    PropC Make(string kind, string source, string id, P p) => new PropC { Kind = kind, Source = source, Id = id, X = p.x, Z = p.z, Landmark = LandmarkKinds.Contains(kind) };

    void AddTags(PropC c, Dictionary<string, string> t)
    {
        foreach (var k in TagKeys) if (t.TryGetValue(k, out var v)) c.Tag(k, v);
        if (t.TryGetValue("name", out var n)) c.Name = n;
    }

    public void AddOsmNode(JsonNode e)
    {
        var t = Tags(e);
        var kind = NodeKind(t);
        if (kind == null) return;
        var p = Geo.Proj((double)e["lat"], (double)e["lon"]);
        if (!inside(p)) { Dropped++; return; }
        long id = e["id"].GetValue<long>();
        var c = Make(kind, "osm", "osm:node/" + id, p); c.OsmId = "node/" + id;
        AddTags(c, t);
        if (ParseDirection(t) is { } dir) { c.Heading = dir; c.HeadingSrc = "osm_direction"; }
        if (nodeWays.TryGetValue(id, out var refs))
        {
            var roadRefs = refs.Where(r => r.w.Kind != "path").ToList();
            if (roadRefs.Count > 0 || kind is "crossing")
            {
                c.Tag("onRoad", "yes");
                if (kind is "stop_sign" or "give_way" or "traffic_signal") ApproachHeading(c, t, p, roadRefs);
                else if (kind is "crossing" or "level_crossing") CrossingHeading(c, p, roadRefs.Count > 0 ? roadRefs : refs);
            }
        }
        else if (kind is "stop_sign" or "give_way" or "traffic_signal" or "crossing") c.Tag("onRoad", "unknown");
        Items.Add(c);
    }

    void ApproachHeading(PropC c, Dictionary<string, string> t, P p, List<(RoadWay w, int i)> refs)
    {
        t.TryGetValue("direction", out var dirTag);
        t.TryGetValue("traffic_signals:direction", out var sigDir);
        var dt = dirTag ?? sigDir;
        var heads = new List<double>();
        foreach (var (w, i) in refs)
        {
            bool fwd = i > 0, back = i < w.Pts.Count - 1 && !w.Oneway;
            if (dt == "forward") back = false;
            else if (dt == "backward") { fwd = false; back = i < w.Pts.Count - 1; }
            if (fwd) heads.Add(Geo.Bearing(p, w.Pts[i - 1]));
            if (back) heads.Add(Geo.Bearing(p, w.Pts[i + 1]));
        }
        var uniq = new List<double>();
        foreach (var h in heads) if (!uniq.Any(u => Math.Abs(((u - h + 540) % 360) - 180) < 8)) uniq.Add(h);
        if (uniq.Count == 0) return;
        if (c.Heading == null) { c.Heading = uniq[0]; c.HeadingSrc = "faces_approach"; }
        c.Tag("approaches", string.Join(";", uniq.Select(h => Math.Round(h).ToString("0", CultureInfo.InvariantCulture))));
        if (refs.Count > 1) c.Tag("wayCount", refs.Count.ToString());
    }

    void CrossingHeading(PropC c, P p, List<(RoadWay w, int i)> refs)
    {
        var (w, i) = refs.OrderBy(r => r.w.Kind == "path" ? 1 : 0).First();
        P a = i > 0 ? w.Pts[i - 1] : w.Pts[i], b = i < w.Pts.Count - 1 ? w.Pts[i + 1] : w.Pts[i];
        if (a == b) return;
        double along = Geo.Bearing(a, b);
        if (c.Heading == null) { c.Heading = w.Kind == "path" ? along : Geo.Norm360(along + 90); c.HeadingSrc = w.Kind == "path" ? "crossing_way" : "perp_to_road"; }
        c.Tag("roadBearing", Math.Round(along).ToString("0", CultureInfo.InvariantCulture));
    }

    public PropC AddOsmWay(JsonNode e, List<P> pts)
    {
        var t = Tags(e);
        var kind = WayKind(t);
        if (kind == null || pts.Count < 2) return null;
        bool closed = pts.Count >= 4 && Geo.Dist(pts[0], pts[^1]) < 1e-6;
        var ring = closed ? pts.Take(pts.Count - 1).ToList() : pts;
        var cen = closed ? Geo.Centroid(ring) : (ring.Average(q => q.x), ring.Average(q => q.z));
        if (!inside(cen)) { Dropped++; return null; }
        long id = e["id"].GetValue<long>();
        var c = Make(kind, "osm", "osm:way/" + id, cen); c.OsmId = "way/" + id;
        AddTags(c, t);
        c.Closed = closed; c.Pts = pts.SelectMany(q => new[] { (float)Math.Round(q.x, 1), (float)Math.Round(q.z, 1) }).ToArray();
        if (ring.Count >= 3)
        {
            var (len, wid, ax) = Geo.LongAxis(ring);
            c.Tag("lenM", len.ToString("0.0", CultureInfo.InvariantCulture)); c.Tag("widM", wid.ToString("0.0", CultureInfo.InvariantCulture));
            if (len > 0 && (wid / len <= 0.8 || kind == "crane")) { c.Heading = ax; c.HeadingSrc = "way_long_axis"; }
        }
        else { c.Heading = Geo.Bearing(ring[0], ring[^1]) % 180; c.HeadingSrc = "way_long_axis"; c.Tag("lenM", Geo.Dist(ring[0], ring[^1]).ToString("0.0", CultureInfo.InvariantCulture)); }
        if (t.TryGetValue("height", out var hs) && double.TryParse(hs.Replace("m", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) c.HeightM = h;
        Items.Add(c);
        return c;
    }

    public PropC AddDataSf(string dataset, string idPart, string kind, double lat, double lon, string name, params (string k, string v)[] tags)
    {
        var p = Geo.Proj(lat, lon);
        if (!inside(p)) { Dropped++; return null; }
        var c = Make(kind, "datasf", "datasf:" + dataset + "/" + idPart, p);
        c.Name = name ?? "";
        c.Tag("dataset", dataset);
        foreach (var (k, v) in tags) c.Tag(k, v);
        Items.Add(c);
        return c;
    }

    /// <summary>Same kind within radius metres across different sources: keep the DataSF record and remember the other id.</summary>
    static readonly Dictionary<string, double> DedupeRadius = new() { ["bicycle_rental"] = 15, ["bus_stop"] = 12 };

    public int DedupeAcrossSources(double radius = 1.5)
    {
        int removed = 0;
        var kept = new List<PropC>();
        var grid = new Dictionary<(string, int, int), List<PropC>>();
        foreach (var c in Items.OrderBy(i => i.Source == "datasf" ? 0 : 1))
        {
            int cx = (int)Math.Floor(c.X / 4), cz = (int)Math.Floor(c.Z / 4);
            PropC dup = null;
            for (int i = cx - 4; i <= cx + 4 && dup == null; i++)
                for (int j = cz - 4; j <= cz + 4 && dup == null; j++)
                    if (grid.TryGetValue((c.Kind, i, j), out var l))
                        dup = l.FirstOrDefault(o => o.Source != c.Source && Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) <= DedupeRadius.GetValueOrDefault(c.Kind, radius));
            if (dup != null)
            {
                dup.AltId = dup.AltId == "" ? c.Id : dup.AltId + "," + c.Id; removed++;
                if (dup.Heading == null && c.Heading != null) { dup.Heading = c.Heading; dup.HeadingSrc = c.HeadingSrc; }
                continue;
            }
            kept.Add(c);
            if (!grid.TryGetValue((c.Kind, cx, cz), out var cell)) grid[(c.Kind, cx, cz)] = cell = new();
            cell.Add(c);
        }
        Items.Clear(); Items.AddRange(kept);
        // OSM stop signs / signals sit on the road centreline; flag the ones that have a curbside DataSF record within 15 m
        foreach (var c in kept.Where(c => c.Source == "osm" && (c.Kind is "stop_sign" or "traffic_signal")))
        {
            var d = kept.Where(o => o.Source == "datasf" && o.Kind == c.Kind).Select(o => Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z))).DefaultIfEmpty(1e9).Min();
            if (d <= 15) c.Tag("dsNearby", d.ToString("0.0", CultureInfo.InvariantCulture));
        }
        return removed;
    }
}

/// <summary>Nearest-road lookup over the (non-path) road centrelines already written to WorldData.</summary>
class RoadIndex
{
    const double Cell = 40;
    readonly List<(P a, P b, string name)> segs = new();
    readonly Dictionary<(int, int), List<int>> grid = new();

    public RoadIndex(IEnumerable<Road> roads)
    {
        foreach (var r in roads.Where(r => r.Kind != "path"))
            for (int i = 0; i + 3 < r.Pts.Length; i += 2)
            {
                P a = (r.Pts[i], r.Pts[i + 1]), b = (r.Pts[i + 2], r.Pts[i + 3]);
                int idx = segs.Count; segs.Add((a, b, r.Name));
                for (int cx = (int)Math.Floor(Math.Min(a.x, b.x) / Cell); cx <= (int)Math.Floor(Math.Max(a.x, b.x) / Cell); cx++)
                    for (int cz = (int)Math.Floor(Math.Min(a.z, b.z) / Cell); cz <= (int)Math.Floor(Math.Max(a.z, b.z) / Cell); cz++)
                    {
                        if (!grid.TryGetValue((cx, cz), out var l)) grid[(cx, cz)] = l = new();
                        l.Add(idx);
                    }
            }
    }

    public (P pt, double dist, string name, double segBearing)? Nearest(P p)
    {
        int cx = (int)Math.Floor(p.x / Cell), cz = (int)Math.Floor(p.z / Cell);
        (P, double, string, double)? best = null;
        for (int r = 1; r <= 12; r++)
        {
            for (int i = cx - r; i <= cx + r; i++)
                for (int j = cz - r; j <= cz + r; j++)
                {
                    if (!grid.TryGetValue((i, j), out var l)) continue;
                    foreach (var si in l)
                    {
                        var s = segs[si]; var (q, d, _) = Geo.Nearest(p, s.a, s.b);
                        if (best == null || d < best.Value.Item2) best = (q, d, s.name, Geo.Bearing(s.a, s.b));
                    }
                }
            if (best != null && best.Value.Item2 <= (r - 1) * Cell + 1) break;
        }
        return best;
    }
}
