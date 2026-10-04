using System;

namespace LcdMod.Client.Audio
{
    /// <summary>Copies a playback-aligned window to canonical 24 kHz mono PCM16.</summary>
    internal static class VisualizerPcmWindow
    {
        public const int SAMPLE_RATE = 24000;

        // Leaves samples outside this source untouched, allowing adjacent streaming
        // buffers to contribute to the same zero-initialized destination window.
        public static void Copy(byte[] source, int sampleRate, int channels, int blockAlign,
            double sourceStartSeconds, double positionSeconds, byte[] destination)
        {
            if (source == null || sampleRate <= 0 || channels <= 0 || blockAlign < channels * 2)
                return;
            int frames = source.Length / blockAlign;
            int count = destination.Length / 2;
            double start = positionSeconds - count / (double)SAMPLE_RATE - sourceStartSeconds;
            for (int i = 0; i < count; i++)
            {
                int frame = (int)Math.Floor((start + i / (double)SAMPLE_RATE) * sampleRate + 1e-7);
                if (frame < 0 || frame >= frames)
                    continue;
                int sum = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    int offset = frame * blockAlign + channel * 2;
                    sum += unchecked((short)(source[offset] | source[offset + 1] << 8));
                }
                short sample = (short)(sum / channels);
                destination[i * 2] = unchecked((byte)sample);
                destination[i * 2 + 1] = unchecked((byte)(sample >> 8));
            }
        }
    }
}
