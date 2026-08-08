// ReSharper disable RedundantUsingDirective
using System;
using LcdMod.Common.Compression;
using ArgumentOutOfRangeException = LcdMod.Common.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Common.Zip
{
    /// <summary>
    /// Managed zlib DEFLATE decoder.
    /// Supports stored, fixed-Huffman, and dynamic-Huffman blocks.
    /// </summary>
    public static class Zlib
    {
        static readonly int[] LengthBase =
        {
            3, 4, 5, 6, 7, 8, 9, 10,
            11, 13, 15, 17,
            19, 23, 27, 31,
            35, 43, 51, 59,
            67, 83, 99, 115,
            131, 163, 195, 227,
            258
        };

        static readonly int[] LengthExtraBits =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1,
            2, 2, 2, 2,
            3, 3, 3, 3,
            4, 4, 4, 4,
            5, 5, 5, 5,
            0
        };

        static readonly int[] DistanceBase =
        {
            1, 2, 3, 4,
            5, 7,
            9, 13,
            17, 25,
            33, 49,
            65, 97,
            129, 193,
            257, 385,
            513, 769,
            1025, 1537,
            2049, 3073,
            4097, 6145,
            8193, 12289,
            16385, 24577
        };

        static readonly int[] DistanceExtraBits =
        {
            0, 0, 0, 0,
            1, 1,
            2, 2,
            3, 3,
            4, 4,
            5, 5,
            6, 6,
            7, 7,
            8, 8,
            9, 9,
            10, 10,
            11, 11,
            12, 12,
            13, 13
        };

        static readonly int[] CodeLengthOrder =
        {
            16, 17, 18, 0, 8, 7, 9, 6, 10,
            5, 11, 4, 12, 3, 13, 2, 14, 1, 15
        };

        static readonly HuffmanTree<int> FixedLiteralLengthTree =
            CreateFixedLiteralLengthTree();

        static readonly HuffmanTree<int> FixedDistanceTree =
            CreateFixedDistanceTree();

        /// <summary>
        /// Decodes a gzip member containing a DEFLATE stream.
        /// Supports the standard optional gzip header fields and validates
        /// both CRC-32 and ISIZE from the trailer.
        /// </summary>
        public static byte[] InflateGzip(byte[] gzipData)
        {
            if (gzipData == null)
                throw new ArgumentNullException(nameof(gzipData));

            if (gzipData.Length < 18)
                throw new InvalidDataException("Truncated gzip stream.");

            int position = 0;

            if (gzipData[position++] != 0x1F || gzipData[position++] != 0x8B)
                throw new InvalidDataException("Invalid gzip signature.");

            int compressionMethod = gzipData[position++];
            if (compressionMethod != 8)
                throw new NotSupportedException("The gzip stream does not use DEFLATE compression.");

            int flags = gzipData[position++];
            if ((flags & 0xE0) != 0)
                throw new InvalidDataException("Invalid gzip reserved flags.");

            // MTIME(4), XFL(1), OS(1)
            position += 6;

            const int FlagHeaderCrc = 0x02;
            const int FlagExtra = 0x04;
            const int FlagName = 0x08;
            const int FlagComment = 0x10;

            if ((flags & FlagExtra) != 0)
            {
                EnsureGzipBytesAvailable(gzipData, position, 2, 8);
                int extraLength = gzipData[position] | (gzipData[position + 1] << 8);
                position += 2;
                EnsureGzipBytesAvailable(gzipData, position, extraLength, 8);
                position += extraLength;
            }

            if ((flags & FlagName) != 0)
                SkipGzipZeroTerminatedField(gzipData, ref position);

            if ((flags & FlagComment) != 0)
                SkipGzipZeroTerminatedField(gzipData, ref position);

            if ((flags & FlagHeaderCrc) != 0)
            {
                EnsureGzipBytesAvailable(gzipData, position, 2, 8);
                position += 2;
            }

            int trailerOffset = gzipData.Length - 8;
            if (position > trailerOffset)
                throw new InvalidDataException("Truncated gzip DEFLATE payload.");

            uint expectedCrc = ReadUInt32LittleEndian(gzipData, trailerOffset);
            uint expectedSize = ReadUInt32LittleEndian(gzipData, trailerOffset + 4);

            if (expectedSize > int.MaxValue)
                throw new NotSupportedException("gzip output larger than 2 GiB is not supported.");

            byte[] output = InflateRawDeflate(
                gzipData,
                position,
                trailerOffset - position,
                (int)expectedSize);

            uint actualCrc = ComputeCrc32(output, output.Length);
            if (expectedCrc != actualCrc)
                throw new InvalidDataException("gzip CRC-32 check failed.");

            return output;
        }

        /// <summary>
        /// Encodes data as a valid gzip member using DEFLATE stored blocks.
        /// This intentionally performs no compression. It exists for
        /// whitelist-safe environments where System.IO.Compression is not
        /// available and correctness is more important than output size.
        /// </summary>
        public static byte[] DeflateGzipStored(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // Worst case: one five-byte stored-block header per 65535 bytes,
            // plus the ten-byte gzip header and eight-byte trailer.
            int blockCount = Math.Max(1, (data.Length + 65534) / 65535);
            int capacity = checked(data.Length + blockCount * 5 + 18);

            byte[] output = new byte[capacity];
            int position = 0;

            // ID1, ID2, CM=DEFLATE, FLG=0
            output[position++] = 0x1F;
            output[position++] = 0x8B;
            output[position++] = 8;
            output[position++] = 0;

            // MTIME = 0
            output[position++] = 0;
            output[position++] = 0;
            output[position++] = 0;
            output[position++] = 0;

            // XFL = 0, OS = unknown
            output[position++] = 0;
            output[position++] = 255;

            int inputPosition = 0;

            if (data.Length == 0)
            {
                WriteStoredDeflateBlock(output, ref position, data, 0, 0, true);
            }
            else
            {
                while (inputPosition < data.Length)
                {
                    int count = Math.Min(65535, data.Length - inputPosition);
                    bool final = inputPosition + count == data.Length;

                    WriteStoredDeflateBlock(
                        output,
                        ref position,
                        data,
                        inputPosition,
                        count,
                        final);

                    inputPosition += count;
                }
            }

            uint crc = ComputeCrc32(data, data.Length);
            WriteUInt32LittleEndian(output, ref position, crc);
            WriteUInt32LittleEndian(output, ref position, unchecked((uint)data.Length));

            if (position != output.Length)
                throw new InvalidDataException("Internal gzip encoder length mismatch.");

            return output;
        }

        static void WriteStoredDeflateBlock(
            byte[] output,
            ref int position,
            byte[] input,
            int inputOffset,
            int count,
            bool final)
        {
            // At a byte boundary, BFINAL occupies bit 0 and BTYPE=00 the next
            // two bits. Remaining bits in this byte are padding to the stored
            // block byte boundary.
            output[position++] = final ? (byte)0x01 : (byte)0x00;

            ushort length = (ushort)count;
            ushort complement = (ushort)~length;

            output[position++] = (byte)length;
            output[position++] = (byte)(length >> 8);
            output[position++] = (byte)complement;
            output[position++] = (byte)(complement >> 8);

            if (count > 0)
            {
                Buffer.BlockCopy(input, inputOffset, output, position, count);
                position += count;
            }
        }

        static void EnsureGzipBytesAvailable(
            byte[] data,
            int position,
            int count,
            int requiredTrailerBytes)
        {
            if (count < 0 || position < 0 ||
                position > data.Length - requiredTrailerBytes - count)
            {
                throw new InvalidDataException("Truncated gzip header.");
            }
        }

        static void SkipGzipZeroTerminatedField(byte[] data, ref int position)
        {
            int trailerOffset = data.Length - 8;

            while (position < trailerOffset)
            {
                if (data[position++] == 0)
                    return;
            }

            throw new InvalidDataException("Unterminated gzip header field.");
        }

        public static byte[] Inflate(byte[] zlibData, int expectedOutputSize)
        {
            if (zlibData == null)
                throw new ArgumentNullException(nameof(zlibData));

            if (expectedOutputSize < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedOutputSize));

            if (zlibData.Length < 6)
                throw new InvalidDataException("Truncated zlib stream.");

            int cmf = zlibData[0];
            int flg = zlibData[1];

            if ((cmf & 0x0F) != 8)
                throw new NotSupportedException("The zlib stream does not use DEFLATE compression.");

            if ((cmf >> 4) > 7)
                throw new InvalidDataException("Invalid zlib window size.");

            if ((((cmf << 8) | flg) % 31) != 0)
                throw new InvalidDataException("Invalid zlib header check bits.");

            if ((flg & 0x20) != 0)
                throw new NotSupportedException("Preset zlib dictionaries are not supported.");

            byte[] output = InflateRawDeflate(
                zlibData,
                2,
                zlibData.Length - 6,
                expectedOutputSize);

            uint expectedAdler = ReadUInt32BigEndian(zlibData, zlibData.Length - 4);
            uint actualAdler = ComputeAdler32(output, output.Length);
            if (expectedAdler != actualAdler)
                throw new InvalidDataException("zlib Adler-32 check failed.");

            return output;
        }

        public static byte[] InflateRawDeflate(
            byte[] deflateData,
            int offset,
            int count,
            int expectedOutputSize)
        {
            if (deflateData == null)
                throw new ArgumentNullException(nameof(deflateData));

            if (offset < 0 || count < 0 || offset > deflateData.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (expectedOutputSize < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedOutputSize));

            BitReader reader = new BitReader(deflateData, offset, offset + count);
            OutputBuffer output = new OutputBuffer(expectedOutputSize);

            bool finalBlock;
            do
            {
                finalBlock = reader.ReadBits(1) != 0;
                int blockType = reader.ReadBits(2);

                switch (blockType)
                {
                    case 0:
                        DecodeStoredBlock(reader, output);
                        break;

                    case 1:
                        DecodeCompressedBlock(
                            reader,
                            output,
                            FixedLiteralLengthTree,
                            FixedDistanceTree);
                        break;

                    case 2:
                        HuffmanTree<int> literalLengthTree;
                        HuffmanTree<int> distanceTree;
                        ReadDynamicTrees(reader, out literalLengthTree, out distanceTree);
                        DecodeCompressedBlock(reader, output, literalLengthTree, distanceTree);
                        break;

                    default:
                        throw new InvalidDataException("Reserved DEFLATE block type.");
                }
            }
            while (!finalBlock);

            if (output.Position != expectedOutputSize)
            {
                throw new InvalidDataException(
                    "DEFLATE produced " + output.Position +
                    " bytes; expected " + expectedOutputSize + ".");
            }

            return output.Buffer;
        }

        static void DecodeStoredBlock(BitReader reader, OutputBuffer output)
        {
            reader.AlignToByte();

            int length = reader.ReadUInt16LittleEndianAligned();
            int complement = reader.ReadUInt16LittleEndianAligned();

            if ((length ^ 0xFFFF) != complement)
                throw new InvalidDataException("Invalid uncompressed DEFLATE block length.");

            output.WriteAlignedBytesFrom(reader, length);
        }

        static void DecodeCompressedBlock(
            BitReader reader,
            OutputBuffer output,
            HuffmanTree<int> literalLengthTree,
            HuffmanTree<int> distanceTree)
        {
            while (true)
            {
                int symbol = literalLengthTree.Decode(reader);

                if (symbol < 256)
                {
                    output.WriteByte((byte)symbol);
                    continue;
                }

                if (symbol == 256)
                    return;

                if (symbol > 285)
                    throw new InvalidDataException("Invalid DEFLATE length symbol: " + symbol);

                int lengthIndex = symbol - 257;
                int length = LengthBase[lengthIndex] +
                             reader.ReadBits(LengthExtraBits[lengthIndex]);

                if (distanceTree.IsEmpty)
                    throw new InvalidDataException("DEFLATE back-reference used without a distance tree.");

                int distanceSymbol = distanceTree.Decode(reader);
                if (distanceSymbol < 0 || distanceSymbol >= 30)
                    throw new InvalidDataException("Invalid DEFLATE distance symbol: " + distanceSymbol);

                int distance = DistanceBase[distanceSymbol] +
                               reader.ReadBits(DistanceExtraBits[distanceSymbol]);

                output.CopyFromHistory(distance, length);
            }
        }

        static void ReadDynamicTrees(
            BitReader reader,
            out HuffmanTree<int> literalLengthTree,
            out HuffmanTree<int> distanceTree)
        {
            int literalLengthCount = reader.ReadBits(5) + 257;
            int distanceCount = reader.ReadBits(5) + 1;
            int codeLengthCount = reader.ReadBits(4) + 4;

            if (literalLengthCount > 286)
                throw new InvalidDataException("Invalid DEFLATE HLIT value.");

            int[] codeLengthLengths = new int[19];
            for (int i = 0; i < codeLengthCount; i++)
                codeLengthLengths[CodeLengthOrder[i]] = reader.ReadBits(3);

            HuffmanTree<int> codeLengthTree =
                HuffmanTree.CreateCanonicalIndexed(codeLengthLengths, 15);
            int totalCount = literalLengthCount + distanceCount;
            int[] lengths = new int[totalCount];
            int index = 0;

            while (index < totalCount)
            {
                int symbol = codeLengthTree.Decode(reader);

                if (symbol <= 15)
                {
                    lengths[index++] = symbol;
                }
                else if (symbol == 16)
                {
                    if (index == 0)
                        throw new InvalidDataException("DEFLATE repeat code has no previous length.");

                    int repeat = reader.ReadBits(2) + 3;
                    EnsureRepeatFits(index, repeat, totalCount);
                    int previous = lengths[index - 1];

                    for (int i = 0; i < repeat; i++)
                        lengths[index++] = previous;
                }
                else if (symbol == 17)
                {
                    int repeat = reader.ReadBits(3) + 3;
                    EnsureRepeatFits(index, repeat, totalCount);
                    index += repeat;
                }
                else if (symbol == 18)
                {
                    int repeat = reader.ReadBits(7) + 11;
                    EnsureRepeatFits(index, repeat, totalCount);
                    index += repeat;
                }
                else
                {
                    throw new InvalidDataException("Invalid DEFLATE code-length symbol.");
                }
            }

            int[] literalLengths = new int[literalLengthCount];
            int[] distanceLengths = new int[distanceCount];
            Array.Copy(lengths, 0, literalLengths, 0, literalLengthCount);
            Array.Copy(lengths, literalLengthCount, distanceLengths, 0, distanceCount);

            if (literalLengths[256] == 0)
                throw new InvalidDataException("DEFLATE literal/length tree has no end-of-block code.");

            literalLengthTree =
                HuffmanTree.CreateCanonicalIndexed(literalLengths, 15);
            distanceTree =
                HuffmanTree.CreateCanonicalIndexed(distanceLengths, 15, true);
        }

        static void EnsureRepeatFits(int index, int repeat, int total)
        {
            if (repeat < 0 || index + repeat > total)
                throw new InvalidDataException("DEFLATE code-length repeat exceeds the table.");
        }

        static HuffmanTree<int> CreateFixedLiteralLengthTree()
        {
            int[] lengths = new int[288];

            for (int i = 0; i <= 143; i++) lengths[i] = 8;
            for (int i = 144; i <= 255; i++) lengths[i] = 9;
            for (int i = 256; i <= 279; i++) lengths[i] = 7;
            for (int i = 280; i <= 287; i++) lengths[i] = 8;

            return HuffmanTree.CreateCanonicalIndexed(lengths, 15);
        }

        static HuffmanTree<int> CreateFixedDistanceTree()
        {
            int[] lengths = new int[32];
            for (int i = 0; i < lengths.Length; i++) lengths[i] = 5;
            return HuffmanTree.CreateCanonicalIndexed(lengths, 15);
        }

        static uint ComputeAdler32(byte[] data, int count)
        {
            const uint modulus = 65521;
            uint a = 1;
            uint b = 0;

            for (int i = 0; i < count; i++)
            {
                a += data[i];
                if (a >= modulus) a -= modulus;

                b += a;
                if (b >= modulus) b %= modulus;
            }

            return (b << 16) | a;
        }

        static uint ComputeCrc32(byte[] data, int count)
        {
            uint crc = 0xFFFFFFFFu;

            for (int i = 0; i < count; i++)
            {
                crc ^= data[i];

                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (0xEDB88320u & mask);
                }
            }

            return ~crc;
        }

        static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return data[offset] |
                   ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) |
                   ((uint)data[offset + 3] << 24);
        }

        static void WriteUInt32LittleEndian(byte[] data, ref int offset, uint value)
        {
            data[offset++] = (byte)value;
            data[offset++] = (byte)(value >> 8);
            data[offset++] = (byte)(value >> 16);
            data[offset++] = (byte)(value >> 24);
        }

        static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   data[offset + 3];
        }

        sealed class BitReader : IHuffmanBitReader
        {
            readonly byte[] _data;
            readonly int _end;
            int _position;
            uint _bits;
            int _bitCount;

            public BitReader(byte[] data, int start, int end)
            {
                if (data == null)
                    throw new ArgumentNullException(nameof(data));

                if (start < 0 || end < start || end > data.Length)
                    throw new ArgumentOutOfRangeException(nameof(start));

                _data = data;
                _position = start;
                _end = end;
            }

            public bool ReadBit()
            {
                return ReadBits(1) != 0;
            }

            public int ReadBits(int count)
            {
                if (count < 0 || count > 24)
                    throw new ArgumentOutOfRangeException(nameof(count));

                if (count == 0)
                    return 0;

                while (_bitCount < count)
                {
                    if (_position >= _end)
                        throw new InvalidDataException("Unexpected end of DEFLATE stream.");

                    _bits |= (uint)_data[_position++] << _bitCount;
                    _bitCount += 8;
                }

                uint mask = (1u << count) - 1u;
                int value = (int)(_bits & mask);
                _bits >>= count;
                _bitCount -= count;
                return value;
            }

            public void AlignToByte()
            {
                _bits = 0;
                _bitCount = 0;
            }

            public int ReadUInt16LittleEndianAligned()
            {
                EnsureByteAligned();

                if (_position + 2 > _end)
                    throw new InvalidDataException("Unexpected end of DEFLATE stored block.");

                int value = _data[_position] | (_data[_position + 1] << 8);
                _position += 2;
                return value;
            }

            public void CopyAlignedBytes(byte[] destination, int destinationOffset, int count)
            {
                EnsureByteAligned();

                if (count < 0 || _position + count > _end)
                    throw new InvalidDataException("Unexpected end of DEFLATE stored block.");

                Buffer.BlockCopy(_data, _position, destination, destinationOffset, count);
                _position += count;
            }

            void EnsureByteAligned()
            {
                if (_bitCount != 0)
                    throw new InvalidOperationException("DEFLATE reader is not byte-aligned.");
            }
        }

        sealed class OutputBuffer
        {
            public byte[] Buffer { get; private set; }
            public int Position { get; private set; }

            public OutputBuffer(int size)
            {
                Buffer = new byte[size];
            }

            public void WriteByte(byte value)
            {
                if (Position >= Buffer.Length)
                    throw new InvalidDataException("DEFLATE output is larger than expected.");

                Buffer[Position++] = value;
            }

            public void WriteAlignedBytesFrom(BitReader reader, int count)
            {
                if (count < 0 || Position + count > Buffer.Length)
                    throw new InvalidDataException("DEFLATE output is larger than expected.");

                reader.CopyAlignedBytes(Buffer, Position, count);
                Position += count;
            }

            public void CopyFromHistory(int distance, int length)
            {
                if (distance <= 0 || distance > Position)
                    throw new InvalidDataException("Invalid DEFLATE back-reference distance.");

                if (length < 0 || Position + length > Buffer.Length)
                    throw new InvalidDataException("DEFLATE output is larger than expected.");

                for (int i = 0; i < length; i++)
                {
                    int source = Position - distance;
                    Buffer[Position] = Buffer[source];
                    Position++;
                }
            }
        }
    }
}
