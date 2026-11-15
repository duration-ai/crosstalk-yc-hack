using System;
using System.Collections.Generic;
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
        public int routePayphones;
        public GeoPoint[] payphones;
    }

    public struct Spot
    {
        public Vector3 Position;
        public float Heading;
        public float Progress;
    }

    // The race laid onto the generated city: the start ring on clear ground near the start point,
    // a route that follows the street graph (one-way streets only one way) from the ring to YC, a
    // four-car grid at the head of the route, payphones on sidewalks, and the finish line.
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
        public List<Spot> Payphones = new List<Spot>();
        public Vector3 Finish;
        public float FinishHeading;
        public float FinishHalfWidth;
        public Vector3 YcSite;

        readonly WorldContext world;
        readonly WorldData data;

        RaceCourse(WorldContext world, WorldData data, CourseData course)
        {
            this.world = world;
            this.data = data;
            Data = course;
        }

        public static RaceCourse Build(WorldContext world, WorldData data, string json)
        {
            Physics.SyncTransforms();
            var course = new RaceCourse(world, data, JsonUtility.FromJson<CourseData>(json));
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

        void Plan()
        {
            RingRadius = Data.ringRadius > 0f ? Data.ringRadius : 7f;
            var startTarget = Project(Data.start);
            var finishTarget = Project(Data.finish);
            YcSite = Ground(finishTarget);

            var graph = new StreetGraph(world.Roads, RaceKinds);
            var stops = new List<Vector2> { startTarget };
            if (Data.via != null) foreach (var v in Data.via) stops.Add(Project(v));
            stops.Add(finishTarget);
            var points = new List<Vector2>();
            for (int i = 0; i < stops.Count - 1; i++)
            {
                var leg = graph.Route(stops[i], stops[i + 1]);
                if (leg == null || leg.Count < 2) leg = new List<Vector2> { stops[i], stops[i + 1] };
                foreach (var p in leg)
                    if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], p) > 0.5f) points.Add(p);
            }
            Route = new RoutePath(Simplify(points));
            foreach (var p in Route.Points) RouteWorld.Add(Ground(p));
            Debug.Log($"[SpeedDial] route {Data.startName} -> {Data.finishName}: {Route.Length:F0} m, {Route.Points.Length} points");

            PlaceRing(startTarget);
            PlaceGrid();
            PlaceFinish();
            PlacePayphones();
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

        bool GridClear(float front)
        {
            float back = front - 3f * GridStep;
            if (Route.TurnAhead(back - 2f, front - back + 20f) > 12f) return false;
            for (int slot = 0; slot < 4; slot++)
            {
                var p = GridPoint(front, slot);
                if (!world.Roads.OnCurbedCarriageway(p, -1.4f) || world.Buildings.Blocked(p, 1.5f)) return false;
                foreach (var car in world.ParkedCars)
                    if ((new Vector2(car.Position.x, car.Position.z) - p).sqrMagnitude < 4.5f * 4.5f) return false;
            }
            return true;
        }

        void PlaceGrid()
        {
            float first = 3f * GridStep + 4f;
            float front = first;
            bool clear = false;
            for (float d = first; d < Mathf.Min(120f, Route.Length * 0.4f); d += 2f)
                if (clear = GridClear(d))
                {
                    front = d;
                    break;
                }
            for (int slot = 0; slot < 4; slot++)
            {
                float d = front - slot * GridStep;
                Grid[slot] = new Spot { Position = Ground(GridPoint(front, slot)) + Vector3.up * 0.3f, Heading = RoutePath.Heading(Route.DirectionAt(d)), Progress = d };
            }
            Debug.Log($"[SpeedDial] grid from {front - 3f * GridStep:F0} to {front:F0} m along the route{(clear ? "" : ", no fully clear stretch found")}");
        }

        void PlaceFinish()
        {
            var end = Route.End;
            Finish = Ground(end);
            var dir = Route.DirectionAt(Route.Length);
            FinishHeading = RoutePath.Heading(dir);
            FinishHalfWidth = world.Roads.Nearest(end, 5f, out var hit) ? RoadNetwork.HalfWidth(world.Roads.Roads[hit.Road]) : 4.5f;
        }

        bool SidewalkSpot(Vector2 near, int preferSide, out Spot spot)
        {
            spot = default;
            if (!world.Roads.Nearest(near, 12f, out var hit)) return false;
            float hw = RoadNetwork.HalfWidth(world.Roads.Roads[hit.Road]);
            var n = new Vector2(hit.Direction.y, -hit.Direction.x);
            for (int k = 0; k < 2; k++)
            {
                float side = (k == 0 ? preferSide : -preferSide);
                var q = hit.Point + n * side * (hw + 1.6f);
                if (world.Buildings.Blocked(q, 0.8f) || world.Roads.DistanceToIntersection(q) < 12f || !OnLand(q)) continue;
                var facing = -n * side;
                spot = new Spot { Position = Ground(q), Heading = RoutePath.Heading(facing), Progress = Route.Project(q) };
                return true;
            }
            return false;
        }

        void PlacePayphones()
        {
            if (Data.payphones != null && Data.payphones.Length > 0)
            {
                foreach (var g in Data.payphones)
                    if (SidewalkSpot(Project(g), 1, out var s)) Payphones.Add(s);
                return;
            }

            var ring = new Vector2(RingCenter.x, RingCenter.z);
            if (world.Roads.Nearest(ring, 60f, out var near))
            {
                var away = (ring - near.Point).normalized;
                for (float shift = 10f; shift < 60f; shift += 6f)
                {
                    var probe = near.Point + near.Direction * shift + away * 2f;
                    if (!SidewalkSpot(probe, Vector2.Dot(new Vector2(near.Direction.y, -near.Direction.x), away) >= 0f ? 1 : -1, out var s)) continue;
                    Payphones.Add(s);
                    break;
                }
            }

            int count = Mathf.Max(0, Data.routePayphones);
            for (int i = 0; i < count; i++)
            {
                float d = Route.Length * (i + 1f) / (count + 1f);
                for (int tries = 0; tries < 6; tries++, d += 12f)
                {
                    if (d >= Route.Length - 20f) break;
                    if (!SidewalkSpot(Route.PointAt(d), i % 2 == 0 ? 1 : -1, out var s)) continue;
                    Payphones.Add(s);
                    break;
                }
            }
            Debug.Log($"[SpeedDial] {Payphones.Count} payphones placed");
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

        public StreetGraph(RoadNetwork roads, HashSet<string> kinds)
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
