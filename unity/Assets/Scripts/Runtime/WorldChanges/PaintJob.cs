using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    [Serializable]
    public class KnobRecolor
    {
        public string from;
        public string to;
    }

    [Serializable]
    public class ModelKnobs
    {
        public string id;
        public string[] paint;
        public KnobRecolor[] @fixed;
    }

    [Serializable]
    public class KnobTable
    {
        public ModelKnobs[] models;
    }

    // Recolors a Polyfork instance through the model's own color knobs. Polyfork bakes each knob's
    // albedo into COLOR_0, so the vertices of a knob are exactly the ones carrying its default color
    // (Resources/SpeedDial/knobs.json lists the paint knobs per model), as PolyforkPalette has
    // already mapped it to the Speed Dial palette. The instance gets its own
    // copies of the meshes, so painting one car never repaints the others.
    public sealed class PaintJob : MonoBehaviour
    {
        static KnobTable table;

        sealed class Part
        {
            public Mesh Mesh;
            public Color[] Original;
            public Color[] Current;
            public List<int> Paint = new List<int>();
            public List<float> Shade = new List<float>();
        }

        readonly List<Part> parts = new List<Part>();
        Color? baseColor;
        Color? overrideColor;

        public static Color Linear(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c.linear;
        }

        public static ModelKnobs Knobs(string modelId)
        {
            if (table == null) table = JsonUtility.FromJson<KnobTable>(Resources.Load<TextAsset>("SpeedDial/knobs").text);
            foreach (var m in table.models)
                if (m.id == modelId) return m;
            return null;
        }

        static bool Near(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.006f && Mathf.Abs(a.g - b.g) < 0.006f && Mathf.Abs(a.b - b.b) < 0.006f;

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        public static PaintJob For(GameObject root, string modelId)
        {
            var job = root.GetComponent<PaintJob>();
            if (job != null) return job;
            job = root.AddComponent<PaintJob>();
            var knobs = Knobs(modelId);
            var paint = new List<Color>();
            if (knobs?.paint != null) foreach (var hex in knobs.paint) paint.Add(Look.PolyforkPalette.Mapped(modelId, Linear(hex)));
            var fixes = new List<(Color from, Color to)>();
            if (knobs?.@fixed != null) foreach (var f in knobs.@fixed) fixes.Add((Look.PolyforkPalette.Mapped(modelId, Linear(f.from)), Linear(f.to)));
            float baseLuma = paint.Count > 0 ? Mathf.Max(Luma(paint[0]), 1e-4f) : 1f;

            void Take(Mesh shared, Action<Mesh> assign)
            {
                if (shared == null) return;
                var mesh = Instantiate(shared);
                mesh.name = shared.name;
                assign(mesh);
                var part = new Part { Mesh = mesh, Original = mesh.colors };
                if (part.Original.Length == 0) return;
                part.Current = (Color[])part.Original.Clone();
                for (int i = 0; i < part.Original.Length; i++)
                {
                    var c = part.Original[i];
                    foreach (var (from, to) in fixes)
                        if (Near(c, from)) part.Current[i] = new Color(to.r, to.g, to.b, c.a);
                    foreach (var p in paint)
                    {
                        if (!Near(c, p)) continue;
                        part.Paint.Add(i);
                        part.Shade.Add(Luma(p) / baseLuma);
                        break;
                    }
                }
                part.Original = (Color[])part.Current.Clone();
                mesh.colors = part.Current;
                job.parts.Add(part);
            }

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Take(mf.sharedMesh, m => mf.sharedMesh = m);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Take(smr.sharedMesh, m => smr.sharedMesh = m);
            return job;
        }

        public bool HasPaint
        {
            get
            {
                foreach (var p in parts) if (p.Paint.Count > 0) return true;
                return false;
            }
        }

        public Color? Base => baseColor;

        public void SetBase(Color? linear)
        {
            baseColor = linear;
            Apply();
        }

        public void SetOverride(Color? linear)
        {
            overrideColor = linear;
            Apply();
        }

        void Apply()
        {
            var target = overrideColor ?? baseColor;
            foreach (var part in parts)
            {
                if (part.Paint.Count == 0) continue;
                for (int k = 0; k < part.Paint.Count; k++)
                {
                    int i = part.Paint[k];
                    var o = part.Original[i];
                    part.Current[i] = target.HasValue ? new Color(target.Value.r * part.Shade[k], target.Value.g * part.Shade[k], target.Value.b * part.Shade[k], o.a) : o;
                }
                part.Mesh.colors = part.Current;
            }
        }

        void OnDestroy()
        {
            foreach (var part in parts) Destroy(part.Mesh);
        }
    }
}
