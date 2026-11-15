using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Ambient
{
    public enum PedState : byte { Walk, Approach, Idle, WaitCurb, Sit, BusWait, Dive, Startled, Stumble, Tumble, Down, GetUp, Dazed }

    public enum PedRole : byte { Walker, Skater, Cart }

    // One person on the street. It holds only state and the trigger collider that cars, the player
    // and shoves touch; PedestrianSystem moves it and the crowd renderer draws it.
    public sealed class Pedestrian : MonoBehaviour, IShoveable, ITrafficParticipant, IVehicleReactive
    {
        internal PedestrianSystem Owner;
        internal int Id;
        internal CrowdModel Model;
        internal CrowdRenderer.Outfit Outfit;
        internal PedRole Role;
        internal PedState State;
        internal Vector2 Pos;
        internal Vector2 Vel;
        internal float Heading;
        internal float HeadYaw;
        internal float HeadTarget;
        internal float NextGlance;
        internal float Cruise;
        internal float Lateral;
        internal float Radius;
        internal float StateTime;
        internal float StateLength;
        internal readonly List<int> Path = new List<int>();
        internal int PathIndex;
        internal int Spot = -1;
        internal int Crossing = -1;
        internal Vector2 Target;
        internal float TargetHeading;
        internal PedState Then;
        internal Clip Clip = Clip.Idle;
        internal Clip PrevClip = Clip.Idle;
        internal float ClipTime;
        internal float PrevClipTime;
        internal float Fade = 1f;
        internal float FadeSpeed = 1f / 0.22f;
        internal float FadeCap = 1f;
        internal Vector2 Slide;
        internal Vector2 FallDir;
        internal float Lean;
        internal float Hurry;
        // While Posed the body is drawn from Orient, rotating about the hip pivot held Height above
        // the ground, instead of standing upright on Heading. Knock-downs, dives and get-ups use it.
        internal bool Posed;
        internal Quaternion Orient = Quaternion.identity;
        internal Quaternion LieOrient = Quaternion.identity;
        internal float Height;
        internal float Climb;
        internal Vector3 Spin;
        internal float Pitch;
        internal float Mark;
        internal byte Phase;
        internal bool Launched;
        internal float Impact;
        internal Vector2 ThreatFrom;
        internal float Pending;
        internal bool Registered;

        public Vector3 Position => new Vector3(Pos.x, transform.position.y, Pos.y);

        public Vector3 Velocity => new Vector3(Vel.x, 0f, Vel.y);

        public bool CanBeShoved => isActiveAndEnabled && State != PedState.Tumble && State != PedState.Down && State != PedState.GetUp;

        public void Shove(Vector3 impulse, GameObject by) => Owner.Shove(this, new Vector2(impulse.x, impulse.z), by);

        public void OnVehicleThreat(GameObject vehicle, Vector3 vehicleVelocity, float timeToImpact)
        {
            var p = vehicle.transform.position;
            Owner.Threat(this, new Vector2(p.x, p.z), new Vector2(vehicleVelocity.x, vehicleVelocity.z), timeToImpact);
        }

        public void OnVehicleImpact(GameObject vehicle, Vector3 point, Vector3 relativeVelocity)
        {
            var p = vehicle.transform.position;
            Owner.Hit(this, new Vector2(relativeVelocity.x, relativeVelocity.z), new Vector2(p.x, p.z));
        }

        void OnTriggerEnter(Collider other) => Owner.Contact(this, other);
    }
}
