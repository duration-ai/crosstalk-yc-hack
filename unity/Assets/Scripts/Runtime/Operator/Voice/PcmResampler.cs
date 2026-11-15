using System;
using System.Collections.Generic;

namespace Dogpatch.Operator.Voice
{
    // Streams microphone frames (float, any rate, interleaved channels) into mono int16 at the
    // target rate by linear interpolation. State carries across calls, so chunk boundaries never
    // click or drop a sample.
    public sealed class PcmResampler
    {
        readonly double step;
        double position;
        float previous;
        bool hasPrevious;

        public int InputRate { get; }
        public int OutputRate { get; }

        public PcmResampler(int inputRate, int outputRate)
        {
            InputRate = inputRate;
            OutputRate = outputRate;
            step = (double)inputRate / outputRate;
        }

        public static short ToInt16(float sample)
        {
            if (sample > 1f) sample = 1f;
            else if (sample < -1f) sample = -1f;
            return (short)Math.Round(sample * 32767f);
        }

        public void Process(float[] interleaved, int frames, int channels, List<short> output)
        {
            if (frames <= 0) return;
            float Mono(int frame)
            {
                if (channels == 1) return interleaved[frame];
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[frame * channels + c];
                return sum / channels;
            }

            if (!hasPrevious)
            {
                previous = Mono(0);
                hasPrevious = true;
                position = 1.0;
            }
            while (position <= frames)
            {
                int index = (int)Math.Floor(position);
                double frac = position - index;
                float a = index == 0 ? previous : Mono(index - 1);
                float b = index < frames ? Mono(index) : a;
                output.Add(ToInt16((float)(a + (b - a) * frac)));
                position += step;
            }
            previous = Mono(frames - 1);
            position -= frames;
        }
    }

    // The last few hundred milliseconds of int16 audio, so the words just before the Call plate
    // is pressed still reach the transcriber.
    public sealed class PcmRing
    {
        readonly short[] buffer;
        int start;
        int count;

        public PcmRing(int capacity) => buffer = new short[Math.Max(1, capacity)];

        public int Count => count;

        public void Write(List<short> samples)
        {
            foreach (var s in samples)
            {
                buffer[(start + count) % buffer.Length] = s;
                if (count < buffer.Length) count++;
                else start = (start + 1) % buffer.Length;
            }
        }

        public void DrainTo(List<short> output)
        {
            for (int i = 0; i < count; i++) output.Add(buffer[(start + i) % buffer.Length]);
            start = 0;
            count = 0;
        }
    }
}
