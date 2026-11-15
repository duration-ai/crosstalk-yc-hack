using System;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Every crash-damage tunable, read from Resources/Smash/damage.json.
    //   playerDurabilityScale / otherDurabilityScale: multiply each vehicle's own budget
    //     (HandlingProfile.Durability), so the roster keeps its ratios; "other" is AI traffic and
    //     parked cars, kept softer so barging them stays satisfying.
    //   minDamageImpact: m/s of impact into a wall or another vehicle below which the hit costs no
    //     health (scrapes, curbs, low-speed bumps, parked-car nudges). Dents still show.
    //   wallDamagePerImpact: health per m/s of a hit into something static (buildings, columns).
    //   carDamageScale: multiplies car-on-car damage (impact x mass ratio x (1 - barge strength)).
    //   breakableDamageScale: multiplies what smashing a lamp, sign, hydrant, bin, bench or tree costs.
    //   lightSmokeBelowHealth / greySmokeBelowHealth: health share at which smoke starts and thickens.
    //   maxRiseSpeed: m/s a car may move upward after any hit, launch or shove (3.5 is a hop of about
    //     0.6 m); also the cap on a launch's lift.
    //   maxTumbleSpin: rad/s of roll and pitch a car may have while tumbling.
    //   airborneGravity: gravity multiplier for an undriven or stunned car off the ground, so it
    //     lands within a second.
    //   maxDepenetration: m/s at which physics may push apart overlapping bodies, so a parked or AI
    //     car that turns physical while overlapping something slides out instead of shooting up.
    [Serializable]
    public sealed class DamageTuning
    {
        public float playerDurabilityScale = 1f;
        public float otherDurabilityScale = 1f;
        public float minDamageImpact = 2f;
        public float wallDamagePerImpact = 1.4f;
        public float carDamageScale = 1f;
        public float breakableDamageScale = 1f;
        public float lightSmokeBelowHealth = 0.6f;
        public float greySmokeBelowHealth = 0.3f;
        public float maxRiseSpeed = 3.5f;
        public float maxTumbleSpin = 3f;
        public float airborneGravity = 2.5f;
        public float maxDepenetration = 1f;

        static DamageTuning current;

        public static DamageTuning Current => current ??= Load();

        static DamageTuning Load()
        {
            var file = Resources.Load<TextAsset>("Smash/damage");
            return file != null ? JsonUtility.FromJson<DamageTuning>(file.text) : new DamageTuning();
        }
    }
}
