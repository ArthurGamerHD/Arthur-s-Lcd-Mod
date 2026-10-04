using Adk.Gui.Controls;
using Adk.Gui.Core.Input;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class ContextMenuTests
{
    sealed class Command : ICommand
    {
        public int Runs;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => Runs++;
    }

    static (Border target, MenuItem item, Command command) Target(DialogHarness harness, object dataContext = null)
    {
        var command = new Command();
        var item = new MenuItem { Header = "Configure visualizer", Command = command };
        var menu = new ContextMenu();
        menu.Items.Add(item);
        var target = new Border { Background = new SolidColorBrush(Color.Gray), ContextMenu = menu, DataContext = dataContext };
        harness.Owner.Content = target;
        harness.Draw();
        return (target, item, command);
    }

    [Fact]
    public void ASecondaryClickOpensTheMenuAtThePointerAndPickingAnItemRunsItAndCloses()
    {
        using var harness = new DialogHarness();
        var (target, item, command) = Target(harness, "view model");
        var point = new Vector2(120, 140);

        harness.Host.MovePointer(point);
        harness.Host.ClickSecondary();
        harness.Draw();

        Assert.True(target.ContextMenu.IsOpen);
        Assert.Equal("view model", target.ContextMenu.DataContext);
        Assert.Contains("Configure visualizer", harness.Texts());
        Assert.Equal(point.X, item.Parent.Parent.Bounds.X, 1);
        Assert.Equal(point.Y, item.Parent.Parent.Bounds.Y, 1);

        harness.Click(item);
        Assert.Equal(1, command.Runs);
        Assert.False(target.ContextMenu.IsOpen);
        Assert.DoesNotContain("Configure visualizer", harness.Texts());
    }

    [Fact]
    public void NearTheScreenEdgeTheMenuFlipsToStayVisible()
    {
        using var harness = new DialogHarness();
        var (target, item, _) = Target(harness);

        harness.Host.MovePointer(new Vector2(505, 505));
        harness.Host.ClickSecondary();
        harness.Draw();

        var dropdown = item.Parent.Parent.Bounds;
        Assert.True(target.ContextMenu.IsOpen);
        Assert.True(dropdown.Right <= 505.5f && dropdown.Bottom <= 505.5f, dropdown.ToString());
    }

    [Fact]
    public void ClickingAwayClosesTheMenuWithoutReachingWhatIsBelow()
    {
        using var harness = new DialogHarness();
        var (target, _, command) = Target(harness);
        harness.Host.MovePointer(new Vector2(40, 40));
        harness.Host.ClickSecondary();
        harness.Draw();
        Assert.True(target.ContextMenu.IsOpen);

        harness.Host.MovePointer(new Vector2(400, 400));
        harness.Host.ClickSecondary();
        harness.Draw();

        Assert.False(target.ContextMenu.IsOpen);
        Assert.Equal(0, command.Runs);
    }

    [Fact]
    public void AnElementWithItsOwnSecondaryClickKeepsTheAncestorMenuClosed()
    {
        using var harness = new DialogHarness();
        int ownClicks = 0;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Configure visualizer" });
        var button = new Button { Width = 100, Height = 40, SecondaryClicked = () => ownClicks++ };
        var target = new Border { Background = new SolidColorBrush(Color.Gray), ContextMenu = menu, Child = button };
        harness.Owner.Content = target;

        harness.ClickSecondary(button);

        Assert.Equal(1, ownClicks);
        Assert.False(menu.IsOpen);
    }
}
