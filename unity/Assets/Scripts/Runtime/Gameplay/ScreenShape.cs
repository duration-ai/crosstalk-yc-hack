using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Follows device rotation. Keeps the render resolution at the same pixel budget in portrait
    // and landscape (the long side capped at 1400 px on phones), and raises Changed whenever the
    // screen changes shape so input can drop touches that began in the old layout. Runs before
    // everything else so nothing reads a touch against the old halves.
    [DefaultExecutionOrder(-1000)]
    public sealed class ScreenShape : MonoBehaviour
    {
        const int MaxLongSide = 1400;

        public static event Action Changed;

        int nativeLong, nativeShort;
        int width, height;
        bool portrait;

        void Awake()
        {
            nativeLong = Mathf.Max(Screen.width, Screen.height);
            nativeShort = Mathf.Min(Screen.width, Screen.height);
            width = Screen.width;
            height = Screen.height;
            portrait = Portrait();
            Fit();
        }

        static bool Portrait()
        {
            if (Application.isMobilePlatform)
            {
                var o = Screen.orientation;
                if (o == ScreenOrientation.Portrait || o == ScreenOrientation.PortraitUpsideDown) return true;
                if (o == ScreenOrientation.LandscapeLeft || o == ScreenOrientation.LandscapeRight) return false;
            }
            return Screen.height > Screen.width;
        }

        void Fit()
        {
            if (!Application.isMobilePlatform || nativeLong <= MaxLongSide) return;
            float k = MaxLongSide / (float)nativeLong;
            int l = MaxLongSide, s = Mathf.RoundToInt(nativeShort * k);
            Screen.SetResolution(portrait ? s : l, portrait ? l : s, true);
        }

        void Update()
        {
            bool p = Portrait();
            if (p != portrait)
            {
                portrait = p;
                Fit();
            }
            if (Screen.width == width && Screen.height == height) return;
            Debug.Log($"[Screen] {width}x{height} -> {Screen.width}x{Screen.height} ({(p ? "portrait" : "landscape")})");
            width = Screen.width;
            height = Screen.height;
            Changed?.Invoke();
        }
    }
}
