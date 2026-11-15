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

        static DamageTuning current;

        public static DamageTuning Current => current ??= Load();

        static DamageTuning Load()
        {
            var file = Resources.Load<TextAsset>("Smash/damage");
            return file != null ? JsonUtility.FromJson<DamageTuning>(file.text) : new DamageTuning();
        }
    }
}
