using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.WorldChanges;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dogpatch.Operator
{
    // A lasting effect that sets game knobs on Apply and hands back how to put them back.
    public sealed class KnobEffect : OperatorEffect
    {
        readonly Func<OperatorEffect, string, Action> set;
        Action undo;

        public KnobEffect(Func<OperatorEffect, string, Action> set) => this.set = set;

        public override void Apply(string strength, EffectTarget target) => undo = set(this, strength);

        public override void Revert() => undo?.Invoke();
    }

    public sealed class WeatherEffect : OperatorEffect
    {
        readonly WeatherKind kind;
        readonly float fog;
        WeatherKind before;
        float beforeIntensity;
        float beforeFog;

        public WeatherEffect(WeatherKind kind, float fog = -1f)
        {
            this.kind = kind;
            this.fog = fog;
        }

        public override void Apply(string strength, EffectTarget target)
        {
            before = WorldEnvironment.Weather;
            beforeIntensity = WorldEnvironment.WeatherIntensity;
            beforeFog = WorldEnvironment.FogDensity;
            if (kind != WeatherKind.Sun || fog < 0f) WorldEnvironment.SetWeather(kind, Scale(strength, 0.5f, 0.8f, 1f), 2.5f);
            if (fog >= 0f) WorldEnvironment.SetFog(fog * Scale(strength, 0.8f, 1f, 1.2f), 2.5f);
        }

        public override void Revert()
        {
            WorldEnvironment.SetWeather(before == kind ? WeatherKind.Sun : before, beforeIntensity, 2.5f);
            if (fog >= 0f) WorldEnvironment.SetFog(beforeFog, 2.5f);
        }
    }

    public sealed class TimeEffect : OperatorEffect
    {
        readonly TimePreset preset;
        float before;

        public TimeEffect(TimePreset preset) => this.preset = preset;

        public override void Apply(string strength, EffectTarget target)
        {
            before = WorldEnvironment.TimeOfDay;
            WorldEnvironment.SetTime(preset, 2.5f);
        }

        public override void Revert() => WorldEnvironment.SetTime(before, 2.5f);
    }

    // Instant: a fireball on each target and everything that moves is thrown into the air.
    public sealed class ExplodeEffect : OperatorEffect
    {
        const int MaxBlasts = 12;

        public override void Apply(string strength, EffectTarget target)
        {
            float up = Scale(strength, 20f, 30f, 42f);
            int n = 0;
            foreach (var tag in target.Entities)
            {
                if (n++ >= MaxBlasts) break;
                Dogpatch.Smash.BlastFx.Spawn(tag.WorldBounds, Scale(strength, 1.1f, 1.4f, 1.8f));
                var car = tag.GetComponent<CarController>();
                if (car != null && car.Driven && !car.GetComponent<Rigidbody>().isKinematic) CarFx.On(car).Launch(up * 0.6f);
                else if (tag.Span == null) Launched.Start(tag.transform, up, null);
                Blast(tag.WorldBounds.center, car, strength);
            }
        }

        // Racers' cars near the blast are thrown outward, stunned and scorched. Each phone moves its own car.
        static void Blast(Vector3 center, CarController source, string strength)
        {
            float radius = Scale(strength, 6f, 8f, 10f), shove = Scale(strength, 4.5f, 6f, 8f), hurt = Scale(strength, 0.05f, 0.08f, 0.12f);
            var all = Dogpatch.Smash.VehicleDamage.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var damage = all[i];
                var car = damage != null ? damage.Car : null;
                if (car == null || car == source || !car.Driven || car.Autopilot || !car.TryGetComponent<Rigidbody>(out var body) || body.isKinematic) continue;
                var away = car.transform.position - center;
                away.y = 0f;
                if (away.sqrMagnitude > radius * radius) continue;
                away = away.sqrMagnitude > 0.01f ? away.normalized : -car.transform.forward;
                car.Stun(0.5f);
                CarFx.On(car).Push(away * shove + Vector3.up * (shove * 0.5f));
                damage.Burn(hurt);
                Dogpatch.Feel.Haptics.Impact(Dogpatch.Feel.HapticStyle.Heavy, 1f);
            }
        }
    }

    // nitro: bursts instead of a flat cap. A few seconds of hard acceleration and a higher top speed,
    // then a recharge, repeating for as long as the row lasts.
    public sealed class NitroEffect : OperatorEffect
    {
        const float Burst = 3f, Cycle = 6f;
        EffectTicker ticker;
        bool on;
        float top = 1f, accel = 1f;

        public override void Apply(string strength, EffectTarget target)
        {
            top = Scale(strength, 1.5f, 1.8f, 2.2f);
            accel = Scale(strength, 2f, 2.5f, 3f);
            float start = Time.time;
            ticker = EffectTicker.On(Context, () =>
            {
                bool burst = Mathf.Repeat(Time.time - start, Cycle) < Burst;
                if (burst == on) return;
                on = burst;
                CarController.SpeedScale *= burst ? top : 1f / top;
                CarController.AccelScale *= burst ? accel : 1f / accel;
                if (!burst) return;
                Dogpatch.Feel.Haptics.Impact(Dogpatch.Feel.HapticStyle.Medium, 0.8f);
                Dogpatch.Feel.CameraFeel.Instance?.AddTrauma(0.15f);
                var car = Context.Game.CurrentCar;
                if (car != null) MotionFx.Land(car.transform.position - car.transform.forward * car.HalfExtents.z, 0.6f);
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            if (on)
            {
                CarController.SpeedScale /= top;
                CarController.AccelScale /= accel;
            }
            on = false;
        }
    }

    // grip_down: the caller's car leaks oil. It keeps its own grip and lays a trail of slicks for the
    // first few seconds; any other racer who drives over one slides on almost no traction for a moment.
    // Runs on every phone: each draws the trail behind the caller's car and checks only its own car.
    public sealed class OilSlickEffect : OperatorEffect
    {
        const float Every = 0.3f, Spacing = 2.5f, Radius = 1.8f, Slip = 1.5f, SlipGrip = 0.1f;
        const int MaxSlicks = 48;
        static Mesh blob;
        readonly List<Vector3> slicks = new List<Vector3>();
        readonly List<GameObject> decals = new List<GameObject>();
        EffectTicker ticker;
        CarController slipping;
        float slipUntil;

        public override void Apply(string strength, EffectTarget target)
        {
            float trail = Scale(strength, 7f, 10f, 14f);
            float start = Time.time, next = 0f;
            bool mine = target.IncludesMe;
            ticker = EffectTicker.On(Context, () =>
            {
                float now = Time.time;
                if (now - start < trail && now >= next && slicks.Count < MaxSlicks)
                {
                    next = now + Every;
                    foreach (var car in Racers.Cars(Context, target)) Drop(car);
                }
                if (slipping != null && now > slipUntil) Recover();
                if (mine) return;
                var me = Context.Game.CurrentCar;
                if (me == null || !Racers.Physical(me) || !me.OnGround) return;
                var p = me.transform.position;
                foreach (var s in slicks)
                {
                    if (Mathf.Abs(s.y - p.y) > 1.5f || (new Vector2(s.x - p.x, s.z - p.z)).sqrMagnitude > Radius * Radius) continue;
                    if (slipping == null)
                    {
                        slipping = me;
                        CarFx.On(me).Grip *= SlipGrip;
                        Dogpatch.Feel.Haptics.Impact(Dogpatch.Feel.HapticStyle.Soft, 0.7f);
                    }
                    slipUntil = now + Slip;
                    break;
                }
            });
        }

        void Drop(CarController car)
        {
            var at = car.transform.position - car.transform.forward * car.HalfExtents.z;
            if (slicks.Count > 0 && (slicks[slicks.Count - 1] - at).sqrMagnitude < Spacing * Spacing) return;
            at.y = Racers.GroundY(at) + 0.03f;
            slicks.Add(at);
            if (blob == null)
            {
                var mb = new Dogpatch.World.MeshBuilder();
                var oil = PropMeshes.C("#14141B");
                var sheen = PropMeshes.C("#3A3550");
                mb.Box(Vector3.zero, new Vector3(2.4f, 0.02f, 1.6f), Quaternion.Euler(0f, 15f, 0f), oil);
                mb.Box(new Vector3(0.7f, 0.005f, 0.5f), new Vector3(1.2f, 0.02f, 0.9f), Quaternion.Euler(0f, -30f, 0f), oil);
                mb.Box(new Vector3(-0.3f, 0.01f, -0.2f), new Vector3(0.7f, 0.02f, 0.4f), Quaternion.Euler(0f, 40f, 0f), sheen);
                var rainbow = new[] { PropMeshes.C("#7B3FE4"), PropMeshes.C("#1FC8B4"), PropMeshes.C("#FFD23F"), PropMeshes.C("#E040A0") };
                for (int i = 0; i < rainbow.Length; i++)
                    mb.Box(new Vector3(-0.5f + i * 0.28f, 0.015f, 0.15f - i * 0.12f), new Vector3(0.9f - i * 0.12f, 0.02f, 0.09f), Quaternion.Euler(0f, 20f + i * 8f, 0f), rainbow[i]);
                blob = mb.ToMesh("oil slick");
            }
            var decal = PropMeshes.Attach(null, "Oil slick (operator)", blob, Context.Material, at, false);
            decal.transform.rotation = Quaternion.Euler(0f, car.transform.eulerAngles.y + slicks.Count * 37f, 0f);
            Pop.In(decal);
            decals.Add(decal);
        }

        void Recover()
        {
            if (slipping != null) CarFx.On(slipping).Grip /= SlipGrip;
            slipping = null;
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            Recover();
            foreach (var d in decals) Pop.Out(d);
            decals.Clear();
            slicks.Clear();
        }
    }

    // Instant: the building blows up in one go. A fireball, chunky debris and a dust cloud, a boom
    // and a shake when you're close, and in the same frame the building is gone: hidden in its chunk,
    // its own colliders off and a ghost zone over the merged city collider, so you drive straight
    // across the lot over a low rubble patch.
    public sealed class DemolishEffect : OperatorEffect
    {
        static readonly Color[] Chunks = { Dogpatch.Smash.SmashFx.SmokeGrey, Dogpatch.Smash.SmashFx.SmokeDark, WorldEffectsB.Hex("#C9B79C"), WorldEffectsB.Hex("#8A7C6B") };

        public override void Apply(string strength, EffectTarget target)
        {
            var fx = Dogpatch.Smash.SmashWorld.Instance?.Fx;
            foreach (var tag in target.Entities)
            {
                var b = tag.WorldBounds;
                var foot = new Vector3(b.center.x, b.min.y, b.center.z);
                float size = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 4f, 30f);

                Dogpatch.Smash.BlastFx.Spawn(foot, size * 0.6f, false);
                for (int i = 0; i < 8; i++)
                {
                    var from = foot + Vector3.up * (b.size.y * UnityEngine.Random.Range(0.1f, 0.7f));
                    var fling = UnityEngine.Random.insideUnitSphere * size * 1.5f + Vector3.up * UnityEngine.Random.Range(8f, 18f);
                    fx?.Debris(from, fling, Chunks, 6, 0.9f);
                }
                fx?.Smoke(foot + Vector3.up * 2f, Dogpatch.Smash.SmashFx.SmokeGrey, size, 20, 2.5f);
                WorldEffectsB.Poof(b.center, size, Dogpatch.Smash.SmashFx.SmokeLight, 16);
                Dogpatch.Audio.SoundSystem.Play("wreck", b.center, Dogpatch.Audio.Bus.Sfx, 1f, 0.7f, 220, 200f);
                var cam = Camera.main;
                if (cam != null && Dogpatch.Feel.CameraFeel.Instance != null)
                {
                    float d = Vector3.Distance(cam.transform.position, b.center);
                    if (d < 150f) Dogpatch.Feel.CameraFeel.Instance.AddTrauma(Mathf.Lerp(0.9f, 0.2f, d / 150f));
                }

                var rubble = Rubble(tag.Id, foot, b.size);
                if (tag.Span != null)
                {
                    var span = tag.Span;
                    var copy = span.Detach(tag.Id + " (demolished)", Context.Material);
                    if (copy != null) Object.Destroy(copy);
                    foreach (var part in span.Parts)
                        if (part.Collider != null) part.Collider.enabled = false;
                    var lot = new GameObject(tag.Id + " (lot)");
                    lot.AddComponent<GhostZone>().Area = b;
                    Standing.Add(() =>
                    {
                        span.Show();
                        foreach (var part in span.Parts)
                            if (part.Collider != null) part.Collider.enabled = true;
                        Object.Destroy(lot);
                        Object.Destroy(rubble);
                    });
                }
                else
                {
                    var building = tag.gameObject;
                    building.SetActive(false);
                    Standing.Add(() =>
                    {
                        if (building != null) building.SetActive(true);
                        Object.Destroy(rubble);
                    });
                }
            }
        }

        // Every demolition on this device, as the step that puts the building back.
        static readonly List<Action> Standing = new List<Action>();

        // Puts every demolished building back on this device, for a racer starting a mission.
        public static void RebuildAll()
        {
            foreach (var rebuild in Standing) rebuild();
            if (Standing.Count > 0) Debug.Log($"[Operator] rebuilt {Standing.Count} demolished buildings for the race");
            Standing.Clear();
        }

        GameObject Rubble(string id, Vector3 foot, Vector3 size)
        {
            var mb = new Dogpatch.World.MeshBuilder();
            var rng = new System.Random(id.GetHashCode());
            float w = size.x * 0.45f, l = size.z * 0.45f;
            for (int i = 0; i < 14; i++)
            {
                var at = new Vector3(((float)rng.NextDouble() * 2f - 1f) * w, 0f, ((float)rng.NextDouble() * 2f - 1f) * l);
                float s = 1f + (float)rng.NextDouble() * 2.5f;
                at.y = s * 0.15f;
                mb.Box(at, new Vector3(s, s * 0.3f, s * 0.8f), Quaternion.Euler(0f, rng.Next(0, 90), 0f), Chunks[rng.Next(Chunks.Length)]);
            }
            mb.Box(new Vector3(0f, 0.04f, 0f), new Vector3(size.x * 0.95f, 0.08f, size.z * 0.95f), Quaternion.identity, Dogpatch.Smash.SmashFx.SmokeDark);
            var go = new GameObject(id + " (rubble)");
            go.transform.position = foot;
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(id + " rubble");
            go.AddComponent<MeshRenderer>().sharedMaterial = Context.Material;
            return go;
        }
    }

    // Grow or shrink each target that is its own object; merged city meshes stay as built. A resized
    // car's top speed scales with its size, held to 0.6-1.6x so races stay fair.
    public sealed class ResizeEffect : OperatorEffect
    {
        readonly bool grow;
        readonly List<Transform> resized = new List<Transform>();
        float k = 1f;
        readonly List<(CarController car, float k)> sped = new List<(CarController, float)>();

        public ResizeEffect(bool grow) => this.grow = grow;

        public override void Apply(string strength, EffectTarget target)
        {
            k = grow ? Scale(strength, 2f, 3f, 4.5f) : Scale(strength, 0.32f, 0.28f, 0.22f);
            foreach (var tag in target.Entities)
            {
                if (tag.Span != null) continue;
                resized.Add(tag.transform);
                tag.transform.localScale *= k;
                if (!tag.TryGetComponent<CarController>(out var car)) continue;
                float speed = Mathf.Clamp(k, 0.6f, 1.6f);
                car.SpeedMul *= speed;
                sped.Add((car, speed));
            }
        }

        public override void Revert()
        {
            foreach (var t in resized) if (t != null) t.localScale /= k;
            resized.Clear();
            foreach (var (car, speed) in sped) if (car != null) car.SpeedMul /= speed;
            sped.Clear();
        }
    }

    // Giants and Tiny: people only, never cars. Resizes the target's on-foot bodies (this device's avatar
    // when the target includes me) and scales my walking and running speed with my size. Factors stack
    // and unwind multiplicatively.
    public sealed class SizeEffect : OperatorEffect
    {
        readonly float low, medium, high;
        readonly List<Transform> resized = new List<Transform>();
        float k = 1f;
        float run = 1f;

        public SizeEffect(float low, float medium, float high)
        {
            this.low = low;
            this.medium = medium;
            this.high = high;
        }

        public override void Apply(string strength, EffectTarget target)
        {
            k = Scale(strength, low, medium, high);
            var bodies = new HashSet<Transform>();
            foreach (var tag in target.Entities)
                if (tag != null && tag.Span == null && !tag.TryGetComponent<CarController>(out _)) bodies.Add(tag.transform);
            if (target.IncludesMe && Context.Game.Player != null)
            {
                bodies.Add(Context.Game.Player.transform);
                run = Mathf.Max(0.5f, k);
                PlayerController.RunScale *= run;
            }
            foreach (var body in bodies)
            {
                resized.Add(body);
                body.localScale *= k;
            }
        }

        // Undoes only this row's factor, so a "me" and an "everyone" size that overlap unwind in any order.
        public override void Revert()
        {
            foreach (var t in resized) if (t != null) t.localScale /= k;
            resized.Clear();
            PlayerController.RunScale /= run;
            run = 1f;
        }
    }

    public static class MoreEffects
    {
        static Action Gravity(float k)
        {
            Physics.gravity = new Vector3(0f, -9.81f * k, 0f);
            PlayerController.GravityScale = k;
            return () =>
            {
                Physics.gravity = new Vector3(0f, -9.81f, 0f);
                PlayerController.GravityScale = 1f;
            };
        }

        public static void Register(EffectRegistry r)
        {
            r.Register("vehicle_faster", () => new KnobEffect((e, s) =>
            {
                float top = OperatorEffect.Scale(s, 2.5f, 4f, 6f), accel = OperatorEffect.Scale(s, 1.4f, 1.6f, 1.9f);
                CarController.SpeedScale *= top;
                CarController.AccelScale *= accel;
                return () =>
                {
                    CarController.SpeedScale /= top;
                    CarController.AccelScale /= accel;
                };
            }));
            r.Register("vehicle_slower", () => new KnobEffect((e, s) => { float k = OperatorEffect.Scale(s, 0.4f, 0.25f, 0.15f); CarController.SpeedScale *= k; return () => CarController.SpeedScale /= k; }));
            r.Register("nitro", () => new NitroEffect());
            r.Register("run_slower", () => new KnobEffect((e, s) => { float k = OperatorEffect.Scale(s, 0.4f, 0.25f, 0.15f); PlayerController.RunScale *= k; return () => PlayerController.RunScale /= k; }));
            r.Register("jump_higher", () => new KnobEffect((e, s) => { PlayerController.JumpScale = OperatorEffect.Scale(s, 2f, 3f, 4.5f); return () => PlayerController.JumpScale = 1f; }));
            r.Register("slow_fall", () => new KnobEffect((e, s) => { PlayerController.GravityScale = OperatorEffect.Scale(s, 0.35f, 0.2f, 0.1f); return () => PlayerController.GravityScale = 1f; }));
            r.Register("bigger", () => new SizeEffect(1.8f, 2.5f, 3.5f));
            r.Register("smaller", () => new SizeEffect(0.32f, 0.28f, 0.22f));
            r.Register("grip_down", () => new OilSlickEffect());
            r.Register("slippery_ground", () => new KnobEffect((e, s) =>
            {
                CarFx.Traction = OperatorEffect.Scale(s, 0.06f, 0.03f, 0.015f);
                WorldEnvironment.IceTarget = 1f;
                return () =>
                {
                    CarFx.Traction = 1f;
                    WorldEnvironment.IceTarget = 0f;
                };
            }));
            r.Register("gravity_higher", () => new KnobEffect((e, s) => Gravity(OperatorEffect.Scale(s, 2f, 3f, 4f))));
            r.Register("snow", () => new WeatherEffect(WeatherKind.Snow));
            r.Register("storm", () => new WeatherEffect(WeatherKind.Rain, 0.75f));
            r.Register("fog", () => new WeatherEffect(WeatherKind.Sun, 0.85f));
            r.Register("sunset", () => new TimeEffect(TimePreset.Dusk));
            r.Register("day", () => new TimeEffect(TimePreset.Day));
            r.Register("explode", () => new ExplodeEffect());
            r.Register("demolish", () => new DemolishEffect());
            r.Register("grow", () => new ResizeEffect(true));
            r.Register("shrink", () => new ResizeEffect(false));
        }
    }
}
