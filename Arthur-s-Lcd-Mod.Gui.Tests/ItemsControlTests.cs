using Adk.Gui.Collections.Reactive;
using LcdMod.Common.Mvvm;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class ItemsControlTests
{
    [Fact]
    public void ObservableSourceAddsMovesReplacesRemovesAndResetsWithoutManualRefresh()
    {
        var first = new object();
        var second = new object();
        var source = new ResettableCollection { first, second };
        int builds = 0;
        using var control = new ItemsControl
        {
            ItemTemplate = new DataTemplate(_ => { builds++; return new TextBlock(); }),
            ItemsSource = source
        };
        var firstCell = control.GetContainerAt(0);
        var secondCell = control.GetContainerAt(1);
        source.Move(0, 1);
        Assert.Same(firstCell, control.GetContainerAt(1));
        Assert.Same(secondCell, control.GetContainerAt(0));
        source.Add(new object());
        Assert.Equal(3, builds);
        source[2] = new object();
        Assert.Equal(4, builds);
        source.RemoveAt(0);
        Assert.Same(firstCell, control.GetContainerAt(0));
        Assert.Null(secondCell.Parent);
        source.ResetNotification();
        Assert.Equal(4, builds);
        Assert.Same(firstCell, control.GetContainerAt(0));
        source.Clear();
        Assert.Equal(0, control.ItemCount);
        Assert.Empty(control.ItemsPanelRoot.VisualChildren);
    }

    [Fact]
    public void ReplacingSourceAndDisposingDetachCollectionSubscriptions()
    {
        var previous = new ObservableList<object> { new object() };
        var next = new ObservableList<object> { new object() };
        var control = new ItemsControl { ItemsSource = previous };
        control.ItemsSource = next;
        var cell = control.GetContainerAt(0);
        previous.Add(new object());
        Assert.Equal(1, control.ItemCount);
        Assert.Same(cell, control.GetContainerAt(0));
        next.Add(new object());
        Assert.Equal(2, control.ItemCount);
        control.Dispose();
        next.Add(new object());
        Assert.Equal(0, control.ItemCount);
    }

    [Fact]
    public void SharedPanelTemplateBuildsIndependentPanelsAndReplacementRetainsCells()
    {
        var template = new ItemsPanelTemplate(() => new WrapPanel { ItemWidth = 80, ItemHeight = 30 });
        using var first = new ItemsControl { ItemsPanel = template, ItemsSource = new[] { "a", "b" } };
        using var second = new ItemsControl { ItemsPanel = template, ItemsSource = new[] { "c" } };
        Arrange(first); Arrange(second);
        Assert.NotSame(first.ItemsPanelRoot, second.ItemsPanelRoot);
        var cell = first.GetContainerAt(0);
        var oldPanel = first.ItemsPanelRoot;
        first.ItemsPanel = new ItemsPanelTemplate(() => new StackPanel());
        Arrange(first);
        Assert.IsType<StackPanel>(first.ItemsPanelRoot);
        Assert.Same(cell, first.GetContainerAt(0));
        Assert.Same(first.ItemsPanelRoot, cell.Parent);
        Assert.Null(oldPanel.Parent);
    }

    [Fact]
    public void RuntimePanelStyleSupportsTemplateBindingsAndStyleReplacement()
    {
        using var control = new ItemsControl { Width = 80, ItemsSource = new[] { "a", "b" } };
        control.Styles = StyleSheet.Load("""
            <Styles>
              <Style Selector="ItemsControl">
                <Setter Property="ItemsPanel">
                  <ItemsPanelTemplate>
                    <WrapPanel ItemWidth="{TemplateBinding Width}" ItemHeight="30" />
                  </ItemsPanelTemplate>
                </Setter>
              </Style>
            </Styles>
            """);
        Arrange(control);
        var panel = Assert.IsType<WrapPanel>(control.ItemsPanelRoot);
        Assert.Equal(80, panel.ItemWidth);
        control.Width = 120;
        Arrange(control);
        Assert.Same(panel, control.ItemsPanelRoot);
        Assert.Equal(120, panel.ItemWidth);
        var cell = control.GetContainerAt(0);
        control.Styles = StyleSheet.Load("""
            <Styles><Style Selector="ItemsControl"><Setter Property="ItemsPanel">
              <ItemsPanelTemplate><StackPanel Spacing="4" /></ItemsPanelTemplate>
            </Setter></Style></Styles>
            """);
        Arrange(control);
        Assert.IsType<StackPanel>(control.ItemsPanelRoot);
        Assert.Same(cell, control.GetContainerAt(0));
    }

    [Fact]
    public void PanelTemplateRejectsMissingOrNonPanelRoots()
    {
        Assert.Throws<FormatException>(() => ItemsPanelTemplate.Parse("<ItemsPanelTemplate />"));
        Assert.Throws<InvalidOperationException>(() => ItemsPanelTemplate.Parse(
            "<ItemsPanelTemplate><TextBlock Text=\"invalid\" /></ItemsPanelTemplate>").Build());
        Assert.Throws<InvalidOperationException>(() => new ItemsPanelTemplate(() => null!).Build());
    }

    static void Arrange(VisualElement control)
    {
        control.Measure(new Vector2(320, 240));
        control.Arrange(new RectangleF(0, 0, 320, 240));
    }

    sealed class ResettableCollection : ObservableList<object>
    {
        public void ResetNotification() => OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
