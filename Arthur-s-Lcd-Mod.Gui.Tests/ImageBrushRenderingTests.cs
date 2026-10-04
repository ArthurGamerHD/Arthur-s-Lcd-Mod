using System.Reflection;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class ImageBrushRenderingTests
{
    const string Photo = "PictureFrameTestPhoto";

    [Fact]
    public void UniformKeepsTheImageAspectRatio()
    {
        MySprite photo = Render(new ImageBrush(Photo) { Stretch = Stretch.Uniform }, 400, 400).Single(s => s.Data == Photo);
        Assert.Equal(new Vector2(400, 200), photo.Size);
        Assert.Equal(new Vector2(200, 200), photo.Position);
    }

    [Fact]
    public void UniformToFillCoversTheBoundsAndClipsTheOverflow()
    {
        var sprites = Render(new ImageBrush(Photo) { Stretch = Stretch.UniformToFill }, 400, 400);
        Assert.Equal(new Vector2(800, 400), sprites.Single(s => s.Data == Photo).Size);
        Assert.Contains(sprites, s => s.Type == SpriteType.CLIP_RECT && s.Size == new Vector2(400, 400));
    }

    [Fact]
    public void CenterScalesTheNaturalSizeAroundTheMiddle()
    {
        var brush = new ImageBrush(Photo)
        {
            Stretch = Stretch.None, Transform = new ScaleTransform(1.5f, 1.5f), TransformOrigin = RelativePoint.Center
        };
        MySprite photo = Render(brush, 400, 400).Single(s => s.Data == Photo);
        Assert.Equal(new Vector2(300, 150), photo.Size);
        Assert.Equal(new Vector2(200, 200), photo.Position);
    }

    [Fact]
    public void TileRepeatsTheDestinationRectAcrossTheBounds()
    {
        var brush = new ImageBrush(Photo)
        {
            TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 200, 100, RelativeUnit.Absolute)
        };
        var tiles = Render(brush, 400, 300).Where(s => s.Data == Photo).ToList();
        Assert.Equal(6, tiles.Count);
        Assert.All(tiles, tile => Assert.Equal(new Vector2(200, 100), tile.Size));
        Assert.Contains(tiles, tile => tile.Position == new Vector2(300, 250));
    }

    static List<MySprite> Render(ImageBrush brush, float width, float height)
    {
        var grid = new Grid { Background = brush };
        var previousDefined = LcdTextureResolver.IsTextureDefined;
        var previousSize = LcdTextureResolver.TextureSize;
        LcdTextureResolver.IsTextureDefined = name => name == Photo;
        LcdTextureResolver.TextureSize = name => name == Photo ? new Vector2(200, 100) : Vector2.Zero;
        try
        {
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), grid);
            host.Arrange(new RectangleF(0, 0, width, height));
            var sprites = new List<MySprite>();
            host.Draw(sprites);
            return sprites;
        }
        finally
        {
            LcdTextureResolver.IsTextureDefined = previousDefined;
            LcdTextureResolver.TextureSize = previousSize;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class LcdTextureHooks
{
    public const string Name = "LCD texture hooks";
}
