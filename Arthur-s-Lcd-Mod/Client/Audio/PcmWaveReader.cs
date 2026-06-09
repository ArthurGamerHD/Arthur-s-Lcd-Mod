#if EXPERIMENTAL
using System;
using System.IO;
using System.Text;

namespace LcdMod.Client.Audio
{
    internal static class PcmWaveReader
    {
        const ushort WaveFormatPcm = 1;
        const ushort RequiredMonoChannels = 1;
        const ushort SupportedStereoChannels = 2;
        const uint RequiredSampleRate = 24000;
        const ushort RequiredBitsPerSample = 16;
        const ushort RequiredMonoBlockAlign = 2;
        const ushort RequiredStereoBlockAlign = 4;
        const int MaximumPcmBytes = 32 * 1024 * 1024;

        public static bool TryRead(BinaryReader reader, out PcmWaveData result, out string failureReason)
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
                    }
                    else if (chunkId == "data")
                    {
                        if (chunkSize == 0)
                        {
                            failureReason = "WAV data chunk is empty.";
                            return false;
                        }

                        if (chunkSize > MaximumPcmBytes)
                        {
                            failureReason = "PCM payload exceeds proof-of-concept size limit.";
                            return false;
                        }

                        samples = reader.ReadBytes((int)chunkSize);

                        if (samples.Length != chunkSize)
                        {
                            failureReason = "Could not read full PCM data chunk.";
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

                if (formatTag != WaveFormatPcm)
                {
                    failureReason = "Only uncompressed PCM WAV is supported.";
                    return false;
                }

                if (channels != RequiredMonoChannels && channels != SupportedStereoChannels)
                {
                    failureReason = "Expected mono or stereo WAV.";
                    return false;
                }

                if (sampleRate != RequiredSampleRate)
                {
                    failureReason = "Expected 24000 Hz WAV.";
                    return false;
                }

                if (bitsPerSample != RequiredBitsPerSample)
                {
                    failureReason = "Expected 16-bit WAV.";
                    return false;
                }

                var requiredBlockAlign = channels == SupportedStereoChannels
                    ? RequiredStereoBlockAlign
                    : RequiredMonoBlockAlign;

                if (blockAlign != requiredBlockAlign)
                {
                    failureReason = channels == SupportedStereoChannels
                        ? "Expected 4-byte stereo PCM block alignment."
                        : "Expected 2-byte mono PCM block alignment.";
                    return false;
                }

                if (samples.Length % blockAlign != 0)
                {
                    failureReason = "PCM payload is not sample-aligned.";
                    return false;
                }

                var sourceChannels = channels;
                var wasDownmixedToMono = false;

                if (channels == SupportedStereoChannels)
                {
                    samples = DownmixStereo16BitToMono(samples);
                    channels = RequiredMonoChannels;
                    blockAlign = RequiredMonoBlockAlign;
                    wasDownmixedToMono = true;
                }

                result = new PcmWaveData
                {
                    Samples = samples,
                    Channels = channels,
                    SourceChannels = sourceChannels,
                    SampleRate = sampleRate,
                    BitsPerSample = bitsPerSample,
                    BlockAlign = blockAlign,
                    WasDownmixedToMono = wasDownmixedToMono
                };

                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        static string ReadFourCc(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
        }

        static byte[] DownmixStereo16BitToMono(byte[] stereoSamples)
        {
            var monoSamples = new byte[stereoSamples.Length / 2];

            for (var source = 0; source < stereoSamples.Length; source += RequiredStereoBlockAlign)
            {
                var left = (short)(stereoSamples[source] | (stereoSamples[source + 1] << 8));
                var right = (short)(stereoSamples[source + 2] | (stereoSamples[source + 3] << 8));
                var mixed = (short)((left + right) / 2);
                var target = source / 2;

                monoSamples[target] = (byte)(mixed & 0xff);
                monoSamples[target + 1] = (byte)((mixed >> 8) & 0xff);
            }

            return monoSamples;
        }
    }
}
#endif
