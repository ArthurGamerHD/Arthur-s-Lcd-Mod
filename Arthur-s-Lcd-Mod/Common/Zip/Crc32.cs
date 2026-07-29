using System;
using ArgumentOutOfRangeException = LcdMod.Common.ArgumentOutOfRangeException;

namespace LcdMod.Common.Zip
{
    public static class Crc32
    {
        static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return Finish(Update(0xFFFFFFFFu, data, 0, data.Length));
        }

        public static uint Compute(byte[] first, byte[] second)
        {
            if (first == null)
                throw new ArgumentNullException(nameof(first));

            if (second == null)
                throw new ArgumentNullException(nameof(second));

            uint crc = 0xFFFFFFFFu;
            crc = Update(crc, first, 0, first.Length);
            crc = Update(crc, second, 0, second.Length);
            return Finish(crc);
        }

        public static uint Update(uint crc, byte[] data, int offset, int count)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            for (int i = 0; i < count; i++)
                crc = Table[(int)((crc ^ data[offset + i]) & 0xFFu)] ^ (crc >> 8);

            return crc;
        }

        public static uint Finish(uint crc)
        {
            return crc ^ 0xFFFFFFFFu;
        }

        static uint[] BuildTable()
        {
            uint[] table = new uint[256];

            for (int n = 0; n < table.Length; n++)
            {
                uint c = (uint)n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

                table[n] = c;
            }

            return table;
        }
    }
}
