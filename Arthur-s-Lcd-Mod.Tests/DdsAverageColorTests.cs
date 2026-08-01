using LcdMod.Common.Imaging;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class DdsAverageColorTests
{
    [Fact]
    public void TryAverageFirstMip_ReadsDxt1Color()
    {
        byte[] dds = CreateDxt1Dds(4, 4, 1, new[]
        {
            CreateDxt1Block(0xF800, 0x07E0, 0x00000000u)
        });

        Assert.True(DdsAverageColor.TryAverageFirstMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void TryAverageFirstMip_HandlesPartialDxt1Block()
    {
        byte[] dds = CreateDxt1Dds(2, 2, 1, new[]
        {
            CreateDxt1Block(0xF800, 0x07E0, 0x55555555u)
        });

        Assert.True(DdsAverageColor.TryAverageFirstMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(0, r);
        Assert.Equal(255, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void TryAverageSmallestMip_UsesTerminalDxt1Mip()
    {
        byte[] dds = CreateDxt1Dds(4, 4, 3, new[]
        {
            CreateDxt1Block(0xF800, 0x07E0, 0x00000000u),
            CreateDxt1Block(0x07E0, 0x001F, 0x00000000u),
            CreateDxt1Block(0x001F, 0xF800, 0x00000000u)
        });

        Assert.True(DdsAverageColor.TryAverageSmallestMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(255, b);
    }

    [Fact]
    public void TryAverageSmallestMip_ReadsBc7Mode6Dx10Color()
    {
        byte[] dds = CreateBc7Mode6Dds(186, 148, 98);

        Assert.True(DdsAverageColor.TryAverageSmallestMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(186, r);
        Assert.Equal(148, g);
        Assert.Equal(98, b);
    }

    [Fact]
    public void TryAverageSmallestMip_StreamReadsOnlyTerminalMipAndRestoresPosition()
    {
        byte[] dds = CreateBc7Mode6Dds(186, 148, 98);
        using var stream = new MemoryStream(dds);

        Assert.True(DdsAverageColor.TryAverageSmallestMip(stream, out byte r, out byte g, out byte b));
        Assert.Equal(186, r);
        Assert.Equal(148, g);
        Assert.Equal(98, b);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void TryAverageSmallestMip_ReadsBc7Mode5Color()
    {
        byte[] dds = CreateBc7Mode5Dds(0, 129, 64, 193, 17);

        Assert.True(DdsAverageColor.TryAverageSmallestMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(129, r);
        Assert.Equal(64, g);
        Assert.Equal(193, b);
    }

    [Fact]
    public void TryAverageSmallestMip_AppliesBc7Mode5Rotation()
    {
        byte[] dds = CreateBc7Mode5Dds(1, 129, 64, 193, 17);

        Assert.True(DdsAverageColor.TryAverageSmallestMip(dds, out byte r, out byte g, out byte b));
        Assert.Equal(17, r);
        Assert.Equal(64, g);
        Assert.Equal(193, b);
    }

    [Fact]
    public void TryAverageSmallestMip_ReadsNonSeekableStreamForwardOnly()
    {
        byte[] dds = CreateDxt1Dds(4, 4, 3, new[]
        {
            CreateDxt1Block(0xF800, 0x07E0, 0x00000000u),
            CreateDxt1Block(0x07E0, 0x001F, 0x00000000u),
            CreateDxt1Block(0x001F, 0xF800, 0x00000000u)
        });
        using var stream = new NonSeekableReadStream(dds);

        Assert.True(DdsAverageColor.TryAverageSmallestMip(stream, out byte r, out byte g, out byte b));
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(255, b);
    }


    [Fact]
    public void TryAverageSmallestMip_ReportsUnsupportedBc7Mode()
    {
        byte[] dds = CreateEmptyBc7Dds();
        dds[148] = 1 << 4;
        using var stream = new NonSeekableReadStream(dds);

        Assert.False(DdsAverageColor.TryAverageSmallestMip(
            stream,
            out _,
            out _,
            out _,
            out string failureReason));
        Assert.Contains("unsupported mode 4", failureReason.ToLowerInvariant());
    }

    [Fact]
    public void TryAverageSmallestMip_ReportsTruncatedHeader()
    {
        using var stream = new NonSeekableReadStream(new byte[64]);

        Assert.False(DdsAverageColor.TryAverageSmallestMip(
            stream,
            out _,
            out _,
            out _,
            out string failureReason));
        Assert.Contains("header", failureReason.ToLowerInvariant());
        Assert.Contains("truncated", failureReason.ToLowerInvariant());
    }

    static byte[] CreateDxt1Dds(int width, int height, int mipCount, byte[][] mipBlocks)
    {
        int payloadLength = 0;
        for (int i = 0; i < mipBlocks.Length; i++)
            payloadLength += mipBlocks[i].Length;

        byte[] dds = new byte[128 + payloadLength];
        WriteUInt32(dds, 0, 0x20534444u);
        WriteUInt32(dds, 4, 124u);
        WriteUInt32(dds, 8, 0x00021007u);
        WriteUInt32(dds, 12, (uint)height);
        WriteUInt32(dds, 16, (uint)width);
        WriteUInt32(dds, 20, 8u);
        WriteUInt32(dds, 28, (uint)mipCount);
        WriteUInt32(dds, 76, 32u);
        WriteUInt32(dds, 80, 0x00000004u);
        WriteUInt32(dds, 84, 0x31545844u);
        WriteUInt32(dds, 108, 0x00401008u);

        int offset = 128;
        for (int i = 0; i < mipBlocks.Length; i++)
        {
            Array.Copy(mipBlocks[i], 0, dds, offset, mipBlocks[i].Length);
            offset += mipBlocks[i].Length;
        }

        return dds;
    }

    static byte[] CreateDxt1Block(ushort color0, ushort color1, uint selectors)
    {
        byte[] block = new byte[8];
        WriteUInt16(block, 0, color0);
        WriteUInt16(block, 2, color1);
        WriteUInt32(block, 4, selectors);
        return block;
    }

    static byte[] CreateBc7Mode5Dds(int rotation, byte r, byte g, byte b, byte a)
    {
        byte[] dds = CreateEmptyBc7Dds();
        var bits = new LsbBitWriter(dds, 148);
        for (int i = 0; i < 5; i++)
            bits.WriteBits(0, 1);
        bits.WriteBits(1, 1); // BC7 mode 5 marker.
        bits.WriteBits(rotation, 2);

        Assert.Equal(r & 1, r >> 7);
        Assert.Equal(g & 1, g >> 7);
        Assert.Equal(b & 1, b >> 7);

        bits.WriteBits(r >> 1, 7);
        bits.WriteBits(r >> 1, 7);
        bits.WriteBits(g >> 1, 7);
        bits.WriteBits(g >> 1, 7);
        bits.WriteBits(b >> 1, 7);
        bits.WriteBits(b >> 1, 7);
        bits.WriteBits(a, 8);
        bits.WriteBits(a, 8);

        bits.WriteBits(0, 1); // Color anchor texel index.
        for (int i = 1; i < 16; i++)
            bits.WriteBits(0, 2);
        bits.WriteBits(0, 1); // Scalar anchor texel index.
        for (int i = 1; i < 16; i++)
            bits.WriteBits(0, 2);

        Assert.Equal(128, bits.BitsWritten);
        return dds;
    }

    static byte[] CreateBc7Mode6Dds(byte r, byte g, byte b)
    {
        byte[] dds = CreateEmptyBc7Dds();
        var bits = new LsbBitWriter(dds, 148);
        for (int i = 0; i < 6; i++)
            bits.WriteBits(0, 1);
        bits.WriteBits(1, 1); // BC7 mode 6 marker.

        int pBit = r & 1;
        Assert.Equal(pBit, g & 1);
        Assert.Equal(pBit, b & 1);

        bits.WriteBits(r >> 1, 7);
        bits.WriteBits(r >> 1, 7);
        bits.WriteBits(g >> 1, 7);
        bits.WriteBits(g >> 1, 7);
        bits.WriteBits(b >> 1, 7);
        bits.WriteBits(b >> 1, 7);
        bits.WriteBits(0, 7);
        bits.WriteBits(0, 7);
        bits.WriteBits(pBit, 1);
        bits.WriteBits(pBit, 1);

        bits.WriteBits(0, 3); // Anchor texel index.
        for (int i = 1; i < 16; i++)
            bits.WriteBits(0, 4);

        Assert.Equal(128, bits.BitsWritten);
        return dds;
    }

    static byte[] CreateEmptyBc7Dds()
    {
        byte[] dds = new byte[148 + 16];
        WriteUInt32(dds, 0, 0x20534444u);
        WriteUInt32(dds, 4, 124u);
        WriteUInt32(dds, 8, 0x00021007u);
        WriteUInt32(dds, 12, 1u);
        WriteUInt32(dds, 16, 1u);
        WriteUInt32(dds, 20, 16u);
        WriteUInt32(dds, 28, 1u);
        WriteUInt32(dds, 76, 32u);
        WriteUInt32(dds, 80, 0x00000004u);
        WriteUInt32(dds, 84, 0x30315844u);
        WriteUInt32(dds, 108, 0x00001000u);

        WriteUInt32(dds, 128, 99u); // DXGI_FORMAT_BC7_UNORM_SRGB
        WriteUInt32(dds, 132, 3u);  // D3D10_RESOURCE_DIMENSION_TEXTURE2D
        WriteUInt32(dds, 136, 0u);
        WriteUInt32(dds, 140, 1u);
        WriteUInt32(dds, 144, 0u);
        return dds;
    }

    static void WriteUInt16(byte[] output, int offset, ushort value)
    {
        output[offset] = (byte)value;
        output[offset + 1] = (byte)(value >> 8);
    }

    static void WriteUInt32(byte[] output, int offset, uint value)
    {
        output[offset] = (byte)value;
        output[offset + 1] = (byte)(value >> 8);
        output[offset + 2] = (byte)(value >> 16);
        output[offset + 3] = (byte)(value >> 24);
    }

    sealed class NonSeekableReadStream : Stream
    {
        readonly byte[] _data;
        int _position;

        public NonSeekableReadStream(byte[] data)
        {
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = _data.Length - _position;
            if (available <= 0)
                return 0;

            int read = Math.Min(available, count);
            Array.Copy(_data, _position, buffer, offset, read);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    sealed class LsbBitWriter
    {
        readonly byte[] _output;
        readonly int _startBit;
        int _bitPosition;

        public LsbBitWriter(byte[] output, int byteOffset)
        {
            _output = output;
            _startBit = byteOffset * 8;
            _bitPosition = _startBit;
        }

        public int BitsWritten => _bitPosition - _startBit;

        public void WriteBits(int value, int count)
        {
            for (int bit = 0; bit < count; bit++)
            {
                int byteIndex = _bitPosition >> 3;
                int bitIndex = _bitPosition & 7;
                if (((value >> bit) & 1) != 0)
                    _output[byteIndex] |= (byte)(1 << bitIndex);
                _bitPosition++;
            }
        }
    }
}
