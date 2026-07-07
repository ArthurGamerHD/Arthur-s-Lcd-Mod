#if EXPERIMENTAL
namespace LcdMod.Client.Audio.Xwma
{
    internal static class PcmAudioFormat
    {
        public const ushort WAVE_FORMAT_PCM = 1;
        public const ushort REQUIRED_MONO_CHANNELS = 1;
        public const ushort SUPPORTED_STEREO_CHANNELS = 2;
        public const uint REQUIRED_SAMPLE_RATE = 24000;
        public const ushort REQUIRED_BITS_PER_SAMPLE = 16;
        public const ushort REQUIRED_MONO_BLOCK_ALIGN = 2;
        public const ushort REQUIRED_STEREO_BLOCK_ALIGN = 4;
        public const int MAXIMUM_PCM_BYTES = 32 * 1024 * 1024;
    }
}
#endif
