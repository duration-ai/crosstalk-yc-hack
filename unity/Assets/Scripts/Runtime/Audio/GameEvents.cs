using System;
using UnityEngine;

namespace Dogpatch.Audio
{
    public enum Surface { Generic, Vehicle, Metal, Concrete, Wood, Plastic, Glass, Foliage, Body, Water }

    public enum CallState { Ringing, Answered, Ended }

    public enum MusicMood { FreeRoam, Lobby, Countdown, Racing, FinalStretch, Results }

    public struct ImpactInfo
    {
        public Vector3 Position;
        public float Force;
        public Surface Surface;
        public bool InvolvesPlayer;
    }

    public struct SmashInfo
    {
        public Vector3 Position;
        public string Kind;
        public float Force;
        public bool ByPlayer;
    }

    // The one seam between gameplay and sound/feel. Owners raise these from their own code with a
    // single line; Audio and Feel subscribe. Nothing here knows about race, physics or pedestrian
    // internals, so every system compiles and runs with or without the others.
    public static class GameEvents
    {
        public static event Action<ImpactInfo> Impact;
        public static event Action<SmashInfo> Smash;
        public static event Action<Vector3, bool> Wreck;
        public static event Action<Vector3, int> PedestrianDive;
        public static event Action<Vector3> Shove;
        public static event Action<int> Countdown;
        public static event Action<int, int> PlaceChanged;
        public static event Action<int> Results;
        public static event Action NewsBar;
        public static event Action<CallState> Call;
        public static event Action<bool> OperatorSpeaking;
        public static event Action<bool> Paused;
        public static event Action UiTap;
        public static event Action UiReady;
        public static event Action<MusicMood> Mood;
        public static event Action<bool> Boost;
        public static event Action<Transform> Horn;
        public static event Action<Vector3> AskCollected;

        public static float Hour { get; private set; } = 13f;
        public static float Rain { get; private set; }
        public static float Snow { get; private set; }
        public static bool FogBank { get; private set; }
        public static MusicMood CurrentMood { get; private set; } = MusicMood.FreeRoam;
        public static bool Boosting { get; private set; }

        public static bool LogEvents;

        public static void RaiseImpact(ImpactInfo info) { Log($"Impact {info.Surface} force={info.Force:F1} player={info.InvolvesPlayer}"); Impact?.Invoke(info); }
        public static void RaiseSmash(Vector3 position, string kind, float force, bool byPlayer = true) { Log($"Smash {kind} force={force:F1}"); Smash?.Invoke(new SmashInfo { Position = position, Kind = kind ?? "", Force = force, ByPlayer = byPlayer }); }
        public static void RaiseWreck(Vector3 position, bool isPlayer) { Log($"Wreck player={isPlayer}"); Wreck?.Invoke(position, isPlayer); }
        public static void RaisePedestrianDive(Vector3 position, int voiceSeed) { Log("PedestrianDive"); PedestrianDive?.Invoke(position, voiceSeed); }
        public static void RaiseShove(Vector3 position) { Log("Shove"); Shove?.Invoke(position); }
        public static void RaiseCountdown(int secondsLeft) { Log($"Countdown {secondsLeft}"); Countdown?.Invoke(secondsLeft); }
        public static void RaisePlaceChanged(int place, int previous) { Log($"PlaceChanged {previous}->{place}"); PlaceChanged?.Invoke(place, previous); }
        public static void RaiseResults(int place) { Log($"Results {place}"); Results?.Invoke(place); }
        public static void RaiseNewsBar() { Log("NewsBar"); NewsBar?.Invoke(); }
        public static void RaiseCall(CallState state) { Log($"Call {state}"); Call?.Invoke(state); }
        public static void RaiseOperatorSpeaking(bool speaking) { Log($"OperatorSpeaking {speaking}"); OperatorSpeaking?.Invoke(speaking); }
        public static void RaisePaused(bool paused) { Log($"Paused {paused}"); Paused?.Invoke(paused); }
        public static void RaiseUiTap() => UiTap?.Invoke();
        public static void RaiseUiReady() { Log("UiReady"); UiReady?.Invoke(); }
        public static void RaiseHorn(Transform vehicle) { Log("Horn"); Horn?.Invoke(vehicle); }
        public static void RaiseAskCollected(Vector3 position) { Log("AskCollected"); AskCollected?.Invoke(position); }

        public static void SetMood(MusicMood mood)
        {
            if (mood == CurrentMood) return;
            Log($"Mood {mood}");
            CurrentMood = mood;
            Mood?.Invoke(mood);
        }

        public static void SetBoost(bool boosting)
        {
            if (boosting == Boosting) return;
            Log($"Boost {boosting}");
            Boosting = boosting;
            Boost?.Invoke(boosting);
        }

        // Owned by the look/weather system: hour 0-24, rain and snow 0-1, and whether the fog bank is up.
        public static void SetEnvironment(float hour, float rain, float snow, bool fogBank)
        {
            Hour = Mathf.Repeat(hour, 24f);
            Rain = Mathf.Clamp01(rain);
            Snow = Mathf.Clamp01(snow);
            FogBank = fogBank;
        }

        static void Log(string message)
        {
            if (LogEvents) Debug.Log($"[Event] t={Time.realtimeSinceStartup:F2} {message}");
        }
    }
}
