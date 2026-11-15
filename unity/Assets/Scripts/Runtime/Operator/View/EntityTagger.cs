using System.Collections.Generic;
using Dogpatch.Ambient;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using Dogpatch.Traffic;
using Dogpatch.Vehicles;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Puts EntityTags on the city and its people. City pieces come from the static instance index
    // in build order, which every client reproduces from the same extract: buildings (one tag per
    // footprint, bld_<n>), trees (tree_<n>), lamps and signals (light_<n>) and the AI billboards
    // (bb_<n>); the YC office (the "yc" landmark's footprint) is protected. Racers' cars are
    // car:<playerId> (the caller's own car uses the Convex player id the Operator knows it by),
    // parked cars car_<vehicleId>, traffic traffic_<n> and pedestrians ped_<n>. TagDynamic picks up
    // anything spawned since.
    public static class EntityTagger
    {
        public static string MyPlayerId = "";

        static readonly Dictionary<string, (string cls, string desc)> CarKinds = new Dictionary<string, (string, string)>
        {
            { "compact-hatchback-386417", ("car", "compact hatchback") },
            { "yellow-cab-8df4a0", ("car", "yellow cab") },
            { "town-car-56eeb6", ("car", "town car") },
            { "police-cruiser-a2d25e", ("car", "police cruiser") },
            { "family-crossover-suv-42027f", ("car", "SUV") },
            { "pickup-truck-a34bf2", ("car", "pickup truck") },
            { "delivery-truck-a9537e", ("vehicle", "delivery truck") },
            { "family-minivan-924de0", ("car", "minivan") },
        };

        public static void TagWorld(GameController game)
        {
            var index = game.World.Instances;
            if (index == null) return;
            string yc = null;
            if (game.Data?.landmarks != null)
                foreach (var l in game.Data.landmarks)
                    if (l.kind == "yc" && !string.IsNullOrEmpty(l.footprintId)) yc = l.footprintId;

            var root = new GameObject("Entities").transform;
            root.SetParent(game.World.Root.transform, false);
            var buildings = new Dictionary<string, (EntityTag tag, string variant)>();
            var colors = new Dictionary<MeshFilter, Color[]>();
            int trees = 0, lights = 0, boards = 0;
            EntityTag ycTag = null;

            EntityTag Make(string id, string cls, string desc, string color, StaticInstance part)
            {
                var go = new GameObject(id);
                go.transform.SetParent(root, false);
                var tag = EntityTag.Add(go, id, cls, desc, color);
                tag.Span = new MeshSpan { Index = index };
                tag.Span.Add(part);
                go.transform.position = new Vector3(part.WorldBounds.center.x, part.WorldBounds.min.y, part.WorldBounds.center.z);
                return tag;
            }

            foreach (var part in index.All)
            {
                if (part.Chunk == null) continue;
                switch (part.Kind)
                {
                    case "building":
                        string key = string.IsNullOrEmpty(part.SourceId) ? "#" + part.Id : part.SourceId;
                        if (buildings.TryGetValue(key, out var b))
                        {
                            b.tag.Span.Add(part);
                            continue;
                        }
                        var tag = Make("bld_" + buildings.Count, "building", BuildingDesc(part.Variant), ColorOf(part, colors), part);
                        buildings[key] = (tag, part.Variant);
                        if (key == yc)
                        {
                            tag.Desc = "YC building";
                            tag.Protected = true;
                            ycTag = tag;
                        }
                        break;
                    case "tree":
                        Make("tree_" + trees++, "tree", "street tree", "green", part);
                        break;
                    case "prop":
                        if (part.Variant == "p-lamp") Make("light_" + lights++, "streetlight", "street light", "grey", part);
                        else if (part.Variant == "p-signal") Make("light_" + lights++, "streetlight", "traffic light", "grey", part);
                        break;
                    case "landmark":
                        if (part.Variant == "billboard") Make("bb_" + boards++, "billboard", "AI billboard", ColorOf(part, colors), part);
                        break;
                }
            }
            foreach (var (tag, _) in buildings.Values)
            {
                var c = tag.Span.Bounds.center;
                tag.transform.position = new Vector3(c.x, tag.Span.Bounds.min.y, c.z);
            }

            Debug.Log($"[Operator] tagged {EntityTag.All.Count} entities ({buildings.Count} buildings, {trees} trees, {lights} lights, {boards} billboards){(ycTag != null ? ", YC is " + ycTag.Id : ", no YC building found")}");
        }

        static string BuildingDesc(string variant)
        {
            var name = (variant ?? "").StartsWith("b-") ? variant.Substring(2) : variant ?? "";
            int dash = name.IndexOf('-');
            var kind = dash > 0 ? name.Substring(0, dash) : name;
            return string.IsNullOrEmpty(kind) ? "building" : kind + " building";
        }

        static string ColorOf(StaticInstance part, Dictionary<MeshFilter, Color[]> cache)
        {
            if (!cache.TryGetValue(part.Chunk, out var all)) cache[part.Chunk] = all = part.Chunk.sharedMesh.colors;
            if (all.Length == 0) return "grey";
            var sum = Color.black;
            int step = Mathf.Max(1, part.VertexCount / 64), n = 0;
            for (int i = 0; i < part.VertexCount; i += step, n++) sum += all[part.VertexStart + i];
            return ColorWords.NearestLinear(sum / Mathf.Max(1, n));
        }

        public static void TagDynamic(GameController game, RaceDirector race)
        {
            var mine = race != null ? race.MyCar : null;
            if (game != null)
                foreach (var car in game.Cars.Values)
                {
                    if (car == null || car == mine || car.GetComponent<EntityTag>() != null) continue;
                    var (cls, desc) = Kind(car.ModelId);
                    EntityTag.Add(car.gameObject, "car_" + car.VehicleId, cls, desc, PaintColor(car));
                }
            if (mine != null)
            {
                var id = "car:" + (string.IsNullOrEmpty(MyPlayerId) ? race.MyId : MyPlayerId);
                var tag = mine.GetComponent<EntityTag>();
                if (tag == null || tag.Id != id)
                {
                    var (cls, desc) = Kind(mine.ModelId);
                    EntityTag.Add(mine.gameObject, id, cls, "your " + desc, ColorWords.Nearest(SlipstreamColor(race.MySlot)), false, "me");
                }
            }
            if (race?.Last?.racers != null)
                foreach (var racer in race.Last.racers)
                {
                    if (racer.playerId == race.MyId) continue;
                    var car = GameObject.Find("Car " + racer.displayName);
                    if (car != null && car.GetComponent<EntityTag>() == null)
                        EntityTag.Add(car, "car:" + racer.playerId, "car", racer.displayName + "'s race car", ColorWords.Nearest(SlipstreamColor(racer.slot)), false, "player");
                    var body = GameObject.Find("Racer " + racer.displayName);
                    if (body != null && body.GetComponent<EntityTag>() == null)
                        EntityTag.Add(body, "racer:" + racer.playerId, "person", "racer " + racer.displayName, ColorWords.Nearest(SlipstreamColor(racer.slot)), false, "player");
                }
            foreach (var traffic in Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
            {
                if (traffic.GetComponent<EntityTag>() != null) continue;
                var car = traffic.GetComponent<CarController>();
                var (cls, desc) = Kind(car.ModelId);
                EntityTag.Add(traffic.gameObject, car.VehicleId.Replace("traffic-", "traffic_"), cls, desc, PaintColor(car));
            }
            if (TrafficSystem.Instance != null)
                foreach (var agent in TrafficSystem.Instance.Agents)
                {
                    if (agent == null || agent.Car == null || agent.GetComponent<EntityTag>() != null) continue;
                    var (cls, desc) = Kind(agent.Spec);
                    EntityTag.Add(agent.gameObject, "traffic_" + nextTraffic++, cls, desc, PaintColor(agent.Car));
                }
            if (PedestrianSystem.Instance != null)
                foreach (var ped in PedestrianSystem.Instance.Active)
                {
                    var id = "ped_" + ped.Id;
                    var tag = ped.GetComponent<EntityTag>();
                    if (tag != null && tag.Id == id) continue;
                    var top = ped.Outfit.Top;
                    var desc = ped.Role == PedRole.Skater ? "skateboarder" : ped.Role == PedRole.Cart ? "person pushing a cart" : "pedestrian";
                    EntityTag.Add(ped.gameObject, id, "pedestrian", desc, ColorWords.NearestLinear(new Color(top.x, top.y, top.z)));
                }
        }

        static int nextTraffic;

        static (string cls, string desc) Kind(VehicleSpec spec)
        {
            if (spec == null) return ("car", "car");
            var desc = spec.Title.ToLowerInvariant();
            if (spec.Id == VehicleTypes.Zoox) return ("zoox", desc);
            if (spec.Robotaxi) return ("waymo", desc);
            if (spec.Id == VehicleTypes.MuniBus || spec.Id == VehicleTypes.TThird) return ("bus", desc);
            if (spec.Id == VehicleTypes.DoorDashBike || spec.Id == VehicleTypes.UberEatsBike) return ("delivery_rider", desc);
            if (spec.TwoWheeler || spec.Id == VehicleTypes.ShoppingCart) return ("bike", desc);
            return ("car", desc);
        }

        static Color SlipstreamColor(int slot) => Slipstream.SlipstreamHud.Hex(RaceDirector.SlotHex[Mathf.Clamp(slot, 0, RaceDirector.SlotHex.Length - 1)]);

        static (string cls, string desc) Kind(string modelId) =>
            modelId != null && CarKinds.TryGetValue(modelId, out var kind) ? kind : VehicleCatalog.Has(modelId) ? Kind(VehicleCatalog.Get(modelId)) : ("car", "car");

        static string PaintColor(CarController car)
        {
            var knobs = PaintJob.Knobs(car.ModelId);
            if (knobs?.paint == null || knobs.paint.Length == 0) return "white";
            ColorUtility.TryParseHtmlString(knobs.paint[0], out var c);
            return ColorWords.Nearest(c);
        }
    }
}
