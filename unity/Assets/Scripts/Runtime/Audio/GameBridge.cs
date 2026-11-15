using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using Dogpatch.Operator;
using Dogpatch.Race;
using Dogpatch.Smash;
using UnityEngine;
using OpState = Dogpatch.Operator.CallState;

namespace Dogpatch.Audio
{
    // Feeds GameEvents from the systems other owners shipped: CROS-23's smash scoring and wrecks,
    // CROS-17's race loop and Operator calls, CROS-16's environment and motion triggers. It only
    // subscribes to their public events and reads their public state, so their code stays as is.
    public sealed class GameBridge : MonoBehaviour
    {
        const float RingRange = 70f;

        RaceDirector race;
        string lastStatus;
        bool started, results, finalStretch;
        int lastCount = -1;
        int place;
        OpState callState = OpState.None;
        readonly Dictionary<Payphone, SoundLoop> rings = new Dictionary<Payphone, SoundLoop>();

        void OnEnable()
        {
            SmashScore.Scored += OnScored;
            VehicleDamage.Wrecked += OnWrecked;
            MotionFx.Boosted += OnBoosted;
            MotionFx.Landed += OnCarLanded;
        }

        void OnDisable()
        {
            SmashScore.Scored -= OnScored;
            VehicleDamage.Wrecked -= OnWrecked;
            MotionFx.Boosted -= OnBoosted;
            MotionFx.Landed -= OnCarLanded;
            if (race != null) race.EventArrived -= OnRaceEvent;
        }

        void OnScored(SmashScoreEvent e)
        {
            bool mine = e.Scorer != null && e.Scorer.Driven;
            switch (e.Kind)
            {
                case SmashKind.Break:
                    GameEvents.RaiseSmash(e.Position, e.Label, 10f, mine);
                    break;
                case SmashKind.NearMiss:
                    GameEvents.RaisePedestrianDive(e.Position, Random.Range(0, 1000));
                    break;
            }
        }

        void OnWrecked(CarController car) => GameEvents.RaiseWreck(car.transform.position, car.Driven);

        void OnBoosted(float strength)
        {
            GameEvents.SetBoost(true);
            CancelInvoke(nameof(EndBoost));
            Invoke(nameof(EndBoost), 1f);
        }

        void EndBoost() => GameEvents.SetBoost(false);

        void OnCarLanded(Vector3 position, float strength)
        {
            if (strength < 0.15f || SoundSystem.Game == null || SoundSystem.Game.CurrentCar == null) return;
            GameEvents.RaiseImpact(new ImpactInfo { Position = position, Force = 3f + strength * 10f, Surface = Surface.Concrete, InvolvesPlayer = true });
        }

        void OnRaceEvent(RaceEvent e)
        {
            if (!string.IsNullOrEmpty(e.news)) GameEvents.RaiseNewsBar();
        }

        void Update()
        {
            GameEvents.SetEnvironment(WorldEnvironment.TimeOfDay, WorldEnvironment.Rain, WorldEnvironment.Snow, WorldEnvironment.FogDensity > 0.5f);

            if (race == null && RaceDirector.Instance != null)
            {
                race = RaceDirector.Instance;
                race.EventArrived += OnRaceEvent;
            }
            if (race == null) return;
            UpdateRace();
            UpdateCall(race.Calls);
        }

        void UpdateRace()
        {
            var room = race.Room;
            if (room == null || !race.Setup)
            {
                GameEvents.SetMood(MusicMood.FreeRoam);
                return;
            }
            if (room.status != lastStatus)
            {
                lastStatus = room.status;
                if (room.status == RoomStatus.Lobby)
                {
                    started = results = finalStretch = false;
                    lastCount = -1;
                    place = 0;
                }
            }

            if (race.ResultsIn)
            {
                if (!results)
                {
                    results = true;
                    GameEvents.SetMood(MusicMood.Results);
                    GameEvents.RaiseResults(race.Me?.place ?? 0);
                }
                return;
            }
            if (race.Started)
            {
                if (!started)
                {
                    started = true;
                    GameEvents.RaiseCountdown(0);
                }
                var me = race.Me;
                if (me != null && me.place > 0)
                {
                    if (place > 0 && me.place != place) GameEvents.RaisePlaceChanged(me.place, place);
                    place = me.place;
                    finalStretch |= room.routeLength > 0f && me.metersToFinish < Mathf.Min(300f, room.routeLength * 0.25f);
                }
                GameEvents.SetMood(finalStretch ? MusicMood.FinalStretch : MusicMood.Racing);
                return;
            }
            if (race.Gridded)
            {
                GameEvents.SetMood(MusicMood.Countdown);
                int count = Mathf.CeilToInt(race.MsToStart / 1000f);
                if (count >= 1 && count <= 3 && count != lastCount) GameEvents.RaiseCountdown(count);
                lastCount = count;
                return;
            }
            GameEvents.SetMood(MusicMood.Lobby);
        }

        void UpdateCall(CallController calls)
        {
            if (calls == null) return;
            var state = calls.State;
            if (state != callState)
            {
                if (callState == OpState.None) GameEvents.RaiseCall(CallState.Answered);
                if (state == OpState.Answered) GameEvents.RaiseOperatorSpeaking(true);
                else if (callState == OpState.Answered) GameEvents.RaiseOperatorSpeaking(false);
                if (state == OpState.None) GameEvents.RaiseCall(CallState.Ended);
                SoundSystem.SetDuck(this, state == OpState.Talking);
                callState = state;
            }

            var listener = SoundSystem.ListenerPosition;
            foreach (var phone in calls.Phones)
            {
                if (phone == null) continue;
                bool ring = state == OpState.None && phone.Ringing && Vector3.Distance(phone.transform.position, listener) < RingRange;
                rings.TryGetValue(phone, out var loop);
                if (ring && loop == null) rings[phone] = SoundSystem.LoopAt("call_ring", Bus.Ui, phone.transform.position + Vector3.up * 1.6f, RingRange, 200, 0.9f);
                else if (!ring && loop != null)
                {
                    loop.Stop(0.05f);
                    rings[phone] = null;
                }
            }
        }
    }
}
