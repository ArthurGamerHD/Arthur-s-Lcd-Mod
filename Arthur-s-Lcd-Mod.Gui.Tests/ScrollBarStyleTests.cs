using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class ScrollBarStyleTests
{
    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void PageRegionsStayInvisibleAndClickableWhileOnlyThePillThumbChangesColor(Orientation orientation)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")))
            directory = directory.Parent;
        string controls = Path.Combine(directory!.FullName, "External/Adk/Adk.Gui/Styles/Fluent/Controls");
        var bar = new ScrollBar
        {
            Orientation = orientation, Maximum = 100, Value = 50, ViewportSize = 20,
            Styles = StyleSheet.Load(new[]
            {
                File.ReadAllText(Path.Combine(controls, "Button.xaml")),
                File.ReadAllText(Path.Combine(controls, "ScrollBar.xaml"))
            })
        };
        var trackColor = new Color(20, 30, 40);
        var thumbColor = new Color(80, 90, 100);
        var hoverColor = new Color(120, 130, 140);
        var pressedColor = new Color(160, 170, 180);
        foreach (string key in new[] { "surfaceColor", "onSurfaceColor", "borderVariantColor", "accentContainerColor", "onAccentColor", "accentColor", "secondaryContainerColor" })
            bar.Resources.Add(key, Color.Magenta);
        bar.Resources.Add("scrollBarTrackColor", trackColor);
        bar.Resources.Add("scrollBarThumbColor", thumbColor);
        bar.Resources.Add("scrollBarThumbHoverColor", hoverColor);
        bar.Resources.Add("scrollBarThumbPressedColor", pressedColor);
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), bar);
        host.Arrange(new RectangleF(32, 48,
            orientation == Orientation.Vertical ? 8 : 200,
            orientation == Orientation.Vertical ? 200 : 8));
        var sprites = new List<MySprite>();
        void Draw()
        {
            sprites.Clear();
            host.Draw(sprites);
            sprites.RemoveAll(s => s.Color.HasValue && s.Color.Value.A == 0);
            Assert.True(sprites.Count(s => s.Data == "Circle") == 4,
                string.Join("\n", sprites.Select(s => $"{s.Data}: {s.Position} {s.Size} {s.Color}")));
            Assert.Equal(2, sprites.Count(s => s.Data == "SquareSimple"));
            Assert.DoesNotContain(sprites, s => s.Color == Color.Magenta || s.Data == "Triangle");
        }

        Draw();
        var track = Assert.IsType<Track>(bar.GetTemplateChild("PART_Track"));
        Assert.Equal(bar.Bounds, track.Bounds);
        var page = Assert.IsType<RepeatButton>(bar.GetTemplateChild("PART_PageIncreaseButton"));
        host.MovePointer(page.Bounds.Center);
        Draw();
        Assert.True(page.IsPointerOver);
        Assert.Equal(0, page.Background.FallbackColor.A);
        Assert.Equal(Vector4.Zero, page.BorderThickness);
        host.SetPrimaryButton(true);
        Draw();
        Assert.True(bar.Value > 50);
        host.SetPrimaryButton(false);

        var thumb = Assert.IsType<Thumb>(bar.GetTemplateChild("PART_Thumb"));
        host.MovePointer(thumb.Bounds.Center);
        Draw();
        Assert.Equal(3, sprites.Count(s => s.Color == hoverColor));
        Assert.Equal(3, sprites.Count(s => s.Color == trackColor));
        host.SetPrimaryButton(true);
        Draw();
        Assert.Equal(3, sprites.Count(s => s.Color == pressedColor));
        var beforeDrag = bar.Value;
        host.MovePointer(thumb.Bounds.Center + (orientation == Orientation.Vertical ? new Vector2(0, -10) : new Vector2(-10, 0)));
        Assert.True(bar.Value < beforeDrag);
        host.SetPrimaryButton(false);
    }
}
