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

        Assert.Equal(XwmaProfileKind.Wma2Stereo48000Hz, info.Profile);
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

        Assert.Equal(XwmaProfileKind.Wma2Stereo44100Hz, info.Profile);
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
    [Fact]
    public void Parse_AllowsMonoWmav2XwmaProfile()
    {
        byte[] xwma = CreateMinimalXwma(
            channels: 1,
            sampleRate: 44100u,
            averageBytesPerSecond: 3000u,
            blockAlign: 2230,
            decodedBytes: 4096u);
        using var stream = new MemoryStream(xwma);

        var info = XwmaParser.Parse(stream);

        Assert.Equal(XwmaProfileKind.Wma2Mono44100Hz, info.Profile);
        Assert.Equal((ushort)1, info.Format.Channels);
        Assert.Equal(2048L, info.DeclaredSourceSampleFrames);
        Assert.Equal(info.DataOffset, stream.Position);
    }

    [Fact]
    public void Profile_UsesRateDependentReservoirOffsetWidthForMonoWmav2()
    {
        byte[] xwma = CreateMinimalXwma(
            channels: 1,
            sampleRate: 44100u,
            averageBytesPerSecond: 6000u,
            blockAlign: 2230,
            decodedBytes: 4096u);
        using var stream = new MemoryStream(xwma);

        var info = XwmaParser.Parse(stream);
        var profile = Wma2DecoderProfile.FromFile(info);

        Assert.Equal(10, profile.ByteOffsetBits);
        Assert.Equal(13, profile.ReservoirBitOffsetFieldBits);
        Assert.Equal(21, profile.SuperframeHeaderBits);
    }

    [Fact]
    public void Profile_KeepsExistingReservoirOffsetWidthForStereoWmav2()
    {
        byte[] xwma = CreateMinimalXwma(
            channels: 2,
            sampleRate: 44100u,
            averageBytesPerSecond: 6000u,
            blockAlign: 2230,
            decodedBytes: 4096u);
        using var stream = new MemoryStream(xwma);

        var info = XwmaParser.Parse(stream);
        var profile = Wma2DecoderProfile.FromFile(info);

        Assert.Equal(9, profile.ByteOffsetBits);
        Assert.Equal(12, profile.ReservoirBitOffsetFieldBits);
        Assert.Equal(20, profile.SuperframeHeaderBits);
    }

    static byte[] CreateMinimalXwma(
        ushort channels,
        uint sampleRate,
        uint averageBytesPerSecond,
        ushort blockAlign,
        uint decodedBytes)
    {
        byte[] data = new byte[blockAlign];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        uint riffSize = 4 +
            8 + 18 +
            8 + 4 +
            8 + (uint)data.Length;

        writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        writer.Write(riffSize);
        writer.Write(new[] { (byte)'X', (byte)'W', (byte)'M', (byte)'A' });

        writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        writer.Write(18u);
        writer.Write((ushort)0x0161);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(averageBytesPerSecond);
        writer.Write(blockAlign);
        writer.Write((ushort)16);
        writer.Write((ushort)0);

        writer.Write(new[] { (byte)'d', (byte)'p', (byte)'d', (byte)'s' });
        writer.Write(4u);
        writer.Write(decodedBytes);

        writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        writer.Write((uint)data.Length);
        writer.Write(data);

        return stream.ToArray();
    }

}
