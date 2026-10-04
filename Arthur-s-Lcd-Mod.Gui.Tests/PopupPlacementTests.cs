using Adk.Gui.Controls;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class PopupPlacementTests
{
    const float ScreenWidth = 500f;
    const float ScreenHeight = 400f;

    static (DialogHarness Harness, Button Anchor, Border Content) Open(HorizontalAlignment horizontal,
        VerticalAlignment vertical, PlacementMode placement)
    {
        var harness = new DialogHarness(ScreenWidth, ScreenHeight);
        var content = new Border { Width = 160, Height = 90, Background = new SolidColorBrush(Color.Red) };
        var anchor = new Button
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Flyout = new Popup { Child = content, Placement = placement, HorizontalOffset = 8 }
        };
        var view = new Grid();
        view.AddChild(anchor);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, ScreenWidth, ScreenHeight));
        harness.Click(anchor);
        return (harness, anchor, content);
    }

    static void AssertOnScreen(RectangleF bounds)
    {
        Assert.True(bounds.X >= 0f && bounds.Y >= 0f && bounds.Right <= ScreenWidth && bounds.Bottom <= ScreenHeight,
            $"the popup {bounds} must stay inside the {ScreenWidth}x{ScreenHeight} screen");
    }

    [Fact]
    public void ARightPlacedPopupThatDoesNotFitFlipsToTheLeftOfItsAnchor()
    {
        var (harness, anchor, content) = Open(HorizontalAlignment.Right, VerticalAlignment.Center, PlacementMode.Right);
        using (harness)
        {
            AssertOnScreen(content.Bounds);
            Assert.Equal(anchor.Bounds.X - 8f, content.Bounds.Right, 1);
            Assert.Equal(anchor.Bounds.Center.Y, content.Bounds.Center.Y, 1);
        }
    }

    [Fact]
    public void ARightPlacedPopupWithRoomStaysOnTheRight()
    {
        var (harness, anchor, content) = Open(HorizontalAlignment.Left, VerticalAlignment.Center, PlacementMode.Right);
        using (harness)
            Assert.Equal(anchor.Bounds.Right + 8f, content.Bounds.X, 1);
    }

    [Fact]
    public void APopupPastTheBottomEdgeSlidesUpIntoTheScreen()
    {
        var (harness, anchor, content) = Open(HorizontalAlignment.Left, VerticalAlignment.Bottom, PlacementMode.Right);
        using (harness)
        {
            AssertOnScreen(content.Bounds);
            Assert.Equal(ScreenHeight, content.Bounds.Bottom, 1);
            Assert.Equal(anchor.Bounds.Right + 8f, content.Bounds.X, 1);
        }
    }

    [Fact]
    public void ABottomPlacedPopupAtTheRightEdgeSlidesLeftInsteadOfOverflowing()
    {
        var (harness, _, content) = Open(HorizontalAlignment.Right, VerticalAlignment.Top, PlacementMode.Bottom);
        using (harness)
        {
            AssertOnScreen(content.Bounds);
            Assert.Equal(ScreenWidth, content.Bounds.Right, 1);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALightDismissClickOnlyReachesTheControlBelowWithPassThrough(bool passThrough)
    {
        using var harness = new DialogHarness(ScreenWidth, ScreenHeight);
        int clicks = 0;
        var target = new Button
        {
            Width = 60, Height = 40, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Clicked = () => clicks++
        };
        var popup = new Popup
        {
            Child = new Border { Width = 100, Height = 50 },
            PlacementRect = new RectangleF(20, 20, 10, 10),
            IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = passThrough
        };
        var view = new Grid();
        view.AddChild(target);
        view.AddChild(popup);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, ScreenWidth, ScreenHeight));
        popup.Open();

        harness.Click(target);

        Assert.False(popup.IsOpen);
        Assert.Equal(passThrough ? 1 : 0, clicks);
    }
}
