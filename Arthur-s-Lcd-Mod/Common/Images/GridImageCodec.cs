using System;
using System.Collections.Generic;
using System.Text;
using Adk.Compression.Zip;
using Adk.Image;
using Adk.Image.Dds;
using Adk.Image.Png;
using LcdMod.Common.Helpers;

namespace LcdMod.Common.Images
{
    /// <summary>
    /// Converts user images into the DDS the LCD renderer loads, identifies them by the
    /// hash of that DDS, and stores them in the smallest payload that rebuilds it byte for byte.
    /// </summary>
    public static class GridImageCodec
    {
        public const string SPRITE_PREFIX = "LcdImg_";
        public const int LCD_PROFILE = 1;
        const int ID_LENGTH = 32;
        const int DDS_HEADER_BYTES = 128;
        const double PNG_TRIAL_MARGIN = 1.2d;

        static readonly byte[] SpritePrefixBytes = Encoding.ASCII.GetBytes(SPRITE_PREFIX);

        public static bool TryParseSpriteName(string spriteName, out string id)
        {
            id = null;
            if (spriteName == null || spriteName.Length != SPRITE_PREFIX.Length + ID_LENGTH ||
                !spriteName.StartsWith(SPRITE_PREFIX, StringComparison.Ordinal))
            {
                return false;
            }

            string candidate = spriteName.Substring(SPRITE_PREFIX.Length);
            if (!IsValidId(candidate))
                return false;

            id = candidate;
            return true;
        }

        public static string ToSpriteName(string id)
        {
            return SPRITE_PREFIX + id;
        }

        public static string ToFileName(string id)
        {
            return SPRITE_PREFIX + id + ".dds";
        }

        public static bool IsValidId(string id)
        {
            if (id == null || id.Length != ID_LENGTH)
                return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                    return false;
            }

            return true;
        }

        /// <summary>Finds image sprite names in serialized data such as a protobuf screen config.</summary>
        public static void CollectSpriteIds(byte[] serialized, HashSet<string> ids)
        {
            if (serialized == null)
                return;

            int last = serialized.Length - SpritePrefixBytes.Length - ID_LENGTH;
            var chars = new char[ID_LENGTH];
            for (int i = 0; i <= last; i++)
            {
                bool prefix = true;
                for (int p = 0; p < SpritePrefixBytes.Length && prefix; p++)
                    prefix = serialized[i + p] == SpritePrefixBytes[p];

                if (!prefix)
                    continue;

                for (int c = 0; c < ID_LENGTH; c++)
                    chars[c] = (char)serialized[i + SpritePrefixBytes.Length + c];

                var id = new string(chars);
                if (IsValidId(id))
                    ids.Add(id);
            }
        }

        public static string ComputeId(byte[] dds)
        {
            return ImageSha256.ComputeHex(dds, ID_LENGTH / 2);
        }

        public static DdsEncodeOptions CreateLcdOptions(int profile)
        {
            if (profile != LCD_PROFILE)
                return null;

            return new DdsEncodeOptions
            {
                Format = DdsOutputFormat.Bc7Unorm,
                GenerateMipmaps = true,
                MaximumMipLevels = 0,
                MipFilter = DdsMipFilter.Box,
                Bc7Quality = Bc7Quality.Balanced
            };
        }

        public static bool TryReadDdsSize(byte[] dds, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (dds == null || dds.Length < DDS_HEADER_BYTES ||
                dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ' ||
                ReadUInt32(dds, 4) != 124u)
            {
                return false;
            }

            height = (int)ReadUInt32(dds, 12);
            width = (int)ReadUInt32(dds, 16);
            return width > 0 && height > 0;
        }

        public static bool TryEncode(string fileName, byte[] source, out EncodedGridImage image, out string error)
        {
            image = null;
            error = null;
            if (source == null || source.Length == 0)
            {
                error = "the file is empty";
                return false;
            }

            try
            {
                if (IsPng(source))
                    return TryEncodePng(source, out image, out error);

                if (fileName != null && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    error = "the file is not a valid PNG";
                    return false;
                }

                return TryEncodeDds(source, out image, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static bool TryEncodeDds(byte[] dds, out EncodedGridImage image, out string error)
        {
            image = null;
            int width;
            int height;
            if (!TryReadDdsSize(dds, out width, out height))
            {
                error = "the file is not a valid DDS texture";
                return false;
            }

            if (!CheckDimensions(width, height, out error))
                return false;

            if (width % 4 != 0 || height % 4 != 0)
            {
                error = "DDS width and height must be divisible by 4";
                return false;
            }

            var payload = new GridImagePayload
            {
                Encoding = GridImageEncoding.DeflatedDds,
                Data = Zlib.DeflateRaw(dds, DeflateLevel.Maximum),
                DdsLength = dds.Length
            };

            return TryFinish(dds, width, height, payload, out image, out error);
        }

        public static bool TryEncodePng(byte[] png, out EncodedGridImage image, out string error)
        {
            image = null;
            RawPngBitmap bitmap = RawPngBitmap.Load(png);
            if (!CheckDimensions(bitmap.Width, bitmap.Height, out error))
                return false;

            int width = AlignToBlock(bitmap.Width);
            int height = AlignToBlock(bitmap.Height);
            byte[] pixels = PremultiplyAndPad(bitmap.Pixels, bitmap.Width, bitmap.Height, width, height);

            DdsEncodeOptions options = CreateLcdOptions(LCD_PROFILE);
            byte[] dds = DdsEncoder.Encode(pixels, width, height, width * 4, options);

            var payload = new GridImagePayload
            {
                Encoding = GridImageEncoding.DeflatedDds,
                Data = Zlib.DeflateRaw(dds, DeflateLevel.Maximum),
                DdsLength = dds.Length
            };

            byte[] pngCandidate = TryCreatePngCandidate(pixels, width, height, dds, options, payload.Data.Length);
            if (pngCandidate != null)
            {
                payload = new GridImagePayload
                {
                    Encoding = GridImageEncoding.Png,
                    Data = pngCandidate,
                    DdsLength = dds.Length,
                    Profile = LCD_PROFILE
                };
            }

            return TryFinish(dds, width, height, payload, out image, out error);
        }

        static byte[] TryCreatePngCandidate(
            byte[] pixels, int width, int height, byte[] dds, DdsEncodeOptions options, int deflatedDdsBytes)
        {
            byte[] trial = PngEncoder.Encode(pixels, width, height, DeflateLevel.Fast);
            if (trial.Length > deflatedDdsBytes * PNG_TRIAL_MARGIN)
                return null;

            byte[] png = PngEncoder.Encode(pixels, width, height, DeflateLevel.Maximum);
            if (png.Length >= deflatedDdsBytes)
                return null;

            RawPngBitmap decoded = RawPngBitmap.Load(png);
            byte[] rebuilt = DdsEncoder.Encode(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride, options);
            return BytesEqual(rebuilt, dds) ? png : null;
        }

        static bool TryFinish(
            byte[] dds, int width, int height, GridImagePayload payload, out EncodedGridImage image, out string error)
        {
            image = null;
            if (payload.Data.Length > Constants.MAX_IMAGE_PAYLOAD_BYTES)
            {
                error = "the compressed image is " + payload.Data.Length / 1024 + " KiB, above the " +
                        Constants.MAX_IMAGE_PAYLOAD_BYTES / 1024 + " KiB limit";
                return false;
            }

            error = null;
            image = new EncodedGridImage
            {
                Id = ComputeId(dds),
                Width = width,
                Height = height,
                Payload = payload
            };
            return true;
        }

        /// <summary>Rebuilds the DDS and checks it hashes to <paramref name="id"/>.</summary>
        public static bool TryDecode(string id, GridImagePayload payload, out byte[] dds, out string error)
        {
            dds = null;
            error = null;
            if (payload == null || payload.Data == null || payload.Data.Length == 0)
            {
                error = "missing payload";
                return false;
            }

            if (payload.Data.Length > Constants.MAX_IMAGE_PAYLOAD_BYTES ||
                payload.DdsLength <= DDS_HEADER_BYTES ||
                payload.DdsLength > MaxDdsBytes())
            {
                error = "payload exceeds the size limits";
                return false;
            }

            try
            {
                switch (payload.Encoding)
                {
                    case GridImageEncoding.DeflatedDds:
                        dds = Zlib.InflateRawDeflate(payload.Data, 0, payload.Data.Length, payload.DdsLength);
                        break;

                    case GridImageEncoding.Png:
                        DdsEncodeOptions options = CreateLcdOptions(payload.Profile);
                        if (options == null)
                        {
                            error = "unknown texture profile " + payload.Profile;
                            return false;
                        }

                        RawPngBitmap bitmap = RawPngBitmap.Load(payload.Data);
                        if (!CheckDimensions(bitmap.Width, bitmap.Height, out error))
                            return false;

                        dds = DdsEncoder.Encode(bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, options);
                        break;

                    default:
                        error = "unknown payload encoding " + payload.Encoding;
                        return false;
                }
            }
            catch (Exception e)
            {
                dds = null;
                error = e.Message;
                return false;
            }

            int width;
            int height;
            if (dds.Length != payload.DdsLength || !TryReadDdsSize(dds, out width, out height) ||
                !CheckDimensions(width, height, out error))
            {
                error = error ?? "rebuilt texture is malformed";
                dds = null;
                return false;
            }

            if (!string.Equals(ComputeId(dds), id, StringComparison.Ordinal))
            {
                error = "rebuilt texture does not match its id";
                dds = null;
                return false;
            }

            return true;
        }

        static bool CheckDimensions(int width, int height, out string error)
        {
            if (width <= 0 || height <= 0 ||
                width > Constants.MAX_IMAGE_DIMENSION || height > Constants.MAX_IMAGE_DIMENSION)
            {
                error = "the image is " + width + "x" + height + ", above the " +
                        Constants.MAX_IMAGE_DIMENSION + "x" + Constants.MAX_IMAGE_DIMENSION + " limit";
                return false;
            }

            error = null;
            return true;
        }

        static int MaxDdsBytes()
        {
            return Constants.MAX_IMAGE_DIMENSION * Constants.MAX_IMAGE_DIMENSION * 8 + 4096;
        }

        static int AlignToBlock(int value)
        {
            return (value + 3) & ~3;
        }

        static byte[] PremultiplyAndPad(byte[] rgba, int sourceWidth, int sourceHeight, int width, int height)
        {
            var output = new byte[width * height * 4];
            for (int y = 0; y < sourceHeight; y++)
            {
                for (int x = 0; x < sourceWidth; x++)
                {
                    int source = (y * sourceWidth + x) * 4;
                    int target = (y * width + x) * 4;
                    int alpha = rgba[source + 3];
                    output[target] = (byte)((rgba[source] * alpha + 127) / 255);
                    output[target + 1] = (byte)((rgba[source + 1] * alpha + 127) / 255);
                    output[target + 2] = (byte)((rgba[source + 2] * alpha + 127) / 255);
                    output[target + 3] = (byte)alpha;
                }
            }

            return output;
        }

        static bool IsPng(byte[] data)
        {
            return data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G';
        }

        static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return data[offset] | ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) | ((uint)data[offset + 3] << 24);
        }
    }
}
