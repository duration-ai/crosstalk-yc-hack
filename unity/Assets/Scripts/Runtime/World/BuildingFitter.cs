using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.World
{
    // Turns every DataSF footprint into Polyfork models fitted to it. The footprint's minimum-area
    // rectangle is split into a grid of cells sized to the chosen model, oriented so model fronts
    // (+Z) face the nearest street; every cell on the footprint's edge gets one model scaled to the
    // cell and to the building's real height, interior cells get a flat roof. The extruded footprint
    // stays as the collider and as the far LOD.
    public sealed class BuildingFitter
    {
        public struct Fit
        {
            public string Id, Type, Role, Variant;
            public int Cells, Models;
            public float Width, Depth, Height, Area, Iou, MinStretch, MaxStretch, HeightScale;
        }

        // Footprints at or above this IoU with every model stretched at most this much on any axis are within tolerance.
        public const float IouTolerance = 0.75f;
        public const float StretchTolerance = 0.4f;
        const int MaxCellsPerSide = 24;

        static readonly HashSet<string> SideFacing = new HashSet<string>
        {
            "building:brick-hall", "building:brick-industrial", "building:metal-shed", "building:workshop", "building:shed", "building:garage", "building:tank", "building:parking",
        };

        readonly WorldContext ctx;
        readonly ModelLibrary models;
        readonly StaticModels statics;
        readonly Dictionary<string, Landmark> landmarks = new Dictionary<string, Landmark>();
        readonly Dictionary<string, Color> farColours = new Dictionary<string, Color>();
        readonly Dictionary<string, Color> roofColours = new Dictionary<string, Color>();
        readonly bool measure;
        public readonly List<Fit> Fits = new List<Fit>();

        public BuildingFitter(WorldContext ctx, ModelLibrary models, StaticModels statics, Landmark[] landmarkData, bool measure)
        {
            this.ctx = ctx;
            this.models = models;
            this.statics = statics;
            this.measure = measure;
            if (landmarkData != null)
                foreach (var l in landmarkData)
                    if (!string.IsNullOrEmpty(l.footprintId) && (l.kind == "yc" || !landmarks.ContainsKey(l.footprintId))) landmarks[l.footprintId] = l;
        }

        public void Add(Building building, Vector2[] poly, float height)
        {
            var box = OrientedBox.Of(poly);
            float area = Mathf.Abs(Geometry.SignedArea(poly));
            var centroid = Geometry.Centroid(poly);
            landmarks.TryGetValue(building.id ?? "", out var landmark);
            if (landmark != null && landmark.kind == "smokestack") return;
            string role = Role(building, landmark, box, area, height, centroid, out string colour);

            // The street side: whichever box face points most toward the nearest road.
            var front = box.Normal;
            if (ctx.Roads.Nearest(centroid, 80f, out var hit))
            {
                var toRoad = (hit.Point - centroid).normalized;
                Vector2[] faces = { box.Axis, -box.Axis, box.Normal, -box.Normal };
                float best = -2f;
                foreach (var f in faces)
                {
                    float d = Vector2.Dot(f, toRoad);
                    if (d > best) { best = d; front = f; }
                }
            }

            var candidates = models.Role(role);
            if (candidates.Count == 0) candidates = models.Role("building:workshop");
            float bestScore = float.MaxValue;
            ModelEntry pick = null;
            Vector2 pickFront = front;
            int pickNx = 1, pickNz = 1;
            uint hash = Hash(building.id);
            var orientations = SideFacing.Contains(role) ? new[] { front, new Vector2(-front.y, front.x) } : new[] { front };
            for (int o = 0; o < orientations.Length; o++)
            {
                var f = orientations[o];
                bool alongAxis = Mathf.Abs(Vector2.Dot(f, box.Axis)) > 0.5f;
                float width = 2f * (alongAxis ? box.ExtentNormal : box.ExtentAxis);
                float depth = 2f * (alongAxis ? box.ExtentAxis : box.ExtentNormal);
                foreach (var m in candidates)
                {
                    if (colour != null && !m.id.Contains("-" + colour + "-") && !m.id.EndsWith("-" + colour)) continue;
                    int nx = Mathf.Clamp(Mathf.RoundToInt(width / m.sizeX), 1, MaxCellsPerSide);
                    int nz = Mathf.Clamp(Mathf.RoundToInt(depth / m.sizeZ), 1, MaxCellsPerSide);
                    float score = 2f * Mathf.Abs(Mathf.Log(height / m.sizeY)) + Mathf.Abs(Mathf.Log(width / nx / m.sizeX)) + Mathf.Abs(Mathf.Log(depth / nz / m.sizeZ)) + o * 0.15f;
                    score += ((Hash(building.id + m.id) % 1000) / 1000f) * 0.02f;
                    if (score >= bestScore) continue;
                    bestScore = score; pick = m; pickFront = f; pickNx = nx; pickNz = nz;
                }
            }
            if (pick == null) return;
            string variant = Recolour(pick, candidates, hash, colour);

            bool frontAlongAxis = Mathf.Abs(Vector2.Dot(pickFront, box.Axis)) > 0.5f;
            var forward = pickFront;
            var right = new Vector2(forward.y, -forward.x);
            float W = 2f * (frontAlongAxis ? box.ExtentNormal : box.ExtentAxis);
            float D = 2f * (frontAlongAxis ? box.ExtentAxis : box.ExtentNormal);
            int nxCells = pickNx, nzCells = pickNz;
            float cw = W / nxCells, cd = D / nzCells;

            bool rectangular = area / (W * D) >= 0.9f;
            var inside = new bool[nxCells, nzCells];
            int count = 0;
            for (int i = 0; i < nxCells; i++)
                for (int j = 0; j < nzCells; j++)
                {
                    if (rectangular) { inside[i, j] = true; count++; continue; }
                    int hits = 0;
                    for (int a = 0; a < 3; a++)
                        for (int b = 0; b < 3; b++)
                            if (Geometry.Contains(poly, CellPoint(box.Center, right, forward, W, D, cw, cd, i + (a + 0.5f) / 3f, j + (b + 0.5f) / 3f))) hits++;
                    if (hits >= 5) { inside[i, j] = true; count++; }
                }
            if (count == 0)
            {
                inside[nxCells / 2, nzCells / 2] = true;
                count = 1;
            }

            float yaw = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var scale = new Vector3(cw / pick.sizeX, height / pick.sizeY, cd / pick.sizeZ);
            var roofColour = RoofColour(pick.id);
            bool terrace = landmark == null && (role == "building:victorian-low" || role == "building:victorian");
            int placed = 0;
            for (int i = 0; i < nxCells; i++)
                for (int j = 0; j < nzCells; j++)
                {
                    if (!inside[i, j]) continue;
                    var c = CellPoint(box.Center, right, forward, W, D, cw, cd, i + 0.5f, j + 0.5f);
                    bool interior = i > 0 && j > 0 && i < nxCells - 1 && j < nzCells - 1 && inside[i - 1, j] && inside[i + 1, j] && inside[i, j - 1] && inside[i, j + 1];
                    if (interior)
                    {
                        var hr = right * cw * 0.5f;
                        var hf = forward * cd * 0.5f;
                        statics.Generated(StaticModels.Layer.Buildings, c).Quad(MeshBuilder.At(c - hr - hf, height), MeshBuilder.At(c - hr + hf, height), MeshBuilder.At(c + hr + hf, height), MeshBuilder.At(c + hr - hf, height), roofColour, Vector3.up);
                        continue;
                    }
                    string cellVariant = terrace ? Recolour(pick, candidates, Hash(building.id + "/" + (i / 2)), colour) : variant;
                    if (statics.Place(StaticModels.Layer.Buildings, cellVariant, c, Matrix4x4.TRS(MeshBuilder.At(c, 0f), rotation, scale), building.id) >= 0) placed++;
                }

            AddFar(poly, height, centroid, variant);

            var fit = new Fit
            {
                Id = building.id, Type = building.type ?? "", Role = role, Variant = variant, Cells = count, Models = placed,
                Width = W, Depth = D, Height = height, Area = area, HeightScale = scale.y,
                MinStretch = Mathf.Min(scale.x, scale.z), MaxStretch = Mathf.Max(scale.x, scale.z), Iou = -1f,
            };
            if (measure) fit.Iou = Iou(poly, box.Center, right, forward, W, D, nxCells, nzCells, inside, area);
            Fits.Add(fit);
        }

        static Vector2 CellPoint(Vector2 center, Vector2 right, Vector2 forward, float W, float D, float cw, float cd, float u, float v) =>
            center + right * (-W * 0.5f + u * cw) + forward * (-D * 0.5f + v * cd);

        string Role(Building b, Landmark landmark, OrientedBox box, float area, float h, Vector2 centroid, out string colour)
        {
            colour = null;
            var tags = b.osmTags ?? System.Array.Empty<string>();
            bool Tag(string kv) => System.Array.IndexOf(tags, kv) >= 0;
            if (landmark != null)
            {
                switch (landmark.kind)
                {
                    case "yc": colour = "brick"; return "building:loft";
                    case "machine_shop": colour = "brick"; return "building:brick-hall";
                    case "cream_stone": colour = "cream"; return "building:loft";
                }
            }
            if (Tag("amenity=place_of_worship") || Tag("building=church")) return "building:church";
            if (Tag("amenity=fire_station") || Tag("building=fire_station")) return "building:fire-station";
            if (Tag("man_made=storage_tank") || Tag("building=storage_tank")) return "building:tank";

            float w = 2f * Mathf.Max(box.ExtentAxis, box.ExtentNormal), d = 2f * Mathf.Min(box.ExtentAxis, box.ExtentNormal);
            float aspect = w / Mathf.Max(d, 0.1f);
            bool corner = ctx.Roads.DistanceToIntersection(centroid, 40f) < Mathf.Max(w, d) * 0.5f + 10f;
            int year = b.yearBuilt;
            switch (b.type)
            {
                case "victorian": return h <= 8.5f ? "building:victorian-low" : "building:victorian";
                case "rowhouse_modern": return h <= 9f ? "building:victorian-low" : "building:apartment";
                case "apartment_midrise": return h > 28f && area < 400f ? "building:tower" : "building:apartment";
                case "mixed_use":
                    if (h <= 6f) return "building:shop-row";
                    if (h <= 9f && corner) return "building:corner-shop";
                    return year > 0 && year < 1945 ? "building:loft" : "building:apartment";
                case "commercial_lowrise":
                    if (corner && area < 500f) return "building:corner-shop";
                    return h <= 6f ? "building:shop-row" : "building:workshop";
                case "office":
                    if (h > 25f && area < 600f) return "building:tower";
                    return year > 0 && year < 1945 ? "building:loft" : "building:midrise";
                case "warehouse": return h <= 9f ? "building:metal-shed" : "building:workshop";
                case "industrial_brick": return aspect >= 2.2f && h >= 6f && h <= 14f ? "building:brick-hall" : "building:brick-industrial";
                case "shed": return "building:shed";
                case "garage": return h > 5.5f ? "building:parking" : "building:garage";
                case "civic": return "building:school";
                case "utility": return aspect < 1.3f && area / box.Area < 0.86f && area / box.Area > 0.7f ? "building:tank" : "building:workshop";
            }
            if (area < 60f && h < 4.5f) return "building:shed";
            if (area > 800f) return h < 10f ? "building:metal-shed" : "building:workshop";
            if (h <= 9f) return "building:victorian-low";
            return h <= 22f ? "building:apartment" : "building:tower";
        }

        // Same shape as the pick, colour chosen by hash so a block reads as a row of different houses.
        string Recolour(ModelEntry pick, IReadOnlyList<ModelEntry> candidates, uint hash, string colour)
        {
            var same = new List<string>();
            foreach (var m in candidates)
                if (Mathf.Approximately(m.sizeX, pick.sizeX) && Mathf.Approximately(m.sizeY, pick.sizeY) && Mathf.Approximately(m.sizeZ, pick.sizeZ))
                    if (colour == null || m.id.Contains("-" + colour + "-") || m.id.EndsWith("-" + colour)) same.Add(m.id);
            return same.Count == 0 ? pick.id : same[(int)(hash % (uint)same.Count)];
        }

        // Interior cells continue the model's own roof: the colour covering the most upward-facing
        // area in its top third.
        Color RoofColour(string variant)
        {
            if (roofColours.TryGetValue(variant, out var cached)) return cached;
            var mesh = statics.Mesh(variant);
            var verts = mesh.vertices;
            var cols = mesh.colors;
            var tris = mesh.triangles;
            float top = mesh.bounds.max.y - mesh.bounds.size.y / 3f;
            var area = new Dictionary<Color, float>();
            for (int i = 0; cols.Length > 0 && i < tris.Length; i += 3)
            {
                var a = verts[tris[i]];
                var n = Vector3.Cross(verts[tris[i + 1]] - a, verts[tris[i + 2]] - a);
                if (a.y < top || Mathf.Abs(n.normalized.y) < 0.9f) continue;
                var c = cols[tris[i]];
                area.TryGetValue(c, out float s);
                area[c] = s + n.magnitude;
            }
            var best = WorldBuilder.Palette("shadow");
            float most = 0f;
            foreach (var pair in area)
                if (pair.Value > most) { most = pair.Value; best = pair.Key; }
            return roofColours[variant] = best;
        }

        void AddFar(Vector2[] poly, float h, Vector2 centroid, string variant)
        {
            if (!farColours.TryGetValue(variant, out var colour))
            {
                var mesh = statics.Mesh(variant);
                var sum = Color.black;
                var cols = mesh.colors;
                foreach (var c in cols) sum += c;
                farColours[variant] = colour = cols.Length > 0 ? sum / cols.Length : Color.gray;
            }
            var mb = statics.Generated(StaticModels.Layer.BuildingsFar, centroid);
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j];
                var b = poly[i];
                var outward = new Vector3(b.y - a.y, 0f, a.x - b.x).normalized;
                mb.Quad(MeshBuilder.At(a, 0f), MeshBuilder.At(a, h), MeshBuilder.At(b, h), MeshBuilder.At(b, 0f), colour, outward);
            }
            mb.Polygon(poly, h, colour * 0.8f);
        }

        static float Iou(Vector2[] poly, Vector2 center, Vector2 right, Vector2 forward, float W, float D, int nx, int nz, bool[,] inside, float area)
        {
            float step = Mathf.Clamp(Mathf.Sqrt(area) / 40f, 0.25f, 2f);
            float pad = 2f;
            int both = 0, inPoly = 0, inCover = 0;
            for (float u = -W * 0.5f - pad; u < W * 0.5f + pad; u += step)
                for (float v = -D * 0.5f - pad; v < D * 0.5f + pad; v += step)
                {
                    var p = center + right * u + forward * v;
                    bool a = Geometry.Contains(poly, p);
                    int i = Mathf.FloorToInt((u + W * 0.5f) / (W / nx));
                    int j = Mathf.FloorToInt((v + D * 0.5f) / (D / nz));
                    bool b = i >= 0 && j >= 0 && i < nx && j < nz && inside[i, j];
                    if (a) inPoly++;
                    if (b) inCover++;
                    if (a && b) both++;
                }
            int union = inPoly + inCover - both;
            return union == 0 ? 0f : (float)both / union;
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") h = (h ^ c) * 16777619;
            return h;
        }
    }
}
