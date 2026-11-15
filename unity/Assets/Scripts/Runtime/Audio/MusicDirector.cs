using UnityEngine;

namespace Dogpatch.Audio
{
    // Three synced stems of one 140 BPM arcade track: a calm free-roam layer, a driving drums and
    // bass layer, and the lead hook. The mood picks the blend; free roam lifts a little when you
    // drive fast, and races bring in the full band.
    public sealed class MusicDirector : MonoBehaviour
    {
        static readonly string[] Stems = { "music_roam", "music_drive", "music_lead" };
        static readonly float[] StemGain = { 0.55f, 0.6f, 0.5f };

        readonly AudioSource[] sources = new AudioSource[3];
        readonly float[] level = new float[3];

        void Start()
        {
            double start = AudioSettings.dspTime + 0.2;
            for (int i = 0; i < Stems.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("Audio/Music/" + Stems[i]);
                if (clip == null)
                {
                    Debug.LogWarning("[Audio] missing music stem " + Stems[i]);
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
                s.PlayScheduled(start);
                sources[i] = s;
            }
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
                case MusicMood.Lobby: target = new Vector3(1f, 0.45f, 0f); break;
                case MusicMood.Countdown: target = new Vector3(0.7f, 0.85f, 0f); break;
                case MusicMood.Racing: target = new Vector3(0.35f, 1f, 0.85f); break;
                case MusicMood.FinalStretch: target = new Vector3(0.6f, 1f, 1f); break;
                case MusicMood.Results: target = new Vector3(0.5f, 0f, 0f); break;
                default: target = new Vector3(1f, 0.4f * driveBoost, 0f); break;
            }
            float paused = AudioListener.pause ? 0.45f : 1f;
            for (int i = 0; i < 3; i++)
            {
                if (sources[i] == null) continue;
                level[i] = Mathf.MoveTowards(level[i], target[i], Time.unscaledDeltaTime * (target[i] > level[i] ? 0.6f : 0.9f));
                sources[i].volume = level[i] * StemGain[i] * SoundSystem.BusGain(Bus.Music) * paused;
            }
        }
    }
}
