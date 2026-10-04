using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class VirtualizingStackPanelTests
{
    sealed class Row(string text) : LcdMod.Common.Mvvm.ObservableObject
    {
        public string Text { get; } = text;
    }

    static TextBlock BuildRow(object item)
    {
        var block = new TextBlock { Height = 20 };
        block.Bind<Row, string>(row => row.Text, value => block.Text = value, nameof(Row.Text));
        return block;
    }

    sealed class Fixture : IDisposable
    {
        public readonly ScrollViewer Scroll;
        public readonly ItemsControl Items;
        public readonly LcdGuiHost Host;

        public Fixture(int count)
        {
            Items = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(() => new VirtualizingStackPanel()),
                ItemTemplate = new DataTemplate(BuildRow),
                ItemsSource = Enumerable.Range(0, count).Select(i => new Row("Item " + i)).ToList()
            };
            Scroll = new ScrollViewer { Child = Items };
            Host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), Scroll);
            Host.Arrange(new RectangleF(0, 0, 300, 200));
        }

        public List<string> DrawnTexts()
        {
            var sprites = new List<MySprite>();
            Host.Draw(sprites);
            return sprites.Where(s => s.Type == SpriteType.TEXT).Select(s => s.Data).ToList();
        }

        public int RealizedCount => Items.ItemsPanelRoot.VisualChildren.Count(child => child.Visible);

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public void OnlyTheViewportRowsAreRealized()
    {
        using var f = new Fixture(1000);
        var texts = f.DrawnTexts();
        Assert.Contains("Item 0", texts);
        Assert.DoesNotContain("Item 999", texts);
        Assert.InRange(f.RealizedCount, 1, 20);
        Assert.InRange(f.Items.ItemsPanelRoot.VisualChildren.Count, 1, 20);
        Assert.Equal(1000 * 20, f.Items.DesiredSize.Y, 1);
    }

    [Fact]
    public void ScrollingRecyclesRowsForTheNewRange()
    {
        using var f = new Fixture(1000);
        f.DrawnTexts();
        int created = f.Items.ItemsPanelRoot.VisualChildren.Count;
        f.Scroll.ScrollToEnd();
        var texts = f.DrawnTexts();
        Assert.Contains("Item 999", texts);
        Assert.DoesNotContain("Item 0", texts);
        Assert.Equal(created, f.Items.ItemsPanelRoot.VisualChildren.Count);
    }

    [Fact]
    public void ReplacingTheItemsRebindsVisibleRowsInPlace()
    {
        using var f = new Fixture(1000);
        f.DrawnTexts();
        var before = f.Items.ItemsPanelRoot.VisualChildren.ToList();
        f.Items.ItemsSource = new List<Row> { new("Filtered A"), new("Filtered B") };
        f.Host.Arrange(new RectangleF(0, 0, 300, 200));
        var texts = f.DrawnTexts();
        Assert.Equal(new[] { "Filtered A", "Filtered B" }, texts);
        Assert.All(f.Items.ItemsPanelRoot.VisualChildren, child => Assert.Contains(child, before));
    }
}
