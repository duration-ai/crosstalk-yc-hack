using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dogpatch.Race
{
    // Headless check: "-setpiece-stills <dir>" saves one still of every set piece as soon as a race
    // builds them, looking along the racing line from behind and above.
    public sealed class SetPieceStills : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-setpiece-stills");
            if (at < 0 || at + 1 >= args.Length) return;
            var stills = new GameObject("SetPieceStills").AddComponent<SetPieceStills>();
            stills.output = args[at + 1];
            SetPieces.Built += stills.Capture;
        }

        void Capture(Transform root)
        {
            Directory.CreateDirectory(output);
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var cam = new GameObject("still camera").AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.targetTexture = rt;
            cam.fieldOfView = 55f;
            string route = root.name.Replace("SetPieces ", "").Replace(" ", "-").Replace("&", "and").ToLowerInvariant();
            foreach (Transform piece in root)
            {
                SetPieces.SnapMoves = true;
                foreach (var clock in piece.GetComponentsInChildren<HazardClock>()) clock.Tick(clock.StillAt);
                SetPieces.SnapMoves = false;
                var bounds = new Bounds(piece.position, Vector3.one);
                foreach (var r in piece.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float size = Mathf.Clamp(bounds.extents.magnitude, 3f, 45f);
                cam.transform.position = bounds.center - piece.forward * (size * 1.3f + 5f) + piece.right * size * 0.35f + Vector3.up * (size * 0.55f + 3f);
                cam.transform.LookAt(bounds.center);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                var path = Path.Combine(output, $"{route}-{piece.name.Replace(" ", "-")}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[SetPieces] still " + path);
            }
            Destroy(cam.gameObject);
            Destroy(tex);
            rt.Release();
        }
    }

    // Headless check: "-setpiece-smoke" walks onto the ring, races the route on autopilot with the
    // bots, logs every racer passing every set piece and quits with each racer's progress.
    public sealed class SetPieceSmoke : MonoBehaviour
    {
        const float Limit = 600f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-setpiece-smoke") >= 0) new GameObject("SetPieceSmoke").AddComponent<SetPieceSmoke>();
        }

        IEnumerator Start()
        {
            RaceDirector race;
            while ((race = RaceDirector.Instance) == null || !race.Setup || race.Last == null) yield return null;
            float began = Time.realtimeSinceStartup;
            race.EnableAutopilot();
            race.Game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            yield return new WaitForSeconds(2f);
            race.Hud.PressReady();
            while (!race.Gridded || SetPieces.Instance.Root == null)
            {
                if (Time.realtimeSinceStartup - began > 90f) break;
                yield return null;
            }
            var route = race.Course.Route;
            var marks = SetPieces.Instance.Root == null ? new List<(string name, float at)>() : SetPieces.Instance.Root.Cast<Transform>().Select(t => (name: t.name, at: route.Project(RoutePath.Flat(t.position)))).ToList();
            var passed = new HashSet<(string, int)>();
            while (!race.ResultsIn && Time.realtimeSinceStartup - began < Limit)
            {
                foreach (var r in race.Last.racers)
                    for (int i = 0; i < marks.Count; i++)
                        if (r.progress >= marks[i].at + 5f && passed.Add((r.playerId, i)))
                            Debug.Log($"[SetPieces] smoke: {race.NameOf(r.playerId)} passed {marks[i].name} at {(race.NowMs - race.Room.startsAt) / 1000f:F0} s");
                yield return new WaitForSeconds(0.5f);
            }
            foreach (var r in race.Last.racers)
                Debug.Log($"[SetPieces] smoke result: {race.NameOf(r.playerId)} {(r.finished ? "finished" : "did not finish")} at {r.progress:F0} of {route.Length:F0} m, passed {passed.Count(p => p.Item1 == r.playerId)} of {marks.Count} pieces");
            Debug.Log($"[SetPieces] smoke done: {(race.ResultsIn ? "race over" : "timed out")} after {Time.realtimeSinceStartup - began:F0} s");
            Application.Quit(race.ResultsIn ? 0 : 1);
        }
    }
}
