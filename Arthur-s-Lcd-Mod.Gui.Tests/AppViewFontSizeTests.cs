using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Thrust;
using LcdMod.Client.Apps.Views;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class AppViewFontSizeTests
{
    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.75f)]
    [InlineData(2f)]
    public void LiveFontChangeResizesRealAppTitleIconAndThrustLegend(float fontSize)
    {
        string root = RepositoryRoot();
        var previousLoader = StyleSheet.SourceLoader;
        try
        {
            StyleSheet.SourceLoader = source =>
            {
                source = source.TrimStart('/');
                return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                    ? Path.Combine(root, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                    : Path.Combine(root, "Arthur-s-Lcd-Mod/Content", source));
            };
            var app = new LcdMod.Client.Apps.Abstract.App();
            using var view = new AppView { DataContext = app };
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), view, ownsRoot: false);
            host.Arrange(new RectangleF(32, 48, 1024, 768));
            var sprites = new List<MySprite>();
            host.Draw(sprites);
            var title = Descendants(view).OfType<TextBlock>().Single(t => t.Text == app.Title);
            var icon = Descendants(view).OfType<Adk.Gui.Controls.Border>().Single(border => border.OpacityMask is Adk.Gui.Styling.ImageBrush);
            var cell = Descendants(view).OfType<ThrustLegendCellControl>().First();
            var telemetry = Descendants(view).OfType<ThrustTelemetryControl>().First();
            var nativeText = new TextBlock { Text = "Native text" };
            // A bare TextBlock inherits its font size from the application style.
            var panel = (Adk.Gui.Layout.Grid)view.Content;
            panel.AddChild(nativeText);
            Near(1.3f, title.FontSize);
            Near(32, icon.Bounds.Width);
            Near(28, cell.Bounds.Height);

            app.ChangeFontSize(fontSize);
            sprites.Clear();
            host.Draw(sprites); // Same physical viewport, existing tree.
            Assert.Same(cell, Descendants(view).OfType<ThrustLegendCellControl>().First());
            Near(1.3f * fontSize, title.FontSize);
            Near(32 * fontSize, icon.Bounds.Width);
            Near(32 * fontSize, icon.Bounds.Height);
            Near(28 * fontSize, cell.Bounds.Height);
            Near(fontSize, nativeText.FontSize);
            Near(fontSize, telemetry.FontSize);
            Near(89 * fontSize, telemetry.BoxWidth);
            Near(fontSize, Descendants(telemetry).OfType<TextBlock>().Single().FontSize);
            Near(1.3f * fontSize, sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == app.Title).RotationOrScale);
            Near(0.46f * fontSize, sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "Forward:").RotationOrScale);
            Near(32 * fontSize, sprites.First(s => s.Data == "Triangle").Size.Value.X);
        }
        finally { StyleSheet.SourceLoader = previousLoader; }
    }

    [Fact]
    public void ThrustSurfaceForwardsLayoutChangesToItsApp()
    {
        // Guard the game-only bridge that was absent despite the UI tests passing.
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Arthur-s-Lcd-Mod/Client/SurfaceScripts/ThrustGraph.cs"));
        Assert.Matches(@"protected override void LayoutChanged\(\)\s*\{\s*base.LayoutChanged\(\);\s*if \(_app != null\)\s*_app.LayoutChanged\(\);\s*\}", source);
    }

    static IEnumerable<VisualElement> Descendants(VisualElement node)
    {
        yield return node;
        foreach (var child in node.VisualChildren)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    static void Near(float expected, float actual) => Assert.InRange(actual, expected - 0.01f, expected + 0.01f);
}
