using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class PillRenderingTests
{
    [Theory]
    [InlineData(80, 8)]
    [InlineData(8, 80)]
    public void OpaquePillsUseThreePrimitivesInsideTheirBounds(float width, float height)
    {
        using var border = new Border { CornerRadius = new Vector4(9999), BackgroundColor = Color.White };
        var renderer = new Renderer();
        var tree = new VisualTree(border, renderer);
        tree.ArrangeViewport(new RectangleF(32, 48, width * 2, height * 2), 2);
        tree.Draw(1);
        Assert.Empty(renderer.Created);
        Assert.Equal(3, renderer.Draws.Count);
        Assert.Equal(2, renderer.Draws.Count(d => d.Texture.Key == "Circle"));
        foreach (var draw in renderer.Draws)
        {
            Assert.InRange(draw.Bounds.X, 32, 32 + width * 2);
            Assert.InRange(draw.Bounds.Right, 32, 32 + width * 2);
            Assert.InRange(draw.Bounds.Y, 48, 48 + height * 2);
            Assert.InRange(draw.Bounds.Bottom, 48, 48 + height * 2);
        }
    }

    [Theory]
    [InlineData(128, 1f)]
    [InlineData(255, .5f)]
    public void TranslucentPillsUseOneCachedAlphaMaskAndStopUsingItWhenOpaque(byte alpha, float opacity)
    {
        using var border = new Border { CornerRadius = new Vector4(9999), BackgroundColor = new Color(80, 100, 120, alpha) };
        var renderer = new Renderer();
        var tree = new VisualTree(border, renderer);
        tree.ArrangeViewport(new RectangleF(0, 0, 80, 8), 1);
        tree.Draw(opacity);
        var texture = Assert.Single(renderer.Created);
        var draw = Assert.Single(renderer.Draws);
        Assert.Same(texture, draw.Texture.NativeHandle);
        Assert.InRange(draw.Color.A, 127, 128);
        Assert.Equal(0, texture.Pixels[3]); // Transparent corner.
        Assert.Equal(255, texture.Pixels[(4 * 80 + 40) * 4 + 3]); // Solid center mask.

        renderer.Draws.Clear();
        tree.Draw(opacity);
        Assert.Single(renderer.Created);
        Assert.Single(renderer.Draws);
        border.BackgroundColor = Color.White;
        renderer.Draws.Clear();
        tree.Draw(1);
        Assert.Equal(3, renderer.Draws.Count);
        Assert.DoesNotContain(renderer.Draws, d => ReferenceEquals(d.Texture.NativeHandle, texture));
    }

    [Fact]
    public void EachCornerUsesItsOwnRadius()
    {
        using var border = new Border { CornerRadius = new Vector4(12, 12, 0, 0), BackgroundColor = new Color(80, 100, 120, 128) };
        var renderer = new Renderer();
        var tree = new VisualTree(border, renderer);
        tree.ArrangeViewport(new RectangleF(0, 0, 40, 40), 1);
        tree.Draw(1);
        var texture = Assert.Single(renderer.Created);
        byte Alpha(int x, int y) => texture.Pixels[(y * 40 + x) * 4 + 3];
        Assert.Equal(0, Alpha(0, 0));
        Assert.Equal(0, Alpha(39, 0));
        Assert.Equal(255, Alpha(0, 39));
        Assert.Equal(255, Alpha(39, 39));
        Assert.Equal(255, Alpha(20, 0));
    }

    [Fact]
    public void FloatCornerRadiiStillApplyToEveryCorner()
    {
        var model = new CornerRadiusModel();
        using var view = new CornerRadiusView(model);
        Assert.Equal(new Vector4(4), view.Bound.CornerRadius);
        model.Radius = 9;
        Assert.Equal(new Vector4(9), view.Bound.CornerRadius);
        Assert.Equal(new Vector4(6), view.Uniform.CornerRadius);
        Assert.Equal(new Vector4(6, 6, 0, 0), view.PerCorner.CornerRadius);

        var sheet = StyleSheet.Load("""
            <Styles xmlns="https://github.com/ArthurGamerHD/Adk">
              <Style Selector="Border.styled"><Setter Property="CornerRadius" Value="5" /></Style>
              <Style Selector="Button.styled">
                <Setter Property="CornerRadius" Value="3" />
                <Setter Property="Template">
                  <ControlTemplate><Border x:Name="Chrome" CornerRadius="{TemplateBinding CornerRadius}" /></ControlTemplate>
                </Setter>
              </Style>
            </Styles>
            """);
        var styled = new Border { StyleClass = "styled" };
        var button = new Button { StyleClass = "styled" };
        using var root = new Adk.Gui.Layout.StackPanel { Styles = sheet };
        root.AddChild(styled);
        root.AddChild(button);
        var tree = new VisualTree(root, new Renderer());
        tree.ArrangeViewport(new RectangleF(0, 0, 40, 40), 1);
        Assert.Equal(new Vector4(5), styled.CornerRadius);
        Assert.Equal(new Vector4(3), Assert.IsType<Border>(button.GetTemplateChild("Chrome")).CornerRadius);
    }

    [Fact]
    public void SegmentedBarSplitsWidthProportionallyWithGaps()
    {
        var ready = new Color(0, 200, 0);
        var error = new Color(200, 0, 0);
        using var bar = new LcdMod.Client.Apps.Controls.SegmentedBar
        {
            Track = Color.Gray,
            Gap = 2,
            Segments = new[]
            {
                new LcdMod.Client.Apps.Controls.BarSegment(3, ready),
                new LcdMod.Client.Apps.Controls.BarSegment(0, Color.Yellow),
                new LcdMod.Client.Apps.Controls.BarSegment(1, error)
            }
        };
        var renderer = new Renderer();
        var tree = new VisualTree(bar, renderer);
        tree.ArrangeViewport(new RectangleF(0, 0, 102, 8), 1);
        tree.Draw(1);

        Assert.DoesNotContain(renderer.Draws, d => d.Color == Color.Yellow);
        var readyFill = Assert.Single(renderer.Draws, d => d.Color == ready && d.Texture.Key == null);
        var errorFill = Assert.Single(renderer.Draws, d => d.Color == error && d.Texture.Key == null);
        Assert.Equal(4, readyFill.Bounds.X, 3);
        Assert.Equal(71, readyFill.Bounds.Right, 3);
        Assert.Equal(81, errorFill.Bounds.X, 3);
        Assert.Equal(98, errorFill.Bounds.Right, 3);
    }

    [Fact]
    public void RestylingWithUnchangedValuesKeepsTheRoundedTexture()
    {
        var sheet = StyleSheet.Load("""
            <Styles xmlns="https://github.com/ArthurGamerHD/Adk">
              <Style Selector="Border.card">
                <Setter Property="CornerRadius" Value="10" />
                <Setter Property="Background" Value="#80406080" />
              </Style>
            </Styles>
            """);
        using var card = new Border { StyleClass = "card", Styles = sheet };
        var renderer = new Renderer();
        var tree = new VisualTree(card, renderer);
        tree.ArrangeViewport(new RectangleF(0, 0, 120, 80), 1);
        tree.Draw(1);
        var texture = Assert.Single(renderer.Created);

        card.IsPointerOver = true;
        tree.Draw(1);
        card.IsFocusWithin = true;
        tree.Draw(1);
        card.IsPointerOver = false;
        tree.Draw(1);

        Assert.Single(renderer.Created);
        Assert.False(texture.Disposed);
    }

    [Fact]
    public void HoverColorsTintTheSameRoundedMasksInTheSameFrame()
    {
        var sheet = StyleSheet.Load("""
            <Styles xmlns="https://github.com/ArthurGamerHD/Adk">
              <Style Selector="Border.card">
                <Setter Property="CornerRadius" Value="10" />
                <Setter Property="Background" Value="#80406080" />
                <Setter Property="BorderBrush" Value="#FF000000" />
                <Setter Property="BorderThickness" Value="2" />
              </Style>
              <Style Selector="Border.card:pointerover">
                <Setter Property="Background" Value="#80804060" />
                <Setter Property="BorderBrush" Value="#FFFFFFFF" />
              </Style>
            </Styles>
            """);
        using var card = new Border { StyleClass = "card", Styles = sheet };
        var renderer = new Renderer();
        var tree = new VisualTree(card, renderer);
        tree.ArrangeViewport(new RectangleF(0, 0, 120, 80), 1);
        tree.Draw(1);
        Assert.Equal(2, renderer.Created.Count);

        card.IsPointerOver = true;
        renderer.Draws.Clear();
        tree.Draw(1);

        Assert.Equal(2, renderer.Created.Count);
        Assert.Equal(new[] { new Color(128, 64, 96, 128), Color.White }, renderer.Draws.Select(d => d.Color));
    }

    [Fact]
    public void BSoDQrCodeSharesOnePaintedTexture()
    {
        LcdMod.Client.Gui.UserControls.BSoDQrCode.Release();
        try
        {
            var renderer = new Renderer();
            using var qr = new LcdMod.Client.Gui.UserControls.BSoDQrCode();
            var tree = new VisualTree(qr, renderer);
            tree.ArrangeViewport(new RectangleF(0, 0, 104, 104), 1);
            tree.Draw(1);
            Assert.True(LcdMod.Client.Gui.UserControls.BSoDQrCode.CreatedThisFrame);
            var texture = Assert.Single(renderer.Created);
            Assert.Equal(new RectangleF(0, 0, 104, 104), Assert.Single(renderer.Draws).Bounds);

            // Module (x, y) from the top-left at 8 px per module; the top-right finder ring is light, its center dark.
            byte Red(int x, int y) => texture.Pixels[((y * 8 + 4) * texture.Size.X + x * 8 + 4) * 4];
            Assert.Equal(179, Red(0, 0));
            Assert.Equal(0, Red(1, 1));
            Assert.Equal(179, Red(3, 3));
            Assert.Equal(0, Red(7, 7));
            Assert.Equal(179, Red(39, 3));
            Assert.Equal(0, Red(44, 8));

            using var second = new LcdMod.Client.Gui.UserControls.BSoDQrCode();
            var secondTree = new VisualTree(second, renderer);
            secondTree.ArrangeViewport(new RectangleF(0, 0, 52, 52), 1);
            secondTree.Draw(1);
            Assert.False(LcdMod.Client.Gui.UserControls.BSoDQrCode.CreatedThisFrame);
            Assert.Single(renderer.Created);
        }
        finally
        {
            LcdMod.Client.Gui.UserControls.BSoDQrCode.Release();
        }
    }

    internal sealed class Renderer : IUiRenderer, IDynamicTextureFactory, ITextureResolver
    {
        public readonly List<Texture> Created = new();
        public readonly List<(TextureSource Texture, RectangleF Bounds, Color Color)> Draws = new();
        public Renderer() { BorderTextures = new BorderTextureCache(this); }
        public ITextMetrics TextMetrics => null;
        public ITextureResolver Textures => this;
        public IDynamicTextureFactory DynamicTextures => this;
        public BorderTextureCache BorderTextures { get; }
        public TextureSource MissingTexture => default;
        public bool IsAvailable(TextureSource source) => true;
        public bool TryGetSize(TextureSource source, out Vector2 size) { size = Vector2.Zero; return false; }
        public bool TryResolve(TextureSource source, out object value) { value = source.Key; return true; }
        public Vector2I FitToSupportedSize(Vector2I requested) => requested;
        public IDynamicTexture Create(string name, Vector2I size)
        {
            var texture = new Texture(size);
            Created.Add(texture);
            return texture;
        }
        public IDisposable PushClip(RectangleF bounds) => null;
        public void FillRectangle(RectangleF bounds, Color color) => Draws.Add((default, bounds, color));
        public void DrawTexture(TextureSource texture, RectangleF bounds, Color color, float rotation) => Draws.Add((texture, bounds, color));
        public void DrawText(string font, string text, Vector2 position, float scale, Color color,
            HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) { }
    }

    internal sealed class Texture : IDynamicTexture
    {
        public readonly byte[] Pixels;
        public bool Disposed;
        public Texture(Vector2I size) { Size = size; Pixels = new byte[size.X * size.Y * 4]; }
        public TextureSource Source => TextureSource.FromNative(this);
        public bool IsAvailable => !Disposed;
        public Vector2I Size { get; }
        public bool Update(Action<PixelCanvas> paint) { paint(new PixelCanvas(Pixels, Size.X, Size.Y)); return true; }
        public void Dispose() { Disposed = true; }
    }
}
