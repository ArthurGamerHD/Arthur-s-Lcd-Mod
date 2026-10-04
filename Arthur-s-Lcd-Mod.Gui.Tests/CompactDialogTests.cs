using Adk.Gui.Controls;
using Adk.Gui.Controls.Chrome;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// Pre-migration dialogs on very short screens (e.g. 512x100) filled the screen with no title bar or margin.
[Collection(LcdTextureHooks.Name)]
public sealed class CompactDialogTests
{
    [Fact]
    public void AShortScreenShowsDialogsCompactUntilItGrows()
    {
        using var harness = new DialogHarness(512, 100);
        var body = new TextBlock { Text = "Body" };
        var dialog = harness.Show(new Window { Title = "Settings", Width = 400, Height = 300, Padding = new Vector4(14), Content = body });
        harness.Draw();

        Assert.Equal(WindowState.Maximized, dialog.WindowState);
        Assert.True(dialog.ExtendClientAreaToDecorationsHint);
        Assert.Equal(harness.Owner.Bounds, dialog.Bounds);
        Assert.DoesNotContain(harness.Texts(), text => text == "Settings");
        Assert.True(body.Bounds.Y - dialog.Bounds.Y < 8, "content starts at the top: " + body.Bounds);
        var close = DialogHarness.Nodes(dialog).OfType<CaptionButton>().Single(b => b.Visible && b.Glyph == CaptionGlyph.Close);
        Assert.True(body.Bounds.Right <= close.Bounds.X + .5f || body.Bounds.X >= close.Bounds.Right - .5f,
            "content stays beside the close button");

        harness.Host.Arrange(new RectangleF(0, 0, 512, 512));
        harness.Draw();
        Assert.Equal(WindowState.Normal, dialog.WindowState);
        Assert.False(dialog.ExtendClientAreaToDecorationsHint);
        Assert.Equal(new Vector4(14), dialog.Padding);
        Assert.Contains("Settings", harness.Texts());

        harness.Click(DialogHarness.Nodes(dialog).OfType<CaptionButton>().Single(b => b.Visible && b.Glyph == CaptionGlyph.Close));
        Assert.False(dialog.IsOpen);
    }
}
