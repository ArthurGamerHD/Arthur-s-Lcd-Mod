using System;
using System.IO;
using VRageMath;

namespace LcdMod.Common.Imaging
{
    /// <summary>Immutable DDS cube in Direct3D face order (+X, -X, +Y, -Y, +Z, -Z).</summary>
    internal sealed class SkyboxCubemap
    {
        readonly Color[][] _faces;
        readonly int _side;

        SkyboxCubemap(Color[][] faces, int side) { _faces = faces; _side = side; }

        public static SkyboxCubemap Load(Stream stream, int maximumSide = 512)
        {
            if (maximumSide < 1) throw new InvalidOperationException(nameof(maximumSide));
            var reader = new BinaryReader(stream);
            byte[] header = Read(reader, 128);
            if (U32(header, 0) != 0x20534444 || U32(header, 4) != 124 || U32(header, 76) != 32)
                throw new InvalidOperationException("Invalid DDS header.");
            int side = (int)U32(header, 16);
            int mips = Math.Max(1, (int)U32(header, 28));
            if (side < 1 || side > 16384 || U32(header, 12) != side || mips > 15)
                throw new InvalidOperationException("Invalid DDS cube dimensions.");
            uint fourcc = U32(header, 84);
            int format;
            int bytesPerPixel = (int)U32(header, 88) / 8;
            uint red = U32(header, 92), green = U32(header, 96), blue = U32(header, 100);
            if (fourcc == 0x30315844 && (U32(header, 80) & 4) != 0)
            {
                byte[] dx10 = Read(reader, 20);
                if (U32(dx10, 4) != 3 || (U32(dx10, 8) & 4) == 0 || U32(dx10, 12) != 1)
                    throw new InvalidOperationException("DDS must contain one TextureCube.");
                uint dxgi = U32(dx10, 0);
                if (dxgi == 71 || dxgi == 72) format = 1;
                else if (dxgi == 74 || dxgi == 75) format = 2;
                else if (dxgi == 77 || dxgi == 78) format = 3;
                else if (dxgi == 98 || dxgi == 99) format = 7;
                else if (dxgi == 28 || dxgi == 29 || dxgi == 87 || dxgi == 91)
                {
                    format = 0;
                    bytesPerPixel = 4;
                    red = dxgi == 28 || dxgi == 29 ? 0xffu : 0xff0000u;
                    green = 0xff00;
                    blue = red == 0xff ? 0xff0000u : 0xffu;
                }
                else throw new InvalidOperationException("Unsupported skybox DDS DXGI format: " + dxgi);
            }
            else
            {
                if ((U32(header, 112) & 0xfe00) != 0xfe00)
                    throw new InvalidOperationException("DDS is missing cube faces.");
                if ((U32(header, 80) & 4) != 0)
                {
                    if (fourcc == 0x31545844) format = 1;
                    else if (fourcc == 0x33545844) format = 2;
                    else if (fourcc == 0x35545844) format = 3;
                    else throw new InvalidOperationException("Unsupported skybox DDS FourCC.");
                }
                else if ((U32(header, 80) & 0x40) != 0 && (bytesPerPixel == 3 || bytesPerPixel == 4)) format = 0;
                else throw new InvalidOperationException("Unsupported skybox DDS pixel format.");
            }

            int selectedMip = 0;
            while (selectedMip + 1 < mips && (side >> selectedMip) > maximumSide) selectedMip++;
            int selectedSide = Math.Max(1, side >> selectedMip);
            // A mipless mod texture still gets bounded output; only one source row/block is retained.
            int outputSide = Math.Min(selectedSide, maximumSide);
            var faces = new Color[6][];
            var bc7 = new Bc7Decoder();
            byte[] block = new byte[64];
            byte[] skip = new byte[8192];
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < mips; mip++)
                {
                    int size = Math.Max(1, side >> mip);
                    int pitch = format == 0 ? size * bytesPerPixel : Math.Max(1, (size + 3) / 4) * (format == 1 ? 8 : 16);
                    int rows = format == 0 ? size : Math.Max(1, (size + 3) / 4);
                    if (mip != selectedMip) { Skip(reader, (long)pitch * rows, skip); continue; }
                    var pixels = new Color[outputSide * outputSide];
                    faces[face] = pixels;
                    for (int row = 0; row < rows; row++)
                    {
                        byte[] source = Read(reader, pitch);
                        if (format == 0)
                        {
                            for (int y = 0; y < outputSide; y++)
                            {
                                if (y * size / outputSide != row) continue;
                                for (int x = 0; x < outputSide; x++)
                                {
                                    int at = x * size / outputSide * bytesPerPixel;
                                    uint value = 0;
                                    for (int b = 0; b < bytesPerPixel; b++) value |= (uint)source[at + b] << (8 * b);
                                    pixels[y * outputSide + x] = new Color(Channel(value, red), Channel(value, green), Channel(value, blue));
                                }
                            }
                            continue;
                        }
                        int blockBytes = format == 1 ? 8 : 16;
                        for (int bx = 0; bx < (size + 3) / 4; bx++)
                        {
                            if (format == 7) bc7.Decode(source, bx * blockBytes, block);
                            else DecodeBcColor(source, bx * blockBytes + (format == 1 ? 0 : 8), format == 1, block);
                            int firstX = (bx * 4 * outputSide + size - 1) / size;
                            int firstY = (row * 4 * outputSide + size - 1) / size;
                            for (int y = firstY; y < outputSide && y * size / outputSide < row * 4 + 4; y++)
                            for (int x = firstX; x < outputSide && x * size / outputSide < bx * 4 + 4; x++)
                            {
                                int at = ((y * size / outputSide % 4) * 4 + x * size / outputSide % 4) * 4;
                                pixels[y * outputSide + x] = new Color(block[at], block[at + 1], block[at + 2]);
                            }
                        }
                    }
                }
            }
            return new SkyboxCubemap(faces, outputSide);
        }

        public Color Sample(Vector3 direction)
        {
            float ax = Math.Abs(direction.X), ay = Math.Abs(direction.Y), az = Math.Abs(direction.Z);
            float u, v, major;
            int face;
            if (ax >= ay && ax >= az)
            { face = direction.X >= 0 ? 0 : 1; u = direction.X >= 0 ? -direction.Z : direction.Z; v = -direction.Y; major = ax; }
            else if (ay >= az)
            { face = direction.Y >= 0 ? 2 : 3; u = direction.X; v = direction.Y >= 0 ? direction.Z : -direction.Z; major = ay; }
            else
            { face = direction.Z >= 0 ? 4 : 5; u = direction.Z >= 0 ? direction.X : -direction.X; v = -direction.Y; major = az; }
            if (major <= 0f) return Color.Black;
            float x = MathHelper.Clamp((u / major + 1f) * .5f * _side - .5f, 0, _side - 1);
            float y = MathHelper.Clamp((v / major + 1f) * .5f * _side - .5f, 0, _side - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, _side - 1), y1 = Math.Min(y0 + 1, _side - 1);
            Color[] pixels = _faces[face];
            return Color.Lerp(Color.Lerp(pixels[y0 * _side + x0], pixels[y0 * _side + x1], x - x0),
                Color.Lerp(pixels[y1 * _side + x0], pixels[y1 * _side + x1], x - x0), y - y0);
        }

        static void DecodeBcColor(byte[] source, int offset, bool bc1, byte[] block)
        {
            int c0 = source[offset] | source[offset + 1] << 8;
            int c1 = source[offset + 2] | source[offset + 3] << 8;
            uint indices = U32(source, offset + 4);
            for (int i = 0; i < 16; i++)
            {
                int index = (int)((indices >> (i * 2)) & 3);
                for (int c = 0; c < 3; c++)
                {
                    int shift = c == 0 ? 11 : c == 1 ? 5 : 0, mask = c == 1 ? 63 : 31;
                    int a = (c0 >> shift) & mask, b = (c1 >> shift) & mask;
                    a = c == 1 ? (a << 2) | (a >> 4) : (a << 3) | (a >> 2);
                    b = c == 1 ? (b << 2) | (b >> 4) : (b << 3) | (b >> 2);
                    block[i * 4 + c] = (byte)(index == 0 ? a : index == 1 ? b : bc1 && c0 <= c1
                        ? index == 2 ? (a + b) / 2 : 0 : index == 2 ? (2 * a + b) / 3 : (a + 2 * b) / 3);
                }
            }
        }

        static byte Channel(uint value, uint mask)
        {
            if (mask == 0) return 0;
            while ((mask & 1) == 0) { mask >>= 1; value >>= 1; }
            return (byte)((value & mask) * 255L / mask);
        }
        static uint U32(byte[] data, int at)
        { return (uint)(data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24); }
        static byte[] Read(BinaryReader reader, int count)
        {
            byte[] data = reader.ReadBytes(count);
            if (data.Length != count) throw new InvalidOperationException("Truncated skybox DDS.");
            return data;
        }
        static void Skip(BinaryReader reader, long count, byte[] buffer)
        {
            if (reader.BaseStream.CanSeek)
            {
                if (count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidOperationException();
                reader.BaseStream.Position += count;
                return;
            }
            while (count > 0)
            {
                int read = reader.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
                if (read == 0) throw new InvalidOperationException();
                count -= read;
            }
        }
    }
}
