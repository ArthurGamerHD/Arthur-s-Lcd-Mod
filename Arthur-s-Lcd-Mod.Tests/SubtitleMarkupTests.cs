using LcdMod.Client.Video;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Tests;

public class SubtitleMarkupTests
{
    static string Visible(List<SubtitleSpan> spans) =>
        string.Concat(spans.Select(span => span.IsLineBreak ? "\n" : span.Text));

    [Fact]
    public void FontTagsNeverReachTheScreen()
    {
        var spans = SubtitleMarkup.Parse("<font face=\"Arial\" size=\"22\" color=\"#ffff00\">Hello</font> there");

        Assert.Equal("Hello there", Visible(spans));
        Assert.Equal("Arial", spans[0].Face);
        Assert.Equal(new Color(255, 255, 0).PackedValue, spans[0].Color.Value.PackedValue);
        Assert.Equal(SubtitleSizeUnit.Pixels, spans[0].SizeUnit);
        Assert.Equal(22f, spans[0].Size);
        Assert.Null(spans[1].Face);
        Assert.Null(spans[1].Color);
    }

    [Theory]
    [InlineData("3", 1f)]
    [InlineData("7", 3f)]
    [InlineData("+1", 18f / 16f)]
    [InlineData("-2", 10f / 16f)]
    public void HtmlFontSizesAreRelativeToTheNormalSize(string size, float factor)
    {
        var span = SubtitleMarkup.Parse("<font size=" + size + ">x</font>")[0];

        Assert.Equal(SubtitleSizeUnit.Relative, span.SizeUnit);
        Assert.Equal(factor, span.Size, 4);
    }

    [Fact]
    public void PixelSizeWithUnitIsPixels()
    {
        var span = SubtitleMarkup.Parse("<font size='5px'>x</font>")[0];

        Assert.Equal(SubtitleSizeUnit.Pixels, span.SizeUnit);
        Assert.Equal(5f, span.Size);
    }

    [Fact]
    public void NestedStylesUnwindOnClose()
    {
        var spans = SubtitleMarkup.Parse("<i>a<b>b</b>c</i>d");

        Assert.Equal("abcd", Visible(spans));
        Assert.True(spans[0].Italic && !spans[0].Bold);
        Assert.True(spans[1].Italic && spans[1].Bold);
        Assert.True(spans[2].Italic && !spans[2].Bold);
        Assert.False(spans[3].Italic);
    }

    [Fact]
    public void UnknownTagsAndAssOverridesAreDropped()
    {
        Assert.Equal("Line one\nLine two", Visible(SubtitleMarkup.Parse("{\\an8}<c.yellow>Line one</c><br/>Line two")));
        Assert.Equal("a\nb", Visible(SubtitleMarkup.Parse("a\\Nb")));
    }

    [Fact]
    public void LiteralTextSurvives()
    {
        Assert.Equal("1 < 2 & 3 > 2 <3", Visible(SubtitleMarkup.Parse("1 < 2 &amp; 3 &gt; 2 <3")));
        Assert.Equal("café & co", Visible(SubtitleMarkup.Parse("caf&#233; & co")));
    }

    [Theory]
    [InlineData("red", 255, 0, 0, 255)]
    [InlineData("#0f0", 0, 255, 0, 255)]
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44)]
    [InlineData("&H000000FF&", 255, 0, 0, 255)]
    public void ParsesSubtitleColors(string text, int r, int g, int b, int a)
    {
        Assert.True(SubtitleMarkup.TryParseColor(text, out var color));
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b, (byte)a).PackedValue, color.PackedValue);
    }

    [Fact]
    public void TrackWideHugePixelSizeShowsAtNormalSize()
    {
        var sizes = new SubtitleSizeReference();
        var cue = SubtitleMarkup.Parse("<font face=\"sans-serif\" size=\"142\">U.S.S. Kelvin, go for Starfleet Base.</font>")[0];

        Assert.Equal("U.S.S. Kelvin, go for Starfleet Base.", cue.Text);
        Assert.Equal(SubtitleSizeUnit.Pixels, cue.SizeUnit);
        Assert.Equal(1f, sizes.Factor(cue.Size));
        Assert.Equal(1f, sizes.Factor(142f));
        Assert.Equal(.5f, sizes.Factor(71f));
        Assert.Equal(2f, sizes.Factor(1000f));

        sizes.Reset();
        Assert.Equal(1f, sizes.Factor(20f));
    }

    [Fact]
    public void BadColorKeepsTheDefault()
    {
        Assert.Null(SubtitleMarkup.Parse("<font color=\"notacolor\">x</font>")[0].Color);
    }
}
