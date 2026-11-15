using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Operator
{
    // What an effect acts on: the row's scope and the tagged entities it resolves to here (none for
    // world effects, every racer for players, one id for object, a class and its subclasses).
    public sealed class EffectTarget
    {
        public string Scope;
        public string Id;
        public string Class;
        public string Color;
        public string Theme;
        // A SELF row's caller ("self:<playerId>"): only that player's car and avatar change.
        public string Owner;
        public List<EntityTag> Entities = new List<EntityTag>();

        // Whether this device's own car and avatar are in the effect.
        public bool IncludesMe => string.IsNullOrEmpty(Owner) || IsMe(Owner);

        static bool IsMe(string playerId) => playerId == EntityTagger.MyPlayerId || (string.IsNullOrEmpty(EntityTagger.MyPlayerId) && playerId == Race.RaceDirector.Instance?.MyId);
    }

    // The parts of the game an effect may touch.
    public sealed class EffectContext
    {
        public GameController Game;
        public RaceDirector Race;
        public MonoBehaviour Host;

        public Material Material => Game.SharedMaterial;
    }

    // One effect on one active row: Apply when the row appears, Revert when it disappears from
    // operator:activeEffects (expired, countered or undone). Instant rows are applied once and
    // never reverted.
    public abstract class OperatorEffect
    {
        public string Id;
        public ActiveEffect Row;
        public EffectContext Context;

        public abstract void Apply(string strength, EffectTarget target);

        public virtual void Revert() { }

        public static float Scale(string strength, float small, float medium, float large) =>
            strength == "small" ? small : strength == "large" ? large : medium;
    }

    // A catalogue id without a client implementation yet: logged, otherwise a no-op.
    public sealed class StubEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target) =>
            Debug.Log($"[Operator] effect {Id} ({target.Scope} {target.Id}{target.Class}, {strength}) is not built on the client yet: no-op");

        public override void Revert() => Debug.Log($"[Operator] effect {Id} wore off (stub)");
    }

    // A counter the server carries out by ending other rows (clear_weather ends rain, lights_on
    // ends a blackout...); the client reverts those rows when they leave the
    // list, so the counter itself has nothing to do.
    public sealed class ServerSideEffect : OperatorEffect
    {
        public override void Apply(string strength, EffectTarget target) => Debug.Log($"[Operator] {Id}: the server ends the rows it counters");
    }

    // Effect id -> implementation. Every id in the backend catalogue is registered; the ones in
    // Built have real implementations, counters (and extinguish) are server-side, the rest are
    // logged stubs the backend never applies.
    public sealed class EffectRegistry
    {
        public static readonly string[] Catalogue =
        {
            "run_faster", "run_slower", "jump_higher", "slow_fall", "bigger", "smaller", "chunky", "skinny", "flight", "vehicle_faster", "vehicle_slower", "nitro", "grip_up", "grip_down", "tougher", "vehicle_hop", "change_vehicle", "waymo_swarm",
            "night", "day", "sunset", "fast_day_cycle", "rain", "snow", "fog", "storm", "sandstorm", "heatwave", "blackout", "clear_weather", "lights_on",
            "gravity_lower", "gravity_higher", "slippery_ground", "sticky_ground", "thick_air", "slow_motion", "fast_forward", "flood", "floor_is_lava", "earthquake", "cars_fly", "cars_on_water", "floaty_crashes", "gravity_normal", "normal_speed", "drain",
            "more_traffic", "less_traffic", "aggressive_traffic", "green_lights", "more_pedestrians", "fewer_pedestrians", "angry_npcs", "calm_npcs", "panic", "ped_theme", "big_heads",
            "set_fire", "extinguish", "demolish", "explode", "launch", "stop", "speed_up", "slow_down", "grow", "shrink", "recolor", "see_through", "bouncy", "icy", "rock_solid", "soft", "ghost",
        };

        [Serializable]
        class BuiltList
        {
            public string[] built;
        }

        // Effects with a real implementation, shared with the backend: Jev only gets to apply these
        // (plus the server-side counters), so building a stub means adding its id to
        // Resources/Operator/built-effects.json and nothing else.
        public static readonly string[] Built = JsonUtility.FromJson<BuiltList>(Resources.Load<TextAsset>("Operator/built-effects").text).built;

        public static readonly string[] Counters = { "clear_weather", "lights_on", "gravity_normal", "normal_speed", "drain", "extinguish" };

        readonly Dictionary<string, Func<OperatorEffect>> factories = new Dictionary<string, Func<OperatorEffect>>();

        public IEnumerable<string> Ids => factories.Keys;

        public void Register(string id, Func<OperatorEffect> factory) => factories[id] = factory;

        public bool Has(string id) => id != null && factories.ContainsKey(id);

        public bool IsStub(string id) => Has(id) && factories[id]() is StubEffect;

        public OperatorEffect Create(string id, EffectContext context, ActiveEffect row)
        {
            var effect = Has(id) ? factories[id]() : new StubEffect();
            effect.Id = id;
            effect.Row = row;
            effect.Context = context;
            return effect;
        }

        public static EffectRegistry Default()
        {
            var registry = new EffectRegistry();
            foreach (var id in Catalogue) registry.Register(id, () => new StubEffect());
            foreach (var id in Counters) registry.Register(id, () => new ServerSideEffect());
            registry.Register("rain", () => new RainEffect());
            registry.Register("night", () => new NightEffect());
            registry.Register("gravity_lower", () => new LowGravityEffect());
            registry.Register("run_faster", () => new RunFasterEffect());
            registry.Register("vehicle_hop", () => new VehicleHopEffect());
            registry.Register("recolor", () => new RecolorEffect());
            registry.Register("set_fire", () => new SetFireEffect());
            registry.Register("launch", () => new LaunchEffect());
            registry.Register("stop", () => new StopEffect());
            registry.Register("big_heads", () => new BigHeadsEffect());
            registry.Register("change_vehicle", () => new ChangeVehicleEffect());
            registry.Register("waymo_swarm", () => new WaymoSwarmEffect());
            MoreEffects.Register(registry);
            WorldEffectsA.Register(registry);
            WorldEffectsB.Register(registry);
            return registry;
        }
    }
}
