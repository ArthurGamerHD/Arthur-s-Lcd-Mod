using LcdMod.Client.Audio.Xwma;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class XwmaReaderTests
{
    [Fact]
    public void Parse_ReadsSpazzmaticaPolka()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Spazzmatica_Polka.xwm");
        using var stream = File.OpenRead(path);

        var info = XwmaParser.Parse(stream);

        Assert.Equal(XwmaProfileKind.Wma2Stereo48000Hz48Kbps, info.Profile);
        Assert.Equal((ushort)0x0161, info.Format.FormatTag);
        Assert.Equal((ushort)2, info.Format.Channels);
        Assert.Equal(48000u, info.Format.SampleRate);
        Assert.Equal(6000u, info.Format.AverageBytesPerSecond);
        Assert.Equal((ushort)1008, info.Format.BlockAlign);
        Assert.Equal((ushort)16, info.Format.BitsPerSample);
        Assert.Equal(470, info.PacketCount);
        Assert.Equal(473760u, info.DataLength);
        Assert.Equal(11542528u, info.DeclaredDecodedPcmBytes);
        Assert.Equal(info.DataOffset, stream.Position);
    }

    [Fact]
    public void Parse_ReadsMusVictoryKa1()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Mus_victory_KA_1.xwm");
        using var stream = File.OpenRead(path);

        var info = XwmaParser.Parse(stream);

        Assert.Equal(XwmaProfileKind.Wma2Stereo44100Hz48Kbps, info.Profile);
        Assert.Equal((ushort)0x0161, info.Format.FormatTag);
        Assert.Equal((ushort)2, info.Format.Channels);
        Assert.Equal(44100u, info.Format.SampleRate);
        Assert.Equal(6000u, info.Format.AverageBytesPerSecond);
        Assert.Equal((ushort)2230, info.Format.BlockAlign);
        Assert.Equal((ushort)16, info.Format.BitsPerSample);
        Assert.Equal(198, info.PacketCount);
        Assert.Equal(441540u, info.DataLength);
        Assert.Equal(12705792u, info.DeclaredDecodedPcmBytes);
        Assert.Equal(info.DataOffset, stream.Position);
    }
}
