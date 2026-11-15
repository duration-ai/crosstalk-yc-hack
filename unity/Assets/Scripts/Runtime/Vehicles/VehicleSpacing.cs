using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Vehicle footprints on the ground: each body's real extents as an oriented rectangle, used to
    // keep parked cars, traffic spawns and pooled reuse from landing inside one another, and to count
    // overlapping pairs for the logs.
    public static class VehicleSpacing
    {
        public struct Footprint
        {
            public Vector2 Center;
            public Vector2 Forward;
            public Vector2 Half;
        }

        static readonly Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();

        // Model-space bounds of a roster type or a Polyfork car model, built once and cached.
        public static Bounds BoundsOf(string model, ModelLibrary models)
        {
            if (bounds.TryGetValue(model, out var b)) return b;
            if (VehicleCatalog.Has(model))
            {
                var (visual, built) = VehicleModels.Build(VehicleCatalog.Get(model), 0, null);
                Object.Destroy(visual);
                b = built;
            }
            else b = models.Get(model).ComputeBounds();
            bounds[model] = b;
            return b;
        }

        public static Footprint At(Bounds model, Vector2 position, float heading)
        {
            var f = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var r = new Vector2(f.y, -f.x);
            return new Footprint { Center = position + r * model.center.x + f * model.center.z, Forward = f, Half = new Vector2(model.extents.x, model.extents.z) };
        }

        public static Footprint Of(CarController car)
        {
            var t = car.transform;
            var box = car.GetComponent<BoxCollider>();
            var c = box != null ? t.TransformPoint(box.center) : t.position;
            var f = new Vector2(t.forward.x, t.forward.z);
            return new Footprint { Center = new Vector2(c.x, c.z), Forward = f.sqrMagnitude > 1e-4f ? f.normalized : Vector2.up, Half = new Vector2(car.HalfExtents.x, car.HalfExtents.z) };
        }

        // Separating-axis test on the two rectangles, grown by gap.
        public static bool Overlap(in Footprint a, in Footprint b, float gap)
        {
            var d = b.Center - a.Center;
            float reach = a.Half.magnitude + b.Half.magnitude + gap;
            if (d.sqrMagnitude > reach * reach) return false;
            var ra = new Vector2(a.Forward.y, -a.Forward.x);
            var rb = new Vector2(b.Forward.y, -b.Forward.x);
            return Overlaps(d, a.Forward, a, ra, rb, b, gap) && Overlaps(d, ra, a, ra, rb, b, gap)
                && Overlaps(d, b.Forward, a, ra, rb, b, gap) && Overlaps(d, rb, a, ra, rb, b, gap);
        }

        static bool Overlaps(Vector2 d, Vector2 axis, in Footprint a, Vector2 ra, Vector2 rb, in Footprint b, float gap)
        {
            float pa = a.Half.x * Mathf.Abs(Vector2.Dot(ra, axis)) + a.Half.y * Mathf.Abs(Vector2.Dot(a.Forward, axis));
            float pb = b.Half.x * Mathf.Abs(Vector2.Dot(rb, axis)) + b.Half.y * Mathf.Abs(Vector2.Dot(b.Forward, axis));
            return Mathf.Abs(Vector2.Dot(d, axis)) <= pa + pb + gap;
        }

        // A set of placed footprints on a coarse grid, for placing many vehicles without overlap.
        public sealed class Lot
        {
            const float Cell = 8f;
            readonly Dictionary<Vector2Int, List<Footprint>> grid = new Dictionary<Vector2Int, List<Footprint>>();

            public bool Blocked(in Footprint f, float gap)
            {
                var c = Vector2Int.FloorToInt(f.Center / Cell);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (grid.TryGetValue(c + new Vector2Int(x, z), out var list))
                            foreach (var other in list)
                                if (Overlap(f, other, gap)) return true;
                return false;
            }

            public void Add(in Footprint f)
            {
                var c = Vector2Int.FloorToInt(f.Center / Cell);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<Footprint>();
                list.Add(f);
            }
        }

        // Pairs of cars whose bodies overlap, each pair once.
        public static List<(CarController a, CarController b)> OverlappingPairs(IReadOnlyList<CarController> cars, float gap = 0f)
        {
            var pairs = new List<(CarController, CarController)>();
            var feet = new Footprint[cars.Count];
            var grid = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < cars.Count; i++)
            {
                feet[i] = Of(cars[i]);
                var c = Vector2Int.FloorToInt(feet[i].Center / 8f);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < cars.Count; i++)
            {
                var c = Vector2Int.FloorToInt(feet[i].Center / 8f);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (grid.TryGetValue(c + new Vector2Int(x, z), out var list))
                            foreach (int j in list)
                                if (j > i && Overlap(feet[i], feet[j], gap)) pairs.Add((cars[i], cars[j]));
            }
            return pairs;
        }
    }
}
