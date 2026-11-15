using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Audio
{
    public enum Bus { Music, Sfx, Vehicles, Ambience, Voice, Ui }

    // A looping sound owned by gameplay code. It only holds a real AudioSource while it ranks among
    // the most audible loops; the rest stay virtual (silent, no CPU) until they come back in range.
    public sealed class SoundLoop
    {
        public float Volume = 1f;
        public float Pitch = 1f;
        public Transform Follow;
        public Vector3 Position;
        public bool Spatial = true;
        public int Priority = 128;
        public float MaxDistance = 60f;
        internal AudioClip Clip;
        internal Bus Bus;
        internal AudioSource Source;
        internal float Fade;
        internal float FadeRate = 4f;
        internal bool Stopping;
        internal float Score;

        public bool Alive => !Stopping || Fade > 0f;
        public bool Audible => Source != null;

        public void Stop(float fadeSeconds = 0.25f)
        {
            Stopping = true;
            FadeRate = fadeSeconds <= 0f ? 1000f : 1f / fadeSeconds;
        }
    }

    // Pooled 3D voices with distance and priority culling, six code-side buses (music, SFX,
    // vehicles, ambience, VO, UI) with settings volumes and ducking under the Operator's voice.
    // Booted from code like the rest of the game; it also attaches per-object sound components to
    // cars and the player as they appear.
    [DefaultExecutionOrder(-50)]
    public sealed class SoundSystem : MonoBehaviour
    {
        const int OneShotVoices = 28;
        const int LoopVoices = 24;

        static readonly float[] DuckedGain = { 0.35f, 0.6f, 0.5f, 0.45f, 1f, 1f };

        sealed class Voice
        {
            public AudioSource Source;
            public Bus Bus;
            public float Volume;
            public float Score;
        }

        public static SoundSystem Instance { get; private set; }
        public static int Culled { get; private set; }
        public static int Stolen { get; private set; }
        public static Vector3 ListenerPosition { get; private set; }

        readonly List<Voice> voices = new List<Voice>();
        readonly List<SoundLoop> loops = new List<SoundLoop>();
        readonly Stack<AudioSource> freeLoopSources = new Stack<AudioSource>();
        readonly HashSet<object> duckers = new HashSet<object>();
        readonly float[] duck = { 1f, 1f, 1f, 1f, 1f, 1f };
        AnimationCurve rolloff;
        float nextScan;
        Transform listener;
        GameController game;

        public static GameController Game => Instance != null ? Instance.game : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("Audio");
            DontDestroyOnLoad(go);
            go.AddComponent<SoundSystem>();
        }

        void Awake()
        {
            Instance = this;
            var keys = new Keyframe[13];
            for (int i = 0; i < keys.Length; i++)
            {
                float x = i / (keys.Length - 1f);
                keys[i] = new Keyframe(x, Attenuation(x));
            }
            rolloff = new AnimationCurve(keys);
            for (int i = 0; i < OneShotVoices; i++) voices.Add(new Voice { Source = NewSource("voice " + i) });
            for (int i = 0; i < LoopVoices; i++) freeLoopSources.Push(NewSource("loop " + i));

            float t0 = Time.realtimeSinceStartup;
            SoundBank.Load();
            Debug.Log($"[Audio] initialized: {SoundBank.Count} clips loaded in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms, {(SoundBank.Bytes > 0 ? SoundBank.Bytes / 1024 + " KB resident" : "resident size n/a outside development builds")}, output {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}, dsp buffer {DspBuffer()}, {OneShotVoices} one-shot + {LoopVoices} loop voices");

            gameObject.AddComponent<GameSounds>();
            gameObject.AddComponent<MusicDirector>();
            gameObject.AddComponent<Ambience>();
        }

        static string DspBuffer()
        {
            AudioSettings.GetDSPBufferSize(out int length, out int count);
            return length + "x" + count;
        }

        // Shared rolloff: near-flat close up, falling to silence at maxDistance so culling never cuts
        // an audible sound.
        static float Attenuation(float x)
        {
            x = Mathf.Clamp01(x);
            return (1f - x) * (1f - x) / (1f + 5f * x);
        }

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Custom;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
            s.spread = 40f;
            return s;
        }

        public static float BusGain(Bus bus)
        {
            if (Instance == null) return 1f;
            return SoundSettings.Master * SoundSettings.Volume(bus) * Instance.duck[(int)bus];
        }

        // Ducks music, SFX, vehicles and ambience while any key holds it (the Operator's voice).
        public static void SetDuck(object key, bool on)
        {
            if (Instance == null) return;
            if (on) Instance.duckers.Add(key);
            else Instance.duckers.Remove(key);
        }

        public static AudioSource Play(string name, Vector3 position, Bus bus, float volume = 1f, float pitch = 1f, int priority = 128, float maxDistance = 60f)
            => Instance == null ? null : Instance.StartVoice(SoundBank.Get(name), true, position, bus, volume, pitch, priority, maxDistance);

        public static AudioSource Play2D(string name, Bus bus, float volume = 1f, float pitch = 1f, int priority = 200)
            => Instance == null ? null : Instance.StartVoice(SoundBank.Get(name), false, Vector3.zero, bus, volume, pitch, priority, 0f);

        // Plays an Operator line (or any VO clip) on the voice bus and ducks the rest while it plays.
        public static AudioSource PlayVoice(AudioClip clip, float volume = 1f)
            => Instance == null ? null : Instance.StartVoice(clip, false, Vector3.zero, Bus.Voice, volume, 1f, 255, 0f);

        public static SoundLoop Loop(string name, Bus bus, Transform follow, float maxDistance = 60f, int priority = 128, float volume = 1f)
        {
            var clip = SoundBank.Get(name);
            if (Instance == null || clip == null) return null;
            var loop = new SoundLoop { Clip = clip, Bus = bus, Follow = follow, Spatial = follow != null, MaxDistance = maxDistance, Priority = priority, Volume = volume };
            if (follow != null) loop.Position = follow.position;
            Instance.loops.Add(loop);
            return loop;
        }

        public static SoundLoop LoopAt(string name, Bus bus, Vector3 position, float maxDistance = 60f, int priority = 128, float volume = 1f)
        {
            var loop = Loop(name, bus, null, maxDistance, priority, volume);
            if (loop == null) return null;
            loop.Spatial = true;
            loop.Position = position;
            return loop;
        }

        float Audibility(bool spatial, Vector3 position, float maxDistance, float volume)
        {
            if (!spatial) return volume;
            float d = Vector3.Distance(position, ListenerPosition);
            return d >= maxDistance ? 0f : volume * Attenuation(d / maxDistance);
        }

        AudioSource StartVoice(AudioClip clip, bool spatial, Vector3 position, Bus bus, float volume, float pitch, int priority, float maxDistance)
        {
            if (clip == null) return null;
            float audibility = Audibility(spatial, position, maxDistance, volume);
            if (audibility <= 0.002f)
            {
                Culled++;
                return null;
            }
            float score = audibility * (0.4f + priority / 255f);
            Voice pick = null;
            Voice weakest = null;
            foreach (var v in voices)
            {
                if (!v.Source.isPlaying)
                {
                    pick = v;
                    break;
                }
                if (weakest == null || v.Score < weakest.Score) weakest = v;
            }
            if (pick == null)
            {
                if (weakest == null || weakest.Score >= score)
                {
                    Culled++;
                    return null;
                }
                weakest.Source.Stop();
                pick = weakest;
                Stolen++;
            }
            var s = pick.Source;
            s.clip = clip;
            s.loop = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.maxDistance = Mathf.Max(maxDistance, 1f);
            s.transform.position = position;
            s.pitch = pitch;
            s.priority = 255 - Mathf.Clamp(priority, 0, 255);
            s.ignoreListenerPause = bus == Bus.Ui || bus == Bus.Music || bus == Bus.Voice;
            pick.Bus = bus;
            pick.Volume = volume;
            pick.Score = score;
            s.volume = volume * BusGain(bus);
            s.Play();
            return s;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime >= nextScan) Scan();
            if (listener != null) ListenerPosition = listener.position;

            bool voiceActive = duckers.Count > 0;
            foreach (var v in voices)
            {
                if (v.Bus == Bus.Voice && v.Source.isPlaying) voiceActive = true;
            }
            for (int i = 0; i < duck.Length; i++)
            {
                float target = voiceActive ? DuckedGain[i] : 1f;
                duck[i] = Mathf.MoveTowards(duck[i], target, (target < duck[i] ? 3f : 1.2f) * dt);
            }

            foreach (var v in voices)
            {
                if (v.Source.isPlaying) v.Source.volume = v.Volume * BusGain(v.Bus);
            }
            UpdateLoops(dt);
        }

        void UpdateLoops(float dt)
        {
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                var l = loops[i];
                l.Fade = Mathf.MoveTowards(l.Fade, l.Stopping ? 0f : 1f, l.FadeRate * dt);
                if (l.Stopping && l.Fade <= 0f)
                {
                    Release(l);
                    loops.RemoveAt(i);
                    continue;
                }
                if (l.Follow != null) l.Position = l.Follow.position;
                float a = Audibility(l.Spatial, l.Position, l.MaxDistance, Mathf.Max(l.Volume, 0.05f));
                l.Score = a <= 0f ? -1f : a * (0.4f + l.Priority / 255f);
            }
            loops.Sort((x, y) => y.Score.CompareTo(x.Score));
            for (int i = 0; i < loops.Count; i++)
            {
                var l = loops[i];
                bool wanted = i < LoopVoices && l.Score > 0f;
                if (!wanted)
                {
                    Release(l);
                    continue;
                }
                if (l.Source == null)
                {
                    if (freeLoopSources.Count == 0) StealLoopSource(i);
                    if (freeLoopSources.Count == 0) continue;
                    var s = freeLoopSources.Pop();
                    s.clip = l.Clip;
                    s.loop = true;
                    s.priority = 255 - Mathf.Clamp(l.Priority, 0, 255);
                    s.ignoreListenerPause = l.Bus == Bus.Ui || l.Bus == Bus.Voice;
                    s.time = Random.Range(0f, l.Clip.length * 0.9f);
                    s.Play();
                    l.Source = s;
                }
                var src = l.Source;
                src.spatialBlend = l.Spatial ? 1f : 0f;
                src.maxDistance = Mathf.Max(l.MaxDistance, 1f);
                src.transform.position = l.Position;
                src.pitch = Mathf.Clamp(l.Pitch, 0.05f, 3f);
                src.volume = l.Volume * l.Fade * BusGain(l.Bus);
            }
        }

        void StealLoopSource(int rank)
        {
            for (int j = loops.Count - 1; j > rank; j--)
            {
                if (loops[j].Source == null) continue;
                Release(loops[j]);
                Stolen++;
                return;
            }
        }

        void Release(SoundLoop l)
        {
            if (l.Source == null) return;
            l.Source.Stop();
            l.Source.clip = null;
            freeLoopSources.Push(l.Source);
            l.Source = null;
        }

        // Cars and the player are created by gameplay code at any time; attach sound components to
        // them as they appear rather than hooking every spawn site.
        void Scan()
        {
            nextScan = Time.unscaledTime + 0.5f;
            if (game == null) game = FindAnyObjectByType<GameController>();
            var cam = Camera.main;
            if (cam != null && (listener == null || !listener.gameObject.activeInHierarchy))
            {
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                {
                    if (l.gameObject != cam.gameObject) Destroy(l);
                }
                if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
                listener = cam.transform;
            }
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (car.GetComponent<VehicleSounds>() == null) car.gameObject.AddComponent<VehicleSounds>();
            }
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null && player.GetComponent<PlayerSounds>() == null) player.gameObject.AddComponent<PlayerSounds>();
        }
    }
}
