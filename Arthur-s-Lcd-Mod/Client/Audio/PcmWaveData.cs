#if EXPERIMENTAL
namespace LcdMod.Client.Audio
{
    internal sealed class PcmWaveData
    {
        public byte[] Samples;

        public ushort Channels;
        public ushort SourceChannels;
        public uint SampleRate;
        public ushort BitsPerSample;
        public ushort BlockAlign;
        public bool WasDownmixedToMono;

        public double DurationSeconds
        {
            get
            {
                if (Samples == null || SampleRate == 0 || BlockAlign == 0)
                    return 0.0;

                return Samples.Length / (double)BlockAlign / SampleRate;
            }
        }
    }
}
#endif
