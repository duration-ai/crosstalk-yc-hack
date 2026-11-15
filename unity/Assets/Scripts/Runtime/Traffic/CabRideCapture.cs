using System;
using System.Collections;
using Dogpatch.Race;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // Headless check of CabRide, inert unless the player is launched with -cabRide [waymo|cab]:
    // stands the player on a street about 300 m from the mission ring, gets them into the nearest
    // robotaxi (or yellow cab) once traffic has filled in, and logs the ride until it pulls up.
    public sealed class CabRideCapture : MonoBehaviour
    {
        string kind = "waymo";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-cabRide");
            if (i < 0) return;
            var capture = new GameObject("CabRideCapture").AddComponent<CabRideCapture>();
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) capture.kind = args[i + 1];
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            while (RaceDirector.Instance == null || !RaceDirector.Instance.Setup || TrafficSystem.Instance == null || TrafficSystem.Instance.Graph == null)
            {
                if (Time.realtimeSinceStartup - t0 > 120f)
                {
                    yield return Quit("world never came up");
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSeconds(2f);
            var race = RaceDirector.Instance;
            var game = race.Game;
            var traffic = TrafficSystem.Instance;
            var ring = race.Course.RingCenter;
            var flatRing = new Vector2(ring.x, ring.z);

            StreetGraph.Lane spot = null;
            foreach (var lane in traffic.Graph.Lanes)
            {
                float d = Vector2.Distance(lane.Path.Sample(lane.Path.Length * 0.5f, out _), flatRing);
                if (d > 260f && d < 340f) { spot = lane; break; }
            }
            if (spot == null)
            {
                yield return Quit("no street 300 m from the ring");
                yield break;
            }
            var p = spot.Path.Sample(spot.Path.Length * 0.5f, out var dir);
            game.Player.Teleport(new Vector3(p.x, 0.2f, p.y), Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
            Debug.Log($"[CabRide] capture: player on lane {spot.Id}, {Vector2.Distance(p, flatRing):F0} m from the ring");

            TrafficAgent pick = null;
            for (float waited = 0f; pick == null && waited < 40f; waited += 1f)
            {
                yield return new WaitForSeconds(1f);
                foreach (var a in traffic.Agents)
                {
                    if (!a.enabled || a.OnRail || a.Lane == null || a.Link != null) continue;
                    bool want = kind == "cab" ? a.Spec.Id == VehicleTypes.YellowCab : a.Spec.Robotaxi;
                    if (!want) continue;
                    float d = Vector3.Distance(a.transform.position, game.Player.transform.position);
                    if (d < 250f && (pick == null || d < Vector3.Distance(pick.transform.position, game.Player.transform.position))) pick = a;
                }
            }
            if (pick == null)
            {
                yield return Quit("no " + kind + " nearby");
                yield break;
            }

            var car = pick.Car;
            game.Player.Teleport(car.transform.position - car.transform.right * (car.HalfExtents.x + 1.2f), car.transform.eulerAngles.y);
            var enter = game.EnterCar(car);
            while (!enter.IsCompleted) yield return null;
            if (enter.IsFaulted) Debug.LogWarning("[CabRide] capture: enter failed: " + enter.Exception?.GetBaseException());
            Debug.Log($"[CabRide] capture: entered {pick.Spec.Id} {Vector3.Distance(car.transform.position, ring):F0} m from the ring; ride active {traffic.Ride.Active}");

            float start = Time.time;
            while (traffic.Ride.Active && Time.time - start < 150f)
            {
                yield return new WaitForSeconds(3f);
                Debug.Log($"[CabRide] capture: t={Time.time - start:F0}s {Vector3.Distance(car.transform.position, ring):F0} m from the ring, {pick.Speed:F1} m/s, {pick.State}");
            }
            Debug.Log($"[CabRide] capture: ride {(traffic.Ride.Active ? "still running after 150 s" : "over")} at t={Time.time - start:F0}s, {Vector3.Distance(car.transform.position, ring):F0} m from the ring, driven {car.Driven}, autopilot {car.Autopilot}, player in it {game.CurrentCar == car}");
            yield return new WaitForSeconds(3f);
            yield return Quit("done");
        }

        IEnumerator Quit(string why)
        {
            Debug.Log("[CabRide] capture " + why);
            Application.Quit();
            yield break;
        }
    }
}
