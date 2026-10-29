using Adk.Compression.Zip;
using Adk.Image.Png;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class PngEncoderTests
{
    public static IEnumerable<object[]> Images()
    {
        yield return new object[] { "two-color", 61, 37, TwoColor(61, 37), 3, 1 };
        yield return new object[] { "sixteen-translucent", 63, 41, Palette(63, 41, 16, translucent: true), 3, 4 };
        yield return new object[] { "two-hundred-colors", 40, 40, Palette(40, 40, 200, translucent: false), 3, 8 };
        yield return new object[] { "gray", 64, 32, Gray(64, 32, opaque: true), 0, 8 };
        yield return new object[] { "gray-alpha", 64, 32, Gray(64, 32, opaque: false), 4, 8 };
        yield return new object[] { "rgb", 48, 48, Noise(48, 48, opaque: true), 2, 8 };
        yield return new object[] { "rgba", 48, 48, Noise(48, 48, opaque: false), 6, 8 };
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Encode_RoundTripsPixelsWithReducedColorType(
        string name, int width, int height, byte[] rgba, int colorType, int bitDepth)
    {
        byte[] png = PngEncoder.Encode(rgba, width, height, DeflateLevel.Default);

        RawPngBitmap decoded = RawPngBitmap.Load(png);

        Assert.True(rgba.SequenceEqual(decoded.Pixels), name);
        Assert.Equal(colorType, decoded.SourceColorType);
        Assert.Equal(bitDepth, decoded.SourceBitDepth);
    }

    [Fact]
    public void Encode_IsSmallerThanDeflatingUnfilteredPixels()
    {
        const int size = 128;
        byte[] rgba = Gradient(size, size);

        byte[] png = PngEncoder.Encode(rgba, size, size, DeflateLevel.Maximum);
        int unfiltered = Zlib.DeflateRaw(rgba, DeflateLevel.Maximum).Length;

        Assert.True(png.Length < unfiltered, $"png {png.Length} >= unfiltered {unfiltered}");
        Assert.Equal(rgba, RawPngBitmap.Load(png).Pixels);
    }

    static byte[] TwoColor(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            byte value = (byte)((i * 7 % 3) == 0 ? 255 : 0);
            rgba[i * 4] = value;
            rgba[i * 4 + 1] = 10;
            rgba[i * 4 + 2] = 20;
            rgba[i * 4 + 3] = 255;
        }

        return rgba;
    }

    static byte[] Palette(int width, int height, int colors, bool translucent)
    {
        var rgba = new byte[width * height * 4];
        var random = new Random(colors);
        for (int i = 0; i < width * height; i++)
        {
            int color = i < colors ? i : random.Next(colors);
            rgba[i * 4] = (byte)(color * 37);
            rgba[i * 4 + 1] = (byte)(color * 11 + 3);
            rgba[i * 4 + 2] = (byte)color;
            rgba[i * 4 + 3] = translucent && color % 3 == 0 ? (byte)(color * 5) : (byte)255;
        }

        return rgba;
    }

    static byte[] Gray(int width, int height, bool opaque)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            byte value = (byte)(i * 13 % 251);
            rgba[i * 4] = value;
            rgba[i * 4 + 1] = value;
            rgba[i * 4 + 2] = value;
            rgba[i * 4 + 3] = opaque ? (byte)255 : (byte)(i % 256);
        }

        return rgba;
    }

    static byte[] Noise(int width, int height, bool opaque)
    {
        var rgba = new byte[width * height * 4];
        new Random(9).NextBytes(rgba);
        if (opaque)
        {
            for (int i = 3; i < rgba.Length; i += 4)
                rgba[i] = 255;
        }

        return rgba;
    }

    static byte[] Gradient(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            rgba[offset] = (byte)(x * 2);
            rgba[offset + 1] = (byte)(y * 2);
            rgba[offset + 2] = (byte)(x + y);
            rgba[offset + 3] = (byte)(255 - x);
        }

        return rgba;
    }
}
