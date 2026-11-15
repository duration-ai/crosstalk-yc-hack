using System;
using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Look
{
    // Recolors Polyfork models to the Speed Dial palette through their own color knobs. Each
    // knob's default is the exact COLOR_0 value Polyfork bakes into the GLB, so a vertex color is
    // matched to the nearest knob default of its model and replaced with that knob's Speed Dial
    // color. The table lives in Resources/Polyfork/palette.json, one entry per model id.
    public static class PolyforkPalette
    {
        const float MatchDistance = 0.15f;

        [Serializable]
        class Manifest
        {
            public Entry[] models;
        }

        [Serializable]
        class Entry
        {
            public string id;
            public Knob[] knobs;
        }

        [Serializable]
        class Knob
        {
            public string knob;
            public string from;
            public string to;
        }

        static Dictionary<string, (Color from, Color to)[]> table;

        public static void Apply(GlbAsset asset)
        {
            if (table == null) Load();
            if (!table.TryGetValue(asset.Id, out var knobs))
            {
                Debug.LogWarning("[Look] no Speed Dial palette for " + asset.Id + "; it keeps stock colors");
                return;
            }

            var cache = new Dictionary<Color, Color>();
            int missed = 0;
            foreach (var mesh in asset.Meshes)
            {
                if (mesh == null) continue;
                var colors = mesh.colors;
                for (int i = 0; i < colors.Length; i++)
                {
                    var c = colors[i];
                    if (!cache.TryGetValue(c, out var mapped))
                    {
                        mapped = Remap(c, knobs, out bool hit);
                        if (!hit) missed++;
                        cache[c] = mapped;
                    }
                    colors[i] = mapped;
                }
                mesh.colors = colors;
            }
            if (missed > 0) Debug.LogWarning($"[Look] {asset.Id}: {missed} colors matched no knob and kept stock values");
        }

        static Color Remap(Color linear, (Color from, Color to)[] knobs, out bool hit)
        {
            var c = linear.gamma;
            float best = MatchDistance;
            hit = false;
            var result = linear;
            foreach (var (from, to) in knobs)
            {
                float d = Vector3.Distance(new Vector3(c.r, c.g, c.b), new Vector3(from.r, from.g, from.b));
                if (d >= best) continue;
                best = d;
                hit = true;
                result = new Color(to.r, to.g, to.b, 1f).linear;
                result.a = linear.a;
            }
            return result;
        }

        static void Load()
        {
            table = new Dictionary<string, (Color, Color)[]>();
            var file = Resources.Load<TextAsset>("Polyfork/palette");
            if (file == null) return;
            foreach (var entry in JsonUtility.FromJson<Manifest>(file.text).models)
            {
                var knobs = new (Color, Color)[entry.knobs.Length];
                for (int i = 0; i < knobs.Length; i++)
                {
                    ColorUtility.TryParseHtmlString(entry.knobs[i].from, out var from);
                    ColorUtility.TryParseHtmlString(entry.knobs[i].to, out var to);
                    knobs[i] = (from, to);
                }
                table[entry.id] = knobs;
            }
        }
    }
}
