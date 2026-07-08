#if EXPERIMENTAL
using LcdMod.Client.Audio;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class GameAudioPcmLoaderTests
{
    [Fact]
    public void TryRead_UsesWaveFormEvenWhenExtensionIsXwm()
    {
        byte[] waveBytes = CreatePcmWaveBytes();
        using var stream = new MemoryStream(waveBytes);
        using var reader = new BinaryReader(stream);

        bool read = GameAudioPcmLoader.TryRead(
            reader,
            "Audio/FakeWaveWithXwmExtension.xwm",
            out var pcm,
            out var failureReason,
            out var containerKind);

        Assert.True(read, failureReason);
        Assert.Equal(GameAudioContainerKind.PcmWave, containerKind);
        Assert.NotNull(pcm);
        Assert.Equal(24000u, pcm.SampleRate);
        Assert.Equal(24000u, pcm.SourceSampleRate);
        Assert.Equal((ushort)1, pcm.Channels);
        Assert.Equal((ushort)16, pcm.BitsPerSample);
        Assert.False(pcm.WasResampled);
        Assert.Equal(new byte[] { 0x01, 0x00, 0xff, 0x7f }, pcm.Samples);
    }

    [Fact]
    public void TryRead_ResamplesWaveFormInsideXwmTo24KhzMonoPcm()
    {
        byte[] waveBytes = CreatePcmWaveBytes(
            48000u,
            1,
            new short[] { 1000, 2000, 3000, 4000 });
        using var stream = new MemoryStream(waveBytes);
        using var reader = new BinaryReader(stream);

        bool read = GameAudioPcmLoader.TryRead(
            reader,
            "Audio/Fake48KhzWaveWithXwmExtension.xwm",
            out var pcm,
            out var failureReason,
            out var containerKind);

        Assert.True(read, failureReason);
        Assert.Equal(GameAudioContainerKind.PcmWave, containerKind);
        Assert.NotNull(pcm);
        Assert.Equal(24000u, pcm.SampleRate);
        Assert.Equal(48000u, pcm.SourceSampleRate);
        Assert.Equal((ushort)1, pcm.Channels);
        Assert.Equal((ushort)1, pcm.SourceChannels);
        Assert.Equal((ushort)16, pcm.BitsPerSample);
        Assert.True(pcm.WasResampled);
        Assert.False(pcm.WasDownmixedToMono);
        Assert.Equal(new byte[] { 0xe8, 0x03, 0xb8, 0x0b }, pcm.Samples);
    }

    [Fact]
    public void TryRead_DownmixesStereoWaveTo24KhzMonoPcm()
    {
        byte[] waveBytes = CreatePcmWaveBytes(
            24000u,
            2,
            new short[] { 1000, 3000, -1000, -3000 });
        using var stream = new MemoryStream(waveBytes);
        using var reader = new BinaryReader(stream);

        bool read = GameAudioPcmLoader.TryRead(
            reader,
            "Audio/Stereo.wav",
            out var pcm,
            out var failureReason,
            out var containerKind);

        Assert.True(read, failureReason);
        Assert.Equal(GameAudioContainerKind.PcmWave, containerKind);
        Assert.NotNull(pcm);
        Assert.Equal(24000u, pcm.SampleRate);
        Assert.Equal(24000u, pcm.SourceSampleRate);
        Assert.Equal((ushort)1, pcm.Channels);
        Assert.Equal((ushort)2, pcm.SourceChannels);
        Assert.True(pcm.WasDownmixedToMono);
        Assert.False(pcm.WasResampled);
        Assert.Equal(new byte[] { 0xd0, 0x07, 0x30, 0xf8 }, pcm.Samples);
    }


    [Fact]
    public void TryReadInGameContent_UsesWavFallbackWhenDefinitionPathMissing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "lcdmod-audio-fallback-" + Guid.NewGuid().ToString("N"));
        string audioDir = Path.Combine(tempRoot, "Audio", "Arc", "weapon");
        Directory.CreateDirectory(audioDir);
        File.WriteAllBytes(Path.Combine(audioDir, "recharge.wav"), CreatePcmWaveBytes());

        var previousUtilities = Sandbox.ModAPI.MyAPIGateway.Utilities;
        Sandbox.ModAPI.MyAPIGateway.Utilities = new Sandbox.ModAPI.MyUtilities
        {
            GameContentRoot = tempRoot
        };

        try
        {
            bool read = GameAudioPcmLoader.TryReadInGameContent(
                "Audio/Arc/weapon/recharge.xwm",
                out var pcm,
                out var failureReason,
                out var containerKind,
                out var resolvedPath,
                out var usedWavFallback);

            Assert.True(read, failureReason);
            Assert.True(usedWavFallback);
            Assert.Equal("Audio/Arc/weapon/recharge.wav", resolvedPath.Replace('\\', '/'));
            Assert.Equal(GameAudioContainerKind.PcmWave, containerKind);
            Assert.NotNull(pcm);
            Assert.Equal(24000u, pcm.SampleRate);
        }
        finally
        {
            Sandbox.ModAPI.MyAPIGateway.Utilities = previousUtilities;
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void TryRead_RejectsNonRiffPayloadSafely()
    {
        using var stream = new MemoryStream(new byte[]
        {
            (byte)'n', (byte)'o', (byte)'t', (byte)'r',
            (byte)'i', (byte)'f', (byte)'f', (byte)'d',
            (byte)'a', (byte)'t', (byte)'a', (byte)'!'
        });
        using var reader = new BinaryReader(stream);

        bool read = GameAudioPcmLoader.TryRead(
            reader,
            "Audio/Broken.xwm",
            out var pcm,
            out var failureReason,
            out var containerKind);

        Assert.False(read);
        Assert.Null(pcm);
        Assert.Equal(GameAudioContainerKind.Unknown, containerKind);
        Assert.Contains("Expected RIFF audio container", failureReason);
    }

    static byte[] CreatePcmWaveBytes()
    {
        return CreatePcmWaveBytes(
            24000u,
            1,
            new short[] { 1, 32767 });
    }

    static byte[] CreatePcmWaveBytes(
        uint sampleRate,
        ushort channels,
        short[] interleavedSamples)
    {
        byte[] samples = new byte[interleavedSamples.Length * 2];
        for (int i = 0; i < interleavedSamples.Length; i++)
        {
            short sample = interleavedSamples[i];
            int offset = i * 2;
            samples[offset] = (byte)(sample & 0xff);
            samples[offset + 1] = (byte)((sample >> 8) & 0xff);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        ushort blockAlign = (ushort)(channels * 2);

        writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        writer.Write((uint)(4 + 8 + 16 + 8 + samples.Length));
        writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

        writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write((uint)(sampleRate * blockAlign));
        writer.Write(blockAlign);
        writer.Write((ushort)16);

        writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        writer.Write((uint)samples.Length);
        writer.Write(samples);

        return stream.ToArray();
    }
}
#endif
