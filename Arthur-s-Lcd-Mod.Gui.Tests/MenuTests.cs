using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Layout;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class MenuTests
{
    sealed class Recorder : ICommand
    {
        public readonly List<string> Runs = new();
        readonly string _name;
        public Recorder(List<string> runs, string name) { Runs = runs; _name = name; }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => Runs.Add(_name);
    }

    static (DialogHarness Harness, Menu Menu, List<string> Runs) Show()
    {
        var harness = new DialogHarness(512, 400);
        var runs = new List<string>();
        var menu = new Menu { VerticalAlignment = VerticalAlignment.Top };
        var game = new MenuItem { Header = "Game" };
        game.Items.Add(new MenuItem { Header = "New", Command = new Recorder(runs, "new") });
        var difficulty = new MenuItem { Header = "Difficulty" };
        difficulty.Items.Add(new MenuItem { Header = "Hard", Command = new Recorder(runs, "hard") });
        game.Items.Add(difficulty);
        var view = new MenuItem { Header = "View" };
        view.Items.Add(new MenuItem { Header = "Zoom", Command = new Recorder(runs, "zoom") });
        menu.Items.Add(game);
        menu.Items.Add(view);
        harness.Owner.Content = menu;
        harness.Host.Arrange(new RectangleF(0, 0, 512, 400));
        harness.Draw();
        return (harness, menu, runs);
    }

    static MenuItem Item(Menu menu, params string[] path)
    {
        MenuItem item = menu.Items.Single(i => (string)i.Header == path[0]);
        foreach (string header in path.Skip(1))
            item = item.Items.Single(i => (string)i.Header == header);
        return item;
    }

    [Fact]
    public void ALeafRunsItsCommandAndClosesTheMenu()
    {
        var (harness, menu, runs) = Show();
        using var _ = harness;
        harness.Click(Item(menu, "Game"));
        Assert.True(menu.IsOpen);
        Assert.Contains("New", harness.Texts());

        harness.Click(Item(menu, "Game", "New"));
        Assert.Equal(new[] { "new" }, runs);
        Assert.False(menu.IsOpen);
        Assert.DoesNotContain("New", harness.Texts());
    }

    [Fact]
    public void HoveringAnItemFillsItWithoutTheButtonOutline()
    {
        var (harness, menu, _) = Show();
        using var _h = harness;
        MenuItem game = Item(menu, "Game");
        harness.Host.MovePointer(game.Bounds.Center);
        harness.Draw();
        Assert.True(game.IsPointerOver);
        Assert.Equal(Vector4.Zero, game.BorderThickness);
        Assert.Equal(DialogHarness.Accent, game.Background.FallbackColor);

        harness.Host.SetPrimaryButton(true);
        harness.Draw();
        Assert.Equal(Vector4.Zero, game.BorderThickness);
        Assert.Equal(1f, ((Adk.Gui.Styling.ScaleTransform)game.RenderTransform).ScaleX);
        harness.Host.SetPrimaryButton(false);
    }

    [Fact]
    public void ClickingAnotherTopLevelItemSwitchesMenusInOneClick()
    {
        var (harness, menu, _) = Show();
        using var _h = harness;
        harness.Click(Item(menu, "Game"));
        harness.Click(Item(menu, "View"));
        Assert.True(Item(menu, "View").IsSubMenuOpen);
        Assert.False(Item(menu, "Game").IsSubMenuOpen);
        var texts = harness.Texts();
        Assert.Contains("Zoom", texts);
        Assert.DoesNotContain("New", texts);

        harness.Click(Item(menu, "View"));
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void NestedSubmenusOpenBesideTheirParentAndStayClickable()
    {
        var (harness, menu, runs) = Show();
        using var _h = harness;
        harness.Click(Item(menu, "Game"));
        MenuItem difficulty = Item(menu, "Game", "Difficulty");
        harness.Click(difficulty);
        MenuItem hard = Item(menu, "Game", "Difficulty", "Hard");
        harness.Draw();
        Assert.True(hard.Bounds.X >= difficulty.Bounds.Right - 1f);

        harness.Click(Item(menu, "Game", "New"));
        Assert.Equal(new[] { "new" }, runs);
    }

    [Fact]
    public void ClickingOutsideClosesTheMenuAndOnlyTheMenuOwnsThePointer()
    {
        var (harness, menu, _) = Show();
        using var _h = harness;
        harness.Host.MovePointer(new Vector2(400, 300));
        Assert.False(harness.Host.OwnsPointer);
        harness.Host.MovePointer(Item(menu, "Game").Bounds.Center);
        Assert.True(harness.Host.OwnsPointer);

        harness.Click(Item(menu, "Game"));
        harness.Host.MovePointer(new Vector2(400, 300));
        Assert.True(harness.Host.OwnsPointer, "an open menu owns every click until dismissed");
        harness.Host.ClickPrimary();
        Assert.False(menu.IsOpen);
    }
}
