using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Dogpatch.Look
{
    // Headless smoke drive from Dogpatch into SoMa, inert unless the player is launched with
    //   -somaSmoke <dir>
    // Enters a parked car, drives 3rd Street north from Mariposa to King, King and 4th to Brannan, then Brannan, 6th and
    // Folsom to 7th, steering at waypoints through the normal CarController input. Logs the car's
    // height (falling through), stalls, draw calls, triangles and memory inside SoMa, writes a chase
    // still and a top-down still, then quits.
    public sealed class SomaSmoke : MonoBehaviour
    {
        static readonly (Vector2 at, float side)[] Route =
        {
            (new Vector2(-45.1f, 818.9f), 7f), (new Vector2(-324.8f, 2354.9f), 7f), (new Vector2(-504.2f, 2133.5f), 3f),
            (new Vector2(-599.6f, 2232f), 3f), (new Vector2(-734.7f, 2367.1f), 3f), (new Vector2(-926.1f, 2177.1f), 3f),
            (new Vector2(-1121.1f, 1981.5f), 3f), (new Vector2(-1256f, 2117.7f), 3f), (new Vector2(-1391.8f, 2254.8f), 3f),
            (new Vector2(-1528f, 2391.6f), 3f), (new Vector2(-1723.4f, 2196.4f), 3f),
        };

        string output;
        GameController game;
        CarController car;
        Vector3 target;
        bool driving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-somaSmoke");
            if (i < 0 || i + 1 >= args.Length) return;
            new GameObject("SomaSmoke").AddComponent<SomaSmoke>().output = args[i + 1];
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            for (int k = 0; k < 10; k++) yield return null;
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);
            foreach (var hud in FindObjectsByType<Operator.OperatorHud>(FindObjectsSortMode.None)) hud.DebugInput = false;

            CarController parked = null;
            foreach (var c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (c.Enterable && !c.Autopilot && !c.Driven && c.HalfExtents.z < 2.6f && (parked == null || Vector3.Distance(c.transform.position, game.Player.transform.position) < Vector3.Distance(parked.transform.position, game.Player.transform.position)))
                    parked = c;
            if (parked == null) throw new InvalidOperationException("no parked car");
            game.Player.Teleport(parked.transform.position - parked.transform.right * (parked.HalfExtents.x + 1f), 0f);
            for (int k = 0; k < 3; k++) yield return null;
            car = game.NearestCar ?? parked;
            var enter = typeof(GameController).GetMethod("EnterCarAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)enter.Invoke(game, new object[] { car });
            while (!task.IsCompleted) yield return null;

            var first = Lane(0);
            var toNext = Lane(1) - first;
            car.Place(first + Vector3.up * 0.5f, Mathf.Atan2(toNext.x, toNext.z) * Mathf.Rad2Deg);
            game.View.GetComponent<FollowCamera>().Snap(car.transform, true);
            for (int k = 0; k < 20; k++) yield return null;
            Time.timeScale = 2f;
            driving = true;

            int falls = 0, stalls = 0;
            float minY = float.MaxValue;
            for (int w = 1; w < Route.Length; w++)
            {
                target = Lane(w);
                float stuck = 0f, legStart = Time.time;
                while (Flat(target - car.transform.position).magnitude > 12f)
                {
                    minY = Mathf.Min(minY, car.transform.position.y);
                    if (car.transform.position.y < -2f)
                    {
                        falls++;
                        Debug.LogError($"[SomaSmoke] fell through at {car.transform.position}");
                        car.Place(target + Vector3.up * 0.5f, car.transform.eulerAngles.y);
                        break;
                    }
                    stuck = Mathf.Abs(car.Speed) < 1f ? stuck + Time.deltaTime : 0f;
                    if (stuck > 5f || Time.time - legStart > 120f)
                    {
                        stalls++;
                        Debug.Log($"[SomaSmoke] stalled at {car.transform.position} on leg {w}, placing at the next waypoint");
                        var d = target - Lane(w - 1);
                        car.Place(target + Vector3.up * 0.5f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                        break;
                    }
                    yield return null;
                }
                Debug.Log($"[SomaSmoke] waypoint {w} reached at ({car.transform.position.x:F0},{car.transform.position.y:F1},{car.transform.position.z:F0}) t={Time.time:F0}s");
                if (w == 8) yield return Measure("chase");
            }
            driving = false;
            car.SetInput(Vector3.zero, 0f);
            Time.timeScale = 1f;
            for (int k = 0; k < 60; k++)
            {
                minY = Mathf.Min(minY, car.transform.position.y);
                yield return null;
            }
            Debug.Log($"[SomaSmoke] done: falls={falls} stalls={stalls} minY={minY:F2} end=({car.transform.position.x:F0},{car.transform.position.y:F2},{car.transform.position.z:F0})");
            yield return TopDown();
            Application.Quit();
        }

        IEnumerator Measure(string name)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            long drawSum = 0, triSum = 0;
            int frames = 0;
            for (int k = 0; k < 30; k++)
            {
                yield return null;
                if (!draws.Valid || draws.LastValue <= 0) continue;
                drawSum += draws.LastValue;
                triSum += tris.LastValue;
                frames++;
            }
            draws.Dispose();
            tris.Dispose();
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
            var p = car.transform.position;
            Debug.Log($"[SomaSmoke] {name} at ({p.x:F0},{p.z:F0}): draws={(frames > 0 ? drawSum / frames : -1)} tris={(frames > 0 ? triSum / frames : -1)} totalAllocatedMB={Profiler.GetTotalAllocatedMemoryLong() / 1048576} totalReservedMB={Profiler.GetTotalReservedMemoryLong() / 1048576} monoHeapMB={Profiler.GetMonoHeapSizeLong() / 1048576} gfxMB={Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576}");
        }

        IEnumerator TopDown()
        {
            var cam = game.View;
            var follow = cam.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            cam.transform.SetPositionAndRotation(new Vector3(-1150f, 1100f, 1950f), Quaternion.Euler(90f, -45f, 0f));
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 3000f);
            for (int k = 0; k < 20; k++) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "topdown.png"));
            for (int k = 0; k < 5; k++) yield return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 Lane(int w)
        {
            var at = Route[w].at;
            var next = w + 1 < Route.Length ? Route[w + 1].at : at + (at - Route[w - 1].at);
            var prev = w > 0 ? Route[w - 1].at : at - (next - at);
            var dir = (next - prev).normalized;
            var right = new Vector2(dir.y, -dir.x);
            var p = at + right * Route[w].side;
            return new Vector3(p.x, 0f, p.y);
        }

        void LateUpdate()
        {
            if (!driving || car == null) return;
            var to = Flat(target - car.transform.position);
            car.SetInput(to.normalized, 0.7f);
        }
    }
}
