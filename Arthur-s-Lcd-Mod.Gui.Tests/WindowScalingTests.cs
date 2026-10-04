using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Lcd;
using Adk.Gui.Rendering;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class WindowScalingTests
{
    static IMyTextSurface Surface() => DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>();

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void DpiScalesAllPrimitivesAndClipsAroundViewportOrigin(float scale)
    {
        var probe = new Probe();
        var window = new Window { WindowState = WindowState.FullScreen, Content = probe };
        using var host = new LcdGuiHost(Surface(), window) { RenderScaling = scale };
        var bounds = new RectangleF(32, 48, 600, 400);
        host.Arrange(bounds);
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Equal((double)scale, window.RenderScaling);
        Assert.Equal(bounds.Size / scale, window.Bounds.Size);
        Assert.Equal(bounds.Position, window.Bounds.Position);
        Assert.Equal(new Vector2(20, 28), probe.MeasuredText); // Measurement stays logical.
        var fill = Assert.Single(sprites, s => s.Data == "SquareSimple");
        Assert.Equal(new Vector2(20, 10) * scale, fill.Size);
        Assert.Equal(bounds.Position + new Vector2(20, 25) * scale, fill.Position);
        var texture = Assert.Single(sprites, s => s.Data == "Triangle");
        Assert.Equal(new Vector2(8, 6) * scale, texture.Size);
        Assert.Equal(0.7f, texture.RotationOrScale); // Texture rotation is not a scale.
        var text = Assert.Single(sprites, s => s.Type == SpriteType.TEXT);
        Assert.Equal(scale, text.RotationOrScale);
        Assert.Equal(bounds.Position + new Vector2(40, 36) * scale, text.Position);
        var clip = sprites.First(s => s.Type == SpriteType.CLIP_RECT);
        Assert.Equal(bounds.Position, clip.Position);
        Assert.Equal(bounds.Size, clip.Size);
        Assert.Null(sprites.Last().Size);
    }

    [Fact]
    public void InputUsesLogicalCoordinatesIncludingDragScrollAndKeyboardPointerQueries()
    {
        var probe = new Probe();
        using var host = new LcdGuiHost(Surface(), new Window { WindowState = WindowState.FullScreen, Content = probe }) { RenderScaling = 2 };
        host.Arrange(new RectangleF(32, 48, 600, 400));
        host.MovePointer(new Vector2(132, 148));
        host.SetPrimaryButton(true);
        Assert.Equal(new Vector2(82, 98), probe.Pressed);
        host.MovePointer(new Vector2(152, 188));
        Assert.Equal(new Vector2(10, 20), probe.Delta);
        Assert.Equal(new Vector2(92, 118), probe.KeyboardPointer);
        host.SetPrimaryButton(false);
        Assert.True(probe.Clicked);
        Assert.True(host.Scroll(120));
        Assert.Equal(new Vector2(92, 118), probe.Scrolled);

        // A DPI change with identical physical bounds must relayout before input.
        host.RenderScaling = 1;
        host.MovePointer(new Vector2(152, 188));
        Assert.Equal(new Vector2(600, 400), host.Root.Bounds.Size);
        Assert.Equal(new Vector2(152, 188), probe.KeyboardPointer);
    }

    [Fact]
    public void WindowsHaveIndependentScaleAndRuntimeProviderChangesRelayout()
    {
        float scale = 2;
        using var first = new LcdGuiHost(Surface(), new Window { WindowState = WindowState.FullScreen }) { RenderScalingProvider = () => scale };
        using var second = new LcdGuiHost(Surface(), new Window { WindowState = WindowState.FullScreen });
        var bounds = new RectangleF(0, 0, 600, 400);
        first.Arrange(bounds);
        second.Arrange(bounds);
        Assert.Equal(new Vector2(300, 200), first.Root.Bounds.Size);
        Assert.Equal(new Vector2(600, 400), second.Root.Bounds.Size);
        scale = 0.5f;
        first.Draw(new List<MySprite>());
        Assert.Equal(new Vector2(1200, 800), first.Root.Bounds.Size);
        Assert.Equal(0.5, ((Window)first.Root).RenderScaling);
        Assert.Equal(1, ((Window)second.Root).RenderScaling);
        Assert.Throws<ArgumentException>(() => first.RenderScaling = 0);
        Assert.Throws<ArgumentException>(() => first.RenderScaling = float.NaN);
        Assert.Throws<ArgumentException>(() => first.RenderScaling = float.PositiveInfinity);
    }

    [Fact]
    public void SpriteCanvasScalesCopiesAndKeepsTextAnchorsAndTextureRotation()
    {
        var original = new[]
        {
            MySprite.CreateClipRect(new Rectangle(40, 60, 100, 50)),
            new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(50, 70), new Vector2(8, 6), Color.White, rotation: 0.7f),
            new MySprite(SpriteType.TEXT, "hello", new Vector2(60, 80), null, Color.White, "White", TextAlignment.RIGHT, 0.4f),
            MySprite.CreateClearClipRect()
        };
        var canvas = new LcdSpriteCanvas { SpriteProvider = (_, _) => original };
        using var host = new LcdGuiHost(Surface(), canvas) { RenderScaling = 2 };
        host.Arrange(new RectangleF(32, 48, 600, 400));
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        var texture = Assert.Single(sprites, s => s.Data == "Triangle");
        Assert.Equal(new Vector2(68, 92), texture.Position);
        Assert.Equal(new Vector2(16, 12), texture.Size);
        Assert.Equal(0.7f, texture.RotationOrScale);
        var text = Assert.Single(sprites, s => s.Data == "hello");
        Assert.Equal(new Vector2(88, 112), text.Position);
        Assert.Equal(0.8f, text.RotationOrScale);
        Assert.Equal(TextAlignment.RIGHT, text.Alignment);
        Assert.Contains(sprites, s => s.Type == SpriteType.CLIP_RECT && s.Position == new Vector2(48, 72) && s.Size == new Vector2(200, 100));
        Assert.Equal(new Vector2(50, 70), original[1].Position);
        Assert.Equal(0.4f, original[2].RotationOrScale);
    }

    sealed class Probe : VisualElement
    {
        public Vector2 Pressed, Delta, Scrolled, KeyboardPointer, MeasuredText;
        public bool Clicked;
        public override bool AcceptsKeyboardFocus => true;
        protected override bool HitTestSelf(Vector2 point) => true;
        protected override Vector2 MeasureOverride(Vector2 size) => size;
        protected override void DrawSelf(RenderContext context)
        {
            MeasuredText = context.TextMetrics.Measure("White", "Hi", 1);
            using (context.PushClip(Bounds))
            {
                context.FillRectangle(new RectangleF(Bounds.Position + new Vector2(10, 20), new Vector2(20, 10)), Color.White);
                context.Texture("Triangle", new RectangleF(Bounds.Position + new Vector2(30, 40), new Vector2(8, 6)), Color.White, rotation: 0.7f);
                context.Text("Hi", Bounds.Position + new Vector2(40, 50), 1, Color.White,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }
        }
        internal override void PointerPressed(Vector2 point) { Pressed = point; }
        internal override void PointerMoved(Vector2 point, Vector2 delta) { Delta = delta; }
        internal override void PointerReleased(Vector2 point, bool clicked) { Clicked = clicked; }
        internal override bool PointerScrolled(Vector2 point, int delta) { Scrolled = point; return true; }
        internal override void KeyboardInput(IInputSource input) { input.TryGetPointerPosition(out KeyboardPointer); }
    }
}
