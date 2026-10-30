using System;

namespace LcdMod.Client.Video
{
    /// <summary>Brings decoded audio to the 24 kHz, 16-bit mono PCM Space Engineers can play from memory.</summary>
    internal static class MediaPcm
    {
        public const int SAMPLE_RATE = 24000;

        /// <summary>
        /// Downmixes and linearly resamples interleaved 16-bit PCM, padding silence in front when the track
        /// starts after the video.
        /// </summary>
        public static byte[] ToMono24k(byte[] samples, int sampleRate, int channels, double startSeconds)
        {
            if (samples == null || sampleRate <= 0 || channels <= 0)
                throw new ArgumentException("Invalid PCM audio.");

            int frames = samples.Length / (2 * channels);
            int lead = startSeconds > 0 ? (int)Math.Round(startSeconds * SAMPLE_RATE) : 0;
            int length = (int)Math.Round(frames * (double)SAMPLE_RATE / sampleRate);
            var output = new byte[(lead + length) * 2];
            double step = sampleRate / (double)SAMPLE_RATE;
            for (int i = 0; i < length; i++)
            {
                double position = i * step;
                int index = Math.Min((int)position, frames - 1);
                int next = Math.Min(index + 1, frames - 1);
                double fraction = position - index;
                double value = Mono(samples, index, channels) * (1 - fraction) + Mono(samples, next, channels) * fraction;
                short sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(value)));
                int offset = (lead + i) * 2;
                output[offset] = (byte)(sample & 0xFF);
                output[offset + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return output;
        }

        /// <summary>
        /// Places mono 24 kHz audio that starts at <paramref name="audioStart"/> onto the range
        /// <paramref name="rangeStart"/>..<paramref name="rangeEnd"/>, trimming or padding silence at either end.
        /// </summary>
        public static byte[] FitToRange(byte[] mono, double audioStart, double rangeStart, double rangeEnd)
        {
            int length = Math.Max(1, (int)Math.Round((rangeEnd - rangeStart) * SAMPLE_RATE));
            var output = new byte[length * 2];
            if (mono == null)
                return output;

            int shift = (int)Math.Round((audioStart - rangeStart) * SAMPLE_RATE);
            int source = Math.Max(0, -shift);
            int target = Math.Max(0, shift);
            int count = Math.Min(mono.Length / 2 - source, length - target);
            if (count > 0)
                Buffer.BlockCopy(mono, source * 2, output, target * 2, count * 2);

            return output;
        }

        public static byte[] Silence(double seconds)
        {
            return new byte[Math.Max(1, (int)Math.Ceiling(seconds * SAMPLE_RATE)) * 2];
        }

        static double Mono(byte[] samples, int frame, int channels)
        {
            int sum = 0;
            int offset = frame * channels * 2;
            for (int c = 0; c < channels; c++, offset += 2)
                sum += (short)(samples[offset] | (samples[offset + 1] << 8));

            return sum / (double)channels;
        }
    }
}
