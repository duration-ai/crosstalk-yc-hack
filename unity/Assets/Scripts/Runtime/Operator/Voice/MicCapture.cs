using System;
using UnityEngine;

namespace Dogpatch.Operator.Voice
{
    // Unity's Microphone as a stream: a looping clip, read from the last position every frame.
    public sealed class MicCapture
    {
        const int LoopSeconds = 2;

        AudioClip clip;
        string device;
        int readAt;
        float[] scratch = new float[0];

        public bool Running => clip != null;

        // Peak of the latest block read, 0 to 1; 0 while stopped. Read-only, for the call card's waveform.
        public static float Level { get; private set; }
        public int Rate => clip != null ? clip.frequency : 0;
        public int Channels => clip != null ? clip.channels : 1;

        public bool Start(int preferredRate)
        {
            if (clip != null) return true;
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[Operator] no microphone on this device");
                return false;
            }
            device = Microphone.devices[0];
            Microphone.GetDeviceCaps(device, out int min, out int max);
            int rate = min == 0 && max == 0 ? preferredRate : Mathf.Clamp(preferredRate, min, max);
            clip = Microphone.Start(device, true, LoopSeconds, rate);
            if (clip == null)
            {
                Debug.LogWarning("[Operator] Microphone.Start failed on " + device);
                return false;
            }
            readAt = 0;
            Debug.Log($"[Operator] microphone {device} at {clip.frequency} Hz x{clip.channels}");
            return true;
        }

        // Hands every frame recorded since the last call to sink(samples, frames, channels).
        public void Poll(Action<float[], int, int> sink)
        {
            if (clip == null) return;
            int at = Microphone.GetPosition(device);
            if (at < 0 || at == readAt) return;
            int frames = (at - readAt + clip.samples) % clip.samples;
            int needed = frames * clip.channels;
            if (scratch.Length < needed) scratch = new float[needed];
            var block = scratch.Length == needed ? scratch : new float[needed];
            clip.GetData(block, readAt);
            readAt = at;
            float peak = 0f;
            for (int i = 0; i < needed; i++) peak = Mathf.Max(peak, Mathf.Abs(block[i]));
            Level = peak;
            sink(block, frames, clip.channels);
        }

        public void Stop()
        {
            if (clip == null) return;
            Microphone.End(device);
            Level = 0f;
            UnityEngine.Object.Destroy(clip);
            clip = null;
        }
    }
}
