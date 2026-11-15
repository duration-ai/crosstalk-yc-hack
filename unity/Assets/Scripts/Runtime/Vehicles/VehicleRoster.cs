using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // What a vehicle is: its type, paint and, for two-wheelers, who is riding it.
    public sealed class VehicleIdentity : MonoBehaviour
    {
        public VehicleSpec Spec;
        public int Paint;
        public GameObject Rider;
    }

    // The one way to make any vehicle in the roster, and to turn an existing one into another type in
    // place (CROS-17's Swap world change: VehicleRoster.SwapInto(car, VehicleTypes.ShoppingCart)).
    public static class VehicleRoster
    {
        static int nextId = 1;
        static Material material;
        static GlbAsset playerModel;

        // CROS-23's damage and barging read mass, durability and barge strength from the roster's
        // handling profiles; vehicles outside the roster keep its per-model table.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ProvideSpecs()
        {
            Smash.VehicleSpecs.Provider = car =>
            {
                if (car == null || car.GetComponent<VehicleIdentity>() == null) return null;
                var h = car.Handling;
                return new Smash.VehicleSpec { Mass = h.Mass, Durability = h.Durability, BargeStrength = h.BargeStrength };
            };
        }

        // Set once by the traffic system; defaults to the shared flat shader.
        public static void Configure(Material shared, GlbAsset player)
        {
            material = shared;
            playerModel = player;
        }

        static Material SharedMaterial
        {
            get
            {
                if (material == null) material = new Material(Resources.Load<Shader>("Shaders/FlatVertexColor")) { name = "FlatVertexColor" };
                return material;
            }
        }

        public static CarController Spawn(string type, Vector3 position, float heading, Transform parent = null, int paint = -1, string id = null)
        {
            var spec = VehicleCatalog.Get(type);
            if (paint < 0) paint = UnityEngine.Random.Range(0, 1000);
            var (visual, bounds) = VehicleModels.Build(spec, paint, SharedMaterial);
            var car = CarController.Create(id ?? type + "-" + nextId++, spec.Id, visual, bounds, position, heading);
            if (parent != null) car.transform.SetParent(parent, true);
            Apply(car, spec, paint);
            return car;
        }

        // Morphs car into another type without replacing it: same instance, position, heading and
        // forward speed, so whoever holds the reference (GameController, race bots) keeps it.
        public static CarController SwapInto(CarController car, string type)
        {
            if (car == null) throw new ArgumentNullException(nameof(car));
            var spec = VehicleCatalog.Get(type);
            int paint = UnityEngine.Random.Range(0, 1000);
            var (visual, bounds) = VehicleModels.Build(spec, paint, SharedMaterial);
            car.Rebuild(spec.Id, visual, bounds);
            Apply(car, spec, paint);
            Swapped?.Invoke(car);
            return car;
        }

        // Raised after SwapInto so traffic and audio can pick up the new type.
        public static event Action<CarController> Swapped;

        public static string TypeOf(CarController car)
        {
            var id = car != null ? car.GetComponent<VehicleIdentity>() : null;
            return id != null ? id.Spec.Id : VehicleTypes.Hatchback;
        }

        public static VehicleSpec SpecOf(CarController car)
        {
            var id = car != null ? car.GetComponent<VehicleIdentity>() : null;
            return id != null ? id.Spec : VehicleCatalog.Get(VehicleTypes.Hatchback);
        }

        // Polyfork rig pivots on this body (wheel-fl, steer-fl, door-l, hood, …) for CROS-23's panels.
        public static IReadOnlyList<string> RigParts(CarController car)
        {
            var names = new List<string>();
            foreach (var t in car.GetComponentsInChildren<Transform>(true))
            {
                if (t == car.transform || t.GetComponent<Renderer>() != null || t.name.EndsWith("-mesh")) continue;
                if (t.parent == car.transform || t.name.StartsWith("sensor") || t.name == "rider" || t.name == "lidar") continue;
                names.Add(t.name);
            }
            return names;
        }

        public static Transform RigPart(CarController car, string name)
        {
            foreach (var t in car.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static void Apply(CarController car, VehicleSpec spec, int paint)
        {
            car.Handling = spec.Handling;
            car.gameObject.name = spec.Title + " " + car.VehicleId;
            var identity = car.GetComponent<VehicleIdentity>();
            if (identity == null) identity = car.gameObject.AddComponent<VehicleIdentity>();
            identity.Spec = spec;
            identity.Paint = paint;
            if (identity.Rider != null) UnityEngine.Object.Destroy(identity.Rider);
            identity.Rider = null;
            if (car.GetComponent<VehicleAudio>() == null) car.gameObject.AddComponent<VehicleAudio>();
            var wash = car.GetComponent<HoverWash>();
            if (spec.Handling.HoverHeight > 0f && wash == null) car.gameObject.AddComponent<HoverWash>();
            if (spec.Handling.HoverHeight <= 0f && wash != null) UnityEngine.Object.Destroy(wash);
            if (spec.Rider) identity.Rider = MountCourier(car, spec);
        }

        static GameObject MountCourier(CarController car, VehicleSpec spec)
        {
            var asset = VehicleModels.Asset("Vehicles/courier-rider");
            if (asset == null) return null;
            var recolor = new Dictionary<string, string>
            {
                ["#c2582a"] = spec.RiderPack, ["#466a61"] = "#3b4250", ["#3e5f57"] = "#323845",
                ["#262d35"] = spec.RiderAccent == spec.RiderPack ? "#262d35" : spec.RiderAccent,
                ["#34313a"] = spec.RiderAccent,
            };
            return Riders.Mount(car.transform, spec.Id, asset, spec.Id + " rider", recolor, SharedMaterial);
        }

        // The player's own figure on a two-wheeler or behind the cart while they ride it.
        public static GameObject MountPlayer(CarController car)
        {
            var spec = SpecOf(car);
            if (playerModel == null || !(spec.TwoWheeler || spec.Id == VehicleTypes.ShoppingCart)) return null;
            return Riders.Mount(car.transform, spec.Id, playerModel, "player", null, SharedMaterial);
        }
    }
}
