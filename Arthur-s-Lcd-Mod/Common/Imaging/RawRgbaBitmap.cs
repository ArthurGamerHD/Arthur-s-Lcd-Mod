using LcdMod.Common.Exceptions;

namespace LcdMod.Common.Imaging
{
    /// <summary>
    /// Generated top-to-bottom RGBA8 bitmap.
    /// </summary>
    public sealed class RawRgbaBitmap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public byte[] Pixels { get; private set; }

        public RawRgbaBitmap(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Stride = checked(width * 4);
            Pixels = new byte[checked(Stride * height)];
        }

        public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException("x/y");

            int offset = checked(y * Stride + x * 4);
            Pixels[offset] = r;
            Pixels[offset + 1] = g;
            Pixels[offset + 2] = b;
            Pixels[offset + 3] = a;
        }
    }
}
