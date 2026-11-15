using System;
using System.Reflection;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Race
{
    // Each racer's smash points this race, sent with every race/update so the server can award
    // WRECKING BALL. The points come from CROS-23's Dogpatch.Smash.SmashScore (TotalFor/ResetFor per
    // car, breaks, barges, near misses and wrecks with combo multipliers already applied). That
    // lives on another branch, so it is found at runtime; without it every racer scores 0.
    public sealed class SmashTally
    {
        MethodInfo totalFor;
        MethodInfo resetFor;
        bool looked;

        void Look()
        {
            if (looked) return;
            looked = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var score = assembly.GetType("Dogpatch.Smash.SmashScore");
                if (score == null) continue;
                totalFor = score.GetMethod("TotalFor", new[] { typeof(CarController) });
                resetFor = score.GetMethod("ResetFor", new[] { typeof(CarController) });
            }
            Debug.Log(totalFor != null ? "[SpeedDial] smash points come from Dogpatch.Smash.SmashScore" : "[SpeedDial] smash scoring not in this build; smash points stay 0");
        }

        public int Of(CarController car)
        {
            Look();
            if (car == null || totalFor == null) return 0;
            return (int)totalFor.Invoke(null, new object[] { car });
        }

        public void Reset(CarController car)
        {
            Look();
            if (car != null && resetFor != null) resetFor.Invoke(null, new object[] { car });
        }
    }
}
