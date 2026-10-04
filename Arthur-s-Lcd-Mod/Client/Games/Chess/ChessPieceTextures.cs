using System;
using System.Collections.Generic;
using System.Text;
using Adk.Gui.Lcd;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Games.Chess
{
    public partial class ChessGame
    {
        const int PIECE_TEXTURE_UPSCALE = 8;
        const int PALETTE_GLYPH_BASE = 0xE100;
        const char PALETTE_PROBE_GLYPH = '';

        static readonly Dictionary<byte, PieceBitmap> PieceBitmaps = new Dictionary<byte, PieceBitmap>();
        static readonly Dictionary<byte, LcdDynamicTexture> PieceTextures = new Dictionary<byte, LcdDynamicTexture>();

        Vector2 _piecePixelSize;
        float _pieceMaxSize;

        internal static void ReleasePieceTextures()
        {
            foreach (var texture in PieceTextures.Values)
                texture.Dispose();
            PieceTextures.Clear();
            PieceBitmaps.Clear();
        }

        void UpdatePieceScale(float cellSize)
        {
            var glyph = _panel.MeasureStringInPixels(new StringBuilder().Append(PALETTE_PROBE_GLYPH),
                MOD_PREFIX + "Monospace", 1);
            var reference = GetPieceBitmap(0x01);
            int widestRow = reference == null ? 1 : Math.Max(1, reference.WidestOpaqueRow);
            Scale = glyph.X > 0f ? cellSize * .8f / (widestRow * glyph.X) : 1f;
            _piecePixelSize = glyph * Scale;
            _pieceMaxSize = cellSize * .8f;
        }

        internal bool TryCreatePieceSprite(byte textureId, Vector2 topCenter, out MySprite sprite)
        {
            sprite = default(MySprite);
            var bitmap = GetPieceBitmap((byte)(textureId & 0xEF));
            var texture = GetPieceTexture(textureId, bitmap);
            if (texture == null)
                return false;

            var size = new Vector2(bitmap.Width * _piecePixelSize.X, bitmap.Height * _piecePixelSize.Y);
            // The pixel aspect comes from the font glyph; keep the whole piece inside its cell either way.
            float largest = Math.Max(size.X, size.Y);
            if (largest > _pieceMaxSize && largest > 0f)
                size *= _pieceMaxSize / largest;
            sprite = new MySprite(SpriteType.TEXTURE, texture.Name, topCenter + new Vector2(0f, size.Y * .5f), size,
                Color.White, alignment: TextAlignment.CENTER);
            return true;
        }

        static LcdDynamicTexture GetPieceTexture(byte textureId, PieceBitmap bitmap)
        {
            if (bitmap == null)
                return null;

            LcdDynamicTexture texture;
            if (PieceTextures.TryGetValue(textureId, out texture) && texture.IsAvailable)
                return texture;

            texture?.Dispose();
            texture = new LcdDynamicTexture("Chess_" + textureId,
                new Vector2I(bitmap.Width * PIECE_TEXTURE_UPSCALE, bitmap.Height * PIECE_TEXTURE_UPSCALE));
            int paletteOffset = (textureId & 0x10) == 0 ? 0 : 3;
            if (!texture.Update(canvas =>
                {
                    int scale = Math.Max(1, canvas.Width / bitmap.Width);
                    canvas.Clear(Color.Transparent);
                    for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = bitmap.Indices[y * bitmap.Width + x];
                        if (index != 0)
                            canvas.FillRectangle(x * scale, y * scale, scale, scale,
                                PaletteColor(TEXTURE_ATLAS[paletteOffset + index - 1]));
                    }
                }))
            {
                texture.Dispose();
                return null;
            }

            PieceTextures[textureId] = texture;
            return texture;
        }

        static Color PaletteColor(char glyph)
        {
            int value = glyph - PALETTE_GLYPH_BASE;
            return new Color(((value >> 6) & 7) * 255 / 7, ((value >> 3) & 7) * 255 / 7, (value & 7) * 255 / 7);
        }

        static PieceBitmap GetPieceBitmap(byte textureIndex)
        {
            if (textureIndex == 0)
                return null;

            PieceBitmap bitmap;
            if (PieceBitmaps.TryGetValue(textureIndex, out bitmap))
                return bitmap;

            int seeker = 0;
            int textureSize = 6;
            for (int i = 0; i < textureIndex; i++)
            {
                seeker += textureSize;
                if (seeker >= TEXTURE_ATLAS.Length)
                    return null;
                textureSize = (int)(((TEXTURE_ATLAS[seeker] >> 8) & 0x7F) / 7f + .5f) *
                    ((TEXTURE_ATLAS[seeker] >> 1) & 0x7F) + 1;
            }

            if (seeker + textureSize > TEXTURE_ATLAS.Length)
                return null;
            bitmap = PieceBitmap.Unpack(TEXTURE_ATLAS, seeker, textureSize);
            PieceBitmaps[textureIndex] = bitmap;
            return bitmap;
        }

        sealed class PieceBitmap
        {
            public int Width;
            public int Height;
            public byte[] Indices;
            public int WidestOpaqueRow;

            public static PieceBitmap Unpack(string atlas, int offset, int length)
            {
                int header = atlas[offset];
                int width = (header >> 8) & 0x7F;
                int height = (header >> 1) & 0x7F;
                var bitmap = new PieceBitmap { Width = width, Height = height, Indices = new byte[width * height] };
                int index = offset + 1;
                int end = offset + length;
                for (int y = 0; y < height; y++)
                {
                    int opaque = 0;
                    for (int x = 0; x < width; x += 7)
                    {
                        if (index >= end)
                            throw new ArgumentException("Packed chess texture is too short.");

                        int bits = atlas[index++] >> 1;
                        for (int i = 6; i >= 0; i--)
                        {
                            int position = x + (6 - i);
                            if (position >= width)
                                continue;
                            byte color = (byte)((bits >> (i * 2)) & 0x3);
                            bitmap.Indices[y * width + position] = color;
                            if (color != 0)
                                opaque++;
                        }
                    }

                    bitmap.WidestOpaqueRow = Math.Max(bitmap.WidestOpaqueRow, opaque);
                }

                return bitmap;
            }
        }
    }
}
