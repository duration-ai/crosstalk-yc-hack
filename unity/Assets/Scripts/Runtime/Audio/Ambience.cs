using UnityEngine;

namespace Dogpatch.Audio
{
    // Layered urban ambience: each layer's level comes from where the listener is in Dogpatch (world
    // metres, x east, z north, from the committed OSM extract), the hour, and the weather. Only the
    // loudest MaxLayers play, the whole bed sits low and ducks while you drive. Spot sounds (gulls
    // near the bay, the foghorn, Caltrain at 22nd St, T Third bells on 3rd St, a far siren) are
    // scheduled on top.
    public sealed class Ambience : MonoBehaviour
    {
        // Easternmost land x per 100 m band of z, from data/dogpatch land polygons: the shoreline.
        static readonly float[] ShoreX = { 725f, 725f, 721f, 476f, 639f, 656f, 608f, 602f, 625f, 619f, 558f, 552f, 633f, 634f, 239f, 157f };
        const float ShoreZ0 = -900f;
        static readonly Vector3 Caltrain22nd = new Vector3(-372f, 0f, 110f);
        static readonly Vector3 Pier70 = new Vector3(520f, 0f, 160f);
        static readonly Vector3 FogBank = new Vector3(1100f, 0f, 150f);
        const int MaxLayers = 4;
        const float BedGain = 0.7f;

        struct Layer
        {
            public string Clip;
            public SoundLoop Loop;
            public float Level;
        }

        Layer[] layers;
        readonly float[] targets = new float[8];
        readonly float[] ranked = new float[8];
        float nextGull, nextFoghorn, nextCaltrain, nextSiren, nextTramBell;

        public float WaterWeight { get; private set; }

        void Start()
        {
            string[] clips = { "city_base", "third_st_traffic", "pier_wind", "bay_water", "freeway_hum", "rain", "snow", "construction" };
            layers = new Layer[clips.Length];
            for (int i = 0; i < clips.Length; i++) layers[i].Clip = clips[i];
            nextGull = Time.time + 4f;
            nextFoghorn = Time.time + 6f;
            nextCaltrain = Time.time + Random.Range(40f, 80f);
            nextSiren = Time.time + Random.Range(90f, 160f);
            nextTramBell = Time.time + Random.Range(20f, 50f);
        }

        static float Near(float distance, float full, float silent) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(full, silent, distance));

        void Update()
        {
            var p = SoundSystem.ListenerPosition;
            float hour = GameEvents.Hour, rain = GameEvents.Rain, snow = GameEvents.Snow;
            float night = Mathf.Max(Mathf.InverseLerp(19.5f, 21f, hour), Mathf.InverseLerp(6.5f, 5f, hour));
            float day = 1f - night;

            float bandF = Mathf.Clamp((p.z - ShoreZ0) / 100f, 0f, ShoreX.Length - 1.001f);
            int band = (int)bandF;
            float shore = Mathf.Lerp(ShoreX[band], ShoreX[band + 1], bandF - band);
            WaterWeight = Near(shore - p.x, 20f, 150f);
            float third = Near(Mathf.Abs(p.x - (3.5f + p.z * 0.083f)), 18f, 160f);
            float freeway = Near(Mathf.Abs(p.x - (-483f + p.z * 0.077f)), 40f, 420f);
            float pier = Near(Vector3.Distance(new Vector3(p.x, 0f, p.z), Pier70), 60f, 150f);

            targets[0] = 0.4f;
            targets[1] = third * (0.55f + 0.25f * day) * (night > 0.5f ? 0.6f : 1f);
            targets[2] = Mathf.Max(0.12f, Mathf.Max(WaterWeight, pier) * 0.5f) * (0.7f + 0.3f * rain);
            targets[3] = WaterWeight * 0.35f;
            targets[4] = 0.15f + 0.5f * freeway;
            targets[5] = rain * 0.7f;
            targets[6] = snow * 0.5f;
            targets[7] = day * 0.18f * (0.4f + 0.6f * pier);

            float duck = BedGain * (1f - 0.45f * DriveAmount());
            System.Array.Copy(targets, ranked, targets.Length);
            System.Array.Sort(ranked);
            float cut = ranked[ranked.Length - MaxLayers];
            int kept = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                bool keep = targets[i] >= cut && targets[i] > 0.01f && kept < MaxLayers;
                if (keep) kept++;
                Set(i, keep ? targets[i] * duck : 0f);
            }

            if (Time.time > nextGull)
            {
                nextGull = Time.time + Random.Range(3f, 9f);
                if (day > 0.5f && Mathf.Max(WaterWeight, pier) > 0.35f && rain < 0.5f)
                {
                    var offset = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(15f, 45f);
                    SoundSystem.Play("gull", p + offset + Vector3.up * 12f, Bus.Ambience, Random.Range(0.4f, 0.8f), Random.Range(0.9f, 1.15f), 60, 120f);
                }
            }
            if (GameEvents.FogBank && Time.time > nextFoghorn)
            {
                nextFoghorn = Time.time + Random.Range(25f, 40f);
                PlayFoghorn();
            }
            if (Time.time > nextCaltrain)
            {
                nextCaltrain = Time.time + Random.Range(90f, 150f);
                PlayCaltrain();
            }
            if (Time.time > nextTramBell)
            {
                nextTramBell = Time.time + Random.Range(45f, 110f);
                if (third > 0.2f)
                {
                    var onThird = new Vector3(3.5f + p.z * 0.083f, 2f, p.z + Random.Range(-80f, 80f));
                    SoundSystem.Play("tram_bell", onThird, Bus.Ambience, 0.7f, 1f, 70, 250f);
                }
            }
            if (Time.time > nextSiren)
            {
                nextSiren = Time.time + Random.Range(120f, 240f);
                var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                SoundSystem.Play("distant_siren", p + dir * 350f, Bus.Ambience, 0.5f, Random.Range(0.95f, 1.05f), 40, 800f);
            }
        }

        // 0 on foot or parked, 1 at 20 m/s in the player's car.
        static float DriveAmount()
        {
            var game = SoundSystem.Game;
            if (game == null || !game.Driving || game.View == null) return 0f;
            var car = game.View.GetComponent<Gameplay.FollowCamera>()?.Target?.GetComponent<Gameplay.CarController>();
            return car != null ? Mathf.Clamp01(0.4f + Mathf.Abs(car.Speed) / 20f * 0.6f) : 0.4f;
        }

        public void PlayFoghorn() => SoundSystem.Play("foghorn", FogBank, Bus.Ambience, 1f, 1f, 90, 3000f);

        public void PlayCaltrain()
        {
            SoundSystem.Play("caltrain_pass", Caltrain22nd, Bus.Ambience, 0.9f, 1f, 80, 700f);
            SoundSystem.Play("caltrain_horn", Caltrain22nd + Vector3.up * 4f, Bus.Ambience, 1f, 1f, 90, 1400f);
        }

        void Set(int i, float level)
        {
            ref var layer = ref layers[i];
            layer.Level = Mathf.MoveTowards(layer.Level, level, Time.deltaTime * 0.5f);
            if (layer.Level > 0.01f && layer.Loop == null) layer.Loop = SoundSystem.Loop(layer.Clip, Bus.Ambience, null, 0f, 240, 0f);
            if (layer.Loop == null) return;
            layer.Loop.Volume = layer.Level;
            if (layer.Level <= 0.01f && level <= 0.01f)
            {
                layer.Loop.Stop(0.5f);
                layer.Loop = null;
            }
        }
    }
}
