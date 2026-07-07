using System;
using System.IO;
using EndOfStreamException = LcdMod.Common.EndOfStreamException;
using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma
{
public enum XwmaProfileKind
    {
        Wma2Stereo44100Hz48Kbps,
        Wma2Stereo48000Hz48Kbps
    }

    public sealed class XwmaWaveFormat
    {
        internal XwmaWaveFormat(
            ushort formatTag,
            ushort channels,
            uint sampleRate,
            uint averageBytesPerSecond,
            ushort blockAlign,
            ushort bitsPerSample,
            ushort extraSize,
            uint formatChunkSize)
        {
            FormatTag = formatTag;
            Channels = channels;
            SampleRate = sampleRate;
            AverageBytesPerSecond = averageBytesPerSecond;
            BlockAlign = blockAlign;
            BitsPerSample = bitsPerSample;
            ExtraSize = extraSize;
            FormatChunkSize = formatChunkSize;
        }

        public ushort FormatTag { get; private set; }
        public ushort Channels { get; private set; }
        public uint SampleRate { get; private set; }
        public uint AverageBytesPerSecond { get; private set; }
        public ushort BlockAlign { get; private set; }
        public ushort BitsPerSample { get; private set; }
        public ushort ExtraSize { get; private set; }
        public uint FormatChunkSize { get; private set; }
    }

    public sealed class XwmaFileInfo
    {
        internal XwmaFileInfo(
            XwmaProfileKind profile,
            XwmaWaveFormat format,
            uint riffSize,
            uint[] decodedPacketCumulativeBytes,
            long dataOffset,
            uint dataLength)
        {
            Profile = profile;
            Format = format;
            RiffSize = riffSize;
            DecodedPacketCumulativeBytes = decodedPacketCumulativeBytes;
            DataOffset = dataOffset;
            DataLength = dataLength;
        }

        public XwmaProfileKind Profile { get; private set; }
        public XwmaWaveFormat Format { get; private set; }
        public uint RiffSize { get; private set; }
        public uint[] DecodedPacketCumulativeBytes { get; private set; }
        public long DataOffset { get; private set; }
        public uint DataLength { get; private set; }

        public int PacketCount => DecodedPacketCumulativeBytes.Length;

        public uint DeclaredDecodedPcmBytes
        {
            get
            {
                if (DecodedPacketCumulativeBytes.Length == 0)
                    return 0;

                return DecodedPacketCumulativeBytes[
                    DecodedPacketCumulativeBytes.Length - 1];
            }
        }

        // The restricted input profile is stereo, 16-bit PCM after decode:
        // two channels * two bytes = four bytes per sample frame.
        public long DeclaredSourceSampleFrames => DeclaredDecodedPcmBytes / 4L;

        public long Estimate24000HzStereoPcmBytes()
        {
            long sourceFrames = DeclaredSourceSampleFrames;
            long outputFrames = checked(
                (sourceFrames * 24000L + Format.SampleRate - 1L) /
                Format.SampleRate);

            return checked(outputFrames * 4L);
        }
    }

    public static class XwmaParser
    {
        private const ushort WMA_AUDIO2_FORMAT_TAG = 0x0161;
        private const ushort REQUIRED_INPUT_CHANNELS = 2;
        private const uint REQUIRED_AVERAGE_BYTES_PER_SECOND = 6000;
        private const ushort REQUIRED_INPUT_BITS_PER_SAMPLE = 16;
        private const uint REQUIRED_FORMAT_CHUNK_SIZE = 18;
        private const ushort REQUIRED_EXTRA_SIZE = 0;

        private const uint SAMPLE_RATE44100 = 44100;
        private const ushort PACKET_SIZE44100 = 2230;

        private const uint SAMPLE_RATE48000 = 48000;
        private const ushort PACKET_SIZE48000 = 1008;

        // Protects against allocating an unreasonable table from malformed input.
        // The supplied files use fewer than 10 KiB of dpds data.
        private const uint MAXIMUM_DPDS_CHUNK_SIZE = 16u * 1024u * 1024u;

        public static XwmaFileInfo Parse(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead)
                throw new ArgumentException("The file stream must be readable.", nameof(stream));

            if (!stream.CanSeek)
                throw new ArgumentException("The file stream must be seekable.", nameof(stream));

            long riffStart = stream.Position;

            if (stream.Length - riffStart < 12)
                throw new InvalidDataException("The file is too small to contain an XWMA RIFF header.");

            string riffMagic = ReadFourCc(stream);
            if (riffMagic != "RIFF")
            {
                throw new InvalidDataException(
                    "Invalid container magic. Expected RIFF, found " + QuoteFourCc(riffMagic) + ".");
            }

            uint riffSize = ReadUInt32LittleEndian(stream);

            string formMagic = ReadFourCc(stream);
            if (formMagic != "XWMA")
            {
                throw new InvalidDataException(
                    "Invalid RIFF form type. Expected XWMA, found " + QuoteFourCc(formMagic) + ".");
            }

            long riffEnd;
            try
            {
                riffEnd = checked(riffStart + 8L + riffSize);
            }
            catch (Exception ex)
            {
                if (ex.GetType().FullName == "System.OverflowException") // thanks keen
                    throw new InvalidDataException("The RIFF size overflows the file address space.", ex);

                throw;
            }

            if (riffEnd > stream.Length)
            {
                throw new InvalidDataException(
                    "The RIFF header declares more bytes than the file contains.");
            }

            XwmaWaveFormat format = null;
            uint[] dpds = null;
            long dataOffset = -1;
            uint dataLength = 0;

            while (stream.Position < riffEnd)
            {
                long remaining = riffEnd - stream.Position;
                if (remaining < 8)
                    throw new InvalidDataException("A truncated RIFF chunk header was found.");

                string chunkId = ReadFourCc(stream);
                uint chunkSize = ReadUInt32LittleEndian(stream);
                long chunkDataStart = stream.Position;

                long chunkDataEnd;
                long nextChunkOffset;

                try
                {
                    chunkDataEnd = checked(chunkDataStart + chunkSize);
                    nextChunkOffset = checked(chunkDataEnd + (chunkSize & 1u));
                }
                catch (Exception ex)
                {
                    if (ex.GetType().FullName == "System.OverflowException") // thanks keen
                        throw new InvalidDataException("A RIFF chunk size overflows the file address space.", ex);
                    throw;
                }

                if (nextChunkOffset > riffEnd)
                {
                    throw new InvalidDataException(
                        "Chunk " + QuoteFourCc(chunkId) + " extends beyond the RIFF boundary.");
                }

                if (chunkId == "fmt ")
                {
                    if (format != null)
                        throw new InvalidDataException("The file contains more than one fmt chunk.");

                    format = ReadFormatChunk(stream, chunkSize);
                }
                else if (chunkId == "dpds")
                {
                    if (dpds != null)
                        throw new InvalidDataException("The file contains more than one dpds chunk.");

                    dpds = ReadDpdsChunk(stream, chunkSize);
                }
                else if (chunkId == "data")
                {
                    if (dataOffset >= 0)
                        throw new InvalidDataException("The file contains more than one data chunk.");

                    dataOffset = chunkDataStart;
                    dataLength = chunkSize;
                }

                // This skips unknown chunks and avoids reading the compressed data payload.
                stream.Position = nextChunkOffset;
            }

            if (format == null)
                throw new InvalidDataException("The XWMA file has no fmt chunk.");

            if (dpds == null)
                throw new InvalidDataException("The XWMA file has no dpds chunk.");

            if (dataOffset < 0)
                throw new InvalidDataException("The XWMA file has no data chunk.");

            XwmaProfileKind profile = DecodeAndValidateProfile(format);
            ValidatePacketTable(format, dpds, dataLength);

            // Position the stream for the next stage: compressed packet decoding.
            stream.Position = dataOffset;

            return new XwmaFileInfo(
                profile,
                format,
                riffSize,
                dpds,
                dataOffset,
                dataLength);
        }

        private static XwmaWaveFormat ReadFormatChunk(Stream stream, uint chunkSize)
        {
            if (chunkSize < REQUIRED_FORMAT_CHUNK_SIZE)
                throw new InvalidDataException("The fmt chunk is smaller than WAVEFORMATEX.");

            ushort formatTag = ReadUInt16LittleEndian(stream);
            ushort channels = ReadUInt16LittleEndian(stream);
            uint sampleRate = ReadUInt32LittleEndian(stream);
            uint averageBytesPerSecond = ReadUInt32LittleEndian(stream);
            ushort blockAlign = ReadUInt16LittleEndian(stream);
            ushort bitsPerSample = ReadUInt16LittleEndian(stream);
            ushort extraSize = ReadUInt16LittleEndian(stream);

            if (18u + extraSize > chunkSize)
            {
                throw new InvalidDataException(
                    "The fmt chunk's codec-extra size exceeds the chunk boundary.");
            }

            return new XwmaWaveFormat(
                formatTag,
                channels,
                sampleRate,
                averageBytesPerSecond,
                blockAlign,
                bitsPerSample,
                extraSize,
                chunkSize);
        }

        private static uint[] ReadDpdsChunk(Stream stream, uint chunkSize)
        {
            if (chunkSize == 0)
                throw new InvalidDataException("The dpds chunk is empty.");

            if ((chunkSize & 3u) != 0)
                throw new InvalidDataException("The dpds chunk size is not divisible by four.");

            if (chunkSize > MAXIMUM_DPDS_CHUNK_SIZE)
                throw new InvalidDataException("The dpds chunk is unreasonably large.");

            int count = checked((int)(chunkSize / 4u));
            uint[] values = new uint[count];

            for (int i = 0; i < values.Length; i++)
                values[i] = ReadUInt32LittleEndian(stream);

            return values;
        }

        private static XwmaProfileKind DecodeAndValidateProfile(XwmaWaveFormat format)
        {
            if (format.FormatChunkSize != REQUIRED_FORMAT_CHUNK_SIZE)
            {
                throw new NotSupportedException(
                    "Only the 18-byte xWMA WAVEFORMATEX layout is supported.");
            }

            if (format.FormatTag != WMA_AUDIO2_FORMAT_TAG)
            {
                throw new NotSupportedException(
                    "Only WMAv2 format tag 0x0161 is supported.");
            }

            if (format.Channels != REQUIRED_INPUT_CHANNELS)
                throw new NotSupportedException("Only stereo WMAv2 input is supported.");

            if (format.AverageBytesPerSecond != REQUIRED_AVERAGE_BYTES_PER_SECOND)
            {
                throw new NotSupportedException(
                    "Only the 48 kb/s WMAv2 profile is supported.");
            }

            if (format.BitsPerSample != REQUIRED_INPUT_BITS_PER_SAMPLE)
            {
                throw new NotSupportedException(
                    "Only files declaring 16 bits per sample are supported.");
            }

            if (format.ExtraSize != REQUIRED_EXTRA_SIZE)
            {
                throw new NotSupportedException(
                    "This restricted xWMA profile requires cbSize to be zero.");
            }

            if (format.SampleRate == SAMPLE_RATE44100 &&
                format.BlockAlign == PACKET_SIZE44100)
            {
                return XwmaProfileKind.Wma2Stereo44100Hz48Kbps;
            }

            if (format.SampleRate == SAMPLE_RATE48000 &&
                format.BlockAlign == PACKET_SIZE48000)
            {
                return XwmaProfileKind.Wma2Stereo48000Hz48Kbps;
            }

            throw new NotSupportedException(
                "Unsupported sample-rate and packet-size combination: " +
                format.SampleRate + " Hz, " + format.BlockAlign + " bytes.");
        }

        private static void ValidatePacketTable(
            XwmaWaveFormat format,
            uint[] dpds,
            uint dataLength)
        {
            if (dataLength == 0)
                throw new InvalidDataException("The compressed data chunk is empty.");

            if ((dataLength % format.BlockAlign) != 0)
            {
                throw new InvalidDataException(
                    "The data chunk is not an exact number of compressed packets.");
            }

            uint packetCount = dataLength / format.BlockAlign;
            if (packetCount > int.MaxValue)
                throw new InvalidDataException("The compressed packet count is too large.");

            if (dpds.Length != (int)packetCount)
            {
                throw new InvalidDataException(
                    "The dpds entry count does not match the compressed packet count.");
            }

            uint previous = 0;
            for (int i = 0; i < dpds.Length; i++)
            {
                uint current = dpds[i];

                // A packet can decode no additional PCM frames. Some valid xWMA
                // files therefore repeat a cumulative value, including the final
                // entry, but the table must never move backwards.
                if (current < previous)
                {
                    throw new InvalidDataException(
                        "The dpds table decreases at entry " + i + ".");
                }

                // Decoded output for this restricted source profile is stereo PCM16,
                // so cumulative byte positions must be aligned to four bytes.
                if ((current & 3u) != 0)
                {
                    throw new InvalidDataException(
                        "The dpds entry at index " + i + " is not stereo-PCM16 aligned.");
                }

                previous = current;
            }
        }

        private static string ReadFourCc(Stream stream)
        {
            byte[] bytes = new byte[4];
            ReadExactly(stream, bytes, 0, bytes.Length);

            return new string(new[]
            {
                (char)bytes[0],
                (char)bytes[1],
                (char)bytes[2],
                (char)bytes[3]
            });
        }

        private static ushort ReadUInt16LittleEndian(Stream stream)
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();

            if ((b0 | b1) < 0)
                throw new EndOfStreamException("Unexpected end of file.");

            return (ushort)(b0 | (b1 << 8));
        }

        private static uint ReadUInt32LittleEndian(Stream stream)
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            int b3 = stream.ReadByte();

            if ((b0 | b1 | b2 | b3) < 0)
                throw new EndOfStreamException("Unexpected end of file.");

            return (uint)(
                b0 |
                (b1 << 8) |
                (b2 << 16) |
                (b3 << 24));
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of file.");

                offset += read;
                count -= read;
            }
        }

        private static string QuoteFourCc(string value)
        {
            return "'" + value + "'";
        }
    }
}
