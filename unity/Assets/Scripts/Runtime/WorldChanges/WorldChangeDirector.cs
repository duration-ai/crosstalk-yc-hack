using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Race;
using Dogpatch.Vehicles;
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

        const float Blend = 2.5f;

        readonly Dictionary<string, float> wearsOff = new Dictionary<string, float>();
        readonly List<GameObject> spawned = new List<GameObject>();
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
                case "fog": Fog(p.level == "thick"); break;
                case "rain": WorldEnvironment.SetWeather(p.level == "on" ? WeatherKind.Rain : WeatherKind.Sun, 1f, Blend); break;
                case "snow": WorldEnvironment.SetWeather(p.level == "on" ? WeatherKind.Snow : WeatherKind.Sun, 1f, Blend); break;
                case "time": WorldEnvironment.SetTime(Preset(p.timeOfDay), Blend); break;
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
                case "fog": Fog(false); break;
                case "rain":
                case "snow": WorldEnvironment.SetWeather(WeatherKind.Sun, 1f, Blend); break;
                case "traffic": Traffic(false); break;
                case "resize": CarFx.SizeTarget = 1f; break;
            }
        }

        public void ResetAll()
        {
            lastSeq = 0;
            foreach (var type in new List<string>(wearsOff.Keys)) Revert(type);
            wearsOff.Clear();
            Gravity("normal");
            CarFx.Traction = 1f;
            CarFx.SizeTarget = 1f;
            WorldEnvironment.SetTime(TimePreset.Day, 1f);
            Fog(false);
            WorldEnvironment.SetWeather(WeatherKind.Sun, 1f, 1f);
            Traffic(false);
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            Recolor(null);
            CartsBack();
        }

        static void Fog(bool thick) => WorldEnvironment.SetFog(thick ? 1f : 0.35f, Blend);

        static TimePreset Preset(string timeOfDay) =>
            timeOfDay == "golden" ? TimePreset.Golden : timeOfDay == "dusk" ? TimePreset.Dusk : timeOfDay == "night" ? TimePreset.Night : TimePreset.Day;

        void Update()
        {
            foreach (var pair in new List<KeyValuePair<string, float>>(wearsOff))
            {
                if (Time.time < pair.Value) continue;
                wearsOff.Remove(pair.Key);
                Revert(pair.Key);
            }
            if (swappedFrom.Count > 0 && Time.time >= cartsBackAt) CartsBack();
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
            if (targetCar == null) return;
            if (!swappedFrom.ContainsKey(targetCar)) swappedFrom[targetCar] = VehicleRoster.TypeOf(targetCar);
            VehicleRoster.SwapInto(targetCar, VehicleTypes.ShoppingCart);
            cartsBackAt = Time.time + 12f;
        }

        // The punchline: whoever got swapped finishes the swap pushing a shopping cart (CROS-21's
        // vehicle roster), and gets their car back a few seconds later.
        readonly Dictionary<CarController, string> swappedFrom = new Dictionary<CarController, string>();

        void CartsBack()
        {
            foreach (var pair in swappedFrom)
                if (pair.Key != null && !string.IsNullOrEmpty(pair.Value)) VehicleRoster.SwapInto(pair.Key, pair.Value);
            swappedFrom.Clear();
        }
    }
}
