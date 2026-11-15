using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Vehicles
{
    // Cheap synthesized sounds, generated once at startup: a two-tone car horn, the soft robotaxi
    // chime, the cart's wheel squeak, the bus and train bell, and a bicycle bell. One 3D AudioSource
    // per vehicle; the cart squeaks on its own whenever it rolls.
    public sealed class VehicleAudio : MonoBehaviour
    {
        const int Rate = 22050;
        static readonly Dictionary<SoundKind, AudioClip> clips = new Dictionary<SoundKind, AudioClip>();

        AudioSource source;
        SoundKind kind;
        CarController car;
        float nextSound;
        float nextSqueak;

        public void Configure(VehicleSpec spec)
        {
            kind = spec.Sound;
            car = GetComponent<CarController>();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 4f;
                source.maxDistance = 70f;
                source.dopplerLevel = 0f;
            }
            source.volume = kind == SoundKind.Bell ? 0.8f : 0.6f;
        }

        // Plays the vehicle's own signal (horn, chime, bell) unless it played within cooldown seconds.
        public bool Signal(float cooldown = 3f)
        {
            if (Time.time < nextSound || source == null) return false;
            nextSound = Time.time + cooldown;
            source.pitch = Random.Range(0.95f, 1.05f);
            source.PlayOneShot(Clip(kind == SoundKind.Squeak ? SoundKind.Squeak : kind));
            return true;
        }

        void Update()
        {
            if (kind != SoundKind.Squeak || car == null || source == null) return;
            float speed = Mathf.Abs(car.Speed);
            if (speed < 0.3f || Time.time < nextSqueak) return;
            nextSqueak = Time.time + Mathf.Lerp(0.9f, 0.28f, speed / 6f) * Random.Range(0.8f, 1.2f);
            source.pitch = Random.Range(0.85f, 1.25f);
            source.PlayOneShot(Clip(SoundKind.Squeak), 0.5f);
        }

        public static AudioClip Clip(SoundKind kind)
        {
            if (clips.TryGetValue(kind, out var clip)) return clip;
            float seconds = kind == SoundKind.Horn ? 0.45f : kind == SoundKind.Squeak ? 0.16f : kind == SoundKind.Chime ? 0.7f : kind == SoundKind.BikeBell ? 0.6f : 1.4f;
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float s;
                switch (kind)
                {
                    case SoundKind.Horn:
                        float env = Mathf.Clamp01(t * 60f) * Mathf.Clamp01((seconds - t) * 30f);
                        s = (Square(t * 349f) * 0.5f + Square(t * 440f) * 0.5f) * 0.35f * env;
                        break;
                    case SoundKind.Chime:
                        float n = t < 0.3f ? 784f : 1046f;
                        float local = t < 0.3f ? t : t - 0.3f;
                        s = Mathf.Sin(2f * Mathf.PI * n * t) * Mathf.Exp(-local * 6f) * 0.45f;
                        break;
                    case SoundKind.Squeak:
                        float f = 2100f + Mathf.Sin(t * 90f) * 500f;
                        s = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Sin(Mathf.PI * t / seconds) * 0.25f;
                        break;
                    case SoundKind.BikeBell:
                        float b = t < 0.18f ? t : t - 0.18f;
                        s = (Mathf.Sin(2f * Mathf.PI * 2637f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 3950f * t)) * Mathf.Exp(-b * 9f) * 0.3f;
                        break;
                    default:
                        float d = t < 0.5f ? t : t - 0.5f;
                        s = (Mathf.Sin(2f * Mathf.PI * 1180f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 2950f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 4720f * t)) * Mathf.Exp(-d * 4.5f) * 0.3f;
                        break;
                }
                data[i] = s;
            }
            clip = AudioClip.Create(kind.ToString(), data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clips[kind] = clip;
            return clip;
        }

        static float Square(float phase) => (phase - Mathf.Floor(phase)) < 0.5f ? 1f : -1f;
    }
}
