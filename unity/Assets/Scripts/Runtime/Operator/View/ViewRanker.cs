using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Ranks what the caller can see: centred, big on screen and close wins. Pure maths over
    // viewport rects (0..1, origin bottom left) so it runs without a camera in tests.
    public static class ViewRanker
    {
        public const float MaxDistance = 120f;

        public static float Centredness(Rect r)
        {
            var c = new Vector2(Mathf.Clamp01(r.center.x), Mathf.Clamp01(r.center.y));
            return 1f - Mathf.Clamp01(Vector2.Distance(c, new Vector2(0.5f, 0.5f)) / 0.7071f);
        }

        public static float Area(Rect r) => Mathf.Clamp01(r.width) * Mathf.Clamp01(r.height);

        public static float Score(Rect r, float distance)
        {
            float size = Mathf.Clamp01(Mathf.Sqrt(Area(r)) / 0.6f);
            float near = 1f - Mathf.Clamp01(distance / MaxDistance);
            return 0.45f * Centredness(r) + 0.35f * size + 0.2f * near;
        }

        public static string Screen(Rect r)
        {
            float x = r.center.x;
            string side = x < 0.36f ? "left" : x > 0.64f ? "right" : "centre";
            float area = Area(r);
            string size = area < 0.012f ? "small" : area < 0.09f ? "medium" : "large";
            return side + ", " + size;
        }

        // Clips a viewport rect to the screen.
        public static Rect Clip(Rect r) => Rect.MinMaxRect(Mathf.Clamp01(r.xMin), Mathf.Clamp01(r.yMin), Mathf.Clamp01(r.xMax), Mathf.Clamp01(r.yMax));

        // Indices of the best candidates, best first: visible(i) is asked in score order until max
        // pass, and forced ones are always in.
        public static List<int> Rank(IReadOnlyList<(Rect rect, float distance, bool forced)> candidates, int max, System.Func<int, bool> visible)
        {
            var order = new List<int>();
            for (int i = 0; i < candidates.Count; i++) order.Add(i);
            order.Sort((a, b) => Score(candidates[b].rect, candidates[b].distance).CompareTo(Score(candidates[a].rect, candidates[a].distance)));
            int forced = 0;
            foreach (var c in candidates) if (c.forced) forced++;
            var picked = new List<int>();
            int free = 0;
            foreach (int i in order)
            {
                if (candidates[i].forced)
                {
                    picked.Add(i);
                    continue;
                }
                if (free >= max - forced) continue;
                if (visible != null && !visible(i)) continue;
                picked.Add(i);
                free++;
            }
            if (picked.Count > max) picked.RemoveRange(max, picked.Count - max);
            return picked;
        }
    }
}
