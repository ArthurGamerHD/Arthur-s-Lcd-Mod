using Adk.Gui.Controls;
using Adk.Gui.Markup;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class NamedColorView : UserControl
{
    public NamedColorView()
    {
        InitializeComponent();
    }

    public TextBlock Named => NamedText;
    public Border Short => ShortBorder;
}

public sealed class NamedColorTests
{
    [Theory]
    [InlineData("Red", 255, 0, 0, 255)]
    [InlineData("cornflowerblue", 100, 149, 237, 255)]
    [InlineData(" Transparent ", 255, 255, 255, 0)]
    [InlineData("#F00", 255, 0, 0, 255)]
    [InlineData("#8F00", 255, 0, 0, 136)]
    [InlineData("#102030", 16, 32, 48, 255)]
    [InlineData("#80102030", 16, 32, 48, 128)]
    public void RuntimeParsesAvaloniaColorLiterals(string literal, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new Color(r, g, b, a), new PropertyConverter().Convert(literal, typeof(Color)));
    }

    [Fact]
    public void RuntimeRejectsUnknownNames()
    {
        Assert.Throws<FormatException>(() => new PropertyConverter().Convert("NotAColor", typeof(Color)));
    }

    [Fact]
    public void StyleSettersAcceptNamedColors()
    {
        var sheet = StyleSheet.Load("""
            <Styles xmlns="https://github.com/ArthurGamerHD/Adk">
              <Style Selector="TextBlock.named">
                <Setter Property="Foreground" Value="Orange" />
              </Style>
            </Styles>
            """);
        var text = new TextBlock { StyleClass = "named", Styles = sheet };
        text.Measure(new Vector2(100, 100));
        Assert.Equal(new Color(255, 165, 0), text.Foreground);
    }

    [Fact]
    public void CompiledViewsAcceptNamedAndShortHexColors()
    {
        using var view = new NamedColorView();
        Assert.Equal(new Color(100, 149, 237), view.Named.Foreground);
        Assert.Equal(new Color(255, 0, 0, 136), view.Short.Background.FallbackColor);
    }
}
