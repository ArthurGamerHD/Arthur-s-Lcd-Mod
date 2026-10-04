using Adk.Gui.Controls;
using Adk.Gui.Layout;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// Avalonia's Grid reports the size its content needs, star definitions included; stars only fill at arrange.
public sealed class GridMeasureTests
{
    static Border Box(float width, float height, int column = 0, int row = 0, int columnSpan = 1)
    {
        var box = new Border { Width = width, Height = height };
        box.SetGridPlacement(column, row, columnSpan, 1);
        return box;
    }

    [Fact]
    public void StarRowsReportTheirContentNotTheAvailableHeight()
    {
        var grid = new Grid();
        grid.SetRowDefinitions(GridLength.Auto, GridLength.Star, GridLength.Auto);
        grid.AddChild(Box(10, 20, row: 0));
        grid.AddChild(Box(10, 30, row: 1));
        grid.AddChild(Box(10, 10, row: 2));

        Assert.Equal(60, grid.Measure(new Vector2(200, 300)).Y, 1);

        grid.Arrange(new RectangleF(0, 0, 200, 300));
        Assert.Equal(290, grid.VisualChildren[2].Bounds.Y, 1);
    }

    [Fact]
    public void StarColumnsSumTheirContentAndSpansGrowThem()
    {
        var grid = new Grid();
        grid.SetColumnDefinitions(GridLength.Star, new GridLength(2, GridUnitType.Star));
        grid.AddChild(Box(50, 10, column: 0));
        grid.AddChild(Box(40, 10, column: 1));
        Assert.Equal(90, grid.Measure(new Vector2(400, 100)).X, 1);

        grid.AddChild(Box(150, 10, column: 0, columnSpan: 2));
        Assert.Equal(150, grid.Measure(new Vector2(400, 100)).X, 1);
    }
}
