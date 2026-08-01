using System;
using System.Collections.Generic;
using LcdMod.Common.Imaging;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography
{
    internal sealed class PaintedPlanetFaces
    {
        public readonly Dictionary<PlanetCubeFace, RawRgbaBitmap> Faces =
            new Dictionary<PlanetCubeFace, RawRgbaBitmap>();
        public Dictionary<string, long> MissingMaterialUsage;
    }

    internal static class PlanetSurfacePainter
    {
        public static PaintedPlanetFaces Render(
            PlanetMapSource source,
            PlanetDefinitionSnapshot planet,
            FarColorCatalogSnapshot farColors,
            CartographyRequest request,
            CartographyCancellation cancellation)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (cancellation == null)
                throw new ArgumentNullException(nameof(cancellation));

            if (request.Projection != CartographyProjection.CubemapFaces)
                throw new NotSupportedException("Only cubemap-face cartography is implemented.");
            if (request.Layer == CartographyLayer.Satellite && farColors == null)
                throw new ArgumentNullException(nameof(farColors));

            int outputSide = request.MaximumFaceSide > 0
                ? Math.Min(request.MaximumFaceSide, source.Resolution)
                : source.Resolution;

            var result = new PaintedPlanetFaces();
            if (request.IncludeDiagnostics && request.Layer == CartographyLayer.Satellite)
            {
                result.MissingMaterialUsage =
                    new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            }
            for (int faceIndex = 0; faceIndex < PlanetMapSource.ExportOrder.Length; faceIndex++)
            {
                cancellation.ThrowIfCancelled();
                PlanetCubeFace face = PlanetMapSource.ExportOrder[faceIndex];
                result.Faces[face] = RenderFace(
                    source,
                    planet,
                    farColors,
                    request.Layer,
                    face,
                    outputSide,
                    result.MissingMaterialUsage,
                    cancellation);
            }

            return result;
        }

        static RawRgbaBitmap RenderFace(
            PlanetMapSource source,
            PlanetDefinitionSnapshot planet,
            FarColorCatalogSnapshot farColors,
            CartographyLayer layer,
            PlanetCubeFace face,
            int outputSide,
            Dictionary<string, long> missingMaterialUsage,
            CartographyCancellation cancellation)
        {
            switch (layer)
            {
                case CartographyLayer.Satellite:
                    return RenderSatelliteFace(
                        source,
                        planet,
                        farColors,
                        face,
                        outputSide,
                        missingMaterialUsage,
                        cancellation);
                case CartographyLayer.Terrain:
                    return RenderTerrainFace(source, face, outputSide, cancellation);
                case CartographyLayer.Biomes:
                    return RenderBiomeFace(source, face, outputSide, cancellation);
                default:
                    throw new NotSupportedException("The requested cartography layer is not implemented.");
            }
        }

        static RawRgbaBitmap RenderSatelliteFace(
            PlanetMapSource source,
            PlanetDefinitionSnapshot planet,
            FarColorCatalogSnapshot farColors,
            PlanetCubeFace face,
            int outputSide,
            Dictionary<string, long> missingMaterialUsage,
            CartographyCancellation cancellation)
        {
            var bitmap = new RawRgbaBitmap(outputSide, outputSide);
            float directionStep = 1f / Math.Max(2, source.HeightResolution - 1);

            for (int y = 0; y < outputSide; y++)
            {
                if ((y & 15) == 0)
                    cancellation.ThrowIfCancelled();

                float v = (y + 0.5f) / outputSide;
                for (int x = 0; x < outputSide; x++)
                {
                    float u = (x + 0.5f) / outputSide;
                    Vector3 direction = PlanetMapSource.FaceUvToDirection(face, u, v);
                    float height = source.SampleHeightNormalized(direction);
                    float latitude = direction.Y;
                    float longitude = PlanetMapSource.GetLongitudeRuleValue(direction);
                    float slope = CalculateSlopeCosine(
                        source,
                        planet,
                        face,
                        u,
                        v,
                        directionStep,
                        direction);

                    byte materialMapValue = source.SampleMaterialNearest(face, u, v);
                    string material = ResolveSurfaceMaterial(
                        planet,
                        materialMapValue,
                        height,
                        latitude,
                        longitude,
                        slope);

                    Color color;
                    bool resolved = farColors.TryGet(material, out color);
                    if (!resolved)
                        color = FarColorCatalogSnapshot.MissingColorFallback;

                    if (missingMaterialUsage != null &&
                        (!resolved || farColors.IsMissing(material)))
                    {
                        string name = string.IsNullOrWhiteSpace(material)
                            ? "<empty material>"
                            : material;
                        long count;
                        missingMaterialUsage.TryGetValue(name, out count);
                        missingMaterialUsage[name] = count + 1L;
                    }

                    bitmap.SetPixel(x, y, color.R, color.G, color.B, color.A);
                }
            }

            return bitmap;
        }

        static RawRgbaBitmap RenderTerrainFace(
            PlanetMapSource source,
            PlanetCubeFace face,
            int outputSide,
            CartographyCancellation cancellation)
        {
            var bitmap = new RawRgbaBitmap(outputSide, outputSide);

            for (int y = 0; y < outputSide; y++)
            {
                if ((y & 31) == 0)
                    cancellation.ThrowIfCancelled();

                float v = (y + 0.5f) / outputSide;
                for (int x = 0; x < outputSide; x++)
                {
                    float u = (x + 0.5f) / outputSide;
                    Vector3 direction = PlanetMapSource.FaceUvToDirection(face, u, v);
                    float normalizedHeight = source.SampleHeightMinMaxNormalized(direction);
                    byte shade = (byte)Math.Round(normalizedHeight * 255f);
                    bitmap.SetPixel(x, y, shade, shade, shade, 255);
                }
            }

            return bitmap;
        }

        static RawRgbaBitmap RenderBiomeFace(
            PlanetMapSource source,
            PlanetCubeFace face,
            int outputSide,
            CartographyCancellation cancellation)
        {
            var bitmap = new RawRgbaBitmap(outputSide, outputSide);

            for (int y = 0; y < outputSide; y++)
            {
                if ((y & 31) == 0)
                    cancellation.ThrowIfCancelled();

                float v = (y + 0.5f) / outputSide;
                for (int x = 0; x < outputSide; x++)
                {
                    float u = (x + 0.5f) / outputSide;
                    byte biome = source.SampleMaterialNearest(face, u, v);
                    Color color = GetBiomeColor(biome);
                    bitmap.SetPixel(x, y, color.R, color.G, color.B, color.A);
                }
            }

            return bitmap;
        }

        internal static Color GetBiomeColor(byte biome)
        {
            // An odd multiplier makes the red channel an injective permutation of
            // all 256 biome values. The other channels are independent permutations,
            // producing stable, high-contrast categorical colors without a palette.
            byte red = (byte)((biome * 73 + 41) & 255);
            byte greenSeed = (byte)((biome * 151 + 97) & 255);
            byte blueSeed = (byte)((biome * 199 + 17) & 255);
            byte green = (byte)(48 + greenSeed * 175 / 255);
            byte blue = (byte)(48 + blueSeed * 175 / 255);
            return new Color(red, green, blue, 255);
        }

        static float CalculateSlopeCosine(
            PlanetMapSource source,
            PlanetDefinitionSnapshot planet,
            PlanetCubeFace face,
            float u,
            float v,
            float step,
            Vector3 radialDirection)
        {
            Vector3 leftDirection = PlanetMapSource.FaceUvToDirection(face, u - step, v);
            Vector3 rightDirection = PlanetMapSource.FaceUvToDirection(face, u + step, v);
            Vector3 downDirection = PlanetMapSource.FaceUvToDirection(face, u, v - step);
            Vector3 upDirection = PlanetMapSource.FaceUvToDirection(face, u, v + step);

            Vector3 left = SurfacePoint(source, planet, leftDirection);
            Vector3 right = SurfacePoint(source, planet, rightDirection);
            Vector3 down = SurfacePoint(source, planet, downDirection);
            Vector3 up = SurfacePoint(source, planet, upDirection);

            Vector3 tangentU = right - left;
            Vector3 tangentV = up - down;
            Vector3 normal = Vector3.Cross(tangentU, tangentV);
            if (normal.Normalize() <= 1e-8f)
                return 1f;

            float slope = Vector3.Dot(normal, radialDirection);
            if (slope < 0f)
                slope = -slope;
            return MathHelper.Clamp(slope, 0f, 1f);
        }

        static Vector3 SurfacePoint(
            PlanetMapSource source,
            PlanetDefinitionSnapshot planet,
            Vector3 direction)
        {
            float height = source.SampleHeightNormalized(direction);
            double hillRatio = planet.MinimumHillRatio +
                               height * (planet.MaximumHillRatio - planet.MinimumHillRatio);
            float radius = (float)(planet.RadiusMeters * (1d + hillRatio));
            return direction * radius;
        }

        static string ResolveSurfaceMaterial(
            PlanetDefinitionSnapshot planet,
            byte mapValue,
            float height,
            float latitude,
            float longitude,
            float slope)
        {
            string direct = planet.DirectSurfaceMaterials[mapValue];
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            MaterialRuleSnapshot[] rules = planet.MaterialGroups[mapValue];
            if (rules != null)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    MaterialRuleSnapshot rule = rules[i];
                    if (rule != null && rule.Matches(height, latitude, longitude, slope))
                        return rule.SurfaceMaterial;
                }
            }

            return planet.DefaultSurfaceMaterial;
        }
    }
}
