using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Headless rotation check, inert unless the player is launched with -orientation-check <dir>.
    // Captures each screen in the launch orientation, swaps width and height the way a phone
    // rotation does, and captures it again: the HOST/JOIN menu, typing a code, the pause menu,
    // the avatar camera, the hosted lobby (online only), then a drive that rotates mid-drive. It
    // logs PASS only if the world, the car and the room survive every rotation, a rotation raises
    // ScreenShape.Changed, and Changed cancels a drag that is held at that moment.
    public sealed class OrientationCheck : MonoBehaviour
    {
        string output;
        GameController game;
        RaceDirector race;
        CarController driving;
        int failures;
        int changes;

        void OnEnable() => ScreenShape.Changed += Count;

        void OnDisable() => ScreenShape.Changed -= Count;

        void Count() => changes++;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-orientation-check");
            if (i < 0 || i + 1 >= args.Length) return;
            Application.runInBackground = true;
            new GameObject("OrientationCheck").AddComponent<OrientationCheck>().output = args[i + 1];
        }

        static void Log(string message) => Debug.Log("[OrientationCheck] " + message);

        void Expect(bool ok, string what)
        {
            if (!ok) failures++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            float deadline = Time.realtimeSinceStartup + 240f;
            while ((game = FindAnyObjectByType<GameController>()) == null || !game.IsReady || (race = RaceDirector.Instance) == null || !race.Setup)
            {
                if (Time.realtimeSinceStartup > deadline) { Finish("game never got ready"); yield break; }
                yield return null;
            }
            yield return Wait(3f);
            int world = game.World.Root.GetInstanceID();
            Log($"ready at {Screen.width}x{Screen.height}, online={race.Online}");

            yield return Both("on-foot");

            var setPaused = typeof(Hud).GetMethod("SetPaused", BindingFlags.Instance | BindingFlags.NonPublic);
            var pause = game.GetComponent<Hud>();
            setPaused.Invoke(pause, new object[] { true });
            yield return Both("pause-menu");
            setPaused.Invoke(pause, new object[] { false });

            var capture = game.GetComponent<AvatarCapture>();
            if (capture != null)
            {
                capture.Press("open");
                yield return Wait(1.5f);
                yield return Both("avatar-capture");
                capture.Press("skip");
                yield return Wait(0.5f);
            }

            string room = null;
            if (race.Online)
            {
                game.Player.Teleport(race.RingSpot(0), 0f);
                float until = Time.realtimeSinceStartup + 20f;
                while ((race.RoomId == null || race.Last == null || race.Room == null) && Time.realtimeSinceStartup < until) yield return null;
                Expect(race.RoomId != null, "entered the mission lobby");
                yield return Wait(2f);
                yield return Both("lobby");
            }

            var me = game.Player.transform.position;
            var car = game.Cars.Values.Where(c => c != null && !c.Handling.PassengerOnly).OrderBy(c => (c.transform.position - me).sqrMagnitude).FirstOrDefault();
            if (car == null) { Finish("no car to drive"); yield break; }
            var entering = game.EnterCar(car);
            while (!entering.IsCompleted) yield return null;
            Expect(game.CurrentCar == car, "entered " + car.VehicleId);
            game.ExternalInput = true;
            driving = car;
            yield return Wait(4f);
            yield return Shot("driving");

            var drag = game.Drag;
            typeof(DragInput).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(drag, new object[] { 7, new Vector2(Screen.width * 0.5f, Screen.height * 0.3f) });
            Expect(drag.Dragging, "drag held before the screen changes shape");
            var changed = (Action)typeof(ScreenShape).GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            changed?.Invoke();
            Expect(!drag.Dragging, "ScreenShape.Changed cancelled the held drag");
            var cam = game.View;
            float fovBefore = cam.fieldOfView;
            int raised = changes;
            yield return Rotate();
            Expect(changes > raised, "rotation raised ScreenShape.Changed");
            yield return Wait(1.5f);
            yield return Shot("driving");
            Log($"fov {fovBefore:F1} -> {cam.fieldOfView:F1} (aspect {cam.aspect:F2})");
            Expect(game.World.Root.GetInstanceID() == world, "world not rebuilt");
            Expect(game.CurrentCar == car, "still driving the same car");
            yield return Rotate();
            yield return Wait(1.5f);
            Expect(game.World.Root.GetInstanceID() == world && game.CurrentCar == car, "second rotation kept world and car");

            driving = null;
            game.ExternalInput = false;
            if (room != null) race.Leave();
            yield return Wait(1f);
            Finish(failures == 0 ? null : failures + " checks failed");
        }

        void Update()
        {
            if (driving != null) driving.SetInput(driving.transform.forward, 0.8f);
        }

        IEnumerator Both(string name)
        {
            yield return Shot(name);
            yield return Rotate();
            yield return Wait(0.8f);
            yield return Shot(name);
        }

        IEnumerator Rotate()
        {
            int w = Screen.width, h = Screen.height;
            Screen.SetResolution(h, w, FullScreenMode.Windowed);
            float until = Time.realtimeSinceStartup + 3f;
            while (Screen.width != h && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            Log($"rotated {w}x{h} -> {Screen.width}x{Screen.height}");
            Expect(Screen.width == h && Screen.height == w, "window took the rotated size");
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(output, $"{name}-{(Screen.height > Screen.width ? "portrait" : "landscape")}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log($"shot {path} at {Screen.width}x{Screen.height}");
        }

        void Finish(string error)
        {
            if (error != null) Debug.LogError("[OrientationCheck] " + error);
            else Log("PASS");
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
