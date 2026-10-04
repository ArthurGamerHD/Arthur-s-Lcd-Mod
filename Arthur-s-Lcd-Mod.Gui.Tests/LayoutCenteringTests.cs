using Adk.Gui.Controls;
using Adk.Gui.Controls.Chrome;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class LayoutCenteringTests
{
    [Fact]
    public void AFixedSizeChildWithDefaultAlignmentIsCenteredInItsSlotLikeAvalonia()
    {
        using var harness = new DialogHarness(400, 300);
        var image = new Image { Source = "LeftArrow", Width = 16, Height = 16 };
        var button = new Button { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Content = image };
        var view = new Grid();
        view.AddChild(button);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Draw();

        Assert.Equal(button.Bounds.Center.X, image.Bounds.Center.X, 1);
        Assert.Equal(button.Bounds.Center.Y, image.Bounds.Center.Y, 1);
    }

    [Fact]
    public void AClearButtonTextBoxShowsTheCloseGlyphOnlyWithTextAndClearsWhenClicked()
    {
        using var harness = new DialogHarness(400, 300);
        string changed = null;
        var box = new TextBox { StyleClass = "clearButton", VerticalAlignment = VerticalAlignment.Top, TextChanged = t => changed = t };
        var view = new Grid();
        view.AddChild(box);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Draw();
        Assert.DoesNotContain(DialogHarness.Nodes(box).OfType<CaptionButton>(), b => b.Visible);

        box.Text = "rotor";
        harness.Draw();
        var clear = DialogHarness.Nodes(box).OfType<CaptionButton>().Single(b => b.Visible);
        Assert.True(box.Bounds.Contains(clear.Bounds.Center), "the clear button sits inside the box");

        harness.Click(clear);

        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(string.Empty, changed);
    }
}
