using System.IO.Compression;
using System.Text;
using Adk.Compression.Zip;
using Adk.Image.Png;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class PngDecoderTests
{
    static readonly int[] Adam7StartX = { 0, 4, 0, 2, 0, 1, 0 };
    static readonly int[] Adam7StartY = { 0, 0, 4, 0, 2, 0, 1 };
    static readonly int[] Adam7StepX = { 8, 8, 4, 4, 2, 2, 1 };
    static readonly int[] Adam7StepY = { 8, 8, 8, 4, 4, 2, 2 };

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

    [Fact]
    public void Load_DecodesSinglePixelAdam7Image()
    {
        var bitmap = RawPngBitmap.Load(new MemoryStream(
            CreatePng(1, 1, 8, 6, new byte[] { 0, 12, 34, 56, 78 }, interlaceMethod: 1)));

        bitmap.GetRgba(0, 0, out var r, out var g, out var b, out var a);
        Assert.Equal(12, r);
        Assert.Equal(34, g);
        Assert.Equal(56, b);
        Assert.Equal(78, a);
    }

    [Fact]
    public void Load_DecodesAdam7RgbaAcrossAllPasses()
    {
        const int width = 9;
        const int height = 9;
        var expected = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                expected[offset] = (byte)(x * 17 + y);
                expected[offset + 1] = (byte)(y * 19 + x);
                expected[offset + 2] = (byte)(x * 11 + y * 7);
                expected[offset + 3] = (byte)(255 - x * 3 - y * 5);
            }
        }

        var filteredRows = CreateAdam7Rgba8(width, height, expected);
        var bitmap = RawPngBitmap.Load(new MemoryStream(
            CreatePng(width, height, 8, 6, filteredRows, interlaceMethod: 1)));

        Assert.Equal(expected, bitmap.Pixels);
        Assert.Null(bitmap.RedSamples16);
    }

    [Fact]
    public void Load_DecodesAdam7PackedTwoBitGrayscale()
    {
        const int width = 10;
        const int height = 9;
        var samples = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                samples[y * width + x] = (byte)((x + y * 2) & 3);
        }

        var filteredRows = CreateAdam7PackedGray(width, height, 2, samples);
        var bitmap = RawPngBitmap.Load(new MemoryStream(
            CreatePng(width, height, 2, 0, filteredRows, interlaceMethod: 1)));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = (byte)(samples[y * width + x] * 85);
                bitmap.GetRgba(x, y, out var r, out var g, out var b, out var a);
                Assert.Equal(expected, r);
                Assert.Equal(expected, g);
                Assert.Equal(expected, b);
                Assert.Equal(255, a);
            }
        }
    }

    [Fact]
    public void Load_DecodesImageMagickAdam7FixtureWithPassFilters()
    {
        // 15x11 grayscale16 PNG written by ImageMagick with Adam7 enabled.
        const string fixture =
            "iVBORw0KGgoAAAANSUhEUgAAAA8AAAALEAAAAAGiu2RVAAAAAmJLR0T//xSrMc0AAAAHdElNRQfqCAETAhNbL4CVAAAAvUlEQVQY032KSw4BQRRF672qqNJE035NfCJC2piJPfRiLMEaLKIG1mHAZSDpmakNMBETYtBoLSEvuTnv3Cu8ib9T3SATiOq85atuoAPqrVUoQxlS+RCDbFJ25sycE41ZxqZxdwfFa3Fb6nhnb1Wp02jBlizbd06HqdfKss4t87f8vtB3L+5GabBlEBgMAhsYaLxSmdiCwJahzBPiOSdDAw2qNURE7xPf1D7+KSNl8CESRqpORJqVwa9SRBQ9AJY1d4TGTJgYAAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA4LTAxVDE5OjAyOjE5KzAwOjAwnvnOiAAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wOC0wMVQxOTowMjoxOSswMDowMO+kdjQAAAAodEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDgtMDFUMTk6MDI6MTkrMDA6MDC4sVfrAAAAAElFTkSuQmCC";

        var bitmap = RawPngBitmap.Load(new MemoryStream(Convert.FromBase64String(fixture)));

        Assert.Equal(15, bitmap.Width);
        Assert.Equal(11, bitmap.Height);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var expected = (ushort)(0x1234 + x * 211 + y * 997);
                Assert.Equal(expected, bitmap.GetHeight16Expanded(x, y));
            }
        }
    }

    [Fact]
    public void Load_PreservesAdam7SixteenBitGrayscaleSamples()
    {
        const int width = 9;
        const int height = 8;
        var samples = new ushort[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                samples[y * width + x] = (ushort)(0x1000 + y * 0x0800 + x * 0x0031);
        }

        var filteredRows = CreateAdam7Gray16(width, height, samples);
        var bitmap = RawPngBitmap.Load(new MemoryStream(
            CreatePng(width, height, 16, 0, filteredRows, interlaceMethod: 1)));

        Assert.Equal(samples, bitmap.RedSamples16);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = samples[y * width + x];
                Assert.Equal(expected, bitmap.GetHeight16Expanded(x, y));
                Assert.Equal((byte)(expected >> 8), bitmap.GetHeight8(x, y));
            }
        }
    }

    [Fact]
    public void PlanetImageSet_AllowsDifferentHeightAndMaterialResolutions()
    {
        var heightPng = CreateSolidGrayscalePng(4, 4, 17);
        var materialPng = CreateSolidGrayscalePng(2, 2, 91);

        var images = PlanetImageSet.LoadAll(fileName => new MemoryStream(
            fileName.EndsWith("_mat.png", StringComparison.OrdinalIgnoreCase)
                ? materialPng
                : heightPng,
            writable: false));

        Assert.Equal(4, images["back"].Width);
        Assert.Equal(4, images["up"].Height);
        Assert.Equal(2, images["back_mat"].Width);
        Assert.Equal(2, images["up_mat"].Height);
    }

    [Fact]
    public void PlanetImageSet_StillRejectsMismatchWithinOneMapFamily()
    {
        var heightPng = CreateSolidGrayscalePng(4, 4, 17);
        var wrongHeightPng = CreateSolidGrayscalePng(2, 2, 17);
        var materialPng = CreateSolidGrayscalePng(2, 2, 91);

        var error = Assert.Throws<Adk.Compression.Exceptions.InvalidDataException>(() =>
            PlanetImageSet.LoadAll(fileName => new MemoryStream(
                string.Equals(fileName, "front.png", StringComparison.OrdinalIgnoreCase)
                    ? wrongHeightPng
                    : fileName.EndsWith("_mat.png", StringComparison.OrdinalIgnoreCase)
                        ? materialPng
                        : heightPng,
                writable: false)));

        Assert.Contains("other height faces", error.Message);
    }

    static byte[] CreateAdam7Rgba8(int width, int height, byte[] rgba)
    {
        var output = new List<byte>();
        ForEachAdam7Pass(width, height, (passWidth, passHeight, startX, startY, stepX, stepY) =>
        {
            for (var passY = 0; passY < passHeight; passY++)
            {
                output.Add(0);
                var y = startY + passY * stepY;
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var x = startX + passX * stepX;
                    var offset = (y * width + x) * 4;
                    output.Add(rgba[offset]);
                    output.Add(rgba[offset + 1]);
                    output.Add(rgba[offset + 2]);
                    output.Add(rgba[offset + 3]);
                }
            }
        });

        return output.ToArray();
    }

    static byte[] CreateSolidGrayscalePng(int width, int height, byte value)
    {
        var filteredRows = new byte[height * (width + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width + 1);
            filteredRows[row] = 0;
            for (var x = 0; x < width; x++)
                filteredRows[row + 1 + x] = value;
        }

        return CreatePng(width, height, 8, 0, filteredRows);
    }

    static byte[] CreateAdam7Gray16(int width, int height, ushort[] samples)
    {
        var output = new List<byte>();
        ForEachAdam7Pass(width, height, (passWidth, passHeight, startX, startY, stepX, stepY) =>
        {
            for (var passY = 0; passY < passHeight; passY++)
            {
                output.Add(0);
                var y = startY + passY * stepY;
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var x = startX + passX * stepX;
                    var sample = samples[y * width + x];
                    output.Add((byte)(sample >> 8));
                    output.Add((byte)sample);
                }
            }
        });

        return output.ToArray();
    }

    static byte[] CreateAdam7PackedGray(
        int width,
        int height,
        int bitDepth,
        byte[] samples)
    {
        var output = new List<byte>();
        ForEachAdam7Pass(width, height, (passWidth, passHeight, startX, startY, stepX, stepY) =>
        {
            var rowBytes = (passWidth * bitDepth + 7) / 8;
            for (var passY = 0; passY < passHeight; passY++)
            {
                output.Add(0);
                var packed = new byte[rowBytes];
                var y = startY + passY * stepY;
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var x = startX + passX * stepX;
                    var sample = samples[y * width + x];
                    var bitOffset = passX * bitDepth;
                    var byteOffset = bitOffset >> 3;
                    var shift = 8 - bitDepth - (bitOffset & 7);
                    packed[byteOffset] |= (byte)(sample << shift);
                }

                output.AddRange(packed);
            }
        });

        return output.ToArray();
    }

    static void ForEachAdam7Pass(
        int width,
        int height,
        Action<int, int, int, int, int, int> action)
    {
        for (var pass = 0; pass < 7; pass++)
        {
            var passWidth = GetPassSize(width, Adam7StartX[pass], Adam7StepX[pass]);
            var passHeight = GetPassSize(height, Adam7StartY[pass], Adam7StepY[pass]);
            if (passWidth == 0 || passHeight == 0)
                continue;

            action(
                passWidth,
                passHeight,
                Adam7StartX[pass],
                Adam7StartY[pass],
                Adam7StepX[pass],
                Adam7StepY[pass]);
        }
    }

    static int GetPassSize(int fullSize, int start, int step)
    {
        return fullSize <= start ? 0 : (fullSize - start + step - 1) / step;
    }

    static byte[] CreatePng(
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte[] filteredRows,
        byte interlaceMethod = 0)
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
            writer.Write(interlaceMethod);
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
