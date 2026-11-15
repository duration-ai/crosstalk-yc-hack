using System.Collections.Generic;
using Dogpatch.Look;
using Dogpatch.Smash;
using Dogpatch.World;
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
    // Around the rim, ten segments are a lobby meter: a chaser in free roam, filled by the players
    // waiting, then dropping one a second through the countdown while the ring builds energy (spin,
    // halos, motes, a punch on every drop). At GO a shockwave rolls out of the ring, and in front of
    // the player's car two checkered flags pop up spinning and confetti bursts over the road.
    // Look only: it reads RaceDirector's room state and never changes it.
    public sealed class StartRingFx : MonoBehaviour
    {
        static readonly Color Yellow = PaintJob.Linear("#FFCB1F");
        static readonly Color Cream = PaintJob.Linear("#FCE9A8");
        static readonly Color White = PaintJob.Linear("#F7F8F9");
        static readonly Color Ink = PaintJob.Linear("#1C2347");
        static readonly Color[] Confetti =
        {
            PaintJob.Linear("#FFCB1F"), PaintJob.Linear("#F7F8F9"), PaintJob.Linear("#2D8CFF"),
            PaintJob.Linear("#A35BFF"), PaintJob.Linear("#FF8A1E"), PaintJob.Linear("#2EC27E"),
        };
        static readonly int TintId = Shader.PropertyToID("_Tint");

        const float PillarHeight = 90f;
        const float FloorY = 0.06f;
        const float NearFrom = 40f, NearTo = 10f;
        const float MotesWithin = 120f;
        const int Segments = 10;
        const float CountdownMs = 10000f;
        const float FlagsUp = 2.6f;

        float radius;
        Renderer pillar, haze, core, floor, chevrons, glyph;
        readonly Renderer[] halos = new Renderer[3];
        MaterialPropertyBlock block;
        ParticleSystem motes;
        float spin, glyphSpin, climb, moteDebt;

        Mesh meter;
        Color[] meterColors;
        int meterMask = -1;
        int lastLit = -1;
        float punch;
        Renderer shock;
        float goAt = -100f;
        bool wasStarted;
        ParticleSystem confetti;
        readonly Transform[] flags = new Transform[2];

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

            g = new Strip();
            for (int i = 0; i < Segments; i++)
            {
                float a0 = (i + 0.08f) * Mathf.PI * 2f / Segments, a1 = (i + 0.92f) * Mathf.PI * 2f / Segments;
                g.Arc(r + 0.45f, r + 0.95f, FloorY, a0, a1, White, 4);
            }
            var meterRenderer = Part(g, "Meter", lines);
            meter = meterRenderer.GetComponent<MeshFilter>().sharedMesh;
            meterColors = meter.colors;

            g = new Strip();
            g.Annulus(0.86f, 0.94f, FloorY + 0.02f, WithAlpha(White, 0.9f), WithAlpha(White, 0.9f), 48);
            g.Annulus(0.94f, 1f, FloorY + 0.02f, WithAlpha(Yellow, 0.6f), WithAlpha(Yellow, 0f), 48);
            shock = Part(g, "Shock", lines);
            shock.enabled = false;

            motes = Motes("Motes", flat, SmashFx.Tetra(), 160, 0f);
            confetti = Motes("Confetti", flat, Flake(), 320, 0.55f);
            var drag = confetti.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            for (int i = 0; i < flags.Length; i++) flags[i] = Flag(flat);
        }

        static Mesh Flake()
        {
            var v = new[]
            {
                new Vector3(-0.5f, 0f, -0.32f), new Vector3(0.5f, 0f, -0.32f), new Vector3(0.5f, 0f, 0.32f), new Vector3(-0.5f, 0f, 0.32f),
            };
            var colors = new Color32[4];
            for (int i = 0; i < 4; i++) colors[i] = new Color32(255, 255, 255, 255);
            var mesh = new Mesh { name = "confetti", vertices = v, triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, colors32 = colors };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        Transform Flag(Material flat)
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 1.9f, 0f), new Vector3(0.14f, 3.8f, 0.14f), Quaternion.identity, Ink);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 3; y++)
                    mb.Box(new Vector3(0.12f + (x + 0.5f) * 0.45f, 2.55f + (y + 0.5f) * 0.42f, 0f), new Vector3(0.45f, 0.42f, 0.05f), Quaternion.identity, ((x + y) & 1) == 0 ? Ink : White);
            mb.Box(new Vector3(0f, 3.9f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Quaternion.Euler(45f, 0f, 45f), Yellow);
            var go = new GameObject("GoFlag");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("GoFlag");
            go.AddComponent<MeshRenderer>().sharedMaterial = flat;
            go.SetActive(false);
            return go.transform;
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

        ParticleSystem Motes(string name, Material flat, Mesh mesh, int max, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.gravityModifier = gravity;
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
            renderer.mesh = mesh;
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

            var race = RaceDirector.Instance;
            var room = race == null ? null : race.Room;
            float energy = 0f;
            int lit;
            if (room != null && room.status == RoomStatus.Countdown && race.MsToStart < CountdownMs)
            {
                energy = Mathf.Clamp01(1f - race.MsToStart / CountdownMs);
                lit = Mathf.Clamp(Mathf.CeilToInt(race.MsToStart / 1000f), 0, Segments);
                if (lastLit >= 0 && lit < lastLit) punch = 1f;
            }
            else if (room != null && room.status == RoomStatus.Lobby)
                lit = Mathf.Clamp(Mathf.RoundToInt((race.Last?.racers?.Count ?? 0) * Segments / 4f), 1, Segments);
            else
                lit = -1;
            lastLit = lit;
            Meter(lit < 0 ? 1 << ((int)(t * 4f) % Segments) : (1 << lit) - 1);

            bool started = race != null && race.Started;
            if (started && !wasStarted) Go(race);
            wasStarted = started;
            punch = Mathf.Max(0f, punch - dt * 3f);
            Pop(t - goAt);

            float beatRate = 0.8f + energy * 2.4f;
            float pulse = 1f + Mathf.Sin(t * Mathf.PI * 2f * beatRate) * (0.05f + energy * 0.06f);
            pillar.transform.localScale = new Vector3(pulse, 1f, pulse);
            float swollen = pulse * (1f + energy * 0.6f);
            core.transform.localScale = new Vector3(swollen, 1f + energy * 0.4f, swollen);
            haze.transform.localScale = new Vector3(2f - pulse, 1f, 2f - pulse);
            float fade = Mathf.Lerp(1f, 0.35f, near) + energy * 0.3f;
            SetAlpha(pillar, fade);
            SetAlpha(haze, fade);
            SetAlpha(core, fade);

            climb += dt * (0.25f + near * 0.25f + energy * 1.6f);
            for (int i = 0; i < halos.Length; i++)
            {
                float u = Mathf.Repeat(climb + i / (float)halos.Length, 1f);
                halos[i].transform.localPosition = Vector3.up * (u * u * 24f + 0.1f);
                float s = 1f + u * 0.4f;
                halos[i].transform.localScale = new Vector3(s, 1f, s);
                SetAlpha(halos[i], (1f - u) * (0.6f + energy * 0.4f));
            }

            spin += dt * (25f + near * 40f + energy * energy * 500f);
            glyphSpin -= dt * (15f + near * 20f + energy * 200f);
            chevrons.transform.localRotation = Quaternion.Euler(0f, spin, 0f);
            glyph.transform.localRotation = Quaternion.Euler(0f, glyphSpin, 0f);
            float swell = 1f + near * 0.04f + Mathf.Sin(t * Mathf.PI * 1.6f) * 0.015f + punch * punch * 0.25f;
            glyph.transform.localScale = new Vector3(swell, 1f, swell);
            float beat = Mathf.Min(1f, 0.75f + 0.25f * Mathf.Sin(t * Mathf.PI * 2f * beatRate) + punch * 0.5f);
            SetAlpha(floor, beat);
            SetAlpha(chevrons, 0.85f + near * 0.15f);
            SetAlpha(glyph, beat);

            if (distance > MotesWithin) return;
            moteDebt += dt * (4f + near * 10f + energy * 40f);
            for (; moteDebt >= 1f; moteDebt -= 1f)
            {
                float a = Random.value * Mathf.PI * 2f;
                var at = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Random.Range(0.3f, 0.8f) + Vector3.up * 0.2f;
                motes.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Vector3.up * Random.Range(2.5f, 3.5f) * (1f + energy * 1.5f) + new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (0.8f + energy * 3f),
                    startColor = Random.value < 0.7f ? Yellow : White,
                    startSize = Random.Range(0.22f, 0.36f),
                    startLifetime = Random.Range(2.4f, 3.2f),
                    rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f),
                    angularVelocity3D = Random.insideUnitSphere * 220f,
                }, 1);
            }
        }

        void Meter(int mask)
        {
            if (mask == meterMask) return;
            meterMask = mask;
            int per = meterColors.Length / Segments;
            for (int i = 0; i < meterColors.Length; i++)
                meterColors[i] = (mask & (1 << (i / per))) != 0 ? WithAlpha(Yellow, 0.95f) : WithAlpha(White, 0.18f);
            meter.colors = meterColors;
        }

        // GO: a shockwave out of the ring, and the pop where the racers are (just ahead of the
        // player's car on the grid, or the ring itself without one).
        void Go(RaceDirector race)
        {
            goAt = Time.time;
            shock.enabled = true;
            var car = race.MyCar;
            Vector3 site = transform.position, forward = Vector3.forward;
            if (car != null && Vector3.Distance(car.transform.position, transform.position) < 400f)
            {
                forward = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
                site = car.transform.position + forward * 16f;
            }
            var side = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i].gameObject.SetActive(true);
                flags[i].position = site + side * (i == 0 ? -6f : 6f);
            }
            int n = EffectsSettings.Count(220);
            float size = 0.42f * EffectsSettings.Scale;
            for (int i = 0; i < n; i++)
            {
                var v = Random.insideUnitSphere * 5f;
                v.y = Random.Range(7f, 15f);
                confetti.Emit(new ParticleSystem.EmitParams
                {
                    position = site + Vector3.up * 1.5f + Random.insideUnitSphere * 1.5f,
                    velocity = v,
                    startColor = Confetti[Random.Range(0, Confetti.Length)],
                    startSize = size * Random.Range(0.7f, 1.2f),
                    startLifetime = Random.Range(2.2f, 3.2f),
                    rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f),
                    angularVelocity3D = Random.insideUnitSphere * 600f,
                }, 1);
            }
        }

        void Pop(float since)
        {
            if (shock.enabled)
            {
                float s = radius * (1f + since * 7f);
                shock.transform.localScale = new Vector3(s, 1f, s);
                SetAlpha(shock, Mathf.Max(0f, 1f - since * 1.2f));
                if (since > 0.9f) shock.enabled = false;
            }
            for (int i = 0; i < flags.Length; i++)
            {
                if (!flags[i].gameObject.activeSelf) continue;
                if (since > FlagsUp + 0.4f)
                {
                    flags[i].gameObject.SetActive(false);
                    continue;
                }
                float rise = since < 0.45f ? 1f + Mathf.Sin(since / 0.45f * Mathf.PI * 1.5f) * (1f - since / 0.45f) * 0.35f : 1f;
                if (since < 0.45f) rise *= Mathf.Clamp01(since / 0.12f);
                if (since > FlagsUp) rise = 1f - (since - FlagsUp) / 0.4f;
                flags[i].localScale = new Vector3(1f, Mathf.Max(0.01f, rise), 1f);
                float turn = 900f * Mathf.Exp(-since * 1.6f);
                flags[i].Rotate(0f, (i == 0 ? turn : -turn) * Time.deltaTime, 0f, Space.World);
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

            public void Arc(float inner, float outer, float y, float from, float to, Color color, int steps)
            {
                for (int i = 0; i < steps; i++)
                {
                    float a0 = Mathf.Lerp(from, to, i / (float)steps), a1 = Mathf.Lerp(from, to, (i + 1f) / steps);
                    Quad(P(a0, inner, y), P(a1, inner, y), P(a1, outer, y), P(a0, outer, y), Vector3.up, Vector3.up, color, color, color, color);
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
