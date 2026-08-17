using System;
using System.IO;
using Adk.Image.Png;
using Sandbox.ModAPI;
using VoxelCubemapApi.Api;
using VRageMath;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

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
        readonly byte[][] _biomeIds = new byte[6][];
        int _heightResolution;
        int _materialResolution;
        int _biomeResolution;
        ushort _minimumHeight = ushort.MaxValue;
        ushort _maximumHeight;

        public int Resolution { get; private set; }

        public int HeightResolution
        {
            get { return _heightResolution; }
        }

        public int MaterialResolution
        {
            get { return _materialResolution; }
        }

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
            CartographyLayer layer,
            CartographyCancellation cancellation,
            PlanetMetadataProvider runtimePlanetMetadata = null,
            bool requireHeightForOverlay = false)
        {
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));
            if (cancellation == null)
                throw new ArgumentNullException(nameof(cancellation));

            bool loadHeight;
            bool loadMaterials;
            bool loadBiomes;
            switch (layer)
            {
                case CartographyLayer.Satellite:
                    loadHeight = true;
                    loadMaterials = true;
                    loadBiomes = false;
                    break;
                case CartographyLayer.Terrain:
                    loadHeight = true;
                    loadMaterials = false;
                    loadBiomes = false;
                    break;
                case CartographyLayer.Materials:
                    loadHeight = requireHeightForOverlay;
                    loadMaterials = true;
                    loadBiomes = false;
                    break;
                case CartographyLayer.Biomes:
                    loadHeight = requireHeightForOverlay;
                    loadMaterials = false;
                    loadBiomes = true;
                    break;
                default:
                    throw new NotSupportedException("The requested cartography layer is not implemented.");
            }

            var source = new PlanetMapSource();
            int runtimeFaceResolution = 0;
            for (int i = 0; i < ExportOrder.Length; i++)
            {
                cancellation.ThrowIfCancelled();
                PlanetCubeFace face = ExportOrder[i];
                string faceName = GetFaceName(face);

                if (runtimePlanetMetadata != null && runtimeFaceResolution == 0)
                {
                    int[] size = runtimePlanetMetadata.GetFaceSize(faceName);
                    runtimeFaceResolution = ValidateRuntimeFaceSize(size, faceName);
                }

                if (loadHeight)
                {
                    if (runtimePlanetMetadata != null)
                    {
                        ushort[] height = runtimePlanetMetadata.LoadHeightFace(faceName);
                        ValidateRuntimeFaceSamples(height, runtimeFaceResolution, faceName + ".png");
                        source._heightResolution = runtimeFaceResolution;
                        source._heights[(int)face] = height;
                    }
                    else
                    {
                        RawPngBitmap height = LoadPng(planet, faceName + ".png");
                        source.ValidateHeightResolution(height, faceName + ".png");
                        source._heights[(int)face] = ExtractHeight(height);
                    }
                }

                if (loadMaterials)
                {
                    cancellation.ThrowIfCancelled();
                    if (runtimePlanetMetadata != null)
                    {
                        byte[] material = runtimePlanetMetadata.LoadMaterialFace(faceName);
                        ValidateRuntimeFaceSamples(material, runtimeFaceResolution, faceName + "_mat.png");
                        source._materialResolution = runtimeFaceResolution;
                        source._materialIds[(int)face] = material;
                    }
                    else
                    {
                        RawPngBitmap material = LoadPng(planet, faceName + "_mat.png");
                        source.ValidateMaterialResolution(material, faceName + "_mat.png");
                        source._materialIds[(int)face] = ExtractMaterialChannel(material, 0);
                    }
                }

                if (loadBiomes)
                {
                    cancellation.ThrowIfCancelled();
                    if (runtimePlanetMetadata != null)
                    {
                        byte[] biome = runtimePlanetMetadata.LoadBiomeFace(faceName);
                        ValidateRuntimeFaceSamples(biome, runtimeFaceResolution, faceName + "_mat.png");
                        source._biomeResolution = runtimeFaceResolution;
                        source._biomeIds[(int)face] = biome;
                    }
                    else
                    {
                        RawPngBitmap material = LoadPng(planet, faceName + "_mat.png");
                        source.ValidateBiomeResolution(material, faceName + "_mat.png");
                        source._biomeIds[(int)face] = ExtractMaterialChannel(material, 1);
                    }
                }
            }

            source.Resolution = source.GetLayerResolution(layer);
            if (loadHeight)
                source.CalculateHeightRange();
            return source;
        }

        public float SampleHeightNormalized(Vector3 direction)
        {
            PlanetCubeFace face;
            float u;
            float v;
            DirectionToFaceUv(direction, out face, out u, out v);

            int resolution = _heightResolution;
            float x = u * (resolution - 1);
            float y = v * (resolution - 1);
            int x0 = Clamp((int)Math.Floor(x), 0, resolution - 1);
            int y0 = Clamp((int)Math.Floor(y), 0, resolution - 1);
            int x1 = Math.Min(x0 + 1, resolution - 1);
            int y1 = Math.Min(y0 + 1, resolution - 1);
            float tx = x - x0;
            float ty = y - y0;
            ushort[] data = _heights[(int)face];

            float h00 = data[y0 * resolution + x0] / 65535f;
            float h10 = data[y0 * resolution + x1] / 65535f;
            float h01 = data[y1 * resolution + x0] / 65535f;
            float h11 = data[y1 * resolution + x1] / 65535f;

            float top = h00 + (h10 - h00) * tx;
            float bottom = h01 + (h11 - h01) * tx;
            return top + (bottom - top) * ty;
        }

        public float SampleHeightMinMaxNormalized(Vector3 direction)
        {
            float height = SampleHeightNormalized(direction);
            float minimum = _minimumHeight / 65535f;
            float maximum = _maximumHeight / 65535f;
            float range = maximum - minimum;
            if (range <= 0.0000001f)
                return 0.5f;

            return Clamp01((height - minimum) / range);
        }

        public byte SampleMaterialNearest(PlanetCubeFace face, float u, float v)
        {
            int resolution = _materialResolution;
            int x = Clamp((int)(u * resolution), 0, resolution - 1);
            int y = Clamp((int)(v * resolution), 0, resolution - 1);
            return _materialIds[(int)face][y * resolution + x];
        }

        public byte SampleBiomeNearest(PlanetCubeFace face, float u, float v)
        {
            int resolution = _biomeResolution;
            int x = Clamp((int)(u * resolution), 0, resolution - 1);
            int y = Clamp((int)(v * resolution), 0, resolution - 1);
            return _biomeIds[(int)face][y * resolution + x];
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

        void CalculateHeightRange()
        {
            ushort minimum = ushort.MaxValue;
            ushort maximum = ushort.MinValue;

            for (int face = 0; face < _heights.Length; face++)
            {
                ushort[] values = _heights[face];
                if (values == null)
                    continue;

                for (int i = 0; i < values.Length; i++)
                {
                    ushort value = values[i];
                    if (value < minimum)
                        minimum = value;
                    if (value > maximum)
                        maximum = value;
                }
            }

            if (minimum == ushort.MaxValue)
                minimum = 0;

            _minimumHeight = minimum;
            _maximumHeight = maximum;
        }

        static RawPngBitmap LoadPng(PlanetDefinitionSnapshot planet, string fileName)
        {
            string path = "Data/PlanetDataFiles/" + planet.FolderName + "/" + fileName;
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                throw new InvalidOperationException("Utilities are not ready.");

            // Planet placer and compatibility mods can define a generator whose
            // PlanetDataFiles are supplied by another active mod. Search from the
            // highest-priority (last-loaded) mod to the lowest before falling back
            // to vanilla content, matching definition override precedence.
            var mods = planet.ModLoadOrder;
            if (mods != null)
            {
                for (int i = mods.Length - 1; i >= 0; i--)
                {
                    try
                    {
                        using (var reader = utilities.ReadBinaryFileInModLocation(path, mods[i]))
                            return RawPngBitmap.Load(reader.BaseStream);
                    }
                    catch (FileNotFoundException)
                    {
                        // Linux contains flaky FileExistsInModLocation
                    }
                }
            }

            using (var reader = utilities.ReadBinaryFileInGameContent(path))
                return RawPngBitmap.Load(reader.BaseStream);
        }

        void ValidateHeightResolution(RawPngBitmap bitmap, string fileName)
        {
            ValidateResolution(
                bitmap,
                fileName,
                "height",
                ref _heightResolution);
        }

        void ValidateMaterialResolution(RawPngBitmap bitmap, string fileName)
        {
            ValidateResolution(
                bitmap,
                fileName,
                "material",
                ref _materialResolution);
        }

        void ValidateBiomeResolution(RawPngBitmap bitmap, string fileName)
        {
            ValidateResolution(
                bitmap,
                fileName,
                "biome",
                ref _biomeResolution);
        }

        static int ValidateRuntimeFaceSize(int[] size, string faceName)
        {
            if (size == null || size.Length < 2 || size[0] <= 0 || size[1] <= 0)
                throw new InvalidDataException(faceName + " returned an invalid runtime cubemap size.");
            if (size[0] != size[1])
                throw new InvalidDataException(faceName + " runtime cubemap face is not square.");

            return size[0];
        }

        static void ValidateRuntimeFaceSamples(Array samples, int resolution, string fileName)
        {
            if (samples == null)
                throw new InvalidDataException(fileName + " returned null runtime cubemap samples.");

            int expected = checked(resolution * resolution);
            if (samples.Length != expected)
            {
                throw new InvalidDataException(
                    fileName + " returned " + samples.Length +
                    " runtime cubemap samples; expected " + expected + ".");
            }
        }

        static void ValidateResolution(
            RawPngBitmap bitmap,
            string fileName,
            string familyName,
            ref int resolution)
        {
            if (bitmap == null)
                throw new InvalidDataException(fileName + " decoded to null.");
            if (bitmap.Width != bitmap.Height)
                throw new InvalidDataException(fileName + " is not square.");

            if (resolution == 0)
                resolution = bitmap.Width;
            else if (bitmap.Width != resolution || bitmap.Height != resolution)
            {
                throw new InvalidDataException(
                    fileName + " does not match the other " + familyName +
                    " cubemap faces.");
            }
        }

        int GetLayerResolution(CartographyLayer layer)
        {
            switch (layer)
            {
                case CartographyLayer.Satellite:
                    if (_heightResolution <= 0 || _materialResolution <= 0)
                    {
                        throw new InvalidDataException(
                            "Satellite cartography requires both height and material cubemap faces.");
                    }

                    // Height and material maps are sampled independently in normalized
                    // face UV space. Limit full-resolution output to the lower-detail
                    // family instead of expanding a 4K categorical map to a 16K array.
                    return Math.Min(_heightResolution, _materialResolution);

                case CartographyLayer.Terrain:
                    if (_heightResolution <= 0)
                        throw new InvalidDataException("Terrain cartography requires height cubemap faces.");
                    return _heightResolution;

                case CartographyLayer.Materials:
                    if (_materialResolution <= 0)
                        throw new InvalidDataException("Material cartography requires material cubemap faces.");
                    return _materialResolution;

                case CartographyLayer.Biomes:
                    if (_biomeResolution <= 0)
                        throw new InvalidDataException("Biome cartography requires biome cubemap faces.");
                    return _biomeResolution;

                default:
                    throw new NotSupportedException("The requested cartography layer is not implemented.");
            }
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

        static byte[] ExtractMaterialChannel(RawPngBitmap bitmap, int channel)
        {
            int count = checked(bitmap.Width * bitmap.Height);
            var result = new byte[count];
            for (int i = 0; i < count; i++)
                result[i] = bitmap.Pixels[i * 4 + channel];
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
