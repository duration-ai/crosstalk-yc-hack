using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Map
{
    // Dogpatch as flat vector meshes for the minimap and the full map: land, parks, paths, street
    // casings, streets and building blocks in the world palette, so the map stays sharp at any
    // zoom. The triangles are built off the main thread (Build is pure C# over the world data) and
    // bucketed into 200 m tiles, one submesh per layer, so a view only draws the tiles it can see.
    // Also collects where street names and landmark names go on the full map. Vertex colours are
    // raw palette values, drawn with the HUD's vertex-colour shader into a linear render target.
    public sealed class MapGeometry
    {
        const float Tile = 200f;
        const int Layers = 6;

        public struct Palette
        {
            public Color32 Land;
            public Color32 Park;
            public Color32 Path;
            public Color32 Casing;
            public Color32 Street;
            public Color32 Building;
        }

        public struct Label
        {
            public string Text;
            public Vector2 Position;
            public Vector2 Direction;
        }

        sealed class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color32> Colors = new List<Color32>();
            public readonly List<int>[] Layers = new List<int>[MapGeometry.Layers];
            public Rect Bounds;

            public Part()
            {
                for (int i = 0; i < Layers.Length; i++) Layers[i] = new List<int>();
            }
        }

        public readonly List<Label> Streets = new List<Label>();
        public readonly List<Label> Places = new List<Label>();
        public Rect Bounds;

        readonly Dictionary<Vector2Int, Part> tiles = new Dictionary<Vector2Int, Part>();
        readonly Part global = new Part();
        readonly List<(Mesh mesh, Rect bounds)> meshes = new List<(Mesh, Rect)>();

        public static MapGeometry Build(WorldData data, Palette palette)
        {
            var g = new MapGeometry { Bounds = Rect.MinMaxRect(data.minX, data.minZ, data.maxX, data.maxZ) };
            if (data.land != null) foreach (var poly in data.land) g.Fill(g.global, Geometry.ToPoints(poly.pts), 0, palette.Land);
            if (data.parks != null) foreach (var poly in data.parks) g.Fill(null, Geometry.ToPoints(poly.pts), 1, palette.Park);
            foreach (var road in data.roads)
            {
                var pts = Geometry.ToPoints(road.pts);
                if (road.kind == "path") g.Line(pts, 1.2f, 2, palette.Path);
                else
                {
                    float hw = Mathf.Max(road.width, 5f) * 0.5f;
                    g.Line(pts, hw + 1.1f, 3, palette.Casing);
                    g.Line(pts, hw, 4, palette.Street);
                }
            }
            foreach (var b in data.buildings) g.Fill(null, Geometry.ToPoints(b.pts), 5, palette.Building);
            g.CollectLabels(data);
            return g;
        }

        Part PartAt(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / Tile), Mathf.FloorToInt(p.y / Tile));
            if (!tiles.TryGetValue(key, out var part))
            {
                part = new Part { Bounds = new Rect(key.x * Tile, key.y * Tile, Tile, Tile) };
                tiles[key] = part;
            }
            return part;
        }

        static void Grow(Part part, Vector2 p)
        {
            if (part.Bounds.Contains(p)) return;
            part.Bounds = Rect.MinMaxRect(Mathf.Min(part.Bounds.xMin, p.x), Mathf.Min(part.Bounds.yMin, p.y), Mathf.Max(part.Bounds.xMax, p.x), Mathf.Max(part.Bounds.yMax, p.y));
        }

        int Vertex(Part part, Vector2 p, Color32 color)
        {
            Grow(part, p);
            part.Vertices.Add(new Vector3(p.x, p.y, 0f));
            part.Colors.Add(color);
            return part.Vertices.Count - 1;
        }

        void Fill(Part part, Vector2[] poly, int layer, Color32 color)
        {
            if (poly.Length < 3) return;
            if (part == null) part = PartAt(Geometry.Centroid(poly));
            var tris = Geometry.Triangulate(poly);
            int start = part.Vertices.Count;
            foreach (var p in poly) Vertex(part, p, color);
            foreach (int i in tris) part.Layers[layer].Add(start + i);
        }

        // Quads per segment, a round cap at each end and a round joint where the line bends.
        void Line(Vector2[] pts, float hw, int layer, Color32 color)
        {
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var d = b - a;
                if (d.sqrMagnitude < 1e-4f) continue;
                var part = PartAt((a + b) * 0.5f);
                var n = new Vector2(-d.y, d.x).normalized * hw;
                int v0 = Vertex(part, a + n, color), v1 = Vertex(part, b + n, color), v2 = Vertex(part, b - n, color), v3 = Vertex(part, a - n, color);
                var idx = part.Layers[layer];
                idx.Add(v0); idx.Add(v1); idx.Add(v2);
                idx.Add(v0); idx.Add(v2); idx.Add(v3);
                bool bend = i + 2 < pts.Length && Vector2.Angle(d, pts[i + 2] - b) > 4f;
                if (i == 0) Disc(part, a, hw, layer, color);
                if (bend || i + 2 == pts.Length) Disc(part, b, hw, layer, color);
            }
        }

        void Disc(Part part, Vector2 c, float r, int layer, Color32 color)
        {
            const int segments = 10;
            int center = Vertex(part, c, color);
            int first = part.Vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vertex(part, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, color);
            }
            var idx = part.Layers[layer];
            for (int i = 0; i < segments; i++)
            {
                idx.Add(center);
                idx.Add(first + i);
                idx.Add(first + (i + 1) % segments);
            }
        }

        // Street names every 320 m along the major streets, and the four landmarks.
        void CollectLabels(WorldData data)
        {
            foreach (var road in data.roads)
            {
                if (string.IsNullOrEmpty(road.name) || (road.kind != "primary" && road.kind != "secondary" && road.kind != "tertiary")) continue;
                var pts = Geometry.ToPoints(road.pts);
                float length = 0f;
                for (int i = 1; i < pts.Length; i++) length += Vector2.Distance(pts[i - 1], pts[i]);
                if (length < 90f) continue;
                float next = Mathf.Min(length * 0.5f, 160f), walked = 0f;
                for (int i = 1; i < pts.Length; i++)
                {
                    float seg = Vector2.Distance(pts[i - 1], pts[i]);
                    while (next <= walked + seg && seg > 0.01f)
                    {
                        var d = (pts[i] - pts[i - 1]) / seg;
                        var at = pts[i - 1] + d * (next - walked);
                        var text = road.name.ToUpperInvariant();
                        if (!Streets.Exists(l => l.Text == text && Vector2.Distance(l.Position, at) < 260f))
                            Streets.Add(new Label { Text = text, Position = at, Direction = d });
                        next += 320f;
                    }
                    walked += seg;
                }
            }

            if (data.landmarks == null) return;
            Vector2? caltrain = null, yc = null, esprit = null;
            var cranes = new List<Vector2>();
            foreach (var l in data.landmarks)
            {
                var p = new Vector2(l.x, l.z);
                if (l.kind == "caltrain_22nd") caltrain = p;
                else if (l.kind == "yc") yc = p;
                else if (l.name == "Esprit Park") esprit = LargestParkNear(data, p) ?? p;
                else if (l.kind == "crane") cranes.Add(p);
            }
            if (caltrain.HasValue) Places.Add(new Label { Text = "22ND ST CALTRAIN", Position = caltrain.Value });
            if (yc.HasValue) Places.Add(new Label { Text = "YC AT PIER 70", Position = yc.Value });
            if (esprit.HasValue) Places.Add(new Label { Text = "ESPRIT PARK", Position = esprit.Value });
            if (cranes.Count >= 2)
            {
                // The two historic gantry cranes on Crane Cove Park's shore are the westmost; the
                // rest stand along Pier 70's drydocks.
                cranes.Sort((a, b) => a.x.CompareTo(b.x));
                var cove = LargestParkNear(data, (cranes[0] + cranes[1]) * 0.5f);
                if (cove.HasValue) Places.Add(new Label { Text = "CRANE COVE", Position = cove.Value });
            }
        }

        // Centroid of the biggest park polygon within 150 m (the data names no parks).
        static Vector2? LargestParkNear(WorldData data, Vector2 near)
        {
            if (data.parks == null) return null;
            Vector2? best = null;
            float bestArea = 0f;
            foreach (var poly in data.parks)
            {
                if (poly.kind != "park") continue;
                var pts = Geometry.ToPoints(poly.pts);
                var c = Geometry.Centroid(pts);
                if (Vector2.Distance(c, near) > 150f) continue;
                float area = 0f;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++) area += pts[j].x * pts[i].y - pts[i].x * pts[j].y;
                area = Mathf.Abs(area) * 0.5f;
                if (area <= bestArea) continue;
                bestArea = area;
                best = c;
            }
            return best;
        }

        // Main thread: turns the built triangles into meshes.
        public void Upload()
        {
            void Add(Part part)
            {
                if (part.Vertices.Count == 0) return;
                var mesh = new Mesh { name = "MapTile", indexFormat = IndexFormat.UInt32, subMeshCount = Layers };
                mesh.SetVertices(part.Vertices);
                mesh.SetColors(part.Colors);
                for (int i = 0; i < Layers; i++)
                {
                    mesh.SetTriangles(part.Layers[i], i, false);
                    TriangleCount += part.Layers[i].Count / 3;
                }
                mesh.bounds = new Bounds(new Vector3(part.Bounds.center.x, part.Bounds.center.y, 0f), new Vector3(part.Bounds.width, part.Bounds.height, 1f));
                mesh.UploadMeshData(true);
                meshes.Add((mesh, part.Bounds));
            }
            Add(global);
            foreach (var part in tiles.Values) Add(part);
            tiles.Clear();
        }

        public int TriangleCount { get; private set; }

        // Queues every tile overlapping view (world metres), layer by layer so streets stay over
        // parks and buildings over streets across tile edges. model maps a vertex (world x,
        // world z, 0) to a pixel of the target.
        public void Draw(CommandBuffer cb, Material material, Matrix4x4 model, Rect view)
        {
            for (int layer = 0; layer < Layers; layer++)
                foreach (var (mesh, bounds) in meshes)
                    if (bounds.Overlaps(view)) cb.DrawMesh(mesh, model, material, layer, 0);
        }
    }
}
