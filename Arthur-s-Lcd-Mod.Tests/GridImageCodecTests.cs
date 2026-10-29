using System.Security.Cryptography;
using Adk.Image.Dds;
using Adk.Image.Png;
using Adk.Compression.Zip;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Images;
using ProtoBuf;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class GridImageCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(119)]
    [InlineData(100_003)]
    public void ImageSha256_MatchesRuntimeSha256(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);

        Assert.Equal(SHA256.HashData(data), ImageSha256.Compute(data));
    }

    [Fact]
    public void TryEncode_PngRoundTripsToTheSameDdsAndId()
    {
        byte[] png = PngEncoder.Encode(Photo(70, 45), 70, 45, DeflateLevel.Fast);

        Assert.True(GridImageCodec.TryEncode("photo.png", png, out var image, out var error), error);
        Assert.True(GridImageCodec.TryDecode(image.Id, image.Payload, out var dds, out error), error);

        Assert.Equal(image.Id, GridImageCodec.ComputeId(dds));
        Assert.True(GridImageCodec.TryReadDdsSize(dds, out int width, out int height));
        Assert.Equal((72, 48), (width, height));
        Assert.Equal((72, 48), (image.Width, image.Height));
    }

    [Fact]
    public void TryEncode_FlatImageStoresPngThatRebuildsIdenticalDds()
    {
        var rgba = new byte[256 * 256 * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)(i / 4 % 256 < 128 ? 200 : 20);
            rgba[i + 1] = 90;
            rgba[i + 2] = 30;
            rgba[i + 3] = 255;
        }

        byte[] png = PngEncoder.Encode(rgba, 256, 256, DeflateLevel.Fast);

        Assert.True(GridImageCodec.TryEncode("flat.png", png, out var image, out var error), error);
        Assert.Equal(GridImageEncoding.Png, image.Payload.Encoding);
        Assert.Equal(GridImageCodec.LCD_PROFILE, image.Payload.Profile);
        Assert.True(GridImageCodec.TryDecode(image.Id, image.Payload, out _, out error), error);
    }

    [Fact]
    public void TryEncode_DdsKeepsTheSourceBytes()
    {
        byte[] dds = DdsEncoder.Encode(Photo(64, 32), 64, 32, 64 * 4, GridImageCodec.CreateLcdOptions(GridImageCodec.LCD_PROFILE));

        Assert.True(GridImageCodec.TryEncode("photo.dds", dds, out var image, out var error), error);
        Assert.Equal(GridImageEncoding.DeflatedDds, image.Payload.Encoding);
        Assert.True(GridImageCodec.TryDecode(image.Id, image.Payload, out var rebuilt, out error), error);
        Assert.Equal(dds, rebuilt);
    }

    [Fact]
    public void TryEncode_RejectsDdsThatIsNotBlockAligned()
    {
        byte[] dds = DdsEncoder.Encode(Photo(30, 32), 30, 32, 30 * 4, GridImageCodec.CreateLcdOptions(GridImageCodec.LCD_PROFILE));

        Assert.False(GridImageCodec.TryEncode("odd.dds", dds, out _, out var error));
        Assert.Contains("divisible by 4", error);
    }

    [Fact]
    public void TryDecode_RejectsPayloadThatDoesNotMatchItsId()
    {
        byte[] dds = DdsEncoder.Encode(Photo(32, 32), 32, 32, 32 * 4, GridImageCodec.CreateLcdOptions(GridImageCodec.LCD_PROFILE));
        Assert.True(GridImageCodec.TryEncodeDds(dds, out var image, out _));

        string otherId = new string('0', 32);

        Assert.False(GridImageCodec.TryDecode(otherId, image.Payload, out var rebuilt, out var error));
        Assert.Null(rebuilt);
        Assert.Contains("does not match", error);
    }

    [Fact]
    public void CollectSpriteIds_FindsImagesInSerializedPictureFrameConfig()
    {
        string first = new string('a', 32);
        string second = "0123456789abcdef0123456789abcdef";
        var config = new DigitalPictureFramesConfigComponent
        {
            BackgroundSprite = GridImageCodec.ToSpriteName(first),
            SelectedSprites = new[] { GridImageCodec.ToSpriteName(first), "LCD_Economy_Badge", GridImageCodec.ToSpriteName(second), "LcdImg_notanid" }
        };

        using var stream = new MemoryStream();
        Serializer.Serialize<ConfigComponent>(stream, config);
        var ids = new HashSet<string>();
        GridImageCodec.CollectSpriteIds(stream.ToArray(), ids);

        Assert.Equal(new[] { first, second }.OrderBy(id => id), ids.OrderBy(id => id));
    }

    static byte[] Photo(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var random = new Random(width * 31 + height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            rgba[offset] = (byte)(x * 3 + random.Next(8));
            rgba[offset + 1] = (byte)(y * 5 + random.Next(8));
            rgba[offset + 2] = (byte)((x ^ y) + random.Next(8));
            rgba[offset + 3] = (byte)(x < width / 2 ? 255 : 128);
        }

        return rgba;
    }
}
