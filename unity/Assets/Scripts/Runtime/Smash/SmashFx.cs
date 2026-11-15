using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Cheap pooled effects in the world's style: faceted debris chips, faceted smoke puffs that grow
    // and shrink away (no transparency), and a playful hydrant water spout. Each is one particle
    // system drawing low-poly meshes with the shared flat vertex-colour material: one draw call each.
    // Tuned as accents (fewer, smaller, shorter-lived) and scaled by EffectsSettings.Intensity:
    // counts by the intensity, sizes by its square root.
    public sealed class SmashFx
    {
        public static readonly Color SmokeLight = new Color(0.96f, 0.96f, 0.97f);
        public static readonly Color SmokeGrey = new Color(0.62f, 0.64f, 0.68f);
        public static readonly Color SmokeDark = new Color(0.26f, 0.27f, 0.3f);
        static readonly Color WaterBlue = new Color(0.36f, 0.78f, 0.95f);
        static readonly Color WaterFoam = new Color(0.86f, 0.96f, 1f);

        readonly ParticleSystem debris;
        readonly ParticleSystem smoke;
        readonly ParticleSystem water;
        readonly List<(Vector3 position, float until)> spouts = new List<(Vector3, float)>();

        public SmashFx(Transform parent, Material material)
        {
            debris = Make(parent, "SmashDebris", Tetra(), material, 1.6f, 900, true, true,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            smoke = Make(parent, "SmashSmoke", Icosahedron(), material, -0.08f, 500, false, false,
                new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            water = Make(parent, "SmashWater", Tetra(), material, 1.1f, 700, true, false,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f)));
        }

        public int LiveParticles => debris.particleCount + smoke.particleCount + water.particleCount;

        static ParticleSystem Make(Transform parent, string name, Mesh mesh, Material material, float gravity, int max, bool collide, bool shadows, AnimationCurve size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startRotation3D = true;
            main.startSize3D = false;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);
            if (collide)
            {
                var floor = new GameObject(name + " Floor").transform;
                floor.SetParent(go.transform, false);
                floor.position = new Vector3(0f, 0.05f, 0f);
                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                collision.SetPlane(0, floor);
                collision.dampen = 0.45f;
                collision.bounce = 0.35f;
                collision.lifetimeLoss = 0f;
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = material;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
            renderer.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Vector3 RandomRotation() => new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);

        public void Debris(Vector3 point, Vector3 velocity, IReadOnlyList<Color> colors, int count, float size = 0.28f)
        {
            count = EffectsSettings.Count(count * 0.5f);
            size *= 0.6f * EffectsSettings.Scale;
            for (int i = 0; i < count; i++)
            {
                var v = velocity * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 4f + Vector3.up * Random.Range(2.5f, 6.5f);
                debris.Emit(new ParticleSystem.EmitParams
                {
                    position = point + Random.insideUnitSphere * 0.4f,
                    velocity = v,
                    startColor = colors.Count == 0 ? Color.white : colors[Random.Range(0, colors.Count)],
                    startSize = size * Random.Range(0.5f, 1.3f),
                    startLifetime = Random.Range(0.9f, 1.4f),
                    rotation3D = RandomRotation(),
                    angularVelocity3D = Random.insideUnitSphere * 540f,
                }, 1);
            }
        }

        public void Smoke(Vector3 point, Color color, float size, int count = 1, float rise = 1.2f, Vector3 drift = default)
            => EmitSmoke(point, color, size, EffectsSettings.Count(count), rise, drift);

        void EmitSmoke(Vector3 point, Color color, float size, int count, float rise, Vector3 drift)
        {
            size *= 0.55f * EffectsSettings.Scale;
            for (int i = 0; i < count; i++)
            {
                var tint = Color.Lerp(color, Color.white, Random.Range(0f, 0.12f));
                smoke.Emit(new ParticleSystem.EmitParams
                {
                    position = point + Random.insideUnitSphere * size * 0.3f,
                    velocity = drift + Vector3.up * rise * Random.Range(0.7f, 1.3f) + Random.insideUnitSphere * 0.5f,
                    startColor = tint,
                    startSize = size * Random.Range(0.75f, 1.25f),
                    startLifetime = Random.Range(0.5f, 0.9f),
                    rotation3D = RandomRotation(),
                    angularVelocity3D = Random.insideUnitSphere * 60f,
                }, 1);
            }
        }

        // A puff ring for a big hit or a piece vanishing.
        public void Poof(Vector3 point, float radius, Color color, int count = 8)
        {
            count = EffectsSettings.Count(count * 0.5f);
            radius *= 0.6f;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var dir = new Vector3(Mathf.Cos(a), 0.25f, Mathf.Sin(a));
                EmitSmoke(point + dir * radius * 0.4f, color, radius * 0.7f, 1, 0.8f, dir * radius * 2.2f);
            }
        }

        public void Spout(Vector3 position, float seconds) => spouts.Add((position, Time.time + seconds));

        public void Tick()
        {
            for (int i = spouts.Count - 1; i >= 0; i--)
            {
                var (position, until) = spouts[i];
                if (Time.time > until)
                {
                    spouts.RemoveAt(i);
                    continue;
                }
                float strength = Mathf.Clamp01((until - Time.time) / 2f);
                int n = Mathf.CeilToInt(3f * strength * EffectsSettings.Intensity);
                for (int k = 0; k < n; k++)
                {
                    water.Emit(new ParticleSystem.EmitParams
                    {
                        position = position + Random.insideUnitSphere * 0.08f,
                        velocity = new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(8.5f, 11f) * (0.5f + 0.5f * strength), Random.Range(-0.9f, 0.9f)),
                        startColor = Random.value < 0.3f ? WaterFoam : WaterBlue,
                        startSize = Random.Range(0.08f, 0.15f) * EffectsSettings.Scale,
                        startLifetime = Random.Range(0.9f, 1.3f),
                        rotation3D = RandomRotation(),
                        angularVelocity3D = Random.insideUnitSphere * 360f,
                    }, 1);
                }
            }
        }

        public static Mesh Tetra()
        {
            var v = new[] { new Vector3(0f, 0.6f, 0f), new Vector3(-0.5f, -0.3f, -0.35f), new Vector3(0.5f, -0.3f, -0.35f), new Vector3(0f, -0.3f, 0.55f) };
            return Faceted("chip", v, new[] { 0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3 });
        }

        public static Mesh Icosahedron()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * 0.5f;
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            return Faceted("puff", v, f);
        }

        // Unshared vertices per triangle, white vertex colour: the particle colour tints it. The colours
    // must be 8-bit: a mesh with float colours garbles particle colours in the mesh render path.
        static Mesh Faceted(string name, Vector3[] v, int[] f)
        {
            var verts = new Vector3[f.Length];
            var tris = new int[f.Length];
            var colors = new Color32[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                verts[i] = v[f[i]];
                tris[i] = i;
                colors[i] = new Color32(255, 255, 255, 255);
            }
            var mesh = new Mesh { name = name, vertices = verts, triangles = tris, colors32 = colors };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly Dictionary<Mesh, Color[]> palettes = new Dictionary<Mesh, Color[]>();

        // The few most common vertex colours of a mesh, for debris that matches what broke.
        public static IReadOnlyList<Color> PaletteOf(Mesh mesh)
        {
            if (mesh == null) return System.Array.Empty<Color>();
            if (palettes.TryGetValue(mesh, out var cached)) return cached;
            var counts = new Dictionary<Color32, int>();
            var colors = mesh.colors32;
            for (int i = 0; i < colors.Length; i += 3)
            {
                counts.TryGetValue(colors[i], out int n);
                counts[colors[i]] = n + 1;
            }
            var sorted = new List<KeyValuePair<Color32, int>>(counts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var result = new Color[Mathf.Min(3, sorted.Count)];
            for (int i = 0; i < result.Length; i++) result[i] = sorted[i].Key;
            palettes[mesh] = result;
            return result;
        }
    }
}
