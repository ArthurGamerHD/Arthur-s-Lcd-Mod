#if EXPERIMENTAL
using LcdMod.Client.Audio.Xwma.Decoder;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class XwmaPcmDecoderTests
{
    [Theory]
    [InlineData("Spazzmatica_Polka.xwm")]
    [InlineData("Mus_victory_KA_1.xwm")]
    public void TryDecodeToWaveFile_ConvertsExampleSound(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        var outPath = Path.Combine(AppContext.BaseDirectory, "Data", fileName.Replace(".xwm", ".wav"));
        using var input = File.OpenRead(path);
        using var output = File.Open(outPath, FileMode.Create);

        Assert.True(
            XwmaPcmDecoder.TryDecodeToWaveFile(
                input,
                output,
                out var failureReason),
            failureReason);

        var memoryStream = new MemoryStream();
        output.Seek(0, SeekOrigin.Begin);
        output.CopyTo(memoryStream);
        
        var wave = memoryStream.ToArray();
        Assert.True(wave.Length > 44);
        Assert.Equal("RIFF", ReadFourCc(wave, 0));
        Assert.Equal((uint)(wave.Length - 8), ReadUInt32LittleEndian(wave, 4));
        Assert.Equal("WAVE", ReadFourCc(wave, 8));
        Assert.Equal((ushort)1, ReadUInt16LittleEndian(wave, 22));
        Assert.Equal(24000u, ReadUInt32LittleEndian(wave, 24));
        Assert.Equal((ushort)16, ReadUInt16LittleEndian(wave, 34));
        Assert.Equal("data", ReadFourCc(wave, 36));
        Assert.Equal((uint)(wave.Length - 44), ReadUInt32LittleEndian(wave, 40));
    }

    static string ReadFourCc(byte[] data, int offset)
    {
        return new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3]
        });
    }

    static ushort ReadUInt16LittleEndian(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    static uint ReadUInt32LittleEndian(byte[] data, int offset)
    {
        return (uint)(
            data[offset] |
            (data[offset + 1] << 8) |
            (data[offset + 2] << 16) |
            (data[offset + 3] << 24));
    }
}
#endif
