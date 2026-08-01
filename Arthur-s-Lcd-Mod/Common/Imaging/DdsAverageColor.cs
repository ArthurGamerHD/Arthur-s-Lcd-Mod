// ReSharper disable RedundantUsingDirective
using System;
using System.IO;
// ReSharper disable NotAccessedOutParameterVariable

namespace LcdMod.Common.Imaging
{
    public static class DdsAverageColor
    {
        const uint DDS_MAGIC = 0x20534444u;
        const uint DDS_HEADER_SIZE = 124u;
        const uint DDPF_ALPHAPIXELS = 0x00000001u;
        const uint DDPF_FOURCC = 0x00000004u;
        const uint DDPF_RGB = 0x00000040u;
        const uint FOURCC_DXT1 = 0x31545844u;
        const uint FOURCC_DX10 = 0x30315844u;
        const uint DXGI_FORMAT_BC1_UNORM = 71u;
        const uint DXGI_FORMAT_BC1_UNORM_SRGB = 72u;
        const uint DXGI_FORMAT_BC7_UNORM = 98u;
        const uint DXGI_FORMAT_BC7_UNORM_SRGB = 99u;
        const int DDS_HEADER_BYTES = 128;
        const int DDS_DX10_HEADER_BYTES = 20;

        static readonly int[] Bc7Weights2 =
        {
            0, 21, 43, 64
        };

        static readonly int[] Bc7Weights4 =
        {
            0, 4, 9, 13, 17, 21, 26, 30,
            34, 38, 43, 47, 51, 55, 60, 64
        };

        enum DdsPixelEncoding
        {
            // ReSharper disable once UnusedMember.Local
            // well, is unknown, so its usage is unknown ;P
            Unknown,
            Dxt1,
            Bc7,
            Rgb
        }

        struct DdsInfo
        {
            public int Width;
            public int Height;
            public int MipCount;
            public int DataOffset;
            public DdsPixelEncoding Encoding;
            public uint RgbBitCount;
            public uint RMask;
            public uint GMask;
            public uint BMask;
            public uint AMask;
            public bool HasAlpha;
        }

        public static bool TryAverageFirstMip(byte[] data, out byte r, out byte g, out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            DdsInfo info;
            if (!TryReadInfo(data, out info))
                return false;

            return TryAverageMip(
                data,
                info,
                info.DataOffset,
                info.Width,
                info.Height,
                out r,
                out g,
                out b);
        }

        /// <summary>
        /// Reads the smallest stored DDS mip and returns its average visible RGB.
        /// Color-metal voxel textures normally provide a complete chain down to
        /// 1x1, making this a cheap representative far-distance color.
        /// </summary>
        public static bool TryAverageSmallestMip(byte[] data, out byte r, out byte g, out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            DdsInfo info;
            if (!TryReadInfo(data, out info))
                return false;

            int offset = info.DataOffset;
            int width = info.Width;
            int height = info.Height;

            for (int mip = 0; mip < info.MipCount - 1; mip++)
            {
                int mipBytes;
                if (!TryGetMipByteCount(info, width, height, out mipBytes) ||
                    offset > data.Length - mipBytes)
                {
                    return false;
                }

                offset += mipBytes;
                width = width > 1 ? width / 2 : 1;
                height = height > 1 ? height / 2 : 1;
            }

            return TryAverageMip(data, info, offset, width, height, out r, out g, out b);
        }

        public static bool TryAverageFirstMip(Stream input, out byte r, out byte g, out byte b)
        {
            string failureReason;
            return TryAverageFirstMip(input, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageFirstMip(
            Stream input,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            return TryAverageStreamMip(input, false, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageSmallestMip(Stream input, out byte r, out byte g, out byte b)
        {
            string failureReason;
            return TryAverageSmallestMip(input, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageSmallestMip(
            Stream input,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            return TryAverageStreamMip(input, true, out r, out g, out b, out failureReason);
        }

        static bool TryAverageStreamMip(
            Stream input,
            bool useSmallestMip,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            r = 0;
            g = 0;
            b = 0;
            failureReason = null;

            if (input == null)
            {
                failureReason = "Texture stream is null.";
                return false;
            }

            if (!input.CanRead)
            {
                failureReason = "Texture stream is not readable.";
                return false;
            }

            bool restorePosition = false;
            long start = 0;

            try
            {
                if (input.CanSeek)
                {
                    start = input.Position;
                    restorePosition = true;
                }

                // Read only the fixed DDS header. DX10 textures append another
                // 20-byte header, which can be detected from the base header.
                byte[] header = new byte[DDS_HEADER_BYTES + DDS_DX10_HEADER_BYTES];
                if (!TryReadExactly(input, header, 0, DDS_HEADER_BYTES))
                {
                    failureReason = "DDS header is truncated before 128 bytes.";
                    return false;
                }

                int consumedBytes = DDS_HEADER_BYTES;
                uint pixelFormatFlags = ReadUInt32(header, 80);
                uint fourCc = ReadUInt32(header, 84);
                if ((pixelFormatFlags & DDPF_FOURCC) != 0 && fourCc == FOURCC_DX10)
                {
                    if (!TryReadExactly(
                            input,
                            header,
                            DDS_HEADER_BYTES,
                            DDS_DX10_HEADER_BYTES))
                    {
                        failureReason = "DDS DX10 header is truncated before 20 bytes.";
                        return false;
                    }

                    consumedBytes += DDS_DX10_HEADER_BYTES;
                }

                DdsInfo info;
                if (!TryReadInfo(header, out info))
                {
                    failureReason = DescribeHeaderFailure(header, consumedBytes);
                    return false;
                }

                int offset = info.DataOffset;
                int width = info.Width;
                int height = info.Height;
                int targetMip = useSmallestMip ? info.MipCount - 1 : 0;

                for (int mip = 0; mip < targetMip; mip++)
                {
                    int mipBytes;
                    if (!TryGetMipByteCount(info, width, height, out mipBytes))
                    {
                        failureReason = "Could not calculate byte count for DDS mip " + mip +
                                        " (" + width + "x" + height + ", " + info.Encoding + ").";
                        return false;
                    }

                    if (offset > int.MaxValue - mipBytes)
                    {
                        failureReason = "DDS mip offset overflow before target mip " + targetMip + ".";
                        return false;
                    }

                    offset += mipBytes;
                    width = width > 1 ? width / 2 : 1;
                    height = height > 1 ? height / 2 : 1;
                }

                int targetBytes;
                if (!TryGetMipByteCount(info, width, height, out targetBytes))
                {
                    failureReason = "Could not calculate target DDS mip byte count (" +
                                    width + "x" + height + ", " + info.Encoding + ").";
                    return false;
                }

                if (offset < consumedBytes)
                {
                    failureReason = "DDS target mip offset precedes the consumed header: offset " +
                                    offset + ", header " + consumedBytes + ".";
                    return false;
                }

                // Workshop archive streams may be forward-only. Discard the
                // earlier mip bytes instead of copying the entire DDS or seeking.
                int bytesToSkip = offset - consumedBytes;
                if (!TrySkipForward(input, bytesToSkip))
                {
                    failureReason = "Texture stream ended while skipping " + bytesToSkip +
                                    " bytes to DDS mip " + targetMip + ".";
                    return false;
                }

                byte[] mipData = new byte[targetBytes];
                if (!TryReadExactly(input, mipData, 0, mipData.Length))
                {
                    failureReason = "Texture stream ended while reading DDS mip " + targetMip +
                                    ": expected " + targetBytes + " bytes.";
                    return false;
                }

                if (!TryAverageMip(mipData, info, 0, width, height, out r, out g, out b))
                {
                    failureReason = DescribeMipDecodeFailure(info, mipData, width, height, targetMip);
                    return false;
                }

                return true;
            }
            catch (NotSupportedException error)
            {
                failureReason = "Stream operation is not supported while reading DDS: " + error.Message;
                return false;
            }
            catch (Exception error)
            {
                failureReason = error.GetType().Name.Contains("IOException") ? "I/O error while reading DDS: " : (error.GetType().Name + " while reading DDS: ") + error.Message;
                return false;
            }
            finally
            {
                if (restorePosition)
                {
                    try
                    {
                        input.Position = start;
                    }
                    catch
                    {
                        // A stream that advertised seeking but stopped supporting
                        // it should not turn a failed color probe into a game error.
                    }
                }
            }
        }

        static bool TrySkipForward(Stream input, int count)
        {
            if (count < 0)
                return false;
            if (count == 0)
                return true;

            byte[] scratch = new byte[4096];
            while (count > 0)
            {
                int request = Math.Min(count, scratch.Length);
                int read = input.Read(scratch, 0, request);
                if (read <= 0)
                    return false;

                count -= read;
            }

            return true;
        }

        static bool TryReadExactly(Stream input, byte[] output, int offset, int count)
        {
            while (count > 0)
            {
                int read = input.Read(output, offset, count);
                if (read <= 0)
                    return false;

                offset += read;
                count -= read;
            }

            return true;
        }

        static bool TryReadInfo(byte[] data, out DdsInfo info)
        {
            info = default(DdsInfo);
            if (data == null || data.Length < DDS_HEADER_BYTES)
                return false;

            if (ReadUInt32(data, 0) != DDS_MAGIC ||
                ReadUInt32(data, 4) != DDS_HEADER_SIZE)
            {
                return false;
            }

            int height = (int)ReadUInt32(data, 12);
            int width = (int)ReadUInt32(data, 16);
            int mipCount = (int)ReadUInt32(data, 28);
            if (width <= 0 || height <= 0)
                return false;
            if (mipCount <= 0)
                mipCount = 1;

            uint pixelFormatFlags = ReadUInt32(data, 80);
            uint fourCc = ReadUInt32(data, 84);

            info.Width = width;
            info.Height = height;
            info.MipCount = mipCount;
            info.DataOffset = DDS_HEADER_BYTES;

            if ((pixelFormatFlags & DDPF_FOURCC) != 0)
            {
                if (fourCc == FOURCC_DXT1)
                {
                    info.Encoding = DdsPixelEncoding.Dxt1;
                    return true;
                }

                if (fourCc != FOURCC_DX10 ||
                    data.Length < DDS_HEADER_BYTES + DDS_DX10_HEADER_BYTES)
                {
                    return false;
                }

                uint dxgiFormat = ReadUInt32(data, DDS_HEADER_BYTES);
                info.DataOffset += DDS_DX10_HEADER_BYTES;

                if (dxgiFormat == DXGI_FORMAT_BC1_UNORM ||
                    dxgiFormat == DXGI_FORMAT_BC1_UNORM_SRGB)
                {
                    info.Encoding = DdsPixelEncoding.Dxt1;
                    return true;
                }

                if (dxgiFormat == DXGI_FORMAT_BC7_UNORM ||
                    dxgiFormat == DXGI_FORMAT_BC7_UNORM_SRGB)
                {
                    info.Encoding = DdsPixelEncoding.Bc7;
                    return true;
                }

                return false;
            }

            if ((pixelFormatFlags & DDPF_RGB) == 0)
                return false;

            info.Encoding = DdsPixelEncoding.Rgb;
            info.RgbBitCount = ReadUInt32(data, 88);
            info.RMask = ReadUInt32(data, 92);
            info.GMask = ReadUInt32(data, 96);
            info.BMask = ReadUInt32(data, 100);
            info.AMask = ReadUInt32(data, 104);
            info.HasAlpha = (pixelFormatFlags & DDPF_ALPHAPIXELS) != 0 && info.AMask != 0;
            return info.RgbBitCount == 24u || info.RgbBitCount == 32u;
        }

        static string DescribeHeaderFailure(byte[] data, int availableBytes)
        {
            if (data == null || availableBytes < DDS_HEADER_BYTES)
                return "DDS header is missing or truncated.";

            uint magic = ReadUInt32(data, 0);
            if (magic != DDS_MAGIC)
                return "Invalid DDS magic 0x" + magic.ToString("X8") + ".";

            uint headerSize = ReadUInt32(data, 4);
            if (headerSize != DDS_HEADER_SIZE)
                return "Invalid DDS header size " + headerSize + "; expected 124.";

            int height = (int)ReadUInt32(data, 12);
            int width = (int)ReadUInt32(data, 16);
            if (width <= 0 || height <= 0)
                return "Invalid DDS dimensions " + width + "x" + height + ".";

            uint pixelFormatFlags = ReadUInt32(data, 80);
            uint fourCc = ReadUInt32(data, 84);
            if ((pixelFormatFlags & DDPF_FOURCC) != 0)
            {
                if (fourCc == FOURCC_DX10)
                {
                    if (availableBytes < DDS_HEADER_BYTES + DDS_DX10_HEADER_BYTES)
                        return "DDS declares a DX10 header, but it is truncated.";

                    uint dxgiFormat = ReadUInt32(data, DDS_HEADER_BYTES);
                    return "Unsupported DDS DXGI format " + dxgiFormat +
                           "; supported formats are BC1 and BC7.";
                }

                return "Unsupported DDS FourCC " + FormatFourCc(fourCc) +
                       "; supported legacy format is DXT1.";
            }

            if ((pixelFormatFlags & DDPF_RGB) == 0)
                return "DDS pixel format is neither supported FourCC nor RGB.";

            uint bitCount = ReadUInt32(data, 88);
            return "Unsupported uncompressed DDS RGB bit count " + bitCount +
                   "; supported values are 24 and 32.";
        }

        static string DescribeMipDecodeFailure(
            DdsInfo info,
            byte[] mipData,
            int width,
            int height,
            int mipIndex)
        {
            if (info.Encoding == DdsPixelEncoding.Bc7)
            {
                int blockCount = ((width + 3) / 4) * ((height + 3) / 4);
                for (int block = 0; block < blockCount; block++)
                {
                    int offset = block * 16;
                    if (mipData == null || offset > mipData.Length - 16)
                    {
                        return "BC7 mip " + mipIndex + " is truncated at block " + block + ".";
                    }

                    int mode = GetBc7Mode(mipData, offset);
                    if (mode < 0)
                        return "BC7 mip " + mipIndex + " has an invalid mode marker at block " + block + ".";
                    if (mode != 5 && mode != 6)
                    {
                        return "BC7 mip " + mipIndex + " uses unsupported mode " + mode +
                               " at block " + block + "; supported modes are 5 and 6.";
                    }
                }

                return "BC7 mip " + mipIndex + " could not be decoded despite using supported modes.";
            }

            if (info.Encoding == DdsPixelEncoding.Dxt1)
                return "DXT1 mip " + mipIndex + " contains no visible decodable pixels or is malformed.";
            if (info.Encoding == DdsPixelEncoding.Rgb)
                return "RGB mip " + mipIndex + " contains no visible pixels or is malformed.";

            return "DDS mip " + mipIndex + " uses an unsupported pixel encoding.";
        }

        static int GetBc7Mode(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset >= data.Length)
                return -1;

            byte marker = data[offset];
            for (int mode = 0; mode < 8; mode++)
            {
                if ((marker & (1 << mode)) != 0)
                    return mode;
            }

            return -1;
        }

        static string FormatFourCc(uint value)
        {
            char a = (char)(value & 255u);
            char b = (char)((value >> 8) & 255u);
            char c = (char)((value >> 16) & 255u);
            char d = (char)((value >> 24) & 255u);
            return "'" + a + b + c + d + "' (0x" + value.ToString("X8") + ")";
        }

        static bool TryGetMipByteCount(DdsInfo info, int width, int height, out int byteCount)
        {
            byteCount = 0;
            long result;

            if (info.Encoding == DdsPixelEncoding.Dxt1)
            {
                result = (long)((width + 3) / 4) * ((height + 3) / 4) * 8L;
            }
            else if (info.Encoding == DdsPixelEncoding.Bc7)
            {
                result = (long)((width + 3) / 4) * ((height + 3) / 4) * 16L;
            }
            else if (info.Encoding == DdsPixelEncoding.Rgb)
            {
                result = (long)((width * (int)info.RgbBitCount + 7) / 8) * height;
            }
            else
            {
                return false;
            }

            if (result <= 0 || result > int.MaxValue)
                return false;

            byteCount = (int)result;
            return true;
        }

        static bool TryAverageMip(
            byte[] data,
            DdsInfo info,
            int offset,
            int width,
            int height,
            out byte r,
            out byte g,
            out byte b)
        {
            if (info.Encoding == DdsPixelEncoding.Dxt1)
                return TryAverageDxt1(data, offset, width, height, out r, out g, out b);

            if (info.Encoding == DdsPixelEncoding.Bc7)
                return TryAverageBc7(data, offset, width, height, out r, out g, out b);

            if (info.Encoding == DdsPixelEncoding.Rgb)
            {
                return TryAverageRgb(
                    data,
                    offset,
                    width,
                    height,
                    info.RgbBitCount,
                    info.RMask,
                    info.GMask,
                    info.BMask,
                    info.AMask,
                    info.HasAlpha,
                    out r,
                    out g,
                    out b);
            }

            r = 0;
            g = 0;
            b = 0;
            return false;
        }

        static bool TryAverageDxt1(
            byte[] data,
            int offset,
            int width,
            int height,
            out byte r,
            out byte g,
            out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            long requiredBytes = offset + (long)blockCountX * blockCountY * 8L;
            if (offset < 0 || requiredBytes > data.Length)
                return false;

            long rSum = 0;
            long gSum = 0;
            long bSum = 0;
            long count = 0;

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    ushort color0 = ReadUInt16(data, offset);
                    ushort color1 = ReadUInt16(data, offset + 2);
                    uint indices = ReadUInt32(data, offset + 4);
                    offset += 8;

                    byte[] palette = new byte[12];
                    DecodeRgb565(color0, palette, 0);
                    DecodeRgb565(color1, palette, 3);

                    if (color0 > color1)
                    {
                        palette[6] = (byte)((2 * palette[0] + palette[3]) / 3);
                        palette[7] = (byte)((2 * palette[1] + palette[4]) / 3);
                        palette[8] = (byte)((2 * palette[2] + palette[5]) / 3);
                        palette[9] = (byte)((palette[0] + 2 * palette[3]) / 3);
                        palette[10] = (byte)((palette[1] + 2 * palette[4]) / 3);
                        palette[11] = (byte)((palette[2] + 2 * palette[5]) / 3);
                    }
                    else
                    {
                        palette[6] = (byte)((palette[0] + palette[3]) / 2);
                        palette[7] = (byte)((palette[1] + palette[4]) / 2);
                        palette[8] = (byte)((palette[2] + palette[5]) / 2);
                    }

                    for (int y = 0; y < 4; y++)
                    {
                        int pixelY = blockY * 4 + y;
                        if (pixelY >= height)
                            continue;

                        for (int x = 0; x < 4; x++)
                        {
                            int pixelX = blockX * 4 + x;
                            if (pixelX >= width)
                                continue;

                            int selectorIndex = y * 4 + x;
                            int paletteIndex = (int)((indices >> (selectorIndex * 2)) & 3u);
                            if (color0 <= color1 && paletteIndex == 3)
                                continue;

                            int paletteOffset = paletteIndex * 3;
                            rSum += palette[paletteOffset];
                            gSum += palette[paletteOffset + 1];
                            bSum += palette[paletteOffset + 2];
                            count++;
                        }
                    }
                }
            }

            return TryFinishAverage(rSum, gSum, bSum, count, out r, out g, out b);
        }

        static bool TryAverageBc7(
            byte[] data,
            int offset,
            int width,
            int height,
            out byte r,
            out byte g,
            out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            long requiredBytes = offset + (long)blockCountX * blockCountY * 16L;
            if (offset < 0 || requiredBytes > data.Length)
                return false;

            long rSum = 0;
            long gSum = 0;
            long bSum = 0;
            long count = 0;

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    LsbBitReader bits = new LsbBitReader(data, offset, 16);
                    offset += 16;

                    int mode;
                    if (!TryReadBc7Mode(ref bits, out mode))
                        return false;

                    if (mode == 5)
                    {
                        if (!TryAccumulateBc7Mode5(
                                ref bits,
                                blockX,
                                blockY,
                                width,
                                height,
                                ref rSum,
                                ref gSum,
                                ref bSum,
                                ref count))
                        {
                            return false;
                        }
                    }
                    else if (mode == 6)
                    {
                        if (!TryAccumulateBc7Mode6(
                                ref bits,
                                blockX,
                                blockY,
                                width,
                                height,
                                ref rSum,
                                ref gSum,
                                ref bSum,
                                ref count))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // Only modes currently observed in Space Engineers
                        // voxel color-metal terminal mips are supported.
                        return false;
                    }
                }
            }

            return TryFinishAverage(rSum, gSum, bSum, count, out r, out g, out b);
        }

        static bool TryReadBc7Mode(ref LsbBitReader bits, out int mode)
        {
            mode = 0;
            int marker;
            while (mode < 8)
            {
                if (!bits.TryReadBits(1, out marker))
                    return false;
                if (marker != 0)
                    return true;
                mode++;
            }

            return false;
        }

        static bool TryAccumulateBc7Mode5(
            ref LsbBitReader bits,
            int blockX,
            int blockY,
            int width,
            int height,
            ref long rSum,
            ref long gSum,
            ref long bSum,
            ref long count)
        {
            int rotation;
            int r0;
            int r1;
            int g0;
            int g1;
            int b0;
            int b1;
            int a0;
            int a1;

            if (!bits.TryReadBits(2, out rotation) ||
                !bits.TryReadBits(7, out r0) || !bits.TryReadBits(7, out r1) ||
                !bits.TryReadBits(7, out g0) || !bits.TryReadBits(7, out g1) ||
                !bits.TryReadBits(7, out b0) || !bits.TryReadBits(7, out b1) ||
                !bits.TryReadBits(8, out a0) || !bits.TryReadBits(8, out a1))
            {
                return false;
            }

            r0 = ExpandBc7SevenBit(r0);
            r1 = ExpandBc7SevenBit(r1);
            g0 = ExpandBc7SevenBit(g0);
            g1 = ExpandBc7SevenBit(g1);
            b0 = ExpandBc7SevenBit(b0);
            b1 = ExpandBc7SevenBit(b1);

            int[] colorIndices = new int[16];
            int[] alphaIndices = new int[16];
            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 1 : 2, out colorIndices[pixel]))
                    return false;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 1 : 2, out alphaIndices[pixel]))
                    return false;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int colorWeight = Bc7Weights2[colorIndices[pixel]];
                int alphaWeight = Bc7Weights2[alphaIndices[pixel]];
                int red = InterpolateBc7(r0, r1, colorWeight);
                int green = InterpolateBc7(g0, g1, colorWeight);
                int blue = InterpolateBc7(b0, b1, colorWeight);
                int alpha = InterpolateBc7(a0, a1, alphaWeight);

                // Modes 4 and 5 encode one scalar channel separately. Rotation
                // swaps that scalar back into its original RGBA component.
                if (rotation == 1)
                    red = alpha;
                else if (rotation == 2)
                    green = alpha;
                else if (rotation == 3)
                    blue = alpha;

                rSum += red;
                gSum += green;
                bSum += blue;
                count++;
            }

            return true;
        }

        static bool TryAccumulateBc7Mode6(
            ref LsbBitReader bits,
            int blockX,
            int blockY,
            int width,
            int height,
            ref long rSum,
            ref long gSum,
            ref long bSum,
            ref long count)
        {
            int r0;
            int r1;
            int g0;
            int g1;
            int b0;
            int b1;
            int a0;
            int a1;
            int p0;
            int p1;

            if (!bits.TryReadBits(7, out r0) || !bits.TryReadBits(7, out r1) ||
                !bits.TryReadBits(7, out g0) || !bits.TryReadBits(7, out g1) ||
                !bits.TryReadBits(7, out b0) || !bits.TryReadBits(7, out b1) ||
                !bits.TryReadBits(7, out a0) || !bits.TryReadBits(7, out a1) ||
                !bits.TryReadBits(1, out p0) || !bits.TryReadBits(1, out p1))
            {
                return false;
            }

            r0 = (r0 << 1) | p0;
            g0 = (g0 << 1) | p0;
            b0 = (b0 << 1) | p0;
            r1 = (r1 << 1) | p1;
            g1 = (g1 << 1) | p1;
            b1 = (b1 << 1) | p1;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int index;
                if (!bits.TryReadBits(pixel == 0 ? 3 : 4, out index))
                    return false;

                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int weight = Bc7Weights4[index];
                rSum += InterpolateBc7(r0, r1, weight);
                gSum += InterpolateBc7(g0, g1, weight);
                bSum += InterpolateBc7(b0, b1, weight);
                count++;
            }

            return true;
        }

        static int ExpandBc7SevenBit(int value)
        {
            return (value << 1) | (value >> 6);
        }

        static int InterpolateBc7(int endpoint0, int endpoint1, int weight)
        {
            return ((64 - weight) * endpoint0 + weight * endpoint1 + 32) >> 6;
        }

        static bool TryAverageRgb(
            byte[] data,
            int offset,
            int width,
            int height,
            uint rgbBitCount,
            uint rMask,
            uint gMask,
            uint bMask,
            uint aMask,
            bool hasAlpha,
            out byte r,
            out byte g,
            out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            if (rgbBitCount != 24u && rgbBitCount != 32u)
                return false;

            int bytesPerPixel = (int)rgbBitCount / 8;
            int rowPitch = (width * (int)rgbBitCount + 7) / 8;
            long requiredBytes = offset + (long)rowPitch * height;
            if (offset < 0 || requiredBytes > data.Length)
                return false;

            long rSum = 0;
            long gSum = 0;
            long bSum = 0;
            long count = 0;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = offset + y * rowPitch;
                for (int x = 0; x < width; x++)
                {
                    uint pixel = 0;
                    int pixelOffset = rowOffset + x * bytesPerPixel;
                    for (int i = 0; i < bytesPerPixel; i++)
                        pixel |= (uint)data[pixelOffset + i] << (i * 8);

                    if (hasAlpha && ExtractMaskedByte(pixel, aMask) == 0)
                        continue;

                    rSum += ExtractMaskedByte(pixel, rMask);
                    gSum += ExtractMaskedByte(pixel, gMask);
                    bSum += ExtractMaskedByte(pixel, bMask);
                    count++;
                }
            }

            return TryFinishAverage(rSum, gSum, bSum, count, out r, out g, out b);
        }

        static bool TryFinishAverage(
            long rSum,
            long gSum,
            long bSum,
            long count,
            out byte r,
            out byte g,
            out byte b)
        {
            if (count <= 0)
            {
                r = 0;
                g = 0;
                b = 0;
                return false;
            }

            r = (byte)((rSum + count / 2) / count);
            g = (byte)((gSum + count / 2) / count);
            b = (byte)((bSum + count / 2) / count);
            return true;
        }

        static void DecodeRgb565(ushort value, byte[] output, int offset)
        {
            output[offset] = (byte)(((value >> 11) & 31) * 255 / 31);
            output[offset + 1] = (byte)(((value >> 5) & 63) * 255 / 63);
            output[offset + 2] = (byte)((value & 31) * 255 / 31);
        }

        static byte ExtractMaskedByte(uint value, uint mask)
        {
            if (mask == 0)
                return 0;

            int shift = 0;
            while (shift < 32 && ((mask >> shift) & 1u) == 0u)
                shift++;

            int bitCount = 0;
            while (shift + bitCount < 32 && ((mask >> (shift + bitCount)) & 1u) != 0u)
                bitCount++;

            uint raw = (value & mask) >> shift;
            if (bitCount >= 8)
                return (byte)(raw >> (bitCount - 8));

            uint maximum = (1u << bitCount) - 1u;
            if (maximum == 0)
                throw new DivideByZeroException();
            return (byte)((raw * 255u + maximum / 2u) / maximum);
        }

        static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) |
                          (data[offset + 3] << 24));
        }

        struct LsbBitReader
        {
            readonly byte[] _data;
            readonly int _endBit;
            int _bitPosition;

            public LsbBitReader(byte[] data, int offset, int byteCount)
            {
                _data = data;
                _bitPosition = offset * 8;
                _endBit = (offset + byteCount) * 8;
            }

            public bool TryReadBits(int count, out int value)
            {
                value = 0;
                if (count < 0 || _bitPosition > _endBit - count)
                    return false;

                for (int bit = 0; bit < count; bit++)
                {
                    int byteIndex = _bitPosition >> 3;
                    int bitIndex = _bitPosition & 7;
                    value |= ((_data[byteIndex] >> bitIndex) & 1) << bit;
                    _bitPosition++;
                }

                return true;
            }
        }
    }
}
