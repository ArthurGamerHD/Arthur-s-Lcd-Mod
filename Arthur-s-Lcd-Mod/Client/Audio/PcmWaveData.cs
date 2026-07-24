namespace LcdMod.Client.Audio
{
    internal sealed class PcmWaveData
    {
        public byte[] Samples;

        public ushort Channels;
        public ushort SourceChannels;
        public uint SampleRate;
        public uint SourceSampleRate;
        public ushort BitsPerSample;
        public ushort SourceBitsPerSample;
        public ushort BlockAlign;
        public bool WasDownmixedToMono;
        public bool WasResampled;
        public string SourceFormatDisplayName;

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
