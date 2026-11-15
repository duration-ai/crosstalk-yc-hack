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

        readonly WorldContext ctx;
        readonly StaticModels statics;
        readonly List<(Vector3 position, float radius, float height)> colliders = new List<(Vector3, float, float)>();
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
                float height = TreeHeight(form, tree.dbh);
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
                float scale = Mathf.Clamp(height / mesh.bounds.size.y, 0.45f, 2.6f);
                var m = Matrix4x4.TRS(MeshBuilder.At(p, 0.03f), Quaternion.Euler(0f, h % 360, 0f), Vector3.one * scale);
                if (!statics.PlaceAtOrigin(StaticModels.Layer.Trees, id, p, m)) { tally.Except("model missing"); continue; }
                tally.Placed++;
                TreeVariants.TryGetValue(id, out int n);
                TreeVariants[id] = n + 1;
                TreeHeights.Add(mesh.bounds.size.y * scale);
                colliders.Add((MeshBuilder.At(p, 0f), 0.3f * Mathf.Max(1f, scale), 3f));
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
            ["street_lamp"] = ("p-lamp", 0.2f), ["stop"] = ("p-stop", 0.1f), ["give_way"] = ("p-signpost", 0.1f),
            ["fire_hydrant"] = ("p-hydrant", 0.25f), ["bench"] = ("p-bench", 0f), ["bollard"] = ("p-bollard", 0.2f),
            ["bike_rack"] = ("p-bikerack", 0f), ["parking_meter"] = ("p-meter", 0.15f), ["trash_can"] = ("p-bin", 0.3f),
            ["post_box"] = ("p-mailbox", 0.35f), ["drinking_water"] = ("p-fountain", 0.3f), ["picnic_table"] = ("p-picnic", 0f),
            ["vending"] = ("p-newsbox", 0.3f), ["charging_station"] = ("p-charger", 0.3f), ["power_pole"] = ("p-pole", 0.2f),
            ["flagpole"] = ("p-flagpole", 0.15f),
        };

        public static readonly HashSet<string> LandmarkKinds = new HashSet<string>
        {
            "yc", "machine_shop", "cream_stone", "crane", "smokestack", "caltrain_22nd", "drydock", "billboard", "chimney",
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
                switch (prop.kind)
                {
                    case "traffic_signal": PlaceSignal(prop, p, tally); continue;
                    case "crossing": PlaceCrossing(prop, p, tally); continue;
                    case "bus_stop": PlaceSingle(Has(prop, "shelter=yes") ? "p-shelter" : "p-busstop", 0.15f, prop, p, tally); continue;
                    case "bicycle_rental": PlaceDock(prop, p, tally); continue;
                }
                if (!PropModels.TryGetValue(prop.kind, out var model)) { tally.Except("no Polyfork model for this kind"); continue; }
                PlaceSingle(model.id, model.radius, prop, p, tally);
            }
        }

        void PlaceSingle(string id, float radius, Prop prop, Vector2 p, Tally tally)
        {
            var q = ToCurb(p, tally, prop.kind == "stop" || prop.kind == "give_way" ? 0.6f : 0.5f);
            float heading = prop.heading >= 0f ? prop.heading : FaceStreet(q);
            if (prop.kind == "stop" || prop.kind == "give_way") q = StopSignSpot(p, heading, q);
            if (!statics.PlaceAtOrigin(StaticModels.Layer.Props, id, q, Matrix4x4.TRS(MeshBuilder.At(q, 0.03f), Quaternion.Euler(0f, heading, 0f), Vector3.one)))
            {
                tally.Except("model missing");
                return;
            }
            tally.Placed++;
            if (radius > 0f) colliders.Add((MeshBuilder.At(q, 0f), radius, 2.5f));
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
        // corners, reaching over the road.
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
                var corner = hit.Point + n * side * (hw + 1f) - d * side * (hw + 2.5f);
                float yaw = Mathf.Atan2(d.x * side, d.y * side) * Mathf.Rad2Deg;
                statics.PlaceAtOrigin(StaticModels.Layer.Props, "p-signal", corner, Matrix4x4.TRS(MeshBuilder.At(corner, 0.03f), Quaternion.Euler(0f, yaw, 0f), Vector3.one));
                colliders.Add((MeshBuilder.At(corner, 0f), 0.2f, 3f));
            }
        }

        // Zebra crossing painted across the road at the crossing node (no crosswalk model in the catalogue).
        void PlaceCrossing(Prop prop, Vector2 p, Tally tally)
        {
            if (!ctx.Roads.Nearest(p, 2f, out var hit)) { tally.Except("not on a road"); return; }
            var road = ctx.Roads.Roads[hit.Road];
            if (road.kind == "path") { tally.Except("not on a road"); return; }
            if (Has(prop, "crossing=unmarked") || Has(prop, "crossing:markings=no")) { tally.Except("unmarked (nothing to draw)"); return; }
            float hw = RoadNetwork.HalfWidth(road);
            var d = hit.Direction;
            var n = new Vector2(-d.y, d.x);
            var mb = statics.Generated(StaticModels.Layer.Props, hit.Point);
            var white = WorldBuilder.Palette("cloud");
            for (float s = -hw + 0.3f; s < hw - 0.3f; s += 1.2f)
            {
                var c = hit.Point + n * (s + 0.3f);
                var a = d * 1.6f;
                var b = n * 0.3f;
                mb.Quad(MeshBuilder.At(c - a - b, 0.06f), MeshBuilder.At(c + a - b, 0.06f), MeshBuilder.At(c + a + b, 0.06f), MeshBuilder.At(c - a + b, 0.06f), white, Vector3.up);
            }
            tally.Placed++;
            tally.Except("represented by painted stripes, not a Polyfork model");
        }

        // A Bay Wheels dock: a row of hoop racks along the dock, one per six docking points.
        void PlaceDock(Prop prop, Vector2 p, Tally tally)
        {
            var mesh = statics.Mesh("p-bikerack");
            if (mesh == null) { tally.Except("model missing"); return; }
            float heading = prop.heading >= 0f ? prop.heading : FaceStreet(p) + 90f;
            var along = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            int racks = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(prop.capacity, 6) / 6f), 1, 6);
            float length = mesh.bounds.size.x;
            for (int i = 0; i < racks; i++)
            {
                var q = p + along * ((i - (racks - 1) * 0.5f) * (length + 0.2f));
                statics.PlaceAtOrigin(StaticModels.Layer.Props, "p-bikerack", q, Matrix4x4.TRS(MeshBuilder.At(q, 0.03f), Quaternion.Euler(0f, heading - 90f, 0f), Vector3.one));
            }
            tally.Placed++;
        }

        static bool Has(Prop prop, string kv) => prop.tags != null && System.Array.IndexOf(prop.tags, kv) >= 0;

        public void EmitColliders(Transform parent)
        {
            var root = new GameObject("PropColliders");
            root.transform.SetParent(parent, false);
            foreach (var (position, radius, height) in colliders)
            {
                var go = new GameObject("c");
                go.transform.SetParent(root.transform, false);
                go.transform.position = position;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = radius;
                capsule.height = Mathf.Max(height, radius * 2f);
                capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            }
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") h = (h ^ c) * 16777619;
            return h;
        }
    }
}
