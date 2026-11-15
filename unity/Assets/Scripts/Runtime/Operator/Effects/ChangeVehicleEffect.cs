using Dogpatch.Look;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Operator
{
    // change_vehicle: the caller's own ride becomes the roster type the row names, rebuilt in place so
    // it keeps its position, heading and speed (the new type's top speed and handling take over), with
    // a puff. Instant, so it lasts until the next swap. Other devices see it through presence.
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
                Debug.Log("[Operator] change_vehicle: on foot, nothing to swap");
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
    }
}
