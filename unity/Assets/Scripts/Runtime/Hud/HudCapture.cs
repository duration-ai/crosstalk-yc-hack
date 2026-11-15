using System;
using System.Collections;
using System.IO;
using System.Text;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // Headless renders of the HUD layout options: run the player build with
    //   -offline -safe-iphone -hud-layout A|B|C -hud-capture <folder>
    // and it shoots free roam on foot (stick held, talking to the Operator), driving (stick held),
    // the mission lobby on the start ring, and mid-race with a news line, then writes every
    // HudLayout slot to layout.json.
    public sealed class HudCapture : MonoBehaviour
    {
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-hud-capture");
            if (at < 0) return;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            var capture = new GameObject("HudCapture").AddComponent<HudCapture>();
            capture.output = at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "hud-capture");
        }

        static void Log(string message) => Debug.Log("[Hud capture] " + message);

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            Time.captureDeltaTime = 1f / 30f;
            RaceDirector race;
            while ((race = RaceDirector.Instance) == null || !race.Setup || race.Last == null) yield return null;
            var game = race.Game;
            yield return Seconds(1f);
            Log($"layout {HudLayout.Option} at {Screen.width}x{Screen.height}, safe {HudLayout.Safe}");

            var phonePos = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z) + new Vector2(120f, 90f);
            if (game.World.Roads.Nearest(phonePos, 60f, out var hit))
            {
                game.Player.Teleport(new Vector3(hit.Point.x, 0.3f, hit.Point.y), Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg);
                game.Follow.Snap(game.Player.transform, false);
            }
            game.Drag.Hold(new Vector2(0.35f, 0.8f));
            yield return Seconds(0.8f);
            race.Calls.BeginTalk();
            yield return Seconds(0.4f);
            yield return Shot("1-free-roam");
            game.Drag.Release();
            race.Calls.HangUp();

            CarController car = null;
            foreach (var candidate in game.Cars.Values)
                if (candidate != race.MyCar && !candidate.Handling.PassengerOnly && candidate.Enterable) { car = candidate; break; }
            if (game.World.Roads.Nearest(phonePos + new Vector2(-260f, -180f), 120f, out hit))
            {
                car.Place(new Vector3(hit.Point.x, 0.3f, hit.Point.y), Mathf.Atan2(hit.Direction.x, hit.Direction.y) * Mathf.Rad2Deg);
                game.Player.Teleport(car.transform.position + car.transform.right * 2.2f, car.transform.eulerAngles.y);
                yield return null;
            }
            var enter = game.EnterCar(car);
            while (!enter.IsCompleted) yield return null;
            game.Follow.Snap(car.transform, true);
            game.Drag.Hold(new Vector2(0.25f, 0.95f));
            yield return Seconds(3f);
            Log($"driving {car.ModelId} at {car.Speed * 3.6f:F0} km/h with the stick at (0.25, 0.95), heading {car.transform.eulerAngles.y:F0}, kinematic {car.GetComponent<Rigidbody>().isKinematic}, direction {game.Drag.Direction} strength {game.Drag.Strength:F2}");
            yield return Shot("2-driving");
            game.Drag.Release();
            var exit = game.ExitCar();
            while (!exit.IsCompleted) yield return null;

            game.Player.Teleport(race.RingSpot(race.MySlot), 0f);
            yield return Seconds(1.6f);
            yield return Shot("4-lobby");
            race.Calls.HangUp();

            if (!(race.Me?.ready ?? false) && !race.Gridded) race.Hud.PressReady();
            float until = Time.time + 45f;
            while (!race.Started && Time.time < until) yield return null;
            if (!race.Started) { Log("FAILED waiting for the race"); Application.Quit(1); yield break; }
            race.EnableAutopilot();
            yield return Seconds(4f);
            race.Hud.PushNews("MAYA LOWERED GRAVITY", "gravity", "purple");
            yield return Seconds(0.7f);
            yield return Shot("3-race");

            WriteLayout();
            Log("done");
            Application.Quit(0);
        }

        void WriteLayout()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            void R(string name, Rect r, bool last = false) => sb.Append($"  \"{name}\": [{r.x:F0}, {r.y:F0}, {r.width:F0}, {r.height:F0}]{(last ? "" : ",")}\n");
            float u = HudLayout.U;
            var stick = HudLayout.StickScreen;
            float sr = HudLayout.StickRadius;
            var map = HudLayout.MinimapScreen;
            float mr = HudLayout.MinimapRadius;
            var pause = HudLayout.PauseScreen;
            sb.Append($"  \"option\": \"{HudLayout.Option}\", \"screen\": [{Screen.width}, {Screen.height}], \"u\": {u:F3},\n");
            R("safe", new Rect(HudLayout.Safe.x, Screen.height - HudLayout.Safe.yMax, HudLayout.Safe.width, HudLayout.Safe.height));
            R("stick", new Rect(stick.x - sr, Screen.height - stick.y - sr, sr * 2f, sr * 2f));
            R("stickGrab", new Rect(stick.x - sr * 1.6f, Screen.height - stick.y - sr * 1.6f, sr * 3.2f, sr * 3.2f));
            R("minimap", new Rect(map.x - mr, Screen.height - map.y - mr, mr * 2f, mr * 2f));
            R("pause", new Rect(pause.x, Screen.height - pause.yMax, pause.width, pause.height));
            R("band", new Rect(HudLayout.Left, HudLayout.Band, HudLayout.Right - HudLayout.Left, 38f * u));
            R("arrowBand", HudLayout.ArrowBand);
            R("call", HudLayout.Call(72f * u, 72f * u));
            R("hearing", HudLayout.Hearing(200f * u, 30f * u));
            var chips = HudLayout.Chips;
            R("chips", new Rect(chips.x, chips.y, 160f * u, 49f * u));
            var meters = HudLayout.Meters;
            R("meters", new Rect(meters.x - 175f * u, meters.y - 4f * u, 175f * u, 56f * u));
            R("place", new Rect(HudLayout.Left, HudLayout.Top - 6f * u, 110f * u, 70f * u));
            R("lobbyPanel", new Rect(HudLayout.LobbyPanel.x, HudLayout.LobbyPanel.y, 183f * u, 160f * u));
            R("perf", HudLayout.Perf(Screen.width * 0.5f, Mathf.Min(Screen.width, Screen.height) * 0.03f * 4.5f), true);
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(output, "layout.json"), sb.ToString());
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Log("screenshot " + name);
        }
    }
}
