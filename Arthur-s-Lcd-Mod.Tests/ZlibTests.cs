using System.IO.Compression;
using System.Text;
using Adk.Compression.Zip;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ZlibTests
{
    [Fact]
    public void Inflate_DecodesRuntimeZlibStream()
    {
        var expected = Encoding.UTF8.GetBytes(
            string.Join("|", Enumerable.Repeat("lcd mod planet texture proof", 128)));
        var compressed = CompressZlib(expected);

        Assert.Equal(expected, Zlib.Inflate(compressed, expected.Length));
    }

    public static IEnumerable<object[]> Payloads()
    {
        foreach (DeflateLevel level in Enum.GetValues<DeflateLevel>())
        {
            yield return new object[] { level, "empty", Array.Empty<byte>() };
            yield return new object[] { level, "single", new byte[] { 42 } };
            yield return new object[] { level, "zeros", new byte[300_000] };
            yield return new object[] { level, "random", Random(200_000, 1) };
            yield return new object[] { level, "text", Text(150_000) };
            yield return new object[] { level, "gradient", Gradient(256, 256) };
            yield return new object[] { level, "mixed", Mixed() };
            yield return new object[] { level, "far-repeat", FarRepeat() };
        }
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void DeflateRaw_IsDecodedByRuntimeInflater(DeflateLevel level, string name, byte[] data)
    {
        byte[] compressed = Zlib.DeflateRaw(data, level);

        Assert.True(data.SequenceEqual(InflateWithRuntime(compressed)), name);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void DeflateRaw_IsDecodedByManagedInflater(DeflateLevel level, string name, byte[] data)
    {
        byte[] compressed = Zlib.DeflateRaw(data, level);

        Assert.True(data.SequenceEqual(Zlib.InflateRawDeflate(compressed, 0, compressed.Length, data.Length)), name);
    }

    [Fact]
    public void DeflateZlib_IsDecodedByRuntimeZlibStream()
    {
        byte[] data = Text(50_000);
        byte[] compressed = Zlib.DeflateZlib(data, DeflateLevel.Maximum);

        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        Assert.Equal(data, output.ToArray());
        Assert.Equal(data, Zlib.Inflate(compressed, data.Length));
    }

    [Fact]
    public void DeflateRaw_RandomDataFallsBackToStoredBlocks()
    {
        byte[] data = Random(200_000, 7);

        byte[] compressed = Zlib.DeflateRaw(data, DeflateLevel.Default);

        Assert.True(compressed.Length <= data.Length + 64, $"{compressed.Length} > {data.Length}");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("gradient")]
    [InlineData("mixed")]
    public void DeflateRaw_MaximumIsNotLargerThanRuntimeSmallestSize(string name)
    {
        byte[] data = name == "text" ? Text(150_000) : name == "gradient" ? Gradient(256, 256) : Mixed();

        int runtime = DeflateWithRuntime(data, CompressionLevel.SmallestSize).Length;
        int maximum = Zlib.DeflateRaw(data, DeflateLevel.Maximum).Length;

        Assert.True(maximum <= runtime, $"{name}: maximum {maximum} > runtime {runtime}");
    }

    static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    static byte[] Text(int length)
    {
        var random = new Random(3);
        string[] words = { "lcd", "picture", "frame", "grid", "texture", "storage", "canary", "sentinel", "image", "dedup" };
        var builder = new StringBuilder();
        while (builder.Length < length)
            builder.Append(words[random.Next(words.Length)]).Append(random.Next(8) == 0 ? ".\n" : " ");

        return Encoding.UTF8.GetBytes(builder.ToString(0, length));
    }

    static byte[] Gradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        var random = new Random(5);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            bytes[offset] = (byte)x;
            bytes[offset + 1] = (byte)(y + random.Next(3));
            bytes[offset + 2] = (byte)((x + y) / 2);
            bytes[offset + 3] = 255;
        }

        return bytes;
    }

    static byte[] Mixed()
    {
        return Random(70_000, 11)
            .Concat(new byte[90_000])
            .Concat(Text(80_000))
            .Concat(Random(40_000, 12))
            .Concat(Gradient(64, 64))
            .ToArray();
    }

    static byte[] FarRepeat()
    {
        byte[] chunk = Random(20_000, 13);
        return chunk.Concat(Random(12_000, 14)).Concat(chunk).Concat(chunk).ToArray();
    }

    static byte[] InflateWithRuntime(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    static byte[] DeflateWithRuntime(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
            deflate.Write(data, 0, data.Length);

        return output.ToArray();
    }

    static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);

        return output.ToArray();
    }
}
