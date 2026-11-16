using System.Collections.Generic;
using System.Linq;
using Dogpatch.Gameplay;
using Dogpatch.Nav;
using Dogpatch.Race;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // Getting into a robotaxi or a cab in the open world makes it a ride to the mission start ring:
    // its traffic agent keeps driving, routed over the street graph at a brisk pace through lights and
    // stop signs, and pulls up at the ring, where the car is handed to the player. Steering takes
    // over and ends the ride early; getting out ends it too. A chip shows where it is going, and the
    // minimap draws NavService's route to the mission as usual.
    public sealed class CabRide : MonoBehaviour
    {
        public TrafficSystem System;

        TrafficAgent agent;
        string mission;
        float startMetres;
        float shownAt;
        float hideAt = -1f;
        HudV3.RidePlate.State endState;
        float endMetres, endProgress;

        public bool Active => agent != null;

        public static bool IsCab(VehicleSpec spec) => spec != null && (spec.Robotaxi || spec.Id == VehicleTypes.YellowCab);

        void OnEnable() => GameController.VehicleExited += OnExited;

        void OnDisable() => GameController.VehicleExited -= OnExited;

        public bool Begin(TrafficAgent a)
        {
            var race = RaceDirector.Instance;
            bool openWorld = race != null && race.Setup && !race.Gridded && !race.Started && (race.Room == null || (!race.Online && race.Room.status == RoomStatus.Lobby));
            if (!openWorld || a.OnRail || a.Lane == null || System.Graph == null) return false;

            var ring = new Vector2(race.Course.RingCenter.x, race.Course.RingCenter.z);
            var start = a.Link != null ? a.Link.To : a.Lane;
            var candidates = System.Graph.Lanes
                .Select(lane => (lane, s: lane.Path.Closest(ring, out float d), d))
                .OrderBy(c => c.d)
                .Take(8);
            foreach (var (lane, s, d) in candidates)
            {
                if (lane == start && a.Link == null && a.S > s - 3f) continue;
                var route = System.Graph.Route(start, lane);
                if (route == null) continue;
                if (a.Link != null) route.Insert(0, a.Lane);
                a.DriveTo(route, lane, s);
                agent = a;
                startMetres = Mathf.Max(1f, Metres());
                shownAt = Time.time;
                hideAt = -1f;
                var name = race.Course.Data?.finishName ?? "MISSION";
                mission = (name.Contains(" (") ? name.Substring(0, name.IndexOf(" (")) : name).ToUpperInvariant();
                Debug.Log($"[CabRide] {a.Spec.Id} ride to {mission}: {route.Count} lanes, pulls up {d:F0} m from the ring");
                return true;
            }
            Debug.LogWarning($"[CabRide] no route from lane {start.Id} to the mission ring; driving freely");
            return false;
        }

        void OnExited(CarController car)
        {
            if (agent == null || car != agent.Car) return;
            Debug.Log("[CabRide] got out; ride over");
            agent.DriveTo(null, null, 0f);
            agent = null;
        }

        void Update()
        {
            if (agent == null) return;
            var game = RaceDirector.Instance != null ? RaceDirector.Instance.Game : null;
            if (!agent.enabled || game == null || game.CurrentCar != agent.Car)
            {
                if (agent.enabled) agent.DriveTo(null, null, 0f);
                agent = null;
                return;
            }
            if (agent.Car.InputStrength > 0.25f)
            {
                Debug.Log("[CabRide] steering took over; ride cancelled");
                Handover(HudV3.RidePlate.State.Manual, 2.5f);
                return;
            }
            if (agent.Link == null && agent.Lane == agent.GoalLane && agent.S >= agent.GoalS - 2.5f && agent.Speed < 0.5f)
            {
                var ring = RaceDirector.Instance.Course.RingCenter;
                Debug.Log($"[CabRide] arrived at {mission}, {Vector2.Distance(new Vector2(ring.x, ring.z), new Vector2(agent.transform.position.x, agent.transform.position.z)):F0} m from the ring centre");
                Handover(HudV3.RidePlate.State.Arrived, 1.2f);
            }
        }

        // The player keeps the car: off the traffic agent, drivable even if it was passenger-only.
        void Handover(HudV3.RidePlate.State state, float hold)
        {
            endState = state;
            endMetres = Metres();
            endProgress = Progress(endMetres);
            hideAt = Time.time + hold;
            var car = agent.Car;
            agent.DriveTo(null, null, 0f);
            System.Release(agent);
            agent = null;
            if (car.Handling.PassengerOnly)
            {
                var handling = car.Handling.Clone();
                handling.PassengerOnly = false;
                car.Handling = handling;
            }
            car.SetDriven(true);
        }

        float Metres()
        {
            var nav = NavService.Instance;
            return nav != null && nav.HasObjective && nav.Kind == NavKind.Mission
                ? nav.Remaining
                : Vector3.Distance(agent.transform.position, RaceDirector.Instance.Course.RingCenter);
        }

        float Progress(float metres) => Mathf.Clamp01(1f - metres / startMetres);

        void OnGUI()
        {
            if (Dogpatch.Map.Minimap.FullOpen) return;
            if (agent != null)
            {
                float m = Metres();
                HudV3.RidePlate.Draw(HudV3.RidePlate.State.Driving, mission, m, Progress(m), shownAt, -1f);
            }
            else if (hideAt >= 0f && Time.time < hideAt + 0.2f)
                HudV3.RidePlate.Draw(endState, mission, endMetres, endState == HudV3.RidePlate.State.Arrived ? 1f : endProgress, shownAt, hideAt);
        }
    }
}
