#if EXPERIMENTAL
using System;

namespace LcdMod.Client.Audio.Adpcm
{
    internal sealed class MsAdpcmFormat
    {
        public ushort Channels;
        public uint SampleRate;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort SamplesPerBlock;
        public short[] Coefficients1;
        public short[] Coefficients2;
        public int CoefficientCount;
    }

    internal static class MsAdpcmDecoder
    {
        const int MINIMUM_DELTA = 16;
        const int MAXIMUM_COEFFICIENT_COUNT = 64;

        static readonly int[] AdaptationTable = new int[]
        {
            230, 230, 230, 230,
            307, 409, 512, 614,
            768, 614, 512, 409,
            307, 230, 230, 230
        };

        public static bool TryReadFormat(
            ushort channels,
            uint sampleRate,
            ushort blockAlign,
            ushort bitsPerSample,
            byte[] formatExtension,
            out MsAdpcmFormat format,
            out string failureReason)
        {
            format = null;
            failureReason = string.Empty;

            if (channels != 1 && channels != 2)
            {
                failureReason = "Only mono or stereo Microsoft ADPCM WAV is supported.";
                return false;
            }

            if (bitsPerSample != 4)
            {
                failureReason = "Expected 4-bit Microsoft ADPCM WAV.";
                return false;
            }

            int headerByteCount = 7 * channels;
            if (blockAlign <= headerByteCount)
            {
                failureReason = "Microsoft ADPCM block alignment is too small.";
                return false;
            }

            if (formatExtension == null || formatExtension.Length < 4)
            {
                failureReason = "Missing Microsoft ADPCM format extension.";
                return false;
            }

            ushort samplesPerBlock = ReadUInt16LittleEndian(formatExtension, 0);
            ushort coefficientCount = ReadUInt16LittleEndian(formatExtension, 2);

            if (samplesPerBlock < 2)
            {
                failureReason = "Invalid Microsoft ADPCM samples-per-block metadata.";
                return false;
            }

            int maximumSamplesPerBlock = 2 + ((blockAlign - headerByteCount) * 2 / channels);
            if (samplesPerBlock > maximumSamplesPerBlock)
            {
                failureReason = "Microsoft ADPCM samples-per-block exceeds the block capacity.";
                return false;
            }

            if (coefficientCount == 0 || coefficientCount > MAXIMUM_COEFFICIENT_COUNT)
            {
                failureReason = "Invalid Microsoft ADPCM coefficient count: " + coefficientCount + ".";
                return false;
            }

            int requiredExtensionBytes = 4 + coefficientCount * 4;
            if (formatExtension.Length < requiredExtensionBytes)
            {
                failureReason = "Microsoft ADPCM coefficient table is truncated.";
                return false;
            }

            var coefficients1 = new short[coefficientCount];
            var coefficients2 = new short[coefficientCount];
            int coefficientOffset = 4;
            for (int i = 0; i < coefficientCount; i++)
            {
                coefficients1[i] = ReadInt16LittleEndian(formatExtension, coefficientOffset);
                coefficients2[i] = ReadInt16LittleEndian(formatExtension, coefficientOffset + 2);
                coefficientOffset += 4;
            }

            format = new MsAdpcmFormat
            {
                Channels = channels,
                SampleRate = sampleRate,
                BlockAlign = blockAlign,
                BitsPerSample = bitsPerSample,
                SamplesPerBlock = samplesPerBlock,
                Coefficients1 = coefficients1,
                Coefficients2 = coefficients2,
                CoefficientCount = coefficientCount
            };
            return true;
        }

        public static bool TryDecode(
            byte[] adpcmData,
            MsAdpcmFormat format,
            int maximumOutputBytes,
            out byte[] pcm16Interleaved,
            out string failureReason)
        {
            pcm16Interleaved = null;
            failureReason = string.Empty;

            if (adpcmData == null || adpcmData.Length == 0)
            {
                failureReason = "Microsoft ADPCM data chunk is empty.";
                return false;
            }

            if (format == null)
            {
                failureReason = "Missing Microsoft ADPCM format metadata.";
                return false;
            }

            if (format.Channels != 1 && format.Channels != 2)
            {
                failureReason = "Only mono or stereo Microsoft ADPCM blocks are supported.";
                return false;
            }

            if (format.BlockAlign == 0 || adpcmData.Length % format.BlockAlign != 0)
            {
                failureReason = "Microsoft ADPCM data is not block-aligned.";
                return false;
            }

            int blockCount = adpcmData.Length / format.BlockAlign;
            long outputFrameCount = (long)blockCount * format.SamplesPerBlock;
            long outputByteCount = outputFrameCount * format.Channels * 2L;

            if (outputByteCount > maximumOutputBytes)
            {
                failureReason = "Decoded Microsoft ADPCM exceeds the 32 MiB limit.";
                return false;
            }

            if (outputByteCount > int.MaxValue)
            {
                failureReason = "Decoded Microsoft ADPCM is too large.";
                return false;
            }

            var output = new byte[(int)outputByteCount];
            int outputOffset = 0;

            for (int block = 0; block < blockCount; block++)
            {
                if (!DecodeBlock(
                        adpcmData,
                        block * format.BlockAlign,
                        format,
                        output,
                        ref outputOffset,
                        out failureReason))
                {
                    return false;
                }
            }

            if (outputOffset != output.Length)
            {
                failureReason = "Microsoft ADPCM decoder produced an unexpected sample count.";
                return false;
            }

            pcm16Interleaved = output;
            return true;
        }

        static bool DecodeBlock(
            byte[] input,
            int blockOffset,
            MsAdpcmFormat format,
            byte[] output,
            ref int outputOffset,
            out string failureReason)
        {
            failureReason = string.Empty;

            int channels = format.Channels;
            int blockEnd = blockOffset + format.BlockAlign;
            int predictorOffset = blockOffset;
            int deltaOffset = predictorOffset + channels;
            int sample1Offset = deltaOffset + channels * 2;
            int sample2Offset = sample1Offset + channels * 2;
            int payloadOffset = sample2Offset + channels * 2;

            if (payloadOffset > blockEnd)
            {
                failureReason = "Microsoft ADPCM block header is truncated.";
                return false;
            }

            var states = new ChannelState[channels];
            for (int channel = 0; channel < channels; channel++)
            {
                int predictor = input[predictorOffset + channel];
                if (predictor < 0 || predictor >= format.CoefficientCount)
                {
                    failureReason = "Microsoft ADPCM predictor index is out of range: " + predictor + ".";
                    return false;
                }

                int delta = ReadInt16LittleEndian(input, deltaOffset + channel * 2);
                if (delta < MINIMUM_DELTA)
                    delta = MINIMUM_DELTA;

                states[channel] = new ChannelState
                {
                    Delta = delta,
                    Sample1 = ReadInt16LittleEndian(input, sample1Offset + channel * 2),
                    Sample2 = ReadInt16LittleEndian(input, sample2Offset + channel * 2),
                    Coefficient1 = format.Coefficients1[predictor],
                    Coefficient2 = format.Coefficients2[predictor]
                };
            }

            WriteInitialFrame(states, output, ref outputOffset, false);
            WriteInitialFrame(states, output, ref outputOffset, true);
            int frameInBlock = 2;
            int offset = payloadOffset;

            if (channels == 1)
            {
                while (frameInBlock < format.SamplesPerBlock && offset < blockEnd)
                {
                    int packed = input[offset++];
                    short sample = DecodeNibble(ref states[0], (packed >> 4) & 0x0f);
                    WritePcm16(output, ref outputOffset, sample);
                    frameInBlock++;

                    if (frameInBlock >= format.SamplesPerBlock)
                        break;

                    sample = DecodeNibble(ref states[0], packed & 0x0f);
                    WritePcm16(output, ref outputOffset, sample);
                    frameInBlock++;
                }
            }
            else
            {
                while (frameInBlock < format.SamplesPerBlock && offset < blockEnd)
                {
                    int packed = input[offset++];
                    short left = DecodeNibble(ref states[0], (packed >> 4) & 0x0f);
                    short right = DecodeNibble(ref states[1], packed & 0x0f);
                    WritePcm16(output, ref outputOffset, left);
                    WritePcm16(output, ref outputOffset, right);
                    frameInBlock++;
                }
            }

            if (frameInBlock != format.SamplesPerBlock)
            {
                failureReason = "Microsoft ADPCM block ended before samples-per-block was reached.";
                return false;
            }

            return true;
        }

        static void WriteInitialFrame(
            ChannelState[] states,
            byte[] output,
            ref int outputOffset,
            bool useSample1)
        {
            for (int channel = 0; channel < states.Length; channel++)
            {
                WritePcm16(
                    output,
                    ref outputOffset,
                    useSample1 ? states[channel].Sample1 : states[channel].Sample2);
            }
        }

        static short DecodeNibble(
            ref ChannelState state,
            int nibble)
        {
            int signedNibble = nibble >= 8 ? nibble - 16 : nibble;
            int predicted = ((state.Sample1 * state.Coefficient1) +
                             (state.Sample2 * state.Coefficient2)) >> 8;
            int decoded = predicted + signedNibble * state.Delta;
            short sample = ClampToInt16(decoded);

            state.Sample2 = state.Sample1;
            state.Sample1 = sample;

            int delta = (AdaptationTable[nibble] * state.Delta) >> 8;
            state.Delta = delta < MINIMUM_DELTA ? MINIMUM_DELTA : delta;

            return sample;
        }

        static void WritePcm16(
            byte[] output,
            ref int offset,
            short sample)
        {
            output[offset++] = (byte)(sample & 0xff);
            output[offset++] = (byte)((sample >> 8) & 0xff);
        }

        static short ClampToInt16(int value)
        {
            if (value > short.MaxValue)
                return short.MaxValue;

            if (value < short.MinValue)
                return short.MinValue;

            return (short)value;
        }

        static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static short ReadInt16LittleEndian(byte[] bytes, int offset)
        {
            return unchecked((short)(bytes[offset] | (bytes[offset + 1] << 8)));
        }

        struct ChannelState
        {
            public int Delta;
            public short Sample1;
            public short Sample2;
            public int Coefficient1;
            public int Coefficient2;
        }
    }
}
#endif
