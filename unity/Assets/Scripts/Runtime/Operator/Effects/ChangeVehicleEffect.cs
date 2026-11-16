using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Race;
using Dogpatch.Traffic;
using Dogpatch.World;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Operator
{
    // change_vehicle: the caller's own ride becomes the roster type the row names, rebuilt in place so
    // it keeps its position, heading and speed (the new type's top speed and handling take over), with
    // a puff. Instant, so it lasts until the next swap. Other devices see it through presence.
    // On foot, the asked-for vehicle appears beside the caller, facing along the road (in a race, the
    // caller's race car is rebuilt as that type on the racing line where they stand), and they get in.
    public sealed class ChangeVehicleEffect : OperatorEffect
    {
        public static string TypeFor(string vehicle)
        {
            switch (vehicle)
            {
                case "sports_car": return VehicleTypes.SportsCar;
                case "race_car": return VehicleTypes.RaceCar;
                case "flying_car": return VehicleTypes.FlyingCar;
                case "cable_car": return VehicleTypes.CableCar;
                case "lowrider": return VehicleTypes.Lowrider;
                case "monster_truck": return VehicleTypes.MonsterTruck;
                case "art_car": return VehicleTypes.ArtCar;
                case "duck_boat": return VehicleTypes.DuckBoat;
                case "fire_truck": return VehicleTypes.FireTruck;
                case "hatchback": return VehicleTypes.Hatchback;
                case "sedan": return VehicleTypes.Sedan;
                case "taxi": return VehicleTypes.YellowCab;
                case "cybertruck": return VehicleTypes.Cybertruck;
                case "waymo": return VehicleTypes.WaymoSuv;
                case "waymo_ojai": return VehicleTypes.WaymoOjai;
                case "van": return VehicleTypes.WaymoVan;
                case "zoox": return VehicleTypes.Zoox;
                case "bus": return VehicleTypes.MuniBus;
                case "bike": return VehicleTypes.BayWheels;
                case "scooter": return VehicleTypes.Scooter;
                case "delivery_bike": return VehicleTypes.DoorDashBike;
                case "shopping_cart": return VehicleTypes.ShoppingCart;
                default: return null;
            }
        }

        public override void Apply(string strength, EffectTarget target)
        {
            var type = TypeFor(Row.vehicle);
            if (type == null)
            {
                Debug.LogWarning($"[Operator] change_vehicle: unknown vehicle '{Row.vehicle}'");
                return;
            }
            var me = EntityTagger.MyPlayerId;
            if (!string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(Row.requestedBy) && Row.requestedBy != me) return;
            var car = Context.Game.CurrentCar;
            if (car == null)
            {
                Arrive(type);
                return;
            }
            var from = VehicleRoster.TypeOf(car);
            if (from == type) return;
            var body = car.GetComponent<Rigidbody>();
            var velocity = body.linearVelocity;
            VehicleRoster.SwapInto(car, type);
            if (!body.isKinematic) body.linearVelocity = velocity;
            MotionFx.Land(car.transform.position, 1f);
            Debug.Log($"[Operator] change_vehicle: {from} -> {type} at {velocity.magnitude:F1} m/s (top {car.Handling.TopSpeed:F1} m/s)");
        }

        void Arrive(string type)
        {
            var game = Context.Game;
            var at = game.Player.transform.position;
            var here = new Vector2(at.x, at.z);
            var race = Context.Race;
            CarController car;
            Vector3 position;
            float heading;
            if (race != null && race.Gridded && race.MyCar != null && race.Course?.Route != null)
            {
                var route = race.Course.Route;
                float d = route.Project(here);
                var p = route.PointAt(d);
                position = new Vector3(p.x, race.Course.RouteY(d) + 0.05f, p.y);
                heading = RoutePath.Heading(route.DirectionAt(d));
                car = race.MyCar;
                if (VehicleRoster.TypeOf(car) != type) VehicleRoster.SwapInto(car, type);
                car.Place(position, heading);
            }
            else
            {
                var graph = TrafficSystem.Instance != null ? TrafficSystem.Instance.Graph : null;
                float s = 0f;
                var lane = graph?.Nearest(here, Vector2.zero, 20f, out s, out _);
                if (lane != null)
                {
                    var p = lane.Path.Sample(s, out var forward);
                    position = new Vector3(p.x, RoadSurface.GroundY(p) + 0.05f, p.y);
                    heading = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
                }
                else
                {
                    position = at + game.Player.transform.right * 2.5f;
                    heading = game.Player.transform.eulerAngles.y;
                }
                car = VehicleRoster.Spawn(type, position, heading, game.World.Root.transform);
            }
            MotionFx.Land(position, 1f);
            _ = game.EnterCar(car);
            Debug.Log($"[Operator] change_vehicle: on foot -> {type} at {position} heading {heading:F0}{(car == race?.MyCar ? " on the racing line" : "")}");
        }
    }
}
