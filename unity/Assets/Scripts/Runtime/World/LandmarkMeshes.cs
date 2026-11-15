using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.World
{
    // Hand-built low-poly landmark geometry appended to a MeshBuilder. Everything is flat shaded
    // and vertex coloured in the Speed Dial palette; ground level is y = 0 unless a base is passed.
    public static class LandmarkMeshes
    {
        public static Color Hex(string hex)
        {
            string h = hex.TrimStart('#');
            float r = System.Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
            float g = System.Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
            float b = System.Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            return new Color(ToLinear(r), ToLinear(g), ToLinear(b), 1f);
        }

        static float ToLinear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        static readonly Color Asphalt = Hex("#A9ADB4");
        static readonly Color Shadow = Hex("#6C758B");
        static readonly Color Shade = Hex("#A3AEC4");
        static readonly Color Brick = Hex("#E2603F");
        static readonly Color Sunshine = Hex("#FFCB1F");
        static readonly Color CraneYellow = Hex("#FFC21A");
        static readonly Color Lemon = Hex("#FFE27A");
        static readonly Color Cloud = Hex("#FFFFFF");
        static readonly Color Cream = Hex("#FFF3DA");
        static readonly Color Ink = Hex("#1C2347");
        static readonly Color Sidewalk = Hex("#EEE7DA");
        static readonly Color ConcreteMid = Color.Lerp(Sidewalk, Shade, 0.45f);
        static readonly Color ConcreteDark = Color.Lerp(Shade, Shadow, 0.5f);

        // ------------------------------------------------------------------ shared helpers

        static Quaternion Yaw(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        static Vector3 V3(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Vector3 Dir3(Vector2 d) => new Vector3(d.x, 0f, d.y);

        static float Heading(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        // Square-section beam between two points. Caps are optional so braces buried in legs stay cheap.
        static void Beam(MeshBuilder mb, Vector3 a, Vector3 b, float thickness, Color color, bool caps = true)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            var reference = Mathf.Abs(d.y) > 0.95f ? Vector3.right : Vector3.up;
            var u = Vector3.Cross(d, reference).normalized * (thickness * 0.5f);
            var v = Vector3.Cross(d, u).normalized * (thickness * 0.5f);
            var ca = new[] { a - u - v, a + u - v, a + u + v, a - u + v };
            var cb = new[] { b - u - v, b + u - v, b + u + v, b - u + v };
            for (int k = 0; k < 4; k++)
            {
                int n = (k + 1) % 4;
                mb.Quad(ca[k], ca[n], cb[n], cb[k], color, (ca[k] + ca[n]) * 0.5f - a);
            }
            if (!caps) return;
            mb.Quad(ca[0], ca[1], ca[2], ca[3], color, -d);
            mb.Quad(cb[0], cb[1], cb[2], cb[3], color, d);
        }

        // Frustum with flat facets. Top cap optional, bottom never drawn (it sits on the ground).
        static void Frustum(MeshBuilder mb, Vector2 c, float y0, float y1, float r0, float r1, int sides, Color wall, Color top, bool capTop)
        {
            float phase = Mathf.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float a0 = phase + i * Mathf.PI * 2f / sides;
                float a1 = phase + (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                var mid = Dir3((d0 + d1).normalized);
                mb.Quad(V3(c + d0 * r0, y0), V3(c + d1 * r0, y0), V3(c + d1 * r1, y1), V3(c + d0 * r1, y1), wall, mid);
                if (capTop) mb.Triangle(V3(c, y1), V3(c + d0 * r1, y1), V3(c + d1 * r1, y1), top, Vector3.up);
            }
        }

        // Extrudes a convex (lateral, vertical) profile, wound counter-clockwise, along a horizontal axis.
        static void Prism(MeshBuilder mb, Vector2 center, float baseY, Vector2 lateral, Vector2 along, float depth, Vector2[] profile, Color color)
        {
            Vector3 P(int i, float s) => V3(center + lateral * profile[i].x + along * s, baseY + profile[i].y);
            float h = depth * 0.5f;
            int n = profile.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var e = profile[j] - profile[i];
                var normal = Dir3(lateral) * e.y + Vector3.up * -e.x;
                mb.Quad(P(i, -h), P(j, -h), P(j, h), P(i, h), color, normal);
            }
            for (int i = 1; i + 1 < n; i++)
            {
                mb.Triangle(P(0, h), P(i, h), P(i + 1, h), color, Dir3(along));
                mb.Triangle(P(0, -h), P(i, -h), P(i + 1, -h), color, -Dir3(along));
            }
        }

        // Cross-section frames along a polyline: mitred left offsets (as in MeshBuilder.Ribbon) plus
        // a height and arc length per vertex. Long segments are split so the height can vary smoothly.
        struct Section
        {
            public Vector2 p;
            public Vector2 left;
            public float h;
            public float s;
        }

        static Section Mix(Section a, Section b, float t) => new Section
        {
            p = Vector2.Lerp(a.p, b.p, t),
            left = Vector2.Lerp(a.left, b.left, t),
            h = Mathf.Lerp(a.h, b.h, t),
            s = Mathf.Lerp(a.s, b.s, t),
        };

        static Vector2 SegDir(List<Section> secs, int i) => (secs[i + 1].p - secs[i].p).normalized;

        static Vector2 LeftOf(Vector2 d) => new Vector2(-d.y, d.x);

        static List<Section> BuildSections(Vector2[] pts, float[] heights, float halfWidth, float maxStep)
        {
            var pp = new List<Vector2>();
            var hh = new List<float>();
            for (int i = 0; i < pts.Length; i++)
            {
                float h = heights == null || heights.Length == 0 ? 0f : heights[Mathf.Min(i, heights.Length - 1)];
                if (pp.Count > 0)
                {
                    var prev = pp[pp.Count - 1];
                    float len = (pts[i] - prev).magnitude;
                    if (len < 0.01f) continue;
                    int n = Mathf.CeilToInt(len / maxStep);
                    float prevH = hh[hh.Count - 1];
                    for (int k = 1; k < n; k++)
                    {
                        float t = k / (float)n;
                        pp.Add(Vector2.Lerp(prev, pts[i], t));
                        hh.Add(Mathf.Lerp(prevH, h, t));
                    }
                }
                pp.Add(pts[i]);
                hh.Add(h);
            }
            var secs = new List<Section>(pp.Count);
            if (pp.Count < 2) return secs;
            float arc = 0f;
            for (int i = 0; i < pp.Count; i++)
            {
                var dPrev = i > 0 ? (pp[i] - pp[i - 1]).normalized : (pp[1] - pp[0]).normalized;
                var dNext = i < pp.Count - 1 ? (pp[i + 1] - pp[i]).normalized : dPrev;
                var nPrev = LeftOf(dPrev);
                var nNext = LeftOf(dNext);
                var miter = (nPrev + nNext).normalized;
                if (miter.sqrMagnitude < 1e-4f) miter = nNext;
                if (i > 0) arc += (pp[i] - pp[i - 1]).magnitude;
                secs.Add(new Section { p = pp[i], left = miter * (halfWidth / Mathf.Max(Vector2.Dot(miter, nNext), 0.5f)), h = hh[i], s = arc });
            }
            return secs;
        }

        // Point on a section at a lateral fraction f of the half width (+1 left edge, -1 right edge).
        static Vector3 Pt(Section s, float f, float dy) => V3(s.p + s.left * f, s.h + dy);

        static Section SampleAt(List<Section> secs, float s, out Vector2 dir)
        {
            int i = 0;
            while (i < secs.Count - 2 && secs[i + 1].s < s) i++;
            dir = SegDir(secs, i);
            return Mix(secs[i], secs[i + 1], Mathf.InverseLerp(secs[i].s, secs[i + 1].s, s));
        }

        // Flat stripe on a lofted surface, solid or dashed by arc length.
        static void Stripe(MeshBuilder mb, List<Section> secs, float halfWidth, float lateral, float lineWidth, Color color, float dash = 0f, float period = 0f)
        {
            const float lift = 0.04f;
            float f = lateral / halfWidth;
            float df = lineWidth * 0.5f / halfWidth;
            for (int i = 0; i + 1 < secs.Count; i++)
            {
                var a = secs[i];
                var b = secs[i + 1];
                float len = b.s - a.s;
                if (len < 1e-3f) continue;
                if (dash <= 0f)
                {
                    mb.Quad(Pt(a, f - df, lift), Pt(b, f - df, lift), Pt(b, f + df, lift), Pt(a, f + df, lift), color, Vector3.up);
                    continue;
                }
                for (float k = Mathf.Floor(a.s / period) * period; k < b.s; k += period)
                {
                    float s0 = Mathf.Max(a.s, k);
                    float s1 = Mathf.Min(b.s, k + dash);
                    if (s1 - s0 < 0.05f) continue;
                    var m0 = Mix(a, b, (s0 - a.s) / len);
                    var m1 = Mix(a, b, (s1 - a.s) / len);
                    mb.Quad(Pt(m0, f - df, lift), Pt(m1, f - df, lift), Pt(m1, f + df, lift), Pt(m0, f + df, lift), color, Vector3.up);
                }
            }
        }

        static void Fan(MeshBuilder mb, Vector3[] pts, Color color, Vector3 facing)
        {
            for (int i = 1; i + 1 < pts.Length; i++) mb.Triangle(pts[0], pts[i], pts[i + 1], color, facing);
        }

        // ------------------------------------------------------------------ viaduct

        // Elevated freeway: deckHeights are the top-surface heights at each centreline vertex.
        public static void Viaduct(MeshBuilder mb, Vector2[] centerline, float[] deckHeights, float width)
        {
            if (centerline == null || centerline.Length < 2) return;
            const float thick = 1.8f;
            const float parapetW = 0.5f;
            const float parapetH = 1.1f;
            const float slabEdge = 0.7f;
            const float chamfer = 1.5f;
            float hw = width * 0.5f;
            var secs = BuildSections(centerline, deckHeights, hw, 8f);
            if (secs.Count < 2) return;

            var profile = new (float l0, float v0, float l1, float v1, Color color, float nl, float nv)[]
            {
                (hw - parapetW, 0f, hw - parapetW, parapetH, Sidewalk, -1f, 0f),
                (hw - parapetW, parapetH, hw, parapetH, Cream, 0f, 1f),
                (hw, parapetH, hw, 0f, Sidewalk, 1f, 0f),
                (hw, 0f, hw, -slabEdge, ConcreteMid, 1f, 0f),
                (hw, -slabEdge, hw - chamfer, -thick, Shade, 1.1f, -1.5f),
            };
            float deckF = (hw - parapetW) / hw;
            float soffitF = (hw - chamfer) / hw;

            for (int i = 0; i + 1 < secs.Count; i++)
            {
                var a = secs[i];
                var b = secs[i + 1];
                var left = Dir3(LeftOf(SegDir(secs, i)));
                mb.Quad(Pt(a, -deckF, 0f), Pt(b, -deckF, 0f), Pt(b, deckF, 0f), Pt(a, deckF, 0f), Asphalt, Vector3.up);
                mb.Quad(Pt(a, -soffitF, -thick), Pt(b, -soffitF, -thick), Pt(b, soffitF, -thick), Pt(a, soffitF, -thick), ConcreteDark, Vector3.down);
                foreach (float side in new[] { 1f, -1f })
                {
                    foreach (var e in profile)
                    {
                        mb.Quad(Pt(a, side * e.l0 / hw, e.v0), Pt(b, side * e.l0 / hw, e.v0), Pt(b, side * e.l1 / hw, e.v1), Pt(a, side * e.l1 / hw, e.v1),
                            e.color, left * (side * e.nl) + Vector3.up * e.nv);
                    }
                }
            }

            for (int end = 0; end < 2; end++)
            {
                var s = end == 0 ? secs[0] : secs[secs.Count - 1];
                var facing = Dir3(end == 0 ? -SegDir(secs, 0) : SegDir(secs, secs.Count - 2));
                Fan(mb, new[]
                {
                    Pt(s, 1f, 0f), Pt(s, 1f, -slabEdge), Pt(s, soffitF, -thick),
                    Pt(s, -soffitF, -thick), Pt(s, -1f, -slabEdge), Pt(s, -1f, 0f),
                }, ConcreteMid, facing);
                foreach (float side in new[] { 1f, -1f })
                    mb.Quad(Pt(s, side * deckF, 0f), Pt(s, side * deckF, parapetH), Pt(s, side, parapetH), Pt(s, side, 0f), Sidewalk, facing);
            }

            float edgeLine = hw - parapetW - 0.6f;
            Stripe(mb, secs, hw, edgeLine, 0.28f, Sunshine);
            Stripe(mb, secs, hw, -edgeLine, 0.28f, Cloud);
            if (width >= 16f)
            {
                Stripe(mb, secs, hw, 0.2f, 0.14f, Sunshine);
                Stripe(mb, secs, hw, -0.2f, 0.14f, Sunshine);
                int lanes = Mathf.Max(1, Mathf.RoundToInt((edgeLine - 0.3f) / 3.6f));
                for (int k = 1; k < lanes; k++)
                {
                    float lat = 0.3f + k * (edgeLine - 0.3f) / lanes;
                    Stripe(mb, secs, hw, lat, 0.2f, Cream, 3f, 9f);
                    Stripe(mb, secs, hw, -lat, 0.2f, Cream, 3f, 9f);
                }
            }
            else
            {
                int lanes = Mathf.Max(1, Mathf.RoundToInt(edgeLine * 2f / 3.6f));
                for (int k = 1; k < lanes; k++)
                    Stripe(mb, secs, hw, -edgeLine + k * edgeLine * 2f / lanes, 0.2f, Cream, 3f, 9f);
            }

            float total = secs[secs.Count - 1].s;

            for (float st = 2.5f; st < total; st += 5f)
            {
                var m = SampleAt(secs, st, out var d);
                foreach (float side in new[] { 1f, -1f })
                {
                    var c = m.p + m.left * (side * (hw - 0.25f) / hw);
                    mb.Box(V3(c, m.h + 0.55f), new Vector3(0.8f, parapetH + 0.6f, 0.7f), Yaw(Heading(d)), ConcreteMid);
                }
            }

            int spans = Mathf.Max(1, Mathf.RoundToInt(total / 32f));
            float spacing = total / spans;
            for (int k = 0; k < spans; k++)
            {
                var m = SampleAt(secs, (k + 0.5f) * spacing, out var d);
                var lat = LeftOf(d);
                float jointY = m.h + 0.05f;
                var across = lat * (hw - parapetW);
                mb.Quad(V3(m.p - across - d * 0.15f, jointY), V3(m.p + across - d * 0.15f, jointY), V3(m.p + across + d * 0.15f, jointY), V3(m.p - across + d * 0.15f, jointY), ConcreteDark, Vector3.up);
                if (m.h <= 3f) continue;

                float capV = Mathf.Min(1.6f, m.h - thick - 0.5f);
                float capTop = m.h - thick + 0.05f;
                float capHalf = hw - 1.6f;
                bool twin = width >= 18f;
                float colX = twin ? hw * 0.42f : 0f;
                const float colR = 1.05f;
                float bottomHalf = Mathf.Min(twin ? colX + colR + 0.5f : colR + 1.3f, capHalf - 0.5f);
                Prism(mb, m.p, capTop, lat, d, 2.8f, new[]
                {
                    new Vector2(-bottomHalf, -capV), new Vector2(bottomHalf, -capV), new Vector2(capHalf, -capV * 0.45f),
                    new Vector2(capHalf, 0f), new Vector2(-capHalf, 0f), new Vector2(-capHalf, -capV * 0.45f),
                }, Sidewalk);
                for (int c = 0; c < (twin ? 2 : 1); c++)
                {
                    var cp = m.p + lat * (twin ? (c == 0 ? -colX : colX) : 0f);
                    Frustum(mb, cp, 0f, capTop - capV + 0.1f, colR + 0.1f, colR, 8, ConcreteMid, ConcreteMid, false);
                }
            }
        }

        // ------------------------------------------------------------------ smokestack

        // Tapered 12-sided chimney with concrete lifts, brick bands near the top and an ink rim.
        public static void Smokestack(MeshBuilder mb, Vector2 pos, float height, float baseRadius, float topRadius)
        {
            float R(float y) => Mathf.Lerp(baseRadius, topRadius, y / height);
            float plinth = baseRadius * 2.4f;
            mb.Box(V3(pos, 0.25f), new Vector3(plinth * 1.2f, 0.5f, plinth * 1.2f), Quaternion.identity, ConcreteMid);
            mb.Box(V3(pos, 1.25f), new Vector3(plinth, 1.5f, plinth), Quaternion.identity, Shade);

            float rimY = height - 1.6f;
            var cuts = new List<(float y, Color c)>();
            float lift = height * 0.72f / 7f;
            for (int i = 0; i < 7; i++) cuts.Add((i * lift, i % 2 == 0 ? Cream : Sidewalk));
            float[] bandStart = { 0.72f, 0.80f, 0.88f };
            for (int i = 0; i < bandStart.Length; i++)
            {
                cuts.Add((bandStart[i] * height, Brick));
                cuts.Add(((bandStart[i] + 0.04f) * height, i % 2 == 0 ? Cream : Sidewalk));
            }
            for (int i = 0; i < cuts.Count; i++)
            {
                float y0 = cuts[i].y;
                float y1 = Mathf.Min(i + 1 < cuts.Count ? cuts[i + 1].y : rimY, rimY);
                if (y1 <= y0) continue;
                Frustum(mb, pos, y0, y1, R(y0), R(y1), 12, cuts[i].c, cuts[i].c, false);
            }

            float rimOuter = topRadius + 0.5f;
            float rimInner = topRadius - 0.3f;
            Frustum(mb, pos, rimY, height, R(rimY) + 0.05f, rimOuter, 12, Ink, Ink, false);
            float phase = Mathf.PI / 12f;
            for (int i = 0; i < 12; i++)
            {
                float a0 = phase + i * Mathf.PI * 2f / 12f;
                float a1 = phase + (i + 1) * Mathf.PI * 2f / 12f;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                mb.Quad(V3(pos + d0 * rimInner, height), V3(pos + d1 * rimInner, height), V3(pos + d1 * rimOuter, height), V3(pos + d0 * rimOuter, height), Ink, Vector3.up);
                mb.Quad(V3(pos + d0 * rimInner, height - 2f), V3(pos + d1 * rimInner, height - 2f), V3(pos + d1 * rimInner, height), V3(pos + d0 * rimInner, height), Shadow, -Dir3((d0 + d1).normalized));
                mb.Triangle(V3(pos, height - 2f), V3(pos + d0 * rimInner, height - 2f), V3(pos + d1 * rimInner, height - 2f), Ink, Vector3.up);
            }
        }

        // ------------------------------------------------------------------ crane gantry

        public static float GantryTop(float legHeight) => legHeight + 0.6f;

        // Four-legged portal of a whirley crane. Ground is y = 0 and the centre of the top deck stays clear.
        public static void CraneGantry(MeshBuilder mb, Vector2 pos, float headingDeg, float span, float legHeight)
        {
            const float bogieH = 1f;
            const float legW = 1.1f;
            float rad = headingDeg * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            var right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));
            var rot = Yaw(headingDeg);
            float half = span * 0.5f;
            var corners = new[]
            {
                pos - right * half - fwd * half, pos + right * half - fwd * half,
                pos + right * half + fwd * half, pos - right * half + fwd * half,
            };

            foreach (var c in corners)
            {
                mb.Box(V3(c, 0.2f), new Vector3(2.8f, 0.4f, 3.6f), rot, Ink);
                mb.Box(V3(c, 0.7f), new Vector3(2.2f, 0.6f, 2.4f), rot, Ink);
                mb.Box(V3(c, (bogieH + legHeight) * 0.5f), new Vector3(legW, legHeight - bogieH, legW), rot, CraneYellow);
            }

            float yLo = bogieH + 0.5f;
            float yHi = legHeight - 1.2f;
            int panels = Mathf.Max(1, Mathf.RoundToInt((yHi - yLo) / (span * 0.85f)));
            for (int k = 0; k < 4; k++)
            {
                var a = corners[k];
                var b = corners[(k + 1) % 4];
                for (int p = 0; p <= panels; p++)
                {
                    float y = Mathf.Lerp(yLo, yHi, p / (float)panels);
                    Beam(mb, V3(a, y), V3(b, y), 0.5f, CraneYellow);
                    if (p == panels) continue;
                    float y1 = Mathf.Lerp(yLo, yHi, (p + 1) / (float)panels);
                    Beam(mb, V3(a, y), V3(b, y1), 0.3f, CraneYellow, false);
                    Beam(mb, V3(b, y), V3(a, y1), 0.3f, CraneYellow, false);
                }
                Beam(mb, V3(a, legHeight - 0.5f), V3(b, legHeight - 0.5f), 1f, CraneYellow);
            }
            Beam(mb, V3(corners[0], legHeight - 0.8f), V3(corners[2], legHeight - 0.8f), 0.6f, CraneYellow, false);
            Beam(mb, V3(corners[1], legHeight - 0.8f), V3(corners[3], legHeight - 0.8f), 0.6f, CraneYellow, false);

            mb.Box(V3(pos, legHeight + 0.3f), new Vector3(span + 1.4f, 0.6f, span + 1.4f), rot, CraneYellow);

            float edge = half + 0.5f;
            var ring = new[] { new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(-1, 1), new Vector2(-1, 0) };
            var posts = new Vector2[ring.Length];
            for (int i = 0; i < ring.Length; i++) posts[i] = pos + right * (ring[i].x * edge) + fwd * (ring[i].y * edge);
            float deck = GantryTop(legHeight);
            for (int i = 0; i < posts.Length; i++)
            {
                var next = posts[(i + 1) % posts.Length];
                Beam(mb, V3(posts[i], deck), V3(posts[i], deck + 1.1f), 0.16f, CraneYellow);
                Beam(mb, V3(posts[i], deck + 1.1f), V3(next, deck + 1.1f), 0.12f, CraneYellow, false);
                Beam(mb, V3(posts[i], deck + 0.55f), V3(next, deck + 0.55f), 0.1f, CraneYellow, false);
            }
        }

        // ------------------------------------------------------------------ drydock

        static Vector2[] Offset(Vector2[] poly, float d)
        {
            int n = poly.Length;
            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var cur = poly[i];
                var dPrev = (cur - poly[(i + n - 1) % n]).normalized;
                var dNext = (poly[(i + 1) % n] - cur).normalized;
                var nPrev = new Vector2(dPrev.y, -dPrev.x);
                var nNext = new Vector2(dNext.y, -dNext.x);
                var miter = (nPrev + nNext).normalized;
                if (miter.sqrMagnitude < 1e-4f) miter = nNext;
                result[i] = cur + miter * (d / Mathf.Max(Vector2.Dot(miter, nNext), 0.5f));
            }
            return result;
        }

        // Pier 70 style basin. The outline (closed polygon) is the water-side edge of the walls; the 3 m
        // thick walls grow outward from it. openEdge, when >= 0, is the index i of the outline edge
        // i -> i+1 left open to the bay; the default closes the hull.
        public static void Drydock(MeshBuilder mb, Vector2[] outline, float rimHeight, float depth, int openEdge = -1)
        {
            if (outline == null || outline.Length < 3) return;
            const float wallThickness = 3f;
            var inner = (Vector2[])outline.Clone();
            int n = inner.Length;
            if (Geometry.SignedArea(inner) < 0f)
            {
                System.Array.Reverse(inner);
                if (openEdge >= 0) openEdge = ((n - 2 - openEdge) % n + n) % n;
            }
            var outer = Offset(inner, wallThickness);
            var coping = Offset(inner, 0.4f);

            mb.Polygon(inner, -depth + 1f, Shade);

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var d = (inner[j] - inner[i]).normalized;
                var outward = new Vector2(d.y, -d.x);
                if (i == openEdge)
                {
                    mb.Quad(V3(inner[i], -depth), V3(outer[i], -depth), V3(outer[i], rimHeight), V3(inner[i], rimHeight), ConcreteMid, Dir3(d));
                    mb.Quad(V3(inner[j], -depth), V3(outer[j], -depth), V3(outer[j], rimHeight), V3(inner[j], rimHeight), ConcreteMid, -Dir3(d));
                    continue;
                }
                mb.Quad(V3(inner[i], rimHeight), V3(inner[j], rimHeight), V3(outer[j], rimHeight), V3(outer[i], rimHeight), Sidewalk, Vector3.up);
                mb.Quad(V3(inner[i], rimHeight + 0.03f), V3(inner[j], rimHeight + 0.03f), V3(coping[j], rimHeight + 0.03f), V3(coping[i], rimHeight + 0.03f), Cream, Vector3.up);
                mb.Quad(V3(inner[i], -depth), V3(inner[j], -depth), V3(inner[j], rimHeight), V3(inner[i], rimHeight), ConcreteMid, -Dir3(outward));
                mb.Quad(V3(outer[i], -depth), V3(outer[j], -depth), V3(outer[j], rimHeight), V3(outer[i], rimHeight), Shade, Dir3(outward));

                float len = (inner[j] - inner[i]).magnitude;
                int count = len < 6f ? 0 : Mathf.RoundToInt(len / 10f);
                for (int k = 0; k < count; k++)
                {
                    var c = inner[i] + d * (len * (k + 0.5f) / count) + outward * 0.9f;
                    mb.Box(V3(c, rimHeight + 0.35f), new Vector3(0.5f, 0.7f, 0.5f), Yaw(Heading(d)), Ink);
                }
            }
        }

        // ------------------------------------------------------------------ platform

        // Raised platform on a polyline: top at height, with sloped ramps down to y = 0 at both ends.
        public static void Platform(MeshBuilder mb, Vector2[] centerline, float width, float height)
        {
            if (centerline == null || centerline.Length < 2) return;
            float hw = width * 0.5f;
            float ramp = Mathf.Clamp(height * 10f, 2f, 12f);
            var pts = new List<Vector2>(centerline);
            var d0 = (centerline[1] - centerline[0]).normalized;
            var d1 = (centerline[centerline.Length - 1] - centerline[centerline.Length - 2]).normalized;
            pts.Insert(0, centerline[0] - d0 * ramp);
            pts.Add(centerline[centerline.Length - 1] + d1 * ramp);
            var heights = new float[pts.Count];
            for (int i = 1; i < heights.Length - 1; i++) heights[i] = height;

            var secs = BuildSections(pts.ToArray(), heights, hw, 1e6f);
            if (secs.Count < 4) return;
            float stripF = 1f - 0.6f / hw;
            float levelStart = secs[1].s;
            float levelEnd = secs[secs.Count - 2].s;

            for (int i = 0; i + 1 < secs.Count; i++)
            {
                var a = secs[i];
                var b = secs[i + 1];
                var dir = SegDir(secs, i);
                var left = Dir3(LeftOf(dir));
                mb.Quad(Pt(a, -1f, 0f), Pt(b, -1f, 0f), Pt(b, 1f, 0f), Pt(a, 1f, 0f), Sidewalk, Vector3.up);
                foreach (float side in new[] { 1f, -1f })
                    mb.Quad(Pt(a, side, -a.h - 0.05f), Pt(b, side, -b.h - 0.05f), Pt(b, side, 0f), Pt(a, side, 0f), Shade, left * side);
                if (i < 1 || i > secs.Count - 3) continue;
                foreach (float side in new[] { 1f, -1f })
                    mb.Quad(Pt(a, side * stripF, 0.04f), Pt(b, side * stripF, 0.04f), Pt(b, side, 0.04f), Pt(a, side, 0.04f), Sunshine, Vector3.up);
                float len = b.s - a.s;
                for (float s = Mathf.Ceil(a.s / 6f) * 6f; s < b.s; s += 6f)
                {
                    if (s <= levelStart + 0.5f || s >= levelEnd - 0.5f) continue;
                    var m = Mix(a, b, (s - a.s) / len);
                    var along = dir * 0.06f;
                    var l = m.left * stripF;
                    mb.Quad(V3(m.p - l - along, m.h + 0.025f), V3(m.p + l - along, m.h + 0.025f), V3(m.p + l + along, m.h + 0.025f), V3(m.p - l + along, m.h + 0.025f), ConcreteMid, Vector3.up);
                }
            }
        }

        // ------------------------------------------------------------------ billboard

        const string FontRows =
            "A=01110/10001/10001/11111/10001/10001/10001;B=11110/10001/10001/11110/10001/10001/11110;" +
            "C=01110/10001/10000/10000/10000/10001/01110;D=11110/10001/10001/10001/10001/10001/11110;" +
            "E=11111/10000/10000/11110/10000/10000/11111;F=11111/10000/10000/11110/10000/10000/10000;" +
            "G=01110/10001/10000/10111/10001/10001/01111;H=10001/10001/10001/11111/10001/10001/10001;" +
            "I=01110/00100/00100/00100/00100/00100/01110;J=00111/00010/00010/00010/00010/10010/01100;" +
            "K=10001/10010/10100/11000/10100/10010/10001;L=10000/10000/10000/10000/10000/10000/11111;" +
            "M=10001/11011/10101/10101/10001/10001/10001;N=10001/11001/10101/10011/10001/10001/10001;" +
            "O=01110/10001/10001/10001/10001/10001/01110;P=11110/10001/10001/11110/10000/10000/10000;" +
            "Q=01110/10001/10001/10001/10101/10010/01101;R=11110/10001/10001/11110/10100/10010/10001;" +
            "S=01111/10000/10000/01110/00001/00001/11110;T=11111/00100/00100/00100/00100/00100/00100;" +
            "U=10001/10001/10001/10001/10001/10001/01110;V=10001/10001/10001/10001/10001/01010/00100;" +
            "W=10001/10001/10001/10101/10101/11011/10001;X=10001/10001/01010/00100/01010/10001/10001;" +
            "Y=10001/10001/01010/00100/00100/00100/00100;Z=11111/00001/00010/00100/01000/10000/11111;" +
            "0=01110/10001/10011/10101/11001/10001/01110;1=00100/01100/00100/00100/00100/00100/01110;" +
            "2=01110/10001/00001/00010/00100/01000/11111;3=11110/00001/00001/01110/00001/00001/11110;" +
            "4=00010/00110/01010/10010/11111/00010/00010;5=11111/10000/11110/00001/00001/10001/01110;" +
            "6=00110/01000/10000/11110/10001/10001/01110;7=11111/00001/00010/00100/01000/01000/01000;" +
            "8=01110/10001/10001/01110/10001/10001/01110;9=01110/10001/10001/01111/00001/00010/01100;" +
            ".=00000/00000/00000/00000/00000/01100/01100;,=00000/00000/00000/00000/01100/00100/01000;" +
            "!=00100/00100/00100/00100/00100/00000/00100;?=01110/10001/00001/00010/00100/00000/00100;" +
            "'=00100/00100/01000/00000/00000/00000/00000;-=00000/00000/00000/11111/00000/00000/00000;" +
            ":=00000/01100/01100/00000/01100/01100/00000;&=01100/10010/10100/01000/10101/10010/01101;" +
            "/=00001/00001/00010/00100/01000/10000/10000;+=00000/00100/00100/11111/00100/00100/00000;" +
            "%=11001/11010/00010/00100/01000/01011/10011;";

        static Dictionary<char, string[]> font;

        static Dictionary<char, string[]> Font()
        {
            if (font != null) return font;
            font = new Dictionary<char, string[]>();
            foreach (var entry in FontRows.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
                font[entry[0]] = entry.Substring(2).Split('/');
            return font;
        }

        // Splits words over up to three lines so the text can use the largest pixel size that fits.
        static string[] FitLines(string text, float usableW, float usableH, out float pixel)
        {
            var words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 16) System.Array.Resize(ref words, 16);
            pixel = 0f;
            if (words.Length == 0) return null;
            string[] best = null;
            for (int mask = 0; mask < (1 << (words.Length - 1)); mask++)
            {
                var lines = new List<string>();
                var line = words[0];
                for (int w = 1; w < words.Length; w++)
                {
                    if ((mask & (1 << (w - 1))) != 0)
                    {
                        lines.Add(line);
                        line = words[w];
                    }
                    else line += " " + words[w];
                }
                lines.Add(line);
                if (lines.Count > 3) continue;
                int widest = 0;
                foreach (var l in lines) widest = Mathf.Max(widest, l.Length * 6 - 1);
                float p = Mathf.Min(usableW / widest, usableH / (7 * lines.Count + 3 * (lines.Count - 1)));
                bool tie = Mathf.Abs(p - pixel) <= 1e-4f && best != null && lines.Count < best.Length;
                if (p <= pixel + 1e-4f && !tie) continue;
                pixel = p;
                best = lines.ToArray();
            }
            return best;
        }

        // Flat advertising panel on one or two ink poles. poleHeight is the bare pole from baseCenter.y up
        // to the bottom edge of the frame; the panel sits above it. Text is baked as quads in front.
        public static void BillboardPanel(MeshBuilder mb, Vector3 baseCenter, float headingDeg, float width, float height, float poleHeight, string text, Color panel, Color ink, Color frame)
        {
            const float frameBorder = 0.4f;
            const float frameDepth = 0.5f;
            float rad = headingDeg * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var viewerRight = new Vector3(-Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            var rot = Yaw(headingDeg);
            var center = baseCenter + Vector3.up * (poleHeight + height * 0.5f);

            mb.Box(center, new Vector3(width + frameBorder * 2f, height + frameBorder * 2f, frameDepth), rot, frame);

            var panelC = center + fwd * (frameDepth * 0.5f + 0.01f);
            Vector3 Q(float u, float v, float forward) => panelC + viewerRight * u + Vector3.up * v + fwd * forward;
            mb.Quad(Q(-width * 0.5f, -height * 0.5f, 0f), Q(width * 0.5f, -height * 0.5f, 0f), Q(width * 0.5f, height * 0.5f, 0f), Q(-width * 0.5f, height * 0.5f, 0f), panel, fwd);

            float poleW = Mathf.Clamp(height * 0.1f, 0.3f, 0.8f);
            int poleCount = width >= 7f ? 2 : 1;
            float poleTop = poleHeight + height - 0.2f;
            for (int i = 0; i < poleCount; i++)
            {
                float u = poleCount == 1 ? 0f : (i == 0 ? -1f : 1f) * width * 0.28f;
                var pc = baseCenter + viewerRight * u - fwd * (frameDepth * 0.5f + poleW * 0.5f);
                mb.Box(pc + Vector3.up * (poleTop * 0.5f), new Vector3(poleW, poleTop, poleW), rot, Ink);
                mb.Box(pc + Vector3.up * 0.2f, new Vector3(poleW + 0.8f, 0.4f, poleW + 0.8f), rot, Shade);
            }

            for (int i = -1; i <= 1; i++)
            {
                var ac = center + Vector3.up * (height * 0.5f + frameBorder + 0.12f) + viewerRight * (i * width * 0.33f) + fwd * (frameDepth * 0.5f + 0.55f);
                mb.Box(ac, new Vector3(0.25f, 0.25f, 1.3f), rot, Ink);
                mb.Box(ac + fwd * 0.55f + Vector3.up * 0.1f, new Vector3(0.5f, 0.2f, 0.4f), rot, Lemon);
            }

            if (string.IsNullOrEmpty(text)) return;
            float margin = Mathf.Max(0.4f, Mathf.Min(width, height) * 0.08f);
            var lines = FitLines(text.ToUpperInvariant(), width - margin * 2f, height - margin * 2f, out float pixel);
            if (lines == null) return;
            var glyphs = Font();
            float blockH = (7 * lines.Length + 3 * (lines.Length - 1)) * pixel;
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                float lineW = (line.Length * 6 - 1) * pixel;
                float top = blockH * 0.5f - l * 10f * pixel;
                for (int row = 0; row < 7; row++)
                {
                    int runStart = -1;
                    for (int col = 0; col <= line.Length * 6; col++)
                    {
                        bool lit = false;
                        if (col < line.Length * 6 - 1 && col % 6 < 5 && line[col / 6] != ' ')
                        {
                            if (!glyphs.TryGetValue(line[col / 6], out var rows)) rows = glyphs['?'];
                            lit = rows[row][col % 6] == '1';
                        }
                        if (lit && runStart < 0) runStart = col;
                        if (lit || runStart < 0) continue;
                        float u0 = -lineW * 0.5f + runStart * pixel;
                        float u1 = -lineW * 0.5f + col * pixel;
                        float v1 = top - row * pixel;
                        float v0 = v1 - pixel;
                        mb.Quad(Q(u0, v0, 0.05f), Q(u1, v0, 0.05f), Q(u1, v1, 0.05f), Q(u0, v1, 0.05f), ink, fwd);
                        runStart = -1;
                    }
                }
            }
        }

        public static readonly string[] BillboardLines =
        {
            "YOUR CODE, BUT IT APOLOGIZES",
            "AGENTS THAT ATTEND YOUR STANDUP",
            "STUCK IN TRAFFIC? SO IS YOUR BACKLOG.",
            "WE ADDED AI TO YOUR TOASTER. SORRY.",
            "SHIP FASTER. BREAK THINGS. BLAME US.",
            "CHATBOTS FOR YOUR OTHER CHATBOTS",
            "VIBES-BASED INFRASTRUCTURE",
            "MY AGENT MET YOUR AGENT. WE'RE FINE.",
            "PROMPT. PRAY. DEPLOY.",
            "YOUR ROADMAP, HALLUCINATED DAILY",
            "SYNERGY AS A SERVICE",
            "NOW WITH 40% MORE CONFIDENCE",
            "FINE-TUNED ON YOUR GROUP CHAT",
            "REPLACE YOUR MEETINGS WITH MEETINGS",
            "SEED ROUND SEEKS SERIES NAP",
            "GPUS WANTED. WILL TRADE KIDNEY.",
            "WE PIVOTED WHILE YOU READ THIS",
            "SOURDOUGH, BUT MAKE IT AGENTIC",
            "AUTONOMOUS CARS. MANUAL TRAFFIC.",
            "THE MODEL IS 97% SURE. GOOD LUCK!",
            "ASK YOUR DOCTOR ABOUT AGENTS",
            "10X ENGINEER, NOW IN A TAB",
        };
    }
}
