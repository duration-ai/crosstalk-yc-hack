using System.Collections.Generic;
using Dogpatch.Smash;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // The start ring's look, in the spirit of the old Need for Speed race markers and GTA's coronas:
    // a tall see-through yellow light pillar with glowing edges and a soft outer haze that reads from
    // 200 m+ over the rooftops, halos climbing it, and faceted motes spiralling up. The floor is an
    // overlay, not a disc: thin glowing lines (outer ring, inner ring, a chevron ring and a six-point
    // glyph spinning against each other) drawn additively over the road so asphalt, paint and
    // crosswalks show through. Close up the pillar fades so the middle of the screen stays clear.
    // Look only: the ring's position, radius and mission logic stay with RaceDirector.
    public sealed class StartRingFx : MonoBehaviour
    {
        static readonly Color Yellow = PaintJob.Linear("#FFCB1F");
        static readonly Color Cream = PaintJob.Linear("#FCE9A8");
        static readonly Color White = PaintJob.Linear("#F7F8F9");
        static readonly int TintId = Shader.PropertyToID("_Tint");

        const float PillarHeight = 90f;
        const float FloorY = 0.06f;
        const float NearFrom = 40f, NearTo = 10f;
        const float MotesWithin = 120f;

        float radius;
        Renderer pillar, haze, core, floor, chevrons, glyph;
        readonly Renderer[] halos = new Renderer[3];
        MaterialPropertyBlock block;
        ParticleSystem motes;
        float spin, glyphSpin, climb, moteDebt;

        public static void Attach(GameObject ring, float radius, Material flat)
        {
            ring.AddComponent<StartRingFx>().Build(radius, flat);
        }

        void Build(float r, Material flat)
        {
            radius = r;
            block = new MaterialPropertyBlock();
            var shader = Resources.Load<Shader>("Shaders/FxGlow");
            var beam = new Material(shader) { name = "Start ring beam" };
            beam.SetFloat("_Rim", 1f);
            var lines = new Material(shader) { name = "Start ring lines" };

            var g = new Strip();
            g.Tube(r * 0.55f, 0f, PillarHeight, WithAlpha(Yellow, 0.36f), WithAlpha(Yellow, 0f), 16);
            pillar = Part(g, "Pillar", beam);
            g = new Strip();
            g.Tube(r * 0.8f, 0f, PillarHeight * 0.5f, WithAlpha(Yellow, 0.12f), WithAlpha(Yellow, 0f), 16);
            haze = Part(g, "Haze", beam);
            g = new Strip();
            g.Tube(r * 0.2f, 0f, PillarHeight * 0.66f, WithAlpha(White, 0.35f), WithAlpha(Cream, 0f), 10);
            core = Part(g, "Core", beam);
            for (int i = 0; i < halos.Length; i++)
            {
                g = new Strip();
                g.Annulus(r * 0.6f, r * 0.66f, 0f, WithAlpha(White, 0.7f), WithAlpha(White, 0.7f), 32);
                g.Annulus(r * 0.66f, r * 0.8f, 0f, WithAlpha(Yellow, 0.35f), WithAlpha(Yellow, 0f), 32);
                halos[i] = Part(g, "Halo", lines);
            }

            g = new Strip();
            g.Annulus(r - 0.25f, r, FloorY, WithAlpha(Yellow, 0.95f), WithAlpha(Yellow, 0.95f), 64);
            g.Annulus(r, r + 1.3f, FloorY, WithAlpha(Yellow, 0.35f), WithAlpha(Yellow, 0f), 64);
            g.Annulus(r - 1.1f, r - 0.25f, FloorY, WithAlpha(Yellow, 0f), WithAlpha(Yellow, 0.25f), 64);
            g.Annulus(r * 0.6f - 0.06f, r * 0.6f + 0.06f, FloorY, WithAlpha(White, 0.8f), WithAlpha(White, 0.8f), 48);
            floor = Part(g, "Floor", lines);

            g = new Strip();
            const int count = 12;
            float mid = r * 0.8f;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var c = radial * mid + Vector3.up * FloorY;
                var tip = c + tangent * 0.5f;
                g.Line(c - tangent * 0.5f + radial * r * 0.1f, tip, 0.2f, WithAlpha(Yellow, 0.9f));
                g.Line(c - tangent * 0.5f - radial * r * 0.1f, tip, 0.2f, WithAlpha(Yellow, 0.9f));
            }
            chevrons = Part(g, "Chevrons", lines);

            g = new Strip();
            for (int k = 0; k < 2; k++)
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 2f / 3f + k / 3f) * Mathf.PI, b = a + Mathf.PI * 2f / 3f;
                    var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.48f + Vector3.up * FloorY;
                    var q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * r * 0.48f + Vector3.up * FloorY;
                    g.Line(p, q, 0.14f, WithAlpha(k == 0 ? Yellow : White, 0.85f));
                }
            glyph = Part(g, "Glyph", lines);

            motes = Motes(flat);
        }

        static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        Renderer Part(Strip g, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = g.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        ParticleSystem Motes(Material flat)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = true;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = SmashFx.Tetra();
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = flat;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void Update()
        {
            var cam = Camera.main;
            float distance = cam == null ? 0f : Vector2.Distance(new Vector2(cam.transform.position.x, cam.transform.position.z), new Vector2(transform.position.x, transform.position.z));
            float near = Mathf.Clamp01((NearFrom - distance) / (NearFrom - NearTo));
            float dt = Time.deltaTime;
            float t = Time.time;

            float pulse = 1f + Mathf.Sin(t * Mathf.PI * 1.6f) * 0.05f;
            pillar.transform.localScale = new Vector3(pulse, 1f, pulse);
            core.transform.localScale = new Vector3(pulse, 1f, pulse);
            haze.transform.localScale = new Vector3(2f - pulse, 1f, 2f - pulse);
            float fade = Mathf.Lerp(1f, 0.35f, near);
            SetAlpha(pillar, fade);
            SetAlpha(haze, fade);
            SetAlpha(core, fade);

            climb += dt * (0.25f + near * 0.25f);
            for (int i = 0; i < halos.Length; i++)
            {
                float u = Mathf.Repeat(climb + i / (float)halos.Length, 1f);
                halos[i].transform.localPosition = Vector3.up * (u * u * 24f + 0.1f);
                float s = 1f + u * 0.4f;
                halos[i].transform.localScale = new Vector3(s, 1f, s);
                SetAlpha(halos[i], (1f - u) * 0.6f);
            }

            spin += dt * (25f + near * 40f);
            glyphSpin -= dt * (15f + near * 20f);
            chevrons.transform.localRotation = Quaternion.Euler(0f, spin, 0f);
            glyph.transform.localRotation = Quaternion.Euler(0f, glyphSpin, 0f);
            float swell = 1f + near * 0.04f + Mathf.Sin(t * Mathf.PI * 1.6f) * 0.015f;
            glyph.transform.localScale = new Vector3(swell, 1f, swell);
            float beat = 0.75f + 0.25f * Mathf.Sin(t * Mathf.PI * 1.6f);
            SetAlpha(floor, beat);
            SetAlpha(chevrons, 0.85f + near * 0.15f);
            SetAlpha(glyph, beat);

            if (distance > MotesWithin) return;
            moteDebt += dt * (4f + near * 10f);
            for (; moteDebt >= 1f; moteDebt -= 1f)
            {
                float a = Random.value * Mathf.PI * 2f;
                var at = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Random.Range(0.3f, 0.8f) + Vector3.up * 0.2f;
                motes.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Vector3.up * Random.Range(2.5f, 3.5f) + new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f,
                    startColor = Random.value < 0.7f ? Yellow : White,
                    startSize = Random.Range(0.22f, 0.36f),
                    startLifetime = Random.Range(2.4f, 3.2f),
                    rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f),
                    angularVelocity3D = Random.insideUnitSphere * 220f,
                }, 1);
            }
        }

        void SetAlpha(Renderer r, float alpha)
        {
            block.SetColor(TintId, new Color(1f, 1f, 1f, alpha));
            r.SetPropertyBlock(block);
        }

        // A tiny builder with per-vertex colour and normals, for the see-through gradients and the
        // pillar's rim glow that the shared MeshBuilder can't express.
        sealed class Strip
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Color> c = new List<Color>();
            readonly List<int> t = new List<int>();

            static Vector3 P(float angle, float r, float y) => new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
            static Vector3 Out(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            void Vertex(Vector3 p, Vector3 normal, Color color)
            {
                v.Add(p);
                n.Add(normal);
                c.Add(color);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 na, Vector3 nb, Color ca, Color cb, Color cd, Color ce)
            {
                int i = v.Count;
                Vertex(a, na, ca);
                Vertex(b, nb, cb);
                Vertex(d, nb, cd);
                Vertex(e, na, ce);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            public void Annulus(float inner, float outer, float y, Color innerColor, Color outerColor, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    Quad(P(a0, inner, y), P(a1, inner, y), P(a1, outer, y), P(a0, outer, y), Vector3.up, Vector3.up, innerColor, innerColor, outerColor, outerColor);
                }
            }

            public void Tube(float r, float y0, float y1, Color bottom, Color top, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    Quad(P(a0, r, y0), P(a1, r, y0), P(a1, r, y1), P(a0, r, y1), Out(a0), Out(a1), bottom, bottom, top, top);
                }
            }

            public void Line(Vector3 from, Vector3 to, float width, Color color)
            {
                var along = to - from;
                var side = Vector3.Cross(Vector3.up, along).normalized * width * 0.5f;
                from -= along.normalized * width * 0.5f;
                to += along.normalized * width * 0.5f;
                Quad(from - side, to - side, to + side, from + side, Vector3.up, Vector3.up, color, color, color, color);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
