using UnityEngine;

namespace Dogpatch.Audio
{
    public enum Ground { Concrete, Asphalt, Grass, Wood, Metal, Water }

    // What's underfoot, rasterized once from the generated street and park meshes on a 2 m grid:
    // blue-grey street triangles are asphalt, warm ones sidewalk, green ones grass.
    public static class SurfaceMap
    {
        const float Cell = 2f;
        static byte[] grid;
        static int width, height;
        static Vector2 origin;

        public static Ground At(Vector3 p)
        {
            if (grid == null && !Build()) return Ground.Concrete;
            int x = Mathf.FloorToInt((p.x - origin.x) / Cell), z = Mathf.FloorToInt((p.z - origin.y) / Cell);
            if (x < 0 || z < 0 || x >= width || z >= height) return Ground.Concrete;
            return (Ground)grid[z * width + x];
        }

        static bool Build()
        {
            var root = GameObject.Find("Dogpatch");
            if (root == null) return false;
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var bounds = new Bounds();
            bool any = false;
            foreach (var f in filters)
            {
                if (!Wanted(f.name) || f.sharedMesh == null) continue;
                var b = f.GetComponent<Renderer>().bounds;
                if (!any) bounds = b;
                else bounds.Encapsulate(b);
                any = true;
            }
            if (!any) return false;
            origin = new Vector2(bounds.min.x, bounds.min.z);
            width = Mathf.CeilToInt(bounds.size.x / Cell) + 1;
            height = Mathf.CeilToInt(bounds.size.z / Cell) + 1;
            grid = new byte[width * height];
            foreach (var f in filters)
            {
                if (Wanted(f.name) && f.sharedMesh != null && f.sharedMesh.isReadable) Rasterize(f);
            }
            return true;
        }

        static bool Wanted(string name) => name.StartsWith("Streets") || name == "Parks";

        static void Rasterize(MeshFilter f)
        {
            var mesh = f.sharedMesh;
            var verts = mesh.vertices;
            var colors = mesh.colors;
            var tris = mesh.triangles;
            var m = f.transform.localToWorldMatrix;
            bool parks = f.name == "Parks";
            for (int i = 0; i < tris.Length; i += 3)
            {
                Ground g;
                if (parks) g = Ground.Grass;
                else if (colors.Length == 0) g = Ground.Asphalt;
                else
                {
                    var c = colors[tris[i]];
                    g = c.g > c.r + 0.08f && c.g > c.b ? Ground.Grass : c.b > c.r + 0.01f ? Ground.Asphalt : Ground.Concrete;
                }
                Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]), b = m.MultiplyPoint3x4(verts[tris[i + 1]]), d = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                Fill(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(d.x, d.z), (byte)g);
            }
        }

        static void Fill(Vector2 a, Vector2 b, Vector2 c, byte value)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - origin.x) / Cell));
            int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - origin.x) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - origin.y) / Cell));
            int z1 = Mathf.Min(height - 1, Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - origin.y) / Cell));
            float area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 1e-4f) return;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(origin.x + (x + 0.5f) * Cell, origin.y + (z + 0.5f) * Cell);
                    float w0 = Cross(b - p, c - p) / area, w1 = Cross(c - p, a - p) / area;
                    if (w0 < -0.01f || w1 < -0.01f || w0 + w1 > 1.01f) continue;
                    grid[z * width + x] = value;
                }
            }
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
    }
}
