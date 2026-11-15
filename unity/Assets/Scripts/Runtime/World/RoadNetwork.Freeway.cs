using UnityEngine;

namespace Dogpatch.World
{
    // The freeway's hook into the street network (agreed with CROS-5): the graph rides along, each
    // ramp-to-street junction is added to the intersection points, and the HUD's street name knows
    // when you are up on I-280. Street roads, segments and Nearest are untouched.
    public sealed partial class RoadNetwork
    {
        public FreewayGraph Freeway { get; private set; }

        public void AttachFreeway(FreewayGraph freeway)
        {
            Freeway = freeway;
            if (freeway == null) return;
            foreach (var junction in freeway.Junctions)
            {
                Intersections.Add(junction.Point);
                var cell = CellOf(junction.Point);
                if (!intersectionGrid.TryGetValue(cell, out var list)) intersectionGrid[cell] = list = new System.Collections.Generic.List<Vector2>();
                list.Add(junction.Point);
            }
        }

        public string StreetNameAt(Vector3 p)
        {
            if (Freeway != null && Freeway.OnDeck(p, out var way)) return way.Label;
            return StreetNameAt(new Vector2(p.x, p.z));
        }
    }
}
