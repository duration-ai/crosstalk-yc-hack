using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // Tall roadside unipoles with parody AI-startup ads, the wall of AI billboards SF drivers know from
    // 101: along Third St, Cesar Chavez, the 22nd St strip and I-280. Each column goes through the
    // street furniture's kerb check, and the head rises clear of the roofs it overhangs.
    public sealed partial class Landmarks
    {
        const float AdWidth = 12f;
        const float AdHeight = 4.4f;
        const float AdHeadOffset = 2f;

        static readonly (string street, float spacing)[] AdStreets =
        {
            ("3rd Street", 150f), ("Third Street", 150f), ("Cesar Chavez Street", 140f), ("22nd Street", 130f),
        };

        readonly List<Vector2> adSpots = new List<Vector2>();
        List<(Rect box, Vector2[] poly, float height)> roofs;
        int ad;

        void AdUnipoles(WorldData data)
        {
            roofs = new List<(Rect, Vector2[], float)>();
            foreach (var b in data.buildings)
            {
                var poly = Geometry.ToPoints(b.pts);
                roofs.Add((Geometry.BoundsOf(poly), poly, b.height));
            }

            var net = ctx.Roads;
            foreach (var (street, spacing) in AdStreets)
                for (int r = 0; r < net.Roads.Length; r++)
                {
                    if (net.Roads[r].name != street) continue;
                    var pts = net.Points[r];
                    float hw = RoadNetwork.HalfWidth(net.Roads[r]);
                    float walked = 0f, next = spacing * 0.5f;
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        float len = Vector2.Distance(pts[i], pts[i + 1]);
                        var d = (pts[i + 1] - pts[i]) / Mathf.Max(len, 1e-3f);
                        for (; next < walked + len; next += spacing)
                        {
                            var c = pts[i] + d * (next - walked);
                            float first = ad % 2 == 0 ? 1f : -1f;
                            foreach (var side in new[] { first, -first })
                                if (TryUnipole(c, d, new Vector2(-d.y, d.x) * side, hw + 4.5f, 0f, "billboard (AI unipole, " + street + ")")) break;
                        }
                        walked += len;
                    }
                }

            if (ctx.Roads.Freeway != null)
                foreach (var way in ctx.Roads.Freeway.Ways)
                {
                    if (way.Ramp) continue;
                    var p3 = way.Points;
                    for (float s = 140f; s < way.Length - 60f; s += 260f)
                    {
                        int i = 1;
                        while (i < p3.Length - 1 && way.Along[i] < s) i++;
                        float t = Mathf.InverseLerp(way.Along[i - 1], way.Along[i], s);
                        float deck = Mathf.Lerp(p3[i - 1].y, p3[i].y, t);
                        var c = Vector2.Lerp(new Vector2(p3[i - 1].x, p3[i - 1].z), new Vector2(p3[i].x, p3[i].z), t);
                        var d = new Vector2(p3[i].x - p3[i - 1].x, p3[i].z - p3[i - 1].z).normalized;
                        float first = ad % 2 == 0 ? 1f : -1f;
                        bool placed = false;
                        foreach (float setback in new[] { 9f, 16f, 24f })
                            foreach (var side in new[] { first, -first })
                                if (!placed && TryUnipole(c, d, new Vector2(-d.y, d.x) * side, way.HalfWidth + setback, deck + 4f, "billboard (AI unipole, I-280)")) placed = true;
                    }
                }
        }

        // A unipole beside the road at c (direction d), on the side n, setback from the centreline. Both
        // faces stand nearly square to the road, one for each direction of traffic.
        bool TryUnipole(Vector2 c, Vector2 d, Vector2 n, float setback, float minHead, string kind)
        {
            var at = c + n * setback;
            foreach (var s in adSpots)
                if (Vector2.Distance(s, at) < 70f) return false;
            if (!ctx.Furniture.ClearSpot(ref at, 1.0f)) return false;
            if (ctx.Buildings.Blocked(at, 1.2f)) return false;

            var freeway = ctx.Roads.Freeway;
            if (freeway != null && freeway.SurfaceAt(at, 0f, 1f, 100f, out _, out _, -1, 2f)) return false;

            var face = (-d * 0.94f - n * 0.34f).normalized;
            var right = new Vector2(-face.y, face.x);
            float offset = Vector2.Dot(right, n) > 0f ? AdHeadOffset : -AdHeadOffset;
            float clear = minHead;
            for (int k = -2; k <= 2; k++)
            {
                var p = at + right * (offset + k * AdWidth * 0.25f);
                if (freeway != null && minHead <= 0f && freeway.SurfaceAt(p, 0f, 1f, 60f, out _, out _, -1, 3f)) return false;
                foreach (var (box, poly, height) in roofs)
                    if (box.Contains(p) && height > clear - 1.5f && Geometry.Contains(poly, p)) clear = height + 1.5f;
            }
            float poleHeight = Mathf.Max(10f, clear);
            if (poleHeight > 30f) return false;

            float heading = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
            var front = LandmarkMeshes.Ads[ad % LandmarkMeshes.Ads.Length];
            var back = LandmarkMeshes.Ads[(ad + 7) % LandmarkMeshes.Ads.Length];
            ad++;
            var pivot = Matrix4x4.TRS(MeshBuilder.At(at, 0f), Quaternion.Euler(0f, heading, 0f), Vector3.one);
            statics.GeneratedInstance(StaticModels.Layer.Landmarks, at, "billboard", "ad-unipole-" + ad, PhysicsClass.Breakable, 2400f, "steel", pivot, new Vector3(1.2f, poleHeight, 1.2f),
                mb => LandmarkMeshes.Unipole(mb, MeshBuilder.At(at, 0f), heading, AdWidth, AdHeight, poleHeight, offset, front, back));
            adSpots.Add(at);
            Count(kind);
            return true;
        }
    }
}
