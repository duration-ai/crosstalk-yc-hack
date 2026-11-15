using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Smash
{
    public enum SmashKind { Break, Barge, NearMiss, Wreck }

    public readonly struct SmashScoreEvent
    {
        public readonly CarController Scorer;
        public readonly SmashKind Kind;
        public readonly string Label;
        public readonly int BasePoints;
        public readonly int Multiplier;
        public readonly int Points;
        public readonly Vector3 Position;

        public SmashScoreEvent(CarController scorer, SmashKind kind, string label, int basePoints, int multiplier, Vector3 position)
        {
            Scorer = scorer;
            Kind = kind;
            Label = label;
            BasePoints = basePoints;
            Multiplier = multiplier;
            Points = basePoints * multiplier;
            Position = position;
        }
    }

    // Internal smash events per car, never shown as points: audio, haptics, camera feel and race
    // logic react to Scored. Every break, barge, near miss and wreck raises one; events within
    // ComboWindow of the last one chain, and every second link raises the multiplier, up to x5.
    public static class SmashScore
    {
        public const float ComboWindow = 2.5f;
        public const int MaxMultiplier = 5;

        sealed class Tally
        {
            public int Total;
            public int Chain;
            public float LastTime = -99f;
        }

        static readonly Dictionary<CarController, Tally> tallies = new Dictionary<CarController, Tally>();

        public static event Action<SmashScoreEvent> Scored;

        public static int TotalFor(CarController car) => car != null && tallies.TryGetValue(car, out var t) ? t.Total : 0;

        public static int MultiplierFor(CarController car)
        {
            if (car == null || !tallies.TryGetValue(car, out var t) || Time.time - t.LastTime > ComboWindow) return 1;
            return Multiplier(t.Chain);
        }

        public static void ResetFor(CarController car)
        {
            if (car != null) tallies.Remove(car);
        }

        static int Multiplier(int chain) => Mathf.Min(MaxMultiplier, 1 + (chain - 1) / 2);

        public static void Add(CarController scorer, SmashKind kind, string label, int basePoints, Vector3 position)
        {
            if (scorer == null || basePoints <= 0) return;
            if (!tallies.TryGetValue(scorer, out var t)) tallies[scorer] = t = new Tally();
            t.Chain = Time.time - t.LastTime <= ComboWindow ? t.Chain + 1 : 1;
            t.LastTime = Time.time;
            var e = new SmashScoreEvent(scorer, kind, label, basePoints, Multiplier(t.Chain), position);
            t.Total += e.Points;
            Scored?.Invoke(e);
        }
    }
}
