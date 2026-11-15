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
                var fire = FireFx.Create(Context.Material, tag.WorldBounds, Scale(strength, 1.8f, 2.4f, 3.2f), n <= 3);
                Object.Destroy(fire.gameObject, 3.5f);
                var car = tag.GetComponent<CarController>();
                if (car != null && car.Driven && !car.GetComponent<Rigidbody>().isKinematic) CarFx.On(car).Launch(up * 0.6f);
                else if (tag.Span == null) Launched.Start(tag.transform, up, null);
            }
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

                var fire = FireFx.Create(Context.Material, b, 2.4f, true);
                Object.Destroy(fire.gameObject, 3f);
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

                if (tag.Span != null)
                {
                    var copy = tag.Span.Detach(tag.Id + " (demolished)", Context.Material);
                    if (copy != null) Object.Destroy(copy);
                    foreach (var part in tag.Span.Parts)
                        if (part.Collider != null) part.Collider.enabled = false;
                    new GameObject(tag.Id + " (lot)").AddComponent<GhostZone>().Area = b;
                }
                else tag.gameObject.SetActive(false);
                Rubble(tag.Id, foot, b.size);
            }
        }

        void Rubble(string id, Vector3 foot, Vector3 size)
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
        }
    }

    // Grow or shrink each target that is its own object; merged city meshes stay as built.
    public sealed class ResizeEffect : OperatorEffect
    {
        readonly bool grow;
        readonly System.Collections.Generic.List<(Transform t, Vector3 scale)> resized = new System.Collections.Generic.List<(Transform, Vector3)>();

        public ResizeEffect(bool grow) => this.grow = grow;

        public override void Apply(string strength, EffectTarget target)
        {
            float k = grow ? Scale(strength, 2f, 3f, 4.5f) : Scale(strength, 0.5f, 0.3f, 0.2f);
            foreach (var tag in target.Entities)
            {
                if (tag.Span != null) continue;
                resized.Add((tag.transform, tag.transform.localScale));
                tag.transform.localScale *= k;
            }
        }

        public override void Revert()
        {
            foreach (var (t, scale) in resized) if (t != null) t.localScale = scale;
            resized.Clear();
        }
    }

    // Resizes only the target's cars, plus this device's avatar when the target includes me.
    public sealed class SizeEffect : OperatorEffect
    {
        readonly float low, medium, high;
        readonly List<CarFx> sized = new List<CarFx>();
        Transform avatar;
        Vector3 before;

        public SizeEffect(float low, float medium, float high)
        {
            this.low = low;
            this.medium = medium;
            this.high = high;
        }

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, low, medium, high);
            foreach (var car in Racers.Cars(Context, target))
            {
                var fx = CarFx.On(car);
                fx.OwnSize = k;
                sized.Add(fx);
            }
            if (!target.IncludesMe || Context.Game.Player == null) return;
            avatar = Context.Game.Player.transform;
            before = avatar.localScale;
            avatar.localScale = before * k;
        }

        public override void Revert()
        {
            foreach (var fx in sized) if (fx != null) fx.OwnSize = 1f;
            sized.Clear();
            if (avatar != null) avatar.localScale = before;
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
            r.Register("vehicle_faster", () => new KnobEffect((e, s) => { CarController.SpeedScale = OperatorEffect.Scale(s, 2.5f, 4f, 6f); return () => CarController.SpeedScale = 1f; }));
            r.Register("vehicle_slower", () => new KnobEffect((e, s) => { CarController.SpeedScale = OperatorEffect.Scale(s, 0.4f, 0.25f, 0.15f); return () => CarController.SpeedScale = 1f; }));
            r.Register("nitro", () => new KnobEffect((e, s) => { CarController.SpeedScale = OperatorEffect.Scale(s, 4f, 5f, 7f); return () => CarController.SpeedScale = 1f; }));
            r.Register("run_slower", () => new KnobEffect((e, s) => { PlayerController.RunScale = OperatorEffect.Scale(s, 0.4f, 0.25f, 0.15f); return () => PlayerController.RunScale = 1f; }));
            r.Register("jump_higher", () => new KnobEffect((e, s) => { PlayerController.JumpScale = OperatorEffect.Scale(s, 2f, 3f, 4.5f); return () => PlayerController.JumpScale = 1f; }));
            r.Register("slow_fall", () => new KnobEffect((e, s) => { PlayerController.GravityScale = OperatorEffect.Scale(s, 0.35f, 0.2f, 0.1f); return () => PlayerController.GravityScale = 1f; }));
            r.Register("bigger", () => new SizeEffect(1.8f, 2.5f, 3.5f));
            r.Register("smaller", () => new SizeEffect(0.5f, 0.35f, 0.25f));
            r.Register("grip_down", () => new KnobEffect((e, s) => { CarFx.Traction = OperatorEffect.Scale(s, 0.1f, 0.05f, 0.025f); return () => CarFx.Traction = 1f; }));
            r.Register("slippery_ground", () => new KnobEffect((e, s) => { CarFx.Traction = OperatorEffect.Scale(s, 0.06f, 0.03f, 0.015f); return () => CarFx.Traction = 1f; }));
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
