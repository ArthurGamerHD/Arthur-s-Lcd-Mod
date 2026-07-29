using System;
using System.IO;
using LcdMod.Common.Png;
using Sandbox.ModAPI;
using VRageMath;
using ArgumentOutOfRangeException = LcdMod.Common.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = LcdMod.Common.Exceptions.InvalidDataException;

namespace LcdMod.Client.Modules.Cartography
{
    internal enum PlanetCubeFace
    {
        Left = 0,
        Right = 1,
        Up = 2,
        Down = 3,
        Back = 4,
        Front = 5
    }

    internal sealed class PlanetMapSource
    {
        public static readonly PlanetCubeFace[] ExportOrder =
        {
            PlanetCubeFace.Back,
            PlanetCubeFace.Down,
            PlanetCubeFace.Front,
            PlanetCubeFace.Left,
            PlanetCubeFace.Right,
            PlanetCubeFace.Up
        };

        readonly ushort[][] _heights = new ushort[6][];
        readonly byte[][] _materialIds = new byte[6][];

        public int Resolution { get; private set; }

        public static string GetFaceName(PlanetCubeFace face)
        {
            switch (face)
            {
                case PlanetCubeFace.Left: return "left";
                case PlanetCubeFace.Right: return "right";
                case PlanetCubeFace.Up: return "up";
                case PlanetCubeFace.Down: return "down";
                case PlanetCubeFace.Back: return "back";
                case PlanetCubeFace.Front: return "front";
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        public static PlanetMapSource Load(
            PlanetDefinitionSnapshot planet,
            CartographyCancellation cancellation)
        {
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));
            if (cancellation == null)
                throw new ArgumentNullException(nameof(cancellation));

            var source = new PlanetMapSource();
            for (int i = 0; i < ExportOrder.Length; i++)
            {
                cancellation.ThrowIfCancelled();
                PlanetCubeFace face = ExportOrder[i];
                string faceName = GetFaceName(face);

                RawPngBitmap height = LoadPng(planet, faceName + ".png");
                source.ValidateResolution(height, faceName + ".png");
                source._heights[(int)face] = ExtractHeight(height);

                cancellation.ThrowIfCancelled();
                RawPngBitmap material = LoadPng(planet, faceName + "_mat.png");
                source.ValidateResolution(material, faceName + "_mat.png");
                source._materialIds[(int)face] = ExtractMaterialRed(material);
            }

            return source;
        }

        public float SampleHeightNormalized(Vector3 direction)
        {
            PlanetCubeFace face;
            float u;
            float v;
            DirectionToFaceUv(direction, out face, out u, out v);

            float x = u * (Resolution - 1);
            float y = v * (Resolution - 1);
            int x0 = Clamp((int)Math.Floor(x), 0, Resolution - 1);
            int y0 = Clamp((int)Math.Floor(y), 0, Resolution - 1);
            int x1 = Math.Min(x0 + 1, Resolution - 1);
            int y1 = Math.Min(y0 + 1, Resolution - 1);
            float tx = x - x0;
            float ty = y - y0;
            ushort[] data = _heights[(int)face];

            float h00 = data[y0 * Resolution + x0] / 65535f;
            float h10 = data[y0 * Resolution + x1] / 65535f;
            float h01 = data[y1 * Resolution + x0] / 65535f;
            float h11 = data[y1 * Resolution + x1] / 65535f;

            float top = h00 + (h10 - h00) * tx;
            float bottom = h01 + (h11 - h01) * tx;
            return top + (bottom - top) * ty;
        }

        public byte SampleMaterialNearest(PlanetCubeFace face, float u, float v)
        {
            int x = Clamp((int)(u * Resolution), 0, Resolution - 1);
            int y = Clamp((int)(v * Resolution), 0, Resolution - 1);
            return _materialIds[(int)face][y * Resolution + x];
        }

        public static Vector3 FaceUvToDirection(PlanetCubeFace face, float u, float v)
        {
            float rawU = u * 2f - 1f;
            float rawV = v * 2f - 1f;
            Vector3 direction;

            switch (face)
            {
                case PlanetCubeFace.Left:
                    direction = new Vector3(1f, -rawV, -rawU);
                    break;
                case PlanetCubeFace.Right:
                    direction = new Vector3(-1f, -rawV, rawU);
                    break;
                case PlanetCubeFace.Up:
                    direction = new Vector3(-rawU, 1f, -rawV);
                    break;
                case PlanetCubeFace.Down:
                    direction = new Vector3(rawU, -1f, -rawV);
                    break;
                case PlanetCubeFace.Back:
                    direction = new Vector3(rawU, -rawV, 1f);
                    break;
                case PlanetCubeFace.Front:
                    direction = new Vector3(-rawU, -rawV, -1f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(face));
            }

            direction.Normalize();
            return direction;
        }

        public static float GetLongitudeRuleValue(Vector3 direction)
        {
            Vector2 longitude = new Vector2(-direction.X, -direction.Z);
            if (longitude.LengthSquared() <= 1e-12f)
                return 0f;

            longitude.Normalize();
            float value = longitude.Y;
            if (-direction.X > 0f)
                value = 2f - value;
            return value;
        }

        internal static void DirectionToFaceUv(
            Vector3 direction,
            out PlanetCubeFace face,
            out float u,
            out float v)
        {
            Vector3 absolute = Vector3.Abs(direction);
            float rawU;
            float rawV;

            if (absolute.X > absolute.Y && absolute.X > absolute.Z)
            {
                rawV = -direction.Y / absolute.X;
                if (direction.X > 0f)
                {
                    face = PlanetCubeFace.Left;
                    rawU = -direction.Z / absolute.X;
                }
                else
                {
                    face = PlanetCubeFace.Right;
                    rawU = direction.Z / absolute.X;
                }
            }
            else if (absolute.Y > absolute.Z)
            {
                rawV = -direction.Z / absolute.Y;
                if (direction.Y > 0f)
                {
                    face = PlanetCubeFace.Up;
                    rawU = -direction.X / absolute.Y;
                }
                else
                {
                    face = PlanetCubeFace.Down;
                    rawU = direction.X / absolute.Y;
                }
            }
            else
            {
                rawV = -direction.Y / absolute.Z;
                if (direction.Z > 0f)
                {
                    face = PlanetCubeFace.Back;
                    rawU = direction.X / absolute.Z;
                }
                else
                {
                    face = PlanetCubeFace.Front;
                    rawU = -direction.X / absolute.Z;
                }
            }

            u = Clamp01((rawU + 1f) * 0.5f);
            v = Clamp01((rawV + 1f) * 0.5f);
        }

        static RawPngBitmap LoadPng(PlanetDefinitionSnapshot planet, string fileName)
        {
            string path = "Data/PlanetDataFiles/" + planet.FolderName + "/" + fileName;
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                throw new InvalidOperationException("Utilities are not ready.");

            if (planet.IsBaseGame)
            {
                if (!utilities.FileExistsInGameContent(path))
                    throw new FileNotFoundException("Planet PNG was not found.", path);

                using (var reader = utilities.ReadBinaryFileInGameContent(path))
                    return RawPngBitmap.Load(reader.BaseStream);
            }

            if (!planet.HasModItem)
                throw new InvalidOperationException("Mod planet source does not have a mod item.");
            if (!utilities.FileExistsInModLocation(path, planet.ModItem))
                throw new FileNotFoundException("Mod planet PNG was not found.", path);

            using (var reader = utilities.ReadBinaryFileInModLocation(path, planet.ModItem))
                return RawPngBitmap.Load(reader.BaseStream);
        }

        void ValidateResolution(RawPngBitmap bitmap, string fileName)
        {
            if (bitmap == null)
                throw new InvalidDataException(fileName + " decoded to null.");
            if (bitmap.Width != bitmap.Height)
                throw new InvalidDataException(fileName + " is not square.");

            if (Resolution == 0)
                Resolution = bitmap.Width;
            else if (bitmap.Width != Resolution || bitmap.Height != Resolution)
                throw new InvalidDataException(fileName + " does not match the other cubemap faces.");
        }

        static ushort[] ExtractHeight(RawPngBitmap bitmap)
        {
            int count = checked(bitmap.Width * bitmap.Height);
            var result = new ushort[count];
            if (bitmap.RedSamples16 != null)
            {
                Array.Copy(bitmap.RedSamples16, result, count);
                return result;
            }

            for (int i = 0; i < count; i++)
                result[i] = (ushort)(bitmap.Pixels[i * 4] * 257);
            return result;
        }

        static byte[] ExtractMaterialRed(RawPngBitmap bitmap)
        {
            int count = checked(bitmap.Width * bitmap.Height);
            var result = new byte[count];
            for (int i = 0; i < count; i++)
                result[i] = bitmap.Pixels[i * 4];
            return result;
        }

        static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
                return minimum;
            if (value > maximum)
                return maximum;
            return value;
        }

        static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value >= 1f)
                return 0.99999994f;
            return value;
        }
    }
}
