using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Common.Zip;
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
        static readonly HashSet<string> CustomTextures = new HashSet<string>();
        static readonly HashSet<string> LocalCustomTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> PendingTextureRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> RegisteredGeneratedTextures =
            new Dictionary<string, string>(StringComparer.Ordinal);

        static readonly HashSet<string> FailedTextureParseRequests =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static readonly object PendingTextureLock = new object();
        static readonly object FailedTextureParseLock = new object();

        static readonly HashSet<string> DeferredLocalTextureRegistrations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const long COLORFUL_ICONS_REPLY_CHANNEL = (Constants.WORKSHOP_ID << 8) | 1L;
        static ColorfulIconsApiClient _colorfulIcons;

        sealed class TextureImportCandidate
        {
            public string SourceFile;
            public string ArchiveFile;
            public string RegistrationName;
            public byte[] TextureBytes;
            public TextureTransferHelper.TextureMetadata Metadata;
        }

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
                if (ItemsSurfaceScriptBase.SpriteCache != null)
                    ItemsSurfaceScriptBase.SpriteCache.Clear();
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

            var subtypeName = "NpcMarket_" + MakeSafeTextureSubtype(registrationKey);
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
            LocalCustomTextures.Add(subtypeName);
            return subtypeName;
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

            foreach (var texture in LocalCustomTextures)
            {
                if (texture == null)
                    continue;

                if (!string.IsNullOrEmpty(texture))
                    spriteNames.Add(texture);
            }
        }

        public static bool IsKnownTexture(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            return CustomTextures.Contains(textureName);
        }

        public static bool CanRenderTexture(string textureName)
        {
            if (LocalConfigManager.RenderOtherUserTextures)
                return true;

            ulong ownerId;
            string parsedTextureName;
            return !TextureTransferHelper.TryParseTextureKey(textureName, out ownerId, out parsedTextureName) ||
                   !IsTextureOwnedByOtherUser(ownerId);
        }

        public static bool IsTextureOwnedByOtherUser(string textureName)
        {
            ulong ownerId;
            string parsedTextureName;
            return TextureTransferHelper.TryParseTextureKey(textureName, out ownerId, out parsedTextureName) &&
                   IsTextureOwnedByOtherUser(ownerId);
        }

        static bool IsTextureOwnedByOtherUser(ulong ownerId)
        {
            if (ownerId == 0)
                return false;

            var localOwnerId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            return ownerId != localOwnerId;
        }

        public static bool TryQueueTextureRequest(ulong ownerId, string textureName, string registrationName = null)
        {
            var localOwnerId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            if (localOwnerId == 0 || ownerId == 0 || ownerId == localOwnerId)
                return false;

            if (!LocalConfigManager.RenderOtherUserTextures)
                return false;

            registrationName = string.IsNullOrWhiteSpace(registrationName)
                ? TextureTransferHelper.BuildTextureKey(ownerId, textureName)
                : registrationName;
            if (string.IsNullOrWhiteSpace(registrationName) || IsKnownTexture(registrationName))
                return false;

            if (TryRegisterCachedTexture(ownerId, textureName, false))
                return false;

            lock (PendingTextureLock)
            {
                if (!PendingTextureRequests.Add(registrationName))
                    return false;
            }

            LogHelper.LogInfo(
                $"Requesting texture {registrationName} from owner {ownerId} for requester {localOwnerId}");

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                if (MyAPIGateway.Session?.Player == null)
                    return;

                if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                {
                    LcdModSessionComponent.Server.HandleLocalRequestTexture(
                        new PacketRequestTexture(ownerId, textureName, localOwnerId));
                    return;
                }

                LcdModSessionComponent.NetworkManager.TransmitToServer(
                    new PacketRequestTexture(ownerId, textureName, localOwnerId), false);
            });

            return true;
        }

        public static void MarkTextureParseFailed(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            lock (FailedTextureParseLock)
                FailedTextureParseRequests.Add(textureName);
        }

        public static bool HasTextureParseFailed(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            lock (FailedTextureParseLock)
                return FailedTextureParseRequests.Contains(textureName);
        }

        public static void ClearPendingTextureRequest(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            lock (PendingTextureLock)
                PendingTextureRequests.Remove(textureName);
        }

        public static bool HasPendingTextureRequest(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            lock (PendingTextureLock)
                return PendingTextureRequests.Contains(textureName);
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

        public static void ImportLocalTexture(string[] obj)
        {
            if (obj.Length == 1)
            {
                var file = obj[0];
                ImportTexture(file, true);
            }
#if DEBUG
            else if (obj.Length == 2)
            {
                var id = obj[1];
                var file = obj[0];
                ForceLoadTexture(file, id);
            }
#endif
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
            }
        }

#if DEBUG
        static void ForceLoadTexture(string path, string name)
        {
            // fun fact, you can load
            // C:\Windows\Web\Wallpaper\Windows\img0.jpg
            // or
            // C:\Users\<username>\AppData\Local\Google\Chrome\User Data\Profile 1\Google Profile Picture.png
            // here, dangerous... (but fortunately only client side)

            if (path.Length <= 2 || path[1] != ':')
            {
                path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName, path);
            }

            path = path.Replace("/", "\\");

            MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition),
                    name),
                Public = true,
                LocalizationId = name,
                SpritePath = path,
                TexturePath = path,
                Selectable = true,
                AvailableInSurvival = true
            };

            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(textureDefinition);

            MyAPIGateway.Utilities.ShowNotification($"Force loaded texture {name} at {path}");
        }
#endif

        public static void RemoveLocalTexture(string[] obj)
        {
            if (obj.Length == 1)
            {
                var file = obj[0];
                var baseName = Path.GetFileNameWithoutExtension(file);
                var sourceFileName = file;
                TextureTransferHelper.TextureMetadata metadata;
                if (TextureTransferHelper.TryReadTextureMetadata(baseName, out metadata) &&
                    metadata != null &&
                    !string.IsNullOrWhiteSpace(metadata.SourceFileName))
                {
                    sourceFileName = metadata.SourceFileName;
                }

                var removed = false;
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModSessionComponent)))
                {
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(sourceFileName, typeof(LcdModSessionComponent));
                    removed = true;
                }

                removed = TextureTransferHelper.TryRemoveLocalTextureFile(sourceFileName) || removed;
                if (!removed)
                {
                    MyAPIGateway.Utilities.ShowNotification($"Texture {file} not loaded");
                    return;
                }

                if (!string.IsNullOrEmpty(baseName))
                    TextureTransferHelper.TryRemoveTextureMetadata(baseName);

                MyAPIGateway.Utilities.ShowNotification(
                    $"Texture {file} will not be loaded next time the game is restarted");
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
            }
        }

        public static void MigrateLegacyLocalTexture(string id)
        {
            id = TextureTransferHelper.NormalizeTextureName(id);
            if (string.IsNullOrWhiteSpace(id))
                return;

            TextureTransferHelper.TextureMetadata metadata;
            var hasMetadata = TextureTransferHelper.TryReadTextureMetadata(id, out metadata) && metadata != null;
            var sourceFileName = Path.GetFileName(hasMetadata && !string.IsNullOrWhiteSpace(metadata.SourceFileName)
                ? metadata.SourceFileName
                : TextureTransferHelper.BuildPlainTextureFileName(id));

            if (string.IsNullOrWhiteSpace(sourceFileName))
                return;

            var migrated = TextureTransferHelper.TryMigrateLocalTextureFileToArchive(sourceFileName);
            if (!migrated)
            {
                var legacySourceFileName = id + ".dds";
                if (!string.Equals(sourceFileName, legacySourceFileName, StringComparison.OrdinalIgnoreCase))
                {
                    sourceFileName = legacySourceFileName;
                    migrated = TextureTransferHelper.TryMigrateLocalTextureFileToArchive(sourceFileName);
                }
            }

            if (!migrated || !hasMetadata)
                return;

            metadata.SourceFileName = sourceFileName;
            TextureTransferHelper.TryWriteTextureMetadata(id, metadata);
        }

        public static void LoadLocalTextures()
        {
            var localTextures = TextureTransferHelper.GetLocalTextureRegistrationNames();
            for (var i = 0; i < localTextures.Count; i++)
            {
                var registrationName = localTextures[i];
                if (string.IsNullOrWhiteSpace(registrationName) || IsKnownTexture(registrationName))
                    continue;

                LocalTexture(registrationName, false, true, false);
            }
        }

        public static void LoadLegacyLocalTextures()
        {
            if (LocalConfigManager.Config == null || LocalConfigManager.Config.LocalTextures == null)
                return;

            foreach (var localTexture in LocalConfigManager.Config.LocalTextures)
                LocalTexture(localTexture);
        }

        public static void EnableLegacyLocalTextureStorage()
        {
            if (LocalConfigManager.Config == null)
                return;

            if (LocalConfigManager.Config.LocalTextures == null)
                LocalConfigManager.Config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> archiveRegistrationNames;
            if (TextureTransferHelper.TryExtractLocalTextureArchiveToLooseFiles(out archiveRegistrationNames))
            {
                for (var i = 0; i < archiveRegistrationNames.Count; i++)
                {
                    var registrationName = archiveRegistrationNames[i];
                    if (!string.IsNullOrWhiteSpace(registrationName))
                        LocalConfigManager.Config.LocalTextures.Add(registrationName);
                }
            }
        }

        public static void Import(bool verbose = false)
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("import.txt", typeof(LcdModClientComponent)))
                {
                    if (verbose)
                        MyAPIGateway.Utilities.ShowNotification("import.txt does not exists in mod storage");
                    return;
                }

                var imports = new List<TextureImportCandidate>();

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("import.txt", typeof(LcdModClientComponent)))
                {
                    string file;
                    while ((file = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(file))
                            continue;

                        try
                        {
                            TextureImportCandidate import;
                            if (TryPrepareTextureImport(file, true, out import))
                                imports.Add(import);
                        }
                        catch (Exception e)
                        {
                            ErrorHandlerHelper.LogError(e, typeof(TextureHelper));
                        }
                    }
                }

                ImportTextures(imports, true);

                MyAPIGateway.Utilities.DeleteFileInLocalStorage("import.txt", typeof(LcdModClientComponent));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureHelper));
            }
        }

        static string GetLocalStoragePath()
        {
            var userDataPath = MyAPIGateway.Utilities.GamePaths.UserDataPath;
            return Path.Combine(
                    userDataPath,
                    "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName)
                .Replace('\\', '/');
        }

        public static void LocalTexture(
            string id,
            bool verbose = false,
            bool persistAsLocal = true,
            bool writeMetadata = true)
        {
            id = TextureTransferHelper.NormalizeTextureName(id);
            if (string.IsNullOrWhiteSpace(id))
                return;

            var localUserId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            ulong inputOwnerId;
            string inputTextureName;
            var inputIsTextureKey =
                TextureTransferHelper.TryParseTextureKey(id, out inputOwnerId, out inputTextureName);

            if (persistAsLocal && localUserId == 0 && !inputIsTextureKey)
            {
                DeferLocalTextureRegistration(id, verbose);
                return;
            }

            TextureTransferHelper.TextureMetadata metadata;
            if (!TextureTransferHelper.TryReadTextureMetadata(id, out metadata))
            {
                var metadataOwnerId = inputIsTextureKey ? inputOwnerId : localUserId;
                var metadataTextureName = inputIsTextureKey ? inputTextureName : id;
                metadata = new TextureTransferHelper.TextureMetadata
                {
                    OwnerSteamId = metadataOwnerId,
                    RegistrationName = metadataOwnerId != 0
                        ? TextureTransferHelper.BuildTextureKey(metadataOwnerId, metadataTextureName)
                        : metadataTextureName,
                    TextureName = metadataTextureName,
                    SourceFileName = TextureTransferHelper.BuildPlainTextureFileName(id),
                    LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            var ownerId = metadata.OwnerSteamId != 0
                ? metadata.OwnerSteamId
                : inputIsTextureKey
                    ? inputOwnerId
                    : localUserId;
            var textureName = TextureTransferHelper.NormalizeTextureName(metadata.TextureName);


            string ownerName = "local";
            ulong parsedOwnerId;
            string parsedTextureName;
            if (TextureTransferHelper.TryParseTextureKey(textureName, out parsedOwnerId, out parsedTextureName))
            {
                ownerId = parsedOwnerId;
                ownerName = MyAPIGateway.Players.TryGetIdentityId(MyAPIGateway.Players.TryGetIdentityId(parsedOwnerId))
                    ?.Character?.DisplayName ?? "Unknown";
                textureName = parsedTextureName;
            }
            else if (string.IsNullOrWhiteSpace(textureName))
            {
                if (inputIsTextureKey)
                {
                    ownerId = inputOwnerId;
                    textureName = inputTextureName;
                }
                else
                {
                    textureName = id;
                }
            }

            var registrationName = ownerId != 0
                ? TextureTransferHelper.BuildTextureKey(ownerId, textureName)
                : textureName;

            if (persistAsLocal && ownerId == 0)
            {
                DeferLocalTextureRegistration(id, verbose);
                return;
            }

            if (persistAsLocal && localUserId != 0 && ownerId != 0 && ownerId != localUserId)
            {
                LogHelper.Log(MyLogSeverity.Warning,
                    $"Skipping local texture registration for {registrationName}; owner {ownerId} is not local player {localUserId}");
                return;
            }

            var sourceFileName = Path.GetFileName(string.IsNullOrWhiteSpace(metadata.SourceFileName)
                ? TextureTransferHelper.BuildPlainTextureFileName(textureName)
                : metadata.SourceFileName);

            if (string.IsNullOrWhiteSpace(sourceFileName))
                sourceFileName = TextureTransferHelper.BuildPlainTextureFileName(textureName);

            if (persistAsLocal &&
                localUserId != 0 &&
                ownerId == localUserId &&
                !TextureTransferHelper.UseLegacyLocalTextureStorage)
            {
                TextureTransferHelper.TryMigrateLocalTextureFileToArchive(sourceFileName);
            }

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModClientComponent)))
            {
                var localSourceFileName = id + ".dds";
                if (!string.Equals(sourceFileName, localSourceFileName, StringComparison.OrdinalIgnoreCase) &&
                    MyAPIGateway.Utilities.FileExistsInLocalStorage(localSourceFileName, typeof(LcdModClientComponent)))
                {
                    sourceFileName = localSourceFileName;
                    if (persistAsLocal &&
                        localUserId != 0 &&
                        ownerId == localUserId &&
                        !TextureTransferHelper.UseLegacyLocalTextureStorage)
                    {
                        TextureTransferHelper.TryMigrateLocalTextureFileToArchive(sourceFileName);
                    }
                }
            }

            string path = string.Empty;
            if (TextureTransferHelper.UseLegacyLocalTextureStorage &&
                MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModClientComponent)))
            {
                path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName,
                    sourceFileName);
                path = path.Replace("/", "\\");
            }
            else
            {
                if (TextureTransferHelper.UseLegacyLocalTextureStorage)
                    TextureTransferHelper.TryExtractLocalTextureFileToLooseStorage(sourceFileName);

                // ZIP storage is canonical after a texture has been migrated. Prefer the archive
                // entry even when a stale loose file is still visible in local storage.
                var foundTexturePath =
                    !TextureTransferHelper.UseLegacyLocalTextureStorage &&
                    TextureTransferHelper.TryGetLocalTexturePath(sourceFileName, out path);

                if (!foundTexturePath)
                    foundTexturePath = TextureTransferHelper.TryGetCachedTexturePath(sourceFileName, out path);

                if (!foundTexturePath)
                {
                    if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModClientComponent)))
                    {
                        if (verbose)
                            MyAPIGateway.Utilities.ShowNotification($"File {sourceFileName} does not exists in mod storage");
                        return;
                    }

                    path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage",
                        MyAPIGateway.Utilities.GamePaths.ModScopeName,
                        sourceFileName);
                    path = path.Replace("/", "\\");
                }
            }

            metadata.OwnerSteamId = ownerId;
            metadata.RegistrationName = registrationName;
            metadata.TextureName = textureName;
            metadata.SourceFileName = sourceFileName;
            if (metadata.Width <= 0 || metadata.Height <= 0)
            {
                int width;
                int height;
                if (TextureTransferHelper.TryReadTextureFileDimensions(sourceFileName, out width, out height))
                {
                    metadata.Width = width;
                    metadata.Height = height;
                }
            }

            // In ZIP mode, finish any archive rewrite before exposing the definition to the
            // renderer; otherwise its asynchronous DDS load can race the ZIP being rebuilt.
            if (writeMetadata)
                TextureTransferHelper.TryWriteTextureMetadata(registrationName, metadata);

            MyLCDTextureDefinition textureDefinition = new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition),
                    registrationName),
                Public = true,
                LocalizationId = ownerName + "_" + textureName,
                SpritePath = path,
                TexturePath = path,
                Selectable = true,
                AvailableInSurvival = true
            };


            LogHelper.Log(MyLogSeverity.Info, $"Registered texture {registrationName} at path {path}");

            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(textureDefinition);

            var addedCustomTexture = CustomTextures.Add(registrationName);
            if (persistAsLocal)
                LocalCustomTextures.Add(registrationName);

            if (verbose)
                MyAPIGateway.Utilities.ShowNotification(addedCustomTexture
                    ? $"Definition created for texture {registrationName}"
                    : $"Existing texture found for id {registrationName}, you may need to restart your game to see the effects");

            if (TextureTransferHelper.UseLegacyLocalTextureStorage && persistAsLocal && LocalConfigManager.Config != null)
            {
                if (LocalConfigManager.Config.LocalTextures == null)
                    LocalConfigManager.Config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.Equals(id, registrationName, StringComparison.OrdinalIgnoreCase))
                    LocalConfigManager.Config.LocalTextures.Remove(id);

                if (LocalConfigManager.Config.LocalTextures.Add(registrationName) ||
                    !string.Equals(id, registrationName, StringComparison.OrdinalIgnoreCase))
                {
                    LocalConfigManager.Save();
                }
            }
        }

        static void DeferLocalTextureRegistration(string id, bool verbose)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (DeferredLocalTextureRegistrations)
            {
                if (!DeferredLocalTextureRegistrations.Add(id))
                    return;
            }

            LogHelper.Log(MyLogSeverity.Warning,
                $"Delaying local texture registration for {id}; local player Steam ID is not available yet");

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                lock (DeferredLocalTextureRegistrations)
                    DeferredLocalTextureRegistrations.Remove(id);

                LocalTexture(id, verbose);
            });
        }

        public static void ImportTexture(string file, bool verbose = false, string id = null)
        {
            TextureImportCandidate import;
            if (!TryPrepareTextureImport(file, verbose, out import))
                return;

            if (!TextureTransferHelper.TryWriteLocalTextureFilesWithMetadata(
                    new[] { new MinimalZip.Entry(import.ArchiveFile, import.TextureBytes) },
                    new[] { import.Metadata }))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification($"Failed to save texture {import.ArchiveFile} to local texture storage");
                return;
            }

            CompleteTextureImport(import, verbose);
        }

        static void ImportTextures(List<TextureImportCandidate> imports, bool verbose)
        {
            if (imports == null || imports.Count == 0)
                return;

            var archiveEntries = new List<MinimalZip.Entry>(imports.Count);
            var metadataEntries = new List<TextureTransferHelper.TextureMetadata>(imports.Count);
            for (var i = 0; i < imports.Count; i++)
            {
                var import = imports[i];
                if (import == null)
                    continue;

                archiveEntries.Add(new MinimalZip.Entry(import.ArchiveFile, import.TextureBytes));
                metadataEntries.Add(import.Metadata);
            }

            if (archiveEntries.Count == 0)
                return;

            if (!TextureTransferHelper.TryWriteLocalTextureFilesWithMetadata(
                    archiveEntries,
                    metadataEntries))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification("Failed to save imported textures to local texture storage");
                return;
            }

            for (var i = 0; i < imports.Count; i++)
            {
                var import = imports[i];
                if (import == null)
                    continue;

                CompleteTextureImport(import, verbose);
            }
        }

        static bool TryPrepareTextureImport(
            string file,
            bool verbose,
            out TextureImportCandidate import)
        {
            import = null;

            var sourceFile = NormalizeDdsImportFileName(file);
            if (string.IsNullOrWhiteSpace(sourceFile))
                return false;

            var archiveFile = Path.GetFileName(sourceFile);
            if (string.IsNullOrWhiteSpace(archiveFile))
                return false;

            var localUserId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            byte[] textureBytes;
            int width;
            int height;
            if (!TryReadDdsImportFile(sourceFile, out textureBytes) ||
                !TextureTransferHelper.TryGetDdsDimensions(textureBytes, out width, out height))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification($"Invalid DDS header in {sourceFile}");
                return false;
            }

            if (!AreDdsDimensionsBlockAligned(width, height))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Refusing {sourceFile}: DDS width and height must be divisible by 4");
                return false;
            }

            if (verbose)
            {
                var syncWarning =
                    TextureTransferHelper.GetMultiplayerSyncWarning(sourceFile, textureBytes.Length, width, height);
                if (!string.IsNullOrWhiteSpace(syncWarning))
                    MyAPIGateway.Utilities.ShowNotification(syncWarning, 8000);
            }

            var baseName = TextureTransferHelper.NormalizeTextureName(archiveFile);
            if (string.IsNullOrWhiteSpace(baseName))
                return false;

            var registrationName = localUserId != 0
                ? TextureTransferHelper.BuildTextureKey(localUserId, baseName)
                : baseName;
            var metadata = new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = localUserId,
                RegistrationName = registrationName,
                TextureName = baseName,
                SourceFileName = archiveFile,
                Width = width,
                Height = height,
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
            };

            import = new TextureImportCandidate
            {
                SourceFile = sourceFile,
                ArchiveFile = archiveFile,
                RegistrationName = registrationName,
                TextureBytes = textureBytes,
                Metadata = metadata
            };

            return true;
        }

        static void CompleteTextureImport(TextureImportCandidate import, bool verbose)
        {
            if (import == null)
                return;

            if ((!TextureTransferHelper.UseLegacyLocalTextureStorage ||
                 !string.Equals(import.SourceFile, import.ArchiveFile, StringComparison.OrdinalIgnoreCase)) &&
                MyAPIGateway.Utilities.FileExistsInLocalStorage(import.SourceFile, typeof(LcdModClientComponent)))
            {
                MyAPIGateway.Utilities.DeleteFileInLocalStorage(import.SourceFile, typeof(LcdModClientComponent));
            }

            LocalTexture(import.RegistrationName, verbose, true, false);
        }

        static bool TryReadDdsImportFile(string sourceFile, out byte[] textureBytes)
        {
            textureBytes = null;

            if (TextureTransferHelper.TryReadDdsFile(sourceFile, out textureBytes))
                return true;

            if (!TryReadLocalStorageSubfolderFile(sourceFile, out textureBytes))
                return false;

            return TextureTransferHelper.IsReadableDdsTexturePayload(textureBytes);
        }

        
        /// <summary>
        /// Hack: this should NOT be possible under the real mod api...
        /// but it is, until keen fixes it (or add a proper way to do),
        /// this method is the only way to read sub-folders
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <param name="bytes"></param>
        /// <returns></returns>
        static bool TryReadLocalStorageSubfolderFile(string sourceFile, out byte[] bytes)
        {
            bytes = null;

            try
            {
                if (string.IsNullOrWhiteSpace(sourceFile))
                    return false;

                var storagePath = GetLocalStoragePath();
                if (string.IsNullOrWhiteSpace(storagePath))
                    return false;

                var relativePath = sourceFile.Trim().Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relativePath) ||
                    Path.IsPathRooted(relativePath) ||
                    HasParentDirectorySegment(relativePath))
                {
                    return false;
                }

                var storageMod = new MyObjectBuilder_Checkpoint.ModItem(storagePath, 0, null);
                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(relativePath, storageMod))
                {
                    if (reader == null)
                        return false;

                    var length = reader.BaseStream.Length - reader.BaseStream.Position;
                    if (length <= 0 || length > int.MaxValue)
                        return false;

                    bytes = reader.ReadBytes((int)length);
                    return bytes != null && bytes.Length > 0;
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning,
                    $"Failed to read texture import file {sourceFile} through mod-location storage fallback: {e.Message}");
                bytes = null;
                return false;
            }
        }

        static bool HasParentDirectorySegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return string.Equals(path, "..", StringComparison.Ordinal) ||
                   path.StartsWith("../", StringComparison.Ordinal) ||
                   path.EndsWith("/..", StringComparison.Ordinal) ||
                   path.IndexOf("/../", StringComparison.Ordinal) >= 0;
        }

        static string NormalizeDdsImportFileName(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return string.Empty;

            var normalized = file.Trim().Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            if (fileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                return normalized;

            var directory = Path.GetDirectoryName(normalized);
            var ddsFileName = Path.GetFileNameWithoutExtension(fileName) + ".dds";
            return string.IsNullOrWhiteSpace(directory)
                ? ddsFileName
                : Path.Combine(directory, ddsFileName).Replace('\\', '/');
        }

        static bool AreDdsDimensionsBlockAligned(int width, int height)
        {
            return width > 0 &&
                   height > 0 &&
                   width % 4 == 0 &&
                   height % 4 == 0;
        }

        public static void SaveRemoteTexture(PacketSyncTexture packet, bool verbose = false)
        {
            if (packet == null)
                return;

            SaveRemoteTexture(packet.Metadata ?? new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = packet.OwnerSteamId,
                TextureName = packet.TextureName
            }, packet.Data, verbose);
        }

        public static void SaveRemoteTexture(TextureTransferHelper.TextureMetadata metadata, byte[] data,
            bool verbose = false)
        {
            if (!TextureTransferHelper.IsValidTexturePayload(data))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification("Invalid texture payload");
                return;
            }

            if (metadata == null)
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification("Invalid texture metadata");
                return;
            }

            var textureName = TextureTransferHelper.NormalizeTextureName(metadata.TextureName);
            if (string.IsNullOrWhiteSpace(textureName))
                textureName = TextureTransferHelper.NormalizeTextureName(metadata.RegistrationName);
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            var ownerId = metadata.OwnerSteamId;
            ulong parsedOwnerId;
            string parsedTextureName;
            if (TextureTransferHelper.TryParseTextureKey(textureName, out parsedOwnerId, out parsedTextureName))
            {
                ownerId = parsedOwnerId;
                textureName = parsedTextureName;
            }

            var registrationName = ownerId != 0
                ? TextureTransferHelper.BuildTextureKey(ownerId, textureName)
                : textureName;
            metadata.TextureName = textureName;
            metadata.RegistrationName = registrationName;
            metadata.OwnerName = TextureTransferHelper.NormalizeOwnerName(metadata.OwnerName);

            var fileName = ownerId != 0
                ? TextureTransferHelper.BuildTextureFileName(ownerId, textureName)
                : TextureTransferHelper.BuildPlainTextureFileName(textureName);
            if (string.IsNullOrEmpty(fileName))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification("Invalid texture name");
                return;
            }

            if (!TextureTransferHelper.TryWriteCachedTextureFile(fileName, data))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification($"Failed to save texture {fileName}");
                return;
            }

            metadata.SourceFileName = fileName;

            if (metadata.Width <= 0 || metadata.Height <= 0)
            {
                int width;
                int height;
                if (TextureTransferHelper.TryGetDdsDimensions(data, out width, out height))
                {
                    metadata.Width = width;
                    metadata.Height = height;
                }
            }

            TextureTransferHelper.TryWriteTextureMetadata(registrationName, metadata);

            var localId = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(localId))
                localId = fileName;

            TextureTransferHelper.RegisterCachedTexture(
                ownerId,
                textureName,
                data.Length,
                fileName,
                metadata);

            LocalTexture(localId, verbose, false);

            ClearPendingTextureRequest(registrationName);
        }

        public static void LoadCachedTextures()
        {
            TextureTransferHelper.MigrateCachedTextureStorageToZip();

            var localUserId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            var entries = TextureTransferHelper.GetCachedTextureEntries();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
                    continue;

                var localId = Path.GetFileNameWithoutExtension(entry.FileName);
                if (string.IsNullOrWhiteSpace(localId))
                    continue;

                TextureTransferHelper.TextureMetadata metadata;
                if (!TextureTransferHelper.TryReadTextureMetadata(localId, out metadata))
                {
                    metadata = new TextureTransferHelper.TextureMetadata
                    {
                        OwnerSteamId = entry.OwnerId,
                        OwnerName = entry.OwnerName,
                        RegistrationName = entry.RegistrationName,
                        TextureName = entry.TextureName,
                        SourceFileName = Path.GetFileName(entry.FileName),
                        Width = entry.Width,
                        Height = entry.Height,
                        LastUpdatedUtcTicks = entry.LastUpdatedUtcTicks
                    };
                    TextureTransferHelper.TryWriteTextureMetadata(localId, metadata);
                }
                else if (!string.Equals(Path.GetFileName(metadata.SourceFileName), Path.GetFileName(entry.FileName),
                             StringComparison.OrdinalIgnoreCase))
                {
                    metadata.SourceFileName = Path.GetFileName(entry.FileName);
                    TextureTransferHelper.TryWriteTextureMetadata(localId, metadata);
                }

                if (localUserId != 0 && entry.OwnerId == localUserId)
                    LocalTexture(localId);
            }
        }

        static bool TryRegisterCachedTexture(ulong ownerId, string textureName, bool verbose)
        {
            var normalizedTextureName = TextureTransferHelper.NormalizeTextureName(textureName);
            if (ownerId == 0 || string.IsNullOrWhiteSpace(normalizedTextureName))
                return false;

            var entries = TextureTransferHelper.GetCachedTextureEntries();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null ||
                    entry.OwnerId != ownerId ||
                    !string.Equals(entry.TextureName, normalizedTextureName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var localId = Path.GetFileNameWithoutExtension(entry.FileName);
                if (string.IsNullOrWhiteSpace(localId))
                    return false;

                LocalTexture(localId, verbose, false);
                return IsKnownTexture(TextureTransferHelper.BuildTextureKey(ownerId, normalizedTextureName));
            }

            byte[] bytes;
            if (!TextureTransferHelper.TryLoadCachedTexture(ownerId, normalizedTextureName,
                    out bytes))
                return false;

            LocalTexture(TextureTransferHelper.BuildTextureKey(ownerId, normalizedTextureName), verbose, false);
            return IsKnownTexture(TextureTransferHelper.BuildTextureKey(ownerId, normalizedTextureName));
        }

        public static void ClearCacheCommand(string[] args)
        {
            var deletedFiles = TextureTransferHelper.ClearCachedTextures();

            ClearRuntimeTextureState();

            LoadLocalTextures();

            LogHelper.LogInfo($"Cleared remote texture cache ({deletedFiles} files) and pending texture requests");
            MyAPIGateway.Utilities.ShowMessage("lcdMod", "Remote texture cache cleared. Local images were kept.");
        }

        public static void UnloadTextureCache()
        {
            var deletedFiles = TextureTransferHelper.ClearCachedTextures();
            ClearRuntimeTextureState();
            LogHelper.LogInfo($"Cleared remote texture cache on unload ({deletedFiles} files)");
        }

        static void ClearRuntimeTextureState()
        {
            lock (PendingTextureLock)
                PendingTextureRequests.Clear();
            lock (FailedTextureParseLock)
                FailedTextureParseRequests.Clear();
            lock (DeferredLocalTextureRegistrations)
                DeferredLocalTextureRegistrations.Clear();

            CustomTextures.Clear();
            LocalCustomTextures.Clear();
        }

        public static void ExportConverter()
        {
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("png-to-dds.sh", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("png-to-dds.sh",
                    typeof(LcdModClientComponent));
                file.WriteLine(@"#!/usr/bin/env bash
set -euo pipefail

if [ ! -f tools_path.txt ]; then
  echo ""tools_path.txt not found"" >&2
  exit 1
fi

path=""$(awk 'NF{print; exit}' tools_path.txt)""
# Remove surrounding quotes and trailing slash if present
path=""${path%\""}""
path=""${path#\""}""
# Ensure no trailing slash required for wine invocation (wine accepts either)
# Run with wine
wine ""${path}\texconv.exe"" ./*.png -nologo -y -f BC7_UNORM -pmalpha 2

for file in ./*.DDS; do
    [ -e ""$file"" ] || continue

    destination=""${file%.DDS}.dds""
    temporary=""${file}.rename-tmp-$$""

    mv -- ""$file"" ""$temporary"" &&
        mv -- ""$temporary"" ""$destination"" &&
        printf '%s\n' ""${destination#./}"" >> import.txt
done
");
                file.Flush();
                file.Close();
            }

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("png-to-dds.bat", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("png-to-dds.bat",
                    typeof(LcdModClientComponent));
                file.WriteLine($@"@echo off
setlocal enabledelayedexpansion

rem Read first non-empty line from tools_path.txt into PATHVAR
rem %%~A removes surrounding quotes automatically
set ""PATHVAR=""
for /f ""usebackq delims="" %%A in (""tools_path.txt"") do (
    if not defined PATHVAR set ""PATHVAR=%%~A""
)

if not defined PATHVAR (
    echo tools_path.txt is empty or missing.
    exit /b 1
)

rem Ensure path ends with a backslash
if not ""%PATHVAR:~-1%""==""\"" set ""PATHVAR=%PATHVAR%\""

rem Run texconv.exe on all PNGs in the current directory
""%PATHVAR%texconv.exe"" .\*.png -nologo -y -f BC7_UNORM -pmalpha
set ""TEXCONV_ERROR=%ERRORLEVEL%""

if not ""%TEXCONV_ERROR%""==""0"" (
    echo texconv.exe failed with error code %TEXCONV_ERROR%.
    exit /b %TEXCONV_ERROR%
)

rem Rename generated .DDS files to .dds and write their names to import.txt
> import.txt (
    for %%F in (*.DDS) do (
        if exist ""%%F"" (
            ren ""%%F"" ""%%~nF.__rename_tmp__""
            ren ""%%~nF.__rename_tmp__"" ""%%~nF.dds""
            echo %%~nF.dds
        )
    )
)

exit /b 0");
                file.Flush();
                file.Close();
            }

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage("tools_path.txt", typeof(LcdModClientComponent)))
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage("tools_path.txt",
                    typeof(LcdModClientComponent));

                var content = MyAPIGateway.Utilities.GamePaths.ContentPath;

                content = content.Replace("Content", "Tools\\TexturePacking\\Tools");

                if (content.StartsWith("z", StringComparison.InvariantCultureIgnoreCase))
                    content = content.Substring(2).Replace("\\", "/");

                file.WriteLine(content);
                file.Flush();
                file.Close();
            }

            {
                var toolsPath = MyAPIGateway.Utilities.GamePaths.ContentPath;
                toolsPath = toolsPath.Replace("Content", "Tools\\TexturePacking\\Tools");

                var configFolderPath = Path.Combine(
                    MyAPIGateway.Utilities.GamePaths.UserDataPath,
                    "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName);

                // Convert Wine/Proton Z: paths into readable Linux paths when applicable.
                if (toolsPath.StartsWith("z:", StringComparison.InvariantCultureIgnoreCase))
                    toolsPath = toolsPath.Substring(2).Replace("\\", "/");

                if (configFolderPath.StartsWith("z:", StringComparison.InvariantCultureIgnoreCase))
                    configFolderPath = configFolderPath.Substring(2).Replace("\\", "/");

                var texconvExePath = toolsPath.TrimEnd('\\', '/') +
                                     (toolsPath.IndexOf('/') >= 0
                                         ? "/texconv.exe"
                                         : "\\texconv.exe");

                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                    "readme-for-texture-import.txt",
                    typeof(LcdModClientComponent));

                file.Write(
                    $@"Arthur's Lcd Mod Custom Texture Import
============================

Before starting, a few notes:

- The image **MUST** be divisible by 4 on both it's height and width (requirement of texconv).
- Mods can only see files inside the Storage folder (the folder you found this file), while we technically can display 
  images outside this folder, it can only be synced in multiplayer or imported automatically while here.
    (in your system, we think the storage folder is located at: {configFolderPath})
- Microsoft's texconv.exe is shipped as a ""modding tool"" alongside with Space Engineers and is located at /SpaceEngineers/Tools/TexturePacking/Tools/
    (in your system, we think texconv is located at: {texconvExePath})
- Tested conversion parameters are '-nologo -y -f BC7_UNORM -pmalpha', but you can try other parameters if you want, as long as it is a valid .dds image
- The mod will refuse to import texture filenames not ending with .dds or not having a valid dds header
- Use simple texture filenames without special characters when possible to avoid issues
- By default imported local DDS files are moved into {Constants.LOCAL_TEXTURES}; on Linux/Proton, run /lcdmod legacylocaltexturestorage true to keep loose DDS files in this Storage folder
- When replacing an existing texture, you need to restart the game
- Images with dimension greater than {Constants.MAX_SYNC_TEXTURE_DIMENSION}x{Constants.MAX_SYNC_TEXTURE_DIMENSION} or size greater than {Constants.MAX_TEXTURE_BYTES/1000}kb
will NOT be synced in multiplayer

============================

How to import custom textures:

- Manual import of single texture:

1. Convert your image into a .dds texture by running:
   ""{texconvExePath}"" -nologo -y -f BC7_UNORM -pmalpha ""path\to\image.png""

2. Copy the generated dds file into the mod ""Storage"" folder:
   {configFolderPath}

3. In game, run:
   /lcdmod importlocaltexture image.dds


- Manual import of multiple textures:

1. Copy your .dds textures into Storage folder as you would for manual import:
   {configFolderPath}

2. Create a file named:
   import.txt

3. Add one dds texture name per line. Each filename must end with .dds. Example:

   logo.dds
   advertisements.dds
   coolwallpaper.dds

4. Save import.txt in:
   {configFolderPath}\import.txt

5. In game, either restart the current session (exit to menu, then load) or run:
   /lcdmod importtextures

- Automatically import multiple textures:

1. Copy all png images into this config folder:
   {configFolderPath}

1b. Ensure tools_path.txt points to the correct folder containing texconv.exe

2. Run png-to-dds.bat (or png-to-dds.sh if you are on linux - tools_path.txt will mostly likely be wrong but you can figure it out 🙃)

2b. Ensure all png got converted and the import.txt was created with the correct .dds files

3. You can delete the png files now if you want (is not required anymore)

4. In game, either restart the current session (exit to menu, then load) or run:
   /lcdmod importtextures
");

                file.Flush();
                file.Close();
            }
        }
    }
}
