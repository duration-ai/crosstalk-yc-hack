using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // Places every street tree and street prop from the extract as a Polyfork model at its real
    // position and heading. Trees are picked by species and scaled from DBH. Props without a heading
    // face the nearest street; props the source put inside a carriageway are moved to the curb.
    public sealed class StreetFurniture
    {
        public sealed class Tally
        {
            public int Total, Placed, Moved;
            public float MovedMetres;
            public readonly Dictionary<string, int> Exceptions = new Dictionary<string, int>();
            public void Except(string why) { Exceptions.TryGetValue(why, out int n); Exceptions[why] = n + 1; }
        }

        public readonly Dictionary<string, Tally> Tallies = new Dictionary<string, Tally>();
        public readonly Dictionary<string, int> TreeVariants = new Dictionary<string, int>();
        public readonly List<float> TreeHeights = new List<float>();
        public readonly List<(string kind, Vector2 position, float heading)> Seating = new List<(string, Vector2, float)>();

        readonly WorldContext ctx;
        readonly StaticModels statics;
        readonly HashSet<Vector2Int> signalledJunctions = new HashSet<Vector2Int>();

        public StreetFurniture(WorldContext ctx, StaticModels statics)
        {
            this.ctx = ctx;
            this.statics = statics;
        }

        Tally T(string kind)
        {
            if (!Tallies.TryGetValue(kind, out var t)) Tallies[kind] = t = new Tally();
            return t;
        }

        // ---- Trees

        public void PlaceTrees(TreeRecord[] trees)
        {
            if (trees == null) return;
            foreach (var tree in trees)
            {
                var tally = T("tree:" + tree.source);
                tally.Total++;
                var p = new Vector2(tree.x, tree.z);
                if (!ctx.Bounds.Contains(p)) { tally.Except("outside bounds"); continue; }
                string form = TreeForm(tree.species);
                uint h = Hash(tree.id);
                float height = TreeHeight(form, tree.dbhKnown ? tree.dbh : 0f);
                string id = form switch
                {
                    "palm" => height > 11.5f ? "t-palm-14" : "t-palm-9",
                    "conifer" => "t-conifer",
                    "shade" => (h & 1) == 0 ? "t-shade-tree" : "t-shade-treeDeep",
                    "ornamental" => "t-ornamental",
                    _ => "t-street-" + new[] { "egg", "round", "broad" }[h % 3] + ((h >> 3 & 1) == 0 ? "-tree" : "-treeDeep"),
                };
                var mesh = statics.Mesh(id);
                if (mesh == null) { tally.Except("model missing"); continue; }
                if (!ClearSpot(ref p, 0.3f)) { tally.Except("no spot off the asphalt (dropped)"); continue; }
                float scale = Mathf.Clamp(height / mesh.bounds.size.y, 0.45f, 2.6f);
                var m = Matrix4x4.TRS(MeshBuilder.At(p, 0.03f), Quaternion.Euler(0f, h % 360, 0f), Vector3.one * scale);
                if (statics.PlaceAtOrigin(StaticModels.Layer.Trees, id, p, m, tree.id, 0.3f * Mathf.Max(1f, scale), 3f) < 0) { tally.Except("model missing"); continue; }
                tally.Placed++;
                TreeVariants.TryGetValue(id, out int n);
                TreeVariants[id] = n + 1;
                TreeHeights.Add(mesh.bounds.size.y * scale);
            }
        }

        static string TreeForm(string species)
        {
            var s = (species ?? "").ToLowerInvariant();
            bool Any(params string[] words) { foreach (var w in words) if (s.Contains(w)) return true; return false; }
            if (Any("palm", "phoenix", "washingtonia", "trachycarpus", "syagrus", "archontophoenix", "butia", "chamaerops")) return "palm";
            if (Any("pinus", " pine", "pine ", "cupressus", "cypress", "sequoia", "redwood", "araucaria", "cedrus", "cedar", "juniper", "taxodium")) return "conifer";
            if (Any("platanus", "plane", "sycamore", "ulmus", " elm", "fraxinus", " ash", "quercus", " oak", "liquidambar", "sweet gum", "sweetgum", "zelkova", "ginkgo", "tilia", "gleditsia", "eucalyptus", "corymbia", "populus", "ficus", "celtis")) return "shade";
            if (Any("prunus", "plum", "cherry", "malus", "apple", "pyrus", "pear", "lagerstroemia", "crape", "magnolia", "arbutus", "acer", "maple", "cercis", "redbud", "olea", "olive", "callistemon", "bottlebrush", "leptospermum", "ceanothus", "geijera", "laurus", "citrus")) return "ornamental";
            return "street";
        }

        // Height from trunk diameter at breast height (inches): a broad street-tree allometry, capped
        // per form. Trees with no DBH get the form's typical height.
        static float TreeHeight(string form, float dbhInches)
        {
            if (dbhInches <= 0f)
                return form switch { "palm" => 9f, "conifer" => 10f, "shade" => 9f, "ornamental" => 4.5f, _ => 6.5f };
            return form switch
            {
                "palm" => Mathf.Clamp(2f + 0.55f * dbhInches, 4f, 18f),
                "conifer" => Mathf.Clamp(3f + 0.6f * dbhInches, 4f, 28f),
                "ornamental" => Mathf.Clamp(2.5f + 0.35f * dbhInches, 2.5f, 9f),
                _ => Mathf.Clamp(3f + 0.45f * dbhInches, 3f, 20f),
            };
        }

        // ---- Props

        static readonly Dictionary<string, (string id, float radius)> PropModels = new Dictionary<string, (string, float)>
        {
            ["street_lamp"] = ("p-lamp", 0.2f), ["stop_sign"] = ("p-stop", 0.1f), ["give_way"] = ("p-signpost", 0.1f),
            ["fire_hydrant"] = ("p-hydrant", 0.25f), ["bench"] = ("p-bench", 0f), ["bollard"] = ("p-bollard", 0.2f),
            ["bike_rack"] = ("p-bikerack", 0f), ["parking_meter"] = ("p-meter", 0.15f), ["trash_can"] = ("p-bin", 0.3f), ["recycling"] = ("p-bin", 0.3f),
            ["post_box"] = ("p-mailbox", 0.35f), ["drinking_water"] = ("p-fountain", 0.3f), ["picnic_table"] = ("p-picnic", 0f),
            ["vending"] = ("p-newsbox", 0.3f), ["charging_station"] = ("p-charger", 0.3f), ["power_pole"] = ("p-pole", 0.2f),
            ["flagpole"] = ("p-flagpole", 0.15f),
        };

        public static readonly HashSet<string> LandmarkKinds = new HashSet<string>
        {
            "yc", "machine_shop", "cream_stone", "crane", "smokestack", "caltrain_22nd", "drydock", "billboard", "chimney", "bike_share",
        };

        public void PlaceProps(Prop[] props)
        {
            if (props == null) return;
            foreach (var prop in props)
            {
                if (LandmarkKinds.Contains(prop.kind)) continue;
                var tally = T(prop.kind);
                tally.Total++;
                var p = new Vector2(prop.x, prop.z);
                if (!ctx.Bounds.Contains(p)) { tally.Except("outside bounds"); continue; }
                if (prop.kind == "stop_sign" && prop.source == "osm" && System.Array.Exists(prop.tags ?? System.Array.Empty<string>(), t => t.StartsWith("dsNearby=")))
                {
                    tally.Except("duplicate of a DataSF stop sign within 15 m (DataSF placed instead)");
                    continue;
                }
                switch (prop.kind)
                {
                    case "traffic_signal": PlaceSignal(prop, p, tally); continue;
                    case "crossing": PlaceCrossing(prop, tally); continue;
                    case "bus_stop":
                        bool shelter = Has(prop, "shelter=yes") || Has(prop, "shelter=1");
                        PlaceSingle(shelter ? "p-shelter" : "p-busstop", shelter ? 0f : 0.15f, prop, p, tally);
                        continue;
                    case "bicycle_rental": PlaceDock(prop, p, tally); continue;
                }
                if (!PropModels.TryGetValue(prop.kind, out var model)) { tally.Except("no Polyfork model for this kind"); continue; }
                PlaceSingle(model.id, model.radius, prop, p, tally);
            }
        }

        void PlaceSingle(string id, float radius, Prop prop, Vector2 p, Tally tally)
        {
            var q = ToCurb(p, tally, prop.kind == "stop_sign" || prop.kind == "give_way" ? 0.6f : 0.5f);
            float heading = prop.headingKnown ? prop.heading : FaceStreet(q);
            if (prop.kind == "stop_sign" || prop.kind == "give_way") q = StopSignSpot(p, heading, q);
            if (!ClearSpot(ref q, radius)) { tally.Except("no spot off the asphalt (dropped)"); return; }
            if (statics.PlaceAtOrigin(StaticModels.Layer.Props, id, q, Matrix4x4.TRS(MeshBuilder.At(q, 0.03f), Quaternion.Euler(0f, heading, 0f), Vector3.one), prop.id, radius) < 0)
            {
                tally.Except("model missing");
                return;
            }
            tally.Placed++;
            if (prop.kind == "bench" || prop.kind == "bus_stop") Seating.Add((prop.kind, q, heading));
        }

        // A sign on the carriageway (OSM puts stop nodes on the road) goes to the curb on the right of
        // the traffic it faces.
        Vector2 StopSignSpot(Vector2 p, float heading, Vector2 curb)
        {
            if (!ctx.Roads.Nearest(p, 2f, out var hit) || hit.Distance > 0f) return curb;
            float hw = RoadNetwork.HalfWidth(ctx.Roads.Roads[hit.Road]);
            var face = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var travel = -face;
            var rightOfTravel = new Vector2(travel.y, -travel.x);
            return hit.Point + rightOfTravel * (hw + 0.6f);
        }

        Vector2 ToCurb(Vector2 p, Tally tally, float setback)
        {
            if (!ctx.Roads.Nearest(p, 0.5f, out var hit) || hit.Distance >= 0f) return p;
            var road = ctx.Roads.Roads[hit.Road];
            if (road.kind == "path") return p;
            var away = p - hit.Point;
            if (away.sqrMagnitude < 1e-4f) away = new Vector2(hit.Direction.y, -hit.Direction.x);
            var q = hit.Point + away.normalized * (RoadNetwork.HalfWidth(road) + setback);
            tally.Moved++;
            tally.MovedMetres += Vector2.Distance(p, q);
            return q;
        }

        float FaceStreet(Vector2 p)
        {
            if (!ctx.Roads.Nearest(p, 60f, out var hit)) return 0f;
            var d = hit.Point - p;
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        // OSM puts most signals on the junction itself: one junction gets a mast at two opposite
        // corners, reaching over the road. A corner that finds no kerb spot (the median of a divided street)
        // gets none.
        void PlaceSignal(Prop prop, Vector2 p, Tally tally)
        {
            if (!ctx.Roads.Nearest(p, 3f, out var hit) || hit.Distance > 0.5f)
            {
                PlaceSingle("p-signal", 0.2f, prop, p, tally);
                return;
            }
            var junction = Vector2Int.RoundToInt(p / 12f);
            tally.Placed++;
            if (!signalledJunctions.Add(junction)) return;
            float hw = RoadNetwork.HalfWidth(ctx.Roads.Roads[hit.Road]);
            var d = hit.Direction;
            var n = new Vector2(-d.y, d.x);
            foreach (var side in new[] { 1f, -1f })
            {
                if (KerbSpot(hit.Point, n * side, -d * side, hw, out var corner))
                {
                    float yaw = Mathf.Atan2(d.x * side, d.y * side) * Mathf.Rad2Deg;
                    statics.PlaceAtOrigin(StaticModels.Layer.Props, "p-signal", corner, Matrix4x4.TRS(MeshBuilder.At(corner, 0.03f), Quaternion.Euler(0f, yaw, 0f), Vector3.one), prop.id, 0.2f, 3f);
                }
                else tally.Except("signal corner without a kerb spot (dropped)");
            }
        }

        // The nearest spot off every carriageway at a junction corner: across the street from its centre,
        // then walking along it, back from the junction first and past it if the cross street is in the way.
        bool KerbSpot(Vector2 centre, Vector2 across, Vector2 back, float hw, out Vector2 corner)
        {
            for (float along = hw + 1f; along <= hw + 16f; along += 0.5f)
                foreach (float off in new[] { 1.2f, 2.5f })
                    foreach (float dir in new[] { 1f, -1f })
                    {
                        corner = centre + across * (hw + off) + back * dir * along;
                        if (!OnCarriageway(corner)) return true;
                    }
            corner = default;
            return false;
        }

        // True when an item of this collider radius at p (with a little room) would stand on any street's
        // asphalt, curbed or flush. Medians and paths are fine.
        bool OnCarriageway(Vector2 p, float radius = 0.2f)
        {
            float margin = Mathf.Max(radius, 0.15f) + 0.4f;
            if (ctx.Roads.OnCurbedCarriageway(p, margin)) return true;
            if (!ctx.Roads.Nearest(p, margin, out var near)) return false;
            if (ctx.Roads.Roads[near.Road].kind != "path") return true;
            return ctx.Roads.Nearest(p, margin, out near, near.Road) && ctx.Roads.Roads[near.Road].kind != "path";
        }

        // The one placement check every piece of street furniture goes through: p stays if it is off the
        // asphalt, else moves to the nearest clear spot within 8 m (rings every half metre); false when
        // there is none, and the item is dropped.
        bool ClearSpot(ref Vector2 p, float radius)
        {
            if (!OnCarriageway(p, radius)) return true;
            for (float r = 0.5f; r <= 8f; r += 0.5f)
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2f / 24f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (OnCarriageway(q, radius)) continue;
                    p = q;
                    return true;
                }
            return false;
        }

        // Crosswalks are painted by RoadSurface (curb to curb, with curb ramps), so a crossing node is
        // only tallied here.
        void PlaceCrossing(Prop prop, Tally tally)
        {
            bool unmarked = Has(prop, "crossing=unmarked") || Has(prop, "crossing:markings=no");
            tally.Except(unmarked ? "unmarked (nothing to draw)" : "painted by the road surface's crosswalks");
        }

        // A Bay Wheels dock: a row of hoop racks along the dock, one per six docking points.
        void PlaceDock(Prop prop, Vector2 p, Tally tally)
        {
            var mesh = statics.Mesh("p-bikerack");
            if (mesh == null) { tally.Except("model missing"); return; }
            float heading = prop.headingKnown ? prop.heading : FaceStreet(p) + 90f;
            var along = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            int racks = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Capacity(prop), 6) / 6f), 1, 6);
            float length = mesh.bounds.size.x;
            for (int i = 0; i < racks; i++)
            {
                var q = p + along * ((i - (racks - 1) * 0.5f) * (length + 0.2f));
                if (!ClearSpot(ref q, 0.3f)) { tally.Except("rack with no spot off the asphalt (dropped)"); continue; }
                statics.PlaceAtOrigin(StaticModels.Layer.Props, "p-bikerack", q, Matrix4x4.TRS(MeshBuilder.At(q, 0.03f), Quaternion.Euler(0f, heading - 90f, 0f), Vector3.one), prop.id);
            }
            tally.Placed++;
        }

        static int Capacity(Prop prop)
        {
            if (prop.tags != null)
                foreach (var t in prop.tags)
                    if (t.StartsWith("capacity=") && int.TryParse(t.Substring(9), out int n)) return n;
            return 0;
        }

        static bool Has(Prop prop, string kv) => prop.tags != null && System.Array.IndexOf(prop.tags, kv) >= 0;

        static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") h = (h ^ c) * 16777619;
            return h;
        }
    }
}
