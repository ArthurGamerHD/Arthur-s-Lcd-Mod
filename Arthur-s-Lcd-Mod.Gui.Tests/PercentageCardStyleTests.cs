using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Controls;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// The card is a Button, so the Fluent Button state rules also match it; the card's own rules must win.
public sealed class PercentageCardStyleTests
{
    static readonly Color Header = new(40, 90, 140);
    static readonly Color Accent = new(200, 120, 20);

    [Theory]
    [InlineData("card")]
    [InlineData("row")]
    public void HoverKeepsTheCardColorsInsteadOfFluentButtonStates(string classes)
    {
        using var f = new Fixture(classes);
        f.Host.MovePointer(f.Card.Bounds.Center);
        f.Draw();
        Assert.Equal(Header, f.Card.Background.FallbackColor);
        Assert.NotEqual(Accent, f.Card.BorderBrush);
        Assert.Equal(Adk.Gui.Core.Input.PointerCursor.Default, f.Host.PointerCursor);
    }

    [Fact]
    public void LinesModeDropsTheCardBackgroundEvenWhileHovered()
    {
        using var f = new Fixture("card lines");
        Assert.Equal(0, f.Card.Background.FallbackColor.A);
        Assert.Equal(0, f.Card.Margin.X);
        f.Host.MovePointer(f.Card.Bounds.Center);
        f.Draw();
        Assert.Equal(0, f.Card.Background.FallbackColor.A);
    }

    sealed class Fixture : IDisposable
    {
        readonly Func<string, string> _previousLoader;
        public readonly BlockPropertyPercentage Card;
        public readonly LcdGuiHost Host;

        public Fixture(string classes)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
            string repo = directory!.FullName;
            _previousLoader = StyleSheet.SourceLoader;
            StyleSheet.SourceLoader = path =>
            {
                path = path.TrimStart('/');
                return File.ReadAllText(path.StartsWith("Data/Gui/Styles/Adk/")
                    ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", path.Substring("Data/Gui/Styles/Adk/".Length))
                    : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", path));
            };
            Card = new BlockPropertyPercentage
            {
                StyleClass = classes, Label = "Cargo", ValueText = "50%", Value = .5f, Width = 220,
                HorizontalAlignment = Adk.Gui.Core.HorizontalAlignment.Left, VerticalAlignment = Adk.Gui.Core.VerticalAlignment.Top,
                Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Cards.xaml")
            };
            var root = new Window { WindowState = WindowState.FullScreen, Content = Card, Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml") };
            foreach (string key in new[] { "fontColor", "onSurfaceColor", "surfaceColor", "onAccentColor", "secondaryContainerColor",
                         "borderVariantColor", "cardShadowColor", "cardFillColor", "surfaceContainerHighestColor", "disabledColor" })
                root.Resources.Add(key, Color.Gray);
            root.Resources.Add("headerColor", Header);
            root.Resources.Add("accentColor", Accent);
            root.Resources.Add("accentContainerColor", Accent);
            root.Resources.Add("textFont", "White");
            root.Resources.Add("fontScale", 1f);
            root.Resources.Add("cardHeight", 72f);
            Host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
            Host.Arrange(new RectangleF(0, 0, 400, 300));
            Draw();
        }

        public void Draw() => Host.Draw(new List<MySprite>());

        public void Dispose()
        {
            Host.Dispose();
            StyleSheet.SourceLoader = _previousLoader;
        }
    }
}
