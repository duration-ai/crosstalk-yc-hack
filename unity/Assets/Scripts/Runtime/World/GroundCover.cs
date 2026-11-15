using System;
using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    // Ground cover for the open land between streets and buildings, from OSM (World/ground.json):
    // concrete industrial yards, rail ballast, dirt lots, surface parking with painted stalls, grass,
    // pitches, plazas, piers, sand and scrub. Everything stays flat vertex colour; the detail is
    // geometry: each surface is cut into cells (slabs, planks, turf rows) whose colours vary a little,
    // so a ten-hectare yard reads as concrete rather than one blank plane. The base land gets the same
    // treatment, faintly. Surfaces are flat at ground level and stack with polygon offset (land, then
    // yards, then lots and grass, then parks, then pitches, then paint), so they never z-fight. Roads
    // mask them through the ground stencil.
    public static class GroundCover
    {
        [Serializable]
        sealed class GroundPolygon
        {
            public string kind;
            public float[] pts;
        }

        [Serializable]
        sealed class GroundData
        {
            public GroundPolygon[] polygons;
        }

        struct Style
        {
            public Color A, B;
            public float Cell;
            public int Layer;
            public bool Rows;
        }

        static Style S(string a, string b, float cell, int layer, bool rows = false) =>
            new Style { A = SpeedDial.Hex(a).linear, B = SpeedDial.Hex(b).linear, Cell = cell, Layer = layer, Rows = rows };

        static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            ["land"] = S("#E6DDCB", "#DBD0BB", 12f, 0),
            ["industrial"] = S("#D0CDC6", "#BDB9B1", 10f, 1),
            ["railway"] = S("#AEA397", "#998D81", 5f, 1),
            ["dirt"] = S("#DDB98A", "#CBA371", 8f, 1),
            ["parking"] = S("#9EA3AC", "#8E939D", 10f, 2),
            ["plaza"] = S("#EEE7DA", "#E1D9CA", 4f, 2),
            ["pier"] = S("#CEC7BB", "#BBB3A6", 6f, 2),
            ["grass"] = S("#7FD493", "#6CC683", 6f, 2),
            ["pitch"] = S("#3FBA69", "#35AA5D", 4f, 4, true),
            ["sand"] = S("#F3E0AF", "#E8D29B", 3f, 4),
            ["scrub"] = S("#8DC46F", "#79B35F", 4f, 4),
        };

        public const int ParkLayer = 3;
        const int PaintLayer = 5;
        static readonly Color StallPaint = SpeedDial.Hex("#F7F8F9").linear;

        // A copy of the ground material that draws in front of lower layers at the same height.
        public static Material Layer(Material ground, int layer)
        {
            if (layer == 0) return ground;
            var m = new Material(ground) { name = "Ground layer " + layer };
            m.SetFloat("_OffsetFactor", -1f);
            m.SetFloat("_OffsetUnits", -2f * layer);
            return m;
        }

        public static void Build(Transform parent, Vector2[][] land, Material ground)
        {
            var layers = new SortedDictionary<int, MeshBuilder>();
            MeshBuilder At(int layer) => layers.TryGetValue(layer, out var mb) ? mb : layers[layer] = new MeshBuilder();

            foreach (var poly in land) Cells(At(0), poly, Styles["land"], 0x5eed);
            var asset = Resources.Load<TextAsset>("World/ground");
            int count = 0, stalls = 0;
            if (asset != null)
            {
                var data = JsonUtility.FromJson<GroundData>(asset.text);
                for (int i = 0; i < data.polygons.Length; i++)
                {
                    var g = data.polygons[i];
                    if (!Styles.TryGetValue(g.kind, out var style)) continue;
                    var poly = Geometry.ToPoints(g.pts);
                    if (poly.Length < 3) continue;
                    if (Geometry.SignedArea(poly) < 0f) Array.Reverse(poly);
                    Cells(At(style.Layer), poly, style, i * 7919 + 13);
                    if (g.kind == "parking") stalls += Stalls(At(PaintLayer), poly);
                    count++;
                }
            }
            foreach (var (layer, mb) in layers)
                WorldBuilder.Emit(parent, layer == 0 ? "Land" : "Ground cover " + layer, mb.ToMesh("ground " + layer), Layer(ground, layer), false);
            Debug.Log($"[GroundCover] {count} surfaces, {stalls} parking stall lines");
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }

        // Cuts the polygon into square cells and gives each its own shade between A and B. Cells
        // that no polygon edge touches are either wholly inside (one quad) or outside; the rest are
        // clipped to the polygon.
        static void Cells(MeshBuilder mb, Vector2[] poly, Style style, int seed)
        {
            var bounds = Geometry.BoundsOf(poly);
            float cell = style.Cell;
            int x0 = Mathf.FloorToInt(bounds.xMin / cell), x1 = Mathf.FloorToInt(bounds.xMax / cell);
            int y0 = Mathf.FloorToInt(bounds.yMin / cell), y1 = Mathf.FloorToInt(bounds.yMax / cell);
            var touched = new HashSet<Vector2Int>();
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j];
                var b = poly[i];
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / (cell * 0.5f)) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)steps);
                    int cx = Mathf.FloorToInt(p.x / cell), cy = Mathf.FloorToInt(p.y / cell);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            touched.Add(new Vector2Int(cx + dx, cy + dy));
                }
            }

            var piece = new List<Vector2>(16);
            var scratch = new List<Vector2>(16);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    var min = new Vector2(cx * cell, cy * cell);
                    var max = min + new Vector2(cell, cell);
                    float t = style.Rows ? ((cy & 1) == 0 ? 0.15f : 0.85f) : Hash(cx, cy, seed);
                    var color = Color.Lerp(style.A, style.B, t);
                    if (!touched.Contains(new Vector2Int(cx, cy)))
                    {
                        if (!Geometry.Contains(poly, (min + max) * 0.5f)) continue;
                        mb.Quad(MeshBuilder.At(min, 0f), MeshBuilder.At(new Vector2(min.x, max.y), 0f), MeshBuilder.At(max, 0f), MeshBuilder.At(new Vector2(max.x, min.y), 0f), color, Vector3.up);
                        continue;
                    }
                    piece.Clear();
                    piece.AddRange(poly);
                    Clip(piece, scratch, 0, min.x, 1f);
                    Clip(piece, scratch, 0, max.x, -1f);
                    Clip(piece, scratch, 1, min.y, 1f);
                    Clip(piece, scratch, 1, max.y, -1f);
                    if (piece.Count < 3) continue;
                    var pts = piece.ToArray();
                    if (Mathf.Abs(Geometry.SignedArea(pts)) < 0.05f) continue;
                    mb.Polygon(pts, 0f, color);
                }
        }

        // Sutherland-Hodgman against one axis-aligned half plane: keeps points where
        // sign * (p[axis] - at) >= 0.
        static void Clip(List<Vector2> poly, List<Vector2> scratch, int axis, float at, float sign)
        {
            scratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float da = sign * (a[axis] - at), db = sign * (b[axis] - at);
                if (da >= 0f) scratch.Add(a);
                if ((da >= 0f) != (db >= 0f)) scratch.Add(Vector2.Lerp(a, b, da / (da - db)));
            }
            poly.Clear();
            poly.AddRange(scratch);
        }

        // Painted stalls: back-to-back rows of 2.6 m bays along the lot's longest edge, 5 m deep,
        // with a 6.5 m aisle between pairs of rows. A line is kept only where it lies wholly inside
        // the lot with a metre to spare.
        static int Stalls(MeshBuilder mb, Vector2[] poly)
        {
            var axis = Vector2.right;
            float longest = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float len = Vector2.Distance(poly[i], poly[j]);
                if (len <= longest) continue;
                longest = len;
                axis = (poly[i] - poly[j]) / len;
            }
            var across = new Vector2(-axis.y, axis.x);
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var p in poly)
            {
                float u = Vector2.Dot(p, axis), v = Vector2.Dot(p, across);
                u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
            }
            bool Inside(Vector2 p)
            {
                if (!Geometry.Contains(poly, p)) return false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if (Vector2.Distance(p, Geometry.ClosestPointOnSegment(p, poly[j], poly[i], out _)) < 1f) return false;
                return true;
            }
            Vector2 W(float u, float v) => axis * u + across * v;
            const float Bay = 2.6f, Depth = 5f, Aisle = 6.5f, Half = 0.07f;
            int lines = 0;
            for (float v = v0 + 1.5f; v + Depth * 2f < v1 - 1f; v += Depth * 2f + Aisle)
                for (float u = u0 + 1.5f; u < u1 - 1f; u += Bay)
                {
                    var a = W(u, v);
                    var b = W(u, v + Depth * 2f);
                    if (!Inside(a) || !Inside(b) || !Inside((a + b) * 0.5f)) continue;
                    var side = axis * Half;
                    mb.Quad(MeshBuilder.At(a - side, 0f), MeshBuilder.At(b - side, 0f), MeshBuilder.At(b + side, 0f), MeshBuilder.At(a + side, 0f), StallPaint, Vector3.up);
                    lines++;
                }
            return lines;
        }
    }
}
