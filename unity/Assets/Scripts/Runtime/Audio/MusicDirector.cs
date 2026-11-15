using UnityEngine;

namespace Dogpatch.Audio
{
    // Three looping tracks crossfaded by mood: a free-roam bed (also the lobby and results bed), the
    // race track, and its hotter final-stretch version. A track starts from its top when it fades in
    // and stops once it has faded out. Free roam lifts a little when you drive fast; pause muffles
    // and softens whatever is playing.
    public sealed class MusicDirector : MonoBehaviour
    {
        static readonly string[] Tracks = { "music_roam", "music_race", "music_final" };
        static readonly float[] TrackGain = { 0.5f, 0.75f, 0.75f };

        readonly AudioSource[] sources = new AudioSource[3];
        readonly float[] level = new float[3];
        AudioLowPassFilter muffle;

        void Start()
        {
            for (int i = 0; i < Tracks.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("Audio/Music/" + Tracks[i]);
                if (clip == null)
                {
                    Debug.LogWarning("[Audio] missing music track " + Tracks[i]);
                    continue;
                }
                var s = gameObject.AddComponent<AudioSource>();
                s.clip = clip;
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.ignoreListenerPause = true;
                s.volume = 0f;
                sources[i] = s;
            }
            muffle = gameObject.AddComponent<AudioLowPassFilter>();
            muffle.cutoffFrequency = 22000f;
            GameEvents.Results += _ => SoundSystem.Play2D("results_sting", Bus.Music, 1f, 1f, 255);
        }

        void Update()
        {
            float driveBoost = 0f;
            var game = SoundSystem.Game;
            if (game != null && game.Driving)
            {
                var car = game.View != null ? game.View.GetComponent<Gameplay.FollowCamera>()?.Target?.GetComponent<Gameplay.CarController>() : null;
                if (car != null) driveBoost = Mathf.InverseLerp(8f, 20f, Mathf.Abs(car.Speed));
            }
            Vector3 target;
            switch (GameEvents.CurrentMood)
            {
                case MusicMood.Lobby: target = new Vector3(0.8f, 0f, 0f); break;
                case MusicMood.Countdown: target = new Vector3(0f, 0.7f, 0f); break;
                case MusicMood.Racing: target = new Vector3(0f, 1f, 0f); break;
                case MusicMood.FinalStretch: target = new Vector3(0f, 0f, 1f); break;
                case MusicMood.Results: target = new Vector3(0.6f, 0f, 0f); break;
                default: target = new Vector3(0.8f + 0.2f * driveBoost, 0f, 0f); break;
            }
            bool paused = AudioListener.pause;
            if (muffle != null) muffle.cutoffFrequency = Mathf.MoveTowards(muffle.cutoffFrequency, paused ? 900f : 22000f, Time.unscaledDeltaTime * 60000f);
            for (int i = 0; i < 3; i++)
            {
                var s = sources[i];
                if (s == null) continue;
                level[i] = Mathf.MoveTowards(level[i], target[i], Time.unscaledDeltaTime * (target[i] > level[i] ? 1.5f : 0.8f));
                if (level[i] > 0f && !s.isPlaying)
                {
                    s.time = 0f;
                    s.Play();
                }
                else if (level[i] <= 0f && s.isPlaying) s.Stop();
                s.volume = level[i] * TrackGain[i] * SoundSystem.BusGain(Bus.Music) * (paused ? 0.45f : 1f);
            }
        }
    }
}
