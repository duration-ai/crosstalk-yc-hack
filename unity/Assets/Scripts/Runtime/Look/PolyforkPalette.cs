using System;
using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Look
{
    // Recolors Polyfork models to the Speed Dial palette through their own color knobs. Each
    // knob's default is the exact COLOR_0 value Polyfork bakes into the GLB, so a vertex color is
    // matched to the nearest knob default of its model and replaced with that knob's Speed Dial
    // color. The table lives in Resources/Polyfork/palette.json, one entry per model id. Models
    // not in it (the knob-built variants in variants.json, baked in Speed Dial colors) are left as is.
    //
    // Night: a model's night zones (Polyfork's night block, keyed by knob) glow after dark. The glow
    // is baked into vertex alpha as 1 - intensity, so alpha 1 never glows and alpha 0 glows fully;
    // the shared FlatVertexColor shader adds albedo * (1 - alpha) * _DogpatchNight.
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
            public Night[] night;
        }

        [Serializable]
        class Night
        {
            public string zone;
            public float intensity;
        }

        [Serializable]
        class Knob
        {
            public string knob;
            public string from;
            public string to;
        }

        static Dictionary<string, (Color from, Color to, float alpha)[]> table;

        public static void Apply(GlbAsset asset)
        {
            if (table == null) Load();
            if (!table.TryGetValue(asset.Id, out var knobs)) return;

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

        static Color Remap(Color linear, (Color from, Color to, float alpha)[] knobs, out bool hit)
        {
            var c = linear.gamma;
            float best = MatchDistance;
            hit = false;
            var result = linear;
            foreach (var (from, to, alpha) in knobs)
            {
                float d = Vector3.Distance(new Vector3(c.r, c.g, c.b), new Vector3(from.r, from.g, from.b));
                if (d >= best) continue;
                best = d;
                hit = true;
                result = new Color(to.r, to.g, to.b, 1f).linear;
                result.a = alpha;
            }
            return result;
        }

        static void Load()
        {
            table = new Dictionary<string, (Color, Color, float)[]>();
            var file = Resources.Load<TextAsset>("Polyfork/palette");
            if (file == null) return;
            foreach (var entry in JsonUtility.FromJson<Manifest>(file.text).models)
            {
                var glow = new Dictionary<string, float>();
                if (entry.night != null)
                    foreach (var zone in entry.night) glow[zone.zone] = Mathf.Clamp01(zone.intensity);
                var knobs = new (Color, Color, float)[entry.knobs.Length];
                for (int i = 0; i < knobs.Length; i++)
                {
                    ColorUtility.TryParseHtmlString(entry.knobs[i].from, out var from);
                    ColorUtility.TryParseHtmlString(entry.knobs[i].to, out var to);
                    knobs[i] = (from, to, glow.TryGetValue(entry.knobs[i].knob, out var g) ? 1f - g : 1f);
                }
                table[entry.id] = knobs;
            }
        }
    }
}
