using System;

namespace LcdMod.Common.Imaging
{
    // BC7 partition/anchor tables from BCnEncoder.NET (Nominom and contributors),
    // used under the Unlicense: https://github.com/Nominom/BCnEncoder.NET.
    // All eight BC7 modes are needed by the stock Space Engineers skybox.
    internal sealed class Bc7Decoder
    {
        static readonly uint[] Partitions2 =
        {
            0x50505050u, 0x40404040u, 0x54545454u, 0x54505040u, 0x50404000u, 0x55545450u, 0x55545040u, 0x54504000u,
            0x50400000u, 0x55555450u, 0x55544000u, 0x54400000u, 0x55555440u, 0x55550000u, 0x55555500u, 0x55000000u,
            0x55150100u, 0x00004054u, 0x15010000u, 0x00405054u, 0x00004050u, 0x15050100u, 0x05010000u, 0x40505054u,
            0x00404050u, 0x05010100u, 0x14141414u, 0x05141450u, 0x01155440u, 0x00555500u, 0x15014054u, 0x05414150u,
            0x44444444u, 0x55005500u, 0x11441144u, 0x05055050u, 0x05500550u, 0x11114444u, 0x41144114u, 0x44111144u,
            0x15055054u, 0x01055040u, 0x05041050u, 0x05455150u, 0x14414114u, 0x50050550u, 0x41411414u, 0x00141400u,
            0x00041504u, 0x00105410u, 0x10541000u, 0x04150400u, 0x50410514u, 0x41051450u, 0x05415014u, 0x14054150u,
            0x41050514u, 0x41505014u, 0x40011554u, 0x54150140u, 0x50505500u, 0x00555050u, 0x15151010u, 0x54540404u,
        };
        static readonly uint[] Partitions3 =
        {
            0xaa685050u, 0x6a5a5040u, 0x5a5a4200u, 0x5450a0a8u, 0xa5a50000u, 0xa0a05050u, 0x5555a0a0u, 0x5a5a5050u,
            0xaa550000u, 0xaa555500u, 0xaaaa5500u, 0x90909090u, 0x94949494u, 0xa4a4a4a4u, 0xa9a59450u, 0x2a0a4250u,
            0xa5945040u, 0x0a425054u, 0xa5a5a500u, 0x55a0a0a0u, 0xa8a85454u, 0x6a6a4040u, 0xa4a45000u, 0x1a1a0500u,
            0x0050a4a4u, 0xaaa59090u, 0x14696914u, 0x69691400u, 0xa08585a0u, 0xaa821414u, 0x50a4a450u, 0x6a5a0200u,
            0xa9a58000u, 0x5090a0a8u, 0xa8a09050u, 0x24242424u, 0x00aa5500u, 0x24924924u, 0x24499224u, 0x50a50a50u,
            0x500aa550u, 0xaaaa4444u, 0x66660000u, 0xa5a0a5a0u, 0x50a050a0u, 0x69286928u, 0x44aaaa44u, 0x66666600u,
            0xaa444444u, 0x54a854a8u, 0x95809580u, 0x96969600u, 0xa85454a8u, 0x80959580u, 0xaa141414u, 0x96960000u,
            0xaaaa1414u, 0xa05050a0u, 0xa0a5a5a0u, 0x96000000u, 0x40804080u, 0xa9a8a9a8u, 0xaaaaaa44u, 0x2a4a5254u,
        };
        static readonly int[] Anchors2 = { 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2, 15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15 };
        static readonly int[] Anchors31 = { 3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15, 8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15, 3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3 };
        static readonly int[] Anchors32 = { 15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8, 15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8, 15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8, 15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8 };

        static readonly int[] ColorBits = { 4, 6, 5, 7, 5, 7, 7, 5 };
        static readonly int[] AlphaBits = { 0, 0, 0, 0, 6, 8, 7, 5 };
        static readonly int[][] Weights =
        {
            new[] { 0, 21, 43, 64 },
            new[] { 0, 9, 18, 27, 37, 46, 55, 64 },
            new[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 }
        };
        readonly int[,] _endpoints = new int[6, 4];
        readonly int[] _indices = new int[16];

        public void Decode(byte[] data, int offset, byte[] rgba)
        {
            var bits = new Bits(data, offset);
            int mode = 0;
            while (mode < 8 && bits.Read(1) == 0) mode++;
            if (mode == 8)
                throw new InvalidOperationException("Invalid BC7 block.");
            int subsets = mode == 0 || mode == 2 ? 3 : mode == 1 || mode == 3 || mode == 7 ? 2 : 1;
            int partition = subsets == 1 ? 0 : bits.Read(mode == 0 ? 4 : 6);
            int rotation = mode == 4 || mode == 5 ? bits.Read(2) : 0;
            int selection = mode == 4 ? bits.Read(1) : 0;
            int endpoints = subsets * 2;
            for (int c = 0; c < 4; c++)
            for (int e = 0; e < endpoints; e++)
                _endpoints[e, c] = bits.Read(c == 3 ? AlphaBits[mode] : ColorBits[mode]);

            bool pbits = mode == 0 || mode == 1 || mode == 3 || mode == 6 || mode == 7;
            if (pbits)
            {
                for (int e = 0; e < endpoints;)
                {
                    int p = bits.Read(1);
                    int count = mode == 1 ? 2 : 1;
                    for (int i = 0; i < count; i++, e++)
                    for (int c = 0; c < 4; c++)
                        _endpoints[e, c] = (_endpoints[e, c] << 1) | p;
                }
            }
            for (int e = 0; e < endpoints; e++)
            for (int c = 0; c < 4; c++)
            {
                int precision = c == 3 ? AlphaBits[mode] : ColorBits[mode];
                if (precision == 0) { _endpoints[e, c] = 255; continue; }
                precision += pbits ? 1 : 0;
                int v = _endpoints[e, c] << (8 - precision);
                _endpoints[e, c] = v | (v >> precision);
            }
            int indexBits = mode == 0 || mode == 1 ? 3 : mode == 6 ? 4 : 2;
            int secondaryBits = mode == 4 ? 3 : mode == 5 ? 2 : 0;
            uint partitionMap = subsets == 2 ? Partitions2[partition] : subsets == 3 ? Partitions3[partition] : 0u;
            for (int i = 0; i < 16; i++)
            {
                bool anchor = i == 0 ||
                    subsets == 2 && i == Anchors2[partition] ||
                    subsets == 3 && (i == Anchors31[partition] || i == Anchors32[partition]);
                _indices[i] = bits.Read(indexBits - (anchor ? 1 : 0));
            }
            for (int i = 0; i < 16; i++)
            {
                int second = secondaryBits == 0 ? 0 : bits.Read(secondaryBits - (i == 0 ? 1 : 0));
                int subset = (int)((partitionMap >> (i * 2)) & 3);
                for (int c = 0; c < 4; c++)
                {
                    bool secondary = secondaryBits != 0 && (c == 3 ? selection == 0 : selection != 0);
                    int weight = secondary ? Weights[secondaryBits - 2][second] : Weights[indexBits - 2][_indices[i]];
                    rgba[i * 4 + c] = (byte)(((64 - weight) * _endpoints[subset * 2, c] +
                        weight * _endpoints[subset * 2 + 1, c] + 32) >> 6);
                }
                if (rotation != 0)
                {
                    byte alpha = rgba[i * 4 + 3];
                    rgba[i * 4 + 3] = rgba[i * 4 + rotation - 1];
                    rgba[i * 4 + rotation - 1] = alpha;
                }
            }
        }

        struct Bits
        {
            readonly byte[] _data;
            readonly int _offset;
            int _bit;
            public Bits(byte[] data, int offset) { _data = data; _offset = offset; _bit = 0; }
            public int Read(int count)
            {
                if (_bit + count > 128) throw new InvalidOperationException("BC7 block overflow.");
                int value = 0;
                for (int i = 0; i < count; i++, _bit++)
                    value |= ((_data[_offset + (_bit >> 3)] >> (_bit & 7)) & 1) << i;
                return value;
            }
        }
    }
}
