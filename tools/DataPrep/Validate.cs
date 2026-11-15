using SkiaSharp;
using P = (double x, double z);

namespace DataPrep;

/// <summary>
/// Validation overlays only: Esri World Imagery tiles are fetched (cached) as a backdrop to eyeball projection offsets.
/// Nothing is sampled from the imagery; only the overlay PNGs under data/validation/ are committed.
/// </summary>
static class Validate
{
    const string TileUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{0}/{1}/{2}";

    static (double px, double py) Merc(double lat, double lon, int z)
    {
        double n = 256.0 * (1 << z), s = Math.Sin(lat * Math.PI / 180);
        return ((lon + 180) / 360 * n, (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * n);
    }

    public static readonly Dictionary<string, SKColor> KindColors = new()
    {
        ["street_lamp"] = new SKColor(255, 240, 80), ["traffic_signal"] = new SKColor(255, 60, 60), ["stop_sign"] = new SKColor(220, 20, 20), ["give_way"] = new SKColor(240, 120, 120),
        ["crossing"] = new SKColor(255, 255, 255), ["fire_hydrant"] = new SKColor(255, 120, 0), ["bench"] = new SKColor(150, 90, 40), ["bus_stop"] = new SKColor(60, 120, 255),
        ["bollard"] = new SKColor(170, 170, 170), ["bike_rack"] = new SKColor(200, 80, 220), ["parking_meter"] = new SKColor(0, 200, 200), ["trash_can"] = new SKColor(120, 120, 120),
        ["power_pole"] = new SKColor(255, 160, 200), ["bicycle_rental"] = new SKColor(0, 255, 160), ["crane"] = new SKColor(255, 0, 255), ["yc"] = new SKColor(255, 255, 0),
    };
    public static SKColor KindColor(string k) => KindColors.GetValueOrDefault(k, new SKColor(255, 255, 255, 200));

    static (double x, double z)? Intersect(WorldData w, string a, string b)
    {
        var sa = Segs(w, a).ToList(); var sb = Segs(w, b).ToList();
        double best = 1e9; (double, double)? at = null;
        foreach (var s in sa) foreach (var t in sb)
            foreach (var p in new[] { s.a, s.b }) { var d = Geo.Nearest(p, t.a, t.b); if (d.dist < best) { best = d.dist; at = ((p.x + d.pt.x) / 2, (p.z + d.pt.z) / 2); } }
        return best < 15 ? at : null;
    }
    static IEnumerable<(P a, P b)> Segs(WorldData w, string name)
    {
        foreach (var r in w.Roads.Where(r => r.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            for (int i = 0; i + 3 < r.Pts.Length; i += 2) yield return ((r.Pts[i], r.Pts[i + 1]), (r.Pts[i + 2], r.Pts[i + 3]));
    }

    public static async Task<List<string>> Run(WorldData w, Fetch fetch, string dir)
    {
        Directory.CreateDirectory(dir);
        P Mid(P a, P b) => ((a.x + b.x) / 2, (a.z + b.z) / 2);
        var t22 = Intersect(w, "Tennessee", "22nd"); var t3_22 = Intersect(w, "3rd", "22nd") ?? Intersect(w, "Third", "22nd");
        var t3_20 = Intersect(w, "3rd", "20th") ?? Intersect(w, "Third", "20th");
        var pier = w.Landmarks.Where(l => l.Kind is "machine_shop" or "cream_stone" or "yc" || (l.Kind == "crane" && l.OsmId.StartsWith("way/"))).ToList();
        P pierC = pier.Count > 0 ? ((pier.Min(l => l.X) + pier.Max(l => l.X)) / 2, (pier.Min(l => l.Z) + pier.Max(l => l.Z)) / 2) : Geo.Proj(37.7605, -122.3858);
        var views = new List<(string file, string title, P c, int z, int wpx, int hpx)>
        {
            ("tennessee_22nd.png", "Tennessee St at 22nd St", t22 is { } q0 ? (q0.x, q0.z) : Geo.Proj(37.7578, -122.3893), 19, 960, 700),
            ("third_20th_22nd.png", "3rd St between 20th and 22nd", t3_20 is { } q1 && t3_22 is { } q2 ? Mid((q1.x, q1.z), (q2.x, q2.z)) : Geo.Proj(37.7585, -122.3885), 19, 700, 960),
            ("22nd_tennessee_third.png", "22nd St between Tennessee and 3rd", t22 is { } q3 && t3_22 is { } q4 ? Mid((q3.x, q3.z), (q4.x, q4.z)) : Geo.Proj(37.7578, -122.3887), 19, 960, 700),
            ("pier70.png", "Pier 70 historic shipyard buildings and cranes", pierC, 18, 900, 920),
        };
        var done = new List<string>();
        foreach (var (file, title, center, z, wpx, hpx) in views)
        {
            var ll = Geo.Unproj(center);
            var (cx, cy) = Merc(ll.lat, ll.lon, z);
            double tlx = cx - wpx / 2.0, tly = cy - hpx / 2.0;
            using var bmp = new SKBitmap(wpx, hpx);
            using var cv = new SKCanvas(bmp);
            cv.Clear(SKColors.DarkGray);
            for (int tx = (int)Math.Floor(tlx / 256); tx <= (int)Math.Floor((tlx + wpx) / 256); tx++)
                for (int ty = (int)Math.Floor(tly / 256); ty <= (int)Math.Floor((tly + hpx) / 256); ty++)
                {
                    using var tile = SKBitmap.Decode(await fetch.Get(string.Format(TileUrl, z, ty, tx), "image/jpeg"));
                    if (tile != null) cv.DrawBitmap(tile, (float)(tx * 256 - tlx), (float)(ty * 256 - tly));
                }
            SKPoint Px(double x, double zz) { var l = Geo.Unproj((x, zz)); var (px, py) = Merc(l.lat, l.lon, z); return new SKPoint((float)(px - tlx), (float)(py - tly)); }
            using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0, 0, 0, 200) };
            SKPath Poly(float[] pts, bool close)
            {
                var p = new SKPath();
                for (int i = 0; i + 1 < pts.Length; i += 2) { var q = Px(pts[i], pts[i + 1]); if (i == 0) p.MoveTo(q); else p.LineTo(q); }
                if (close) p.Close();
                return p;
            }
            line.Color = new SKColor(255, 230, 0);
            foreach (var b in w.Buildings) { using var p = Poly(b.Pts, true); cv.DrawPath(p, line); }
            line.StrokeWidth = 1.8f;
            foreach (var r in w.Roads) { line.Color = r.Kind == "path" ? new SKColor(255, 0, 255) : new SKColor(0, 255, 255); line.StrokeWidth = r.Kind == "path" ? 1f : 1.8f; using var p = Poly(r.Pts, false); cv.DrawPath(p, line); }
            line.StrokeWidth = 1.8f; line.Color = new SKColor(255, 140, 0);
            foreach (var r in w.Rails) { using var p = Poly(r.Pts, false); cv.DrawPath(p, line); }
            fill.Color = new SKColor(40, 220, 60);
            foreach (var t in w.TreeRecords) { var q = Px(t.X, t.Z); cv.DrawCircle(q, 3.2f, fill); cv.DrawCircle(q, 3.2f, edge); }
            foreach (var pr in w.Props) { var q = Px(pr.X, pr.Z); fill.Color = KindColor(pr.Kind); cv.DrawCircle(q, 3.4f, fill); cv.DrawCircle(q, 3.4f, edge); }
            foreach (var l in w.Landmarks.Where(l => l.Kind is "yc" or "crane" or "machine_shop" or "cream_stone" or "smokestack"))
            { var q = Px(l.X, l.Z); fill.Color = SKColors.White; cv.DrawCircle(q, 7, fill); fill.Color = KindColor(l.Kind); cv.DrawCircle(q, 5, fill); }
            // scale bar (20 m) + title
            double mpp = 156543.03392 * Math.Cos(ll.lat * Math.PI / 180) / (1 << z);
            float bar = (float)(20 / mpp);
            using var bg = new SKPaint { Color = new SKColor(0, 0, 0, 170) }; cv.DrawRect(6, hpx - 30, bar + 110, 24, bg);
            using var txt = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Lato") ?? SKTypeface.Default };
            cv.DrawRect(12, hpx - 18, bar, 4, txt); cv.DrawText("20 m", bar + 20, hpx - 12, txt);
            cv.DrawRect(6, 6, 520, 24, bg); cv.DrawText(title + "  (z" + z + ", yellow=footprints cyan=roads green=trees dots=props)", 10, 23, txt);
            using var img = SKImage.FromBitmap(bmp);
            byte[] png = img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
            float scale = 1;
            while (png.Length > 950_000 && scale > 0.4f)
            {
                scale -= 0.1f;
                using var small = bmp.Resize(new SKImageInfo((int)(wpx * scale), (int)(hpx * scale)), SKFilterQuality.High);
                using var si = SKImage.FromBitmap(small);
                png = si.Encode(SKEncodedImageFormat.Png, 100).ToArray();
            }
            File.WriteAllBytes(Path.Combine(dir, file), png);
            Console.WriteLine($"validation: {file} {png.Length / 1024} KB (scale {scale:F1})");
            done.Add("data/validation/" + file);
        }
        return done;
    }
}
