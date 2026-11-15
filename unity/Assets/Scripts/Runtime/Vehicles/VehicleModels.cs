using System.Collections.Generic;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Builds a vehicle's visual from its spec: a Polyfork GLB repainted with the spec's colour map (the
    // remix colour knobs, applied per vertex the way Polyfork's own module does), lamp zones marked to
    // glow at night, plus code-built parts; or a code-built model. Repainted meshes are cached per
    // type and paint, so every hatchback of one colour shares one mesh.
    public static class VehicleModels
    {
        static readonly Dictionary<string, GlbAsset> assets = new Dictionary<string, GlbAsset>();
        static readonly Dictionary<string, Mesh> painted = new Dictionary<string, Mesh>();

        public static GlbAsset Asset(string resource)
        {
            if (assets.TryGetValue(resource, out var asset)) return asset;
            var bytes = Resources.Load<TextAsset>(resource);
            asset = bytes == null ? null : GlbAsset.Load(resource.Substring(resource.LastIndexOf('/') + 1), bytes.bytes);
            assets[resource] = asset;
            return asset;
        }

        static Color32 Srgb(Color linear)
        {
            var g = linear.gamma;
            return new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(g.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(g.g) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(g.b) * 255f), 255);
        }

        static Color32 Parse(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static bool Near(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) <= 2 && Mathf.Abs(a.g - b.g) <= 2 && Mathf.Abs(a.b - b.b) <= 2;

        public static Mesh Repaint(Mesh source, string key, IDictionary<string, string> recolor, IDictionary<string, float> lamps)
        {
            key = key + "/" + source.name + "/" + source.GetInstanceID();
            if (painted.TryGetValue(key, out var mesh)) return mesh;
            var from = new List<(Color32 from, Color to)>();
            foreach (var pair in recolor) from.Add((Parse(pair.Key), LowPoly.Hex(pair.Value)));
            var glow = new List<(Color32 from, float glow)>();
            if (lamps != null) foreach (var pair in lamps) glow.Add((Parse(pair.Key), pair.Value));

            var colors = source.colors;
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                var s = Srgb(c);
                foreach (var (f, to) in from)
                    if (Near(s, f)) { c = new Color(to.r, to.g, to.b, c.a); break; }
                foreach (var (f, g) in glow)
                    if (Near(s, f)) { c.a = 1f - g; break; }
                colors[i] = c;
            }
            mesh = Object.Instantiate(source);
            mesh.name = source.name + " " + key;
            mesh.colors = colors;
            painted[key] = mesh;
            return mesh;
        }

        static void RepaintAll(GameObject visual, string key, IDictionary<string, string> recolor, IDictionary<string, float> lamps)
        {
            foreach (var mf in visual.GetComponentsInChildren<MeshFilter>()) mf.sharedMesh = Repaint(mf.sharedMesh, key, recolor, lamps);
            foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.sharedMesh = Repaint(smr.sharedMesh, key, recolor, lamps);
        }

        public static (GameObject visual, Bounds bounds) Build(VehicleSpec spec, int paint, Material material)
        {
            if (spec.Source == ModelSource.Procedural)
            {
                switch (spec.Id)
                {
                    case VehicleTypes.Cybertruck: return ProceduralModels.Cybertruck(material);
                    case VehicleTypes.Zoox: return ProceduralModels.Zoox(material);
                    case VehicleTypes.ShoppingCart: return ProceduralModels.ShoppingCart(material);
                    case VehicleTypes.FlyingCar: return ProceduralModels.FlyingCar(material);
                }
            }

            var asset = Asset(spec.Resource);
            var recolor = new Dictionary<string, string>(spec.Recolor);
            if (spec.PaintKey != null && spec.Paints.Length > 0) recolor[spec.PaintKey] = spec.Paints[Mathf.Abs(paint) % spec.Paints.Length];
            string key = spec.Id + "#" + (spec.Paints.Length > 0 ? Mathf.Abs(paint) % spec.Paints.Length : 0);

            if (spec.Id == VehicleTypes.TThird) return Train(asset, material, key, spec);

            var visual = asset.Instantiate(material);
            visual.name = spec.Id;
            RepaintAll(visual, key, recolor, spec.Lamps);
            var bounds = asset.ComputeBounds();
            if (spec.Robotaxi) ProceduralModels.RobotaxiSensors(visual.transform, material, bounds.max.y - 0.02f, bounds.center.z - bounds.size.z * 0.05f, bounds.extents.x, bounds.extents.z, bounds.min.y + bounds.size.y * 0.42f);
            if (spec.Id == VehicleTypes.BayWheels) ProceduralModels.EbikeKit(visual.transform, material);
            if (spec.Robotaxi) bounds.Encapsulate(new Vector3(bounds.center.x, bounds.max.y + 0.6f, bounds.center.z));
            if (!spec.TwoWheeler) AddBrakeGlow(visual, material, spec, bounds);
            return (visual, bounds);
        }

        static readonly Dictionary<Mesh, Mesh> brakeGlow = new Dictionary<Mesh, Mesh>();

        // Brake lights that come on while braking (CarController toggles every "brake-glow" child): the
        // model's own red tail-lamp facets, copied a little bigger and proud of the body in a bright
        // full-glow red, so they read in daylight and bloom at night like Forza Horizon's tail lamps.
        static void AddBrakeGlow(GameObject visual, Material material, VehicleSpec spec, Bounds bounds)
        {
            var reds = new List<Color32>();
            foreach (var pair in spec.Lamps)
            {
                var c = Parse(pair.Key);
                if (c.r > 90 && c.r > c.g * 1.6f && c.r > c.b * 1.6f) reds.Add(c);
            }
            if (reds.Count == 0) return;
            foreach (var mf in visual.GetComponentsInChildren<MeshFilter>())
            {
                var source = mf.sharedMesh;
                if (source == null) continue;
                if (!brakeGlow.TryGetValue(source, out var glow))
                {
                    glow = BuildBrakeGlow(source, reds, mf.transform, visual.transform, bounds);
                    brakeGlow[source] = glow;
                }
                if (glow == null) continue;
                var go = new GameObject("brake-glow");
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = glow;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                go.SetActive(false);
            }
        }

        static Mesh BuildBrakeGlow(Mesh source, List<Color32> reds, Transform node, Transform root, Bounds bounds)
        {
            var colors = source.colors;
            var vertices = source.vertices;
            var triangles = source.triangles;
            if (colors.Length != vertices.Length) return null;
            var lit = LowPoly.Hex("#ff2a1f", 1f);
            var lp = new LowPoly();
            int count = 0;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                bool red = true;
                for (int k = 0; k < 3 && red; k++)
                {
                    var c = Srgb(colors[triangles[t + k]]);
                    bool match = false;
                    foreach (var r in reds) if (Near(c, r)) { match = true; break; }
                    red = match;
                }
                if (!red) continue;
                var a = vertices[triangles[t]];
                var b = vertices[triangles[t + 1]];
                var d = vertices[triangles[t + 2]];
                var centre = (a + b + d) / 3f;
                var world = root.InverseTransformPoint(node.TransformPoint(centre));
                if (world.z > bounds.center.z) continue;
                var push = node.InverseTransformVector(root.TransformVector(new Vector3(0f, 0f, -0.025f)));
                lp.Tri(centre + (a - centre) * 1.12f + push, centre + (b - centre) * 1.12f + push, centre + (d - centre) * 1.12f + push, lit);
                count++;
            }
            return count == 0 ? null : lp.ToMesh(source.name + " brake-glow");
        }

        // Two light-rail cars coupled back to back, as the T Third runs them.
        static (GameObject, Bounds) Train(GlbAsset asset, Material material, string key, VehicleSpec spec)
        {
            var bounds = asset.ComputeBounds();
            var root = new GameObject(spec.Id);
            float gap = 0.6f;
            for (int i = 0; i < 2; i++)
            {
                var car = asset.Instantiate(material, root.transform);
                RepaintAll(car, key, spec.Recolor, spec.Lamps);
                car.name = "car-" + (i + 1);
                float z = i == 0 ? -bounds.center.z + (bounds.size.z + gap) * 0.5f : -(-bounds.center.z + (bounds.size.z + gap) * 0.5f);
                car.transform.localPosition = new Vector3(0f, 0f, z);
                car.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 0f : 180f, 0f);
            }
            var total = new Bounds(new Vector3(0f, bounds.center.y, 0f), new Vector3(bounds.size.x, bounds.size.y, bounds.size.z * 2f + gap));
            return (root, total);
        }
    }
}
