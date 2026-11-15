using SkiaSharp;

namespace DataPrep;

static class Render
{
    const float Scale = 0.7f;

    static SKColor Lerp(SKColor a, SKColor b, float t) =>
        new((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));

    static SKPath Path(float[] pts, bool close, WorldData w)
    {
        var p = new SKPath();
        for (int i = 0; i + 1 < pts.Length; i += 2)
        {
            float x = (pts[i] - w.MinX) * Scale, y = (w.MaxZ - pts[i + 1]) * Scale;
            if (i == 0) p.MoveTo(x, y); else p.LineTo(x, y);
        }
        if (close) p.Close();
        return p;
    }

    public static void Preview(WorldData w, string path)
    {
        int width = (int)((w.MaxX - w.MinX) * Scale), height = (int)((w.MaxZ - w.MinZ) * Scale);
        using var bmp = new SKBitmap(width, height);
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(120, 170, 215));
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        fill.Color = new SKColor(238, 234, 222);
        foreach (var l in w.Land) c.DrawPath(Path(l.Pts, true, w), fill);
        fill.Color = new SKColor(170, 210, 150);
        foreach (var p in w.Parks) c.DrawPath(Path(p.Pts, true, w), fill);
        foreach (var r in w.Roads.OrderBy(r => r.Width))
        {
            stroke.Color = r.Kind == "path" ? new SKColor(200, 160, 140) : r.Width >= 11 ? new SKColor(255, 220, 140) : SKColors.White;
            stroke.StrokeWidth = Math.Max(1, r.Width * Scale);
            using var rp = Path(r.Pts, false, w);
            c.DrawPath(rp, stroke);
        }
        stroke.Color = new SKColor(60, 60, 60); stroke.StrokeWidth = 1.2f; stroke.PathEffect = SKPathEffect.CreateDash(new[] { 5f, 3f }, 0);
        foreach (var r in w.Rails) c.DrawPath(Path(r.Pts, false, w), stroke);
        foreach (var b in w.Buildings)
        {
            fill.Color = Lerp(new SKColor(215, 205, 195), new SKColor(90, 70, 110), Math.Clamp(b.Height / 40f, 0, 1));
            c.DrawPath(Path(b.Pts, true, w), fill);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
    }
}
