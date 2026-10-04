using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class HyperlinkStyleTests
{
    static readonly Color Hover = new(10, 200, 90);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHyperlinkIsUnderlinedInItsTextColorWithoutAnyOtherChrome(bool hovered)
    {
        using var harness = new DialogHarness(400, 300);
        var link = new Button
        {
            StyleClass = "map-info-action hyperlink",
            Content = new TextBlock { StyleClass = "map-info-link", Text = "Position: 12 km" },
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Vector4(40f)
        };
        var view = new Grid { Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Maps.xaml") };
        view.Resources.Add("onAccentColor", Hover);
        view.AddChild(link);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Draw();
        harness.Host.MovePointer(hovered ? link.Bounds.Center : new Vector2(390, 290));
        var bounds = link.Bounds;

        var sprites = harness.Draw();
        Color textColor = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "Position: 12 km").Color!.Value;
        Assert.Equal(hovered ? Hover : DialogHarness.OnSurface, textColor);
        var chrome = sprites
            .Where(s => s.Type == SpriteType.TEXTURE && s.Color.HasValue && s.Color.Value.A > 0 && s.Position.HasValue && s.Size.HasValue)
            .Where(s => bounds.Contains(s.Position!.Value))
            .ToList();

        Assert.NotEmpty(chrome);
        Assert.All(chrome, s =>
        {
            Assert.True(s.Size!.Value.Y <= 2.5f, $"only an underline may be drawn, found a {s.Size.Value} sprite");
            Assert.True(s.Position!.Value.Y >= bounds.Bottom - 3f, "the underline sits at the bottom of the link");
            Assert.Equal(textColor, s.Color!.Value);
        });
    }
}
