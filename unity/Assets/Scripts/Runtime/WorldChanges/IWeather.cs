namespace Dogpatch.WorldChanges
{
    // The sky and weather the Time, Fog, Rain and Snow world changes drive. CROS-16's weather and
    // time-of-day controller is the real owner; LocalWeather stands in until it lands.
    public interface IWeather
    {
        // "day" | "golden" | "dusk" | "night"
        void SetTimeOfDay(string timeOfDay, float blendSeconds);

        void SetFog(bool thick, float blendSeconds);

        void SetRain(bool on, float blendSeconds);

        void SetSnow(bool on, float blendSeconds);

        void ResetAll(float blendSeconds);
    }
}
