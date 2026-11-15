using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // The view sent with a call, taken when Call is pressed: tagged entities within about 120 m,
    // inside the camera frustum and not hidden behind something (a few raycasts each), ranked by
    // ViewRanker, top 8. While driving, the caller's own car is always in as veh_me.
    public static class ViewSnapshot
    {
        const int Max = 8;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public static OperatorView Take(Camera cam, Vector3 focus, bool driving, Transform[] ignore, string timeOfDay, string weather)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var eye = cam.transform.position;
            var tags = new List<EntityTag>();
            var candidates = new List<(Rect rect, float distance, bool forced)>();
            foreach (var tag in EntityTag.All)
            {
                if (tag == null || !tag.isActiveAndEnabled || (tag.Span != null && tag.Span.Hidden)) continue;
                var bounds = tag.WorldBounds;
                bool mine = driving && tag.ControlledBy == "me";
                if (!mine && !tag.gameObject.activeInHierarchy) continue;
                float range = Vector3.Distance(bounds.ClosestPoint(eye), eye);
                if (!mine && (range > ViewRanker.MaxDistance || !GeometryUtility.TestPlanesAABB(planes, bounds))) continue;
                tags.Add(tag);
                candidates.Add((ViewportRect(cam, bounds), Vector3.Distance(bounds.ClosestPoint(focus), focus), mine));
            }
            var picked = ViewRanker.Rank(candidates, Max, i => Visible(eye, tags[i], ignore));
            var entities = new ViewEntity[picked.Count];
            for (int n = 0; n < picked.Count; n++)
            {
                var tag = tags[picked[n]];
                var c = candidates[picked[n]];
                bool mine = driving && tag.ControlledBy == "me";
                entities[n] = new ViewEntity
                {
                    id = mine ? "veh_me" : tag.Id,
                    @class = tag.Class,
                    desc = tag.Desc,
                    color = tag.Color,
                    screen = ViewRanker.Screen(c.rect),
                    distance_m = Mathf.Round(c.distance),
                    view_rank = n + 1,
                    @protected = tag.Protected,
                    controlledBy = tag.ControlledBy ?? "",
                };
            }
            return new OperatorView { mode = driving ? "driving" : "onFoot", timeOfDay = timeOfDay, weather = weather, entities = entities };
        }

        static Rect ViewportRect(Camera cam, Bounds b)
        {
            float xMin = 1f, yMin = 1f, xMax = 0f, yMax = 0f;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var v = cam.WorldToViewportPoint(corner);
                if (v.z <= 0f) continue;
                any = true;
                xMin = Mathf.Min(xMin, v.x);
                yMin = Mathf.Min(yMin, v.y);
                xMax = Mathf.Max(xMax, v.x);
                yMax = Mathf.Max(yMax, v.y);
            }
            return any ? ViewRanker.Clip(Rect.MinMaxRect(xMin, yMin, xMax, yMax)) : new Rect(0.5f, 0.5f, 0f, 0f);
        }

        // Visible when a ray to any of a few points on the entity reaches it before anything else.
        static bool Visible(Vector3 eye, EntityTag tag, Transform[] ignore)
        {
            var b = tag.WorldBounds;
            var reach = new Bounds(b.center, b.size + Vector3.one * 1.2f);
            var points = new[] { b.center, b.center + Vector3.up * b.extents.y * 0.6f, new Vector3(b.min.x, b.center.y, b.center.z), new Vector3(b.max.x, b.center.y, b.center.z) };
            foreach (var p in points)
            {
                var to = p - eye;
                float length = to.magnitude;
                if (length < 0.01f) return true;
                int count = Physics.RaycastNonAlloc(eye, to / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                Vector3 at = p;
                Transform blocker = null;
                for (int i = 0; i < count; i++)
                {
                    if (Ignored(hits[i].transform, ignore) || hits[i].distance >= nearest) continue;
                    nearest = hits[i].distance;
                    at = hits[i].point;
                    blocker = hits[i].transform;
                }
                if (blocker == null || nearest >= length - 0.5f || reach.Contains(at) || blocker.IsChildOf(tag.transform)) return true;
            }
            return false;
        }

        static bool Ignored(Transform t, Transform[] ignore)
        {
            if (ignore == null) return false;
            foreach (var i in ignore)
                if (i != null && t.IsChildOf(i)) return true;
            return false;
        }
    }
}
