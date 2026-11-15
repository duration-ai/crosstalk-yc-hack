using UnityEngine;

namespace Dogpatch.Audio
{
    public enum EngineClass { Hatchback, Sedan, Cab, Truck, Cybertruck, RaceCar, Bus, Tram, EBike, Scooter, Cart, Robotaxi }

    // How each vehicle class sounds. Combustion classes run a gearbox model (speed -> gear -> rpm)
    // and crossfade a low and a high loop, each pitched by rpm against the rpm it was synthesized
    // at. Electric classes pitch their whine straight from speed.
    public sealed class EngineProfile
    {
        public EngineClass Class;
        public bool Electric;
        public string Low;
        public string High;
        public float LowRpm;
        public float HighRpm;
        public float IdleRpm;
        public float MaxRpm;
        public int Gears;
        public float TopSpeed;
        public float Volume = 0.8f;
        public float Range = 70f;
        public string Horn;
        public bool Road = true;

        public static EngineProfile For(string id)
        {
            var cls = Classify(id);
            switch (cls)
            {
                case EngineClass.Hatchback: return Ice(cls, "hatchback", 900f, 3000f, 850f, 6500f, 5, 22f, "horn_hatchback");
                case EngineClass.Cab: return Ice(cls, "cab", 620f, 2100f, 650f, 5200f, 4, 22f, "horn_cab", 0.85f);
                case EngineClass.Truck: return Ice(cls, "cab", 560f, 1900f, 600f, 4600f, 4, 20f, "horn_truck", 0.9f);
                case EngineClass.RaceCar: return Ice(cls, "race", 2640f, 7440f, 2000f, 9200f, 6, 40f, "horn_race", 0.95f, 120f);
                case EngineClass.Bus: return Ice(cls, "bus", 500f, 1300f, 600f, 2200f, 4, 13f, "horn_bus", 1f, 90f);
                case EngineClass.Cybertruck: return Ev(cls, "ev_whine", "ev_hum", 30f, "horn_truck", 0.7f);
                case EngineClass.Robotaxi: return Ev(cls, "robotaxi_whine", "ev_hum", 17f, "robotaxi_chime", 0.45f);
                case EngineClass.Tram: return Ev(cls, "tram_run", "ev_hum", 15f, "tram_bell", 0.9f, 90f);
                case EngineClass.EBike: return Ev(cls, "ebike_whir", null, 9.5f, "bike_bell", 0.45f, 35f, false);
                case EngineClass.Scooter: return Ev(cls, "scooter_whir", null, 7f, "horn_scooter", 0.45f, 35f, false);
                case EngineClass.Cart: return Ev(cls, "cart_rattle", null, 6f, null, 0.6f, 35f, false);
                default: return Ice(EngineClass.Sedan, "sedan", 800f, 2600f, 750f, 6000f, 5, 24f, "horn_sedan");
            }
        }

        // Matches the catalog type ids (CROS-21) and the Polyfork model ids by keyword, so this
        // needs no reference to either catalog.
        public static EngineClass Classify(string id)
        {
            id = (id ?? "").ToLowerInvariant();
            if (id.Contains("cyber") || id.Contains("wedge")) return EngineClass.Cybertruck;
            if (id.Contains("race") || id.Contains("gt3") || id.Contains("sports")) return EngineClass.RaceCar;
            if (id.Contains("t-third") || id.Contains("lrv") || id.Contains("tram") || id.Contains("streetcar") || id.Contains("cable")) return EngineClass.Tram;
            if (id.Contains("muni") || (id.Contains("bus") && !id.Contains("shelter"))) return EngineClass.Bus;
            if (id.Contains("cart")) return EngineClass.Cart;
            if (id.Contains("scooter")) return EngineClass.Scooter;
            if (id.Contains("bike") || id.Contains("bicycle")) return EngineClass.EBike;
            if (id.Contains("waymo") || id.Contains("zoox") || id.Contains("robotaxi")) return EngineClass.Robotaxi;
            if (id.Contains("cab") || id.Contains("taxi")) return EngineClass.Cab;
            if (id.Contains("hatch")) return EngineClass.Hatchback;
            if (id.Contains("truck") || id.Contains("pickup") || id.Contains("van")) return EngineClass.Truck;
            return EngineClass.Sedan;
        }

        static EngineProfile Ice(EngineClass cls, string clip, float lowRpm, float highRpm, float idle, float max, int gears, float top, string horn, float volume = 0.8f, float range = 70f)
            => new EngineProfile { Class = cls, Low = clip + "_low", High = clip + "_high", LowRpm = lowRpm, HighRpm = highRpm, IdleRpm = idle, MaxRpm = max, Gears = gears, TopSpeed = top, Horn = horn, Volume = volume, Range = range };

        static EngineProfile Ev(EngineClass cls, string whine, string hum, float top, string horn, float volume, float range = 70f, bool road = true)
            => new EngineProfile { Class = cls, Electric = true, High = whine, Low = hum, TopSpeed = top, Horn = horn, Volume = volume, Range = range, Road = road };

        // Gearbox model: rpm climbs through each gear and drops on the shift.
        public float TargetRpm(float speed, ref int gear)
        {
            float ratio = Mathf.Clamp01(Mathf.Abs(speed) / TopSpeed);
            float span = 1f / Gears;
            int wanted = Mathf.Min((int)(ratio / span), Gears - 1);
            if (wanted > gear && ratio > (gear + 1) * span + 0.01f) gear = wanted;
            else if (wanted < gear && ratio < gear * span - 0.04f) gear = wanted;
            float within = Mathf.Clamp01((ratio - gear * span) / span);
            float floor = gear == 0 ? 0f : 0.42f;
            return Mathf.Lerp(IdleRpm, MaxRpm, floor + (1f - floor) * within);
        }
    }
}
