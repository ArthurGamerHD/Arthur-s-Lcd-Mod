using System;

namespace LcdMod.Common.Images
{
    public static class ImageSha256
    {
        static readonly uint[] RoundConstants =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        public static byte[] Compute(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            uint[] state =
            {
                0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
            };

            var schedule = new uint[64];
            int fullBlocks = data.Length / 64;
            for (int block = 0; block < fullBlocks; block++)
                ProcessBlock(data, block * 64, state, schedule);

            int remaining = data.Length - fullBlocks * 64;
            var tail = new byte[remaining < 56 ? 64 : 128];
            Buffer.BlockCopy(data, fullBlocks * 64, tail, 0, remaining);
            tail[remaining] = 0x80;

            ulong bitLength = (ulong)data.Length * 8UL;
            for (int i = 0; i < 8; i++)
                tail[tail.Length - 1 - i] = (byte)(bitLength >> (8 * i));

            for (int offset = 0; offset < tail.Length; offset += 64)
                ProcessBlock(tail, offset, state, schedule);

            var hash = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                hash[i * 4] = (byte)(state[i] >> 24);
                hash[i * 4 + 1] = (byte)(state[i] >> 16);
                hash[i * 4 + 2] = (byte)(state[i] >> 8);
                hash[i * 4 + 3] = (byte)state[i];
            }

            return hash;
        }

        public static string ComputeHex(byte[] data, int byteCount)
        {
            byte[] hash = Compute(data);
            var chars = new char[byteCount * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < byteCount; i++)
            {
                chars[i * 2] = digits[hash[i] >> 4];
                chars[i * 2 + 1] = digits[hash[i] & 15];
            }

            return new string(chars);
        }

        static void ProcessBlock(byte[] data, int offset, uint[] state, uint[] schedule)
        {
            for (int i = 0; i < 16; i++)
            {
                int index = offset + i * 4;
                schedule[i] = ((uint)data[index] << 24) | ((uint)data[index + 1] << 16) |
                              ((uint)data[index + 2] << 8) | data[index + 3];
            }

            for (int i = 16; i < 64; i++)
            {
                uint s0 = RotateRight(schedule[i - 15], 7) ^ RotateRight(schedule[i - 15], 18) ^ (schedule[i - 15] >> 3);
                uint s1 = RotateRight(schedule[i - 2], 17) ^ RotateRight(schedule[i - 2], 19) ^ (schedule[i - 2] >> 10);
                schedule[i] = unchecked(schedule[i - 16] + s0 + schedule[i - 7] + s1);
            }

            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint e = state[4], f = state[5], g = state[6], h = state[7];

            for (int i = 0; i < 64; i++)
            {
                uint sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint choice = (e & f) ^ (~e & g);
                uint temp1 = unchecked(h + sum1 + choice + RoundConstants[i] + schedule[i]);
                uint sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint majority = (a & b) ^ (a & c) ^ (b & c);
                uint temp2 = unchecked(sum0 + majority);

                h = g;
                g = f;
                f = e;
                e = unchecked(d + temp1);
                d = c;
                c = b;
                b = a;
                a = unchecked(temp1 + temp2);
            }

            unchecked
            {
                state[0] += a;
                state[1] += b;
                state[2] += c;
                state[3] += d;
                state[4] += e;
                state[5] += f;
                state[6] += g;
                state[7] += h;
            }
        }

        static uint RotateRight(uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }
    }
}
