using System.Collections.Generic;
using Dogpatch.Audio;
using Dogpatch.Feel;
using Dogpatch.Look;
using Dogpatch.Operator;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Smash
{
    // One shared, pooled explosion in the world's flat vertex-colour style, used by the explode
    // effect, demolish and car wrecks. Each blast runs in stages:
    //   1. a white-yellow flash and a point-light spike (about 0.1 s),
    //   2. a fireball of overlapping low-poly spheres that expand fast, roll upward and cool from
    //      yellow to orange to deep red,
    //   3. a dark smoke stem and a mushroom cap of grey-black puffs that hang for 4-6 s,
    //   4. charred debris chunks that bounce and settle, trailing small flames that turn to smoke,
    //   5. a ring of dust racing out along the ground, a scorch mark left behind, and a few small
    //      lingering fires (FireFx),
    //   6. camera shake and a low boom, scaled by distance.
    // Every stage draws from six shared mesh-particle systems (one draw call each), a fixed pool of
    // lights and a round-robin pool of scorch marks. At most MaxFull blasts run fully at once; any
    // more get only the flash, fireball and smoke.
    public sealed class BlastFx : MonoBehaviour
    {
        const int MaxFull = 4;
        const int MaxScorches = 10;
        const float BlastLife = 6f;
        const float ScorchLife = 25f;
        static readonly Color FlashWhite = new Color(1f, 0.97f, 0.8f);
        static readonly Color Dust = new Color(0.8f, 0.72f, 0.6f);
        static readonly Color[] Chunks = { new Color(0.12f, 0.11f, 0.11f), new Color(0.24f, 0.22f, 0.21f), new Color(0.4f, 0.37f, 0.34f), new Color(0.85f, 0.38f, 0.1f) };

        static BlastFx instance;

        sealed class Blast
        {
            public Vector3 At;
            public float Size;
            public float Born;
            public bool Full;
            public bool Capped;
            public float NextStem;
            public Light Light;
        }

        Material material;
        ParticleSystem flash, fireball, smoke, debris, trail, dust;
        readonly List<Blast> live = new List<Blast>();
        readonly Stack<Light> lights = new Stack<Light>();
        readonly List<(Transform t, float until)> scorches = new List<(Transform, float)>();
        readonly ParticleSystem.Particle[] chunks = new ParticleSystem.Particle[64];
        Mesh scorchMesh;
        int nextScorch;
        float nextTrail;

        // ground: the point on the ground under the blast. size: roughly its radius in metres (a car
        // is about 2.5, a building 8-14). boom: false when the caller already plays a sound and shake.
        public static void Spawn(Vector3 ground, float size, bool boom = true)
        {
            var world = SmashWorld.Instance;
            if (world == null) return;
            if (instance == null)
            {
                instance = new GameObject("BlastFx").AddComponent<BlastFx>();
                instance.Init(world.Material);
            }
            instance.Fire(ground, Mathf.Clamp(size, 1f, 14f), boom, world.View);
        }

        // A blast sized to what blew up, sitting on its footprint.
        public static void Spawn(Bounds b, float scale = 1f, bool boom = true) =>
            Spawn(new Vector3(b.center.x, b.min.y, b.center.z), Mathf.Max(b.extents.x, b.extents.z, b.extents.y * 0.6f) * scale, boom);

        void Init(Material shared)
        {
            material = shared;
            var ico = SmashFx.Icosahedron();
            flash = Make("BlastFlash", ico, 0f, 24, null, Curve(0f, 0.7f, 1f, 1.25f), 0f, false);
            fireball = Make("BlastFireball", ico, -0.35f, 160, Ramp(
                (0f, FlashWhite), (0.12f, new Color(1f, 0.82f, 0.22f)), (0.35f, new Color(1f, 0.48f, 0.08f)), (0.65f, new Color(0.62f, 0.12f, 0.05f)), (1f, new Color(0.18f, 0.15f, 0.15f))),
                Curve(0f, 0.35f, 0.12f, 1f, 0.6f, 0.95f, 1f, 0.15f), 2.6f, false);
            smoke = Make("BlastSmoke", ico, -0.012f, 320, Ramp(
                (0f, new Color(0.2f, 0.17f, 0.16f)), (0.5f, new Color(0.27f, 0.26f, 0.27f)), (1f, new Color(0.45f, 0.45f, 0.47f))),
                Curve(0f, 0.3f, 0.15f, 1f, 0.85f, 1f, 1f, 0f), 1.4f, false);
            debris = Make("BlastDebris", SmashFx.Tetra(), 1.8f, 64, null, Curve(0f, 1f, 0.85f, 1f, 1f, 0f), 0f, true);
            trail = Make("BlastTrail", ico, -0.15f, 260, Ramp(
                (0f, new Color(1f, 0.75f, 0.2f)), (0.3f, new Color(0.95f, 0.35f, 0.08f)), (0.6f, new Color(0.3f, 0.27f, 0.27f)), (1f, new Color(0.4f, 0.4f, 0.42f))),
                Curve(0f, 0.5f, 0.2f, 1f, 1f, 0f), 0.5f, false);
            dust = Make("BlastDust", ico, 0f, 160, null, Curve(0f, 0.4f, 0.3f, 1f, 1f, 0f), 3f, false);
            for (int i = 0; i < MaxFull; i++)
            {
                var light = new GameObject("BlastLight").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights.Push(light);
            }
            scorchMesh = Scorch();
        }

        void Fire(Vector3 ground, float size, bool boom, Camera view)
        {
            float now = Time.time;
            int full = 0;
            foreach (var b in live) if (b.Full) full++;
            var blast = new Blast { At = ground, Size = size, Born = now, Full = full < MaxFull && lights.Count > 0, NextStem = now };
            live.Add(blast);
            var core = ground + Vector3.up * size * 0.45f;

            for (int i = 0; i < 3; i++)
                flash.Emit(new ParticleSystem.EmitParams { position = core + Random.insideUnitSphere * size * 0.2f, startColor = FlashWhite, startSize = size * Random.Range(1.8f, 2.4f), startLifetime = 0.12f, rotation3D = Spin() }, 1);

            int balls = blast.Full ? EffectsSettings.Count(9f) : 5;
            for (int i = 0; i < balls; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.6f;
                fireball.Emit(new ParticleSystem.EmitParams
                {
                    position = core + dir * size * 0.3f,
                    velocity = dir * size * Random.Range(2.5f, 5f) + Vector3.up * size * Random.Range(1.2f, 2.6f),
                    startColor = Color.white,
                    startSize = size * Random.Range(0.9f, 1.5f),
                    startLifetime = Random.Range(1.3f, 2f),
                    rotation3D = Spin(),
                    angularVelocity3D = Random.insideUnitSphere * 120f,
                }, 1);
            }

            if (blast.Full)
            {
                blast.Light = lights.Pop();
                blast.Light.transform.position = core + Vector3.up;
                blast.Light.range = size * 6f + 12f;
                blast.Light.enabled = true;

                int pieces = EffectsSettings.Count(10f);
                for (int i = 0; i < pieces; i++)
                {
                    var v = Random.insideUnitSphere * size * 3.5f;
                    v.y = Mathf.Abs(v.y) + Random.Range(6f, 12f);
                    debris.Emit(new ParticleSystem.EmitParams
                    {
                        position = core + Random.insideUnitSphere * size * 0.3f,
                        velocity = v,
                        startColor = Chunks[Random.Range(0, Chunks.Length)],
                        startSize = Random.Range(0.25f, 0.5f) * Mathf.Sqrt(size),
                        startLifetime = Random.Range(2.4f, 3.2f),
                        rotation3D = Spin(),
                        angularVelocity3D = Random.insideUnitSphere * 400f,
                    }, 1);
                }

                int ring = EffectsSettings.Count(20f);
                for (int i = 0; i < ring; i++)
                {
                    float a = i * Mathf.PI * 2f / ring + Random.Range(-0.1f, 0.1f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    dust.Emit(new ParticleSystem.EmitParams
                    {
                        position = ground + dir * size * 0.4f + Vector3.up * 0.3f,
                        velocity = dir * size * Random.Range(6f, 8f) + Vector3.up * Random.Range(0.3f, 1.2f),
                        startColor = Color.Lerp(Dust, Color.white, Random.Range(0f, 0.2f)),
                        startSize = size * Random.Range(0.45f, 0.7f),
                        startLifetime = Random.Range(1f, 1.4f),
                        rotation3D = Spin(),
                        angularVelocity3D = Random.insideUnitSphere * 90f,
                    }, 1);
                }

                for (int i = 0; i < 3; i++)
                {
                    var spot = ground + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * size * 0.6f;
                    var fire = FireFx.Create(material, new Bounds(spot + Vector3.up * 0.2f, new Vector3(1.2f, 0.4f, 1.2f)), Random.Range(0.6f, 0.9f), false);
                    Destroy(fire.gameObject, Random.Range(3.5f, 5.5f));
                }
            }

            PlaceScorch(ground, size);

            if (!boom || view == null) return;
            float d = Vector3.Distance(view.transform.position, ground);
            if (d > 240f) return;
            float near = 1f - d / 240f;
            if (CameraFeel.Instance != null) CameraFeel.Instance.AddTrauma(near * near * Mathf.Clamp(size / 4f, 0.45f, 1f));
            SoundSystem.Play("wreck", core, Bus.Sfx, Mathf.Lerp(0.55f, 1f, near), Mathf.Lerp(0.8f, 0.55f, size / 12f) * Random.Range(0.96f, 1.04f), 230, 260f);
        }

        void Update()
        {
            float now = Time.time;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var b = live[i];
                float age = now - b.Born;
                if (b.Light != null)
                {
                    float glow = age < 0.1f ? 16f : 6f * Mathf.Exp(-(age - 0.1f) * 2.2f) * (0.75f + 0.5f * Mathf.PerlinNoise(now * 12f, i));
                    b.Light.intensity = glow;
                    b.Light.color = Color.Lerp(FlashWhite, new Color(1f, 0.45f, 0.12f), Mathf.Clamp01(age * 4f));
                    if (age > 2f)
                    {
                        b.Light.enabled = false;
                        lights.Push(b.Light);
                        b.Light = null;
                    }
                }
                if (age < 1.1f && now >= b.NextStem)
                {
                    b.NextStem = now + (b.Full ? 0.08f : 0.2f);
                    smoke.Emit(new ParticleSystem.EmitParams
                    {
                        position = b.At + new Vector3(Random.Range(-0.3f, 0.3f) * b.Size, b.Size * Random.Range(0.3f, 1f), Random.Range(-0.3f, 0.3f) * b.Size),
                        velocity = Vector3.up * b.Size * Random.Range(0.7f, 1.3f) + Random.insideUnitSphere * 0.6f,
                        startColor = Color.white,
                        startSize = b.Size * Random.Range(0.6f, 0.9f),
                        startLifetime = Random.Range(4f, 6f),
                        rotation3D = Spin(),
                        angularVelocity3D = Random.insideUnitSphere * 25f,
                    }, 1);
                }
                if (!b.Capped && age > 0.15f)
                {
                    b.Capped = true;
                    int cap = b.Full ? EffectsSettings.Count(10f) : 4;
                    for (int k = 0; k < cap; k++)
                    {
                        float a = k * Mathf.PI * 2f / cap + Random.Range(-0.3f, 0.3f);
                        var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * b.Size * Random.Range(1.3f, 2.1f);
                        smoke.Emit(new ParticleSystem.EmitParams
                        {
                            position = b.At + Vector3.up * b.Size * 1.1f + outward * 0.15f,
                            velocity = Vector3.up * b.Size * Random.Range(1.5f, 2f) + outward,
                            startColor = Color.white,
                            startSize = b.Size * Random.Range(1.1f, 1.6f),
                            startLifetime = Random.Range(4.5f, 6f),
                            rotation3D = Spin(),
                            angularVelocity3D = Random.insideUnitSphere * 20f,
                        }, 1);
                    }
                }
                if (age > BlastLife)
                {
                    if (b.Light != null)
                    {
                        b.Light.enabled = false;
                        lights.Push(b.Light);
                    }
                    live.RemoveAt(i);
                }
            }

            if (now >= nextTrail && debris.particleCount > 0)
            {
                nextTrail = now + 0.08f;
                int n = debris.GetParticles(chunks);
                for (int i = 0; i < n; i += 2)
                {
                    var p = chunks[i];
                    if (p.remainingLifetime < 0.6f || p.velocity.sqrMagnitude < 4f) continue;
                    trail.Emit(new ParticleSystem.EmitParams
                    {
                        position = p.position,
                        velocity = Vector3.up * 0.6f,
                        startColor = Color.white,
                        startSize = Random.Range(0.3f, 0.45f),
                        startLifetime = Random.Range(0.5f, 0.8f),
                        rotation3D = Spin(),
                    }, 1);
                }
            }

            for (int i = 0; i < scorches.Count; i++)
            {
                var (t, until) = scorches[i];
                if (t.gameObject.activeSelf && now > until) t.gameObject.SetActive(false);
            }
        }

        void PlaceScorch(Vector3 ground, float size)
        {
            Transform t;
            if (scorches.Count < MaxScorches)
            {
                var go = new GameObject("Scorch");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = scorchMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t = go.transform;
                scorches.Add((t, 0f));
            }
            else t = scorches[nextScorch % MaxScorches].t;
            int slot = scorches.FindIndex(s => s.t == t);
            nextScorch = slot + 1;
            float y = Mathf.Max(ground.y, RoadSurface.GroundY(new Vector2(ground.x, ground.z))) + 0.04f;
            t.SetPositionAndRotation(new Vector3(ground.x, y, ground.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            t.localScale = new Vector3(size * 1.2f, 1f, size * 1.2f);
            t.gameObject.SetActive(true);
            scorches[slot] = (t, Time.time + ScorchLife);
        }

        // A ragged dark disc: a sooty ring around a near-black centre.
        static Mesh Scorch()
        {
            var mb = new MeshBuilder();
            var soot = new Color(0.07f, 0.065f, 0.065f);
            var ash = new Color(0.17f, 0.155f, 0.15f);
            const int Sides = 12;
            var rng = new System.Random(7);
            var outer = new Vector3[Sides];
            var inner = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                outer[i] = dir * (0.75f + (float)rng.NextDouble() * 0.3f);
                inner[i] = dir * (0.35f + (float)rng.NextDouble() * 0.15f);
            }
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                mb.Triangle(Vector3.zero, inner[j], inner[i], soot, Vector3.up);
                mb.Triangle(inner[i], inner[j], outer[j], ash, Vector3.up);
                mb.Triangle(inner[i], outer[j], outer[i], ash, Vector3.up);
            }
            return mb.ToMesh("scorch");
        }

        static Vector3 Spin() => new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);

        static AnimationCurve Curve(params float[] timeValue)
        {
            var keys = new Keyframe[timeValue.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
            return new AnimationCurve(keys);
        }

        static Gradient Ramp(params (float time, Color color)[] stops)
        {
            var colors = new GradientColorKey[stops.Length];
            for (int i = 0; i < stops.Length; i++) colors[i] = new GradientColorKey(stops[i].color, stops[i].time);
            var g = new Gradient();
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // Same setup as SmashFx's systems: world-space mesh particles on the shared material, emitted
        // only from script. color is a colour over lifetime (null keeps each particle's start colour),
        // drag slows particles down so bursts expand fast and then hang.
        ParticleSystem Make(string name, Mesh mesh, float gravity, int max, Gradient color, AnimationCurve size, float drag, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
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
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);
            if (color != null)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(color);
            }
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(1000f);
                limit.drag = new ParticleSystem.MinMaxCurve(drag);
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }
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
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
