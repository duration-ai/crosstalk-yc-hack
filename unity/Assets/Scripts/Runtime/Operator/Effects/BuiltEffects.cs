using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Glb;
using Dogpatch.Look;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    public sealed class RainEffect : OperatorEffect
    {
        WeatherKind before;
        float beforeIntensity;

        public override void Apply(string strength, EffectTarget target)
        {
            before = WorldEnvironment.Weather;
            beforeIntensity = WorldEnvironment.WeatherIntensity;
            WorldEnvironment.SetWeather(WeatherKind.Rain, Scale(strength, 0.5f, 0.8f, 1f), 2.5f);
        }

        public override void Revert() => WorldEnvironment.SetWeather(before == WeatherKind.Rain ? WeatherKind.Sun : before, beforeIntensity, 2.5f);
    }

    public sealed class NightEffect : OperatorEffect
    {
        float before;

        public override void Apply(string strength, EffectTarget target)
        {
            before = WorldEnvironment.IsNight ? WorldEnvironment.PresetHours(TimePreset.Day) : WorldEnvironment.TimeOfDay;
            WorldEnvironment.SetTime(TimePreset.Night, 2.5f);
        }

        public override void Revert() => WorldEnvironment.SetTime(before, 2.5f);
    }

    // Moon gravity for everything: physics bodies, the on-foot player, and parked cars drift up
    // the way the race's own low-gravity change does.
    public sealed class LowGravityEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 0.35f, 0.2f, 0.12f);
            Physics.gravity = new Vector3(0f, -9.81f * k, 0f);
            PlayerController.GravityScale = k;
            CarFx.Float = strength == "small" ? 0f : 1f;
            foreach (var car in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (car.Driven) CarFx.On(car).Launch(3.2f);
        }

        public override void Revert()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            PlayerController.GravityScale = 1f;
            CarFx.Float = 0f;
        }
    }

    public sealed class RunFasterEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target) => PlayerController.RunScale = Scale(strength, 2.5f, 4f, 6f);

        public override void Revert() => PlayerController.RunScale = 1f;
    }

    // Racers' cars bounce while it lasts.
    public sealed class VehicleHopEffect : OperatorEffect
    {
        HopTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            ticker = Context.Host.gameObject.AddComponent<HopTicker>();
            ticker.Up = Scale(strength, 5f, 7.5f, 10f);
            ticker.Cars = new List<CarController>();
            foreach (var tag in target.Entities)
            {
                var car = tag.GetComponent<CarController>();
                if (car != null) ticker.Cars.Add(car);
            }
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
        }
    }

    // Instant and permanent: a car takes the paint through its own colour knobs, a city piece
    // has its vertices tinted in the merged mesh, anything else is tinted per vertex.
    public sealed class RecolorEffect : OperatorEffect
    {
        public static readonly Dictionary<string, string> Hex = new Dictionary<string, string>
        {
            { "red", "#E8333A" }, { "orange", "#FF8A1E" }, { "yellow", "#FFCB1F" }, { "green", "#2EC27E" }, { "blue", "#2D8CFF" },
            { "purple", "#A35BFF" }, { "pink", "#FF3E8E" }, { "white", "#F4F4F0" }, { "black", "#1A1A22" }, { "gold", "#D4AF37" }, { "silver", "#C0C4CC" },
        };

        public static Color Linear(string color, Vector3 at)
        {
            if (color == "rainbow") return Color.HSVToRGB(Mathf.Repeat(at.y * 0.07f + (at.x + at.z) * 0.01f, 1f), 0.75f, 1f).linear;
            return PaintJob.Linear(Hex.TryGetValue(color ?? "", out var hex) ? hex : "#FF3E8E");
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        public override void Apply(string strength, EffectTarget target)
        {
            foreach (var tag in target.Entities)
            {
                if (tag.Span != null)
                {
                    tag.Span.Paint((c, at, brightest) => Shade(Linear(target.Color, at), Luma(c) / brightest));
                    continue;
                }
                var car = tag.GetComponent<CarController>();
                if (car != null && target.Color != "rainbow")
                {
                    var job = PaintJob.For(car.gameObject, car.ModelId);
                    if (job.HasPaint)
                    {
                        job.SetOverride(Linear(target.Color, Vector3.zero));
                        continue;
                    }
                }
                Tint(tag.gameObject, target.Color);
            }
        }

        static Color Shade(Color c, float k) => new Color(c.r * Mathf.Lerp(0.55f, 1f, k), c.g * Mathf.Lerp(0.55f, 1f, k), c.b * Mathf.Lerp(0.55f, 1f, k), 1f);

        static void Tint(GameObject go, string color)
        {
            void Recolor(Mesh shared, System.Action<Mesh> assign, Transform t)
            {
                if (shared == null || shared.colors.Length == 0) return;
                var mesh = Object.Instantiate(shared);
                var colors = mesh.colors;
                var vertices = mesh.vertices;
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.Lerp(colors[i], Linear(color, t.TransformPoint(vertices[i])), 0.7f);
                mesh.colors = colors;
                assign(mesh);
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) Recolor(mf.sharedMesh, m => mf.sharedMesh = m, mf.transform);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Recolor(smr.sharedMesh, m => smr.sharedMesh = m, smr.transform);
        }
    }

    // Flames on each target, nearest first; lights only on the closest few to keep iPhone happy.
    public sealed class SetFireEffect : OperatorEffect
    {
        const int MaxFires = 40;
        const int MaxLights = 4;
        readonly List<FireFx> fires = new List<FireFx>();

        public override void Apply(string strength, EffectTarget target)
        {
            var eye = Context.Game.View.transform.position;
            var order = new List<EntityTag>(target.Entities);
            order.Sort((a, b) => (a.WorldBounds.center - eye).sqrMagnitude.CompareTo((b.WorldBounds.center - eye).sqrMagnitude));
            for (int i = 0; i < order.Count && i < MaxFires; i++)
            {
                var tag = order[i];
                var b = tag.WorldBounds;
                var fire = FireFx.Create(Context.Material, b, Scale(strength, 0.8f, 1f, 1.3f), i < MaxLights);
                if (tag.Span == null) fire.transform.SetParent(tag.transform, true);
                fires.Add(fire);
            }
        }

        public override void Revert()
        {
            foreach (var fire in fires) if (fire != null) fire.Extinguish();
            fires.Clear();
        }
    }

    // A one-shot hop into the air: driven cars get airtime through CarFx, city pieces are lifted
    // out of their chunk, fly up spinning and land back where they were, people and parked cars
    // arc on their transforms.
    public sealed class LaunchEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target)
        {
            float up = Scale(strength, 16f, 24f, 34f);
            foreach (var tag in target.Entities)
            {
                var car = tag.GetComponent<CarController>();
                if (car != null && car.Driven && !car.GetComponent<Rigidbody>().isKinematic)
                {
                    CarFx.On(car).Launch(up * 0.6f);
                    continue;
                }
                if (tag.Span != null)
                {
                    var copy = tag.Span.Detach(tag.Id + " (launched)", Context.Material);
                    if (copy != null) Launched.Start(copy.transform, up, tag.Span);
                    continue;
                }
                Launched.Start(tag.transform, up, null);
            }
        }
    }

    // Stops vehicles and people in their tracks: lasting rows hold them until the row ends,
    // instant rows freeze them for a few seconds.
    public sealed class StopEffect : OperatorEffect
    {
        readonly List<Frozen> frozen = new List<Frozen>();

        public override void Apply(string strength, EffectTarget target)
        {
            foreach (var tag in target.Entities)
            {
                if (tag.Span != null) continue;
                var f = tag.gameObject.AddComponent<Frozen>();
                f.Until = Row.instant ? Time.time + Scale(strength, 2f, 3.5f, 6f) : float.MaxValue;
                frozen.Add(f);
            }
        }

        public override void Revert()
        {
            foreach (var f in frozen) if (f != null) Object.Destroy(f);
            frozen.Clear();
        }
    }

    // Every person's head blown up to cartoon size: head bones on racers and you, and the crowd
    // shader's head scale for the GPU-drawn pedestrians.
    public sealed class BigHeadsEffect : OperatorEffect
    {
        static readonly int CrowdHeadScale = Shader.PropertyToID("_CrowdHeadScale");
        readonly List<BigHead> heads = new List<BigHead>();

        public override void Apply(string strength, EffectTarget target)
        {
            float size = Scale(strength, 2.5f, 3.5f, 4.5f);
            Shader.SetGlobalFloat(CrowdHeadScale, size);
            Ambient.PedestrianSystem.HeadScale = size;
            var people = new List<GameObject>();
            foreach (var tag in EntityTag.All)
                if (EntityClasses.IsA(tag.Class, "person")) people.Add(tag.gameObject);
            if (Context.Game.Player != null) people.Add(Context.Game.Player.gameObject);
            foreach (var person in people)
            {
                var head = BigHead.FindHead(person.transform);
                if (head == null) continue;
                var big = head.gameObject.AddComponent<BigHead>();
                big.Target = size;
                heads.Add(big);
            }
            if (heads.Count == 0) Debug.Log("[Operator] big_heads: no head bones found");
        }

        public override void Revert()
        {
            Shader.SetGlobalFloat(CrowdHeadScale, 1f);
            Ambient.PedestrianSystem.HeadScale = 1f;
            foreach (var h in heads) if (h != null) h.Target = 1f;
            heads.Clear();
        }
    }
}
