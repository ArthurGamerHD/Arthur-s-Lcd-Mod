using System;
using Acrux.Lcd;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Images
{
    /// <summary>
    /// A generated texture that shows decoded frames. Frames are scaled into <see cref="Size"/> on a worker
    /// with <see cref="Scale"/>, then handed over on the game thread with <see cref="Upload"/>.
    /// </summary>
    internal sealed class FrameTexture : IDisposable
    {
        IMyGeneratedTexture _texture;

        public FrameTexture(string nameHint, int sourceWidth, int sourceHeight)
        {
            Size = LcdDynamicTexture.FitToRenderer(new Vector2I(sourceWidth, sourceHeight));
            if (Size.X > 0 && Size.Y > 0)
                _texture = MyAPIGateway.GeneratedTextures.Create(nameHint, Size);
        }

        public Vector2I Size { get; }
        public bool IsAvailable => _texture != null && _texture.IsValid;
        public string Name => IsAvailable ? _texture.Name : null;

        public bool Upload(byte[] pixels)
        {
            if (!IsAvailable || pixels == null)
                return false;

            byte[] buffer = _texture.BeginUpdate();
            if (buffer == null)
                return false;

            Buffer.BlockCopy(pixels, 0, buffer, 0, Math.Min(pixels.Length, buffer.Length));
            _texture.EndUpdate(buffer);
            LcdDynamicTexture.NotifyContentChanged(_texture.Name);
            return true;
        }

        public void Dispose()
        {
            if (_texture == null)
                return;

            LcdDynamicTexture.NotifyDisposed(_texture.Name);
            _texture.Dispose();
            _texture = null;
        }

        // Bilinear resize of RGBA8 source into target, premultiplying straight alpha when premultiply is set, as
        // generated textures expect.
        public static void Scale(byte[] source, int sourceWidth, int sourceHeight,
            byte[] target, int targetWidth, int targetHeight, bool premultiply)
        {
            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
            {
                if (!premultiply)
                {
                    Buffer.BlockCopy(source, 0, target, 0, targetWidth * targetHeight * 4);
                    return;
                }

                for (int i = 0; i < targetWidth * targetHeight * 4; i += 4)
                    Premultiply(source[i], source[i + 1], source[i + 2], source[i + 3], target, i);
                return;
            }

            int xStep = (sourceWidth << 16) / targetWidth;
            int yStep = (sourceHeight << 16) / targetHeight;
            int maxX = sourceWidth - 1;
            int maxY = sourceHeight - 1;
            for (int y = 0; y < targetHeight; y++)
            {
                int sy = Math.Max(0, y * yStep + (yStep >> 1) - 0x8000);
                int y0 = Math.Min(sy >> 16, maxY);
                int y1 = Math.Min(y0 + 1, maxY);
                int fy = (sy >> 8) & 0xFF;
                int row0 = y0 * sourceWidth * 4;
                int row1 = y1 * sourceWidth * 4;
                int output = y * targetWidth * 4;
                for (int x = 0; x < targetWidth; x++, output += 4)
                {
                    int sx = Math.Max(0, x * xStep + (xStep >> 1) - 0x8000);
                    int x0 = Math.Min(sx >> 16, maxX);
                    int x1 = Math.Min(x0 + 1, maxX);
                    int fx = (sx >> 8) & 0xFF;
                    int a = row0 + x0 * 4;
                    int b = row0 + x1 * 4;
                    int c = row1 + x0 * 4;
                    int d = row1 + x1 * 4;
                    byte red = Lerp(source, a, b, c, d, fx, fy);
                    byte green = Lerp(source, a + 1, b + 1, c + 1, d + 1, fx, fy);
                    byte blue = Lerp(source, a + 2, b + 2, c + 2, d + 2, fx, fy);
                    byte alpha = Lerp(source, a + 3, b + 3, c + 3, d + 3, fx, fy);
                    if (premultiply)
                    {
                        Premultiply(red, green, blue, alpha, target, output);
                    }
                    else
                    {
                        target[output] = red;
                        target[output + 1] = green;
                        target[output + 2] = blue;
                        target[output + 3] = alpha;
                    }
                }
            }
        }

        static byte Lerp(byte[] source, int a, int b, int c, int d, int fx, int fy)
        {
            int top = source[a] * (256 - fx) + source[b] * fx;
            int bottom = source[c] * (256 - fx) + source[d] * fx;
            return (byte)((top * (256 - fy) + bottom * fy + 32768) >> 16);
        }

        static void Premultiply(byte red, byte green, byte blue, byte alpha, byte[] target, int offset)
        {
            target[offset] = (byte)((red * alpha + 127) / 255);
            target[offset + 1] = (byte)((green * alpha + 127) / 255);
            target[offset + 2] = (byte)((blue * alpha + 127) / 255);
            target[offset + 3] = alpha;
        }
    }
}
