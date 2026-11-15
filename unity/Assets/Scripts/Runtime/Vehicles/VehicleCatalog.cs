using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;

namespace Dogpatch.Vehicles
{
    // Type ids for every vehicle in the roster, as used by VehicleRoster.Spawn and SwapInto.
    public static class VehicleTypes
    {
        public const string WaymoSuv = "waymo-suv";
        public const string WaymoVan = "waymo-van";
        public const string Zoox = "zoox";
        public const string Cybertruck = "cybertruck";
        public const string RaceCar = "race-car";
        public const string SportsCar = "sports-car";
        public const string FlyingCar = "flying-car";
        public const string CableCar = "cable-car";
        public const string Lowrider = "lowrider";
        public const string MonsterTruck = "monster-truck";
        public const string ArtCar = "art-car";
        public const string DuckBoat = "duck-boat";
        public const string FireTruck = "fire-truck";
        public const string MuniBus = "muni-bus";
        public const string ShoppingCart = "shopping-cart";
        public const string BayWheels = "bay-wheels-ebike";
        public const string Scooter = "scooter";
        public const string DoorDashBike = "doordash-bike";
        public const string UberEatsBike = "ubereats-bike";
        public const string Hatchback = "hatchback";
        public const string Sedan = "sedan";
        public const string YellowCab = "yellow-cab";
        public const string TThird = "t-third-lrv";

        public static readonly string[] All =
        {
            WaymoSuv, WaymoVan, Zoox, Cybertruck, RaceCar, SportsCar, FlyingCar, CableCar, MonsterTruck, Lowrider, ArtCar, MuniBus, ShoppingCart, BayWheels, Scooter,
            DoorDashBike, UberEatsBike, Hatchback, Sedan, YellowCab, TThird, DuckBoat, FireTruck,
        };
    }

    public enum ModelSource { Glb, Procedural }


    // Everything that defines one vehicle type: where its model comes from and how it's repainted,
    // which lamps glow at night, how it drives, and how it sounds.
    public sealed class VehicleSpec
    {
        public string Id;
        public string Title;
        public ModelSource Source;
        public string Resource;
        public string PolyforkId;
        public Dictionary<string, string> Recolor = new Dictionary<string, string>();
        public string PaintKey;
        public string[] Paints = Array.Empty<string>();
        public Dictionary<string, float> Lamps = new Dictionary<string, float>();
        public HandlingProfile Handling;
        public bool Robotaxi;
        public bool TwoWheeler;
        public bool Rider;
        public string RiderPack;
        public string RiderAccent;
        public float CruiseSpeed = 11f;
        public Type Ability;
    }

    public static class VehicleCatalog
    {
        static readonly Dictionary<string, VehicleSpec> specs = new Dictionary<string, VehicleSpec>();

        public static VehicleSpec Get(string type)
        {
            if (type != null && specs.TryGetValue(type, out var spec)) return spec;
            throw new ArgumentException("Unknown vehicle type '" + type + "'. Use one of VehicleTypes.All.", nameof(type));
        }

        public static bool Has(string type) => type != null && specs.ContainsKey(type);

        public static IEnumerable<VehicleSpec> All => specs.Values;

        static HandlingProfile H(float top, float accel, float brake, float turn, float grip, float mass, float durability, float barge, bool passenger = false, float reverse = 7f) =>
            new HandlingProfile
            {
                TopSpeed = top, ReverseSpeed = reverse, Acceleration = accel, Braking = brake, Coasting = 5f, TurnRate = turn,
                Grip = grip, Mass = mass, Durability = durability, BargeStrength = barge, PassengerOnly = passenger,
            };

        static void Add(VehicleSpec spec)
        {
            spec.Handling = Pace.Apply(spec.Handling);
            spec.CruiseSpeed *= Pace.Current.trafficSpeedScale;
            specs[spec.Id] = spec;
        }

        static VehicleCatalog()
        {
            Add(new VehicleSpec
            {
                Id = VehicleTypes.WaymoSuv, Title = "Robotaxi SUV", Source = ModelSource.Glb, Resource = "Vehicles/waymo-suv", PolyforkId = "family-crossover-suv-42027f",
                Recolor = { ["#55636d"] = "#f4f4f0", ["#3e4a52"] = "#26292e", ["#a9b4b9"] = "#2b2e33", ["#7b8a93"] = "#3a3f46", ["#8fbfd9"] = "#2c4fa3", ["#cfd6d6"] = "#f4f4f0" },
                Lamps = { ["#e8e7e1"] = 0.8f, ["#b9553f"] = 0.55f },
                Handling = H(17f, 4.5f, 12f, 62f, 0.95f, 2200f, 80f, 0.4f, true), Robotaxi = true, CruiseSpeed = 9f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.WaymoVan, Title = "Robotaxi van", Source = ModelSource.Glb, Resource = "Vehicles/waymo-van", PolyforkId = "kei-van-211ed5",
                Recolor = { ["#f5f6f7"] = "#c9def3", ["#2e3134"] = "#23356e", ["#f0c24b"] = "#f7f8f9", ["#4e5459"] = "#2b2e33", ["#3c4145"] = "#26292e" },
                Lamps = { ["#5b6e8c"] = 0.8f, ["#d6452f"] = 0.55f },
                Handling = H(16f, 4f, 12f, 60f, 0.95f, 2400f, 85f, 0.45f), Robotaxi = true, CruiseSpeed = 8.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.Zoox, Title = "Robotaxi pod", Source = ModelSource.Procedural,
                Handling = H(15f, 4.5f, 12f, 85f, 0.95f, 2000f, 70f, 0.35f, true), Robotaxi = true, CruiseSpeed = 8.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.Cybertruck, Title = "Steel wedge truck", Source = ModelSource.Procedural,
                Handling = H(24f, 9f, 15f, 62f, 0.9f, 3100f, 160f, 0.85f), CruiseSpeed = 12f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.RaceCar, Title = "Race car", Source = ModelSource.Glb, Resource = "Vehicles/race-car", PolyforkId = "model-of-a-gt3-32b70d",
                Recolor = { ["#e4e5e1"] = "#ffcb1f", ["#33a3c8"] = "#1c2347", ["#3a4247"] = "#2c4fa3" },
                Lamps = { ["#3d454b"] = 0.8f, ["#6b1d1f"] = 0.6f },
                Handling = H(36f, 15f, 22f, 95f, 1f, 1250f, 30f, 0.1f), CruiseSpeed = 14f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.SportsCar, Title = "Sports car", Source = ModelSource.Glb, Resource = "Vehicles/race-car", PolyforkId = "model-of-a-gt3-32b70d",
                Recolor = { ["#e4e5e1"] = "#e0182d", ["#33a3c8"] = "#15161a", ["#3a4247"] = "#15161a" },
                Lamps = { ["#3d454b"] = 0.8f, ["#6b1d1f"] = 0.6f },
                Handling = H(28f, 12f, 20f, 90f, 1.15f, 1050f, 45f, 0.2f), CruiseSpeed = 13f,
            });
            var hover = H(25f, 10f, 18f, 85f, 1.05f, 900f, 40f, 0.15f);
            hover.HoverHeight = 3.75f;
            Add(new VehicleSpec { Id = VehicleTypes.Lowrider, Title = "Lowrider", Source = ModelSource.Procedural, Handling = H(23f, 9f, 14f, 72f, 0.95f, 1750f, 75f, 0.5f), CruiseSpeed = 10f, Ability = typeof(Hydraulics) });
            Add(new VehicleSpec { Id = VehicleTypes.ArtCar, Title = "Art car", Source = ModelSource.Procedural, Handling = H(19f, 6f, 10f, 55f, 0.9f, 3800f, 200f, 0.9f), CruiseSpeed = 7f, Ability = typeof(Flamethrower) });
            Add(new VehicleSpec { Id = VehicleTypes.FlyingCar, Title = "Flying car", Source = ModelSource.Procedural, Handling = hover, CruiseSpeed = 12f });
            var cable = H(11f, 2f, 5f, 28f, 1f, 16000f, 1e6f, 1f, false, 3f);
            cable.Coasting = 0.8f;
            Add(new VehicleSpec { Id = VehicleTypes.MonsterTruck, Title = "Monster truck", Source = ModelSource.Procedural, Handling = H(24f, 9f, 16f, 70f, 1f, 3800f, 400f, 0.9f), CruiseSpeed = 12f, Ability = typeof(MonsterCrush) });
            Add(new VehicleSpec { Id = VehicleTypes.CableCar, Title = "Cable car", Source = ModelSource.Procedural, Handling = cable, CruiseSpeed = 6f });
            var duck = H(19f, 6.5f, 12f, 58f, 0.9f, 3400f, 110f, 0.7f);
            duck.Draft = 1.5f;
            Add(new VehicleSpec { Id = VehicleTypes.DuckBoat, Title = "Duck boat", Source = ModelSource.Procedural, Handling = duck, CruiseSpeed = 9f, Ability = typeof(DuckBoat) });
            Add(new VehicleSpec { Id = VehicleTypes.FireTruck, Title = "Fire truck", Source = ModelSource.Procedural, Handling = H(22f, 7f, 16f, 60f, 1.0f, 5200f, 140f, 0.85f), CruiseSpeed = 10f, Ability = typeof(FireTruck) });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.MuniBus, Title = "Muni bus", Source = ModelSource.Glb, Resource = "Vehicles/muni-bus", PolyforkId = "city-bus-49fd97",
                Recolor = { ["#ece5d3"] = "#d5d9de", ["#6f8fa0"] = "#c8102e", ["#6f8fa2"] = "#2c4fa3", ["#b5aea0"] = "#d5d9de" },
                Lamps = { ["#e8a825"] = 0.7f, ["#a34a38"] = 0.55f },
                Handling = H(13f, 2.5f, 7f, 38f, 1f, 14000f, 1e6f, 1f, false, 4f), CruiseSpeed = 9f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.ShoppingCart, Title = "Shopping cart", Source = ModelSource.Procedural,
                Handling = H(6f, 3.5f, 8f, 140f, 0.7f, 40f, 25f, 0f, false, 3f), CruiseSpeed = 3f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.BayWheels, Title = "Bike-share e-bike", Source = ModelSource.Glb, Resource = "Vehicles/bay-wheels-ebike", PolyforkId = "city-bicycle-5b67d1",
                Recolor = { ["#5b6e8c"] = "#1c1d20", ["#d8dadd"] = "#ff4fa3", ["#4e5459"] = "#2a2c30", ["#b9a88c"] = "#1c1d20", ["#7e4b33"] = "#1c1d20" },
                Handling = H(9.5f, 4f, 10f, 120f, 0.9f, 80f, 15f, 0f, false, 2f), TwoWheeler = true, CruiseSpeed = 6f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.Scooter, Title = "Scooter", Source = ModelSource.Glb, Resource = "Vehicles/scooter", PolyforkId = "stunt-scooter-d98af3",
                Recolor = { ["#2d8fbf"] = "#2ec27e", ["#b3643b"] = "#1c1d20", ["#ff7a1f"] = "#1c1d20", ["#6f2f22"] = "#f7f8f9" },
                Handling = H(7f, 3.5f, 8f, 130f, 0.85f, 70f, 12f, 0f, false, 2f), TwoWheeler = true, CruiseSpeed = 5.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.DoorDashBike, Title = "Delivery rider (red bag)", Source = ModelSource.Glb, Resource = "Vehicles/bay-wheels-ebike", PolyforkId = "city-bicycle-5b67d1",
                Recolor = { ["#5b6e8c"] = "#3a3f46", ["#b9a88c"] = "#2a2c30", ["#7e4b33"] = "#1c1d20" },
                Handling = H(8.5f, 4f, 10f, 120f, 0.9f, 85f, 15f, 0f, false, 2f), TwoWheeler = true, Rider = true, RiderPack = "#e3262e", RiderAccent = "#e3262e",
                CruiseSpeed = 5.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.UberEatsBike, Title = "Delivery rider (black bag)", Source = ModelSource.Glb, Resource = "Vehicles/bay-wheels-ebike", PolyforkId = "city-bicycle-5b67d1",
                Recolor = { ["#5b6e8c"] = "#2a2c30", ["#b9a88c"] = "#2a2c30", ["#7e4b33"] = "#1c1d20" },
                Handling = H(8.5f, 4f, 10f, 120f, 0.9f, 85f, 15f, 0f, false, 2f), TwoWheeler = true, Rider = true, RiderPack = "#1c1d20", RiderAccent = "#2ec27e",
                CruiseSpeed = 5.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.Hatchback, Title = "Hatchback", Source = ModelSource.Glb, Resource = "Polyfork/compact-hatchback-386417", PolyforkId = "compact-hatchback-386417",
                Recolor = { ["#2f3f52"] = "#2c4fa3", ["#5a544b"] = "#3a3f46" }, PaintKey = "#b58941",
                Paints = new[] { "#2d8cff", "#e2603f", "#ffe27a", "#8ce3bf", "#bba6f7", "#ff9c86", "#9ed7ff", "#fff3da" },
                Lamps = { ["#edeae3"] = 0.8f, ["#b83833"] = 0.55f },
                Handling = H(22f, 8f, 16f, 75f, 1f, 1200f, 60f, 0.3f), CruiseSpeed = 11f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.Sedan, Title = "Sedan", Source = ModelSource.Glb, Resource = "Vehicles/sedan", PolyforkId = "boxy-sedan-70s-7d55f7",
                Recolor = { ["#ae8566"] = "#fff3da", ["#2a2d35"] = "#2c4fa3" }, PaintKey = "#2a3a6b",
                Paints = new[] { "#ff9c86", "#8ce3bf", "#bba6f7", "#9ed7ff", "#e2603f", "#2d8cff" },
                Lamps = { ["#ecf1cb"] = 0.8f, ["#d13d34"] = 0.55f },
                Handling = H(23f, 8f, 16f, 72f, 1f, 1450f, 70f, 0.35f), CruiseSpeed = 11.5f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.YellowCab, Title = "Yellow cab", Source = ModelSource.Glb, Resource = "Polyfork/yellow-cab-8df4a0", PolyforkId = "yellow-cab-8df4a0",
                Recolor = { ["#e8a825"] = "#ffcb1f", ["#45525f"] = "#2c4fa3" },
                Lamps = { ["#c1b0a1"] = 0.8f, ["#c7504d"] = 0.55f, ["#cfc6b9"] = 0.6f },
                Handling = H(22f, 8f, 16f, 75f, 1f, 1400f, 65f, 0.35f), CruiseSpeed = 12f,
            });
            Add(new VehicleSpec
            {
                Id = VehicleTypes.TThird, Title = "T Third light rail", Source = ModelSource.Glb, Resource = "Vehicles/lrv-car", PolyforkId = "city-tram-0d092d",
                Recolor = { ["#2f6b4f"] = "#c8102e", ["#f7f5ef"] = "#d5d9de", ["#2e3134"] = "#2c4fa3" },
                Lamps = { ["#f0efec"] = 0.85f, ["#b5462f"] = 0.55f, ["#ece9e2"] = 0.5f },
                Handling = H(15f, 1.2f, 1.6f, 0f, 1f, 40000f, 1e6f, 1f, true), CruiseSpeed = 12f,
            });
        }
    }
}
