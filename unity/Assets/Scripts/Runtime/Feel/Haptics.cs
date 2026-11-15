using System.Runtime.InteropServices;
using Dogpatch.Audio;
using UnityEngine;

namespace Dogpatch.Feel
{
    public enum HapticStyle { Light, Medium, Heavy, Soft, Rigid }

    public enum HapticNotice { Success, Warning, Error }

    // UIImpactFeedbackGenerator / UINotificationFeedbackGenerator on iOS (Plugins/iOS/DogpatchHaptics.mm),
    // a no-op elsewhere. Honours the haptics setting and rate-limits so a smash chain never buzzes
    // continuously.
    public static class Haptics
    {
        const float MinInterval = 0.06f;
        static float next;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void DogpatchHapticImpact(int style, float intensity);
        [DllImport("__Internal")] static extern void DogpatchHapticNotify(int type);
        [DllImport("__Internal")] static extern void DogpatchHapticSelection();
#endif

        public static int Fired { get; private set; }

        public static void Impact(HapticStyle style, float intensity = 1f)
        {
            if (!Ready()) return;
#if UNITY_IOS && !UNITY_EDITOR
            DogpatchHapticImpact((int)style, Mathf.Clamp01(intensity));
#endif
        }

        public static void Notify(HapticNotice notice)
        {
            if (!Ready()) return;
#if UNITY_IOS && !UNITY_EDITOR
            DogpatchHapticNotify((int)notice);
#endif
        }

        public static void Selection()
        {
            if (!Ready()) return;
#if UNITY_IOS && !UNITY_EDITOR
            DogpatchHapticSelection();
#endif
        }

        static bool Ready()
        {
            if (!SoundSettings.Haptics || Time.unscaledTime < next) return false;
            next = Time.unscaledTime + MinInterval;
            Fired++;
            return true;
        }
    }
}
