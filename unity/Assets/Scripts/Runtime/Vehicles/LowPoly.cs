using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Vehicles
{
    // Flat-shaded, vertex-coloured mesh builder for the vehicles Polyfork doesn't model. Every face
    // has its own vertices so the shared shader shades it as one facet; colours are sRGB hex in,
    // linear out, matching the GLB importer. Alpha below 1 marks a lamp that glows at night
    // (CROS-16's convention: alpha = 1 - glow). Faces are emitted with both windings, so hand-built
    // shapes never vanish to back-face culling; the shader already flips each facet toward the camera.
    public sealed class LowPoly
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public static Color Hex(string hex, float glow = 0f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c = c.linear;
            c.a = 1f - glow;
            return c;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
            triangles.Add(i);
            triangles.Add(i + 2);
            triangles.Add(i + 1);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Tri(a, b, c, color);
            Tri(a, c, d, color);
        }

        public void Box(Vector3 center, Vector3 size, Color color) => Box(center, size, Quaternion.identity, color);

        public void Box(Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            var c = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
            Quad(c[0], c[3], c[2], c[1], color);
            Quad(c[5], c[6], c[7], c[4], color);
            Quad(c[4], c[7], c[3], c[0], color);
            Quad(c[1], c[2], c[6], c[5], color);
            Quad(c[3], c[7], c[6], c[2], color);
            Quad(c[4], c[0], c[1], c[5], color);
        }

        // A side profile (z forward, y up) extruded across x from -halfWidth to +halfWidth. Faces
        // pointing up take the top colour.
        public void Prism(Vector2[] profile, float halfWidth, Color side, Color top)
        {
            int n = profile.Length;
            float area = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) area += profile[j].x * profile[i].y - profile[i].x * profile[j].y;
            for (int i = 0; i < n; i++)
            {
                var a = profile[i];
                var b = profile[(i + 1) % n];
                var edge = b - a;
                var outward = (area > 0f ? new Vector2(edge.y, -edge.x) : new Vector2(-edge.y, edge.x)).normalized;
                var col = outward.y > 0.3f ? top : side;
                Quad(new Vector3(-halfWidth, a.y, a.x), new Vector3(-halfWidth, b.y, b.x), new Vector3(halfWidth, b.y, b.x), new Vector3(halfWidth, a.y, a.x), col);
            }
            var tris = Triangulate(profile);
            for (int t = 0; t < tris.Count; t += 3)
            {
                var a = profile[tris[t]];
                var b = profile[tris[t + 1]];
                var c = profile[tris[t + 2]];
                Tri(new Vector3(halfWidth, a.y, a.x), new Vector3(halfWidth, b.y, b.x), new Vector3(halfWidth, c.y, c.x), side);
                Tri(new Vector3(-halfWidth, a.y, a.x), new Vector3(-halfWidth, c.y, c.x), new Vector3(-halfWidth, b.y, b.x), side);
            }
        }

        // Cylinder along the local x axis (wheels), sides faceted.
        public void CylinderX(Vector3 center, float radius, float width, int sides, Color tread, Color face)
        {
            float hw = width * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(0f, Mathf.Sin(a0) * radius, Mathf.Cos(a0) * radius);
                var p1 = new Vector3(0f, Mathf.Sin(a1) * radius, Mathf.Cos(a1) * radius);
                var l = Vector3.left * hw;
                var r = Vector3.right * hw;
                Quad(center + l + p0, center + l + p1, center + r + p1, center + r + p0, tread);
                Tri(center + r, center + r + p0, center + r + p1, face);
                Tri(center + l, center + l + p1, center + l + p0, face);
            }
        }

        // Vertical cylinder (domes, posts) with an optional cap.
        public void CylinderY(Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int sides, Color side, Color cap)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var top = baseCenter + Vector3.up * height;
                Quad(baseCenter + d0 * radiusBottom, top + d0 * radiusTop, top + d1 * radiusTop, baseCenter + d1 * radiusBottom, side);
                if (radiusTop > 0.001f) Tri(top, top + d1 * radiusTop, top + d0 * radiusTop, cap);
                Tri(baseCenter, baseCenter + d0 * radiusBottom, baseCenter + d1 * radiusBottom, cap);
            }
        }

        public void Bar(Vector3 a, Vector3 b, float thickness, Color color)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Box((a + b) * 0.5f, new Vector3(thickness, thickness, d.magnitude + thickness), Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up), color);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            var up = new Vector3[vertices.Count];
            for (int i = 0; i < up.Length; i++) up[i] = Vector3.up;
            mesh.normals = up;
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<int> Triangulate(Vector2[] poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            float area = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) area += poly[j].x * poly[i].y - poly[i].x * poly[j].y;
            for (int i = 0; i < poly.Length; i++) idx.Add(area > 0f ? i : poly.Length - 1 - i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 500)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    var a = poly[idx[(i + idx.Count - 1) % idx.Count]];
                    var b = poly[idx[i]];
                    var c = poly[idx[(i + 1) % idx.Count]];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 0f) continue;
                    bool inside = false;
                    foreach (int k in idx)
                    {
                        var p = poly[k];
                        if (p == a || p == b || p == c) continue;
                        if (InTriangle(p, a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(idx[(i + idx.Count - 1) % idx.Count]);
                    result.Add(idx[i]);
                    result.Add(idx[(i + 1) % idx.Count]);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) result.AddRange(idx);
            return result;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
