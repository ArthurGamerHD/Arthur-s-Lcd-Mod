using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown.Inline
{
    public sealed class ImageInline : InlineNode
    {
        public ImageInline()
        {
            AltText = string.Empty;
            Source = string.Empty;
            Kind = ImageType.Unknown;
        }

        public string AltText { get; set; }

        // Sprite: asset path without the "Sprite:" prefix.
        // Monospace: payload without the "Monospace:" prefix.
        public string Source { get; set; }

        public ImageType Kind { get; set; }

        public float Width { get; set; } = 64;
        public float Height { get; set; } = 64;

        public SizeType SizeType { get; set; } = SizeType.Pixel;
    }
}