using Adk.Gui.Controls;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Rendering;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Modules.EyeTracking;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class ProxyPointerCaptureTests
{
    sealed class Proxy
    {
        public LcdGuiHost Owner;
        public Vector2 Offset;
    }

    sealed class Routing
    {
        public int Resets;
        public readonly PointerInputCapture<LcdGuiHost> Capture;

        public Routing()
        {
            Capture = new PointerInputCapture<LcdGuiHost>(host =>
            {
                Resets++;
                host.ClearPointer();
                host.SetPrimaryButton(false);
                host.SetMiddleButton(false);
            });
        }

        public void Move(Proxy proxy, Vector2 local, bool held, bool middle = false)
        {
            // Proxy gaze is translated to host coordinates before buttons are routed.
            proxy.Owner.MovePointer(local + proxy.Offset);
            Capture.Select(proxy.Owner, false);
            if (middle) Capture.Current.SetMiddleButton(held);
            else Capture.Current.SetPrimaryButton(held);
        }
    }

    sealed class Map : IMapSurface, IMapSurfaceDrag
    {
        public int Began;
        public int Ended;
        public int Cancelled;
        public Vector2 Movement;
        public void SetCanvasSize(Vector2I size) { }
        public void Paint(PixelCanvas canvas) => canvas.Clear(Color.Black);
        public void Drag(Vector2 delta) => Movement += delta;
        public void MiddleDrag(Vector2 delta) => Movement += delta;
        public void BeginDrag(Vector2 pixel) => Began++;
        public void DragTo(Vector2 pixel, Vector2 delta) => Movement += delta;
        public void EndDrag() => Ended++;
        public void CancelDrag() => Cancelled++;
        public void Click(Vector2 pixel) { }
        public void MiddleClick(Vector2 pixel) { }
        public void DoubleClick(Vector2 pixel) { }
        public bool Scroll(int delta) => false;
        public void MarkerClicked(MapMarker marker) { }
        public void StreetView(Vector2 pixel) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapDragSurvivesProxySwitchAndPanelGapWithoutRestarting(bool middle)
    {
        using var textures = new FakeGeneratedTextures();
        using var harness = new DialogHarness(400, 300);
        var map = new Map();
        harness.Owner.Content = new CartographyMapView { Surface = map };
        harness.Draw();
        var first = new Proxy { Owner = harness.Host };
        var second = new Proxy { Owner = harness.Host, Offset = new Vector2(100, 0) };
        var routing = new Routing();
        routing.Move(first, new Vector2(160, 150), false, middle);
        routing.Move(first, new Vector2(160, 150), true, middle);
        routing.Move(first, new Vector2(180, 150), true, middle);
        routing.Capture.Select(null, true); // A ray misses both panels at the seam.
        routing.Move(second, new Vector2(100, 150), true, middle);
        Assert.Equal(PointerCursor.DragMove, harness.Host.PointerCursor);
        routing.Move(second, new Vector2(120, 150), true, middle);
        Assert.Equal(new Vector2(60, 0), map.Movement);
        Assert.Equal(0, routing.Resets);
        Assert.Equal(0, map.Cancelled);
        Assert.Equal(middle ? 0 : 1, map.Began);
        Assert.Equal(0, map.Ended);
        routing.Move(second, new Vector2(120, 150), false, middle);
        Assert.Equal(middle ? 0 : 1, map.Ended);
        Assert.NotEqual(PointerCursor.DragMove, harness.Host.PointerCursor);
    }

    [Theory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void ScrollbarThumbRemainsCapturedAcrossProxies(Orientation orientation)
    {
        using var harness = new DialogHarness(400, 300);
        var bar = new ScrollBar { Orientation = orientation, Maximum = 100, Value = 20, ViewportSize = 20 };
        harness.Owner.Content = bar;
        harness.Draw();
        var thumb = Assert.IsType<Thumb>(bar.GetTemplateChild("PART_Thumb"));
        Vector2 start = thumb.Bounds.Center;
        Vector2 motion = orientation == Orientation.Horizontal ? new Vector2(20, 0) : new Vector2(0, 20);
        var first = new Proxy { Owner = harness.Host };
        var second = new Proxy { Owner = harness.Host, Offset = motion * 2 };
        var routing = new Routing();
        routing.Move(first, start, false);
        routing.Move(first, start, true);
        routing.Move(first, start + motion, true);
        float beforeSwitch = bar.Value;
        routing.Capture.Select(null, true);
        routing.Move(second, start + motion * 3 - second.Offset, true);
        Assert.True(bar.Value > beforeSwitch);
        Assert.True(thumb.IsPressed);
        Assert.Equal(0, routing.Resets);
        routing.Move(second, start + motion * 3 - second.Offset, false);
        Assert.False(thumb.IsPressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavingTheHostOrReleasingInAGapCancelsTheOldCapture(bool unrelatedHost)
    {
        using var textures = new FakeGeneratedTextures();
        using var first = new DialogHarness(400, 300);
        using var second = new DialogHarness(400, 300);
        var map = new Map();
        first.Owner.Content = new CartographyMapView { Surface = map };
        first.Draw();
        var routing = new Routing();
        var proxy = new Proxy { Owner = first.Host };
        routing.Move(proxy, new Vector2(160, 150), true);
        routing.Move(proxy, new Vector2(180, 150), true);
        routing.Capture.Select(unrelatedHost ? second.Host : null, false);
        Assert.Equal(1, routing.Resets);
        Assert.Equal(1, map.Cancelled);
        Assert.Equal(0, map.Ended); // Cancellation must not synthesize a release fling.
        Assert.NotEqual(PointerCursor.DragMove, first.Host.PointerCursor);
    }
}
