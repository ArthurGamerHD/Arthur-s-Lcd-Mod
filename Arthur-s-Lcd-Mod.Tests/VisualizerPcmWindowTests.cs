using LcdMod.Client.Audio;

namespace Arthur_s_Lcd_Mod.Tests;

public class VisualizerPcmWindowTests
{
    [Fact]
    public void WindowTracksPlaybackPositionAndPadsStartWithSilence()
    {
        byte[] pcm = { 1, 0, 2, 0, 3, 0, 4, 0 };
        var output = new byte[8];
        VisualizerPcmWindow.Copy(pcm, 24000, 1, 2, 0, 2d / 24000, output);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 0, 2, 0 }, output);
        VisualizerPcmWindow.Copy(pcm, 24000, 1, 2, 0, 4d / 24000, output);
        Assert.Equal(pcm, output);
    }

    [Fact]
    public void AdjacentStreamChunksShareOnePlaybackWindow()
    {
        var output = new byte[8];
        VisualizerPcmWindow.Copy(new byte[] { 1, 0, 2, 0 }, 24000, 1, 2, 0, 4d / 24000, output);
        VisualizerPcmWindow.Copy(new byte[] { 3, 0, 4, 0 }, 24000, 1, 2, 2d / 24000, 4d / 24000, output);
        Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0, 4, 0 }, output);
    }

    [Fact]
    public void StereoDownmixAndSampleRateAreConverted()
    {
        byte[] pcm = { 10, 0, 30, 0, 20, 0, 40, 0 };
        var output = new byte[8];
        VisualizerPcmWindow.Copy(pcm, 12000, 2, 4, 0, 2d / 12000, output);
        Assert.Equal(new byte[] { 20, 0, 20, 0, 30, 0, 30, 0 }, output);
    }
}
