using System;
using System.Collections.Generic;
using System.Reflection;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    // Applies world-change events from the room's log to this device's world. Each event is applied
    // once, by seq; timed effects (catalog "seconds") wear off on their own and everything resets
    // between races.
    public sealed class WorldChangeDirector : MonoBehaviour
    {
        public GameController Game;
        public RaceDirector Race;
        public WorldChangeCatalog Catalog;
        public IWeather Weather;

        const float Blend = 2.5f;

        readonly Dictionary<string, float> wearsOff = new Dictionary<string, float>();
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<CarController> carts = new List<CarController>();
        readonly System.Random random = new System.Random(11);
        Color? paint;
        long lastSeq;
        float cartsBackAt;

        public void Apply(RaceEvent e)
        {
            if (e.seq <= lastSeq) return;
            lastSeq = e.seq;
            var p = e.@params ?? new WorldChangeParams();
            var def = Catalog.Get(e.type);
            float seconds = def != null ? def.seconds : 0f;
            Debug.Log($"[SpeedDial] world change #{e.seq} {e.type} {JsonUtility.ToJson(p)} by {e.displayName}: {e.news}");
            switch (e.type)
            {
                case "gravity": Gravity(p.level); break;
                case "ice": CarFx.Traction = p.level == "on" ? 0.035f : 1f; break;
                case "fog": Weather.SetFog(p.level == "thick", Blend); break;
                case "rain": Weather.SetRain(p.level == "on", Blend); break;
                case "snow": Weather.SetSnow(p.level == "on", Blend); break;
                case "time": Weather.SetTimeOfDay(p.timeOfDay, Blend); break;
                case "traffic": Traffic(p.level == "heavy"); break;
                case "recolor": Recolor(p.hex ?? Catalog.Hex(p.color)); break;
                case "swap": Swap(e.playerId, p.targetPlayerId); break;
                case "resize": CarFx.SizeTarget = p.size == "small" ? 0.6f : p.size == "big" ? 1.6f : 1f; break;
                case "ramp": Ramps(e.playerId, Mathf.Clamp(p.count, 1, 5)); break;
            }
            if (seconds > 0f) wearsOff[e.type] = Time.time + seconds;
            else wearsOff.Remove(e.type);
        }

        void Revert(string type)
        {
            switch (type)
            {
                case "gravity": Gravity("normal"); break;
                case "ice": CarFx.Traction = 1f; break;
                case "fog": Weather.SetFog(false, Blend); break;
                case "rain": Weather.SetRain(false, Blend); break;
                case "snow": Weather.SetSnow(false, Blend); break;
                case "traffic": Traffic(false); break;
                case "resize": CarFx.SizeTarget = 1f; break;
            }
        }

        public void ResetAll()
        {
            foreach (var type in new List<string>(wearsOff.Keys)) Revert(type);
            wearsOff.Clear();
            Gravity("normal");
            CarFx.Traction = 1f;
            CarFx.SizeTarget = 1f;
            Weather.ResetAll(1f);
            Traffic(false);
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            Recolor(null);
            CartsBack();
        }

        void Update()
        {
            foreach (var pair in new List<KeyValuePair<string, float>>(wearsOff))
            {
                if (Time.time < pair.Value) continue;
                wearsOff.Remove(pair.Key);
                Revert(pair.Key);
            }
            if (carts.Count > 0 && Time.time >= cartsBackAt) CartsBack();
        }

        IEnumerable<CarController> AllCars() => FindObjectsByType<CarController>(FindObjectsSortMode.None);

        void Gravity(string level)
        {
            float k = level == "low" ? 0.28f : level == "high" ? 2f : 1f;
            Physics.gravity = new Vector3(0f, -9.81f * k, 0f);
            CarFx.Float = level == "low" ? 1f : 0f;
            if (level != "low") return;
            foreach (var car in AllCars())
                if (car.Driven) CarFx.On(car).Launch(3.2f);
        }

        void Recolor(string hex)
        {
            paint = string.IsNullOrEmpty(hex) ? (Color?)null : PaintJob.Linear(hex);
            foreach (var car in AllCars()) Paint(car);
        }

        public void Paint(CarController car)
        {
            var job = PaintJob.For(car.gameObject, car.ModelId);
            job.SetOverride(paint);
        }

        void Traffic(bool heavy)
        {
            foreach (var go in spawned.ToArray())
            {
                if (go == null || go.GetComponent<TrafficCar>() == null) continue;
                spawned.Remove(go);
                Destroy(go);
            }
            if (!heavy) return;
            var route = Race.Course.Route;
            var models = Game.Models.Role("car");
            int made = 0;
            for (float d = 40f; d < route.Length - 30f && made < 16; d += 22f + (float)random.NextDouble() * 18f)
            {
                bool oncoming = made % 2 == 1;
                float lane = oncoming ? -2.4f : 2.4f;
                var p = route.PointAt(d) + route.RightAt(d) * lane;
                bool clear = true;
                foreach (var racer in Race.RacerBodies())
                    if (Vector2.Distance(RoutePath.Flat(racer.position), p) < 25f) clear = false;
                if (!clear) continue;
                var asset = Game.Models.Get(models[random.Next(models.Count)].id);
                if (asset == null) continue;
                var dir = route.DirectionAt(d) * (oncoming ? -1f : 1f);
                var car = CarController.Create("traffic-" + made, asset, Game.SharedMaterial, new Vector3(p.x, Race.Course.GroundY(p) + 0.05f, p.y), RoutePath.Heading(dir));
                car.transform.SetParent(Game.World.Root.transform, true);
                CarFx.On(car);
                Paint(car);
                var mover = car.gameObject.AddComponent<TrafficCar>();
                mover.Route = route;
                mover.Distance = d;
                mover.Speed = 5f + (float)random.NextDouble() * 4f;
                mover.Lane = lane;
                mover.Oncoming = oncoming;
                spawned.Add(car.gameObject);
                made++;
            }
        }

        void Ramps(string firer, int count)
        {
            var route = Race.Course.Route;
            float from = Race.ProgressOf(firer);
            for (int i = 0; i < count; i++)
            {
                float d = from + 45f + i * 38f;
                if (d > route.Length - 25f) break;
                float lane = (i % 3 - 1) * 2.2f;
                var p = route.PointAt(d) + route.RightAt(d) * lane;
                var ramp = Ramp.Create(Game.World.Root.transform, new Vector3(p.x, Race.Course.GroundY(p) + 0.02f, p.y), RoutePath.Heading(route.DirectionAt(d)), Game.SharedMaterial);
                spawned.Add(ramp.transform.parent.gameObject);
            }
        }

        void Swap(string firer, string target)
        {
            var a = Race.BodyOf(firer);
            var b = Race.BodyOf(target);
            if (a == null || b == null) return;
            var pa = a.position;
            var ha = a.eulerAngles.y;
            var pb = b.position;
            var hb = b.eulerAngles.y;
            Race.MoveBody(firer, pb + Vector3.up * 0.2f, hb);
            Race.MoveBody(target, pa + Vector3.up * 0.2f, ha);

            var targetCar = b.GetComponent<CarController>();
            if (targetCar != null && SwapInto(targetCar, "shopping-cart"))
            {
                carts.Add(targetCar);
                cartsBackAt = Time.time + 12f;
            }
        }

        // The punchline: the racer who got swapped finishes the swap in a shopping cart. The cart
        // comes from the vehicle roster (CROS-21, Dogpatch.Vehicles.VehicleRoster.SwapInto), which
        // lives on another branch; it is looked up at runtime so this branch builds without it.
        static MethodInfo swapInto;
        static bool looked;
        readonly Dictionary<CarController, string> swappedFrom = new Dictionary<CarController, string>();

        bool SwapInto(CarController car, string type)
        {
            if (!looked)
            {
                looked = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var roster = assembly.GetType("Dogpatch.Vehicles.VehicleRoster");
                    if (roster == null) continue;
                    swapInto = roster.GetMethod("SwapInto", new[] { typeof(CarController), typeof(string) });
                    var typeOf = roster.GetMethod("TypeOf", new[] { typeof(CarController) });
                    if (typeOf != null) typeOfMethod = typeOf;
                }
                if (swapInto == null) Debug.Log("[SpeedDial] vehicle roster not in this build; swap moves racers without the shopping cart");
            }
            if (swapInto == null) return false;
            try
            {
                if (typeOfMethod != null && !swappedFrom.ContainsKey(car)) swappedFrom[car] = (string)typeOfMethod.Invoke(null, new object[] { car });
                swapInto.Invoke(null, new object[] { car, type });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SpeedDial] SwapInto failed: " + ex.Message);
                return false;
            }
        }

        static MethodInfo typeOfMethod;

        void CartsBack()
        {
            foreach (var car in carts)
                if (car != null && swappedFrom.TryGetValue(car, out var was) && was != null) SwapInto(car, was);
            carts.Clear();
            swappedFrom.Clear();
        }
    }
}
