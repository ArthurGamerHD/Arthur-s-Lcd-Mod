#if EXPERIMENTAL
using LcdMod.Common.Compression;
using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    /// <summary>
    /// Reads WMAv2 fields most-significant bit first. A reader may address an
    /// arbitrary bit range, which is needed for packet reservoirs.
    /// </summary>
    internal sealed class MsbBitReader : IHuffmanBitReader
    {
        byte[] _buffer;
        int _startBit;
        int _bitLength;
        int _positionBits;

        public int PositionBits => _positionBits;

        public int LengthBits => _bitLength;

        public int BitsRemaining => _bitLength - _positionBits;

        public void Reset(byte[] buffer, int byteOffset, int byteCount)
        {
            if (byteOffset < 0 || byteCount < 0)
                throw new InvalidDataException("The WMAv2 byte range is invalid.");

            ResetBits(buffer, byteOffset * 8, byteCount * 8);
        }

        public void ResetBits(byte[] buffer, int startBit, int bitLength)
        {
            if (buffer == null)
                throw new InvalidDataException("Missing WMAv2 bitstream buffer.");

            if (startBit < 0 || bitLength < 0)
                throw new InvalidDataException("The WMAv2 bit range is invalid.");

            long endBit = (long)startBit + bitLength;
            long bufferBits = (long)buffer.Length * 8L;

            if (endBit > bufferBits)
                throw new InvalidDataException("The WMAv2 bit range exceeds its buffer.");

            _buffer = buffer;
            _startBit = startBit;
            _bitLength = bitLength;
            _positionBits = 0;
        }

        public uint ReadBits(int count)
        {
            if (count < 0 || count > 32)
                throw new InvalidDataException(
                    "A WMAv2 bit read must contain between 0 and 32 bits.");

            if (count > BitsRemaining)
                throw new InvalidDataException(
                    "Unexpected end of the WMAv2 bitstream.");

            uint value = 0;
            int remaining = count;

            while (remaining > 0)
            {
                int absoluteBit = _startBit + _positionBits;
                int byteIndex = absoluteBit >> 3;
                int bitInByte = absoluteBit & 7;
                int availableInByte = 8 - bitInByte;
                int take = remaining < availableInByte
                    ? remaining
                    : availableInByte;
                int shift = availableInByte - take;
                int mask = (1 << take) - 1;
                int part = (_buffer[byteIndex] >> shift) & mask;

                value = (value << take) | (uint)part;
                _positionBits += take;
                remaining -= take;
            }

            return value;
        }

        public bool ReadBit()
        {
            return ReadBits(1) != 0;
        }

        public void SkipBits(int count)
        {
            if (count < 0 || count > BitsRemaining)
                throw new InvalidDataException(
                    "Cannot skip beyond the WMAv2 bitstream.");

            _positionBits += count;
        }

        public void SetPositionBits(int positionBits)
        {
            if (positionBits < 0 || positionBits > _bitLength)
                throw new InvalidDataException(
                    "The WMAv2 bit position is outside the current range.");

            _positionBits = positionBits;
        }
    }
}
#endif
