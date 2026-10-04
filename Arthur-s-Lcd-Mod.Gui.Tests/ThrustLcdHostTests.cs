using System.Reflection;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Thrust;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class ThrustLcdHostTests
{
    [Theory]
    [InlineData(0.5f, 1f)]
    [InlineData(1.5f, 0.8f)]
    [InlineData(2f, 1.5f)]
    public void WindowDpiAndSurfaceFontSizeAreAppliedExactlyOnce(float scale, float fontSize)
    {
        var model = new ThrustViewModel();
        model.RefreshLayout(1, fontSize, "White", Color.White, Color.Black,
            new[] { "Forward:", "Backward:", "Left:", "Right:", "Up:", "Down:" });
        model.UpdateWarning("Warning", Color.Red);
        model.UpdateTelemetryOpacity(1);
        var view = new ThrustView(model);
        var window = new Adk.Gui.Controls.Window { WindowState = Adk.Gui.Controls.WindowState.FullScreen, Content = view };
        window.Resources.Add("fontScale", fontSize);
        window.Styles = StyleSheet.Load(new[]
        {
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustLegendCellControl.xaml")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustTelemetryControl.xaml"))
        });
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, TextSurfaceProxy>(), window)
        {
            RenderScaling = scale
        };
        host.Arrange(new RectangleF(32, 48, 1024, 768));
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        var label = Assert.Single(sprites, sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "Forward:");
        Near(0.46f * fontSize * scale, label.RotationOrScale);
        Near(32 + (6 + 12 * fontSize) * scale, label.Position.Value.X);
        Near(48 + 768 - 45.22f * fontSize * scale, label.Position.Value.Y);
        var warning = Assert.Single(sprites, sprite => sprite.Data == "Warning");
        Near(0.7f * fontSize * scale, warning.RotationOrScale);
        var box = sprites.First(sprite => sprite.Data == "AH_TextBox");
        Near(Math.Min(89 * fontSize, model.PanelWidth) * scale, box.Size.Value.X);
        Near(32 * scale * fontSize, box.Size.Value.Y);
    }

    [Fact]
    public void LcdHostRendersRetainedCellsAndRestoresClipAfterDiagram()
    {
        IMyTextSurface surface = DispatchProxy.Create<IMyTextSurface, TextSurfaceProxy>();
        var model = new ThrustViewModel();
        model.RefreshLayout(1, 1, "White", Color.White, Color.Black,
            new[] { "Forward:", "Backward:", "Left:", "Right:", "Up:", "Down:" });
        model.Speed.UpdateValue("100");
        model.Forward.UpdateValue("120 kN");
        model.UpdateTelemetryOpacity(1);
        var view = new ThrustView(model);
        view.Resources.Add("fontScale", 1f);
        view.Styles = StyleSheet.Load(new[]
        {
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustLegendCellControl.xaml")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustTelemetryControl.xaml"))
        });
        using var host = new LcdGuiHost(surface, view);
        host.Arrange(new RectangleF(32, 48, 512, 456));
        var sprites = new List<MySprite>();
        host.Draw(sprites);

        var label = Assert.Single(sprites, sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "Forward:");
        Near(50, label.Position.Value.X);
        Near(458.78f, label.Position.Value.Y);
        Assert.Equal(TextAlignment.LEFT, label.Alignment);
        var value = Assert.Single(sprites, sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "120 kN");
        Assert.Equal(TextAlignment.RIGHT, value.Alignment);
        Near(32 + 512f / 3 - 6, value.Position.Value.X);
        var boxes = sprites.Where(sprite => sprite.Data == "AH_TextBox").ToArray();
        Assert.Equal(4, boxes.Length);
        Near(101, boxes[0].Position.Value.X);
        Near(218, boxes[0].Position.Value.Y);
        Assert.Equal(new Vector2(89, 32), boxes[0].Size);
        Assert.Equal(SpriteType.CLIP_RECT, sprites.Last().Type);
        Assert.Null(sprites.Last().Size);

        // Thrust is a read-only dashboard; pointer/wheel input must not capture focus.
        host.MovePointer(new Vector2(100, 200));
        host.ClickPrimary();
        host.Scroll(120);
        Assert.False(host.HasKeyboardFocus);
        sprites.Clear();
        host.Arrange(new RectangleF(20, 30, 256, 256));
        model.UpdateTelemetryOpacity(0);
        host.Draw(sprites);
        Assert.DoesNotContain(sprites, sprite => sprite.Data == "AH_TextBox");
        Assert.Equal(SpriteType.CLIP_RECT, sprites.Last().Type);
        Assert.Null(sprites.Last().Size);
    }

    [Fact]
    public void UnchangedTelemetryReusesTheFrame()
    {
        var model = new ThrustViewModel();
        model.RefreshLayout(1, 1, "White", Color.White, Color.Black,
            new[] { "Forward:", "Backward:", "Left:", "Right:", "Up:", "Down:" });
        model.Forward.UpdateValue("120 kN");
        model.UpdateAxes(Vector3.One, Vector3.Zero, Vector3.Down, Color.White);
        model.UpdateTelemetryOpacity(1);
        var view = new ThrustView(model);
        view.Resources.Add("fontScale", 1f);
        view.Styles = StyleSheet.Load(new[]
        {
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustLegendCellControl.xaml")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustTelemetryControl.xaml"))
        });
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, TextSurfaceProxy>(), view);
        host.Arrange(new RectangleF(32, 48, 512, 456));
        var first = new List<MySprite>();
        host.Draw(first);

        model.Forward.UpdateValue("120 kN");
        model.UpdateAxes(Vector3.One, Vector3.Zero, Vector3.Down, Color.White);
        model.UpdateTelemetryOpacity(1);
        Assert.False(host.NeedsRender);
        var second = new List<MySprite>();
        host.Draw(second);
        Assert.Equal(first, second);

        model.Forward.UpdateValue("130 kN");
        Assert.True(host.NeedsRender);
        var third = new List<MySprite>();
        host.Draw(third);
        Assert.Contains(third, sprite => sprite.Data == "130 kN");
    }

    static void Near(float expected, float actual) => Assert.InRange(actual, expected - 0.01f, expected + 0.01f);

    public class TextSurfaceProxy : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "GetSprites":
                    ((List<string>)args[0]).AddRange(new[] { "SquareSimple", "Circle", "Triangle", "AH_TextBox", "MissingIcon" });
                    return null;
                case "MeasureStringInPixels":
                    var lines = args[0].ToString().Split('\n');
                    return new Vector2(lines.Max(line => line.Length) * 10, 28 * lines.Length) * (float)args[2];
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
