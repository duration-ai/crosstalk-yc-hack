using System;
using System.Collections;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Map;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Nav
{
    // Headless check for the minimap and the direction arrow: run the player build with
    //   -offline -nav-capture <folder>
    // and it shoots free roam on foot toward the start ring, free roam driving at speed, the race
    // on autopilot, the race after leaving the course (the A* reroute), and the map toggled off.
    public sealed class NavCapture : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-nav-capture");
            if (at < 0) return;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            var capture = new GameObject("NavCapture").AddComponent<NavCapture>();
            capture.output = at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "nav-capture");
        }

        static void Log(string message) => Debug.Log("[Nav capture] " + message);

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            Time.captureDeltaTime = 1f / 30f;
            RaceDirector race;
            while ((race = RaceDirector.Instance) == null || !race.Setup || race.Last == null || NavService.Instance == null) yield return null;
            var game = race.Game;
            var nav = NavService.Instance;
            yield return Seconds(1f);

            var anchor = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z);
            if (game.World.Roads.Nearest(anchor + new Vector2(90f, 60f), 80f, out var hit))
            {
                game.Player.Teleport(new Vector3(hit.Point.x, 0.3f, hit.Point.y), 0f);
                yield return Seconds(0.3f);
                var h = nav.Heading;
                game.Player.Teleport(game.Player.transform.position, Mathf.Atan2(h.x, h.y) * Mathf.Rad2Deg + 20f);
                game.Follow.Snap(game.Player.transform, false);
            }
            yield return Seconds(1.2f);
            Log($"on foot: objective {nav.Kind}, {nav.Remaining:F0} m along {nav.Path.Count} points, arrived={nav.Arrived}");
            yield return Shot("on-foot-free-roam");
            for (int i = 0; i < Minimap.StyleNames.Length; i++)
            {
                Minimap.MapStyle = i;
                yield return FullMap("full-map-" + Minimap.StyleNames[i].ToLowerInvariant());
            }
            Minimap.MapStyle = 1;
            var map = FindAnyObjectByType<Minimap>();
            for (int i = 0; i < MapLook.All.Length; i++)
            {
                MapLook.Index = i;
                yield return Seconds(1.2f);
                map.SetFull(true);
                map.Preview(1f, false);
                yield return Seconds(0.3f);
                yield return Shot("map-look-" + MapLook.All[i].Name.ToLowerInvariant());
                map.Preview(2.6f, false);
                yield return Seconds(0.3f);
                yield return Shot("map-look-" + MapLook.All[i].Name.ToLowerInvariant() + "-zoom");
                map.SetFull(false);
            }
            MapLook.Index = 0;
            Minimap.MapStyle = 0;
            FindAnyObjectByType<Minimap>().StartTravel();
            yield return Seconds(1f);
            Log($"fast travel: {Vector3.Distance(game.Player.transform.position, race.Course.RingCenter):F0} m from the ring");
            yield return Shot("fast-travel");

            CarController car = null;
            foreach (var candidate in game.Cars.Values)
                if (candidate != race.MyCar) { car = candidate; break; }
            if (game.World.Roads.Nearest(anchor + new Vector2(-260f, -180f), 120f, out hit))
            {
                car.Place(new Vector3(hit.Point.x, 0.3f, hit.Point.y), Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg);
                game.Player.Teleport(car.transform.position + car.transform.right * 2.2f, car.transform.eulerAngles.y);
                yield return null;
            }
            var enter = game.EnterCar(car);
            while (!enter.IsCompleted) yield return null;
            game.ExternalInput = true;
            float until = Time.time + 3.5f;
            while (Time.time < until)
            {
                var dir = new Vector3(nav.Heading.x, 0f, nav.Heading.y);
                car.SetInput(dir, 1f);
                yield return null;
            }
            Log($"driving: objective {nav.Kind}, {nav.Remaining:F0} m, speed {car.Speed * 3.6f:F0} km/h");
            yield return Shot("driving-free-roam");
            car.SetInput(Vector3.zero, 0f);
            game.ExternalInput = false;
            var exit = game.ExitCar();
            while (!exit.IsCompleted) yield return null;

            game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            race.Hud.PressReady();
            yield return Until(() => race.Started, 40f, "race started");
            race.EnableAutopilot();
            yield return Seconds(3.5f);
            Log($"race: objective {nav.Kind}, {nav.Remaining:F0} m, meters to finish {race.Me?.metersToFinish:F0}");
            yield return Shot("race");
            yield return FullMap("full-map-race");

            var route = race.Course.Route;
            float progress = route.Project(new Vector2(race.MyCar.transform.position.x, race.MyCar.transform.position.z));
            var ahead = route.PointAt(progress + 150f);
            if (game.World.Roads.Nearest(ahead + route.RightAt(progress + 150f) * 110f, 60f, out hit))
            {
                race.MoveBody(race.MyId, new Vector3(hit.Point.x, 0.3f, hit.Point.y), Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg);
                game.Follow.Snap(race.MyCar.transform, true);
            }
            yield return Seconds(1.5f);
            Log($"off course: objective {nav.Kind}, {nav.Remaining:F0} m along {nav.Path.Count} points");
            yield return Shot("race-off-course");

            Minimap.Shown = false;
            yield return Seconds(0.2f);
            yield return Shot("map-hidden");
            Minimap.Shown = true;
            Log("done");
            Application.Quit(0);
        }

        IEnumerator FullMap(string name)
        {
            var map = FindAnyObjectByType<Minimap>();
            map.SetFull(true);
            map.Preview(1f);
            yield return Seconds(0.3f);
            yield return Shot(name);
            map.SetFull(false);
            yield return null;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.time + timeout;
            while (!condition())
            {
                if (Time.time > until)
                {
                    Log("FAILED waiting for " + what);
                    Application.Quit(1);
                    yield break;
                }
                yield return null;
            }
            Log(what);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var arrow = FindAnyObjectByType<DirectionArrow>()?.GetComponentInChildren<MeshRenderer>(true);
            if (arrow != null)
            {
                var cam = RaceDirector.Instance.Game.View;
                var b = arrow.GetComponent<MeshFilter>().sharedMesh.bounds;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var p = cam.WorldToScreenPoint(arrow.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
                }
                Log($"arrow screen rect x {x0:F0}-{x1:F0} y {y0:F0}-{y1:F0} of {Screen.width}x{Screen.height} (origin bottom left)");
                Log($"arrow enabled={arrow.enabled} active={arrow.gameObject.activeInHierarchy} at {arrow.transform.position} scale {arrow.transform.localScale.x:F2} viewport {cam.WorldToViewportPoint(arrow.transform.position)} mesh {arrow.GetComponent<MeshFilter>().sharedMesh?.name} bounds {arrow.bounds}");
            }
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Log("screenshot " + name);
        }
    }
}
