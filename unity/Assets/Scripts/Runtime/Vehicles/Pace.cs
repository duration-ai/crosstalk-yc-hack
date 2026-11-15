using System;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // How busy and how fast the streets are, read from Resources/Vehicles/pace.json.
    //   topSpeedScale / accelerationScale: multiply every vehicle's top speed and acceleration
    //     (the roster's handling profiles and the default hatchback), for the player and AI alike.
    //   trafficSpeedScale: multiplies AI cruise speeds and the lane speed limits they keep to,
    //     the bus and the trains included.
    //   parkedKeepShare: share of the planned curb spaces that get a parked car, thinned evenly.
    //   robotaxiQuota / everydayQuota / riderQuota: moving AI vehicles kept around the player.
    [Serializable]
    public sealed class Pace
    {
        public float topSpeedScale = 1f;
        public float accelerationScale = 1f;
        public float trafficSpeedScale = 1f;
        public float parkedKeepShare = 1f;
        public int robotaxiQuota = 12;
        public int everydayQuota = 12;
        public int riderQuota = 6;

        static Pace current;

        public static Pace Current => current ??= Load();

        static Pace Load()
        {
            var file = Resources.Load<TextAsset>("Vehicles/pace");
            return file != null ? JsonUtility.FromJson<Pace>(file.text) : new Pace();
        }

        public static HandlingProfile Apply(HandlingProfile profile)
        {
            profile.TopSpeed *= Current.topSpeedScale;
            profile.Acceleration *= Current.accelerationScale;
            return profile;
        }
    }
}
