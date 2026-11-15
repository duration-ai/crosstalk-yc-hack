using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Dogpatch.Data;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Builds the world headless with fit measurement on and writes the coverage and fit report
    // (WORLD_REPORT, default ../Builds/world-report.json), then the reference renders (RENDER_DIR):
    //   xvfb-run -a Unity -batchmode -quit -projectPath unity -executeMethod Dogpatch.EditorTools.WorldReport.Run
    public static class WorldReport
    {
        public static void Run()
        {
            try
            {
                var material = WorldRenders.Material();
                var data = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("World/dogpatch").text);
                var models = new ModelLibrary();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var timed = WorldBuilder.Build(data, models, material);
                double buildSeconds = clock.Elapsed.TotalSeconds;
                UnityEngine.Object.DestroyImmediate(timed.Root);
                var ctx = WorldBuilder.Build(data, models, material, measure: true);

                string path = Environment.GetEnvironmentVariable("WORLD_REPORT") ?? "../Builds/world-report.json";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, Report(ctx, data, buildSeconds));
                Debug.Log("[WorldReport] wrote " + Path.GetFullPath(path));

                string renders = Environment.GetEnvironmentVariable("RENDER_DIR");
                if (!string.IsNullOrEmpty(renders)) WorldRenders.Render(ctx, data, renders);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldReport] " + e);
                EditorApplication.Exit(1);
            }
        }

        static string Report(WorldContext ctx, WorldData data, double buildSeconds)
        {
            var j = new Json();
            j.Open();
            j.Num("buildSeconds", buildSeconds);
            j.Str("note", "buildSeconds is WorldBuilder.Build in the Editor on the build machine without fit measurement; device time differs.");

            var fits = ctx.Fitter.Fits;
            int footprints = data.buildings.Length;
            j.Key("buildings").Open();
            j.Num("footprints", footprints);
            j.Num("fitted", fits.Count);
            j.Num("withModels", fits.Count(f => f.Models > 0));
            j.Num("modelsPlaced", fits.Sum(f => f.Models));
            j.Num("iouTolerance", BuildingFitter.IouTolerance);
            j.Num("stretchTolerance", BuildingFitter.StretchTolerance);
            var ious = fits.Where(f => f.Iou >= 0f).Select(f => f.Iou).ToList();
            j.Key("iou"); Dist(j, ious);
            var stretch = fits.Select(f => Mathf.Max(Mathf.Abs(1f - f.MinStretch), Mathf.Abs(f.MaxStretch - 1f))).ToList();
            j.Key("stretch"); Dist(j, stretch);
            j.Key("heightScale"); Dist(j, fits.Select(f => f.HeightScale).ToList());
            bool Within(BuildingFitter.Fit f) => f.Iou >= BuildingFitter.IouTolerance && Mathf.Max(Mathf.Abs(1f - f.MinStretch), Mathf.Abs(f.MaxStretch - 1f)) <= BuildingFitter.StretchTolerance;
            j.Num("withinIouPct", 100.0 * fits.Count(f => f.Iou >= BuildingFitter.IouTolerance) / Math.Max(fits.Count, 1));
            j.Num("withinStretchPct", 100.0 * stretch.Count(s => s <= BuildingFitter.StretchTolerance) / Math.Max(fits.Count, 1));
            j.Num("withinBothPct", 100.0 * fits.Count(Within) / Math.Max(fits.Count, 1));
            j.Key("byRole").Open();
            foreach (var g in fits.GroupBy(f => f.Role).OrderByDescending(g => g.Count()))
            {
                j.Key(g.Key).Open();
                j.Num("count", g.Count());
                j.Num("meanIou", g.Where(f => f.Iou >= 0f).Select(f => (double)f.Iou).DefaultIfEmpty(0).Average());
                j.Num("withinBothPct", 100.0 * g.Count(Within) / g.Count());
                j.Close();
            }
            j.Close();
            j.Key("byType").Open();
            foreach (var g in fits.GroupBy(f => string.IsNullOrEmpty(f.Type) ? "(none)" : f.Type).OrderByDescending(g => g.Count())) j.Num(g.Key, g.Count());
            j.Close();
            j.Key("outsideTolerance").Array();
            foreach (var f in fits.Where(f => !Within(f)).OrderBy(f => f.Iou).Take(60))
            {
                j.Open();
                j.Str("id", f.Id); j.Str("type", f.Type); j.Str("role", f.Role); j.Str("variant", f.Variant);
                j.Num("iou", f.Iou); j.Num("minStretch", f.MinStretch); j.Num("maxStretch", f.MaxStretch); j.Num("width", f.Width); j.Num("depth", f.Depth); j.Num("height", f.Height); j.Num("area", f.Area); j.Num("cells", f.Cells);
                j.Close();
            }
            j.CloseArray();
            j.Close();

            j.Key("streetFurniture").Open();
            foreach (var pair in ctx.Furniture.Tallies.OrderBy(p => p.Key))
            {
                j.Key(pair.Key).Open();
                j.Num("total", pair.Value.Total); j.Num("placed", pair.Value.Placed); j.Num("movedToCurb", pair.Value.Moved); j.Num("movedMetres", pair.Value.MovedMetres);
                j.Key("exceptions").Open();
                foreach (var e in pair.Value.Exceptions) j.Num(e.Key, e.Value);
                j.Close();
                j.Close();
            }
            j.Close();
            j.Key("treeVariants").Open();
            foreach (var pair in ctx.Furniture.TreeVariants.OrderByDescending(p => p.Value)) j.Num(pair.Key, pair.Value);
            j.Close();
            j.Key("treeHeights"); Dist(j, ctx.Furniture.TreeHeights);

            j.Key("landmarks").Open();
            foreach (var pair in ctx.Landmarks.Placed) j.Num(pair.Key, pair.Value);
            j.Key("notes").Array();
            foreach (var n in ctx.Landmarks.Notes) j.Item(n);
            j.CloseArray();
            j.Close();
            j.Key("landmarkRecords").Array();
            foreach (var l in data.landmarks ?? Array.Empty<Landmark>())
            {
                j.Open();
                j.Str("kind", l.kind); j.Str("name", l.name); j.Num("x", l.x); j.Num("z", l.z); j.Str("footprintId", l.footprintId); j.Str("osmId", l.osmId);
                var fit = fits.FirstOrDefault(f => f.Id == l.footprintId);
                if (!string.IsNullOrEmpty(l.footprintId)) { j.Str("fittedRole", fit.Role); j.Str("fittedVariant", fit.Variant); j.Num("fittedIou", fit.Iou); }
                j.Close();
            }
            j.CloseArray();

            var statics = ctx.Statics;
            j.Key("triangles").Open();
            foreach (StaticModels.Layer layer in Enum.GetValues(typeof(StaticModels.Layer))) j.Num(layer.ToString(), statics.Triangles(layer));
            j.Close();
            var renderers = ctx.Root.GetComponentsInChildren<MeshRenderer>(true);
            j.Num("renderers", renderers.Length);
            j.Num("trianglesAllRenderers", renderers.Sum(r => (long)(r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3)));
            var lod = ctx.Root.GetComponent<DistanceLod>();
            j.Key("views").Array();
            foreach (var view in WorldRenders.Views(ctx, data))
            {
                lod.Apply(view.Eye);
                var on = renderers.Where(r => r.enabled).ToList();
                j.Open();
                j.Str("name", view.Name);
                j.Num("renderersAfterLod", on.Count);
                j.Num("trianglesAfterLod", on.Sum(r => (long)(r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3)));
                j.Close();
            }
            j.CloseArray();

            var index = ctx.Instances;
            j.Key("instances").Open();
            j.Num("total", index.Count);
            foreach (var g in index.All.GroupBy(i => i.Kind + ":" + i.Class)) j.Num(g.Key, g.Count());
            j.Num("withCollider", index.All.Count(i => i.Collider != null));
            j.Close();
            j.Key("variantUses").Open();
            foreach (var pair in statics.Uses.OrderByDescending(p => p.Value)) j.Num(pair.Key, pair.Value);
            j.Close();
            j.Close();
            return j.ToString();
        }

        static void Dist(Json j, List<float> values)
        {
            j.Open();
            j.Num("count", values.Count);
            if (values.Count > 0)
            {
                var s = values.OrderBy(v => v).ToList();
                float P(float q) => s[Mathf.Clamp(Mathf.RoundToInt(q * (s.Count - 1)), 0, s.Count - 1)];
                j.Num("min", s[0]); j.Num("p10", P(0.1f)); j.Num("p25", P(0.25f)); j.Num("median", P(0.5f)); j.Num("p75", P(0.75f)); j.Num("p90", P(0.9f)); j.Num("max", s[s.Count - 1]); j.Num("mean", s.Average());
            }
            j.Close();
        }

        sealed class Json
        {
            readonly StringBuilder sb = new StringBuilder();
            readonly Stack<bool> first = new Stack<bool>();
            bool pendingKey;

            void Sep()
            {
                if (pendingKey) { pendingKey = false; return; }
                if (first.Count == 0) return;
                if (!first.Peek()) sb.Append(',');
                first.Pop();
                first.Push(false);
            }

            static string Esc(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
            public Json Key(string k) { Sep(); sb.Append(Esc(k)).Append(':'); pendingKey = true; return this; }
            public Json Open() { Sep(); sb.Append('{'); first.Push(true); return this; }
            public void Close() { sb.Append('}'); first.Pop(); }
            public Json Array() { Sep(); sb.Append('['); first.Push(true); return this; }
            public void CloseArray() { sb.Append(']'); first.Pop(); }
            public void Item(string v) { Sep(); sb.Append(Esc(v)); }
            public void Str(string k, string v) { Key(k); Sep(); sb.Append(Esc(v)); }
            public void Num(string k, double v) { Key(k); Sep(); sb.Append(double.IsNaN(v) || double.IsInfinity(v) ? "null" : Math.Round(v, 4).ToString(CultureInfo.InvariantCulture)); }
            public override string ToString() => sb.ToString();
        }
    }
}
