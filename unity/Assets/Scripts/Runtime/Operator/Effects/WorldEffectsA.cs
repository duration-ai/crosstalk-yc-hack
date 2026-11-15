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
    // honours small/medium/large, reverts cleanly, layers on others through ShapeLayers, Knob and
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
        Knob run;

        public override void Apply(string strength, EffectTarget target)
        {
            float onFoot = Scale(strength, 5f, 9f, 14f);
            float inCar = Scale(strength, 4f, 7f, 11f);
            run = Knob.Times(() => PlayerController.RunScale, v => PlayerController.RunScale = v, Scale(strength, 1.3f, 1.6f, 2f));
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
                        var cape = PropMeshes.Attach(shape, "Cape (operator)", PropMeshes.Cape(), Context.Material, new Vector3(0f, 1.42f, -0.2f));
                        cape.AddComponent<Flutter>().Player = player;
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
        }

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
            float target = ground + Height + Mathf.Sin(Time.time * 2.1f) * 0.35f;
            player.VerticalVelocity = Mathf.Clamp((target - p.y) * 2.4f, -7f, 9f);
        }

        float Ground(Vector3 at) =>
            Physics.Raycast(new Vector3(at.x, transform.position.y + 0.2f, at.z), Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : transform.position.y - Height;
    }

    // A cape that streams back with speed and flutters.
    public sealed class Flutter : MonoBehaviour
    {
        public PlayerController Player;

        void LateUpdate()
        {
            float speed = Player != null ? Mathf.Clamp01(Player.Speed / 9f) : 0f;
            float flap = Mathf.Sin(Time.time * (6f + speed * 10f)) * (6f + speed * 8f);
            transform.localRotation = Quaternion.Euler(-(15f + speed * 60f) - flap, 0f, Mathf.Sin(Time.time * 3.7f) * 4f);
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
        Knob durability;

        public override void Apply(string strength, EffectTarget target)
        {
            var tuning = DamageTuning.Current;
            durability = Knob.Times(() => tuning.playerDurabilityScale, v => tuning.playerDurabilityScale = v, Scale(strength, 3f, 5f, 10f));
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
                foreach (var car in Racers.Cars(Context, target)) Fly(car, fly);
                var eye = Context.Game.View.transform.position;
                foreach (var car in Racers.Nearby(eye, 220f, 90))
                {
                    if (Racers.Physical(car) || Racers.IsRacer(car)) Fly(car, fly);
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

        void Fly(CarController car, float height)
        {
            if (!done.Add(car)) return;
            var shape = Racers.CarShape(car);
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
}
