using Adk.Gui.Controls;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class CheckBoxStyleTests
{
    [Fact]
    public void FluentCheckBoxTemplateRendersCheckedAndIndeterminateGlyphs()
    {
        using var harness = new DialogHarness();
        var box = new CheckBox
        {
            IsThreeState = true,
            Content = new TextBlock { Text = "Option" }
        };
        harness.Show(new Window { Width = 300, Height = 120, Content = box });

        var indicator = Assert.IsType<Border>(box.GetTemplateChild("PART_Indicator"));
        var check = Assert.IsType<Adk.Gui.Controls.Shapes.Path>(box.GetTemplateChild("PART_CheckGlyph"));
        var dash = Assert.IsType<Adk.Gui.Controls.Shapes.Path>(box.GetTemplateChild("PART_IndeterminateGlyph"));
        Assert.Null(check.Stroke);
        Assert.Null(dash.Stroke);

        box.IsChecked = true;
        harness.Draw();
        Assert.Equal(DialogHarness.Accent, indicator.Background.FallbackColor);
        Assert.NotNull(check.Stroke);
        Assert.Null(dash.Stroke);

        box.IsChecked = null;
        harness.Draw();
        Assert.Null(check.Stroke);
        Assert.NotNull(dash.Stroke);
    }
}
