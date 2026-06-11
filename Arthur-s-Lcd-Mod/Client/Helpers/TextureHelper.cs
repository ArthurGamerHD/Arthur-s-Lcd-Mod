using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

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

            var spriteNames = new List<string>();
            if (surface != null)
                surface.GetSprites(spriteNames);

            var itemId = definition.Id.ToString();
            var colorfulIcon = GetColorfulItemIconName(itemId);
            if (!string.IsNullOrEmpty(colorfulIcon) && spriteNames.Contains(colorfulIcon))
                return colorfulIcon;

            if (spriteNames.Contains(itemId))
                return itemId;

            if (definition.Icons != null && definition.Icons.Length > 0 && !string.IsNullOrEmpty(definition.Icons[0]))
                return definition.Icons[0];

            return itemId;
        }

        static string GetColorfulItemIconName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return string.Empty;

            const string prefix = "MyObjectBuilder_";
            if (!itemId.StartsWith(prefix, StringComparison.Ordinal))
                return string.Empty;

            return "ColorfulIcons_" + itemId.Substring(prefix.Length);
        }

        static string MakeSafeTextureSubtype(string value)
        {
            if (string.IsNullOrEmpty(value))
                return StableFnv1a32(string.Empty).ToString("X8");

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var sanitized = sb.ToString();
            if (sanitized.Length > 72)
                sanitized = sanitized.Substring(0, 72);

            return sanitized + "_" + StableFnv1a32(value).ToString("X8");
        }

        static uint StableFnv1a32(string value)
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
                if (LocalConfigManager.Config.LocalTextures.Remove(file))
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Texture {file} will not be loaded next time the game is restarted");
                else
                {
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Texture {file} not loaded");
                    return;
                }

                MyAPIGateway.Utilities.DeleteFileInLocalStorage(file, typeof(LcdModSessionComponent));
                if (!string.IsNullOrEmpty(baseName))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(baseName + "_meta.xml",
                        typeof(LcdModSessionComponent));
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
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

                var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("import.txt", typeof(LcdModClientComponent));

                string file;
                while ((file = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    try
                    {
                        ImportTexture(file, verbose: true);
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, typeof(TextureHelper));
                    }
                }

                reader.Close();

                MyAPIGateway.Utilities.DeleteFileInLocalStorage("import.txt", typeof(LcdModClientComponent));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureHelper));
            }
        }

        public static void LocalTexture(string id, bool verbose = false, bool persistAsLocal = true)
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

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModClientComponent)))
            {
                var localSourceFileName = id + ".dds";
                if (!string.Equals(sourceFileName, localSourceFileName, StringComparison.OrdinalIgnoreCase) &&
                    MyAPIGateway.Utilities.FileExistsInLocalStorage(localSourceFileName, typeof(LcdModClientComponent)))
                {
                    sourceFileName = localSourceFileName;
                }
            }

            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModClientComponent)))
            {
                var path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName,
                    sourceFileName);
                path = path.Replace("/", "\\");

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

                metadata.OwnerSteamId = ownerId;
                metadata.RegistrationName = registrationName;
                metadata.TextureName = textureName;
                metadata.SourceFileName = sourceFileName;
                TextureTransferHelper.TryWriteTextureMetadata(registrationName, metadata);

                if (LocalConfigManager.Config != null && LocalConfigManager.Config.LocalTextures == null)
                    LocalConfigManager.Config.LocalTextures = new HashSet<string>();

                if (persistAsLocal && LocalConfigManager.Config != null)
                {
                    if (!string.Equals(id, registrationName, StringComparison.OrdinalIgnoreCase))
                        LocalConfigManager.Config.LocalTextures.Remove(id);

                    if (LocalConfigManager.Config.LocalTextures.Add(registrationName) ||
                        !string.Equals(id, registrationName, StringComparison.OrdinalIgnoreCase))
                    {
                        LocalConfigManager.Save();
                    }
                }
            }
            else if (verbose)
            {
                MyAPIGateway.Utilities.ShowNotification($"File {sourceFileName} does not exists in mod storage");
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
            var sourceFile = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(sourceFile))
                return;

            if (!sourceFile.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                sourceFile = Path.GetFileNameWithoutExtension(sourceFile) + ".dds";

            var localUserId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            int width;
            int height;
            if (!TextureTransferHelper.TryReadDdsDimensions(sourceFile, out width,
                    out height))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification($"Invalid DDS header in {sourceFile}");
                return;
            }

            if (!AreDdsDimensionsBlockAligned(width, height))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Refusing {sourceFile}: DDS width and height must be divisible by 4");
                return;
            }

            long byteCount;
            if (verbose &&
                TextureTransferHelper.TryGetBinaryFileSize(sourceFile, out byteCount))
            {
                var syncWarning = TextureTransferHelper.GetMultiplayerSyncWarning(sourceFile, byteCount, width, height);
                if (!string.IsNullOrWhiteSpace(syncWarning))
                    MyAPIGateway.Utilities.ShowNotification(syncWarning, 8000);
            }

            var baseName = TextureTransferHelper.NormalizeTextureName(sourceFile);
            if (string.IsNullOrWhiteSpace(baseName))
                return;

            var registrationName = localUserId != 0
                ? TextureTransferHelper.BuildTextureKey(localUserId, baseName)
                : baseName;
            var metadata = new TextureTransferHelper.TextureMetadata
            {
                OwnerSteamId = localUserId,
                RegistrationName = registrationName,
                TextureName = baseName,
                SourceFileName = sourceFile,
                Width = width,
                Height = height,
                LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
            };

            if (!TextureTransferHelper.TryWriteTextureMetadata(registrationName, metadata))
            {
                if (verbose)
                    MyAPIGateway.Utilities.ShowNotification($"Failed to write metadata for {sourceFile}");
                return;
            }

            LocalTexture(registrationName, verbose);
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

            if (!TextureTransferHelper.TryWriteBinaryFile(fileName, data))
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

            if (LocalConfigManager.Config != null && LocalConfigManager.Config.LocalTextures != null)
            {
                foreach (var localTexture in LocalConfigManager.Config.LocalTextures)
                    LocalTexture(localTexture);
            }

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


- Manual import of multiples textures:

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


- Automatically import multiples texture:

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
