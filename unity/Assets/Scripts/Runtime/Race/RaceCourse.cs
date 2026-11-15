using System;
using System.Collections.Generic;
using System.Linq;
using Dogpatch.Data;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Race
{
    [Serializable]
    public class GeoPoint
    {
        public double lat;
        public double lon;
    }

    // Resources/SpeedDial/course.json: where the start ring and YC are, as real coordinates.
    [Serializable]
    public class CourseData
    {
        public string startName;
        public GeoPoint start;
        public string finishName;
        public GeoPoint finish;
        public GeoPoint[] via;
        public float finishRadius;
        public float ringRadius;
        public bool freeway;
    }

    // Ask pickups (Resources/SpeedDial/asks.json): how many asks a racer starts with, and where the
    // pickups that earn more go. Each pickup sits on a street node minDetour to maxDetour metres of
    // street from the route and at least minOffRoute metres from it in a straight line, so taking one
    // costs a detour; they spread evenly along the route, at least minSpacing apart.
    [Serializable]
    public class AskTuning
    {
        public int startAsks = 100;
        public int pickupCount = 6;
        public float minDetour = 40f;
        public float maxDetour = 120f;
        public float minOffRoute = 25f;
        public float minSpacing = 60f;
        public float pickupRadius = 4f;
        public float botDetourChance = 0.35f;
        public int seed = 7;
    }

    public struct Spot
    {
        public Vector3 Position;
        public float Heading;
        public float Progress;
    }

    // The race laid onto the generated city: the start ring on clear ground near the start point,
    // a route that follows the street graph (one-way streets only one way) from the ring to YC, a
    // four-car grid at the head of the route, and the finish line.
    public sealed class RaceCourse
    {
        static readonly HashSet<string> RaceKinds = new HashSet<string>
        {
            "primary", "primary_link", "secondary", "secondary_link", "tertiary", "tertiary_link", "residential", "unclassified", "living_street",
        };

        public CourseData Data;
        public RoutePath Route;
        public List<Vector3> RouteWorld = new List<Vector3>();
        public Vector3 RingCenter;
        public float RingRadius;
        public Vector3 RingFacing;
        public Spot[] Grid = new Spot[4];
        public Vector3 Finish;
        public float FinishHeading;
        public float FinishHalfWidth;
        public Vector3 YcSite;
        public StreetGraph Graph { get; private set; }
        public List<Vector3> AskPickups = new List<Vector3>();

        readonly WorldContext world;
        readonly WorldData data;
        Func<Vector2, bool> occupied;

        RaceCourse(WorldContext world, WorldData data, CourseData course)
        {
            this.world = world;
            this.data = data;
            Data = course;
        }

        // occupied: true where something already stands on the street (a parked bike, a showroom car),
        // so the start grid avoids it.
        public static RaceCourse Build(WorldContext world, WorldData data, string json, Func<Vector2, bool> occupied = null)
        {
            Physics.SyncTransforms();
            var course = new RaceCourse(world, data, JsonUtility.FromJson<CourseData>(json)) { occupied = occupied };
            course.Plan();
            return course;
        }

        public Vector2 Project(GeoPoint g)
        {
            const double MPerDeg = 111194.93;
            double x = (g.lon - data.originLon) * Math.Cos(data.originLat * Math.PI / 180.0) * MPerDeg;
            double z = (g.lat - data.originLat) * MPerDeg;
            return new Vector2((float)x, (float)z);
        }

        public float GroundY(Vector2 p)
        {
            float best = 0f;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 30f, p.y), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < 0.7f || hit.point.y > 1.5f) continue;
                if (hit.collider.attachedRigidbody != null) continue;
                if (!found || hit.point.y > best) best = hit.point.y;
                found = true;
            }
            return found ? best : 0f;
        }

        Vector3 Ground(Vector2 p) => new Vector3(p.x, GroundY(p), p.y);

        // The route's height d metres along it: the street, or the freeway deck and ramps on a course
        // that takes I-280.
        public float RouteY(float d)
        {
            var at = Route.Distance;
            for (int i = 0; i < at.Length - 1; i++)
                if (d <= at[i + 1]) return Mathf.Lerp(RouteWorld[i].y, RouteWorld[i + 1].y, Mathf.InverseLerp(at[i], at[i + 1], d));
            return RouteWorld[RouteWorld.Count - 1].y;
        }

        void Plan()
        {
            RingRadius = Data.ringRadius > 0f ? Data.ringRadius : 7f;
            var startTarget = Project(Data.start);
            var finishTarget = Project(Data.finish);
            YcSite = Ground(finishTarget);

            Graph = new StreetGraph(world.Roads, RaceKinds, Data.freeway);
            var stops = new List<Vector2> { startTarget };
            if (Data.via != null) foreach (var v in Data.via) stops.Add(Project(v));
            stops.Add(finishTarget);
            Route = new RoutePath(Simplify(Streets(stops)));
            foreach (var p in Route.Points) RouteWorld.Add(new Vector3(p.x, Mathf.Max(GroundY(p), Graph.HeightAt(p)), p.y));
            Debug.Log($"[SpeedDial] route {Data.startName} -> {Data.finishName}: {Route.Length:F0} m, {Route.Points.Length} points");

            PlaceRing(startTarget);
            PlaceGrid();
            PlaceFinish();
        }

        // The street path through every stop in order, one-way streets respected.
        List<Vector2> Streets(IReadOnlyList<Vector2> stops)
        {
            var points = new List<Vector2>();
            for (int i = 0; i < stops.Count - 1; i++)
            {
                var leg = Graph.Route(stops[i], stops[i + 1]);
                if (leg == null || leg.Count < 2) leg = new List<Vector2> { stops[i], stops[i + 1] };
                foreach (var p in leg)
                    if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], p) > 0.5f) points.Add(p);
            }
            return points;
        }

        // A street path from `from` through `via` back onto the race route `rejoin` metres along it.
        public RoutePath Detour(Vector2 from, Vector2 via, float rejoin)
        {
            var points = Streets(new[] { from, via, Route.PointAt(Mathf.Min(rejoin, Route.Length)) });
            return points.Count < 2 ? null : new RoutePath(points);
        }

        public void PlaceAskPickups(AskTuning tuning)
        {
            AskPickups.Clear();
            var detours = Graph.StreetDistances(Route, tuning.maxDetour);
            var candidates = new List<(Vector2 p, float progress)>();
            foreach (var (p, detour) in detours)
            {
                if (detour < tuning.minDetour || Graph.HeightAt(p) > 1f) continue;
                float progress = Route.Project(p);
                if (Vector2.Distance(p, Route.PointAt(progress)) < tuning.minOffRoute) continue;
                if (progress < 40f || progress > Route.Length - 40f) continue;
                candidates.Add((p, progress));
            }
            var random = new System.Random(tuning.seed);
            var chosen = new List<Vector2>();
            int count = Mathf.Max(0, tuning.pickupCount);
            for (int i = 0; i < count; i++)
            {
                float from = Route.Length * i / count;
                float to = Route.Length * (i + 1) / count;
                var pool = candidates.Where(c => c.progress >= from && c.progress < to && chosen.All(q => Vector2.Distance(q, c.p) >= tuning.minSpacing)).ToList();
                if (pool.Count == 0) continue;
                chosen.Add(pool[random.Next(pool.Count)].p);
            }
            foreach (var p in chosen.OrderBy(p => Route.Project(p))) AskPickups.Add(Ground(p));
            Debug.Log($"[SpeedDial] {AskPickups.Count} ask pickups placed from {candidates.Count} side-street spots");
        }

        static List<Vector2> Simplify(List<Vector2> pts)
        {
            var result = new List<Vector2> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var a = result[result.Count - 1];
                var b = pts[i];
                var c = pts[i + 1];
                if (Vector2.Angle(b - a, c - b) > 2f) result.Add(b);
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        bool OnLand(Vector2 p)
        {
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 30f, p.y), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.point.y > -0.3f && hit.point.y < 1.5f && hit.normal.y > 0.7f) return true;
            return false;
        }

        bool NearRail(Vector2 p, float clearance)
        {
            if (data.rails == null) return false;
            foreach (var rail in data.rails)
            {
                var pts = Geometry.ToPoints(rail.pts);
                for (int i = 0; i < pts.Length - 1; i++)
                    if (Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, pts[i], pts[i + 1], out _)) < clearance) return true;
            }
            return false;
        }

        // Somewhere a player can stand: off buildings, rails, roads and water.
        public bool StandFits(Vector2 p) =>
            !world.Buildings.Blocked(p, 1.5f) && !NearRail(p, 3f) && !world.Roads.Nearest(p, 4f, out _) && OnLand(p);

        bool RingFits(Vector2 p)
        {
            if (world.Buildings.Blocked(p, RingRadius + 0.5f)) return false;
            if (NearRail(p, RingRadius + 2f)) return false;
            if (world.Roads.Nearest(p, RingRadius + 0.5f, out _)) return false;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * Mathf.PI / 4f;
                if (!OnLand(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * RingRadius)) return false;
            }
            return OnLand(p);
        }

        void PlaceRing(Vector2 target)
        {
            var chosen = target;
            bool found = false;
            for (float r = 0f; r <= 90f && !found; r += 3f)
            {
                int steps = r == 0f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 3f);
                for (int i = 0; i < steps && !found; i++)
                {
                    float ang = i * Mathf.PI * 2f / steps;
                    var p = target + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    if (!RingFits(p)) continue;
                    chosen = p;
                    found = true;
                }
            }
            if (!found) Debug.LogWarning("[SpeedDial] no clear ground for the start ring; using the course point");
            RingCenter = Ground(chosen);
            var toStart = Route.Start - chosen;
            RingFacing = toStart.sqrMagnitude < 1f ? Vector3.forward : new Vector3(toStart.x, 0f, toStart.y).normalized;
        }

        // The start grid is staggered: two columns 3.5 m apart, each car 3.5 m behind the one beside it,
        // so 7 m to the car ahead in its own column, all facing down the route. Slots follow the room's
        // slot order, so every client seats every racer the same. The grid slides down the first
        // straight until each slot sits on asphalt clear of the kerbs, buildings and parked cars.
        public const float GridLateral = 1.75f;
        const float GridStep = 3.5f;

        public static float GridLane(int slot) => slot % 2 == 0 ? -GridLateral : GridLateral;

        Vector2 GridPoint(float front, int slot)
        {
            float d = front - slot * GridStep;
            return Route.PointAt(d) + Route.RightAt(d) * GridLane(slot);
        }

        // How many things are in the way of a grid whose front slot is `front` metres along the route: a
        // bend, kerbs, buildings, or a parked or standing vehicle on a slot or within a car's width of its
        // lane for the first 30 m.
        int GridProblems(float front)
        {
            int problems = 0;
            float back = front - 3f * GridStep;
            if (Route.TurnAhead(back - 2f, front - back + 30f) > 12f) problems++;
            for (int slot = 0; slot < 4; slot++)
            {
                if (world.Buildings.Blocked(GridPoint(front, slot), 1.5f)) problems++;
                for (float ahead = 0f; ahead <= 30f; ahead += 2.5f)
                {
                    var q = GridPoint(front + ahead, slot);
                    if (!world.Roads.OnCurbedCarriageway(q, -1.2f)) problems++;
                    if (occupied != null && occupied(q)) problems++;
                    foreach (var car in world.ParkedCars)
                        if ((new Vector2(car.Position.x, car.Position.z) - q).sqrMagnitude < 3f * 3f) problems++;
                }
            }
            return problems;
        }

        void PlaceGrid()
        {
            float first = 3f * GridStep + 4f;
            float front = first;
            int best = int.MaxValue;
            for (float d = first; d < Mathf.Min(120f, Route.Length * 0.4f) && best > 0; d += 2f)
            {
                int problems = GridProblems(d);
                if (problems >= best) continue;
                best = problems;
                front = d;
            }
            bool clear = best == 0;
            for (int slot = 0; slot < 4; slot++)
            {
                float d = front - slot * GridStep;
                Grid[slot] = new Spot { Position = Ground(GridPoint(front, slot)) + Vector3.up * 0.3f, Heading = RoutePath.Heading(Route.DirectionAt(d)), Progress = d };
            }
            Debug.Log($"[SpeedDial] grid from {front - 3f * GridStep:F0} to {front:F0} m along the route{(clear ? "" : $", {best} problems left")}");
        }

        void PlaceFinish()
        {
            var end = Route.End;
            Finish = Ground(end);
            var dir = Route.DirectionAt(Route.Length);
            FinishHeading = RoutePath.Heading(dir);
            FinishHalfWidth = world.Roads.Nearest(end, 5f, out var hit) ? RoadNetwork.HalfWidth(world.Roads.Roads[hit.Road]) : 4.5f;
        }
    }

    // Driveable streets as a directed graph keyed on shared OSM vertices. One-way streets only get
    // the edge in their drawing direction.
    public sealed class StreetGraph
    {
        readonly Dictionary<Vector2Int, int> ids = new Dictionary<Vector2Int, int>();
        readonly List<Vector2> nodes = new List<Vector2>();
        readonly List<List<(int to, float cost)>> edges = new List<List<(int, float)>>();
        readonly List<(int a, int b, bool oneway)> segments = new List<(int, int, bool)>();
        readonly Dictionary<int, float> heights = new Dictionary<int, float>();

        // freeway: also the I-280 deck and ramps, one way in their drawn direction, for a course over
        // the viaduct; their nodes keep the deck height for HeightAt.
        public StreetGraph(RoadNetwork roads, HashSet<string> kinds, bool freeway = false)
        {
            Streets(roads, kinds);
            if (!freeway || roads.Freeway == null) return;
            foreach (var way in roads.Freeway.Ways)
            {
                var pts = way.Points;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    int a = Node(new Vector2(pts[i].x, pts[i].z));
                    int b = Node(new Vector2(pts[i + 1].x, pts[i + 1].z));
                    if (a == b) continue;
                    if (pts[i].y > 0.5f) heights[a] = pts[i].y;
                    if (pts[i + 1].y > 0.5f) heights[b] = pts[i + 1].y;
                    edges[a].Add((b, Vector2.Distance(nodes[a], nodes[b])));
                    segments.Add((a, b, true));
                }
            }
        }

        public float HeightAt(Vector2 p) => ids.TryGetValue(Vector2Int.RoundToInt(p * 2f), out int id) && heights.TryGetValue(id, out float h) ? h : 0f;

        void Streets(RoadNetwork roads, HashSet<string> kinds)
        {
            for (int r = 0; r < roads.Roads.Length; r++)
            {
                var road = roads.Roads[r];
                if (!kinds.Contains(road.kind)) continue;
                var pts = roads.Points[r];
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    int a = Node(pts[i]);
                    int b = Node(pts[i + 1]);
                    if (a == b) continue;
                    float cost = Vector2.Distance(pts[i], pts[i + 1]);
                    edges[a].Add((b, cost));
                    if (!road.oneway) edges[b].Add((a, cost));
                    segments.Add((a, b, road.oneway));
                }
            }
        }

        int Node(Vector2 p)
        {
            var key = Vector2Int.RoundToInt(p * 2f);
            if (ids.TryGetValue(key, out int id)) return id;
            id = nodes.Count;
            ids[key] = id;
            nodes.Add(p);
            edges.Add(new List<(int, float)>());
            return id;
        }

        (int seg, Vector2 point) NearestSegment(Vector2 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            var point = p;
            for (int i = 0; i < segments.Count; i++)
            {
                var (a, b, _) = segments[i];
                var q = Geometry.ClosestPointOnSegment(p, nodes[a], nodes[b], out _);
                float d = (q - p).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
                point = q;
            }
            return (best, point);
        }

        // Street distance from the route to every node within `limit` of it, either way along every street:
        // how far off the route a stop at that node takes you.
        public List<(Vector2 point, float distance)> StreetDistances(RoutePath route, float limit)
        {
            var links = new List<List<(int to, float cost)>>();
            for (int i = 0; i < nodes.Count; i++) links.Add(new List<(int, float)>());
            foreach (var (a, b, _) in segments)
            {
                float cost = Vector2.Distance(nodes[a], nodes[b]);
                links[a].Add((b, cost));
                links[b].Add((a, cost));
            }
            var dist = new Dictionary<int, float>();
            var open = new SortedSet<(float d, int node)>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (Vector2.Distance(nodes[i], route.PointAt(route.Project(nodes[i]))) > 3f) continue;
                dist[i] = 0f;
                open.Add((0f, i));
            }
            while (open.Count > 0)
            {
                var (d, u) = open.Min;
                open.Remove(open.Min);
                foreach (var (v, cost) in links[u])
                {
                    float nd = d + cost;
                    if (nd > limit || (dist.TryGetValue(v, out float old) && nd >= old)) continue;
                    if (dist.ContainsKey(v)) open.Remove((old, v));
                    dist[v] = nd;
                    open.Add((nd, v));
                }
            }
            return dist.Select(kv => (nodes[kv.Key], kv.Value)).ToList();
        }

        // Street path from a street point near "from" to the one nearest "to". Every street within
        // 40 m of the nearest one is tried as the start, and the shortest total wins, so a start beside
        // a one-way carriageway never begins with a loop around the block.
        public List<Vector2> Route(Vector2 from, Vector2 to)
        {
            if (segments.Count == 0) return null;
            var (ts, tp) = NearestSegment(to);
            float nearest = Vector2.Distance(from, NearestSegment(from).point);
            List<Vector2> best = null;
            float bestCost = float.MaxValue;
            for (int fs = 0; fs < segments.Count; fs++)
            {
                var start = segments[fs];
                var fp = Geometry.ClosestPointOnSegment(from, nodes[start.a], nodes[start.b], out _);
                float approach = Vector2.Distance(from, fp);
                if (approach > nearest + 40f) continue;
                var path = Leg(fs, fp, ts, tp, out float cost);
                if (path == null || approach + cost >= bestCost) continue;
                bestCost = approach + cost;
                best = path;
            }
            return best ?? new List<Vector2> { NearestSegment(from).point, tp };
        }

        List<Vector2> Leg(int fs, Vector2 fp, int ts, Vector2 tp, out float cost)
        {
            var start = segments[fs];
            var goal = segments[ts];
            cost = float.MaxValue;
            if (fs == ts && (!start.oneway || Vector2.Dot(tp - fp, nodes[start.b] - nodes[start.a]) >= 0f))
            {
                cost = Vector2.Distance(fp, tp);
                return new List<Vector2> { fp, tp };
            }
            var sources = new List<(int node, float cost)> { (start.b, Vector2.Distance(fp, nodes[start.b])) };
            if (!start.oneway) sources.Add((start.a, Vector2.Distance(fp, nodes[start.a])));
            var targets = new Dictionary<int, float> { [goal.a] = Vector2.Distance(nodes[goal.a], tp) };
            if (!goal.oneway) targets[goal.b] = Vector2.Distance(nodes[goal.b], tp);
            var path = Search(sources, targets, tp, out cost);
            if (path == null) return null;
            var result = new List<Vector2> { fp };
            foreach (int n in path) result.Add(nodes[n]);
            result.Add(tp);
            return result;
        }

        List<int> Search(List<(int node, float cost)> sources, Dictionary<int, float> targets, Vector2 goal, out float total)
        {
            total = float.MaxValue;
            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var open = new SortedSet<(float f, int node)>();
            foreach (var (node, cost) in sources)
            {
                dist[node] = cost;
                prev[node] = -1;
                open.Add((cost + Vector2.Distance(nodes[node], goal), node));
            }
            int bestEnd = -1;
            float bestTotal = float.MaxValue;
            while (open.Count > 0)
            {
                var (f, u) = open.Min;
                open.Remove(open.Min);
                if (f >= bestTotal) break;
                if (targets.TryGetValue(u, out float tail) && dist[u] + tail < bestTotal)
                {
                    bestTotal = dist[u] + tail;
                    bestEnd = u;
                }
                foreach (var (v, cost) in edges[u])
                {
                    float nd = dist[u] + cost;
                    if (dist.TryGetValue(v, out float old) && nd >= old) continue;
                    if (dist.ContainsKey(v)) open.Remove((old + Vector2.Distance(nodes[v], goal), v));
                    dist[v] = nd;
                    prev[v] = u;
                    open.Add((nd + Vector2.Distance(nodes[v], goal), v));
                }
            }
            if (bestEnd < 0) return null;
            total = bestTotal;
            var path = new List<int>();
            for (int n = bestEnd; n >= 0; n = prev[n]) path.Add(n);
            path.Reverse();
            return path;
        }
    }
}
