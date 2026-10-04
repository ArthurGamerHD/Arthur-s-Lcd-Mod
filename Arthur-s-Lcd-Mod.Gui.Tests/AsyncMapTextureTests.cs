using Adk.Gui.Lcd;
using Adk.Gui.Rendering;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Modules.Cartography.Maps;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class AsyncMapTextureTests
{
    sealed class DeferredSurface : IAsyncMapSurface, IMapSurface
    {
        public PixelCanvas Canvas;
        public Action<Exception> Complete;
        public int Requests;
        public bool Accept = true;

        public bool RequestTexture(PixelCanvas canvas, Action<Exception> completed)
        {
            Requests++;
            if (!Accept)
                return false;
            Canvas = canvas;
            Complete = completed;
            return true;
        }

        public void SetCanvasSize(Vector2I size) { }
        public void Paint(PixelCanvas canvas) => throw new Exception("Must request asynchronously");
        public void Drag(Vector2 delta) { }
        public void MiddleDrag(Vector2 delta) { }
        public void MiddleClick(Vector2 pixel) { }
        public void Click(Vector2 pixel) { }
        public void DoubleClick(Vector2 pixel) { }
        public bool Scroll(int delta) => false;
        public void MarkerClicked(MapMarker marker) { }
        public void StreetView(Vector2 pixel) { }
    }

    [Fact]
    public void ViewDefersNewVersionsUntilCompletionAndThenRequestsOnlyTheLatest()
    {
        using var generated = new FakeGeneratedTextures();
        using var harness = new DialogHarness(64, 48);
        var surface = new DeferredSurface();
        var view = new CartographyMapView { Surface = surface, Version = 1 };
        harness.Owner.Content = view;
        harness.Draw();
        Assert.Equal(1, surface.Requests);
        PixelCanvas firstCanvas = surface.Canvas;

        for (int version = 2; version <= 5; version++)
        {
            view.Version = version;
            harness.Draw();
        }
        Assert.Equal(1, surface.Requests);
        Assert.Same(firstCanvas, surface.Canvas);

        surface.Canvas.Clear(Color.Red);
        surface.Complete(null);
        harness.Draw();
        Assert.Equal(2, surface.Requests);
        surface.Complete(null);
        harness.Draw();
        harness.Draw();
        Assert.Equal(2, surface.Requests);
    }

    [Fact]
    public void ResizeWaitsForTheAcceptedFrameBeforeRequestingTheNewSize()
    {
        using var generated = new FakeGeneratedTextures();
        using var harness = new DialogHarness(64, 48);
        var surface = new DeferredSurface();
        var view = new CartographyMapView { Surface = surface, Width = 40, Height = 32 };
        harness.Owner.Content = view;
        harness.Draw();
        Assert.Equal(40, surface.Canvas.Width);

        view.Width = 60;
        harness.Draw();
        Assert.Equal(1, surface.Requests);
        Assert.Equal(40, surface.Canvas.Width);
        surface.Complete(null);
        harness.Draw();
        Assert.Equal(2, surface.Requests);
        Assert.Equal(60, surface.Canvas.Width);
        surface.Complete(null);
    }

    [Fact]
    public void KeepsTheLastTextureUntilCompletionAndDoesNotQueueWhileBusy()
    {
        using var generated = new FakeGeneratedTextures();
        using var texture = new AsyncMapTexture(new Vector2I(4, 3));
        var surface = new DeferredSurface();
        string name = texture.Source.Key;
        byte[] previous = FakeGeneratedTextures.Textures[name].Pixels;
        int callbacks = 0;

        Assert.True(texture.Request(surface, published => { Assert.True(published); callbacks++; }));
        Assert.True(texture.IsPending);
        Assert.False(texture.Request(surface, _ => throw new Exception("Must not queue")));
        surface.Canvas.Clear(Color.Red);
        Assert.Same(previous, FakeGeneratedTextures.Textures[name].Pixels);
        Assert.Equal(0, callbacks);
        Assert.Equal(1, surface.Requests);

        surface.Complete(null);
        Assert.False(texture.IsPending);
        Assert.Equal(1, callbacks);
        Assert.Equal(1, LcdDynamicTexture.GetContentVersion(name));
        byte[] publishedPixels = FakeGeneratedTextures.Textures[name].Pixels;
        Assert.NotSame(previous, publishedPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, publishedPixels.Take(4));

        Assert.True(texture.Request(surface, _ => { }));
        surface.Canvas.Clear(Color.Blue);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, publishedPixels.Take(4));
        surface.Complete(null);
    }

    [Fact]
    public void AFailedFramePreservesTheLastUploadAndAllowsRetry()
    {
        using var generated = new FakeGeneratedTextures();
        using var texture = new AsyncMapTexture(new Vector2I(4, 3));
        var surface = new DeferredSurface();
        string name = texture.Source.Key;
        byte[] previous = FakeGeneratedTextures.Textures[name].Pixels;
        Assert.True(texture.Request(surface, published => Assert.False(published)));
        surface.Canvas.Clear(Color.Red);
        surface.Complete(new InvalidOperationException("worker failed"));
        Assert.Same(previous, FakeGeneratedTextures.Textures[name].Pixels);
        Assert.False(texture.IsPending);
        Assert.True(texture.Request(surface, published => Assert.True(published)));
        surface.Canvas.Clear(Color.Blue);
        surface.Complete(null);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, FakeGeneratedTextures.Textures[name].Pixels.Take(4));
    }

    [Fact]
    public void DisposalDuringRenderingNeverUploadsOrCallsTheView()
    {
        using var generated = new FakeGeneratedTextures();
        var texture = new AsyncMapTexture(new Vector2I(4, 3));
        var surface = new DeferredSurface();
        string name = texture.Source.Key;
        byte[] previous = FakeGeneratedTextures.Textures[name].Pixels;
        Assert.True(texture.Request(surface, _ => throw new Exception("view was disposed")));
        texture.Dispose();
        Assert.False(texture.IsAvailable);
        Assert.False(texture.Request(surface, _ => { }));
        surface.Canvas.Clear(Color.Red);
        surface.Complete(null);
        Assert.Same(previous, FakeGeneratedTextures.Textures[name].Pixels);
        Assert.Equal(0, LcdDynamicTexture.GetContentVersion(name));
        Assert.False(texture.IsPending);
    }

    [Fact]
    public void ARejectedRequestCanBeRetriedWithoutPublishingAnything()
    {
        using var generated = new FakeGeneratedTextures();
        using var texture = new AsyncMapTexture(new Vector2I(4, 3));
        var surface = new DeferredSurface { Accept = false };
        Assert.False(texture.Request(surface, _ => throw new Exception("request rejected")));
        Assert.False(texture.IsPending);
        surface.Accept = true;
        Assert.True(texture.Request(surface, published => Assert.True(published)));
        surface.Complete(null);
    }

    [Fact]
    public void ParallelBandsOwnEveryRowExactlyOnceAndKeepCommandOrder()
    {
        const int width = 7;
        const int height = 53; // Includes a partial final band.
        var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var canvas = new PixelCanvas(pixels, width, height);
        var visits = new int[height];
        var frame = new MapRenderFrame();
        frame.Initialize = () => frame.DepthBuffer = new float[width * height];
        frame.Commands.Add((target, first, end) =>
        {
            for (int y = first; y < end; y++)
            {
                Assert.Equal(1, Interlocked.Increment(ref visits[y]));
                frame.DepthBuffer[y * width] = y + 1;
                target.SetPixel(0, y, Color.Red);
            }
        });
        frame.Commands.Add((target, first, end) =>
        {
            for (int y = first; y < end; y++)
            {
                Assert.Equal(y + 1, frame.DepthBuffer[y * width]);
                target.SetPixel(0, y, Color.Blue);
            }
        });
        bool finished = false;
        frame.Finish = _ =>
        {
            Assert.All(visits, count => Assert.Equal(1, count));
            finished = true;
        };
        frame.Paint(canvas, (first, end, paint) => Parallel.For(first, end, paint));
        Assert.True(finished);
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels.Skip(y * width * 4).Take(4));
            Assert.All(pixels.Skip((y * width + 1) * 4).Take((width - 1) * 4), value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void WorkerFailureIsReportedAfterAllBandsStopAndSkipsFinalComposition()
    {
        var frame = new MapRenderFrame();
        int completedRows = 0;
        frame.Commands.Add((canvas, first, end) =>
        {
            if (first == 0)
                throw new InvalidOperationException("sampling failed");
            Interlocked.Add(ref completedRows, end - first);
        });
        frame.Finish = _ => throw new Exception("Must not compose a failed frame");
        var error = Assert.Throws<Exception>(() => frame.Paint(
            new PixelCanvas(new byte[4 * 53], 1, 53),
            (first, end, paint) => Parallel.For(first, end, paint)));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(37, completedRows);
    }
}
