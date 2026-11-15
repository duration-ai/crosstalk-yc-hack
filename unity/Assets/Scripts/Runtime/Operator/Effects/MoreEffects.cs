using System;
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
                FireFx.Explosion(Context.Material, tag.WorldBounds, Scale(strength, 1f, 1.3f, 1.7f), n <= 3);
                var car = tag.GetComponent<CarController>();
                if (car != null && car.Driven && !car.GetComponent<Rigidbody>().isKinematic) CarFx.On(car).Launch(up * 0.6f);
                else if (tag.Span == null) Launched.Start(tag.transform, up, null);
            }
        }
    }

    // Instant: the building drops into the ground in a burst of flame and is gone.
    public sealed class DemolishEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target)
        {
            foreach (var tag in target.Entities)
            {
                FireFx.Explosion(Context.Material, tag.WorldBounds, 1.2f, true);
                var copy = tag.Span != null ? tag.Span.Detach(tag.Id + " (demolished)", Context.Material) : tag.gameObject;
                if (copy != null) copy.AddComponent<Sink>().Depth = tag.WorldBounds.size.y + 1f;
            }
        }
    }

    public sealed class Sink : MonoBehaviour
    {
        public float Depth = 10f;
        float sunk;

        void Update()
        {
            float step = Depth / 1.6f * Time.deltaTime;
            transform.position += Vector3.down * step;
            transform.Rotate(0f, 0f, Mathf.Sin(Time.time * 40f) * 0.3f);
            sunk += step;
            if (sunk >= Depth) gameObject.SetActive(false);
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

    public static class MoreEffects
    {
        // Everyone: racers, their cars, traffic and pedestrians.
        static Action Racers(OperatorEffect e, float k)
        {
            foreach (var car in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None)) CarFx.On(car);
            CarFx.SizeTarget = k;
            Ambient.PedestrianSystem.SizeScale = k;
            var player = e.Context.Game.Player;
            var before = player != null ? player.transform.localScale : Vector3.one;
            if (player != null) player.transform.localScale = before * k;
            return () =>
            {
                CarFx.SizeTarget = 1f;
                Ambient.PedestrianSystem.SizeScale = 1f;
                if (player != null) player.transform.localScale = before;
            };
        }

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
            r.Register("bigger", () => new KnobEffect((e, s) => Racers(e, OperatorEffect.Scale(s, 1.8f, 2.5f, 3.5f))));
            r.Register("smaller", () => new KnobEffect((e, s) => Racers(e, OperatorEffect.Scale(s, 0.5f, 0.35f, 0.25f))));
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
