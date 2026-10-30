using LcdMod.Client.Video;

namespace Arthur_s_Lcd_Mod.Tests;

public class SubtitleTrackTests
{
    const string Srt =
        "1\r\n00:01:18,955 --> 00:01:20,915\r\n<font face=\"sans-serif\" size=\"142\">U.S.S. Kelvin, go for Starfleet Base.</font>\r\n\r\n" +
        "2\r\n00:01:20,999 --> 00:01:23,876\r\nStarfleet Base, we've sent you\r\na transmission. Did you receive?\r\n";

    [Fact]
    public void ReadsSrtCuesWithMarkupAndLineBreaks()
    {
        var track = SubtitleTrack.Parse(Srt);

        Assert.Equal(2, track.Count);
        Assert.Null(track.TextAt(78.9));
        Assert.Equal("<font face=\"sans-serif\" size=\"142\">U.S.S. Kelvin, go for Starfleet Base.</font>", track.TextAt(79));
        Assert.Equal("Starfleet Base, we've sent you\na transmission. Did you receive?", track.TextAt(82));
        Assert.Null(track.TextAt(23.876 + 60));
    }

    [Fact]
    public void ReadsWebVttWithShortTimesAndCueSettings()
    {
        var track = SubtitleTrack.Parse("WEBVTT\n\nintro\n00:05.000 --> 00:07.500 line:90% align:center\n<v Kirk>Hello</v>\n");

        Assert.Equal(1, track.Count);
        Assert.Equal("<v Kirk>Hello</v>", track.TextAt(6));
        Assert.Null(track.TextAt(7.5));
    }

    [Fact]
    public void SurvivesTheTripToOtherPlayers()
    {
        var received = SubtitleTrack.FromBytes(SubtitleTrack.Parse(Srt).ToBytes());

        Assert.Equal(2, received.Count);
        Assert.Equal(SubtitleTrack.Parse(Srt).TextAt(79), received.TextAt(79));
        Assert.Equal(SubtitleTrack.Parse(Srt).TextAt(83.8), received.TextAt(83.8));
    }

    [Fact]
    public void SkipsBlocksWithoutValidTiming()
    {
        var track = SubtitleTrack.Parse("1\nnot a time --> either\nText\n\n2\n00:00:01,000 --> 00:00:02,000\nKept\n");

        Assert.Equal(1, track.Count);
        Assert.Equal("Kept", track.TextAt(1.5));
    }
}
