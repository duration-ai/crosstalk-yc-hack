using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Slipstream
{
    // Metal in linear colour space encodes the OnGUI HUD's colours to sRGB a second time (ink #1C2347
    // shows as #5D6890). Runs first among the OnGUI scripts: turns the sRGB write off for the HUD's
    // repaint and back on before the next frame's world render. Metal only.
    [DefaultExecutionOrder(-10000)]
    public sealed class HudSrgbFix : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal || QualitySettings.activeColorSpace != ColorSpace.Linear) return;
            var go = new GameObject("HudSrgbFix");
            DontDestroyOnLoad(go);
            go.AddComponent<HudSrgbFix>();
            Debug.Log("[Hud] sRGB write off during OnGUI (Metal, linear)");
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) GL.sRGBWrite = false;
        }

        void LateUpdate() => GL.sRGBWrite = true;
    }
}
