using Adk.Gui.Core.Input;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Games;
using LcdMod.Client.Games;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class GameInputLayerTests
{
    sealed class FakeGame : IGameInput, IGameInputSource
    {
        public readonly List<GameRegion> RegionList = new();
        public readonly List<string> Log = new();
        public Vector2? Pointer;
        public IGameInput GameInput => this;
        public IReadOnlyList<GameRegion> Regions => RegionList;
        public void PointerMoved(Vector2? position) => Pointer = position;
        public void PointerClicked(GamePointerButton button, Vector2 position) => Log.Add("free " + button);
        public PointerCursor CursorAt(Vector2 position) => PointerCursor.None;
    }

    [Fact]
    public void RegionsBecomeButtonsAndTheRestOfTheGameGetsRawPointerInput()
    {
        using var harness = new DialogHarness(400, 300);
        var game = new FakeGame();
        game.RegionList.Add(new GameRegion
        {
            Bounds = new RectangleF(10, 10, 100, 100), Cursor = PointerCursor.DragMove,
            Clicked = () => game.Log.Add("cell"), SecondaryClicked = () => game.Log.Add("flag")
        });
        game.RegionList.Add(new GameRegion
        {
            Bounds = new RectangleF(60, 60, 100, 100), Cursor = PointerCursor.No, Clicked = () => game.Log.Add("cover")
        });
        var view = new Grid { Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Games.xaml") };
        view.AddChild(new GameInputLayer { DataContext = game });
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Draw();

        harness.Host.MovePointer(new Vector2(30, 30));
        Assert.Equal(PointerCursor.DragMove, harness.Host.PointerCursor);
        Assert.Equal(new Vector2(30, 30), game.Pointer);
        harness.Host.ClickPrimary();
        harness.Host.ClickSecondary();

        harness.Host.MovePointer(new Vector2(80, 80));
        Assert.Equal(PointerCursor.No, harness.Host.PointerCursor);
        harness.Host.ClickPrimary();

        harness.Host.MovePointer(new Vector2(300, 250));
        Assert.Equal(PointerCursor.None, harness.Host.PointerCursor);
        harness.Host.ClickPrimary();

        Assert.Equal(new[] { "cell", "flag", "cover", "free Primary" }, game.Log);

        Assert.DoesNotContain(harness.Draw(), s => s.Type == SpriteType.TEXTURE && s.Position.HasValue &&
                                                   new RectangleF(10, 10, 150, 150).Contains(s.Position.Value));

        harness.Host.ClearPointer();
        harness.Draw();
        Assert.Null(game.Pointer);
    }
}
