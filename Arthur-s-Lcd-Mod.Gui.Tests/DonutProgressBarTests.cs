using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class DonutProgressBarTests
{
    [Fact]
    public void DonutClassReplacesTheStraightBar()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        var previousLoader = StyleSheet.SourceLoader;
        StyleSheet.SourceLoader = path =>
        {
            path = path.TrimStart('/');
            return File.ReadAllText(path.StartsWith("Data/Gui/Styles/Adk/")
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", path.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", path));
        };
        try
        {
            var bar = new ProgressBar { Width = 110, Height = 110, Value = .5f, StyleClass = "donut" };
            var root = new Window { WindowState = WindowState.FullScreen, Content = bar, Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml") };
            foreach (string key in new[] { "fontColor", "accentColor", "surfaceContainerHighestColor" }) root.Resources.Add(key, Color.White);
            root.Resources.Add("textFont", "White");
            root.Resources.Add("fontScale", 1f);
            root.Resources.Add("Multiply", new LcdMod.Client.Apps.Views.MultiplyConverter());
            root.Resources.Add("PercentText", new LcdMod.Client.Apps.Views.PercentTextConverter());
            root.Resources.Add("donutThickness", 17.6f);
            root.Resources.Add("donutTextMargin", new Vector4(20f));
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
            host.Arrange(new RectangleF(0, 0, 200, 200));
            var sprites = new List<MySprite>();
            host.Draw(sprites);
            var templateRoot = typeof(ProgressBar).GetProperty("TemplateRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bar);
            Assert.NotNull(templateRoot);
            Assert.Contains(sprites, s => s.Type == SpriteType.TEXT && s.Data == "50%");
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }
}
