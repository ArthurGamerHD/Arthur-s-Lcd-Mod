using System.IO.Compression;
using System.Text;
using LcdMod.Common.Png;
using LcdMod.Common.Zip;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class PngDecoderTests
{
    [Fact]
    public void Load_Preserves16BitGrayscaleHeightSamples()
    {
        var filteredRows = new byte[]
        {
            0,
            0x12, 0x34,
            0xAB, 0xCD
        };

        var bitmap = RawPngBitmap.Load(new MemoryStream(
            CreatePng(2, 1, 16, 0, filteredRows)));

        Assert.Equal(2, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
        Assert.Equal(16, bitmap.SourceBitDepth);
        Assert.Equal(0, bitmap.SourceColorType);
        Assert.Equal(new ushort[] { 0x1234, 0xABCD }, bitmap.RedSamples16);

        bitmap.GetRgba(1, 0, out var r, out var g, out var b, out var a);

        Assert.Equal(0xAB, r);
        Assert.Equal(0xAB, g);
        Assert.Equal(0xAB, b);
        Assert.Equal(255, a);
    }

    static byte[] CreatePng(
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte[] filteredRows)
    {
        using var output = new MemoryStream();
        output.Write(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            0,
            8);

        using (var header = new MemoryStream())
        using (var writer = new BinaryWriter(header, Encoding.ASCII, leaveOpen: true))
        {
            WriteUInt32BigEndian(writer, (uint)width);
            WriteUInt32BigEndian(writer, (uint)height);
            writer.Write(bitDepth);
            writer.Write(colorType);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            WriteChunk(output, "IHDR", header.ToArray());
        }

        WriteChunk(output, "IDAT", CompressZlib(filteredRows));
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);

        return output.ToArray();
    }

    static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);

        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        WriteUInt32BigEndian(writer, (uint)data.Length);
        writer.Write(typeBytes);
        writer.Write(data);
        WriteUInt32BigEndian(writer, Crc32.Compute(typeBytes, data));
    }

    static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
