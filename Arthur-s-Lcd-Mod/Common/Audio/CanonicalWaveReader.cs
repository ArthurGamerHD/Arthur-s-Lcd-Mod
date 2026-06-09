#if EXPERIMENTAL
using System;
using System.Text;

namespace LcdMod.Common.Audio
{
    internal sealed class CanonicalWavePayload
    {
        public byte[] PcmBytes;
        public int RuntimeByteLength;
        public int PcmByteLength;
        public long DurationTicks;
    }

    internal static class CanonicalWaveReader
    {
        public const int MaxBroadcastWaveBytes = 32 * 1024 * 1024;
        const ushort WaveFormatPcm = 1;
        const ushort RequiredChannels = 1;
        const uint RequiredSampleRate = 24000;
        const ushort RequiredBitsPerSample = 16;
        const ushort RequiredBlockAlign = 2;

        public static bool TryRead(byte[] runtimeWaveBytes, out CanonicalWavePayload result, out string failureReason)
        {
            result = null;
            failureReason = string.Empty;

            if (runtimeWaveBytes == null || runtimeWaveBytes.Length < 12)
            {
                failureReason = "File is too small to be a RIFF/WAVE file.";
                return false;
            }

            if (runtimeWaveBytes.Length > MaxBroadcastWaveBytes)
            {
                failureReason = "Runtime WAV exceeds broadcast size limit.";
                return false;
            }

            try
            {
                var reader = new ByteReader(runtimeWaveBytes);
                var riff = reader.ReadFourCc();
                reader.ReadUInt32();
                var wave = reader.ReadFourCc();

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
                byte[] pcmBytes = null;

                while (reader.Position + 8 <= reader.Length)
                {
                    var chunkId = reader.ReadFourCc();
                    var chunkSize = reader.ReadUInt32();
                    var chunkStart = reader.Position;
                    var chunkEnd = chunkStart + (long)chunkSize;

                    if (chunkEnd > reader.Length)
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

                        pcmBytes = reader.ReadBytes((int)chunkSize);
                    }

                    reader.Position = (int)chunkEnd;
                    if ((chunkSize & 1) != 0 && reader.Position < reader.Length)
                        reader.Position++;
                }

                if (!hasFormat)
                {
                    failureReason = "Missing fmt chunk.";
                    return false;
                }

                if (pcmBytes == null)
                {
                    failureReason = "Missing data chunk.";
                    return false;
                }

                if (formatTag != WaveFormatPcm)
                {
                    failureReason = "Expected PCM WAV.";
                    return false;
                }

                if (channels != RequiredChannels)
                {
                    failureReason = "Expected mono WAV.";
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

                if (blockAlign != RequiredBlockAlign)
                {
                    failureReason = "Expected 2-byte PCM block alignment.";
                    return false;
                }

                if (pcmBytes.Length % RequiredBlockAlign != 0)
                {
                    failureReason = "PCM payload is not sample-aligned.";
                    return false;
                }

                result = new CanonicalWavePayload
                {
                    PcmBytes = pcmBytes,
                    RuntimeByteLength = runtimeWaveBytes.Length,
                    PcmByteLength = pcmBytes.Length,
                    DurationTicks = TimeSpan.FromSeconds(pcmBytes.Length / 48000.0).Ticks
                };

                return true;
            }
            catch (Exception error)
            {
                failureReason = error.Message;
                return false;
            }
        }

        sealed class ByteReader
        {
            readonly byte[] _bytes;
            public int Position;
            public int Length { get { return _bytes.Length; } }

            public ByteReader(byte[] bytes)
            {
                _bytes = bytes;
            }

            public ushort ReadUInt16()
            {
                Ensure(2);
                var value = (ushort)(_bytes[Position] | (_bytes[Position + 1] << 8));
                Position += 2;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                var value = (uint)(_bytes[Position] |
                                   (_bytes[Position + 1] << 8) |
                                   (_bytes[Position + 2] << 16) |
                                   (_bytes[Position + 3] << 24));
                Position += 4;
                return value;
            }

            public byte[] ReadBytes(int count)
            {
                Ensure(count);
                var result = new byte[count];
                Array.Copy(_bytes, Position, result, 0, count);
                Position += count;
                return result;
            }

            public string ReadFourCc()
            {
                Ensure(4);
                var result = Encoding.ASCII.GetString(_bytes, Position, 4);
                Position += 4;
                return result;
            }

            void Ensure(int count)
            {
                if (count < 0 || Position + count > _bytes.Length)
                    throw new InvalidOperationException("Unexpected end of WAV file.");
            }
        }
    }
}
#endif
