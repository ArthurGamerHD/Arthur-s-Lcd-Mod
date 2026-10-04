using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Views;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class AppViewInputTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.3f)]
    public void FooterButtonInsideAppViewReceivesClicks(float scaling)
    {
        var previousLoader = StyleSheet.SourceLoader;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        try
        {
            StyleSheet.SourceLoader = source =>
            {
                source = source.TrimStart('/');
                return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                    ? Path.Combine(root, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                    : Path.Combine(root, "Arthur-s-Lcd-Mod/Content", source));
            };
            int clicks = 0;
            var button = new Button { Content = "Refresh", MinWidth = 96, Command = new TestCommand(() => clicks++) };
            var footer = new Grid();
            footer.SetColumnDefinitions(new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(8), GridLength.Auto);
            footer.SetRowDefinitions(new GridLength(20), new GridLength(28), new GridLength(10));
            footer.Set(button, 3, 1);
            var content = new Grid();
            content.SetRowDefinitions(GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(58));
            content.Set(footer, 0, 2);

            var app = new LcdMod.Client.Apps.Abstract.App(content);
            using var view = new AppView { DataContext = app };
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), view,
                ownsRoot: false) { RenderScaling = scaling };
            host.Arrange(new RectangleF(32, 48, 512, 512));
            host.Draw(new List<MySprite>());

            Vector2 center = new Vector2(32, 48) + (button.Bounds.Center - new Vector2(32, 48)) * scaling;
            host.MovePointer(center);
            host.ClickPrimary();
            Assert.Equal(1, clicks);
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Fact]
    public void BackgroundContentFillsTheSurfaceBeneathTheTitle()
    {
        var previousLoader = StyleSheet.SourceLoader;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        try
        {
            StyleSheet.SourceLoader = source =>
            {
                source = source.TrimStart('/');
                return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                    ? Path.Combine(root, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                    : Path.Combine(root, "Arthur-s-Lcd-Mod/Content", source));
            };
            var background = new Border { Background = new SolidColorBrush(Color.Red) };
            var content = new TextBlock { Text = "Content" };
            var app = new LcdMod.Client.Apps.Abstract.App(content, background);
            using var view = new AppView { DataContext = app };
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), view,
                ownsRoot: false);
            host.Arrange(new RectangleF(0, 0, 512, 512));
            var sprites = new List<MySprite>();
            host.Draw(sprites);

            Assert.Equal(new RectangleF(0, 0, 512, 512), background.Bounds);
            Assert.True(content.Bounds.Y > 0f);
            int backgroundIndex = sprites.FindIndex(s => s.Type == SpriteType.TEXTURE && s.Color == Color.Red);
            int titleIndex = sprites.FindIndex(s => s.Type == SpriteType.TEXT && s.Data == "Thrust title");
            Assert.InRange(backgroundIndex, 0, titleIndex - 1);
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.3f)]
    public void NpcMarketFooterRefreshAndModeComboReceiveClicks(float scaling)
    {
        var previousLoader = StyleSheet.SourceLoader;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        try
        {
            StyleSheet.SourceLoader = source =>
            {
                source = source.TrimStart('/');
                return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                    ? Path.Combine(root, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                    : Path.Combine(root, "Arthur-s-Lcd-Mod/Content", source));
            };
            var model = new LcdMod.Client.Apps.NpcMarket.NpcMarketViewModel();
            var market = new LcdMod.Client.Apps.NpcMarket.NpcMarketView { DataContext = model };
            var app = new LcdMod.Client.Apps.Abstract.App(market);
            using var view = new AppView { DataContext = app };
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), view,
                ownsRoot: false) { RenderScaling = scaling };
            host.Arrange(new RectangleF(32, 48, 512, 512));
            host.Draw(new List<MySprite>());
            model.EnableMode();
            host.Draw(new List<MySprite>());
            Vector2 ToPixels(Vector2 point) => new Vector2(32, 48) + (point - new Vector2(32, 48)) * scaling;

            var refresh = Descendants(market).OfType<Button>().Single(b => b.StyleClass == "market-refresh");
            host.MovePointer(ToPixels(refresh.Bounds.Center));
            host.ClickPrimary();
            Assert.Equal(1, model.RefreshCount);

            var combo = Descendants(market).OfType<ComboBox>().Single();
            host.MovePointer(ToPixels(combo.Bounds.Center));
            host.Draw(new List<MySprite>());
            Assert.True(combo.IsPointerOver);
            host.SetPrimaryButton(true);
            host.Draw(new List<MySprite>());
            host.MovePointer(ToPixels(combo.Bounds.Center));
            host.SetPrimaryButton(false);
            host.Draw(new List<MySprite>());
            Assert.True(combo.IsDropDownOpen);
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    static IEnumerable<VisualElement> Descendants(VisualElement element)
    {
        foreach (var child in element.VisualChildren)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    sealed class TestCommand : Adk.Gui.Core.Input.ICommand
    {
        readonly Action _action;
        public TestCommand(Action action) { _action = action; }
        public event Action CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _action();
    }
}
