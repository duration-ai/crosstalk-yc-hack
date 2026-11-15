using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using P = (double x, double z);

namespace DataPrep;

class PolyIndex<T>
{
    const double Cell = 60;
    readonly Dictionary<(int, int), List<int>> grid = new();
    readonly List<(List<P> ring, T item, double x0, double x1, double z0, double z1)> items = new();

    public void Add(List<P> ring, T item)
    {
        if (ring.Count < 3) return;
        double x0 = ring.Min(p => p.x), x1 = ring.Max(p => p.x), z0 = ring.Min(p => p.z), z1 = ring.Max(p => p.z);
        int idx = items.Count; items.Add((ring, item, x0, x1, z0, z1));
        for (int i = (int)Math.Floor(x0 / Cell); i <= (int)Math.Floor(x1 / Cell); i++)
            for (int j = (int)Math.Floor(z0 / Cell); j <= (int)Math.Floor(z1 / Cell); j++)
            {
                if (!grid.TryGetValue((i, j), out var l)) grid[(i, j)] = l = new();
                l.Add(idx);
            }
    }

    public IEnumerable<(T item, List<P> ring)> Containing(P p)
    {
        if (!grid.TryGetValue(((int)Math.Floor(p.x / Cell), (int)Math.Floor(p.z / Cell)), out var l)) yield break;
        foreach (var i in l)
        {
            var it = items[i];
            if (p.x < it.x0 || p.x > it.x1 || p.z < it.z0 || p.z > it.z1) continue;
            if (Geo.PointInPoly(p, it.ring)) yield return (it.item, it.ring);
        }
    }
}

class OsmBldg
{
    public long Id; public List<P> Ring; public string Kind; public float? Height; public Dictionary<string, string> Tags; public P Centroid; public double Area;
    public string Name => Tags.GetValueOrDefault("name");
}

class ParcelInfo
{
    public string Blklot = "", MapBlklot = "", Street = "", Zoning = "";
    public double Area;
    public List<Assessor> Rows = new();
}
class Assessor { public string Parcel, UseCode, UseDef, Zoning; public int Year; public float Stories; public int Units; }
class LandUseRow
{
    public string MapBlklot, Geography; public double ResUnits, Cie, Med, Mips, Retail, Pdr, Visitor; public bool ParkingLot, Garage, OpenSpace; public double Area;
    public string Use()
    {
        double nonRes = Cie + Med + Mips + Retail + Pdr + Visitor;
        if (ResUnits > 0 && nonRes > 0) return "MIXRES";
        if (ResUnits > 0) return "RESIDENT";
        if (nonRes > 0)
            return new[] { ("CIE", Cie), ("MED", Med), ("MIPS", Mips), ("RETAIL", Retail), ("PDR", Pdr), ("VISITOR", Visitor) }.OrderByDescending(x => x.Item2).First().Item1;
        if (ParkingLot || Garage) return "PARKING";
        if (OpenSpace) return "OPENSPACE";
        return "VACANT";
    }
    public string Raw() => string.Join(";", new[] { ("resunits", ResUnits), ("cie", Cie), ("med", Med), ("mips", Mips), ("retail", Retail), ("pdr", Pdr), ("visitor", Visitor) }
        .Where(x => x.Item2 > 0).Select(x => x.Item1 + "=" + x.Item2.ToString("0.#", CultureInfo.InvariantCulture)).Concat(ParkingLot ? new[] { "parking_lot=yes" } : new string[0])
        .Concat(Garage ? new[] { "garage=yes" } : new string[0]).Concat(OpenSpace ? new[] { "open_space=yes" } : new string[0]));
}

/// <summary>
/// Parcel-level attributes joined to building footprints by centroid containment:
///  acdm-wktn  Parcels - Active and Retired        (polygons: blklot, street, zoning)
///  c5ge-t6pj  San Francisco Land Use              (parcel polygons: residential units + commercial/PDR/CIE/MED/MIPS/retail/visitor sqft, parking/garage/open space)
///  wv5m-vpq2  Assessor Historical Secured Rolls   (closed_roll_year 2025: use_code, year_property_built, stories, units)
/// </summary>
class ParcelData
{
    public readonly PolyIndex<ParcelInfo> Parcels = new();
    public readonly PolyIndex<LandUseRow> LandUse = new();
    public int ParcelCount, LandUseCount, AssessorCount, AssessorUnmatched;

    static double D(JsonNode n, string k) => double.TryParse(n[k]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    static string S(JsonNode n, string k) => n[k]?.ToString() ?? "";

    public static IEnumerable<List<P>> Rings(JsonNode geom)
    {
        var coords = geom?["coordinates"]?.AsArray();
        if (coords == null) yield break;
        bool multi = geom["type"]?.GetValue<string>() == "MultiPolygon";
        foreach (var poly in multi ? coords : new JsonArray(coords.DeepClone()))
        {
            var ring = poly[0].AsArray().Select(c => Geo.Proj((double)c[1], (double)c[0])).ToList();
            if (ring.Count > 1 && Geo.Dist(ring[0], ring[^1]) < 1e-6) ring.RemoveAt(ring.Count - 1);
            if (ring.Count >= 3) yield return ring;
        }
    }

    public static async Task<ParcelData> Load(Fetch fetch, double N, double W, double S_, double E)
    {
        var d = new ParcelData();
        string wb(string g) => $"within_box({g}, {N}, {W}, {S_}, {E})";

        Console.WriteLine("Fetching DataSF parcels (acdm-wktn)...");
        var parcels = await fetch.Soda("acdm-wktn", "mapblklot,blklot,street_name,zoning_code,shape", wb("shape") + " AND active=true", "blklot");
        var byId = new Dictionary<string, ParcelInfo>();
        foreach (var r in parcels)
        {
            var info = new ParcelInfo { Blklot = S(r, "blklot"), MapBlklot = S(r, "mapblklot"), Street = S(r, "street_name"), Zoning = S(r, "zoning_code") };
            foreach (var ring in Rings(r["shape"])) { info.Area += Geo.Area(ring); d.Parcels.Add(ring, info); }
            byId[info.Blklot] = info; byId.TryAdd(info.MapBlklot, info);
        }
        d.ParcelCount = parcels.Count;

        Console.WriteLine("Fetching DataSF land use (c5ge-t6pj)...");
        var lu = await fetch.Soda("c5ge-t6pj", "mapblklot,geography_type,resunits,cie,med,mips,retail,pdr,visitor,parking_lo,garage,open_space,the_geom", wb("the_geom"), "mapblklot");
        foreach (var r in lu)
        {
            var row = new LandUseRow
            {
                MapBlklot = S(r, "mapblklot"), Geography = S(r, "geography_type"), ResUnits = D(r, "resunits"), Cie = D(r, "cie"), Med = D(r, "med"), Mips = D(r, "mips"), Retail = D(r, "retail"),
                Pdr = D(r, "pdr"), Visitor = D(r, "visitor"), ParkingLot = S(r, "parking_lo") == "true", Garage = S(r, "garage") == "true", OpenSpace = S(r, "open_space") == "true",
            };
            foreach (var ring in Rings(r["the_geom"])) { row.Area = Math.Max(row.Area, Geo.Area(ring)); d.LandUse.Add(ring, row); }
        }
        d.LandUseCount = lu.Count;

        Console.WriteLine("Fetching DataSF assessor roll (wv5m-vpq2, 2025)...");
        var asr = await fetch.Soda("wv5m-vpq2", "parcel_number,use_code,use_definition,year_property_built,number_of_stories,number_of_units,zoning_code,the_geom",
            wb("the_geom") + " AND closed_roll_year='2025'", "parcel_number");
        foreach (var r in asr)
        {
            var row = new Assessor
            {
                Parcel = S(r, "parcel_number"), UseCode = S(r, "use_code"), UseDef = S(r, "use_definition"), Zoning = S(r, "zoning_code"),
                Year = (int)D(r, "year_property_built"), Stories = (float)D(r, "number_of_stories"), Units = (int)D(r, "number_of_units"),
            };
            ParcelInfo target = null;
            if (!byId.TryGetValue(row.Parcel, out target))
            {
                var c = r["the_geom"]?["coordinates"]?.AsArray();
                if (c != null) target = d.Parcels.Containing(Geo.Proj((double)c[1], (double)c[0])).Select(x => x.item).FirstOrDefault();
            }
            if (target == null) { d.AssessorUnmatched++; continue; }
            target.Rows.Add(row);
        }
        d.AssessorCount = asr.Count;
        return d;
    }

    public class Result
    {
        public ParcelInfo Parcel; public LandUseRow Lu; public string UseCode = "", UseDef = "", Zoning = "", Use = "UNKNOWN", LuRaw = "";
        public int Year, Units; public float Stories;
    }

    public Result Lookup(P c)
    {
        var res = new Result();
        res.Parcel = Parcels.Containing(c).Select(x => x.item).FirstOrDefault();
        res.Lu = LandUse.Containing(c).Select(x => x.item).OrderBy(x => x.Geography == "parcel" ? 0 : 1).ThenBy(x => x.Area).FirstOrDefault();
        if (res.Lu != null) { res.Use = res.Lu.Use(); res.LuRaw = res.Lu.Raw(); }
        if (res.Parcel == null) return res;
        res.Zoning = res.Parcel.Zoning;
        var rows = res.Parcel.Rows;
        if (rows.Count == 0) return res;
        var top = rows.GroupBy(r => r.UseCode).OrderByDescending(g => g.Count()).First();
        res.UseCode = top.Key; res.UseDef = top.First().UseDef;
        res.Year = rows.Where(r => r.Year > 1700).GroupBy(r => r.Year).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        res.Stories = rows.Max(r => r.Stories);
        res.Units = rows.Sum(r => r.Units);
        return res;
    }
}

static class BuildingTypes
{
    static readonly Regex HistoricName = new("Union Iron|Bethlehem|Pier 70|Machine Shop|Powerhouse|Plate Shop|American Industrial|Shipyard|Drydock|Boiler", RegexOptions.IgnoreCase);
    static readonly HashSet<string> OsmRes = new() { "house", "residential", "apartments", "detached", "semidetached_house", "terrace", "dormitory", "bungalow", "duplex" };
    static readonly HashSet<string> OsmCivicB = new() { "school", "church", "chapel", "cathedral", "kindergarten", "university", "college", "civic", "government", "public", "hospital", "religious", "fire_station", "mosque", "synagogue", "temple" };
    static readonly HashSet<string> OsmCivicA = new() { "school", "kindergarten", "college", "university", "place_of_worship", "fire_station", "police", "library", "community_centre", "townhall", "hospital", "clinic", "social_facility", "courthouse" };

    /// <summary>
    /// Rule-based building type. Rules fire in this order (first match wins); the rule name is written to Building.TypeRule:
    ///  1 garage            OSM building=garage|garages|parking or amenity=parking, or Land Use PARKING
    ///  2 utility           OSM man_made storage_tank|silo|water_tower|tower|pumping_station|chimney|smoke_stack, power=substation|plant, building=service|transformer_tower
    ///  3 shed (osm)        OSM building=shed|roof|hut|cabin|carport|kiosk|container|greenhouse
    ///  4 industrial_brick  OSM historic=* on an industrial/commercial/warehouse building, or a Pier 70 / Union Iron Works style name
    ///  5 civic (osm)       OSM school/church/fire-station/hospital/... via amenity or building tag
    ///  6 shed (geometry)   footprint <= 60 m2 and height <= 4.5 m
    ///  7 industrial_brick  Land Use PDR or assessor IND with year built before 1945
    ///  8 civic (parcel)    assessor GOVT or Land Use CIE/MED
    ///  9 warehouse         Land Use PDR, assessor IND or OSM industrial/warehouse (built 1945+ or unknown)
    /// 10 residential       Land Use RESIDENT/MIXRES, assessor SRES/MRES/MISC with units, or OSM residential tags:
    ///        stories >= 4 or height > 13 m -> apartment_midrise (built < 1960 with ground retail -> mixed_use)
    ///        ground retail                 -> mixed_use
    ///        built < 1925, <= 4 storeys, footprint < 400 m2 -> victorian
    ///        footprint <= 450 m2           -> rowhouse_modern
    ///        otherwise 3+ storeys or 6+ units -> apartment_midrise, else rowhouse_modern
    /// 11 commercial        Land Use RETAIL/VISITOR/MIPS, assessor COMR/COMM/COMH/COMO, OSM commercial/retail/office: > 3 storeys or > 13 m -> office, else commercial_lowrise
    /// 12 open/vacant       small structures on VACANT/OPENSPACE parcels -> shed
    /// 13 fallback          by size: tiny low -> shed, large low -> warehouse, tall -> office, small -> rowhouse_modern, else commercial_lowrise
    /// </summary>
    public static (string type, string rule) Classify(float h, double area, float stories, int year, string use, string useCode, int units, bool retail, Dictionary<string, string> t)
    {
        string g(string k) => t.GetValueOrDefault(k) ?? "";
        string ob = g("building"), name = g("name") + " " + g("addr:housename"), amenity = g("amenity"), manMade = g("man_made"), power = g("power");
        bool tall = stories >= 4 || h > 13;
        bool osmRes = OsmRes.Contains(ob);

        if (ob is "garage" or "garages" or "parking" || amenity == "parking") return ("garage", "osm_garage");
        if (use == "PARKING") return ("garage", "lu_parking");
        if (manMade is "storage_tank" or "silo" or "water_tower" or "tower" or "pumping_station" or "chimney" or "smoke_stack" || power is "substation" or "plant" || ob is "service" or "transformer_tower")
            return ("utility", "osm_utility");
        if (ob is "shed" or "roof" or "hut" or "cabin" or "carport" or "kiosk" or "container" or "greenhouse") return ("shed", "osm_shed");
        if ((t.ContainsKey("historic") && ob is "industrial" or "yes" or "warehouse" or "commercial" or "factory") || HistoricName.IsMatch(name)) return ("industrial_brick", "osm_historic");
        if (OsmCivicA.Contains(amenity) || OsmCivicB.Contains(ob)) return ("civic", "osm_civic");
        if (area <= 60 && h <= 4.5) return ("shed", "geom_small");
        bool pdr = use == "PDR" || useCode == "IND" || ob is "industrial" or "warehouse" or "factory" or "manufacture";
        if ((use == "PDR" || useCode == "IND") && year > 0 && year < 1945) return ("industrial_brick", "parcel_pdr_old");
        if (useCode == "GOVT" || use is "CIE" or "MED") return ("civic", "parcel_civic");
        if (pdr) return ("warehouse", "parcel_pdr");

        bool res = use is "RESIDENT" or "MIXRES" || useCode is "SRES" or "MRES" || (useCode == "MISC" && units > 0) || osmRes;
        if (res)
        {
            bool mixed = retail || use == "MIXRES" && retail || useCode == "MISC" && retail;
            if (tall) return mixed && year > 0 && year < 1960 ? ("mixed_use", "res_tall_old_retail") : ("apartment_midrise", "res_tall");
            if (mixed) return ("mixed_use", "res_retail");
            if (year > 0 && year < 1925 && stories <= 4 && area < 400) return ("victorian", "res_pre1925");
            if (area <= 450) return ("rowhouse_modern", "res_small_lot");
            if (stories >= 3 || units >= 6) return ("apartment_midrise", "res_large_lowrise");
            return ("rowhouse_modern", "res_default");
        }

        bool com = use is "RETAIL" or "VISITOR" or "MIPS" || useCode is "COMR" or "COMM" or "COMH" or "COMO" || ob is "commercial" or "retail" or "office" or "hotel" or "supermarket";
        if (com) return tall ? ("office", "com_tall") : ("commercial_lowrise", "com_low");
        if (use is "VACANT" or "OPENSPACE") return ("shed", "vacant_structure");

        if (area <= 100 && h <= 5) return ("shed", "fallback_small");
        if (area >= 800 && h <= 12) return ("warehouse", "fallback_large_low");
        if (tall) return ("office", "fallback_tall");
        if (area <= 250) return ("rowhouse_modern", "fallback_small_lot");
        return ("commercial_lowrise", "fallback");
    }
}
