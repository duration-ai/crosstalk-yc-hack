using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Audio
{
    // Capture harness, only active with -soundDemo on the command line: plays a scripted drive
    // through a smash sequence in the real game and records the final mix at the listener to a
    // WAV (-soundDemoOut <path>). Input is injected into the real DragInput and the real tap
    // handler; race, call, smash and pedestrian events are raised through GameEvents exactly as
    // their owners will raise them.
    public sealed class SoundDemo : MonoBehaviour
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static bool Active { get; private set; }

        GameController game;
        DragInput input;
        AudioRecorder recorder;
        float t0;
        readonly List<string> log = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-soundDemo")) return;
            GameEvents.LogEvents = true;
            Active = true;
            new GameObject("SoundDemo").AddComponent<SoundDemo>();
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        void Mark(string what)
        {
            string line = $"{Time.realtimeSinceStartup - t0,6:F2}s  {what}";
            log.Add(line);
            Debug.Log("[SoundDemo] " + line);
        }

        void Drag(Vector2 dir, float strength)
        {
            var anchor = new Vector2(Screen.width * 0.5f, Screen.height * 0.35f);
            bool on = strength > 0f;
            Set("<Dragging>k__BackingField", on);
            Set("<Anchor>k__BackingField", anchor);
            float reach = input.TapSlop + strength * (input.Reach - input.TapSlop);
            Set("<Current>k__BackingField", on ? anchor + dir.normalized * reach : anchor);
        }

        void Set(string field, object value) => typeof(DragInput).GetField(field, Private).SetValue(input, value);

        void Tap(Vector2 screen) => typeof(GameController).GetMethod("OnTap", Private).Invoke(game, new object[] { screen });

        static IEnumerator Wait(float seconds) => new WaitForSecondsRealtime(seconds);

        Vector3 CarAhead(float metres)
        {
            var cam = game.View.transform;
            var fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            return cam.position + fwd * (10f + metres) + Vector3.down * cam.position.y + Vector3.up * 0.5f;
        }

        void Smash(string kind, float force)
        {
            Mark($"smash {kind}");
            GameEvents.RaiseSmash(CarAhead(2f), kind, force);
        }

        IEnumerator Start()
        {
            while ((game = FindAnyObjectByType<GameController>()) == null || !game.StatusLine.StartsWith("On foot")) yield return null;
            input = game.GetComponent<DragInput>();
            yield return Wait(1.5f);
            var listener = FindAnyObjectByType<AudioListener>();
            recorder = listener.gameObject.AddComponent<AudioRecorder>();
            t0 = Time.realtimeSinceStartup;
            Mark("recording: free roam, on foot, day");

            Mark("walk toward the hatchback");
            Drag(Vector2.up, 0.45f);
            yield return Wait(2.2f);
            Drag(Vector2.zero, 0f);
            yield return Wait(0.4f);
            Mark("tap empty sky: jump");
            Tap(new Vector2(Screen.width * 0.5f, Screen.height * 0.95f));
            yield return Wait(1.2f);
            Mark("pedestrian dives out of the way (CROS-20 event)");
            GameEvents.RaisePedestrianDive(game.View.transform.position + game.View.transform.forward * 8f, 0);
            yield return Wait(1.0f);
            GameEvents.RaisePedestrianDive(game.View.transform.position + game.View.transform.forward * 9f + game.View.transform.right * 3f, 1);
            yield return Wait(0.8f);

            var car = game.NearestCar;
            if (car != null)
            {
                Mark("tap the hatchback: get in");
                Tap(game.View.WorldToScreenPoint(car.transform.position + Vector3.up * 0.8f));
            }
            yield return Wait(1.6f);
            Mark("horn");
            if (car != null) GameEvents.RaiseHorn(car.transform);
            yield return Wait(0.8f);

            Mark("lobby, ready, countdown (CROS-17 events)");
            GameEvents.SetMood(MusicMood.Lobby);
            GameEvents.RaiseUiReady();
            yield return Wait(1.5f);
            GameEvents.SetMood(MusicMood.Countdown);
            for (int i = 3; i > 0; i--)
            {
                GameEvents.RaiseCountdown(i);
                yield return Wait(1f);
            }
            GameEvents.RaiseCountdown(0);
            GameEvents.SetMood(MusicMood.Racing);
            Mark("GO: full throttle");
            Drag(Vector2.up, 1f);
            yield return Wait(2.5f);
            Smash("hydrant", 14f);
            yield return Wait(1.3f);
            Smash("trash_can", 8f);
            yield return Wait(1.1f);
            Smash("newspaper_box", 9f);
            yield return Wait(1.0f);
            Smash("parking_meter", 9f);
            yield return Wait(1.0f);
            Smash("bench", 10f);
            yield return Wait(0.8f);
            Mark("boost");
            GameEvents.SetBoost(true);
            yield return Wait(2.0f);
            GameEvents.SetBoost(false);
            Mark("news bar + place change 2nd -> 1st");
            GameEvents.RaiseNewsBar();
            yield return Wait(0.6f);
            GameEvents.RaisePlaceChanged(1, 2);
            yield return Wait(0.8f);
            Mark("hard right at speed: skid");
            Drag(new Vector2(1f, 0.15f), 1f);
            yield return Wait(1.6f);
            Mark("drag back: brake hard");
            Drag(Vector2.down, 1f);
            yield return Wait(1.2f);
            Drag(Vector2.up, 1f);
            yield return Wait(1.8f);
            Smash("bus_shelter", 16f);
            yield return Wait(1.2f);
            Smash("street_light", 12f);
            yield return Wait(1.0f);

            Mark("the Operator calls (CROS-17 events): ring, pick up, she talks (ducking)");
            GameEvents.RaiseCall(CallState.Ringing);
            yield return Wait(2.6f);
            GameEvents.RaiseCall(CallState.Answered);
            yield return Wait(0.6f);
            GameEvents.RaiseOperatorSpeaking(true);
            yield return Wait(3.5f);
            GameEvents.RaiseOperatorSpeaking(false);
            yield return Wait(0.5f);
            GameEvents.RaiseCall(CallState.Ended);
            Mark("final stretch");
            GameEvents.SetMood(MusicMood.FinalStretch);
            Drag(new Vector2(-0.6f, 1f), 1f);
            yield return Wait(2.5f);
            Smash("fence", 10f);
            yield return Wait(1.0f);
            Smash("storefront_glass", 18f);
            yield return Wait(1.0f);
            Mark("wreck (CROS-23 event)");
            GameEvents.RaiseWreck(CarAhead(0f), true);
            Drag(Vector2.zero, 0f);
            yield return Wait(2.0f);
            Mark("results: 1st");
            GameEvents.SetMood(MusicMood.Results);
            GameEvents.RaiseResults(1);
            yield return Wait(3.5f);
            Mark("pause and resume");
            GameEvents.RaisePaused(true);
            yield return Wait(1.2f);
            GameEvents.RaisePaused(false);
            yield return Wait(0.8f);

            Mark("night falls, rain, fog bank up (CROS-16 environment)");
            GameEvents.SetMood(MusicMood.FreeRoam);
            GameEvents.SetEnvironment(21.5f, 0.85f, 0f, true);
            var ambience = FindAnyObjectByType<Ambience>();
            yield return Wait(1.5f);
            ambience.PlayFoghorn();
            yield return Wait(3.5f);
            Mark("rain stops: night crickets");
            GameEvents.SetEnvironment(22f, 0f, 0f, true);
            yield return Wait(3.0f);
            Mark("Caltrain at 22nd St");
            ambience.PlayCaltrain();
            yield return Wait(5.0f);
            Mark("snow");
            GameEvents.SetEnvironment(14f, 0f, 1f, false);
            yield return Wait(3.0f);
            Mark("stop");

            string path = Arg("-soundDemoOut", Path.Combine(Application.persistentDataPath, "sound-demo.wav"));
            recorder.Save(path);
            File.WriteAllLines(Path.ChangeExtension(path, ".timeline.txt"), log);
            Debug.Log($"[SoundDemo] wrote {path}: {recorder.Seconds:F1}s, culled={SoundSystem.Culled} stolen={SoundSystem.Stolen} limiterMinGain={MasterLimiter.MinGain:F2}, resident audio {SoundBank.Bytes / 1024} KB");
            yield return Wait(0.5f);
            Application.Quit();
        }
    }

    // Records the listener's final mix (attached to the AudioListener's GameObject).
    public sealed class AudioRecorder : MonoBehaviour
    {
        readonly List<short> samples = new List<short>(48000 * 2 * 120);
        int channels = 2;
        int rate;

        public float Seconds => samples.Count / (float)Mathf.Max(channels * rate, 1);

        void Awake() => rate = AudioSettings.outputSampleRate;

        void OnAudioFilterRead(float[] data, int count)
        {
            channels = count;
            lock (samples)
            {
                foreach (var s in data) samples.Add((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
        }

        public void Save(string path)
        {
            short[] pcm;
            lock (samples) pcm = samples.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var w = new BinaryWriter(File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + pcm.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(pcm.Length * 2);
            foreach (var s in pcm) w.Write(s);
        }
    }
}
