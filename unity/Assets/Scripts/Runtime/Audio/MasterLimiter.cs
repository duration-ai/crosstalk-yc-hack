using UnityEngine;

namespace Dogpatch.Audio
{
    // Brick-wall peak limiter on the final mix (on the AudioListener). A race start with four
    // engines, crashes and the full band can sum past full scale; this holds peaks under the
    // ceiling with instant attack and a short release instead of letting them clip.
    public sealed class MasterLimiter : MonoBehaviour
    {
        const float Ceiling = 0.89f;
        const float ReleaseSeconds = 0.18f;

        float gain = 1f;
        float release;

        public static float MinGain { get; private set; } = 1f;

        void Awake() => release = 1f - Mathf.Exp(-1f / (ReleaseSeconds * AudioSettings.outputSampleRate));

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));
                float target = peak * gain > Ceiling ? Ceiling / peak : 1f;
                gain = target < gain ? target : gain + (1f - gain) * release;
                if (gain < MinGain) MinGain = gain;
                for (int c = 0; c < channels; c++) data[i + c] *= gain;
            }
        }
    }
}
