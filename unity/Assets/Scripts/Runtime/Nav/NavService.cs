using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using Dogpatch.Race;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Nav
{
    // The one current objective and the street path to it, shared by the 3D direction arrow and the
    // minimap route line. Target, when set, wins; otherwise during a race it is the finish along the
    // course (A* back to the finish when you leave the course), and in free roam the nearest
    // ringing payphone. Driving routes respect one-way streets; walking routes ignore them.
    public sealed class NavService : MonoBehaviour
    {
        const float ReplanSeconds = 3f;
        const float OffRouteDriving = 18f;
        const float OffRouteWalking = 10f;
        const float OnCourse = 35f;

        public static NavService Instance { get; private set; }

        public GameController Game { get; private set; }
        public INavTarget Target;

        public bool HasObjective { get; private set; }
        public NavKind Kind { get; private set; }
        public Vector3 Objective { get; private set; }
        public bool Arrived { get; private set; }
        public float Remaining { get; private set; }
        public Vector2 Heading { get; private set; } = Vector2.up;
        public readonly List<Vector2> Path = new List<Vector2>();

        NavGraph drive;
        NavGraph walk;
        Payphone phone;
        float nextPhonePick;
        float nextPlan;
        bool plannedDriving;
        float courseHint = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            GameController.WorldReady -= Create;
            GameController.WorldReady += Create;
        }

        static void Create(GameController game)
        {
            if (Instance != null) return;
            var go = new GameObject("Navigation");
            var nav = go.AddComponent<NavService>();
            nav.Game = game;
            float t0 = Time.realtimeSinceStartup;
            nav.drive = new NavGraph(game.World.Roads, true);
            nav.walk = new NavGraph(game.World.Roads, false);
            Debug.Log($"[Nav] street graphs in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: driving {nav.drive.NodeCount} nodes/{nav.drive.SegmentCount} segments, walking {nav.walk.NodeCount}/{nav.walk.SegmentCount}");
            go.AddComponent<DirectionArrow>().Nav = nav;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetWaypoint(Vector3 position) => Target = new NavPoint(position);

        public void ClearWaypoint() => Target = null;

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        void Update()
        {
            var focus = Game != null && Game.IsReady ? Game.Focus : null;
            if (focus == null || !Resolve(out var objective, out var kind))
            {
                Clear();
                return;
            }
            bool driving = Game.Driving;
            var me = Flat(focus.position);
            if (kind != Kind || (Flat(objective) - Flat(Objective)).sqrMagnitude > 64f) nextPlan = 0f;
            HasObjective = true;
            Kind = kind;
            Objective = objective;

            var race = RaceDirector.Instance;
            bool onCourse = false;
            if (kind == NavKind.Race && Target == null)
            {
                var route = race.Course.Route;
                courseHint = route.Project(me, courseHint, 150f);
                onCourse = Vector2.Distance(me, route.PointAt(courseHint)) < OnCourse;
                if (onCourse)
                {
                    Path.Clear();
                    Path.Add(me);
                    Path.Add(route.PointAt(courseHint));
                    for (int i = 0; i < route.Points.Length; i++)
                        if (route.Distance[i] > courseHint + 1f) Path.Add(route.Points[i]);
                    nextPlan = 0f;
                }
            }
            else courseHint = -1f;

            if (!onCourse)
            {
                float off = Path.Count >= 2 ? DistanceToPath(me) : float.MaxValue;
                if (Time.time >= nextPlan || driving != plannedDriving || off > (driving ? OffRouteDriving : OffRouteWalking))
                {
                    nextPlan = Time.time + ReplanSeconds;
                    plannedDriving = driving;
                    if (!(driving ? drive : walk).Route(me, Flat(objective), Path))
                    {
                        Path.Clear();
                        Path.Add(me);
                        Path.Add(Flat(objective));
                    }
                }
            }

            Follow(me, driving ? 30f : 12f);
            float straight = Vector2.Distance(me, Flat(objective));
            float arrive = driving ? 20f : 8f;
            Arrived = straight < arrive || Remaining < arrive;
        }

        void Clear()
        {
            HasObjective = false;
            Arrived = false;
            Remaining = 0f;
            Path.Clear();
            courseHint = -1f;
        }

        bool Resolve(out Vector3 objective, out NavKind kind)
        {
            objective = default;
            kind = NavKind.Waypoint;
            if (Target != null)
            {
                objective = Target.Position;
                kind = Target.Kind;
                return true;
            }
            var race = RaceDirector.Instance;
            if (race == null || !race.Setup) return false;
            if (race.Gridded)
            {
                if (!race.Started || race.ResultsIn) return false;
                objective = race.Course.Finish;
                kind = NavKind.Race;
                return true;
            }
            var calls = race.Calls;
            if (calls == null || calls.State != CallState.None) return false;
            if (Time.time >= nextPhonePick || phone == null || !phone.Ringing)
            {
                nextPhonePick = Time.time + 1f;
                phone = PickPhone(calls.Phones, Game.Focus.position, phone);
            }
            if (phone == null) return false;
            objective = phone.transform.position;
            kind = NavKind.Payphone;
            return true;
        }

        // Nearest ringing payphone; the current one is kept unless another is clearly closer.
        static Payphone PickPhone(List<Payphone> phones, Vector3 from, Payphone current)
        {
            Payphone best = null;
            float bestD = float.MaxValue;
            foreach (var p in phones)
            {
                if (p == null || !p.Ringing) continue;
                float d = Vector3.Distance(p.transform.position, from) * (p == current ? 0.8f : 1f);
                if (d >= bestD) continue;
                bestD = d;
                best = p;
            }
            return best;
        }

        float DistanceToPath(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < Path.Count - 1; i++)
                best = Mathf.Min(best, Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, Path[i], Path[i + 1], out _)));
            return best;
        }

        // Remaining distance from the closest point on the path, and the arrow heading toward the
        // point lookAhead metres further along it.
        void Follow(Vector2 me, float lookAhead)
        {
            int seg = 0;
            float bestD = float.MaxValue, bestT = 0f;
            for (int i = 1; i < Path.Count - 1; i++)
            {
                var q = Geometry.ClosestPointOnSegment(me, Path[i], Path[i + 1], out float t);
                float d = Vector2.Distance(me, q);
                if (d >= bestD) continue;
                bestD = d;
                bestT = t;
                seg = i;
            }
            if (Path.Count < 3)
            {
                Remaining = Path.Count == 2 ? Vector2.Distance(Path[0], Path[1]) : 0f;
                var d = Path.Count == 2 ? Path[1] - me : Vector2.zero;
                if (d.sqrMagnitude > 1e-4f) Heading = d.normalized;
                return;
            }
            var start = Vector2.Lerp(Path[seg], Path[seg + 1], bestT);
            float remaining = Vector2.Distance(start, Path[seg + 1]);
            for (int i = seg + 1; i < Path.Count - 1; i++) remaining += Vector2.Distance(Path[i], Path[i + 1]);
            Remaining = bestD + remaining;

            var ahead = start;
            float left = lookAhead;
            var cursor = start;
            for (int i = seg + 1; i < Path.Count && left > 0f; i++)
            {
                float len = Vector2.Distance(cursor, Path[i]);
                ahead = len > left ? Vector2.Lerp(cursor, Path[i], left / len) : Path[i];
                left -= len;
                cursor = Path[i];
            }
            var dir = ahead - me;
            if (dir.sqrMagnitude > 1e-4f) Heading = dir.normalized;
        }
    }
}
