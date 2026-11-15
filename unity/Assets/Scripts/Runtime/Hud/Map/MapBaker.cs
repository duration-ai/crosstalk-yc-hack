using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Map
{
    // Rasterises the flat top-down Dogpatch once, off the main thread: bay, land, parks, paths,
    // streets and building blocks in the world palette, at 1.25 px per metre. Pure C# over the
    // world data (no Unity calls), so it runs in a worker; the Minimap uploads the pixels as a
    // linear texture so they reach the screen unconverted, like the HUD's GL vertex colours.
    public static class MapBaker
    {
        public const float PixelsPerMeter = 1.25f;

        public struct Palette
        {
            public Color32 Bay;
            public Color32 Land;
            public Color32 Park;
            public Color32 Path;
            public Color32 Street;
            public Color32 Building;
        }

        public sealed class Result
        {
            public Color32[] Pixels;
            public int Width;
            public int Height;
            public Rect Bounds;
        }

        public static Result Bake(WorldData data, Palette palette)
        {
            var bounds = Rect.MinMaxRect(data.minX, data.minZ, data.maxX, data.maxZ);
            int w = Mathf.CeilToInt(bounds.width * PixelsPerMeter);
            int h = Mathf.CeilToInt(bounds.height * PixelsPerMeter);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = palette.Bay;
            var canvas = new Canvas { Pixels = px, Width = w, Height = h, Origin = bounds.min };

            if (data.land != null) foreach (var poly in data.land) canvas.Fill(Geometry.ToPoints(poly.pts), palette.Land);
            if (data.parks != null) foreach (var poly in data.parks) canvas.Fill(Geometry.ToPoints(poly.pts), palette.Park);
            foreach (var road in data.roads)
                if (road.kind == "path") canvas.Line(Geometry.ToPoints(road.pts), 1.4f, palette.Path);
            foreach (var road in data.roads)
                if (road.kind != "path") canvas.Line(Geometry.ToPoints(road.pts), Mathf.Max(road.width, 5f) * 0.5f, palette.Street);
            foreach (var b in data.buildings) canvas.Fill(Geometry.ToPoints(b.pts), palette.Building);

            return new Result { Pixels = px, Width = w, Height = h, Bounds = bounds };
        }

        sealed class Canvas
        {
            public Color32[] Pixels;
            public int Width;
            public int Height;
            public Vector2 Origin;
            readonly List<float> crossings = new List<float>();

            Vector2 ToPixel(Vector2 p) => (p - Origin) * PixelsPerMeter;

            // Even-odd scanline fill at pixel centres.
            public void Fill(Vector2[] world, Color32 color)
            {
                if (world.Length < 3) return;
                var poly = new Vector2[world.Length];
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < world.Length; i++)
                {
                    poly[i] = ToPixel(world[i]);
                    minY = Mathf.Min(minY, poly[i].y);
                    maxY = Mathf.Max(maxY, poly[i].y);
                }
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(maxY));
                for (int y = y0; y <= y1; y++)
                {
                    float sy = y + 0.5f;
                    crossings.Clear();
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        var a = poly[j];
                        var b = poly[i];
                        if ((a.y <= sy) == (b.y <= sy)) continue;
                        crossings.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    crossings.Sort();
                    for (int k = 0; k + 1 < crossings.Count; k += 2)
                    {
                        int x0 = Mathf.Max(0, Mathf.CeilToInt(crossings[k] - 0.5f));
                        int x1 = Mathf.Min(Width - 1, Mathf.FloorToInt(crossings[k + 1] - 0.5f));
                        int row = y * Width;
                        for (int x = x0; x <= x1; x++) Pixels[row + x] = color;
                    }
                }
            }

            // A polyline of round-capped segments with a half-pixel antialiased edge.
            public void Line(Vector2[] world, float halfWidth, Color32 color)
            {
                float hw = halfWidth * PixelsPerMeter;
                for (int s = 0; s < world.Length - 1; s++)
                {
                    var a = ToPixel(world[s]);
                    var b = ToPixel(world[s + 1]);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - hw - 1f));
                    int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + hw + 1f));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - hw - 1f));
                    int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + hw + 1f));
                    var ab = b - a;
                    float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(x + 0.5f, y + 0.5f);
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                            float d = Vector2.Distance(p, a + ab * t);
                            float cover = Mathf.Clamp01(hw - d + 0.5f);
                            if (cover <= 0f) continue;
                            int i = y * Width + x;
                            Pixels[i] = cover >= 1f ? color : Color32.Lerp(Pixels[i], color, cover);
                        }
                }
            }
        }
    }
}
