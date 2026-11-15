using System;
using UnityEngine;

namespace Dogpatch.Audio
{
    // Player-facing audio and feel settings, persisted in PlayerPrefs. A settings or pause screen
    // calls the setters; every system reads the getters live, so changes apply immediately.
    public static class SoundSettings
    {
        static readonly float[] volumes = new float[6];
        static float master;
        static bool haptics;
        static bool shake;
        static bool loaded;

        public static event Action Changed;

        public static float Master { get { Load(); return master; } }
        public static bool Haptics { get { Load(); return haptics; } }
        public static bool CameraShake { get { Load(); return shake; } }

        public static float Volume(Bus bus)
        {
            Load();
            return volumes[(int)bus];
        }

        public static void SetMaster(float value) => Save(() => master = Mathf.Clamp01(value));
        public static void SetVolume(Bus bus, float value) => Save(() => volumes[(int)bus] = Mathf.Clamp01(value));
        public static void SetHaptics(bool on) => Save(() => haptics = on);
        public static void SetCameraShake(bool on) => Save(() => shake = on);

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = PlayerPrefs.GetFloat("settings.volume.master", 1f);
            for (int i = 0; i < volumes.Length; i++) volumes[i] = PlayerPrefs.GetFloat("settings.volume." + ((Bus)i).ToString().ToLowerInvariant(), 1f);
            haptics = PlayerPrefs.GetInt("settings.haptics", 1) == 1;
            shake = PlayerPrefs.GetInt("settings.cameraShake", 1) == 1;
        }

        static void Save(Action change)
        {
            Load();
            change();
            PlayerPrefs.SetFloat("settings.volume.master", master);
            for (int i = 0; i < volumes.Length; i++) PlayerPrefs.SetFloat("settings.volume." + ((Bus)i).ToString().ToLowerInvariant(), volumes[i]);
            PlayerPrefs.SetInt("settings.haptics", haptics ? 1 : 0);
            PlayerPrefs.SetInt("settings.cameraShake", shake ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
