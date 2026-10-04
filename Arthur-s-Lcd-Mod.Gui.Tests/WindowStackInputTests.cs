using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// An FTUE hint opened while a dialog is up is drawn above the dialog, so it must also receive the clicks there.
[Collection(LcdTextureHooks.Name)]
public sealed class WindowStackInputTests
{
    [Fact]
    public void AHintOpenedAfterADialogTakesTheClicksWhereItIsDrawn()
    {
        using var harness = new DialogHarness(600, 600);
        int dialogClicks = 0, hintClicks = 0, cornerClicks = 0;
        var under = new Button { Content = "Under", Width = 300, Height = 200, Clicked = () => dialogClicks++ };
        var corner = new Button { Content = "Corner", Width = 60, Height = 30, Clicked = () => cornerClicks++,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var panel = new Grid();
        panel.AddChild(under);
        panel.AddChild(corner);
        harness.Show(new Window { Width = 500, Height = 400, Content = panel });

        var hintButton = new Button { Content = "Got it", Clicked = () => hintClicks++ };
        var hint = new Window { Width = 160, Height = 80, Content = hintButton };
        hint.Show(harness.Owner);
        harness.Draw();
        Assert.True(under.Bounds.Contains(hintButton.Bounds.Center), "the hint is drawn over the dialog's button");

        harness.Click(hintButton);
        Assert.Equal(1, hintClicks);
        Assert.Equal(0, dialogClicks);

        harness.Click(corner);
        Assert.Equal(1, cornerClicks);
    }
}
