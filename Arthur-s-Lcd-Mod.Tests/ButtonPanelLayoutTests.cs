using LcdMod.Common.Layout;
using LcdMod.Common.Config.Components;

namespace Arthur_s_Lcd_Mod.Tests;

public class ButtonPanelLayoutTests
{
    [Fact]
    public void DefaultCountUsesAutomaticSentinel()
    {
        Assert.Equal(-1, ButtonPanelLayout.DefaultButtonCount);
        Assert.Equal(-1, new ButtonPanelConfigComponent().ButtonCount);
    }

    [Fact]
    public void AutomaticLayoutFillsSurfaceAtPreferredSize()
    {
        var layout = ButtonPanelLayout.Create(
            ButtonPanelLayout.AutomaticButtonCount,
            512f,
            200f,
            96f,
            8f);

        Assert.Equal(4, layout.ButtonCount);
        Assert.Equal(4, layout.Columns);
        Assert.Equal(1, layout.Rows);
        Assert.Equal(96f, layout.ButtonSize);
    }

    [Fact]
    public void MaximumCountUsesCompleteMinimumSizeGrid()
    {
        const float width = 512f;
        const float height = 472f;
        const float spacing = 3f;
        var expectedColumns = (int)Math.Floor(
            width / (ButtonPanelLayout.MinimumButtonSizePixels + spacing));
        var expectedRows = (int)Math.Floor(
            height / (ButtonPanelLayout.MinimumButtonSizePixels + spacing));
        var maximum = ButtonPanelLayout.GetMaximumButtonCount(512f, 472f, 3f);
        var layout = ButtonPanelLayout.Create(maximum, 512f, 472f, 92f, 3f);

        Assert.Equal(expectedColumns * expectedRows, maximum);
        Assert.Equal(maximum, layout.Columns * layout.Rows);
        Assert.True(layout.ButtonSize >= ButtonPanelLayout.MinimumButtonSizePixels);
    }

    [Fact]
    public void RequestedCountSnapsToCompleteGrid()
    {
        var maximum = ButtonPanelLayout.GetMaximumButtonCount(512f, 472f, 3f);
        var count = ButtonPanelLayout.NormalizeButtonCount(maximum - 1, 512f, 472f, 3f);
        var layout = ButtonPanelLayout.Create(count, 512f, 472f, 92f, 3f);

        Assert.InRange(count, 1, maximum);
        Assert.Equal(count, layout.Columns * layout.Rows);
    }

    [Fact]
    public void LayoutPrefersSquareGridThenScreenOrientation()
    {
        var layout = ButtonPanelLayout.Create(12, 512f, 200f, 92f, 3f);

        Assert.Equal(6, layout.Columns);
        Assert.Equal(2, layout.Rows);
        Assert.Equal(12, layout.ButtonCount);
    }

    [Fact]
    public void SquareGridWinsOverLargerButtonsWhenBothFit()
    {
        var layout = ButtonPanelLayout.Create(16, 1024f, 300f, 96f, 8f);

        Assert.Equal(4, layout.Columns);
        Assert.Equal(4, layout.Rows);
    }

    [Fact]
    public void LayoutDistributesCellsAndKeepsMinimumInternalMargin()
    {
        const float spacing = 8f;
        var layout = ButtonPanelLayout.Create(12, 512f, 300f, 96f, spacing);

        Assert.Equal(512f, layout.Width, 3);
        Assert.Equal(300f, layout.Height, 3);
        Assert.Equal(512f / layout.Columns, layout.CellWidth, 3);
        Assert.Equal(300f / layout.Rows, layout.CellHeight, 3);
        Assert.True(layout.HorizontalSpacing >= spacing);
        Assert.True(layout.VerticalSpacing >= spacing);
    }

    [Fact]
    public void ConfiguredButtonsClampRenderedGridAboveRequestedCount()
    {
        var layout = ButtonPanelLayout.Create(4, 512f, 300f, 96f, 8f, 11);

        Assert.Equal(12, layout.ButtonCount);
        Assert.Equal(4, layout.Columns);
        Assert.Equal(3, layout.Rows);
    }

    [Fact]
    public void ConfiguredButtonsBeyondSurfaceCapacityRemainRenderableByScrolling()
    {
        var layout = ButtonPanelLayout.Create(4, 512f, 300f, 96f, 8f, 40);

        Assert.True(layout.ButtonCount >= 40);
        Assert.True(layout.Height > 300f);
        Assert.True(layout.ButtonSize >= ButtonPanelLayout.MinimumButtonSizePixels);
        Assert.True(layout.HorizontalSpacing >= 8f);
        Assert.True(layout.VerticalSpacing >= 8f);
    }

    [Fact]
    public void ExponentialSliderMapsBothEndpoints()
    {
        const float width = 512f;
        const float height = 472f;
        const float spacing = 3f;
        var maximum = ButtonPanelLayout.GetMaximumButtonCount(width, height, spacing);

        Assert.Equal(1, ButtonPanelLayout.FromSlider(0f, width, height, spacing));
        Assert.Equal(maximum, ButtonPanelLayout.FromSlider(1f, width, height, spacing));
        Assert.Equal(0f, ButtonPanelLayout.ToSlider(1, width, height, spacing));
        Assert.Equal(1f, ButtonPanelLayout.ToSlider(maximum, width, height, spacing));
    }

    [Fact]
    public void PreferredSizeCannotReduceButtonsBelowMinimum()
    {
        var layout = ButtonPanelLayout.Create(16, 512f, 512f, 8f, 1f);

        Assert.True(layout.ButtonSize >= ButtonPanelLayout.MinimumButtonSizePixels);
    }
}
