using Adk.Gui.Controls;
using Adk.Gui.Core;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class WindowChromeTests
{
    static Button Caption(Window window, string kind) =>
        DialogHarness.Nodes(window).OfType<Button>().First(button => button.StyleClass == "caption-button " + kind);

    static Window Dialog(params (string Key, object Value)[] _) => new Window
    {
        Title = "Settings", Width = 300, Height = 200, Content = new TextBlock { Text = "Body" }
    };

    [Theory]
    [InlineData(CaptionButtonsPlacement.Left)]
    [InlineData(CaptionButtonsPlacement.Right)]
    public void CaptionButtonsFollowTheSystemSideMirroredAroundACenteredTitle(CaptionButtonsPlacement placement)
    {
        var previous = WindowPlatforms.CaptionButtons;
        WindowPlatforms.CaptionButtons = placement;
        try
        {
            using var harness = new DialogHarness();
            var window = harness.Show(Dialog());
            Assert.Equal(new RectangleF(106, 156, 300, 200), window.FrameBounds);
            var close = Caption(window, "close");
            var maximize = Caption(window, "maximize");
            var minimize = Caption(window, "minimize");
            var frame = window.FrameBounds;
            if (placement == CaptionButtonsPlacement.Left)
            {
                Assert.True(close.Bounds.X < maximize.Bounds.X && maximize.Bounds.X < minimize.Bounds.X);
                Assert.True(close.Bounds.X - frame.X < 16);
            }
            else
            {
                Assert.True(minimize.Bounds.X < maximize.Bounds.X && maximize.Bounds.X < close.Bounds.X);
                Assert.True(frame.Right - close.Bounds.Right < 16);
            }
            float edgeGap = placement == CaptionButtonsPlacement.Left ? close.Bounds.X - frame.X : frame.Right - close.Bounds.Right;
            Assert.Equal(9, edgeGap, 1);
            var title = DialogHarness.Find<TextBlock>(window, block => block.Text == "Settings");
            Assert.Equal(frame.Center.X, title.Bounds.Center.X, 1);
            Assert.False(minimize.IsEffectivelyEnabled);
            var body = DialogHarness.Find<TextBlock>(window, block => block.Text == "Body");
            Assert.True(body.Bounds.Y >= frame.Y + 32);
        }
        finally
        {
            WindowPlatforms.CaptionButtons = previous;
        }
    }

    [Fact]
    public void MaximizeFillsTheOwnerAndRestoreReturns()
    {
        using var harness = new DialogHarness();
        var window = harness.Show(Dialog());
        harness.Click(Caption(window, "maximize"));
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal(new RectangleF(0, 0, 512, 512), window.FrameBounds);
        harness.Click(Caption(window, "maximize"));
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(300, window.FrameBounds.Width, 1);
    }

    [Fact]
    public void CloseClosesAndHoverSwapsTheGlyphForATintedDisc()
    {
        using var textures = new FakeGeneratedTextures();
        using var harness = new DialogHarness();
        var window = harness.Show(Dialog());
        var close = Caption(window, "close");
        harness.Draw();
        harness.Draw();
        harness.Host.MovePointer(close.Bounds.Center);
        harness.Draw();
        var hovered = harness.Draw().Single(s => s.Type == SpriteType.TEXTURE && Near(s.Position, close.Bounds.Center));
        Assert.Equal(DialogHarness.Error, hovered.Color);
        harness.Host.MovePointer(new Vector2(5, 5));
        harness.Draw();
        var resting = harness.Draw().Single(s => s.Type == SpriteType.TEXTURE && Near(s.Position, close.Bounds.Center));
        Assert.Equal(DialogHarness.OnSurface, resting.Color);
        Assert.NotEqual(hovered.Data, resting.Data);

        harness.Click(close);
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void ExtendedClientAreaPutsContentUnderTheCaptionButtonsWithoutATitle()
    {
        using var harness = new DialogHarness();
        var window = Dialog();
        window.ExtendClientAreaToDecorationsHint = true;
        harness.Show(window);
        Assert.DoesNotContain("Settings", harness.Texts());
        Assert.Equal(window.FrameBounds.Y, DialogHarness.Find<TextBlock>(window, block => block.Text == "Body").Bounds.Y, 1);
        Assert.True(Caption(window, "close").Visible);
    }

    [Fact]
    public void FullScreenAndUndecoratedWindowsHaveNoChrome()
    {
        using var harness = new DialogHarness();
        var full = Dialog();
        full.WindowState = WindowState.FullScreen;
        harness.Show(full);
        Assert.False(Caption(full, "close").Visible);
        Assert.Equal(new RectangleF(0, 0, 512, 512), full.FrameBounds);
        full.Close();

        var bare = Dialog();
        bare.WindowDecorations = WindowDecorations.None;
        harness.Show(bare);
        Assert.False(Caption(bare, "close").Visible);
        Assert.Equal(300, bare.FrameBounds.Width, 1);
    }

    [Fact]
    public void TheDiscIsTheGlyphCutOutOfACircle()
    {
        using var textures = new FakeGeneratedTextures();
        using var harness = new DialogHarness();
        var window = harness.Show(Dialog());
        var close = Caption(window, "close");
        harness.Draw();
        harness.Host.MovePointer(close.Bounds.Center);
        harness.Draw();
        harness.Draw();

        var glyph = FakeGeneratedTextures.Textures.Single(t => t.Key.StartsWith("AdkCaption") && t.Key.Contains("_Close#")).Value;
        var disc = FakeGeneratedTextures.Textures.Single(t => t.Key.Contains("_CloseDisc#")).Value;
        Assert.Equal(255, Alpha(glyph, .5f, .5f));
        Assert.Equal(0, Alpha(glyph, .1f, .1f));
        Assert.Equal(0, Alpha(disc, .5f, .5f));
        Assert.Equal(255, Alpha(disc, .5f, .15f));
        Assert.Equal(0, Alpha(disc, .02f, .02f));
    }

    static int Alpha((Vector2I Size, byte[] Pixels) texture, float u, float v)
    {
        int x = (int)(u * texture.Size.X), y = (int)(v * texture.Size.Y);
        return texture.Pixels[(y * texture.Size.X + x) * 4 + 3];
    }

    static bool Near(Vector2? position, Vector2 center) =>
        position.HasValue && Vector2.Distance(position.Value, center) < 1f;
}
