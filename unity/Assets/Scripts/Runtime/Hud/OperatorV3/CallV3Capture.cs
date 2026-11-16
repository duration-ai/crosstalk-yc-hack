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

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[CallV3] captured " + name);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while (RaceDirector.Instance == null || !RaceDirector.Instance.Setup || RaceDirector.Instance.Last == null) yield return null;
            var race = RaceDirector.Instance;
            var game = race.Game;
            var spot = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z) + new Vector2(120f, 90f);
            if (game.World.Roads.Nearest(spot, 60f, out var hit))
            {
                game.Player.Teleport(new Vector3(hit.Point.x, 0.3f, hit.Point.y), Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg);
                game.Follow.Snap(game.Player.transform, false);
            }
            yield return new WaitForSecondsRealtime(8f);
            var shots = new (string name, CallState state, string words, int asks, bool refused, string line)[]
            {
                ("1-call-ready", CallState.None, "", 2, false, ""),
                ("2-hold-to-talk", CallState.Talking, "make it night and everyone tiny", 2, false, ""),
                ("3-thinking", CallState.Waiting, "make it night and everyone tiny", 2, false, ""),
                ("4-done", CallState.Answered, "", 1, false, "Lights out, Dogpatch."),
                ("5-nope", CallState.Answered, "", 2, true, "Not on my watch, sugar."),
            };
            foreach (var shot in shots)
            {
                SlipstreamHud.PreviewState = shot.state;
                SlipstreamHud.PreviewWords = shot.words;
                SlipstreamHud.PreviewAsks = shot.asks;
                SlipstreamHud.PreviewRefused = shot.refused;
                SlipstreamHud.PreviewLine = shot.line;
                SlipstreamHud.PreviewAt = Time.time;
                yield return new WaitForSecondsRealtime(shot.state == CallState.Answered ? 0.6f : 1.2f);
                yield return Shot(shot.name);
            }
            SlipstreamHud.PreviewState = null;
            SlipstreamHud.PreviewAsks = 1;
            var hud = FindAnyObjectByType<OperatorHud>();
            if (hud != null && hud.Effects != null)
            {
                double now = hud.Effects.ServerNow();
                hud.Effects.Optimistic(new ActiveEffect { id = "capture-night", effect = "night", label = "night", status = "active", scope = "world", startsAt = now, endsAt = now + 58000, durationS = 60f, requestedBy = race.Calls.PlayerId() });
                hud.Effects.Optimistic(new ActiveEffect { id = "capture-gravity", effect = "gravity_lower", label = "gravity", status = "active", scope = "world", startsAt = now, endsAt = now + 4000, durationS = 30f, requestedBy = "someone-else" });
            }
            race.Hud.PushNews("MAYA MADE IT NIGHT", "time", "purple");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("6-news-chips");
            Debug.Log("[CallV3] capture done");
            Application.Quit();
        }
    }
}
