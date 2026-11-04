using Acrux.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Tests;

public class AdkTransformTests
{
    [Theory]
    [InlineData("translate(10px, -20px)", 10f, -20f)]
    [InlineData("translate(10px)", 10f, 0f)]
    [InlineData("translateX(4.5px)", 4.5f, 0f)]
    [InlineData("translateY(12px)", 0f, 12f)]
    [InlineData("translateY(0)", 0f, 0f)]
    public void ParsesAvaloniaTranslateFunctions(string text, float x, float y)
    {
        var transform = Assert.IsType<TranslateTransform>(Transform.Parse(text));

        Assert.Equal(x, transform.X);
        Assert.Equal(y, transform.Y);
        Assert.Equal(Vector2.One, transform.ScaleFactor);
        Assert.Equal(0f, transform.RotationRadians);
    }

    [Theory]
    [InlineData("translate(10)")]
    [InlineData("translateY(1px, 2px)")]
    [InlineData("translate(1px, 2px, 3px)")]
    public void RejectsWhatAvaloniaRejects(string text)
    {
        Assert.Throws<FormatException>(() => Transform.Parse(text));
    }

    [Fact]
    public void ScaleAndRotateCarryNoOffset()
    {
        Assert.Equal(Vector2.Zero, Transform.Parse("scale(0.95)").Offset);
        Assert.Equal(Vector2.Zero, Transform.Parse("rotate(90deg)").Offset);
    }
}
