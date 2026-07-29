using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using LcdMod.Common.Imaging;
using Sandbox.Definitions;
using PlanetGeneratorDefinition = Sandbox.Definitions.MyPlanetGeneratorDefinition;
using VoxelMaterialDefinition = VRage.Game.MyVoxelMaterialDefinition;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography
{

    internal sealed class MaterialRuleSnapshot
    {
        public float MinimumHeight;
        public float MaximumHeight;
        public float MinimumLatitude;
        public float MaximumLatitude;
        public bool MirrorLatitude;
        public float MinimumLongitude;
        public float MaximumLongitude;
        public float MinimumSlope;
        public float MaximumSlope;
        public string SurfaceMaterial;

        public bool Matches(float height, float latitude, float longitude, float slope)
        {
            if (MirrorLatitude)
                latitude = Math.Abs(latitude);

            return height >= MinimumHeight && height <= MaximumHeight &&
                   latitude >= MinimumLatitude && latitude <= MaximumLatitude &&
                   longitude >= MinimumLongitude && longitude <= MaximumLongitude &&
                   slope >= MinimumSlope && slope <= MaximumSlope;
        }
    }

    internal sealed class PlanetDefinitionSnapshot
    {
        public string GeneratorSubtype;
        public string FolderName;
        public bool IsBaseGame;
        public bool HasModItem;
        public MyObjectBuilder_Checkpoint.ModItem ModItem;
        public double RadiusMeters;
        public float MinimumHillRatio;
        public float MaximumHillRatio;
        public string DefaultSurfaceMaterial;
        public readonly string[] DirectSurfaceMaterials = new string[256];
        public readonly MaterialRuleSnapshot[][] MaterialGroups = new MaterialRuleSnapshot[256][];
    }

    [XmlRoot("MyRenderVoxelMaterialData")]
    public sealed class VoxelMaterialColorData
    {
        public Vector4 Far3Color;

        [XmlArray("TextureSets")]
        [XmlArrayItem("TextureSet")]
        public VoxelMaterialTextureSetData[] TextureSets;

        public static implicit operator VoxelMaterialColorData(VoxelMaterialDefinition voxel)
        {
            if (voxel == null)
                return null;

            return MyAPIGateway.Utilities.SerializeFromXML<VoxelMaterialColorData>(
                MyAPIGateway.Utilities.SerializeToXML<object>(voxel.RenderParams));
        }
    }

    [XmlType("TextureSet")]
    public sealed class VoxelMaterialTextureSetData
    {
        public string ColorMetalXZnY;
    }

    internal sealed class FarColorTextureFallbackSnapshot
    {
        public string Subtype;
        public string Far2Path;
        public string Far1Path;
        public string BasePath;
        public string ThumbnailPath;
        public bool IsBaseGame;
        public bool HasModItem;
        public MyObjectBuilder_Checkpoint.ModItem ModItem;
    }

    internal sealed class FarColorCatalogSnapshot
    {
        internal static readonly Color MissingColorFallback = new Color(255, 0, 255, 255);

        readonly Dictionary<string, Color> _colors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        readonly List<FarColorTextureFallbackSnapshot> _textureFallbacks =
            new List<FarColorTextureFallbackSnapshot>();

        public void Add(string subtype, Vector4 color)
        {
            Add(subtype, new Color(
                ToByte(color.X),
                ToByte(color.Y),
                ToByte(color.Z),
                255));
        }

        public void Add(string subtype, Color color)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return;

            _colors[subtype] = color;
        }

        public void AddTextureFallback(FarColorTextureFallbackSnapshot fallback)
        {
            if (fallback == null || string.IsNullOrWhiteSpace(fallback.Subtype))
                return;

            _textureFallbacks.Add(fallback);
        }

        public void ResolveTextureFallbacks(CartographyCancellation cancellation)
        {
            if (cancellation == null)
                throw new ArgumentNullException(nameof(cancellation));

            var colorCache = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            var failedCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _textureFallbacks.Count; i++)
            {
                cancellation.ThrowIfCancelled();
                FarColorTextureFallbackSnapshot fallback = _textureFallbacks[i];
                if (_colors.ContainsKey(fallback.Subtype))
                    continue;

                Color color;
                if (TryResolveMipFallback(
                        fallback,
                        fallback.Far2Path,
                        colorCache,
                        failedCache,
                        out color) ||
                    TryResolveMipFallback(
                        fallback,
                        fallback.Far1Path,
                        colorCache,
                        failedCache,
                        out color) ||
                    TryResolveMipFallback(
                        fallback,
                        fallback.BasePath,
                        colorCache,
                        failedCache,
                        out color) ||
                    TryResolveThumbnailFallback(
                        fallback,
                        colorCache,
                        failedCache,
                        out color))
                {
                    Add(fallback.Subtype, color);
                }
                else
                {
                    Add(fallback.Subtype, MissingColorFallback);
                }
            }

            _textureFallbacks.Clear();
        }

        public bool TryGet(string subtype, out Color color)
        {
            if (string.IsNullOrWhiteSpace(subtype))
            {
                color = default(Color);
                return false;
            }

            return _colors.TryGetValue(subtype, out color);
        }

        static bool TryResolveMipFallback(
            FarColorTextureFallbackSnapshot fallback,
            string texturePath,
            Dictionary<string, Color> colorCache,
            HashSet<string> failedCache,
            out Color color)
        {
            return TryResolveTextureColor(
                fallback,
                NormalizeContentPath(texturePath),
                true,
                colorCache,
                failedCache,
                out color);
        }

        static bool TryResolveThumbnailFallback(
            FarColorTextureFallbackSnapshot fallback,
            Dictionary<string, Color> colorCache,
            HashSet<string> failedCache,
            out Color color)
        {
            return TryResolveTextureColor(
                fallback,
                NormalizeContentPath(fallback.ThumbnailPath),
                false,
                colorCache,
                failedCache,
                out color);
        }

        static bool TryResolveTextureColor(
            FarColorTextureFallbackSnapshot fallback,
            string path,
            bool useSmallestMip,
            Dictionary<string, Color> colorCache,
            HashSet<string> failedCache,
            out Color color)
        {
            color = default(Color);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string cacheKey = BuildTextureCacheKey(fallback, path, useSmallestMip);
            if (colorCache.TryGetValue(cacheKey, out color))
                return true;
            if (failedCache.Contains(cacheKey))
                return false;

            if (!TryReadTextureColor(fallback, path, useSmallestMip, out color))
            {
                failedCache.Add(cacheKey);
                return false;
            }

            colorCache[cacheKey] = color;
            return true;
        }

        static string BuildTextureCacheKey(
            FarColorTextureFallbackSnapshot fallback,
            string path,
            bool useSmallestMip)
        {
            string mode = useSmallestMip ? "mip|" : "average|";
            if (fallback.IsBaseGame)
                return mode + "game|" + path;

            return mode + "mod|" +
                   fallback.ModItem.PublishedServiceName + "|" +
                   fallback.ModItem.PublishedFileId + "|" +
                   fallback.ModItem.Name + "|" + path;
        }

        static bool TryReadTextureColor(
            FarColorTextureFallbackSnapshot fallback,
            string path,
            bool useSmallestMip,
            out Color color)
        {
            color = default(Color);
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null || string.IsNullOrWhiteSpace(path))
                return false;

            if (!fallback.IsBaseGame && fallback.HasModItem)
            {
                try
                {
                    if (utilities.FileExistsInModLocation(path, fallback.ModItem))
                    {
                        using (var reader = utilities.ReadBinaryFileInModLocation(path, fallback.ModItem))
                            return TryDecodeTextureColor(reader, useSmallestMip, out color);
                    }
                }
                catch
                {
                    color = default(Color);
                }
            }

            try
            {
                if (!utilities.FileExistsInGameContent(path))
                    return false;

                using (var reader = utilities.ReadBinaryFileInGameContent(path))
                    return TryDecodeTextureColor(reader, useSmallestMip, out color);
            }
            catch
            {
                color = default(Color);
                return false;
            }
        }

        static bool TryDecodeTextureColor(
            BinaryReader reader,
            bool useSmallestMip,
            out Color color)
        {
            color = default(Color);
            if (reader == null)
                return false;

            byte r;
            byte g;
            byte b;
            bool decoded = useSmallestMip
                ? DdsAverageColor.TryAverageSmallestMip(reader.BaseStream, out r, out g, out b)
                : DdsAverageColor.TryAverageFirstMip(reader.BaseStream, out r, out g, out b);

            if (!decoded)
                return false;

            color = new Color(r, g, b, 255);
            return true;
        }

        static string NormalizeContentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim().Replace('\\', '/');
            while (path.Length > 0 && path[0] == '/')
                path = path.Substring(1);

            return path;
        }

        static byte ToByte(float linearValue)
        {
            float value = ToSrgb(MathHelper.Clamp(linearValue, 0f, 1f));
            return (byte)(value * 255f + 0.5f);
        }

        static float ToSrgb(float linearValue)
        {
            if (linearValue <= 0.0031308f)
                return linearValue * 12.92f;

            return (float)(Math.Pow(linearValue, 1d / 2.4d) * 1.055d - 0.055d);
        }
    }

    internal static class CartographySnapshotBuilder
    {
        const double DEFAULT_PLANET_RADIUS_METERS = 60000d;

        public static PlanetDefinitionSnapshot BuildPlanet(CartographyRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (MyDefinitionManager.Static == null)
                throw new InvalidOperationException("Definition manager is not ready.");

            MyPlanet planet = ResolvePlanet(request);
            PlanetGeneratorDefinition generator = planet != null
                ? planet.Generator
                : ResolveGenerator(request.PlanetGeneratorSubtype);

            if (generator == null)
                throw new InvalidOperationException("Planet generator definition was not found.");

            double radius = request.PlanetRadiusMeters;
            if (radius <= 0d && planet != null)
                radius = planet.AverageRadius > 0d ? planet.AverageRadius : planet.MaximumRadius;
            if (radius <= 0d)
                radius = DEFAULT_PLANET_RADIUS_METERS;

            var snapshot = new PlanetDefinitionSnapshot
            {
                GeneratorSubtype = generator.Id.SubtypeName,
                FolderName = generator.FolderName,
                RadiusMeters = radius,
                MinimumHillRatio = generator.HillParams.Min,
                MaximumHillRatio = generator.HillParams.Max,
                DefaultSurfaceMaterial = GetSurfaceMaterial(generator.DefaultSurfaceMaterial),
                IsBaseGame = generator.Context == null || generator.Context.IsBaseGame,
                HasModItem = generator.Context != null && !generator.Context.IsBaseGame,
                ModItem = generator.Context != null ? generator.Context.ModItem : default(MyObjectBuilder_Checkpoint.ModItem)
            };

            CopyDirectMaterials(generator, snapshot);
            CopyMaterialGroups(generator, snapshot);
            return snapshot;
        }

        public static FarColorCatalogSnapshot BuildFarColors(PlanetDefinitionSnapshot planet)
        {
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));
            if (MyDefinitionManager.Static == null)
                throw new InvalidOperationException("Definition manager is not ready.");

            HashSet<string> requiredMaterials = CollectSurfaceMaterials(planet);
            var foundMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new FarColorCatalogSnapshot();
            foreach (var voxel in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
            {
                if (voxel == null ||
                    !requiredMaterials.Contains(voxel.Id.SubtypeName))
                {
                    continue;
                }

                foundMaterials.Add(voxel.Id.SubtypeName);

                // MDK prohibits direct access to MyRenderVoxelMaterialData members.
                // Serialize the allowed RenderParams value into a module-owned DTO.
                VoxelMaterialColorData renderData = voxel;
                Vector4 far3Color = renderData != null
                    ? renderData.Far3Color
                    : default(Vector4);
                if (!IsMissingFarColor(far3Color))
                {
                    result.Add(voxel.Id.SubtypeName, far3Color);
                    continue;
                }

                bool isBaseGame = voxel.Context == null || voxel.Context.IsBaseGame;
                result.AddTextureFallback(new FarColorTextureFallbackSnapshot
                {
                    Subtype = voxel.Id.SubtypeName,
                    Far2Path = GetTextureSetColorPath(renderData, 2),
                    Far1Path = GetTextureSetColorPath(renderData, 1),
                    BasePath = GetTextureSetColorPath(renderData, 0),
                    ThumbnailPath = GetVoxelThumbnailPath(voxel),
                    IsBaseGame = isBaseGame,
                    HasModItem = voxel.Context != null && !voxel.Context.IsBaseGame,
                    ModItem = voxel.Context != null
                        ? voxel.Context.ModItem
                        : default(MyObjectBuilder_Checkpoint.ModItem)
                });
            }

            foreach (string material in requiredMaterials)
            {
                if (!foundMaterials.Contains(material))
                    result.Add(material, FarColorCatalogSnapshot.MissingColorFallback);
            }

            return result;
        }

        static HashSet<string> CollectSurfaceMaterials(PlanetDefinitionSnapshot planet)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddMaterial(result, planet.DefaultSurfaceMaterial);

            for (int i = 0; i < planet.DirectSurfaceMaterials.Length; i++)
                AddMaterial(result, planet.DirectSurfaceMaterials[i]);

            for (int groupIndex = 0; groupIndex < planet.MaterialGroups.Length; groupIndex++)
            {
                MaterialRuleSnapshot[] rules = planet.MaterialGroups[groupIndex];
                if (rules == null)
                    continue;

                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                    AddMaterial(result, rules[ruleIndex].SurfaceMaterial);
            }

            return result;
        }

        static void AddMaterial(HashSet<string> materials, string material)
        {
            if (!string.IsNullOrWhiteSpace(material))
                materials.Add(material);
        }

        static bool IsMissingFarColor(Vector4 color)
        {
            const float epsilon = 0.000001f;
            return Math.Abs(color.X) <= epsilon &&
                   Math.Abs(color.Y) <= epsilon &&
                   Math.Abs(color.Z) <= epsilon;
        }

        static string GetTextureSetColorPath(
            VoxelMaterialColorData renderData,
            int textureSetIndex)
        {
            if (renderData == null ||
                renderData.TextureSets == null ||
                textureSetIndex < 0 ||
                textureSetIndex >= renderData.TextureSets.Length)
            {
                return null;
            }

            VoxelMaterialTextureSetData textureSet =
                renderData.TextureSets[textureSetIndex];
            return textureSet != null
                ? textureSet.ColorMetalXZnY
                : null;
        }

        static string GetVoxelThumbnailPath(VoxelMaterialDefinition voxel)
        {
            if (voxel == null)
                return null;

            string path = voxel.VoxelHandPreview;
            if (string.IsNullOrWhiteSpace(path))
                path = "Textures/Voxels/" + voxel.Id.SubtypeName + "_Thumbnail.dds";

            return path;
        }

        static MyPlanet ResolvePlanet(CartographyRequest request)
        {
            if (request.PlanetEntityId != 0)
            {
                MyPlanet direct;
                if (LcdMod.Client.Helpers.PlanetHelper.PlanetsById.TryGetValue(request.PlanetEntityId, out direct))
                    return direct;
            }

            if (string.IsNullOrWhiteSpace(request.PlanetGeneratorSubtype))
                return null;

            foreach (var pair in LcdMod.Client.Helpers.PlanetHelper.PlanetsById)
            {
                var candidate = pair.Value;
                if (candidate == null || candidate.MarkedForClose || candidate.Generator == null)
                    continue;

                if (string.Equals(
                        candidate.Generator.Id.SubtypeName,
                        request.PlanetGeneratorSubtype,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        static PlanetGeneratorDefinition ResolveGenerator(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return null;

            return MyDefinitionManager.Static.GetDefinition<PlanetGeneratorDefinition>(
                MyStringHash.GetOrCompute(subtype));
        }

        static void CopyDirectMaterials(
            PlanetGeneratorDefinition generator,
            PlanetDefinitionSnapshot snapshot)
        {
            var materials = generator.SurfaceMaterialTable;
            if (materials == null)
                return;

            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;

                snapshot.DirectSurfaceMaterials[material.Value] = GetSurfaceMaterial(material);
            }
        }

        static void CopyMaterialGroups(
            PlanetGeneratorDefinition generator,
            PlanetDefinitionSnapshot snapshot)
        {
            var groups = generator.MaterialGroups;
            if (groups == null)
                return;

            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null || group.MaterialRules == null)
                    continue;

                var rules = new List<MaterialRuleSnapshot>(group.MaterialRules.Length);
                for (int ruleIndex = 0; ruleIndex < group.MaterialRules.Length; ruleIndex++)
                {
                    var rule = group.MaterialRules[ruleIndex];
                    string material = GetSurfaceMaterial(rule);
                    if (rule == null || string.IsNullOrWhiteSpace(material))
                        continue;

                    rules.Add(new MaterialRuleSnapshot
                    {
                        MinimumHeight = rule.Height.Min,
                        MaximumHeight = rule.Height.Max,
                        MinimumLatitude = rule.Latitude.Min,
                        MaximumLatitude = rule.Latitude.Max,
                        MirrorLatitude = rule.Latitude.Mirror,
                        MinimumLongitude = rule.Longitude.Min,
                        MaximumLongitude = rule.Longitude.Max,
                        MinimumSlope = rule.Slope.Min,
                        MaximumSlope = rule.Slope.Max,
                        SurfaceMaterial = material
                    });
                }

                snapshot.MaterialGroups[group.Value] = rules.ToArray();
            }
        }

        static string GetSurfaceMaterial(MyPlanetMaterialDefinition material)
        {
            return material != null ? material.FirstOrDefault : null;
        }
    }
}
