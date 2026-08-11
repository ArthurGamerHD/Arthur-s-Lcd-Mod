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

    static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);

        return output.ToArray();
    }
}
