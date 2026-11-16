using System;
using System.Collections;
using System.IO;
using Dogpatch.Operator;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // Headless stills of the v3 Operator call (-offline -call-v3-capture <dir>): call ready with two
    // asks, hold to talk with words typing, and she thinks.
    public sealed class CallV3Capture : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-call-v3-capture");
            if (at < 0) return;
            Application.runInBackground = true;
            var go = new GameObject("CallV3Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<CallV3Capture>().output = at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "call-v3");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while (RaceDirector.Instance == null || !RaceDirector.Instance.Setup) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            var shots = new (string name, CallState state, string words, int asks)[]
            {
                ("1-call-ready", CallState.None, "", 2),
                ("2-hold-to-talk", CallState.Talking, "make it night and everyone tiny", 2),
                ("3-thinking", CallState.Waiting, "make it night and everyone tiny", 2),
            };
            foreach (var shot in shots)
            {
                SlipstreamHud.PreviewState = shot.state;
                SlipstreamHud.PreviewWords = shot.words;
                SlipstreamHud.PreviewAsks = shot.asks;
                yield return new WaitForSecondsRealtime(1.2f);
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(output, shot.name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                Debug.Log("[CallV3] captured " + shot.name);
            }
            SlipstreamHud.PreviewState = null;
            Debug.Log("[CallV3] capture done");
            Application.Quit();
        }
    }
}
