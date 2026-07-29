using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma
{
    /// <summary>
    /// WMAv2 settings derived from the SE xWMA profile and stream rate.
    /// This is codec configuration, not output PCM configuration.
    /// </summary>
    public sealed class Wma2DecoderProfile
    {
        private const ushort XWMA_CODEC_FLAGS = 0x001F;
        private const int FIXED_FRAME_LENGTH_BITS = 11;
        private const int FIXED_FRAME_LENGTH = 1 << FIXED_FRAME_LENGTH_BITS;
        private const int FIXED_MINIMUM_BLOCK_LENGTH_BITS = 7;

        readonly int _byteOffsetBits;

        private Wma2DecoderProfile(XwmaFileInfo file)
        {
            SourceProfile = file.Profile;
            SampleRate = file.Format.SampleRate;
            Channels = file.Format.Channels;
            BitRate = file.Format.AverageBytesPerSecond * 8u;
            PacketSize = file.Format.BlockAlign;
            _byteOffsetBits = CalculateByteOffsetBits(
                file.Format.AverageBytesPerSecond,
                file.Format.Channels,
                file.Format.SampleRate);
        }

        public XwmaProfileKind SourceProfile { get; private set; }
        public uint SampleRate { get; private set; }
        public ushort Channels { get; private set; }
        public uint BitRate { get; private set; }
        public ushort PacketSize { get; private set; }

        public ushort CodecFlags => XWMA_CODEC_FLAGS;

        public bool UsesExponentVlc => (XWMA_CODEC_FLAGS & 0x0001) != 0;

        public bool UsesBitReservoir => (XWMA_CODEC_FLAGS & 0x0002) != 0;

        public bool UsesVariableBlockLength => (XWMA_CODEC_FLAGS & 0x0004) != 0;

        public int FrameLengthBits => FIXED_FRAME_LENGTH_BITS;

        public int FrameLength => FIXED_FRAME_LENGTH;

        public int MinimumBlockLengthBits => FIXED_MINIMUM_BLOCK_LENGTH_BITS;

        public int MinimumBlockLength => 1 << FIXED_MINIMUM_BLOCK_LENGTH_BITS;

        public int BlockSizeCount => 5;

        public int ByteOffsetBits => _byteOffsetBits;

        public int ReservoirBitOffsetFieldBits => _byteOffsetBits + 3;

        public int SuperframeHeaderBits => 4 + 4 + ReservoirBitOffsetFieldBits;

        public bool UsesNoiseCoding => false;

        public int CoefficientVlcTableIndex => 1;

        public static Wma2DecoderProfile FromFile(XwmaFileInfo file)
        {
            if (file == null)
                throw new InvalidDataException("Missing parsed xWMA file information.");

            if (file.Format == null)
                throw new InvalidDataException("The parsed xWMA file has no format information.");

            switch (file.Profile)
            {
                case XwmaProfileKind.Wma2Mono44100Hz:
                case XwmaProfileKind.Wma2Mono48000Hz:
                case XwmaProfileKind.Wma2Stereo44100Hz:
                case XwmaProfileKind.Wma2Stereo48000Hz:
                    return new Wma2DecoderProfile(file);

                default:
                    throw new InvalidDataException("Unsupported restricted WMAv2 profile.");
            }
        }

        static int CalculateByteOffsetBits(
            uint averageBytesPerSecond,
            ushort channels,
            uint sampleRate)
        {
            if (averageBytesPerSecond == 0 || channels == 0 || sampleRate == 0)
                throw new InvalidDataException("Invalid WMAv2 byte-offset profile input.");

            long denominator = (long)channels * sampleRate;
            long roundedBytesPerFrame =
                ((long)averageBytesPerSecond * FIXED_FRAME_LENGTH + denominator / 2L) /
                denominator;

            if (roundedBytesPerFrame <= 0L)
                throw new InvalidDataException("Invalid WMAv2 byte-offset bit width.");

            return FloorLog2(roundedBytesPerFrame) + 2;
        }

        static int FloorLog2(long value)
        {
            int result = 0;

            while (value > 1L)
            {
                value >>= 1;
                result++;
            }

            return result;
        }
    }
}
