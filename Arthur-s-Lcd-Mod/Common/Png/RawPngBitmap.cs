using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArgumentOutOfRangeException = LcdMod.Common.ArgumentOutOfRangeException;
using EndOfStreamException = LcdMod.Common.EndOfStreamException;
using InvalidDataException = LcdMod.Common.InvalidDataException;
using ZipCrc32 = LcdMod.Common.Zip.Crc32;
using ZipZlib = LcdMod.Common.Zip.Zlib;

namespace LcdMod.Common.Png
{
    /// <summary>
    /// A decompressed, top-to-bottom RGBA8 PNG bitmap.
    /// For 16-bit source PNGs, RedSamples16 preserves the exact red/gray
    /// channel so planet height precision is not lost.
    /// </summary>
    public sealed class RawPngBitmap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public byte[] Pixels { get; private set; }
        public int SourceBitDepth { get; private set; }
        public int SourceColorType { get; private set; }
        public ushort[] RedSamples16 { get; private set; }

        internal RawPngBitmap(
            int width,
            int height,
            byte[] pixels,
            int sourceBitDepth,
            int sourceColorType,
            ushort[] redSamples16)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            if (pixels == null)
                throw new ArgumentNullException(nameof(pixels));

            if (pixels.Length != checked(width * height * 4))
                throw new ArgumentException("Unexpected RGBA buffer length.", nameof(pixels));

            if (redSamples16 != null && redSamples16.Length != checked(width * height))
                throw new ArgumentException("Unexpected 16-bit red buffer length.", nameof(redSamples16));

            Width = width;
            Height = height;
            Stride = checked(width * 4);
            Pixels = pixels;
            SourceBitDepth = sourceBitDepth;
            SourceColorType = sourceColorType;
            RedSamples16 = redSamples16;
        }

        public static RawPngBitmap Load(Stream input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return PngDecoder.Load(input);
        }

        public void GetRgba(int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int offset = PixelOffset(x, y);
            r = Pixels[offset];
            g = Pixels[offset + 1];
            b = Pixels[offset + 2];
            a = Pixels[offset + 3];
        }

        public byte GetHeight8(int x, int y)
        {
            return Pixels[PixelOffset(x, y)];
        }

        public ushort GetHeight16Expanded(int x, int y)
        {
            int pixelIndex = PixelIndex(x, y);
            if (RedSamples16 != null)
                return RedSamples16[pixelIndex];

            return (ushort)(Pixels[pixelIndex * 4] * 257);
        }

        public void GetMaterialIds(
            int x,
            int y,
            out byte materialId,
            out byte biomeId,
            out byte oreId)
        {
            int offset = PixelOffset(x, y);
            materialId = Pixels[offset];
            biomeId = Pixels[offset + 1];
            oreId = Pixels[offset + 2];
        }

        public RawPngBitmap CreateNearestPreview(int maxSide)
        {
            if (maxSide <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSide));

            if (Width <= maxSide && Height <= maxSide)
                return this;

            float scale = Math.Min((float)maxSide / Width, (float)maxSide / Height);
            int width = Math.Max(1, (int)(Width * scale));
            int height = Math.Max(1, (int)(Height * scale));
            byte[] pixels = new byte[checked(width * height * 4)];

            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Min(Height - 1, y * Height / height);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Min(Width - 1, x * Width / width);
                    int source = (sourceY * Width + sourceX) * 4;
                    int destination = (y * width + x) * 4;

                    pixels[destination] = Pixels[source];
                    pixels[destination + 1] = Pixels[source + 1];
                    pixels[destination + 2] = Pixels[source + 2];
                    pixels[destination + 3] = Pixels[source + 3];
                }
            }

            return new RawPngBitmap(
                width,
                height,
                pixels,
                SourceBitDepth,
                SourceColorType,
                null);
        }

        int PixelIndex(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException("Pixel coordinates are outside the bitmap.");

            return checked(y * Width + x);
        }

        int PixelOffset(int x, int y)
        {
            return checked(PixelIndex(x, y) * 4);
        }
    }

    public static class PlanetImageSet
    {
        public static readonly string[] FileNames =
        {
            "back.png", "down.png", "front.png", "left.png", "right.png", "up.png",
            "back_mat.png", "down_mat.png", "front_mat.png",
            "left_mat.png", "right_mat.png", "up_mat.png"
        };

        public static Dictionary<string, RawPngBitmap> LoadAll(Func<string, Stream> openFile)
        {
            if (openFile == null)
                throw new ArgumentNullException(nameof(openFile));

            Dictionary<string, RawPngBitmap> result =
                new Dictionary<string, RawPngBitmap>(StringComparer.OrdinalIgnoreCase);

            int expectedWidth = -1;
            int expectedHeight = -1;

            for (int i = 0; i < FileNames.Length; i++)
            {
                string fileName = FileNames[i];
                RawPngBitmap bitmap;

                try
                {
                    using (Stream input = openFile(fileName))
                        bitmap = RawPngBitmap.Load(input);
                }
                catch (Exception error)
                {
                    throw new InvalidDataException(
                        "Failed to decode '" + fileName + "': " + error.Message,
                        error);
                }

                if (expectedWidth < 0)
                {
                    expectedWidth = bitmap.Width;
                    expectedHeight = bitmap.Height;
                }
                else if (bitmap.Width != expectedWidth || bitmap.Height != expectedHeight)
                {
                    throw new InvalidDataException(
                        fileName + " does not have the same dimensions as the other faces.");
                }

                result.Add(Path.GetFileNameWithoutExtension(fileName), bitmap);
            }

            return result;
        }
    }

    /// <summary>
    /// Pure managed, non-interlaced PNG decoder.
    /// Supported PNG color types:
    ///   0 grayscale:       1, 2, 4, 8, 16 bits
    ///   2 truecolor RGB:   8, 16 bits
    ///   3 indexed/palette: 1, 2, 4, 8 bits
    ///   4 gray + alpha:    8, 16 bits
    ///   6 RGBA:            8, 16 bits
    /// Adam7 interlacing is intentionally not implemented.
    /// </summary>
    internal static class PngDecoder
    {
        static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };

        public static RawPngBitmap Load(Stream file)
        {
            byte[] signature = new byte[8];
            ReadExactly(file, signature, 0, signature.Length);
            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (signature[i] != PngSignature[i])
                    throw new InvalidDataException("The file is not a PNG image.");
            }

            int width = 0;
            int height = 0;
            int bitDepth = 0;
            int colorType = -1;
            bool sawHeader = false;
            bool sawImageData = false;
            bool sawEnd = false;
            byte[] palette = null;
            byte[] transparency = null;
            var idatChunks = new List<byte[]>();
            int idatSize = 0;

            while (!sawEnd)
            {
                uint unsignedLength = ReadUInt32BigEndian(file);
                if (unsignedLength > int.MaxValue)
                    throw new InvalidDataException("PNG chunk is too large.");

                int length = (int)unsignedLength;
                byte[] typeBytes = new byte[4];
                ReadExactly(file, typeBytes, 0, 4);
                byte[] data = new byte[length];
                ReadExactly(file, data, 0, length);

                uint expectedCrc = ReadUInt32BigEndian(file);
                uint actualCrc = ZipCrc32.Compute(typeBytes, data);
                if (expectedCrc != actualCrc)
                    throw new InvalidDataException("PNG chunk CRC check failed.");

                string type = Encoding.ASCII.GetString(typeBytes);
                if (type == "IHDR")
                {
                    if (sawHeader || length != 13)
                        throw new InvalidDataException("Invalid PNG IHDR chunk.");

                    width = checked((int)ReadUInt32BigEndian(data, 0));
                    height = checked((int)ReadUInt32BigEndian(data, 4));
                    bitDepth = data[8];
                    colorType = data[9];
                    byte compressionMethod = data[10];
                    byte filterMethod = data[11];
                    byte interlaceMethod = data[12];

                    if (width <= 0 || height <= 0)
                        throw new InvalidDataException("Invalid PNG dimensions.");

                    ValidateFormat(bitDepth, colorType);

                    if (compressionMethod != 0 || filterMethod != 0)
                        throw new NotSupportedException("Unsupported PNG compression or filter method.");

                    if (interlaceMethod != 0)
                    {
                        throw new NotSupportedException(
                            "Adam7-interlaced PNG files are not supported. " +
                            "Re-save the file as non-interlaced PNG, or add an Adam7 pass decoder.");
                    }

                    sawHeader = true;
                }
                else if (type == "PLTE")
                {
                    if (!sawHeader)
                        throw new InvalidDataException("PNG PLTE appeared before IHDR.");

                    if (length == 0 || length > 768 || (length % 3) != 0)
                        throw new InvalidDataException("Invalid PNG PLTE chunk.");

                    palette = data;
                }
                else if (type == "tRNS")
                {
                    if (!sawHeader)
                        throw new InvalidDataException("PNG tRNS appeared before IHDR.");

                    transparency = data;
                }
                else if (type == "IDAT")
                {
                    if (!sawHeader)
                        throw new InvalidDataException("PNG IDAT appeared before IHDR.");

                    idatChunks.Add(data);
                    idatSize = checked(idatSize + data.Length);
                    sawImageData = true;
                }
                else if (type == "IEND")
                {
                    if (length != 0)
                        throw new InvalidDataException("Invalid PNG IEND chunk.");

                    sawEnd = true;
                }
                else if (IsCriticalChunk(typeBytes))
                {
                    throw new NotSupportedException("Unsupported critical PNG chunk: " + type);
                }
            }

            if (!sawHeader || !sawImageData)
                throw new InvalidDataException("PNG is missing IHDR or IDAT data.");

            if (colorType == 3 && palette == null)
                throw new InvalidDataException("Indexed-color PNG is missing its PLTE palette.");

            ValidateTransparency(colorType, palette, transparency);

            int channels = GetChannelCount(colorType);
            int bitsPerPixel = checked(channels * bitDepth);
            int rowBytes = checked((width * bitsPerPixel + 7) / 8);
            int filterBytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
            int filteredSize = checked(height * (rowBytes + 1));

            byte[] compressed = CombineChunks(idatChunks, idatSize);
            byte[] filtered = ZipZlib.Inflate(compressed, filteredSize);
            byte[] packed = new byte[checked(rowBytes * height)];
            Unfilter(filtered, packed, rowBytes, height, filterBytesPerPixel);

            ushort[] red16;
            byte[] rgba = ConvertToRgba(
                packed,
                width,
                height,
                rowBytes,
                bitDepth,
                colorType,
                palette,
                transparency,
                out red16);

            return new RawPngBitmap(width, height, rgba, bitDepth, colorType, red16);
        }

        static byte[] CombineChunks(List<byte[]> chunks, int totalSize)
        {
            byte[] combined = new byte[totalSize];
            int offset = 0;

            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] chunk = chunks[i];
                Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                offset += chunk.Length;
            }

            return combined;
        }

        static void ValidateFormat(int bitDepth, int colorType)
        {
            bool valid;
            switch (colorType)
            {
                case 0:
                    valid = bitDepth == 1 || bitDepth == 2 || bitDepth == 4 ||
                            bitDepth == 8 || bitDepth == 16;
                    break;
                case 2:
                    valid = bitDepth == 8 || bitDepth == 16;
                    break;
                case 3:
                    valid = bitDepth == 1 || bitDepth == 2 ||
                            bitDepth == 4 || bitDepth == 8;
                    break;
                case 4:
                case 6:
                    valid = bitDepth == 8 || bitDepth == 16;
                    break;
                default:
                    valid = false;
                    break;
            }

            if (!valid)
            {
                throw new NotSupportedException(
                    "Unsupported PNG format: bit depth " + bitDepth +
                    ", color type " + colorType + ".");
            }
        }

        static void ValidateTransparency(int colorType, byte[] palette, byte[] transparency)
        {
            if (transparency == null)
                return;

            if (colorType == 0 && transparency.Length != 2)
                throw new InvalidDataException("Grayscale PNG tRNS must contain exactly 2 bytes.");

            if (colorType == 2 && transparency.Length != 6)
                throw new InvalidDataException("Truecolor PNG tRNS must contain exactly 6 bytes.");

            if (colorType == 3)
            {
                int paletteEntries = palette.Length / 3;
                if (transparency.Length > paletteEntries)
                    throw new InvalidDataException("PNG tRNS has more entries than PLTE.");
            }

            if (colorType == 4 || colorType == 6)
                throw new InvalidDataException("PNG tRNS is not valid for images that already contain alpha.");
        }

        static int GetChannelCount(int colorType)
        {
            switch (colorType)
            {
                case 0: return 1;
                case 2: return 3;
                case 3: return 1;
                case 4: return 2;
                case 6: return 4;
                default: throw new InvalidDataException("Invalid PNG color type.");
            }
        }

        static byte[] ConvertToRgba(
            byte[] packed,
            int width,
            int height,
            int rowBytes,
            int bitDepth,
            int colorType,
            byte[] palette,
            byte[] transparency,
            out ushort[] red16)
        {
            byte[] rgba = new byte[checked(width * height * 4)];
            red16 = bitDepth == 16 ? new ushort[checked(width * height)] : null;

            ushort transparentGray = 0;
            ushort transparentRed = 0;
            ushort transparentGreen = 0;
            ushort transparentBlue = 0;

            if (transparency != null && colorType == 0)
                transparentGray = ReadUInt16BigEndian(transparency, 0);
            else if (transparency != null && colorType == 2)
            {
                transparentRed = ReadUInt16BigEndian(transparency, 0);
                transparentGreen = ReadUInt16BigEndian(transparency, 2);
                transparentBlue = ReadUInt16BigEndian(transparency, 4);
            }

            for (int y = 0; y < height; y++)
            {
                int row = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    int destination = pixelIndex * 4;
                    byte r;
                    byte g;
                    byte b;
                    byte a;
                    ushort exactRed = 0;

                    if (colorType == 0)
                    {
                        int gray = ReadSample(packed, row, x, 1, 0, bitDepth);
                        byte gray8 = ScaleSampleToByte(gray, bitDepth);
                        r = g = b = gray8;
                        a = transparency != null && gray == transparentGray ? (byte)0 : (byte)255;
                        exactRed = bitDepth == 16 ? (ushort)gray : (ushort)(gray8 * 257);
                    }
                    else if (colorType == 2)
                    {
                        int red = ReadSample(packed, row, x, 3, 0, bitDepth);
                        int green = ReadSample(packed, row, x, 3, 1, bitDepth);
                        int blue = ReadSample(packed, row, x, 3, 2, bitDepth);
                        r = ScaleSampleToByte(red, bitDepth);
                        g = ScaleSampleToByte(green, bitDepth);
                        b = ScaleSampleToByte(blue, bitDepth);
                        a = transparency != null &&
                            red == transparentRed && green == transparentGreen && blue == transparentBlue
                            ? (byte)0 : (byte)255;
                        exactRed = bitDepth == 16 ? (ushort)red : (ushort)(r * 257);
                    }
                    else if (colorType == 3)
                    {
                        int index = ReadPackedSample(packed, row, x, bitDepth);
                        int paletteOffset = index * 3;
                        if (paletteOffset + 2 >= palette.Length)
                            throw new InvalidDataException("PNG pixel references a palette entry that does not exist.");
                        r = palette[paletteOffset];
                        g = palette[paletteOffset + 1];
                        b = palette[paletteOffset + 2];
                        a = transparency != null && index < transparency.Length
                            ? transparency[index]
                            : (byte)255;
                        exactRed = (ushort)(r * 257);
                    }
                    else if (colorType == 4)
                    {
                        int gray = ReadSample(packed, row, x, 2, 0, bitDepth);
                        int alpha = ReadSample(packed, row, x, 2, 1, bitDepth);
                        byte gray8 = ScaleSampleToByte(gray, bitDepth);
                        r = g = b = gray8;
                        a = ScaleSampleToByte(alpha, bitDepth);
                        exactRed = bitDepth == 16 ? (ushort)gray : (ushort)(gray8 * 257);
                    }
                    else
                    {
                        int red = ReadSample(packed, row, x, 4, 0, bitDepth);
                        int green = ReadSample(packed, row, x, 4, 1, bitDepth);
                        int blue = ReadSample(packed, row, x, 4, 2, bitDepth);
                        int alpha = ReadSample(packed, row, x, 4, 3, bitDepth);
                        r = ScaleSampleToByte(red, bitDepth);
                        g = ScaleSampleToByte(green, bitDepth);
                        b = ScaleSampleToByte(blue, bitDepth);
                        a = ScaleSampleToByte(alpha, bitDepth);
                        exactRed = bitDepth == 16 ? (ushort)red : (ushort)(r * 257);
                    }

                    rgba[destination] = r;
                    rgba[destination + 1] = g;
                    rgba[destination + 2] = b;
                    rgba[destination + 3] = a;
                    if (red16 != null)
                        red16[pixelIndex] = exactRed;
                }
            }

            return rgba;
        }

        static int ReadSample(
            byte[] data,
            int row,
            int x,
            int channels,
            int channel,
            int bitDepth)
        {
            if (bitDepth < 8)
            {
                if (channels != 1 || channel != 0)
                    throw new InvalidDataException("Packed PNG samples are only valid for one-channel images.");

                return ReadPackedSample(data, row, x, bitDepth);
            }

            int bytesPerSample = bitDepth == 16 ? 2 : 1;
            int offset = checked(row + (x * channels + channel) * bytesPerSample);
            if (bitDepth == 8)
                return data[offset];

            return (data[offset] << 8) | data[offset + 1];
        }

        static int ReadPackedSample(byte[] data, int row, int x, int bitDepth)
        {
            int bitOffset = checked(x * bitDepth);
            int byteOffset = row + (bitOffset >> 3);
            int shift = 8 - bitDepth - (bitOffset & 7);
            int mask = (1 << bitDepth) - 1;
            return (data[byteOffset] >> shift) & mask;
        }

        static byte ScaleSampleToByte(int sample, int bitDepth)
        {
            if (bitDepth == 8)
                return (byte)sample;

            if (bitDepth == 16)
                return (byte)(sample >> 8);

            int maximum = (1 << bitDepth) - 1;
            return (byte)((sample * 255 + maximum / 2) / maximum);
        }

        static void Unfilter(
            byte[] filtered,
            byte[] output,
            int rowBytes,
            int height,
            int bytesPerPixel)
        {
            int source = 0;
            for (int y = 0; y < height; y++)
            {
                int filter = filtered[source++];
                if ((uint)filter > 4u)
                    throw new InvalidDataException("Invalid PNG row filter: " + filter);

                int row = y * rowBytes;
                int previousRow = row - rowBytes;
                for (int x = 0; x < rowBytes; x++)
                {
                    int encoded = filtered[source++];
                    int left = x >= bytesPerPixel ? output[row + x - bytesPerPixel] : 0;
                    int up = y > 0 ? output[previousRow + x] : 0;
                    int upLeft = y > 0 && x >= bytesPerPixel
                        ? output[previousRow + x - bytesPerPixel]
                        : 0;

                    int predictor;
                    switch (filter)
                    {
                        case 0:
                            predictor = 0;
                            break;
                        case 1:
                            predictor = left;
                            break;
                        case 2:
                            predictor = up;
                            break;
                        case 3:
                            predictor = (left + up) >> 1;
                            break;
                        case 4:
                            predictor = Paeth(left, up, upLeft);
                            break;
                        default:
                            throw new InvalidDataException("Unreachable PNG filter case.");
                    }

                    output[row + x] = unchecked((byte)(encoded + predictor));
                }
            }
        }

        static int Paeth(int left, int up, int upLeft)
        {
            int prediction = left + up - upLeft;
            int distanceLeft = Math.Abs(prediction - left);
            int distanceUp = Math.Abs(prediction - up);
            int distanceUpLeft = Math.Abs(prediction - upLeft);
            if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
                return left;

            return distanceUp <= distanceUpLeft ? up : upLeft;
        }

        static bool IsCriticalChunk(byte[] type)
        {
            return (type[0] & 0x20) == 0;
        }

        static uint ReadUInt32BigEndian(Stream stream)
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            int b3 = stream.ReadByte();
            if ((b0 | b1 | b2 | b3) < 0)
                throw new EndOfStreamException();

            return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | (uint)b3;
        }

        static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   data[offset + 3];
        }

        static ushort ReadUInt16BigEndian(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException();

                offset += read;
                count -= read;
            }
        }
    }
}
