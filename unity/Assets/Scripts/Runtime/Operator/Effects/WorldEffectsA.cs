using System;
using System.Collections.Generic;
using Dogpatch.Audio;
using Dogpatch.Feel;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Smash;
using Dogpatch.WorldChanges;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dogpatch.Operator
{
    // Players, sky and physics effects (CRO-36): shape, flight, grip, armour, time of day, weather
    // grades, time warps, flood, lava, earthquakes and cars that fly, float or sail. Every effect
    // honours small/medium/large, reverts cleanly, layers on others through ShapeLayers, GuardedKnob and
    // SkyGrade, and is applied the same way on every device from its activeEffects row.
    public static class WorldEffectsA
    {
        public static void Register(EffectRegistry r)
        {
            r.Register("chunky", () => new ShapeEffect(s => new Vector3(S(s, 1.4f, 1.75f, 2.2f), S(s, 0.9f, 0.8f, 0.7f), S(s, 1.4f, 1.75f, 2.2f)), s => new Vector3(S(s, 1.25f, 1.45f, 1.7f), S(s, 0.9f, 0.8f, 0.7f), 1f)));
            r.Register("skinny", () => new ShapeEffect(s => new Vector3(S(s, 0.6f, 0.45f, 0.32f), S(s, 1.1f, 1.2f, 1.3f), S(s, 0.6f, 0.45f, 0.32f)), s => new Vector3(S(s, 0.7f, 0.55f, 0.4f), S(s, 1.1f, 1.2f, 1.3f), 1f)));
            r.Register("flight", () => new FlightEffect());
            r.Register("grip_up", () => new GripUpEffect());
            r.Register("tougher", () => new ToughEffect());
            r.Register("cars_fly", () => new CarsFlyEffect());
            r.Register("fast_day_cycle", () => new TimeLapseEffect());
            r.Register("sandstorm", () => new SkyEffect(SkyEffect.Kind.Sandstorm));
            r.Register("heatwave", () => new SkyEffect(SkyEffect.Kind.Heatwave));
            r.Register("blackout", () => new SkyEffect(SkyEffect.Kind.Blackout));
            r.Register("slow_motion", () => new TimeScaleEffect(true));
            r.Register("fast_forward", () => new TimeScaleEffect(false));
            r.Register("thick_air", () => new ThickAirEffect());
            r.Register("flood", () => new FloodEffect());
            r.Register("floor_is_lava", () => new LavaEffect());
            r.Register("earthquake", () => new EarthquakeEffect());
            r.Register("cars_on_water", () => new BoatCarsEffect());
            r.Register("floaty_crashes", () => new FloatyCrashesEffect());
            r.Register("storm", () => new StormEffect());
        }

        static float S(string s, float small, float medium, float large) => OperatorEffect.Scale(s, small, medium, large);
    }

    // chunky / skinny: racers squash wide or stretch thin, on foot and in their cars, with a wobble.
    public sealed class ShapeEffect : OperatorEffect
    {
        readonly Func<string, Vector3> person, car;
        readonly List<ShapeLayers> shaped = new List<ShapeLayers>();
        EffectTicker ticker;

        public ShapeEffect(Func<string, Vector3> person, Func<string, Vector3> car)
        {
            this.person = person;
            this.car = car;
        }

        void Shape(Transform node, Vector3 factor, float pivot)
        {
            var layers = Racers.Layers(node);
            if (layers == null || shaped.Contains(layers)) return;
            layers.Pivot = pivot;
            layers.Scale(this, factor);
            shaped.Add(layers);
        }

        public override void Apply(string strength, EffectTarget target)
        {
            void Sweep()
            {
                foreach (var body in Racers.Bodies(Context, target)) Shape(Racers.Shape(body), person(strength), 0.9f);
                foreach (var c in Racers.Cars(Context, target)) Shape(Racers.CarShape(c), car(strength), 0.6f);
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            foreach (var layers in shaped) if (layers != null) layers.Remove(this);
            shaped.Clear();
        }
    }

    // flight: on foot you fly like a caped hero, leaning into the drag; in a car it sprouts wings
    // and flies above the rooftops. Both land gently when it wears off.
    public sealed class FlightEffect : OperatorEffect
    {
        readonly List<Hover> hovers = new List<Hover>();
        readonly List<GameObject> props = new List<GameObject>();
        readonly List<ShapeLayers> tilted = new List<ShapeLayers>();
        EffectTicker ticker;
        Flyer flyer;
        GuardedKnob run;

        public override void Apply(string strength, EffectTarget target)
        {
            float onFoot = Scale(strength, 5f, 9f, 14f);
            float inCar = Scale(strength, 4f, 7f, 11f);
            run = GuardedKnob.Times(() => PlayerController.RunScale, v => PlayerController.RunScale = v, Scale(strength, 1.3f, 1.6f, 2f));
            void Sweep()
            {
                var player = Context.Game.Player;
                if (player != null && player.isActiveAndEnabled && flyer == null)
                {
                    flyer = player.gameObject.AddComponent<Flyer>();
                    flyer.Height = onFoot;
                    var shape = Racers.Shape(player.Visual);
                    if (shape != null)
                    {
                        var cape = PropMeshes.Attach(shape, "Cape (operator)", PropMeshes.Cape(), Context.Material, new Vector3(0f, 1.42f, 0f));
                        var flutter = cape.AddComponent<Flutter>();
                        flutter.Player = player;
                        flutter.Hang(shape);
                        Pop.In(cape);
                        props.Add(cape);
                        var layers = Racers.Layers(shape);
                        layers.Pivot = 0.9f;
                        layers.Tilt(this, () => Quaternion.Euler(70f * Mathf.Clamp01(player.Speed / 9f), 0f, 0f));
                        tilted.Add(layers);
                    }
                }
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (hovers.Exists(h => h != null && h.gameObject == car.gameObject)) continue;
                    var shape = Racers.CarShape(car);
                    if (shape == null) continue;
                    var wings = PropMeshes.Attach(shape, "Wings (operator)", PropMeshes.Wings(Racers.Box(car)), Context.Material, Vector3.zero);
                    Pop.In(wings);
                    props.Add(wings);
                    var hover = Hover.On(car);
                    hover.Set(this, inCar);
                    hovers.Add(hover);
                }
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.4f;
                Sweep();
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            if (flyer != null) flyer.Land();
            foreach (var h in hovers) if (h != null) h.Remove(this);
            foreach (var p in props) Pop.Out(p);
            foreach (var l in tilted) if (l != null) l.Remove(this);
            run?.Undo();
            hovers.Clear();
            props.Clear();
            tilted.Clear();
        }
    }

    // Holds the on-foot player at a height above whatever is under (and just ahead of) them, then
    // brings them down at a gentle sink rate.
    [DefaultExecutionOrder(-20)]
    public sealed class Flyer : MonoBehaviour
    {
        public float Height = 9f;
        PlayerController player;
        CharacterController body;
        bool landing;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            body = GetComponent<CharacterController>();
            player.Flying = true;
        }

        void OnDestroy() => player.Flying = false;

        public void Land() => landing = true;

        void Update()
        {
            var p = transform.position;
            if (landing)
            {
                player.VerticalVelocity = Mathf.Max(player.VerticalVelocity, -5f);
                if (body.isGrounded) Destroy(this);
                return;
            }
            var ahead = p + transform.forward * Mathf.Max(player.Speed, 0f) * 0.7f;
            float ground = Mathf.Max(Ground(p), Ground(ahead));
            float target = ground + Height * CarController.LiftScale + Mathf.Sin(Time.time * 2.1f) * 0.35f;
            player.VerticalVelocity = Mathf.Clamp((target - p.y) * 2.4f, -7f, 9f);
        }

        float Ground(Vector3 at) =>
            Physics.Raycast(new Vector3(at.x, transform.position.y + 0.2f, at.z), Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : transform.position.y - Height;
    }

    // A cape hung behind both shoulders that streams back and up with speed and flutters. It follows
    // the shoulder bones as the body animates, and never swings forward into the back or legs.
    public sealed class Flutter : MonoBehaviour
    {
        public PlayerController Player;
        Transform left, right, neck;
        Vector3 fallback;

        public void Hang(Transform body)
        {
            fallback = transform.localPosition;
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (left == null && n.EndsWith("leftshoulder")) left = t;
                else if (right == null && n.EndsWith("rightshoulder")) right = t;
                else if (neck == null && n.EndsWith("neck")) neck = t;
            }
        }

        void LateUpdate()
        {
            var parent = transform.parent;
            if (parent == null || Player == null) return;
            var up = parent.up;
            var back = Vector3.ProjectOnPlane(-Player.transform.forward, up);
            if (back.sqrMagnitude < 1e-4f) back = -parent.forward;
            back.Normalize();
            float scale = parent.lossyScale.y;
            Vector3? top = left != null && right != null ? (left.position + right.position) * 0.5f : neck != null ? neck.position : (Vector3?)null;
            transform.position = (top ?? parent.TransformPoint(fallback)) + (back * 0.16f - up * 0.03f) * scale;
            float speed = Mathf.Clamp01(Player.Speed / 9f);
            float flap = Mathf.Sin(Time.time * (5f + speed * 9f)) * (3f + speed * 7f);
            transform.rotation = Quaternion.LookRotation(-back, up) * Quaternion.Euler(10f + speed * 24f + flap, 0f, Mathf.Sin(Time.time * 3.1f) * 3f);
        }
    }

    // grip_up: racers' cars get fat racing slicks and corner on rails, wet or dry.
    public sealed class GripUpEffect : OperatorEffect
    {
        readonly List<(CarController car, HandlingProfile mine, HandlingProfile before)> tuned = new List<(CarController, HandlingProfile, HandlingProfile)>();
        readonly List<ShapeLayers> tyres = new List<ShapeLayers>();
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 1.2f, 1.35f, 1.6f);
            var fat = new Vector3(Scale(strength, 1.6f, 1.9f, 2.3f), Scale(strength, 1.1f, 1.18f, 1.28f), Scale(strength, 1.1f, 1.18f, 1.28f));
            void Sweep()
            {
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (tuned.Exists(t => t.car == car)) continue;
                    var before = car.Handling;
                    var mine = before.Clone();
                    mine.Grip = Mathf.Max(before.Grip, 1f) * 1.3f;
                    mine.AllWeather = true;
                    mine.TurnRate = before.TurnRate * k;
                    mine.Acceleration = before.Acceleration * k;
                    mine.Braking = before.Braking * k;
                    car.Handling = mine;
                    tuned.Add((car, mine, before));
                    foreach (var t in car.GetComponentsInChildren<Transform>())
                    {
                        if (!t.name.StartsWith("wheel-") || t.name.EndsWith("-mesh")) continue;
                        var layers = Racers.Layers(t);
                        layers.Pivot = 0f;
                        layers.Scale(this, fat);
                        tyres.Add(layers);
                    }
                    MotionFx.Land(car.transform.position, 0.6f);
                }
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            foreach (var (car, mine, before) in tuned)
                if (car != null && car.Handling == mine) car.Handling = before;
            foreach (var t in tyres) if (t != null) t.Remove(this);
            tuned.Clear();
            tyres.Clear();
        }
    }

    // tougher: racers' cars are patched up and bolted into steel plates and a ram bar, and take a
    // fraction of the damage; racers on foot get a knight's helmet.
    public sealed class ToughEffect : OperatorEffect
    {
        readonly List<GameObject> props = new List<GameObject>();
        readonly HashSet<Object> armoured = new HashSet<Object>();
        EffectTicker ticker;
        GuardedKnob durability;
        float armour = 1f;

        // How many times tougher armour makes driven cars. Crash damage already scales with it
        // through durability; blasts and lightning, which burn a share of health, divide by it.
        public static float Armour = 1f;

        public override void Apply(string strength, EffectTarget target)
        {
            var tuning = DamageTuning.Current;
            armour = Scale(strength, 3f, 5f, 10f);
            Armour *= armour;
            durability = GuardedKnob.Times(() => tuning.playerDurabilityScale, v => tuning.playerDurabilityScale = v, armour);
            void Sweep()
            {
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!armoured.Add(car)) continue;
                    if (car.TryGetComponent<VehicleDamage>(out var damage) && !damage.IsWrecked) damage.Repair();
                    var shape = Racers.CarShape(car);
                    if (shape == null) continue;
                    var plates = PropMeshes.Attach(shape, "Armour (operator)", PropMeshes.Armour(Racers.Box(car)), Context.Material, Vector3.zero);
                    Pop.In(plates);
                    props.Add(plates);
                    MotionFx.Land(car.transform.position, 0.8f);
                }
                foreach (var body in Racers.Bodies(Context, target))
                {
                    if (!armoured.Add(body)) continue;
                    var head = BigHead.FindHead(body);
                    if (head == null) continue;
                    var helmet = PropMeshes.Attach(head, "Helmet (operator)", PropMeshes.Helmet(), Context.Material, Vector3.zero);
                    var lossy = head.lossyScale;
                    helmet.transform.localScale = new Vector3(1f / Mathf.Max(lossy.x, 1e-3f), 1f / Mathf.Max(lossy.y, 1e-3f), 1f / Mathf.Max(lossy.z, 1e-3f));
                    helmet.transform.position = head.position + Vector3.up * 0.08f;
                    helmet.transform.rotation = body.rotation;
                    Pop.In(helmet);
                    props.Add(helmet);
                }
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            durability?.Undo();
            Armour /= armour;
            armour = 1f;
            foreach (var p in props) Pop.Out(p);
            props.Clear();
            armoured.Clear();
        }
    }

    // cars_fly: every car takes off. Racers' cars sprout wings and really fly; parked and traffic
    // cars lift off their wheels and bob along a few metres up.
    public sealed class CarsFlyEffect : OperatorEffect
    {
        readonly List<Hover> hovers = new List<Hover>();
        readonly List<ShapeLayers> lifted = new List<ShapeLayers>();
        readonly List<GameObject> props = new List<GameObject>();
        readonly HashSet<CarController> done = new HashSet<CarController>();
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            float fly = Scale(strength, 4f, 7f, 11f);
            float lift = Scale(strength, 2.5f, 4.5f, 7f);
            int seed = Racers.Seed(Row?.id);
            void Sweep()
            {
                foreach (var car in Racers.Cars(Context, target)) Fly(car, fly, true);
                if (!string.IsNullOrEmpty(target.Owner)) return;
                var eye = Context.Game.View.transform.position;
                foreach (var car in Racers.Nearby(eye, 220f, 90))
                {
                    if (Racers.Physical(car) || Racers.IsRacer(car)) Fly(car, fly, Racers.IsRacer(car));
                    else if (done.Add(car))
                    {
                        var layers = Racers.Layers(Racers.CarShape(car));
                        if (layers == null) continue;
                        float h = lift * (0.7f + 0.6f * Racers.Hash(seed, car.GetInstanceID()));
                        float phase = Racers.Hash(car.GetInstanceID(), seed) * 6.3f;
                        layers.Pivot = 0.6f;
                        layers.Lift(this, () => h + Mathf.Sin(Time.time * 1.3f + phase) * 0.5f);
                        layers.Tilt(this, () => Quaternion.Euler(Mathf.Sin(Time.time * 0.9f + phase) * 6f, 0f, Mathf.Cos(Time.time * 0.7f + phase) * 5f));
                        lifted.Add(layers);
                    }
                }
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
        }

        void Fly(CarController car, float height, bool withWings)
        {
            if (!done.Add(car)) return;
            var shape = withWings ? Racers.CarShape(car) : null;
            if (shape != null)
            {
                var wings = PropMeshes.Attach(shape, "Wings (operator)", PropMeshes.Wings(Racers.Box(car)), Context.Material, Vector3.zero);
                Pop.In(wings);
                props.Add(wings);
            }
            if (!Racers.Physical(car)) return;
            var hover = Hover.On(car);
            hover.Set(this, height);
            hovers.Add(hover);
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            foreach (var h in hovers) if (h != null) h.Remove(this);
            foreach (var l in lifted) if (l != null) l.Remove(this);
            foreach (var p in props) Pop.Out(p);
            hovers.Clear();
            lifted.Clear();
            props.Clear();
            done.Clear();
        }
    }

    // fast_day_cycle: the sun races across the sky; whole days and nights go by in seconds.
    public sealed class TimeLapseEffect : OperatorEffect
    {
        EffectTicker ticker;
        float before;

        public override void Apply(string strength, EffectTarget target)
        {
            before = WorldEnvironment.TimeOfDay;
            float hoursPerSecond = Scale(strength, 1.5f, 3f, 6f);
            ticker = EffectTicker.On(Context, () => WorldEnvironment.SetTime(WorldEnvironment.TimeOfDay + hoursPerSecond * Time.deltaTime, 0f));
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            WorldEnvironment.SetTime(before, 2.5f);
        }
    }

    // sandstorm, heatwave, blackout: a sky grade, plus what blows through it.
    public sealed class SkyEffect : OperatorEffect
    {
        public enum Kind { Sandstorm, Heatwave, Blackout }

        readonly Kind kind;
        SkyGrade.Grade grade;
        Material sand;
        GameObject sandGo;
        Swarm swarm;
        Light lamp;
        EffectTicker ticker;
        bool signalsOff;
        readonly List<CarController> windy = new List<CarController>();

        public SkyEffect(Kind kind) => this.kind = kind;

        static Color C(string hex) => PropMeshes.C(hex);

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 0.65f, 1f, 1.35f);
            var eye = Context.Game.View.transform;
            int seed = Racers.Seed(Row?.id);
            switch (kind)
            {
                case Kind.Sandstorm:
                    grade = SkyGrade.Add(new SkyGrade.Grade
                    {
                        Top = C("#B9783F"), Mid = C("#D99A57"), Horizon = C("#EDC283"),
                        Ambient = new Color(1.15f, 0.95f, 0.75f), Sun = new Color(1f, 0.78f, 0.52f), SunScale = Mathf.Lerp(1f, 0.5f, k),
                        FogReach = Scale(strength, 0.3f, 0.16f, 0.09f), Fog = C("#E2B279"), FogWeight = 1f, Clouds = new Color(1f, 0.82f, 0.6f),
                        Wind = new Vector4(16f * k, 0f, 5f * k, 0f), Seconds = 1.2f,
                    });
                    sand = Precipitation.Create(Context.Host.transform, true);
                    sandGo = Context.Host.transform.GetChild(Context.Host.transform.childCount - 1).gameObject;
                    sand.SetColor("_Color", new Color(0.93f, 0.74f, 0.47f, 0.9f));
                    sand.SetFloat("_Fall", 0.5f);
                    sand.SetFloat("_Box", 22f);
                    sand.SetVector("_Size", new Vector4(0.05f, 0.05f, 0f, 0f));
                    var weeds = new Mesh[] { PropMeshes.Tumbleweed(seed), PropMeshes.Tumbleweed(seed + 7) };
                    swarm = Swarm.Create("Tumbleweeds (operator)", weeds[0], Mathf.RoundToInt(6 * k), Context.Material, i =>
                    {
                        float t = Time.time + i * 3.1f;
                        float lap = 7f;
                        float u = Mathf.Repeat(t, lap) / lap;
                        var e = eye.position;
                        var p = new Vector3(e.x - 30f + u * 60f, 0f, e.z - 18f + Racers.Hash(i, Mathf.Floor(t / lap)) * 36f);
                        p.y = Racers.GroundY(p) + 0.7f + Mathf.Abs(Mathf.Sin(t * 3f)) * 1.2f;
                        return Matrix4x4.TRS(p, Quaternion.Euler(0f, 20f * i, -t * 260f), Vector3.one * (0.8f + 0.4f * Racers.Hash(i, 3)));
                    }, true);
                    var gustDir = new Vector3(16f, 0f, 5f).normalized;
                    float gustSpeed = Scale(strength, 1.8f, 2.5f, 3.5f);
                    ticker = EffectTicker.On(Context, () =>
                    {
                        sand.SetFloat("_Amount", grade.Weight * k);
                        float gust = Mathf.Clamp01(Mathf.Sin(Time.time * Mathf.PI * 0.4f) * 1.6f - 0.3f) * grade.Weight;
                        foreach (var car in Racers.Cars(Context, target))
                        {
                            if (!Racers.Physical(car)) continue;
                            float heft = car.Heavy ? 0.5f : car.Tiny || car.Handling.Mass < 200f ? 1.5f : 1f;
                            CarFx.On(car).Wind = gustDir * (gustSpeed * gust * heft);
                            if (!windy.Contains(car)) windy.Add(car);
                        }
                    });
                    break;
                case Kind.Heatwave:
                    grade = SkyGrade.Add(new SkyGrade.Grade
                    {
                        Top = C("#2F8DE8"), Mid = C("#9FD4F2"), Horizon = C("#FFE6A6"),
                        Ambient = new Color(1.2f, 1.05f, 0.8f), Sun = new Color(1f, 0.85f, 0.6f), SunScale = Mathf.Lerp(1f, 1.45f, k),
                        FogReach = Mathf.Lerp(1f, 0.6f, k), Fog = C("#FFE0A0"), FogWeight = 0.8f, Clouds = new Color(1f, 0.93f, 0.8f), Seconds = 1.2f,
                    });
                    var fx = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Heat haze (operator)" };
                    var wisp = PropMeshes.Wisp(new Color(1f, 0.97f, 0.88f, 0.22f));
                    swarm = Swarm.Create("Heat haze (operator)", wisp, Mathf.RoundToInt(26 * k), fx, i =>
                    {
                        float life = 2.6f + Racers.Hash(i, 1) * 1.4f;
                        float t = Time.time + Racers.Hash(i, 2) * life;
                        float u = Mathf.Repeat(t, life) / life;
                        float cycle = Mathf.Floor(t / life);
                        var e = eye.position;
                        float a = Racers.Hash(i, cycle) * Mathf.PI * 2f, r = 5f + Racers.Hash(cycle, i) * 22f;
                        var p = new Vector3(e.x + Mathf.Cos(a) * r, 0f, e.z + Mathf.Sin(a) * r);
                        p.y = Racers.GroundY(p) + u * 2.5f;
                        float s = Mathf.Sin(u * Mathf.PI) * grade.Weight;
                        var face = Quaternion.LookRotation(new Vector3(p.x - e.x, 0f, p.z - e.z));
                        return Matrix4x4.TRS(p, face * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 5f + i) * 8f), new Vector3(1f + Mathf.Sin(t * 4f + i) * 0.3f, 1f, 1f) * (s * 1.4f));
                    });
                    break;
                case Kind.Blackout:
                    Dogpatch.Traffic.StreetGraph.SignalsDead++;
                    signalsOff = true;
                    float start = Time.unscaledTime;
                    grade = SkyGrade.Add(new SkyGrade.Grade
                    {
                        Top = C("#02040C"), Mid = C("#060B1E"), Horizon = C("#101A3A"),
                        Ambient = new Color(0.3f, 0.34f, 0.5f) * Mathf.Lerp(1f, 0.7f, k - 0.65f), Sun = new Color(0.6f, 0.7f, 1f), SunScale = Scale(strength, 0.25f, 0.12f, 0.06f),
                        FogReach = 0.55f, Fog = C("#0A1230"), FogWeight = 1f, Clouds = new Color(0.25f, 0.27f, 0.36f), Glow = 0f, Seconds = 0.6f,
                    });
                    grade.Flicker = () =>
                    {
                        float t = Time.unscaledTime - start;
                        if (grade.Target <= 0f || t > 1f) return 1f;
                        return Racers.Hash(Mathf.Floor(t * 14f), 5f) > 0.45f ? 1f : 0.15f;
                    };
                    ticker = EffectTicker.On(Context, () =>
                    {
                        var car = Context.Game.CurrentCar;
                        if (car == null)
                        {
                            if (lamp != null) lamp.enabled = false;
                            return;
                        }
                        if (lamp == null)
                        {
                            lamp = new GameObject("Blackout headlights (operator)").AddComponent<Light>();
                            lamp.type = LightType.Spot;
                            lamp.range = 34f;
                            lamp.spotAngle = 70f;
                            lamp.color = SpeedDial.Hex("#FFF3DA");
                            lamp.shadows = LightShadows.None;
                            lamp.renderMode = LightRenderMode.ForcePixel;
                        }
                        if (lamp.transform.parent != car.transform)
                        {
                            lamp.transform.SetParent(car.transform, false);
                            lamp.transform.localPosition = new Vector3(0f, 0.9f, car.HalfExtents.z + 0.2f);
                            lamp.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
                        }
                        lamp.enabled = true;
                        lamp.intensity = 2.8f * grade.Weight;
                    });
                    break;
            }
        }

        public override void Revert()
        {
            SkyGrade.Remove(grade);
            if (ticker != null) Object.Destroy(ticker);
            foreach (var car in windy) if (car != null) CarFx.On(car).Wind = Vector3.zero;
            windy.Clear();
            if (signalsOff) Dogpatch.Traffic.StreetGraph.SignalsDead = Mathf.Max(0, Dogpatch.Traffic.StreetGraph.SignalsDead - 1);
            signalsOff = false;
            if (lamp != null) Object.Destroy(lamp.gameObject);
            if (swarm != null) Object.Destroy(swarm.gameObject);
            if (sandGo != null) Object.Destroy(sandGo);
        }
    }

    // slow_motion / fast_forward: the whole world's clock runs slower or faster.
    public sealed class TimeScaleEffect : OperatorEffect
    {
        readonly bool slow;
        TimeWarp warp;
        SkyGrade.Grade grade;

        public TimeScaleEffect(bool slow) => this.slow = slow;

        public override void Apply(string strength, EffectTarget target)
        {
            warp = TimeWarp.Begin(Context.Host.gameObject, slow ? Scale(strength, 0.6f, 0.4f, 0.25f) : Scale(strength, 1.4f, 1.8f, 2.4f));
            if (slow)
                grade = SkyGrade.Add(new SkyGrade.Grade
                {
                    Top = PropMeshes.C("#3C5FB0"), Mid = PropMeshes.C("#7393D2"), Horizon = PropMeshes.C("#CCD8F2"), Sky = 0.6f,
                    Ambient = new Color(0.9f, 0.95f, 1.12f), Sun = new Color(0.85f, 0.92f, 1.1f), FogReach = 0.85f, Seconds = 0.5f,
                });
        }

        public override void Revert()
        {
            if (warp != null) warp.End();
            SkyGrade.Remove(grade);
        }
    }

    // thick_air: the air turns to pink syrup. Everything moves slowly, jumps float, debris drifts,
    // and bubbles hang in the air around you.
    public sealed class ThickAirEffect : OperatorEffect
    {
        readonly List<Syrup> syrups = new List<Syrup>();
        readonly List<GuardedKnob> knobs = new List<GuardedKnob>();
        SkyGrade.Grade grade;
        Swarm bubbles;
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 0.65f, 1f, 1.4f);
            grade = SkyGrade.Add(new SkyGrade.Grade
            {
                Top = PropMeshes.C("#B866A8"), Mid = PropMeshes.C("#DE90BC"), Horizon = PropMeshes.C("#F7C6D4"),
                Ambient = new Color(1.12f, 0.92f, 1.02f), Sun = new Color(1f, 0.85f, 0.92f), FogReach = Mathf.Lerp(1f, 0.3f, k * 0.75f), Fog = PropMeshes.C("#F0B6CA"), FogWeight = 1f,
                Clouds = new Color(1f, 0.8f, 0.92f), Seconds = 1f,
            });
            knobs.Add(GuardedKnob.Times(() => PlayerController.RunScale, v => PlayerController.RunScale = v, Scale(strength, 0.65f, 0.5f, 0.35f)));
            knobs.Add(GuardedKnob.Times(() => PlayerController.GravityScale, v => PlayerController.GravityScale = v, Scale(strength, 0.5f, 0.35f, 0.25f)));
            knobs.Add(GuardedKnob.Times(() => PlayerController.JumpScale, v => PlayerController.JumpScale = v, 1.3f));
            knobs.Add(GuardedKnob.Times(() => Physics.gravity.y, v => Physics.gravity = new Vector3(0f, v, 0f), Scale(strength, 0.6f, 0.45f, 0.3f)));
            var eye = Context.Game.View.transform;
            var fx = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Bubbles (operator)" };
            bubbles = Swarm.Create("Bubbles (operator)", PropMeshes.Bubble(new Color(1f, 0.92f, 0.97f, 0.4f)), Mathf.RoundToInt(36 * k), fx, i =>
            {
                float t = Time.time * 0.25f + i * 7.3f;
                var e = eye.position;
                var p = new Vector3(
                    e.x + Mathf.Repeat(Racers.Hash(i, 1) * 40f + Mathf.Sin(t) * 3f - e.x, 40f) - 20f,
                    e.y - 3f + Mathf.Repeat(Racers.Hash(i, 2) * 10f + t * 0.8f, 10f),
                    e.z + Mathf.Repeat(Racers.Hash(i, 3) * 40f + Mathf.Cos(t * 0.8f) * 3f - e.z, 40f) - 20f);
                float s = (0.12f + Racers.Hash(i, 4) * 0.3f) * grade.Weight;
                return Matrix4x4.TRS(p, Quaternion.Euler(t * 40f, t * 30f, 0f), Vector3.one * s);
            });
            float c = Scale(strength, 0.5f, 0.9f, 1.4f);
            void Sweep()
            {
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!Racers.Physical(car) || syrups.Exists(s => s != null && s.gameObject == car.gameObject)) continue;
                    var syrup = Syrup.On(car);
                    syrup.Set(this, () => c);
                    syrups.Add(syrup);
                }
            }
            Sweep();
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                Sweep();
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            SkyGrade.Remove(grade);
            if (bubbles != null) Object.Destroy(bubbles.gameObject);
            foreach (var s in syrups) if (s != null) s.Remove(this);
            for (int i = knobs.Count - 1; i >= 0; i--) knobs[i].Undo();
            syrups.Clear();
            knobs.Clear();
        }
    }

    // flood: turquoise water rises over the streets. Parked and traffic cars float up and bob,
    // rubber ducks paddle about, racers' cars spray and wade, people on foot wade slowly.
    public sealed class FloodEffect : OperatorEffect
    {
        static readonly List<Sheet> floods = new List<Sheet>();

        public static float Level
        {
            get
            {
                float level = float.NegativeInfinity;
                foreach (var s in floods) if (s != null) level = Mathf.Max(level, s.Level);
                return level;
            }
        }

        readonly List<ShapeLayers> floated = new List<ShapeLayers>();
        readonly List<Syrup> syrups = new List<Syrup>();
        readonly HashSet<CarController> seen = new HashSet<CarController>();
        readonly HashSet<CarController> wet = new HashSet<CarController>();
        readonly Dictionary<CarController, float> nextStall = new Dictionary<CarController, float>();
        const float WadeDepth = 0.3f, StallDepth = 0.9f, WaterSpeed = 0.6f, WaterGrip = 0.5f;
        Sheet sheet;

        // Only a car driving through the water wades. A boat (the duck boat, or a car cars_on_water
        // gave a hull) sails the flood at its own speed, a supersized car stands above it, and a car
        // off the ground (flying, hovering, launched, hopping) or phasing (ghost, see-through) passes
        // over or through it, as with every other ground effect.
        static bool Wades(CarController car) =>
            car.Handling.Draft <= 0f && car.transform.localScale.y < PlayerController.GiantScale && !BoatCarsEffect.Sails(car) && car.OnGround;
        GuardedKnob wade;
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            float depth = Scale(strength, 0.4f, 0.85f, 1.5f);
            var eye = Context.Game.View.transform;
            Color deep = PropMeshes.C("#17B2D0"), light = PropMeshes.C("#3FC6DE"), foam = PropMeshes.C("#DFF6FA");
            sheet = Sheet.Create("Flood (operator)", Context.Material, eye, -0.7f, depth, (x, z) =>
            {
                float h = Racers.Hash(x * 0.37f, z * 0.51f);
                return h > 0.93f ? foam : h > 0.55f ? light : deep;
            });
            sheet.Speed = (depth + 0.7f) / 2.2f;
            sheet.Amplitude = 0.2f;
            floods.Add(sheet);
            var floor = new World.MeshBuilder();
            floor.Quad(new Vector3(-3000f, 0f, -3000f), new Vector3(-3000f, 0f, 3000f), new Vector3(3000f, 0f, 3000f), new Vector3(3000f, 0f, -3000f), deep, Vector3.up);
            sheet.Floor = PropMeshes.Attach(sheet.transform, "Flood floor", floor.ToMesh("flood floor"), Context.Material, Vector3.zero, false).transform;
            var ducks = Swarm.Create("Ducks (operator)", PropMeshes.Duck(), 14, Context.Material, i =>
            {
                var e = eye.position;
                float a = Racers.Hash(i, 9) * 6.3f + Time.time * 0.06f * (i % 2 == 0 ? 1f : -1f);
                float r = 6f + Racers.Hash(9, i) * 24f;
                var p = new Vector3(e.x + Mathf.Cos(a) * r, sheet.Level - 0.12f + Mathf.Sin(Time.time * 2f + i) * 0.06f, e.z + Mathf.Sin(a) * r);
                return Matrix4x4.TRS(p, Quaternion.Euler(Mathf.Sin(Time.time * 1.7f + i) * 6f, -a * Mathf.Rad2Deg, 0f), Vector3.one * 1.2f);
            }, true);
            ducks.transform.SetParent(sheet.transform, false);
            wade = GuardedKnob.Times(() => PlayerController.WadeScale, v => PlayerController.WadeScale = v, Scale(strength, 0.8f, 0.65f, 0.5f));
            float drag = Scale(strength, 0.25f, 0.45f, 0.7f);
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                float level = sheet.Level;
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!Racers.Physical(car)) continue;
                    float depth = level - car.transform.position.y;
                    bool wading = depth > WadeDepth && Wades(car);
                    if (wading != wet.Contains(car))
                    {
                        var fx = CarFx.On(car);
                        if (wading)
                        {
                            wet.Add(car);
                            car.SpeedMul *= WaterSpeed;
                            fx.Grip *= WaterGrip;
                        }
                        else
                        {
                            wet.Remove(car);
                            car.SpeedMul /= WaterSpeed;
                            fx.Grip /= WaterGrip;
                        }
                    }
                    if (wading && depth > StallDepth && (!nextStall.TryGetValue(car, out var stallAt) || Time.time >= stallAt))
                    {
                        nextStall[car] = Time.time + 4f;
                        car.Stun(1f);
                        MotionFx.Splash(car.transform.position, 1f);
                        Haptics.Impact(HapticStyle.Heavy, 0.7f);
                    }
                    if (depth < 0f) continue;
                    float speed = Mathf.Abs(car.Speed);
                    if (speed > 2f) MotionFx.Splash(car.transform.position - car.transform.forward * car.HalfExtents.z, Mathf.Clamp01(speed / 18f));
                }
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!Racers.Physical(car) || !seen.Add(car)) continue;
                    var syrup = Syrup.On(car);
                    var t = car.transform;
                    syrup.Set(this, () => t.position.y < sheet.Level && Wades(car) ? drag : 0f);
                    syrups.Add(syrup);
                }
                foreach (var car in Racers.Nearby(eye.position, 200f, 80))
                {
                    if (Racers.Physical(car) || Racers.IsRacer(car) || !seen.Add(car)) continue;
                    var layers = Racers.Layers(Racers.CarShape(car));
                    if (layers == null) continue;
                    float phase = Racers.Hash(car.GetInstanceID(), 3) * 6.3f;
                    layers.Pivot = 0.6f;
                    layers.Lift(this, () => Mathf.Max(0f, sheet.Level * 0.8f) + Mathf.Sin(Time.time * 1.6f + phase) * 0.12f);
                    layers.Tilt(this, () => Quaternion.Euler(Mathf.Sin(Time.time * 1.1f + phase) * 4f * Mathf.Clamp01(sheet.Level), 0f, Mathf.Cos(Time.time * 1.3f + phase) * 5f * Mathf.Clamp01(sheet.Level)));
                    floated.Add(layers);
                }
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            if (sheet != null)
            {
                sheet.Speed = 1f;
                sheet.Sink();
            }
            floods.Remove(sheet);
            foreach (var l in floated) if (l != null) l.Remove(this);
            foreach (var s in syrups) if (s != null) s.Remove(this);
            foreach (var car in wet)
            {
                if (car == null) continue;
                car.SpeedMul /= WaterSpeed;
                CarFx.On(car).Grip /= WaterGrip;
            }
            wet.Clear();
            nextStall.Clear();
            wade?.Undo();
            floated.Clear();
            syrups.Clear();
            seen.Clear();
        }
    }

    // floor_is_lava: glowing, bubbling lava rises over every street and sidewalk. Touch it on foot
    // and you hop off it in a puff of smoke; racers' cars bounce on hot tyres. Rooftops, car roofs
    // and the freeway are safe.
    public sealed class LavaEffect : OperatorEffect
    {
        readonly Dictionary<CarController, float> nextHop = new Dictionary<CarController, float>();
        Sheet sheet;
        GuardedKnob jump;
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            var eye = Context.Game.View.transform;
            Color hot = PropMeshes.C("#FFE14A"), orange = PropMeshes.C("#FF8A1E"), red = PropMeshes.C("#FF4A1C"), crust = PropMeshes.C("#6E1A0E");
            var fx = new Material(Resources.Load<Shader>("Shaders/FxUnlit")) { name = "Lava (operator)" };
            sheet = Sheet.Create("Lava (operator)", fx, eye, -0.4f, 0.12f, (x, z) =>
            {
                float h = Racers.Hash(x * 0.41f, z * 0.29f);
                return h > 0.82f ? hot : h > 0.45f ? orange : h > 0.14f ? red : crust;
            });
            sheet.Speed = 0.7f;
            sheet.Amplitude = 0.06f;
            sheet.FadeEdges = true;
            jump = GuardedKnob.Times(() => PlayerController.JumpScale, v => PlayerController.JumpScale = v, Scale(strength, 1.15f, 1.35f, 1.6f));
            float bounce = Scale(strength, 2.5f, 3.5f, 5f);
            float burn = Scale(strength, 0.03f, 0.04f, 0.06f);
            ticker = EffectTicker.On(Context, () =>
            {
                float level = sheet.Level;
                var player = Context.Game.Player;
                if (player != null && player.isActiveAndEnabled && player.TryGetComponent<CharacterController>(out var body) && body.isGrounded && player.transform.position.y < level + 0.35f)
                {
                    player.Jump();
                    MotionFx.Land(player.transform.position, 0.45f);
                    Haptics.Impact(HapticStyle.Light, 0.6f);
                }
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!Racers.Physical(car) || !car.OnGround) continue;
                    var p = car.transform.position;
                    if (p.y > level + 0.5f || (nextHop.TryGetValue(car, out var at) && Time.time < at)) continue;
                    if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    nextHop[car] = Time.time + 0.6f;
                    CarFx.On(car).Launch(bounce);
                    if (car.TryGetComponent<VehicleDamage>(out var damage)) damage.Burn(burn);
                    MotionFx.Land(p, 0.7f);
                }
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            if (sheet != null) sheet.Sink();
            jump?.Undo();
        }
    }

    // earthquake: the camera rumbles, the street cracks open in dusty fissures, cars jolt and hop,
    // parked cars rattle and you stumble into little hops.
    public sealed class EarthquakeEffect : OperatorEffect
    {
        readonly List<GameObject> cracks = new List<GameObject>();
        readonly List<ShapeLayers> rattled = new List<ShapeLayers>();
        readonly HashSet<CarController> seen = new HashSet<CarController>();
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            float k = Scale(strength, 0.6f, 1f, 1.5f);
            var rng = new System.Random(Racers.Seed(Row?.id));
            float R() => (float)rng.NextDouble();
            CameraFeel.Instance?.AddTrauma(0.5f * k);
            float nextJolt = 0f, nextCrack = 0f, nextDust = 0f, nextSweep = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                float now = Time.time;
                CameraFeel.Instance?.AddTrauma(Mathf.Min(1.5f * Time.unscaledDeltaTime * 0.92f, 0.1f));
                var focus = Context.Game.Focus;
                var eye = focus != null ? focus.position : Context.Game.View.transform.position;
                if (now >= nextJolt)
                {
                    nextJolt = now + Mathf.Lerp(0.5f, 1.1f, R()) / k;
                    CameraFeel.Instance?.AddTrauma(0.22f * k);
                    Haptics.Impact(HapticStyle.Heavy, Mathf.Clamp01(0.6f * k));
                    foreach (var car in Racers.Cars(Context, target))
                        if (Racers.Physical(car) && car.OnGround) CarFx.On(car).Push(new Vector3((R() - 0.5f) * 4f * k, Mathf.Lerp(1.5f, 3f, R()) * k, (R() - 0.5f) * 4f * k));
                    var player = Context.Game.Player;
                    if (player != null && player.isActiveAndEnabled && !player.Giant) player.Jump();
                }
                if (now >= nextDust)
                {
                    nextDust = now + 0.18f / k;
                    float a = R() * Mathf.PI * 2f, r = 3f + R() * 18f;
                    var p = eye + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    p.y = Racers.GroundY(p);
                    MotionFx.Land(p, 0.5f + 0.4f * R());
                }
                if (now >= nextCrack && cracks.Count < Mathf.RoundToInt(6 * k))
                {
                    nextCrack = now + 1.6f / k;
                    var fwd = Context.Game.View.transform.forward;
                    fwd.y = 0f;
                    var p = eye + fwd.normalized * (6f + R() * 12f) + new Vector3((R() - 0.5f) * 10f, 0f, (R() - 0.5f) * 10f);
                    p.y = Racers.GroundY(p, 4f) + 0.04f;
                    var crack = PropMeshes.Attach(null, "Crack (operator)", PropMeshes.Crack(rng.Next(), Mathf.Lerp(5f, 11f, R()) * k), Context.Material, p, false);
                    crack.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
                    Pop.In(crack);
                    cracks.Add(crack);
                    MotionFx.Land(p, 1f);
                }
                if (now >= nextSweep)
                {
                    nextSweep = now + 0.5f;
                    foreach (var car in Racers.Nearby(eye, 90f, 40))
                    {
                        if (Racers.Physical(car) || !seen.Add(car)) continue;
                        var layers = Racers.Layers(Racers.CarShape(car));
                        if (layers == null) continue;
                        float phase = R() * 10f;
                        layers.Pivot = 0.5f;
                        layers.Lift(this, () => Mathf.Abs(Mathf.Sin(Time.time * 19f + phase)) * 0.12f * k);
                        layers.Tilt(this, () => Quaternion.Euler(Mathf.Sin(Time.time * 23f + phase) * 2.5f * k, 0f, Mathf.Sin(Time.time * 17f + phase * 2f) * 3f * k));
                        rattled.Add(layers);
                    }
                }
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            foreach (var c in cracks) Pop.Out(c);
            foreach (var l in rattled) if (l != null) l.Remove(this);
            cracks.Clear();
            rattled.Clear();
            seen.Clear();
        }
    }

    // cars_on_water: racers' cars get boat hulls and the bay (or a flood) becomes drivable: they
    // ride the surface, rock on the swell and throw a wake.
    public sealed class BoatCarsEffect : OperatorEffect
    {
        static readonly List<BoatCarsEffect> active = new List<BoatCarsEffect>();

        // Whether cars_on_water has given this car a hull, so a flood carries it instead of slowing it.
        public static bool Sails(CarController car)
        {
            foreach (var e in active) if (e.seen.Contains(car)) return true;
            return false;
        }

        readonly List<GameObject> hulls = new List<GameObject>();
        readonly List<ShapeLayers> rocking = new List<ShapeLayers>();
        readonly HashSet<CarController> seen = new HashSet<CarController>();
        readonly Dictionary<CarController, bool> afloat = new Dictionary<CarController, bool>();
        GameObject water;
        BoxCollider floor;
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            active.Add(this);
            var b = Context.Game.World.Bounds;
            water = new GameObject("Drivable water (operator)");
            floor = water.AddComponent<BoxCollider>();
            floor.size = new Vector3(b.width + 5000f, 1f, b.height + 5000f);
            water.transform.position = new Vector3(b.center.x, -0.52f, b.center.y);
            float rock = Scale(strength, 2f, 3.5f, 5f);
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                float top = Mathf.Max(-0.02f, FloodEffect.Level - 0.05f);
                if (Mathf.Abs(water.transform.position.y - (top - 0.5f)) > 0.01f) water.transform.position = new Vector3(b.center.x, top - 0.5f, b.center.y);
                foreach (var car in Racers.Cars(Context, target))
                {
                    var p = car.transform.position;
                    bool on = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) && hit.collider == floor;
                    afloat[car] = on;
                    float speed = Mathf.Abs(car.Speed);
                    if (on && speed > 1.5f)
                    {
                        MotionFx.Splash(p - car.transform.forward * car.HalfExtents.z, Mathf.Clamp01(speed / 14f));
                        MotionFx.Splash(p + car.transform.forward * car.HalfExtents.z + car.transform.right * car.HalfExtents.x, Mathf.Clamp01(speed / 25f));
                        MotionFx.Splash(p + car.transform.forward * car.HalfExtents.z - car.transform.right * car.HalfExtents.x, Mathf.Clamp01(speed / 25f));
                    }
                }
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!seen.Add(car)) continue;
                    var shape = Racers.CarShape(car);
                    if (shape == null) continue;
                    var hull = PropMeshes.Attach(shape, "Hull (operator)", PropMeshes.Hull(Racers.Box(car)), Context.Material, Vector3.zero);
                    Pop.In(hull);
                    hulls.Add(hull);
                    MotionFx.Splash(car.transform.position, 1f);
                    var layers = Racers.Layers(shape);
                    layers.Pivot = 0.4f;
                    float phase = Racers.Hash(car.GetInstanceID(), 5) * 6.3f;
                    var c = car;
                    layers.Tilt(this, () => afloat.TryGetValue(c, out var on) && on
                        ? Quaternion.Euler(Mathf.Sin(Time.time * 2.1f + phase) * rock - Mathf.Clamp(c.Speed, 0f, 20f) * 0.35f, 0f, Mathf.Cos(Time.time * 1.6f + phase) * rock * 0.8f)
                        : Quaternion.identity);
                    rocking.Add(layers);
                }
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            active.Remove(this);
            if (water != null) Object.Destroy(water);
            foreach (var h in hulls) Pop.Out(h);
            foreach (var l in rocking) if (l != null) l.Remove(this);
            hulls.Clear();
            rocking.Clear();
            seen.Clear();
        }
    }

    // floaty_crashes: racers' cars carry a bunch of balloons, and any crash sends the cars and
    // debris around it floating up, spinning slowly, before they drift back down.
    public sealed class FloatyCrashesEffect : OperatorEffect
    {
        readonly List<GameObject> balloons = new List<GameObject>();
        readonly HashSet<CarController> seen = new HashSet<CarController>();
        readonly Collider[] near = new Collider[48];
        EffectTicker ticker;
        float radius, seconds, rise;
        bool floating;

        public override void Apply(string strength, EffectTarget target)
        {
            radius = Scale(strength, 4f, 6f, 9f);
            seconds = Scale(strength, 1.5f, 2.5f, 3.5f);
            rise = Scale(strength, 1.8f, 2.6f, 3.6f);
            GameEvents.Impact += OnImpact;
            GameEvents.Smash += OnSmash;
            float next = 0f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                foreach (var car in Racers.Cars(Context, target))
                {
                    if (!seen.Add(car)) continue;
                    var shape = Racers.CarShape(car);
                    if (shape == null) continue;
                    var box = Racers.Box(car);
                    var go = PropMeshes.Attach(shape, "Balloons (operator)", PropMeshes.Balloons(), Context.Material, new Vector3(box.center.x, box.center.y + box.size.y * 0.5f, box.center.z - box.size.z * 0.25f));
                    go.AddComponent<Bob>();
                    Pop.In(go);
                    balloons.Add(go);
                }
            });
        }

        void OnImpact(ImpactInfo info)
        {
            if (info.Force >= 4f) Float(info.Position);
        }

        void OnSmash(SmashInfo info) => Float(info.Position);

        void Float(Vector3 at)
        {
            if (floating) return;
            floating = true;
            try { MotionFx.Land(at, 0.8f); }
            finally { floating = false; }
            int n = Physics.OverlapSphereNonAlloc(at, radius, near, ~0, QueryTriggerInteraction.Ignore);
            var bodies = new HashSet<Rigidbody>();
            for (int i = 0; i < n && bodies.Count < 12; i++)
            {
                var body = near[i].attachedRigidbody;
                if (body != null) bodies.Add(body);
            }
            foreach (var body in bodies) Floater.Start(body, seconds, rise);
        }

        public override void Revert()
        {
            GameEvents.Impact -= OnImpact;
            GameEvents.Smash -= OnSmash;
            if (ticker != null) Object.Destroy(ticker);
            foreach (var b in balloons) Pop.Out(b);
            balloons.Clear();
            seen.Clear();
        }
    }

    public sealed class Bob : MonoBehaviour
    {
        float seed;

        void Awake() => seed = (GetInstanceID() & 255) * 0.1f;

        void LateUpdate() => transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.9f + seed) * 8f, Time.time * 20f, Mathf.Cos(Time.time * 1.4f + seed) * 8f);
    }

    // Floats a body up and spins it slowly for a few seconds, then lets it drift back down. Cars
    // you drive float on CarFx airtime; everything else loses gravity for the duration.
    [DefaultExecutionOrder(45)]
    public sealed class Floater : MonoBehaviour
    {
        Rigidbody body;
        CarController car;
        float until, landUntil, rise;
        bool gravity;

        public static void Start(Rigidbody body, float seconds, float rise)
        {
            if (!body.TryGetComponent<Floater>(out var f))
            {
                f = body.gameObject.AddComponent<Floater>();
                f.body = body;
                f.car = body.GetComponent<CarController>();
                f.gravity = body.useGravity;
            }
            f.until = Time.time + seconds;
            f.landUntil = f.until + 4f;
            f.rise = rise;
            if (f.car != null && !f.car.Driven) f.car.Stun(seconds + 3f);
            if (!body.isKinematic) body.angularVelocity += new Vector3(0f, UnityEngine.Random.Range(-2f, 2f), 0f);
        }

        bool Driven => car != null && car.Driven && !car.Autopilot;

        void FixedUpdate()
        {
            if (body == null || body.isKinematic)
            {
                if (Time.time > until) Destroy(this);
                return;
            }
            if (Time.time < until)
            {
                if (Driven) CarFx.On(car).Launch(rise * 0.7f);
                else
                {
                    body.useGravity = false;
                    var v = body.linearVelocity;
                    v.y = Mathf.MoveTowards(v.y, rise, 6f * Time.fixedDeltaTime);
                    body.linearVelocity = v;
                }
                return;
            }
            body.useGravity = gravity;
            bool high = !Physics.Raycast(body.position + Vector3.up * 0.3f, Vector3.down, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            if (!high || Time.time > landUntil)
            {
                Destroy(this);
                return;
            }
            if (Driven) CarFx.On(car).Launch(-3f);
            else if (body.linearVelocity.y < -3f) body.linearVelocity = new Vector3(body.linearVelocity.x, -3f, body.linearVelocity.z);
        }

        void OnDestroy()
        {
            if (body != null) body.useGravity = gravity;
        }
    }

    // storm: rain and fog, a wetter road, and lightning. Every few seconds a ring flares on the road
    // where your car is heading; a second later the bolt lands, and a car still inside the ring is
    // stunned and scorched.
    public sealed class StormEffect : OperatorEffect
    {
        const float Radius = 4f;
        readonly WeatherEffect weather = new WeatherEffect(WeatherKind.Rain, 0.75f);
        readonly List<GameObject> bolts = new List<GameObject>();
        GuardedKnob grip;
        EffectTicker ticker;

        public override void Apply(string strength, EffectTarget target)
        {
            weather.Id = Id;
            weather.Row = Row;
            weather.Context = Context;
            weather.Apply(strength, target);
            grip = GuardedKnob.Times(() => WorldEnvironment.StormGrip, v => WorldEnvironment.StormGrip = v, Scale(strength, 0.9f, 0.8f, 0.7f));
            var rng = new System.Random(Racers.Seed(Row?.id));
            float R() => (float)rng.NextDouble();
            float every = Scale(strength, 7f, 5f, 3.5f);
            float scorch = Scale(strength, 0.03f, 0.05f, 0.08f);
            float next = Time.time + 2.5f;
            ticker = EffectTicker.On(Context, () =>
            {
                if (Time.time < next) return;
                next = Time.time + every * (0.7f + 0.6f * R());
                var car = Context.Game.CurrentCar;
                var focus = Context.Game.Focus;
                if (focus == null) return;
                var at = focus.position;
                if (car != null && car.TryGetComponent<Rigidbody>(out var body)) at += new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
                float a = R() * Mathf.PI * 2f, r = R() * 5f;
                at += new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                at.y = Racers.GroundY(at);
                var bolt = Lightning.Create(Context.Material, at, Radius, scorch);
                bolts.Add(bolt.gameObject);
            });
        }

        public override void Revert()
        {
            if (ticker != null) Object.Destroy(ticker);
            grip?.Undo();
            weather.Revert();
            foreach (var b in bolts) if (b != null) Object.Destroy(b);
            bolts.Clear();
        }
    }

    // One lightning strike: a pulsing warning ring for a second, then a white zigzag bolt with a flash,
    // a shake and a bang. Your own car inside the ring when it lands is stunned and scorched.
    public sealed class Lightning : MonoBehaviour
    {
        const float Warn = 1f;
        float radius, scorch, born;
        bool struck;
        Material material;
        GameObject ring;

        public static Lightning Create(Material material, Vector3 at, float radius, float scorch)
        {
            var go = new GameObject("Lightning (operator)");
            go.transform.position = at;
            var l = go.AddComponent<Lightning>();
            l.material = material;
            l.radius = radius;
            l.scorch = scorch;
            l.born = Time.time;
            var mb = new World.MeshBuilder();
            var glow = PropMeshes.C("#FFF6B0");
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                mb.Box(new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius), new Vector3(0.35f, 0.08f, radius * 0.42f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), glow);
            }
            l.ring = PropMeshes.Attach(go.transform, "Warning ring", mb.ToMesh("lightning ring"), material, Vector3.zero, false);
            return l;
        }

        void Update()
        {
            float t = Time.time - born;
            if (!struck)
            {
                float pulse = 1f + Mathf.Sin(t * 30f) * 0.08f;
                ring.transform.localScale = new Vector3(pulse, 1f, pulse);
                if (t >= Warn) Strike();
                return;
            }
            if (t > Warn + 0.3f) Destroy(gameObject);
        }

        void Strike()
        {
            struck = true;
            ring.SetActive(false);
            var at = transform.position;
            var mb = new World.MeshBuilder();
            var white = PropMeshes.C("#F4F8FF");
            var p = at + Vector3.up * 70f;
            var rng = new System.Random(GetInstanceID());
            for (int i = 0; i < 9; i++)
            {
                var q = i == 8 ? at : new Vector3(at.x + ((float)rng.NextDouble() - 0.5f) * 6f, p.y - 70f / 9f, at.z + ((float)rng.NextDouble() - 0.5f) * 6f);
                var mid = (p + q) * 0.5f;
                var dir = q - p;
                mb.Box(mid - at, new Vector3(0.45f, dir.magnitude, 0.45f), Quaternion.FromToRotation(Vector3.up, dir), white);
                p = q;
            }
            PropMeshes.Attach(transform, "Bolt", mb.ToMesh("lightning bolt"), material, Vector3.zero, false);
            var flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(transform, false);
            flash.transform.localPosition = Vector3.up * 6f;
            flash.type = LightType.Point;
            flash.range = 60f;
            flash.intensity = 8f;
            flash.color = PropMeshes.C("#DDE8FF");
            flash.shadows = LightShadows.None;
            MotionFx.Land(at, 1f);
            Dogpatch.Audio.SoundSystem.Play("wreck", at, Dogpatch.Audio.Bus.Sfx, 1f, 0.5f, 220, 200f);
            var cam = Camera.main;
            if (cam != null) CameraFeel.Instance?.AddTrauma(Mathf.Lerp(0.6f, 0.1f, Vector3.Distance(cam.transform.position, at) / 80f));
            foreach (var damage in VehicleDamage.All)
            {
                if (damage == null || !damage.Car.Driven || !Racers.Physical(damage.Car)) continue;
                var d = damage.Car.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                damage.Car.Stun(0.6f);
                damage.Burn(scorch / ToughEffect.Armour);
                CarFx.On(damage.Car).Launch(3f);
                Haptics.Impact(HapticStyle.Heavy, 1f);
            }
        }
    }
}

