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
        string brand;
        string mission;
        string flash;
        float flashUntil;
        GUIStyle style;

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
                brand = a.Spec.Robotaxi ? (a.Spec.Id == VehicleTypes.Zoox ? "ZOOX" : "WAYMO") : "CAB";
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
                Handover("MANUAL CONTROL");
                return;
            }
            if (agent.Link == null && agent.Lane == agent.GoalLane && agent.S >= agent.GoalS - 2.5f && agent.Speed < 0.5f)
            {
                var ring = RaceDirector.Instance.Course.RingCenter;
                Debug.Log($"[CabRide] arrived at {mission}, {Vector2.Distance(new Vector2(ring.x, ring.z), new Vector2(agent.transform.position.x, agent.transform.position.z)):F0} m from the ring centre");
                Handover("ARRIVED · " + mission);
            }
        }

        // The player keeps the car: off the traffic agent, drivable even if it was passenger-only.
        void Handover(string message)
        {
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
            flash = message;
            flashUntil = Time.time + 3f;
        }

        string Distance()
        {
            var nav = NavService.Instance;
            float d = nav != null && nav.HasObjective && nav.Kind == NavKind.Mission
                ? nav.Remaining
                : Vector3.Distance(agent.transform.position, RaceDirector.Instance.Course.RingCenter);
            return d >= 1000f ? $"{d / 1000f:F1} km" : $"{Mathf.Round(d / 10f) * 10f:F0} m";
        }

        void OnGUI()
        {
            string text = agent != null ? $"{brand} → {mission} · {Distance()}" : Time.time < flashUntil ? flash : null;
            if (text == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                style.normal.textColor = Color.white;
            }
            style.fontSize = Mathf.RoundToInt(Screen.height * 0.032f);
            var size = style.CalcSize(new GUIContent(text)) + new Vector2(28f, 12f);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.13f, size.x, size.y), text, style);
        }
    }
}
