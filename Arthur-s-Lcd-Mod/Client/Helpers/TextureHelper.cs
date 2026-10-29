using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using LcdMod.Client.Images;
using LcdMod.Client.Market;
using LcdMod.Common.Images;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using ItemsAppBase = LcdMod.Client.Apps.Abstract.ItemsApp;

namespace LcdMod.Client.Helpers
{
    public static class TextureHelper
    {
        static readonly HashSet<MyCubeBlockDefinition> HashSet = new HashSet<MyCubeBlockDefinition>();
        const string GENERATED_PREFIX = "NpcMarket_";

        static readonly Dictionary<string, string> RegisteredGeneratedTextures =
            new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly HashSet<string> UnresolvableSprites = new HashSet<string>(StringComparer.Ordinal);
        const long COLORFUL_ICONS_REPLY_CHANNEL = (LcdMod.Common.Helpers.Constants.WORKSHOP_ID << 8) | 1L;
        static ColorfulIconsApiClient _colorfulIcons;

        public static event Action ColorfulIconsConfigChanged;
        public static event Action TextureIconCacheChanged;

        public static void InitializeColorfulIconsApi()
        {
            if (_colorfulIcons != null)
                return;

            LogHelper.Log(MyLogSeverity.Info,"initializing Colorful Icons Api");
            _colorfulIcons = new ColorfulIconsApiClient(COLORFUL_ICONS_REPLY_CHANNEL);
            _colorfulIcons.Initialized += OnColorfulIconsInitialized;
            _colorfulIcons.ConfigChanged += OnColorfulIconsConfigChanged;
            _colorfulIcons.Init();
            if (!_colorfulIcons.IsReady)
                LogHelper.Log(MyLogSeverity.Warning, "Colorful Icons API is not ready");
        }

        public static void UnloadColorfulIconsApi()
        {
            if (_colorfulIcons == null)
                return;

            _colorfulIcons.Initialized -= OnColorfulIconsInitialized;
            _colorfulIcons.ConfigChanged -= OnColorfulIconsConfigChanged;
            _colorfulIcons.Close();
            _colorfulIcons = null;
        }

        static void OnColorfulIconsInitialized(ColorfulIconsApiClient client)
        {
            LogHelper.Log(MyLogSeverity.Info,"Colorful Icons Api initialized");
            ClearBlockIconCache();
            RaiseColorfulIconsConfigChanged();
        }

        static void OnColorfulIconsConfigChanged(ColorfulIconsConfig config)
        {
            
            {
                if (ItemsAppBase.SpriteCache != null)
                    ItemsAppBase.SpriteCache.Clear();

                foreach (var surface in SurfaceScriptBase.Instances.ToList())
                {
                    if (surface != null)
                        surface.RequestRedraw();
                }
            }
            
            LogHelper.Log(MyLogSeverity.Info,"Colorful Icons request texture redraw");
            ClearBlockIconCache();
            RaiseColorfulIconsConfigChanged();
        }

        static void RaiseColorfulIconsConfigChanged()
        {
            var cacheHandler = TextureIconCacheChanged;
            if (cacheHandler != null)
                cacheHandler();

            var handler = ColorfulIconsConfigChanged;
            if (handler != null)
                handler();
        }

        public static void ClearBlockIconCache()
        {
            if (HashSet.Count == 0)
                return;

            var registeredDefinitions = new List<MyCubeBlockDefinition>(HashSet);
            HashSet.Clear();
            for (var i = 0; i < registeredDefinitions.Count; i++)
            {
                var definition = registeredDefinitions[i];
                if (definition != null)
                    GetOrAddTextureForBlock(definition);
            }
        }

        public static void PreloadAllTextures()
        {
            var sb = new StringBuilder();
            var line = new StringBuilder();

            foreach (var myDefinitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var definition = myDefinitionBase as MyCubeBlockDefinition;
                if (definition != null && !HashSet.Contains(definition))
                    line.Append(GetOrAddTextureForBlock(definition) + ", ");

                if (line.Length > 160)
                {
                    sb.AppendLine(line.ToString());
                    line.Clear();
                }
            }

            sb.AppendLine(line.ToString());
            var textures = sb.ToString().TrimEnd('\n', ',');
            LogHelper.LogInfo($"Added new Sprite textures for blocks: {{\n{textures}\n}}");
        }

        public static string GetOrAddTextureForBlock(MyCubeBlockDefinition definition)
        {
            if (!HashSet.Add(definition))
                return definition.Id.ToString();

            var texture = CreateLcdTextureDefinition(definition);
            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(texture);
            return texture.Id.SubtypeName;
        }

        public static string GetOrAddTextureForPath(string registrationKey, string displayName, string spritePath)
        {
            if (string.IsNullOrWhiteSpace(registrationKey) || string.IsNullOrWhiteSpace(spritePath) ||
                MyDefinitionManager.Static == null)
            {
                return "MissingIcon";
            }

            string existing;
            if (RegisteredGeneratedTextures.TryGetValue(registrationKey, out existing))
                return existing;

            var subtypeName = GeneratedSpriteName(registrationKey);
            var textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition), subtypeName),
                Public = false,
                LocalizationId = string.IsNullOrWhiteSpace(displayName) ? subtypeName : displayName,
                SpritePath = spritePath,
                TexturePath = spritePath,
                Selectable = false
            };

            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(textureDefinition);
            RegisteredGeneratedTextures[registrationKey] = subtypeName;
            return subtypeName;
        }


        internal static string GeneratedSpriteName(string registrationKey)
        {
            return GENERATED_PREFIX + MakeSafeTextureSubtype(registrationKey);
        }

        /// <summary>
        /// Makes sure an LCD texture exists for a sprite this mod generates on demand (block icons and
        /// Engineers Exchange icons), registering it again after a world reload. Game thread only;
        /// sprites that cannot be resolved are remembered so the lookup runs once.
        /// </summary>
        public static bool EnsureRegistered(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName) || MyDefinitionManager.Static == null)
                return false;

            if (MyDefinitionManager.Static.GetDefinition<MyLCDTextureDefinition>(MyStringHash.GetOrCompute(spriteName)) != null)
                return true;

            if (UnresolvableSprites.Contains(spriteName))
                return false;

            string registered;
            bool resolved = spriteName.StartsWith(GENERATED_PREFIX, StringComparison.Ordinal)
                ? NpcMarketAggregator.TryRegisterIcon(spriteName)
                : TryGetOrAddTextureForBlockName(spriteName, out registered) &&
                  string.Equals(registered, spriteName, StringComparison.Ordinal);

            if (!resolved)
                UnresolvableSprites.Add(spriteName);

            return resolved;
        }

        /// <summary>Forgets registrations so the next session registers its own definitions again.</summary>
        public static void ClearRegistrations()
        {
            HashSet.Clear();
            RegisteredGeneratedTextures.Clear();
            UnresolvableSprites.Clear();
        }

        public static void GetRegisteredSpriteNames(List<string> spriteNames)
        {
            if (spriteNames == null)
                return;

            foreach (var definition in HashSet)
            {
                if (definition == null)
                    continue;

                var spriteName = definition.Id.ToString();
                if (!string.IsNullOrEmpty(spriteName))
                    spriteNames.Add(spriteName);
            }

            foreach (var texture in RegisteredGeneratedTextures.Values)
                spriteNames.Add(texture);

            var library = LocalImageLibrary.Instance;
            if (library == null)
                return;

            for (var i = 0; i < library.Items.Count; i++)
                spriteNames.Add(GridImageCodec.ToSpriteName(library.Items[i].Image.Id));
        }

        public static void GetBlockSpriteNames(List<string> spriteNames)
        {
            foreach (var definition in HashSet)
            {
                if (definition != null)
                    spriteNames.Add(definition.Id.ToString());
            }
        }

        /// <summary>Generated textures as "kind/name" paths from their registration keys ("Prefab:Name" becomes "Prefab/Name").</summary>
        public static void GetGeneratedSprites(Dictionary<string, string> spritesByPath)
        {
            foreach (var pair in RegisteredGeneratedTextures)
                spritesByPath[pair.Key.Replace(':', '/')] = pair.Value;
        }

        public static string GetSpriteDisplayName(string spriteName)
        {
            string name;
            var library = LocalImageLibrary.Instance;
            return library != null && library.TryGetDisplayName(spriteName, out name) ? name : spriteName;
        }

        public static bool TryGetOrAddTextureForBlockName(string blockDefinitionName, out string textureName)
        {
            textureName = blockDefinitionName;
            if (string.IsNullOrEmpty(blockDefinitionName))
                return false;

            string requestedType;
            string requestedSubtype;
            SplitDefinitionName(blockDefinitionName, out requestedType, out requestedSubtype);
            foreach (var definitionBase in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var blockDefinition = definitionBase as MyCubeBlockDefinition;
                if (blockDefinition == null)
                    continue;

                if (!string.Equals(blockDefinition.Id.ToString(), blockDefinitionName, StringComparison.Ordinal) &&
                    !MatchesDefinitionId(blockDefinition, requestedType, requestedSubtype))
                    continue;

                textureName = GetOrAddTextureForBlock(blockDefinition);
                return true;
            }

            return false;
        }

        public static HashSet<string> BuildSpriteCatalog(IMyTextSurface surface)
        {
            var catalog = new HashSet<string>(StringComparer.Ordinal);
            if (surface == null)
                return catalog;

            var spriteNames = new List<string>();
            surface.GetSprites(spriteNames);
            for (var i = 0; i < spriteNames.Count; i++)
            {
                var spriteName = spriteNames[i];
                if (!string.IsNullOrEmpty(spriteName))
                    catalog.Add(spriteName);
            }

            return catalog;
        }

        public static string ResolveItemSprite(MyItemType itemType, IMyTextSurface surface)
        {
            var definition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemType)
                : null;
            if (definition != null)
                return ResolveItemSprite(definition, surface);

            return ResolveItemSprite(itemType.ToString(), surface, null);
        }

        static void SplitDefinitionName(string definitionName, out string typeId, out string subtypeId)
        {
            typeId = definitionName ?? string.Empty;
            subtypeId = string.Empty;
            var slash = typeId.IndexOf('/');
            if (slash < 0)
                return;

            subtypeId = slash + 1 < typeId.Length ? typeId.Substring(slash + 1) : string.Empty;
            typeId = typeId.Substring(0, slash);
        }

        static bool MatchesDefinitionId(MyCubeBlockDefinition blockDefinition, string typeId, string subtypeId)
        {
            if (blockDefinition == null || string.IsNullOrEmpty(typeId))
                return false;

            var definitionType = blockDefinition.Id.TypeId.ToString();
            var definitionSubtype = blockDefinition.Id.SubtypeName ?? string.Empty;
            if (!string.Equals(definitionSubtype, subtypeId ?? string.Empty, StringComparison.Ordinal))
                return false;

            return string.Equals(definitionType, typeId, StringComparison.Ordinal) ||
                   string.Equals(definitionType, "MyObjectBuilder_" + typeId, StringComparison.Ordinal) ||
                   definitionType.EndsWith("." + typeId, StringComparison.Ordinal);
        }

        public static string ResolveItemSprite(MyPhysicalItemDefinition definition, IMyTextSurface surface)
        {
            if (definition == null)
                return string.Empty;

            return ResolveItemSprite(definition.Id.ToString(), BuildSpriteCatalog(surface), definition);
        }

        public static string ResolveItemSpriteFromCatalog(MyPhysicalItemDefinition definition,
            ISet<string> spriteCatalog)
        {
            if (definition == null)
                return string.Empty;

            return ResolveItemSprite(definition.Id.ToString(), spriteCatalog, definition);
        }

        static string ResolveItemSprite(string itemId, IMyTextSurface surface, MyPhysicalItemDefinition definition)
        {
            return ResolveItemSprite(itemId, BuildSpriteCatalog(surface), definition);
        }

        static string ResolveItemSprite(string itemId, ISet<string> spriteCatalog,
            MyPhysicalItemDefinition definition)
        {
            string colorfulIcon;
            if (TryGetColorfulItemIconName(itemId, definition, out colorfulIcon) &&
                spriteCatalog != null && spriteCatalog.Contains(colorfulIcon))
            {
                return colorfulIcon;
            }

            if (spriteCatalog != null && spriteCatalog.Contains(itemId))
                return itemId;

            if (definition != null && definition.Icons != null && definition.Icons.Length > 0 &&
                !string.IsNullOrEmpty(definition.Icons[0]))
            {
                return definition.Icons[0];
            }

            return itemId;
        }

        static bool TryGetColorfulItemIconName(
            string itemId,
            MyPhysicalItemDefinition definition,
            out string iconName)
        {
            iconName = string.Empty;
            if (string.IsNullOrEmpty(itemId))
                return false;

            if (!IsColorfulItemCategoryEnabled(itemId, definition))
                return false;

            const string prefix = "MyObjectBuilder_";
            if (!itemId.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            iconName = "ColorfulIcons_" + itemId.Substring(prefix.Length);
            return true;
        }

        static bool IsColorfulItemCategoryEnabled(string itemId, MyPhysicalItemDefinition definition)
        {
            var client = _colorfulIcons;
            if (client == null || !client.IsReady)
                return false;

            var config = client.Config ?? client.GetConfig();
            if (config == null)
                return false;

            var typeName = GetDefinitionTypeName(itemId);
            if (string.Equals(typeName, "Ore", StringComparison.Ordinal))
                return config.Ores;
            if (string.Equals(typeName, "Ingot", StringComparison.Ordinal))
                return config.Ingots;
            if (string.Equals(typeName, "Component", StringComparison.Ordinal))
                return config.Components || config.OldComponents;
            if (string.Equals(typeName, "PhysicalGunObject", StringComparison.Ordinal) ||
                definition is MyToolItemDefinition ||
                definition is MyWeaponItemDefinition)
            {
                return config.Tools;
            }

            return config.ForceOverride;
        }

        static string GetDefinitionTypeName(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
                return string.Empty;

            var typeName = definitionId;
            var slash = typeName.IndexOf('/');
            if (slash >= 0)
                typeName = typeName.Substring(0, slash);

            const string prefix = "MyObjectBuilder_";
            return typeName.StartsWith(prefix, StringComparison.Ordinal)
                ? typeName.Substring(prefix.Length)
                : typeName;
        }

        static string MakeSafeTextureSubtype(string value)
        {
            if (string.IsNullOrEmpty(value))
                return StableFnv1A32(string.Empty).ToString("X8");

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var sanitized = sb.ToString();
            if (sanitized.Length > 72)
                sanitized = sanitized.Substring(0, 72);

            return sanitized + "_" + StableFnv1A32(value).ToString("X8");
        }

        static uint StableFnv1A32(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (value != null)
                {
                    for (var i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619;
                    }
                }

                return hash;
            }
        }

        static MyLCDTextureDefinition CreateLcdTextureDefinition(MyCubeBlockDefinition blockDefinition)
        {
            MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition),
                    blockDefinition.Id.ToString()),
                Public = false,
                LocalizationId = blockDefinition.DisplayNameString,
                SpritePath = blockDefinition.Icons.Length != 0 ? blockDefinition.Icons[0] : string.Empty,
                Selectable = false
            };

            return textureDefinition;
        }
    }
}
