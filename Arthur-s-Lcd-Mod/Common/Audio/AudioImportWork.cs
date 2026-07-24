using System;

namespace LcdMod.Common.Audio
{
    internal sealed class AudioImportWork
    {
        public string AssetId;

        public ulong OwnerSteamId;

        public string SourcePath;
        public byte[] SourceBytes;

        public string SourceSha256;

        public string RuntimePath;
        public byte[] RuntimeWaveBytes;

        public string RuntimeSha256;

        public long PcmByteLength;
        public long DurationTicks;

        public int SourceSampleRate;
        public int SourceChannels;
        public int SourceBitsPerSample;
        public string SourceEncodingName;

        public bool WasNormalized;
        public Exception Error;
    }
}
