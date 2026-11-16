using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Dogpatch.Slipstream.V3
{
    // Offline proof sheet for the v3 pictograms and button family: run the player with
    //   -hud-v3-sheet <file.png>
    // and it draws every glyph plus every plate state over a paper card, saves the screenshot and quits.
    public sealed class HudSheet : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-hud-v3-sheet");
            if (at < 0) return;
            new GameObject("HudSheet").AddComponent<HudSheet>().output = at + 1 < args.Length ? args[at + 1] : "hud-v3-sheet.png";
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(output, tex.EncodeToPNG());
            Debug.Log("[Hud v3] sheet " + output);
            Application.Quit(0);
        }

        void OnGUI()
        {
            GUI.depth = -10000;
            float u = Mathf.Min(Screen.width, Screen.height) / 402f;
            var was = GUI.color;
            GUI.color = new Color(0.93f, 0.95f, 0.99f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = was;

            bool wide = Screen.width > Screen.height;
            int cols = wide ? 14 : 8;
            float cell = (wide ? Screen.width * 0.55f : Screen.width - 16f * u) / cols;
            int n = 0;
            foreach (var name in HudIcons.Names)
            {
                var r = new Rect(8f * u + (n % cols) * cell, 8f * u + (n / cols) * cell, cell, cell);
                HudIcons.Draw(name, new Rect(r.x + cell * 0.15f, r.y + cell * 0.15f, cell * 0.7f, cell * 0.7f), HudPlates.Ink);
                n++;
            }
            float y = 8f * u + Mathf.Ceil(n / (float)cols) * cell + 8f * u;
            float x0 = wide ? Screen.width * 0.58f : 14f * u;
            if (wide) y = 14f * u;
            float x = x0;
            void Next(float w) { x += w + 18f * u; if (x > Screen.width - 120f * u) { x = x0; y += 82f * u; } }
            float t = Time.unscaledTime;
            HudPlates.Call(new Rect(x, y + 10f * u, 96f * u, 64f * u), CallLook.Rest, 3, u); Next(96f * u);
            HudPlates.Call(new Rect(x, y + 10f * u, 96f * u, 64f * u), CallLook.Ringing, 3, u, 1f, 0.5f); Next(96f * u);
            HudPlates.Call(new Rect(x, y + 10f * u, 96f * u, 64f * u), CallLook.Pressed, 3, u); Next(96f * u);
            HudPlates.Call(new Rect(x, y + 10f * u, 150f * u, 64f * u), CallLook.Live, 3, u); Next(150f * u);
            HudPlates.Call(new Rect(x, y + 10f * u, 96f * u, 64f * u), CallLook.OutOfAsks, 0, u); Next(96f * u);
            x = x0; y += 82f * u;
            HudPlates.Ready(new Rect(x, y, 112f * u, 64f * u), ReadyLook.Rest, u); Next(112f * u);
            HudPlates.Ready(new Rect(x, y, 112f * u, 64f * u), ReadyLook.Pressed, u); Next(112f * u);
            HudPlates.Ready(new Rect(x, y, 112f * u, 64f * u), ReadyLook.Locked, u, 2, 4); Next(112f * u);
            x = x0; y += 82f * u;
            HudPlates.Secondary(new Rect(x, y, 84f * u, 56f * u), "retake", false, u); Next(84f * u);
            HudPlates.Secondary(new Rect(x, y, 84f * u, 56f * u), "retake", true, u); Next(84f * u);
            HudPlates.Secondary(new Rect(x, y, 84f * u, 56f * u), "cancel", false, u); Next(84f * u);
            x = x0; y += 70f * u;
            foreach (var icon in new[] { "pause", "close", "back", "recentre", "map", "settings" })
            {
                HudPlates.Small(new Rect(x, y, 48f * u, 48f * u), icon, icon == "close", u);
                x += 54f * u;
            }
        }
    }
}
