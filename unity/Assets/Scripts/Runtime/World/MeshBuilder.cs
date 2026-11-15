using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.World
{
    // Accumulates flat-shaded, vertex-coloured triangles. Every face gets its own vertices so
    // colours never bleed across faces. Triangles are wound from the normal they should face,
    // so callers never think about Unity's clockwise front faces.
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public int VertexCount => vertices.Count;
        public int IndexCount => triangles.Count;

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 facing)
        {
            Triangle(a, b, c, color, facing);
            Triangle(a, c, d, color, facing);
        }

        public void Polygon(Vector2[] poly, float y, Color color)
        {
            var tris = Geometry.Triangulate(poly);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                Triangle(At(poly[tris[i]], y), At(poly[tris[i + 1]], y), At(poly[tris[i + 2]], y), color, Vector3.up);
        }

        public void Disc(Vector2 center, float radius, float y, Color color, int segments = 10)
        {
            var c = At(center, y);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Triangle(c, c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius, color, Vector3.up);
            }
        }

        // Flat ribbon along a polyline, mitred at the joints, with round caps when asked.
        public void Ribbon(Vector2[] pts, float halfWidth, float y, Color color, bool caps)
        {
            if (pts.Length < 2) return;
            var left = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var dPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                var dNext = i < pts.Length - 1 ? (pts[i + 1] - pts[i]).normalized : dPrev;
                var nPrev = new Vector2(-dPrev.y, dPrev.x);
                var nNext = new Vector2(-dNext.y, dNext.x);
                var miter = (nPrev + nNext).normalized;
                if (miter.sqrMagnitude < 1e-4f) miter = nNext;
                float len = halfWidth / Mathf.Max(Vector2.Dot(miter, nNext), 0.5f);
                left[i] = miter * len;
            }
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Quad(At(pts[i] + left[i], y), At(pts[i + 1] + left[i + 1], y), At(pts[i + 1] - left[i + 1], y), At(pts[i] - left[i], y), color, Vector3.up);
            }
            if (!caps) return;
            Disc(pts[0], halfWidth, y, color);
            Disc(pts[pts.Length - 1], halfWidth, y, color);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            var faces = new[]
            {
                (Vector3.up, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1)),
                (Vector3.forward, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)),
                (Vector3.back, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1)),
                (Vector3.right, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1)),
                (Vector3.left, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)),
            };
            foreach (var (n, a, b, c, d) in faces) Quad(a, b, c, d, color, rotation * n);
        }

        public static Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // One MeshBuilder per square chunk of the map so the GPU can frustum-cull the city.
    public sealed class ChunkedMeshBuilder
    {
        readonly float chunkSize;
        readonly Dictionary<Vector2Int, MeshBuilder> chunks = new Dictionary<Vector2Int, MeshBuilder>();

        public ChunkedMeshBuilder(float chunkSize) => this.chunkSize = chunkSize;

        public MeshBuilder At(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.y / chunkSize));
            if (!chunks.TryGetValue(key, out var builder)) chunks[key] = builder = new MeshBuilder();
            return builder;
        }

        public IEnumerable<(Vector2Int key, MeshBuilder builder)> All()
        {
            foreach (var pair in chunks) yield return (pair.Key, pair.Value);
        }
    }
}
