namespace DataPrep;

using P = (double x, double z);

static class Geo
{
    public static double OriginLat, OriginLon;
    const double MPerDeg = 111194.93;

    public static P Proj(double lat, double lon) =>
        ((lon - OriginLon) * Math.Cos(OriginLat * Math.PI / 180) * MPerDeg, (lat - OriginLat) * MPerDeg);

    public static double SignedArea(IList<P> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.x * q.z - q.x * p.z; }
        return a / 2;
    }

    public static P Centroid(IList<P> r)
    {
        double a = 0, cx = 0, cz = 0;
        for (int i = 0; i < r.Count; i++)
        {
            var p = r[i]; var q = r[(i + 1) % r.Count];
            double c = p.x * q.z - q.x * p.z; a += c; cx += (p.x + q.x) * c; cz += (p.z + q.z) * c;
        }
        if (Math.Abs(a) < 1e-9) return (r.Average(p => p.x), r.Average(p => p.z));
        return (cx / (3 * a), cz / (3 * a));
    }

    public static bool PointInPoly(P pt, IList<P> r)
    {
        bool inside = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            if ((r[i].z > pt.z) != (r[j].z > pt.z) && pt.x < (r[j].x - r[i].x) * (pt.z - r[i].z) / (r[j].z - r[i].z) + r[i].x)
                inside = !inside;
        return inside;
    }

    public static List<P> Dedupe(IEnumerable<P> pts)
    {
        var l = new List<P>();
        foreach (var p in pts) if (l.Count == 0 || Dist(l[^1], p) > 1e-6) l.Add(p);
        return l;
    }

    public static double Dist(P a, P b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    public static double MinX, MaxX, MinZ, MaxZ;
    // Optional outline of the world when it isn't the box (DataPrep --soma). Inside and ClipLine follow it.
    public static List<P> Edge;
    public static bool Inside(P p) => p.x >= MinX && p.x <= MaxX && p.z >= MinZ && p.z <= MaxZ && (Edge == null || PointInPoly(p, Edge));

    // Splits a run (already clipped to the box) where it crosses Edge, keeping the pieces inside it.
    static List<List<P>> ClipToEdge(List<P> run)
    {
        var runs = new List<List<P>>(); var cur = new List<P>();
        for (int i = 0; i + 1 < run.Count; i++)
        {
            var a = run[i]; var b = run[i + 1];
            var ts = new List<double> { 0, 1 };
            for (int k = 0; k < Edge.Count; k++)
            {
                var c = Edge[k]; var d = Edge[(k + 1) % Edge.Count];
                double den = (b.x - a.x) * (d.z - c.z) - (b.z - a.z) * (d.x - c.x);
                if (Math.Abs(den) < 1e-12) continue;
                double t = ((c.x - a.x) * (d.z - c.z) - (c.z - a.z) * (d.x - c.x)) / den;
                double u = ((c.x - a.x) * (b.z - a.z) - (c.z - a.z) * (b.x - a.x)) / den;
                if (t > 0 && t < 1 && u >= 0 && u <= 1) ts.Add(t);
            }
            ts.Sort();
            for (int j = 0; j + 1 < ts.Count; j++)
            {
                P p0 = (a.x + (b.x - a.x) * ts[j], a.z + (b.z - a.z) * ts[j]), p1 = (a.x + (b.x - a.x) * ts[j + 1], a.z + (b.z - a.z) * ts[j + 1]);
                if (Dist(p0, p1) < 1e-9) continue;
                if (PointInPoly(((p0.x + p1.x) / 2, (p0.z + p1.z) / 2), Edge))
                {
                    if (cur.Count == 0 || Dist(cur[^1], p0) > 1e-9) { if (cur.Count > 1) runs.Add(cur); cur = new List<P> { p0 }; }
                    cur.Add(p1);
                }
                else { if (cur.Count > 1) runs.Add(cur); cur = new(); }
            }
        }
        if (cur.Count > 1) runs.Add(cur);
        return runs;
    }

    public static (double lat, double lon) Unproj(P p) =>
        (OriginLat + p.z / MPerDeg, OriginLon + p.x / (Math.Cos(OriginLat * Math.PI / 180) * MPerDeg));

    /// <summary>Compass bearing in degrees clockwise from north (+z) of the direction a to b, in [0,360).</summary>
    public static double Bearing(P a, P b)
    {
        var d = Math.Atan2(b.x - a.x, b.z - a.z) * 180 / Math.PI;
        return d < 0 ? d + 360 : d;
    }

    public static double Norm360(double d) { d %= 360; return d < 0 ? d + 360 : d; }

    public static (P pt, double dist, double t) Nearest(P p, P a, P b)
    {
        double dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
        double t = l2 == 0 ? 0 : Math.Clamp(((p.x - a.x) * dx + (p.z - a.z) * dz) / l2, 0, 1);
        var q = (a.x + t * dx, a.z + t * dz);
        return (q, Dist(p, q), t);
    }

    public static double Area(IList<P> r) => Math.Abs(SignedArea(r));

    /// <summary>Minimum-area bounding rectangle: returns length (long side), width, and the long axis as a bearing in [0,180).</summary>
    public static (double len, double wid, double bearing) LongAxis(IList<P> r)
    {
        if (r.Count < 2) return (0, 0, 0);
        double bestA = double.MaxValue, bl = 0, bw = 0, bb = 0;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            double ang = Math.Atan2(b.z - a.z, b.x - a.x), c = Math.Cos(-ang), s = Math.Sin(-ang);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var p in r)
            {
                double u = p.x * c - p.z * s, v = p.x * s + p.z * c;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            double w = u1 - u0, h = v1 - v0, area = w * h;
            if (area >= bestA - 1e-9 || area <= 0) continue;
            bestA = area;
            bool uLong = w >= h; bl = Math.Max(w, h); bw = Math.Min(w, h);
            double axisAng = uLong ? ang : ang + Math.PI / 2;
            bb = Norm360(90 - axisAng * 180 / Math.PI) % 180;
        }
        return (bl, bw, bb);
    }

    static double SegDist(P p, P a, P b)
    {
        double dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
        double t = l2 == 0 ? 0 : Math.Clamp(((p.x - a.x) * dx + (p.z - a.z) * dz) / l2, 0, 1);
        return Dist(p, (a.x + t * dx, a.z + t * dz));
    }

    static void DP(List<P> pts, int lo, int hi, double eps, bool[] keep)
    {
        double best = 0; int idx = -1;
        for (int i = lo + 1; i < hi; i++) { double d = SegDist(pts[i], pts[lo], pts[hi]); if (d > best) { best = d; idx = i; } }
        if (idx < 0 || best <= eps) return;
        keep[idx] = true; DP(pts, lo, idx, eps, keep); DP(pts, idx, hi, eps, keep);
    }

    public static List<P> SimplifyRing(List<P> ring, double eps)
    {
        if (ring.Count < 4) return ring;
        int far = 0; double fd = -1;
        for (int i = 1; i < ring.Count; i++) { double d = Dist(ring[0], ring[i]); if (d > fd) { fd = d; far = i; } }
        var keep = new bool[ring.Count]; keep[0] = keep[far] = true;
        DP(ring, 0, far, eps, keep);
        var tail = ring.Skip(far).Append(ring[0]).ToList();
        var k2 = new bool[tail.Count]; k2[0] = k2[^1] = true;
        DP(tail, 0, tail.Count - 1, eps, k2);
        var res = new List<P>();
        for (int i = 0; i <= far; i++) if (keep[i]) res.Add(ring[i]);
        for (int i = 1; i < tail.Count - 1; i++) if (k2[i]) res.Add(tail[i]);
        return res;
    }

    public static List<List<P>> ClipLine(IList<P> pts, double x0, double x1, double z0, double z1)
    {
        var runs = new List<List<P>>(); var run = new List<P>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            double dx = b.x - a.x, dz = b.z - a.z, t0 = 0, t1 = 1;
            bool ok = Clip(-dx, a.x - x0, ref t0, ref t1) && Clip(dx, x1 - a.x, ref t0, ref t1)
                   && Clip(-dz, a.z - z0, ref t0, ref t1) && Clip(dz, z1 - a.z, ref t0, ref t1);
            if (!ok) { if (run.Count > 1) runs.Add(run); run = new(); continue; }
            if (t0 > 0 && run.Count > 1) { runs.Add(run); run = new(); } else if (t0 > 0) run = new();
            var p0 = (a.x + t0 * dx, a.z + t0 * dz); var p1 = (a.x + t1 * dx, a.z + t1 * dz);
            if (run.Count == 0 || Dist(run[^1], p0) > 1e-9) run.Add(p0);
            run.Add(p1);
            if (t1 < 1) { runs.Add(run); run = new(); }
        }
        if (run.Count > 1) runs.Add(run);
        return Edge == null ? runs : runs.SelectMany(ClipToEdge).ToList();
    }

    static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0) return q >= 0;
        double r = q / p;
        if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }

    public static List<P> ClipPolygon(List<P> poly, double x0, double x1, double z0, double z1)
    {
        var edges = new (Func<P, bool> inside, Func<P, P, P> isect)[]
        {
            (p => p.x >= x0, (a, b) => (x0, a.z + (b.z - a.z) * (x0 - a.x) / (b.x - a.x))),
            (p => p.x <= x1, (a, b) => (x1, a.z + (b.z - a.z) * (x1 - a.x) / (b.x - a.x))),
            (p => p.z >= z0, (a, b) => (a.x + (b.x - a.x) * (z0 - a.z) / (b.z - a.z), z0)),
            (p => p.z <= z1, (a, b) => (a.x + (b.x - a.x) * (z1 - a.z) / (b.z - a.z), z1)),
        };
        var cur = poly;
        foreach (var (inside, isect) in edges)
        {
            var next = new List<P>();
            for (int i = 0; i < cur.Count; i++)
            {
                var a = cur[i]; var b = cur[(i + 1) % cur.Count];
                if (inside(b)) { if (!inside(a)) next.Add(isect(a, b)); next.Add(b); }
                else if (inside(a)) next.Add(isect(a, b));
            }
            cur = next;
            if (cur.Count < 3) return new();
        }
        return cur;
    }
}
