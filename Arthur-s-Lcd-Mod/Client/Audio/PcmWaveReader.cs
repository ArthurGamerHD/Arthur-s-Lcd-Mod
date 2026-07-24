using System;
using System.IO;
using System.Text;
using LcdMod.Client.Audio.Adpcm;

namespace LcdMod.Client.Audio
{
    internal static class PcmWaveReader
    {
        const ushort WAVE_FORMAT_PCM = 1;
        const ushort WAVE_FORMAT_MS_ADPCM = 2;
        const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;
        const ushort REQUIRED_MONO_CHANNELS = 1;
        const uint REQUIRED_SAMPLE_RATE = 24000;
        const ushort REQUIRED_BITS_PER_SAMPLE = 16;
        const ushort REQUIRED_MONO_BLOCK_ALIGN = 2;
        const int MAXIMUM_PCM_BYTES = 32 * 1024 * 1024;
        const int MAXIMUM_TRUSTED_LOCAL_PCM_BYTES = 256 * 1024 * 1024;
        const int MAXIMUM_CHANNELS = 8;
        const uint MAXIMUM_SAMPLE_RATE = 384000;

        static readonly byte[] PcmSubFormatGuid = new byte[]
        {
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00,
            0x10, 0x00,
            0x80, 0x00,
            0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71
        };

        static readonly byte[] IeeeFloatSubFormatGuid = new byte[]
        {
            0x03, 0x00, 0x00, 0x00,
            0x00, 0x00,
            0x10, 0x00,
            0x80, 0x00,
            0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71
        };

        public static bool TryRead(BinaryReader reader, out PcmWaveData result, out string failureReason)
        {
            return TryRead(reader, false, out result, out failureReason);
        }

        public static bool TryRead(
            BinaryReader reader,
            bool allowTrustedLocalPayload,
            out PcmWaveData result,
            out string failureReason)
        {
            result = null;
            failureReason = string.Empty;

            if (reader == null || reader.BaseStream == null)
            {
                failureReason = "Missing input stream.";
                return false;
            }

            var stream = reader.BaseStream;

            if (!stream.CanRead || !stream.CanSeek)
            {
                failureReason = "WAV stream must be readable and seekable.";
                return false;
            }

            try
            {
                stream.Position = 0;

                if (stream.Length < 12)
                {
                    failureReason = "File is too small to be a RIFF/WAVE file.";
                    return false;
                }

                var riff = ReadFourCc(reader);
                reader.ReadUInt32();
                var wave = ReadFourCc(reader);

                if (riff != "RIFF" || wave != "WAVE")
                {
                    failureReason = "Expected RIFF/WAVE container.";
                    return false;
                }

                var hasFormat = false;
                var formatTag = (ushort)0;
                var channels = (ushort)0;
                var sampleRate = (uint)0;
                var blockAlign = (ushort)0;
                var bitsPerSample = (ushort)0;
                var validBitsPerSample = (ushort)0;
                byte[] subFormatGuid = null;
                byte[] formatExtension = null;
                byte[] samples = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    var chunkId = ReadFourCc(reader);
                    var chunkSize = reader.ReadUInt32();
                    var chunkStart = stream.Position;
                    var chunkEnd = chunkStart + chunkSize;

                    if (chunkEnd > stream.Length)
                    {
                        failureReason = "WAV chunk exceeds file length.";
                        return false;
                    }

                    if (chunkId == "fmt ")
                    {
                        if (chunkSize < 16)
                        {
                            failureReason = "Invalid fmt chunk.";
                            return false;
                        }

                        formatTag = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadUInt32();
                        reader.ReadUInt32();
                        blockAlign = reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();
                        hasFormat = true;

                        if (chunkSize > 16)
                        {
                            if (chunkSize < 18)
                            {
                                failureReason = "Invalid WAV format extension.";
                                return false;
                            }

                            ushort extensionSize = reader.ReadUInt16();
                            long availableExtensionBytes = chunkEnd - stream.Position;
                            if (extensionSize > availableExtensionBytes)
                            {
                                failureReason = "WAV format extension exceeds fmt chunk.";
                                return false;
                            }

                            formatExtension = reader.ReadBytes(extensionSize);
                            if (formatExtension.Length != extensionSize)
                            {
                                failureReason = "Could not read full WAV format extension.";
                                return false;
                            }

                            if (formatTag == WAVE_FORMAT_EXTENSIBLE)
                            {
                                if (extensionSize < 22)
                                {
                                    failureReason = "Invalid WAVE_FORMAT_EXTENSIBLE fmt chunk.";
                                    return false;
                                }

                                validBitsPerSample = ReadUInt16LittleEndian(formatExtension, 0);
                                subFormatGuid = new byte[16];
                                Array.Copy(formatExtension, 6, subFormatGuid, 0, 16);
                            }
                        }
                    }
                    else if (chunkId == "data")
                    {
                        if (chunkSize == 0)
                        {
                            failureReason = "WAV data chunk is empty.";
                            return false;
                        }

                        int maximumInputBytes = allowTrustedLocalPayload
                            ? MAXIMUM_TRUSTED_LOCAL_PCM_BYTES
                            : MAXIMUM_PCM_BYTES;
                        if (chunkSize > maximumInputBytes)
                        {
                            failureReason = allowTrustedLocalPayload
                                ? "Trusted local audio payload exceeds safety size limit."
                                : "Audio payload exceeds proof-of-concept size limit.";
                            return false;
                        }

                        samples = reader.ReadBytes((int)chunkSize);

                        if (samples.Length != chunkSize)
                        {
                            failureReason = "Could not read full WAV data chunk.";
                            return false;
                        }
                    }

                    stream.Position = chunkEnd;

                    // RIFF chunks are padded to an even-byte boundary.
                    if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                        stream.Position++;
                }

                if (!hasFormat)
                {
                    failureReason = "Missing fmt chunk.";
                    return false;
                }

                if (samples == null)
                {
                    failureReason = "Missing data chunk.";
                    return false;
                }

                var effectiveFormatTag = ResolveFormatTag(
                    formatTag,
                    subFormatGuid,
                    channels,
                    sampleRate,
                    bitsPerSample,
                    blockAlign,
                    out failureReason);
                if (effectiveFormatTag == 0)
                    return false;

                if (channels < 1 || channels > MAXIMUM_CHANNELS)
                {
                    failureReason = "Expected a 1 to 8 channel WAV.";
                    return false;
                }

                if (sampleRate == 0 || sampleRate > MAXIMUM_SAMPLE_RATE)
                {
                    failureReason = "Unsupported WAV sample rate: " + sampleRate + " Hz.";
                    return false;
                }

                if (effectiveFormatTag == WAVE_FORMAT_MS_ADPCM)
                {
                    return TryReadMsAdpcm(
                        samples,
                        channels,
                        sampleRate,
                        blockAlign,
                        bitsPerSample,
                        formatExtension,
                        allowTrustedLocalPayload,
                        out result,
                        out failureReason);
                }

                if (!IsSupportedBitsPerSample(effectiveFormatTag, bitsPerSample))
                {
                    failureReason = effectiveFormatTag == WAVE_FORMAT_IEEE_FLOAT
                        ? "Expected 32-bit float WAV."
                        : "Expected 8-bit, 16-bit, 24-bit, or 32-bit PCM WAV.";
                    return false;
                }

                if (validBitsPerSample != 0 && validBitsPerSample > bitsPerSample)
                {
                    failureReason = "Invalid WAV valid-bits-per-sample metadata.";
                    return false;
                }

                var bytesPerSample = bitsPerSample / 8;
                var requiredBlockAlign = checked((ushort)(channels * bytesPerSample));

                if (blockAlign != requiredBlockAlign)
                {
                    failureReason = "Expected " + requiredBlockAlign + "-byte PCM block alignment.";
                    return false;
                }

                if (samples.Length % blockAlign != 0)
                {
                    failureReason = "PCM payload is not sample-aligned.";
                    return false;
                }

                var sourceFrameCount = samples.Length / blockAlign;
                if (sourceFrameCount <= 0)
                {
                    failureReason = "PCM payload has no sample frames.";
                    return false;
                }

                var monoSource = DecodeToMonoFloat(
                    samples,
                    sourceFrameCount,
                    channels,
                    blockAlign,
                    bytesPerSample,
                    bitsPerSample,
                    effectiveFormatTag);

                var normalizedSamples = ResampleMonoFloatToPcm16(
                    monoSource,
                    sampleRate,
                    allowTrustedLocalPayload);

                result = new PcmWaveData
                {
                    Samples = normalizedSamples,
                    Channels = REQUIRED_MONO_CHANNELS,
                    SourceChannels = channels,
                    SampleRate = REQUIRED_SAMPLE_RATE,
                    SourceSampleRate = sampleRate,
                    BitsPerSample = REQUIRED_BITS_PER_SAMPLE,
                    SourceBitsPerSample = bitsPerSample,
                    BlockAlign = REQUIRED_MONO_BLOCK_ALIGN,
                    WasDownmixedToMono = channels != REQUIRED_MONO_CHANNELS,
                    WasResampled = sampleRate != REQUIRED_SAMPLE_RATE,
                    SourceFormatDisplayName = effectiveFormatTag == WAVE_FORMAT_IEEE_FLOAT
                        ? "ieee float wav"
                        : "pcm wav"
                };

                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        static bool TryReadMsAdpcm(
            byte[] samples,
            ushort channels,
            uint sampleRate,
            ushort blockAlign,
            ushort bitsPerSample,
            byte[] formatExtension,
            bool allowTrustedLocalPayload,
            out PcmWaveData result,
            out string failureReason)
        {
            result = null;
            failureReason = string.Empty;

            MsAdpcmFormat format;
            if (!MsAdpcmDecoder.TryReadFormat(
                    channels,
                    sampleRate,
                    blockAlign,
                    bitsPerSample,
                    formatExtension,
                    out format,
                    out failureReason))
            {
                return false;
            }

            byte[] decodedPcm16;
            if (!MsAdpcmDecoder.TryDecode(
                    samples,
                    format,
                    GetMaximumPcmBytes(allowTrustedLocalPayload),
                    out decodedPcm16,
                    out failureReason))
            {
                return false;
            }

            ushort decodedBlockAlign = checked((ushort)(channels * 2));
            if (decodedPcm16.Length % decodedBlockAlign != 0)
            {
                failureReason = "Decoded Microsoft ADPCM is not sample-aligned.";
                return false;
            }

            int sourceFrameCount = decodedPcm16.Length / decodedBlockAlign;
            if (sourceFrameCount <= 0)
            {
                failureReason = "Decoded Microsoft ADPCM has no sample frames.";
                return false;
            }

            var monoSource = DecodeToMonoFloat(
                decodedPcm16,
                sourceFrameCount,
                channels,
                decodedBlockAlign,
                2,
                16,
                WAVE_FORMAT_PCM);

            var normalizedSamples = ResampleMonoFloatToPcm16(
                monoSource,
                sampleRate,
                allowTrustedLocalPayload);

            result = new PcmWaveData
            {
                Samples = normalizedSamples,
                Channels = REQUIRED_MONO_CHANNELS,
                SourceChannels = channels,
                SampleRate = REQUIRED_SAMPLE_RATE,
                SourceSampleRate = sampleRate,
                BitsPerSample = REQUIRED_BITS_PER_SAMPLE,
                SourceBitsPerSample = bitsPerSample,
                BlockAlign = REQUIRED_MONO_BLOCK_ALIGN,
                WasDownmixedToMono = channels != REQUIRED_MONO_CHANNELS,
                WasResampled = sampleRate != REQUIRED_SAMPLE_RATE,
                SourceFormatDisplayName = "microsoft adpcm wav"
            };

            return true;
        }

        static ushort ResolveFormatTag(
            ushort formatTag,
            byte[] subFormatGuid,
            ushort channels,
            uint sampleRate,
            ushort bitsPerSample,
            ushort blockAlign,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (formatTag == WAVE_FORMAT_PCM ||
                formatTag == WAVE_FORMAT_IEEE_FLOAT ||
                formatTag == WAVE_FORMAT_MS_ADPCM)
            {
                return formatTag;
            }

            if (formatTag != WAVE_FORMAT_EXTENSIBLE)
            {
                failureReason = "Unsupported WAV codec " + FormatCodecTag(formatTag) +
                    ", " + sampleRate + "hz " + channels + "ch " + bitsPerSample +
                    "bit, blockAlign " + blockAlign + ".";
                return 0;
            }

            if (subFormatGuid == null || subFormatGuid.Length != 16)
            {
                failureReason = "Missing WAVE_FORMAT_EXTENSIBLE subformat.";
                return 0;
            }

            if (GuidEquals(subFormatGuid, PcmSubFormatGuid))
                return WAVE_FORMAT_PCM;

            if (GuidEquals(subFormatGuid, IeeeFloatSubFormatGuid))
                return WAVE_FORMAT_IEEE_FLOAT;

            failureReason = "Unsupported WAVE_FORMAT_EXTENSIBLE subformat, " +
                sampleRate + "hz " + channels + "ch " + bitsPerSample +
                "bit, blockAlign " + blockAlign + ".";
            return 0;
        }

        static string FormatCodecTag(ushort formatTag)
        {
            string name;
            switch (formatTag)
            {
                case WAVE_FORMAT_MS_ADPCM:
                    name = "Microsoft ADPCM";
                    break;
                case 0x0011:
                    name = "IMA ADPCM";
                    break;
                case 0x0050:
                    name = "MPEG";
                    break;
                case 0x0055:
                    name = "MP3";
                    break;
                default:
                    name = "unknown";
                    break;
            }

            return "0x" + formatTag.ToString("x4") + " " + name;
        }

        static bool IsSupportedBitsPerSample(
            ushort formatTag,
            ushort bitsPerSample)
        {
            if (formatTag == WAVE_FORMAT_IEEE_FLOAT)
                return bitsPerSample == 32;

            return bitsPerSample == 8 ||
                bitsPerSample == 16 ||
                bitsPerSample == 24 ||
                bitsPerSample == 32;
        }

        static float[] DecodeToMonoFloat(
            byte[] samples,
            int sourceFrameCount,
            ushort channels,
            ushort blockAlign,
            int bytesPerSample,
            ushort bitsPerSample,
            ushort formatTag)
        {
            var mono = new float[sourceFrameCount];

            for (var frame = 0; frame < sourceFrameCount; frame++)
            {
                var frameOffset = frame * blockAlign;
                double mixed = 0.0;

                for (var channel = 0; channel < channels; channel++)
                {
                    var sampleOffset = frameOffset + channel * bytesPerSample;
                    mixed += DecodeSample(
                        samples,
                        sampleOffset,
                        bitsPerSample,
                        formatTag);
                }

                mono[frame] = (float)(mixed / channels);
            }

            return mono;
        }

        static double DecodeSample(
            byte[] samples,
            int offset,
            ushort bitsPerSample,
            ushort formatTag)
        {
            if (formatTag == WAVE_FORMAT_IEEE_FLOAT)
            {
                float value = BitConverter.ToSingle(samples, offset);

                if (float.IsNaN(value) || float.IsInfinity(value))
                    return 0.0;

                return ClampUnit(value);
            }

            switch (bitsPerSample)
            {
                case 8:
                    return ((int)samples[offset] - 128) / 128.0;

                case 16:
                    return ReadInt16LittleEndian(samples, offset) / 32768.0;

                case 24:
                    return ReadInt24LittleEndian(samples, offset) / 8388608.0;

                case 32:
                    return ReadInt32LittleEndian(samples, offset) / 2147483648.0;

                default:
                    return 0.0;
            }
        }

        static byte[] ResampleMonoFloatToPcm16(
            float[] source,
            uint sourceSampleRate,
            bool allowTrustedLocalPayload)
        {
            if (source == null || source.Length == 0)
                throw new InvalidOperationException("PCM payload has no sample frames.");

            var outputFrameCount = ScaleFrameCount(source.Length, sourceSampleRate);
            var outputByteCount = (long)outputFrameCount * REQUIRED_MONO_BLOCK_ALIGN;

            if (outputByteCount > GetMaximumPcmBytes(allowTrustedLocalPayload))
            {
                throw new InvalidOperationException(allowTrustedLocalPayload
                    ? "Normalized trusted local PCM exceeds safety size limit."
                    : "Normalized PCM exceeds the 32 MiB limit.");
            }

            var output = new byte[(int)outputByteCount];
            var outputOffset = 0;

            if (sourceSampleRate == REQUIRED_SAMPLE_RATE && outputFrameCount == source.Length)
            {
                for (var i = 0; i < source.Length; i++)
                    WritePcm16(output, ref outputOffset, FloatToPcm16(source[i]));

                return output;
            }

            for (var outputFrame = 0; outputFrame < outputFrameCount; outputFrame++)
            {
                double sourcePosition = outputFrame * (double)sourceSampleRate /
                    REQUIRED_SAMPLE_RATE;
                var sourceIndex = (int)sourcePosition;

                if (sourceIndex >= source.Length - 1)
                {
                    WritePcm16(output, ref outputOffset, FloatToPcm16(source[source.Length - 1]));
                    continue;
                }

                var fraction = sourcePosition - sourceIndex;
                double sample = source[sourceIndex] * (1.0 - fraction) +
                    source[sourceIndex + 1] * fraction;

                WritePcm16(output, ref outputOffset, FloatToPcm16(sample));
            }

            return output;
        }

        static int GetMaximumPcmBytes(bool allowTrustedLocalPayload)
        {
            return allowTrustedLocalPayload
                ? MAXIMUM_TRUSTED_LOCAL_PCM_BYTES
                : MAXIMUM_PCM_BYTES;
        }

        static int ScaleFrameCount(
            int sourceFrames,
            uint sourceSampleRate)
        {
            if (sourceFrames < 0 || sourceSampleRate == 0)
                throw new InvalidOperationException("The WAV frame count is invalid.");

            long numerator = (long)sourceFrames * REQUIRED_SAMPLE_RATE;
            long frameCount = (numerator + sourceSampleRate - 1L) / sourceSampleRate;

            if (frameCount > int.MaxValue)
                throw new InvalidOperationException("The resampled PCM frame count is too large.");

            return (int)frameCount;
        }

        static void WritePcm16(
            byte[] output,
            ref int offset,
            short sample)
        {
            output[offset++] = (byte)(sample & 0xff);
            output[offset++] = (byte)((sample >> 8) & 0xff);
        }

        static short FloatToPcm16(double value)
        {
            value = ClampUnit(value);

            if (value >= 1.0)
                return 32767;

            if (value <= -1.0)
                return -32768;

            double scaled = value * 32768.0;
            int rounded = scaled >= 0.0
                ? (int)(scaled + 0.5)
                : (int)(scaled - 0.5);

            if (rounded > 32767)
                rounded = 32767;
            else if (rounded < -32768)
                rounded = -32768;

            return (short)rounded;
        }

        static double ClampUnit(double value)
        {
            if (value > 1.0)
                return 1.0;

            if (value < -1.0)
                return -1.0;

            return value;
        }

        static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static short ReadInt16LittleEndian(byte[] bytes, int offset)
        {
            return unchecked((short)(bytes[offset] | (bytes[offset + 1] << 8)));
        }

        static int ReadInt24LittleEndian(byte[] bytes, int offset)
        {
            var value = bytes[offset] |
                (bytes[offset + 1] << 8) |
                (bytes[offset + 2] << 16);

            if ((value & 0x00800000) != 0)
                value |= unchecked((int)0xff000000);

            return value;
        }

        static int ReadInt32LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset] |
                (bytes[offset + 1] << 8) |
                (bytes[offset + 2] << 16) |
                (bytes[offset + 3] << 24);
        }

        static bool GuidEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        static string ReadFourCc(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
        }
    }
}
