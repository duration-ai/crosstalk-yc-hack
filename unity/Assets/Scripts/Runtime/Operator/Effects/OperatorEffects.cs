using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Keeps this device's world in step with the room's active effects. Rows arrive two ways: the
    // caller's own operator:call answer (applied at once, before the subscription catches up) and
    // the operator:activeEffects subscription. Both go through Upsert, keyed by row id, so an
    // effect is applied exactly once; a new endsAt on the same id just moves its countdown. A
    // lasting row is reverted when it leaves the list (expired, countered or undone); an
    // optimistic row the list has not shown yet gets a grace period first. Instant rows are
    // applied once per id and never reverted.
    public sealed class OperatorEffects : MonoBehaviour
    {
        const float Grace = 6f;
        const float InstantShown = 3f;

        public sealed class Running
        {
            public ActiveEffect Row;
            public OperatorEffect Effect;
            public float AppliedAt;
            public bool Listed;
            public Action Look;
        }

        public EffectRegistry Registry;
        public EffectContext Context;
        public Func<double> ServerNow = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        readonly Dictionary<string, Running> running = new Dictionary<string, Running>();
        readonly Dictionary<string, float> instants = new Dictionary<string, float>();
        readonly List<Running> chips = new List<Running>();

        public event Action<ActiveEffect> Started;

        public IReadOnlyList<Running> Chips
        {
            get
            {
                chips.Clear();
                foreach (var r in running.Values) chips.Add(r);
                foreach (var pair in instants)
                    if (Time.time - pair.Value < InstantShown && lastInstant.TryGetValue(pair.Key, out var row)) chips.Add(new Running { Row = row, AppliedAt = pair.Value });
                chips.Sort((a, b) => a.AppliedAt.CompareTo(b.AppliedAt));
                return chips;
            }
        }

        readonly Dictionary<string, ActiveEffect> lastInstant = new Dictionary<string, ActiveEffect>();

        public bool IsActive(string effect)
        {
            foreach (var r in running.Values) if (r.Row.effect == effect) return true;
            return false;
        }

        public string TimeOfDay => IsActive("night") ? "night" : IsActive("sunset") ? "sunset" : "day";

        public string Weather => IsActive("rain") ? "rain" : IsActive("storm") ? "storm" : IsActive("snow") ? "snow" : IsActive("fog") ? "fog" : "clear";

        public float SecondsLeft(ActiveEffect row) => row.instant || row.endsAt <= 0 ? 0f : Mathf.Max(0f, (float)((row.endsAt - ServerNow()) / 1000.0));

        public void Optimistic(ActiveEffect row)
        {
            if (row == null || !row.IsSet || row.status != "active") return;
            Upsert(row, false);
        }

        public void OnActiveList(ActiveEffect[] rows)
        {
            var seen = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row == null || !row.IsSet) continue;
                seen.Add(row.id);
                Upsert(row, true);
            }
            foreach (var r in new List<Running>(running.Values))
                if (!seen.Contains(r.Row.id) && (r.Listed || Time.time - r.AppliedAt > Grace)) End(r, "left the list");
        }

        void Upsert(ActiveEffect row, bool listed)
        {
            if (row.instant)
            {
                if (instants.ContainsKey(row.id)) return;
                instants[row.id] = Time.time;
                lastInstant[row.id] = row;
                var once = Registry.Create(row.effect, Context, row);
                Run(once, row);
                return;
            }
            if (running.TryGetValue(row.id, out var existing))
            {
                if (row.endsAt != existing.Row.endsAt) Debug.Log($"[Operator] {row.effect} {row.id} extended, {SecondsLeft(row):F0}s left");
                if (listed && !existing.Listed) Debug.Log($"[Operator] {row.effect} {row.id} arrived on activeEffects {(Time.time - existing.AppliedAt) * 1000f:F0} ms after the optimistic apply");
                existing.Row = row;
                existing.Listed |= listed;
                return;
            }
            if (row.endsAt > 0 && row.endsAt < ServerNow() - 1000) return;
            var effect = Registry.Create(row.effect, Context, row);
            var started = new Running { Row = row, Effect = effect, AppliedAt = Time.time, Listed = listed };
            running[row.id] = started;
            var target = Run(effect, row);
            try { started.Look = VehicleLooks.Attach(Context, row, target); }
            catch (Exception e) { Debug.LogError($"[Operator] {row.effect} look failed: {e}"); }
        }

        EffectTarget Run(OperatorEffect effect, ActiveEffect row)
        {
            var target = Resolve(row);
            if (!target.IncludesMe && !RemoteVisible.Contains(row.effect))
            {
                Debug.Log($"[Operator] {row.effect} {row.id} is another racer's own change; their car shows it, ours stays as is");
                Started?.Invoke(row);
                return;
            }
            Debug.Log($"[Operator] apply {row.effect} {row.id} {row.scope}:{row.target}{row.targetClass} on {target.Entities.Count} entities, {row.strength} {row.color}{row.theme}{(row.instant ? " (instant)" : $" for {SecondsLeft(row):F0}s")}");
            try { effect.Apply(row.strength, target); }
            catch (Exception e) { Debug.LogError($"[Operator] {row.effect} apply failed: {e}"); }
            Started?.Invoke(row);
            return target;
        }

        void End(Running r, string why)
        {
            running.Remove(r.Row.id);
            Debug.Log($"[Operator] revert {r.Row.effect} {r.Row.id} ({why})");
            try { r.Effect.Revert(); }
            catch (Exception e) { Debug.LogError($"[Operator] {r.Row.effect} revert failed: {e}"); }
            try { r.Look?.Invoke(); }
            catch (Exception e) { Debug.LogError($"[Operator] {r.Row.effect} look removal failed: {e}"); }
        }

        // SELF effects that only touch the owner's car and avatar models, so other devices can draw them
        // on that racer; the rest also turn this device's own knobs and run on the caller's device only.
        static readonly HashSet<string> RemoteVisible = new HashSet<string> { "chunky", "skinny", "cars_fly", "cars_on_water", "floaty_crashes", "change_vehicle" };

        public EffectTarget Resolve(ActiveEffect row)
        {
            var target = new EffectTarget { Scope = row.scope, Id = row.target, Class = row.targetClass, Color = row.color, Theme = row.theme };
            EntityTagger.TagDynamic(Context.Game, Context.Race);
            if (row.target != null && row.target.StartsWith("self:"))
            {
                var who = target.Owner = row.target.Substring(5);
                foreach (var tag in EntityTag.All)
                    if (tag.Id == "car:" + who || tag.Id == "racer:" + who || (tag.ControlledBy == "me" && target.IncludesMe)) target.Entities.Add(tag);
                return target;
            }
            switch (row.scope)
            {
                case "object":
                    var one = EntityTag.Find(row.target);
                    if (one != null) target.Entities.Add(one);
                    break;
                case "class":
                    foreach (var tag in EntityTag.All)
                        if (EntityClasses.IsA(tag.Class, row.targetClass)) target.Entities.Add(tag);
                    break;
                case "players":
                    foreach (var tag in EntityTag.All)
                        if (tag.ControlledBy == "me" || tag.ControlledBy == "player") target.Entities.Add(tag);
                    break;
            }
            return target;
        }

        public void ResetAll()
        {
            foreach (var r in new List<Running>(running.Values)) End(r, "reset");
        }

        void Update()
        {
            double now = ServerNow();
            foreach (var r in new List<Running>(running.Values))
                if (r.Row.endsAt > 0 && now > r.Row.endsAt + 4000) End(r, "ran out");
        }
    }
}
