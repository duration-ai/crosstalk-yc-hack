using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.World
{
    // Tim's landmark list, placed from the extract: the Crane Cove cranes (a built gantry under a
    // recoloured Polyfork tower crane), the Potrero stack, the I-280 viaduct, Pier 70's drydock,
    // rail and light-rail platforms, and AI billboards along the viaduct and Third Street. The YC
    // office, the machine shop and the cream stone buildings are footprints that BuildingFitter dresses.
    public sealed partial class Landmarks
    {
        public readonly Dictionary<string, int> Placed = new Dictionary<string, int>();
        public readonly List<string> Notes = new List<string>();

        readonly WorldContext ctx;
        readonly StaticModels statics;
        int billboard;

        public Landmarks(WorldContext ctx, StaticModels statics)
        {
            this.ctx = ctx;
            this.statics = statics;
        }

        void Count(string kind)
        {
            Placed.TryGetValue(kind, out int n);
            Placed[kind] = n + 1;
        }

        public void Build(WorldData data)
        {
            if (data.landmarks != null)
                foreach (var l in data.landmarks)
                {
                    var p = new Vector2(l.x, l.z);
                    switch (l.kind)
                    {
                        case "crane": Crane(p, l.headingKnown ? l.heading : 0f, l.heightM > 0f ? l.heightM : 40f); Count("crane"); break;
                        case "smokestack":
                            float h = l.heightM > 0f ? l.heightM : 91f;
                            LandmarkMeshes.Smokestack(statics.Generated(StaticModels.Layer.Landmarks, p), p, h, h * 0.045f, h * 0.028f);
                            Count("smokestack");
                            break;
                        case "drydock":
                            var outline = l.pts != null && l.pts.Length >= 6 ? Geometry.ToPoints(l.pts) : null;
                            if (outline == null) { Notes.Add("drydock has no outline"); break; }
                            if (Geometry.SignedArea(outline) < 0f) System.Array.Reverse(outline);
                            LandmarkMeshes.Drydock(statics.Generated(StaticModels.Layer.Landmarks, Geometry.Centroid(outline)), outline, 1.2f, 7f);
                            Count("drydock");
                            break;
                        case "billboard":
                            Billboard(p, l.headingKnown ? l.heading : FaceStreet(p), 0f, 3.5f);
                            Count("billboard (OSM)");
                            break;
                    }
                }
            if (data.platforms != null)
                foreach (var platform in data.platforms)
                {
                    if (platform.pts == null || platform.pts.Length < 4) { Notes.Add("platform " + platform.osmId + " has no geometry"); continue; }
                    var pts = Geometry.ToPoints(platform.pts);
                    float width = 3.5f;
                    if (platform.closed && pts.Length >= 3)
                    {
                        var box = OrientedBox.Of(pts);
                        bool alongAxis = box.ExtentAxis >= box.ExtentNormal;
                        var axis = alongAxis ? box.Axis : box.Normal;
                        float half = alongAxis ? box.ExtentAxis : box.ExtentNormal;
                        width = Mathf.Clamp(2f * (alongAxis ? box.ExtentNormal : box.ExtentAxis), 2f, 10f);
                        pts = new[] { box.Center - axis * half, box.Center + axis * half };
                    }
                    float height = platform.kind == "rail" ? 1.0f : 0.9f;
                    var pieces = ClearOfStreets(pts, width, height);
                    if (pieces.Count == 0) continue;
                    var longest = pieces[0];
                    foreach (var piece in pieces)
                    {
                        LandmarkMeshes.Platform(statics.Generated(StaticModels.Layer.Landmarks, piece[piece.Length / 2]), piece, width, height);
                        if (Vector2.Distance(piece[0], piece[piece.Length - 1]) > Vector2.Distance(longest[0], longest[longest.Length - 1])) longest = piece;
                    }
                    var dir = (pts[pts.Length - 1] - pts[0]).normalized;
                    var shelterAt = Vector2.Lerp(longest[0], longest[longest.Length - 1], 0.5f);
                    float yaw = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
                    statics.PlaceAtOrigin(StaticModels.Layer.Landmarks, "p-shelter", shelterAt, Matrix4x4.TRS(MeshBuilder.At(shelterAt, height), Quaternion.Euler(0f, yaw, 0f), Vector3.one), "osm:" + platform.osmId);
                    Count("platform:" + platform.kind);
                }

            if (ctx.Roads.Freeway != null) FreewayBillboards(ctx.Roads.Freeway);
            ThirdStreetBillboards(data);
            AdUnipoles(data);
        }

        // OSM platforms sit in the Caltrain cut under the street bridges, but the map is flat, so a platform
        // stops short of every street that crosses it (its end ramps included) instead of walling the road.
        List<Vector2[]> ClearOfStreets(Vector2[] centerline, float width, float height)
        {
            const float Step = 0.5f;
            float ramp = Mathf.Clamp(height * 10f, 2f, 12f) + 1f;
            var samples = new List<(Vector2 p, bool blocked)>();
            for (int i = 0; i + 1 < centerline.Length; i++)
            {
                var a = centerline[i];
                var b = centerline[i + 1];
                var axis = (b - a).normalized;
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / Step));
                for (int k = 0; k <= n; k++)
                {
                    if (k == n && i + 2 < centerline.Length) continue;
                    var p = Vector2.Lerp(a, b, k / (float)n);
                    bool crossing = ctx.Roads.Nearest(p, width * 0.5f + 1f, out var hit) && Mathf.Abs(Vector2.Dot(hit.Direction, axis)) < 0.8f;
                    samples.Add((p, crossing));
                }
            }
            var keep = new bool[samples.Count];
            int reach = Mathf.CeilToInt(ramp / Step);
            for (int i = 0; i < samples.Count; i++)
            {
                keep[i] = true;
                for (int j = Mathf.Max(0, i - reach); j <= Mathf.Min(samples.Count - 1, i + reach) && keep[i]; j++)
                    if (samples[j].blocked) keep[i] = false;
            }
            var pieces = new List<Vector2[]>();
            var run = new List<Vector2>();
            for (int i = 0; i <= samples.Count; i++)
            {
                if (i < samples.Count && keep[i]) { run.Add(samples[i].p); continue; }
                if (run.Count > 1 && Vector2.Distance(run[0], run[run.Count - 1]) >= 8f)
                    pieces.Add(centerline.Length == 2 ? new[] { run[0], run[run.Count - 1] } : run.ToArray());
                run.Clear();
            }
            return pieces;
        }

        // Crane Cove: a four-legged portal on rail bogies, a yellow lattice mast on top and the jib.
        void Crane(Vector2 p, float heading, float height)
        {
            float legs = Mathf.Clamp(height * 0.3f, 8f, 16f);
            var mb = statics.Generated(StaticModels.Layer.Landmarks, p);
            LandmarkMeshes.CraneGantry(mb, p, heading, 10f, legs);
            float top = LandmarkMeshes.GantryTop(legs);
            var mast = statics.Mesh("p-crane-mast");
            var jib = statics.Mesh("p-crane-top");
            if (mast == null || jib == null) return;
            float mastHeight = Mathf.Max(height - top - jib.bounds.size.y * 0.5f, 4f);
            var rot = Quaternion.Euler(0f, heading, 0f);
            statics.PlaceAtOrigin(StaticModels.Layer.Landmarks, "p-crane-mast", p, Matrix4x4.TRS(MeshBuilder.At(p, top), rot, new Vector3(1f, mastHeight / mast.bounds.size.y, 1f)));
            statics.PlaceAtOrigin(StaticModels.Layer.Landmarks, "p-crane-top", p, Matrix4x4.TRS(MeshBuilder.At(p, top + mastHeight), rot, Vector3.one));
        }

        // I-280 billboards beside the drivable deck (Freeway builds the deck itself), one about every
        // 170 m along the main line, standing clear of streets and every deck and ramp.
        void FreewayBillboards(FreewayGraph freeway)
        {
            foreach (var way in freeway.Ways)
            {
                if (way.Ramp) continue;
                var pts = way.Points;
                for (float s = 80f; s < way.Length - 40f; s += 170f)
                {
                    int i = 1;
                    while (i < pts.Length - 1 && way.Along[i] < s) i++;
                    float t = Mathf.InverseLerp(way.Along[i - 1], way.Along[i], s);
                    float h = Mathf.Lerp(pts[i - 1].y, pts[i].y, t);
                    var c = Vector2.Lerp(new Vector2(pts[i - 1].x, pts[i - 1].z), new Vector2(pts[i].x, pts[i].z), t);
                    var d = new Vector2(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z).normalized;
                    var side = (billboard % 2 == 0 ? 1f : -1f) * new Vector2(-d.y, d.x);
                    var at = c + side * (way.HalfWidth + 6f);
                    if (ctx.Buildings.Blocked(at, 4f) || ctx.Roads.Nearest(at, 3f, out _) || freeway.SurfaceAt(at, 0f, 1f, 100f, out _, out _, -1, 6f)) continue;
                    float facing = Mathf.Atan2(-d.x, -d.y) * Mathf.Rad2Deg + (billboard % 2 == 0 ? 20f : -20f);
                    Billboard(at, facing, 0f, h + 1.5f);
                    Count("billboard (I-280)");
                }
            }
        }

        // Rooftop billboards on the buildings that line Third Street, one about every 220 m.
        void ThirdStreetBillboards(WorldData data)
        {
            var net = ctx.Roads;
            float next = 60f, walked = 0f;
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (net.Roads[r].name != "3rd Street" && net.Roads[r].name != "Third Street") continue;
                var pts = net.Points[r];
                float hw = RoadNetwork.HalfWidth(net.Roads[r]);
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float len = Vector2.Distance(pts[i], pts[i + 1]);
                    var d = (pts[i + 1] - pts[i]) / Mathf.Max(len, 1e-3f);
                    for (; next < walked + len; next += 220f)
                    {
                        var c = pts[i] + d * (next - walked);
                        foreach (var side in new[] { 1f, -1f })
                        {
                            var n = new Vector2(-d.y, d.x) * side;
                            var roof = Roof(data, c + n * (hw + 12f));
                            if (roof < 0f) continue;
                            var at = c + n * (hw + 9f);
                            Billboard(at, Mathf.Atan2(-n.x, -n.y) * Mathf.Rad2Deg, roof, 3f);
                            Count("billboard (Third St rooftop)");
                            break;
                        }
                    }
                    walked += len;
                }
            }
        }

        float Roof(WorldData data, Vector2 p)
        {
            foreach (var b in data.buildings)
            {
                if (b.height > 25f || b.height < 4f) continue;
                var poly = Geometry.ToPoints(b.pts);
                if (Geometry.Contains(poly, p)) return b.height;
            }
            return -1f;
        }

        // One breakable instance per billboard (panel on its legs), with a solid box over the legs.
        void Billboard(Vector2 at, float heading, float baseY, float poleHeight)
        {
            var lines = LandmarkMeshes.BillboardLines;
            string text = lines[billboard++ % lines.Length];
            var panel = WorldBuilder.Palette(billboard % 3 == 0 ? "sunshine" : billboard % 3 == 1 ? "cloud" : "mint");
            var pivot = Matrix4x4.TRS(MeshBuilder.At(at, baseY), Quaternion.Euler(0f, heading, 0f), Vector3.one);
            statics.GeneratedInstance(StaticModels.Layer.Landmarks, at, "billboard", "billboard-" + billboard, PhysicsClass.Breakable, 900f, "steel", pivot, new Vector3(10f, poleHeight, 0.6f),
                mb => LandmarkMeshes.BillboardPanel(mb, MeshBuilder.At(at, baseY), heading, 10f, 4f, poleHeight, text, panel, WorldBuilder.Palette("ink"), WorldBuilder.Palette("ink")));
        }

        float FaceStreet(Vector2 p)
        {
            if (!ctx.Roads.Nearest(p, 80f, out var hit)) return 0f;
            var d = hit.Point - p;
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }
    }
}
