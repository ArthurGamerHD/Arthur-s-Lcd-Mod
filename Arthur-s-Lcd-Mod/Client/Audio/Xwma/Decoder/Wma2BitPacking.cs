using InvalidDataException = LcdMod.Common.InvalidDataException;

namespace LcdMod.Client.Audio.Xwma.Decoder
{
    internal static class Wma2BitPacking
    {
        public static byte[] CopyBits(
            byte[] source,
            int sourceStartBit,
            int bitCount)
        {
            if (source == null)
                throw new InvalidDataException("Missing WMAv2 bit source.");

            if (sourceStartBit < 0 || bitCount < 0 ||
                (long)sourceStartBit + bitCount >
                (long)source.Length * 8L)
            {
                throw new InvalidDataException(
                    "Invalid WMAv2 bit-copy range.");
            }

            byte[] destination = new byte[(bitCount + 7) / 8];
            CopyBits(
                source,
                sourceStartBit,
                destination,
                0,
                bitCount);
            return destination;
        }

        public static byte[] Concatenate(
            byte[] first,
            int firstBitCount,
            byte[] second,
            int secondStartBit,
            int secondBitCount)
        {
            if (first == null || second == null)
                throw new InvalidDataException(
                    "Missing WMAv2 reservoir data.");

            if (firstBitCount < 0 ||
                firstBitCount > first.Length * 8)
            {
                throw new InvalidDataException(
                    "Invalid saved WMAv2 reservoir size.");
            }

            if (secondStartBit < 0 || secondBitCount < 0 ||
                (long)secondStartBit + secondBitCount >
                (long)second.Length * 8L)
            {
                throw new InvalidDataException(
                    "Invalid WMAv2 packet reservoir range.");
            }

            int totalBits = firstBitCount + secondBitCount;
            byte[] destination = new byte[(totalBits + 7) / 8];

            CopyBits(first, 0, destination, 0, firstBitCount);
            CopyBits(
                second,
                secondStartBit,
                destination,
                firstBitCount,
                secondBitCount);

            return destination;
        }

        static void CopyBits(
            byte[] source,
            int sourceStartBit,
            byte[] destination,
            int destinationStartBit,
            int bitCount)
        {
            for (int i = 0; i < bitCount; i++)
            {
                int sourceBit = sourceStartBit + i;
                int value =
                    (source[sourceBit >> 3] >>
                    (7 - (sourceBit & 7))) & 1;

                if (value != 0)
                {
                    int destinationBit = destinationStartBit + i;
                    destination[destinationBit >> 3] |=
                        (byte)(1 << (7 - (destinationBit & 7)));
                }
            }
        }
    }
}
