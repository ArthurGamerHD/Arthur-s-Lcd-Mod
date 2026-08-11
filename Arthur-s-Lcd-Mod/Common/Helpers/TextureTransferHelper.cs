using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adk.Compression.Zip;
using Sandbox.ModAPI;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Common.Helpers
{
    public static class TextureTransferHelper
    {

        static readonly object CacheLock = new object();
        static readonly Dictionary<string, List<MinimalZip.Entry>> TextureArchiveEntryCache =
            new Dictionary<string, List<MinimalZip.Entry>>(StringComparer.OrdinalIgnoreCase);

        public static bool UseLegacyLocalTextureStorage { get; set; }

        public static string NormalizeTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;

            var normalized = Path.GetFileNameWithoutExtension(textureName.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            var invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
                normalized = normalized.Replace(invalidChars[i], '-');

            return normalized;
        }

        public static string BuildTextureKey(ulong ownerId, string textureName)
        {
            var normalized = NormalizeTextureName(textureName);
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;

            var prefix = ownerId + "-";
            return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalized
                : prefix + normalized;
        }

        public static bool TryParseTextureKey(string textureKey, out ulong ownerId, out string textureName)
        {
            ownerId = 0;
            textureName = string.Empty;

            if (string.IsNullOrWhiteSpace(textureKey))
                return false;

            var normalized = NormalizeTextureName(textureKey);
            if (string.IsNullOrEmpty(normalized))
                return false;
            
            var ownerEndIndex = normalized.IndexOf('-');
            if (ownerEndIndex <= 0 || ownerEndIndex >= normalized.Length - 1)
                return false;

            var newOwnerText = normalized.Substring(0, ownerEndIndex);
            if (!ulong.TryParse(newOwnerText, out ownerId) || ownerId == 0)
                return false;

            textureName = normalized.Substring(ownerEndIndex + 1);
            return !string.IsNullOrWhiteSpace(textureName);
        }

        public static string BuildTextureFileName(ulong ownerId, string textureName)
        {
            var key = BuildTextureKey(ownerId, textureName);
            return string.IsNullOrEmpty(key) ? string.Empty : key + ".dds";
        }

        public static string BuildPlainTextureFileName(string textureName)
        {
            var normalized = NormalizeTextureName(textureName);
            return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + ".dds";
        }

        public static string NormalizeOwnerName(string ownerName)
        {
            if (string.IsNullOrWhiteSpace(ownerName))
                return string.Empty;

            var normalized = ownerName.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
                normalized = normalized.Replace(invalidChars[i], '_');

            return normalized;
        }

        public static bool IsValidTexturePayload(byte[] data)
        {
            if (data == null || data.Length <= 0 || data.Length > MAX_TEXTURE_BYTES)
                return false;

            int width;
            int height;
            return TryGetDdsDimensions(data, out width, out height) &&
                   IsWithinMultiplayerSyncLimits(data.Length, width, height);
        }

        public static bool IsReadableDdsTexturePayload(byte[] data)
        {
            int width;
            int height;
            return data != null && data.Length > 0 && TryGetDdsDimensions(data, out width, out height);
        }

        public static bool IsWithinMultiplayerSyncLimits(long byteCount, int width, int height)
        {
            return byteCount > 0 &&
                   byteCount <= MAX_TEXTURE_BYTES &&
                   width > 0 &&
                   height > 0 &&
                   width <= MAX_SYNC_TEXTURE_DIMENSION &&
                   height <= MAX_SYNC_TEXTURE_DIMENSION;
        }

        public static string GetMultiplayerSyncWarning(string textureName, long byteCount, int width, int height)
        {
            if (IsWithinMultiplayerSyncLimits(byteCount, width, height))
                return string.Empty;

            var reasons = new List<string>();
            if (width > MAX_SYNC_TEXTURE_DIMENSION || height > MAX_SYNC_TEXTURE_DIMENSION)
                reasons.Add($"above max resolution {MAX_SYNC_TEXTURE_DIMENSION}x{MAX_SYNC_TEXTURE_DIMENSION}");
            if (byteCount > MAX_TEXTURE_BYTES)
                reasons.Add("above the size limit");

            if (reasons.Count == 0)
                return string.Empty;

            var displayName = string.IsNullOrWhiteSpace(textureName) ? "Texture" : textureName;
            return displayName + " is " + string.Join(" and ", reasons.ToArray()) +
                   ". It will not be synced in multiplayer.";
        }

        public static bool TryGetBinaryFileSize(string fileName, out long byteCount)
        {
            byteCount = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    byteCount = reader.BaseStream.Length;
                    return byteCount > 0;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                byteCount = 0;
                return false;
            }
        }

        public static bool TryReadTextureBytes(ulong ownerId, string textureName, out byte[] bytes, out string fileName)
        {
            bytes = null;
            fileName = string.Empty;
            

            var candidates = new List<string>();
            var cacheFile = BuildTextureFileName(ownerId, textureName);
            if (!string.IsNullOrEmpty(cacheFile))
                candidates.Add(cacheFile);

            var plainFile = BuildPlainTextureFileName(textureName);
            if (!string.IsNullOrEmpty(plainFile) &&
                !candidates.Any(a => string.Equals(a, plainFile, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(plainFile);

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!TryReadCachedOrLocalTextureFile(candidate, out bytes))
                    continue;

                fileName = candidate;
                return true;
            }

            return false;
        }

        public static bool TryReadTextureBytesForSync(ulong ownerId, string textureName,
            out byte[] bytes, out string fileName, out string failureReason)
        {
            bytes = null;
            fileName = string.Empty;
            failureReason = string.Empty;

            var candidates = new List<string>();
            var cacheFile = BuildTextureFileName(ownerId, textureName);
            if (!string.IsNullOrEmpty(cacheFile))
                candidates.Add(cacheFile);

            var plainFile = BuildPlainTextureFileName(textureName);
            if (!string.IsNullOrEmpty(plainFile) &&
                !candidates.Any(a => string.Equals(a, plainFile, StringComparison.OrdinalIgnoreCase)))
                candidates.Add(plainFile);

            var foundFile = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!CachedOrLocalTextureFileExists(candidate))
                    continue;

                foundFile = true;
                byte[] candidateBytes;
                if (!TryReadCachedOrLocalTextureFileRaw(candidate, out candidateBytes))
                {
                    failureReason = "failed to read " + candidate;
                    continue;
                }

                if (IsValidTexturePayload(candidateBytes))
                {
                    bytes = candidateBytes;
                    fileName = candidate;
                    return true;
                }

                int width;
                int height;
                if (TryGetDdsDimensions(candidateBytes, out width, out height))
                {
                    failureReason = GetMultiplayerSyncWarning(candidate, candidateBytes.Length, width, height);
                    if (string.IsNullOrWhiteSpace(failureReason))
                        failureReason = candidate + " is not valid for multiplayer sync.";
                }
                else
                {
                    failureReason = candidate + " is not a valid DDS texture.";
                }
            }

            if (!foundFile)
                failureReason = "no local file found for candidates: " + string.Join(", ", candidates.ToArray());

            return false;
        }

        public static bool TryReadBinaryFile(string fileName, out byte[] bytes)
        {
            if (!TryReadBinaryFileRaw(fileName, out bytes))
                return false;

            return IsValidTexturePayload(bytes);
        }

        public static bool TryReadDdsFile(string fileName, out byte[] bytes)
        {
            if (!TryReadBinaryFileRaw(fileName, out bytes))
                return false;

            return IsReadableDdsTexturePayload(bytes);
        }

        public static bool TryReadTextureFileDimensions(string fileName, out int width, out int height)
        {
            width = 0;
            height = 0;

            byte[] bytes;
            if (!TryReadCachedOrLocalTextureFileRaw(fileName, out bytes))
                return false;

            return TryGetDdsDimensions(bytes, out width, out height);
        }

        public static bool TryReadTextureFileBytes(string fileName, out byte[] bytes)
        {
            return TryReadCachedOrLocalTextureFileRaw(fileName, out bytes) &&
                   bytes != null &&
                   bytes.Length > 0;
        }

        static bool TryReadBinaryFileRaw(string fileName, out byte[] bytes)
        {
            bytes = null;

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    var length = reader.BaseStream.Length - reader.BaseStream.Position;
                    if (length <= 0 || length > int.MaxValue)
                        return false;

                    bytes = reader.ReadBytes((int)length);
                }

                return bytes != null && bytes.Length > 0;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                bytes = null;
                return false;
            }
        }

        public static bool TryGetDdsDimensions(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (bytes == null || bytes.Length < DDS_MINIMUM_HEADER_BYTES)
                return false;

            if (bytes[0] != (byte)'D' ||
                bytes[1] != (byte)'D' ||
                bytes[2] != (byte)'S' ||
                bytes[3] != (byte)' ')
                return false;

            if (BitConverter.ToUInt32(bytes, 4) != DDS_HEADER_SIZE)
                return false;

            height = (int)BitConverter.ToUInt32(bytes, 12);
            width = (int)BitConverter.ToUInt32(bytes, 16);

            return width > 0 && height > 0;
        }

        public static bool TryReadDdsDimensions(string fileName, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    var header = reader.ReadBytes(DDS_MINIMUM_HEADER_BYTES);
                    return TryGetDdsDimensions(header, out width, out height);
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static string GetTextureMetaFileName(string textureBaseName)
        {
            var normalized = NormalizeTextureName(textureBaseName);
            return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "_meta.xml";
        }

        public static bool TryWriteTextureMetadata(string textureBaseName, TextureMetadata metadata)
        {
            try
            {
                if (metadata == null)
                    return false;

                if (ShouldStoreMetadataInLocalArchive(textureBaseName, metadata))
                {
                    if (!TryWriteLocalTextureMetadata(textureBaseName, metadata))
                        return false;

                    TryDeleteLocalStorageFile(GetTextureMetaFileName(textureBaseName));
                    return true;
                }

                if (ShouldStoreMetadataInCachedArchive(textureBaseName, metadata))
                {
                    if (!TryWriteCachedTextureMetadata(textureBaseName, metadata))
                        return false;

                    TryDeleteLocalStorageFile(GetTextureMetaFileName(textureBaseName));
                    return true;
                }

                return TryWriteLegacyTextureMetadata(textureBaseName, metadata);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryWriteLegacyTextureMetadata(string textureBaseName, TextureMetadata metadata)
        {
            try
            {
                var fileName = GetTextureMetaFileName(textureBaseName);
                if (string.IsNullOrEmpty(fileName))
                    return false;

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(metadata));

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryReadTextureMetadata(string textureBaseName, out TextureMetadata metadata)
        {
            metadata = null;

            try
            {
                if (TryReadLocalTextureMetadata(textureBaseName, out metadata))
                    return true;

                ulong cachedOwnerId;
                string cachedTextureName;
                if (TryParseTextureKey(textureBaseName, out cachedOwnerId, out cachedTextureName) &&
                    TryGetCachedTextureMetadata(cachedOwnerId, cachedTextureName, out metadata))
                {
                    return true;
                }

                if (!TryReadLegacyTextureMetadata(textureBaseName, out metadata))
                    return false;

                if (ShouldStoreMetadataInLocalArchive(textureBaseName, metadata) &&
                    TryWriteLocalTextureMetadata(textureBaseName, metadata))
                {
                    TryDeleteLocalStorageFile(GetTextureMetaFileName(textureBaseName));
                }
                else if (ShouldStoreMetadataInCachedArchive(textureBaseName, metadata) &&
                         TryWriteCachedTextureMetadata(textureBaseName, metadata))
                {
                    TryDeleteLocalStorageFile(GetTextureMetaFileName(textureBaseName));
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                metadata = null;
                return false;
            }
        }

        static bool TryReadLegacyTextureMetadata(string textureBaseName, out TextureMetadata metadata)
        {
            metadata = null;

            try
            {
                var fileName = GetTextureMetaFileName(textureBaseName);
                if (string.IsNullOrEmpty(fileName) || !MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    var xml = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(xml))
                        return false;

                    metadata = MyAPIGateway.Utilities.SerializeFromXML<TextureMetadata>(xml);
                    return metadata != null;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                metadata = null;
                return false;
            }
        }

        public static bool TryRemoveTextureMetadata(string textureBaseName)
        {
            var removed = false;

            try
            {
                removed = TryRemoveLocalTextureMetadata(textureBaseName);
                removed = TryRemoveCachedTextureMetadata(textureBaseName) || removed;
                removed = TryDeleteLocalStorageFile(GetTextureMetaFileName(textureBaseName)) || removed;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
            }

            return removed;
        }

        static bool ShouldStoreMetadataInLocalArchive(string textureBaseName, TextureMetadata metadata)
        {
            if (UseLegacyLocalTextureStorage)
                return false;

            if (metadata == null)
                return false;

            if (LocalTextureMetadataExists(textureBaseName))
                return true;

            if (!string.IsNullOrWhiteSpace(metadata.SourceFileName) &&
                LocalTextureFileExists(metadata.SourceFileName))
            {
                return true;
            }

            var plainFileName = BuildPlainTextureFileName(textureBaseName);
            return !string.IsNullOrWhiteSpace(plainFileName) && LocalTextureFileExists(plainFileName);
        }

        static bool ShouldStoreMetadataInCachedArchive(string textureBaseName, TextureMetadata metadata)
        {
            ulong ownerId;
            string textureName;
            string fileName;
            if (!TryResolveCachedTextureMetadata(textureBaseName, metadata, out ownerId, out textureName, out fileName))
                return false;

            return CachedTextureFileExists(fileName);
        }

        static bool TryWriteCachedTextureMetadata(string textureBaseName, TextureMetadata metadata)
        {
            try
            {
                ulong ownerId;
                string textureName;
                string fileName;
                if (!TryResolveCachedTextureMetadata(textureBaseName, metadata, out ownerId, out textureName, out fileName))
                    return false;

                RegisterCachedTexture(ownerId, textureName, 0, fileName, metadata);
                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryResolveCachedTextureMetadata(
            string textureBaseName,
            TextureMetadata metadata,
            out ulong ownerId,
            out string textureName,
            out string fileName)
        {
            ownerId = metadata?.OwnerSteamId ?? 0;
            textureName = NormalizeTextureName(metadata?.TextureName);
            fileName = metadata == null ? string.Empty : NormalizeCachedImageEntryName(metadata.SourceFileName);

            ulong parsedOwnerId;
            string parsedTextureName;
            var registrationName = metadata == null || string.IsNullOrWhiteSpace(metadata.RegistrationName)
                ? textureBaseName
                : metadata.RegistrationName;

            if (TryParseTextureKey(registrationName, out parsedOwnerId, out parsedTextureName))
            {
                if (ownerId == 0)
                    ownerId = parsedOwnerId;
                if (string.IsNullOrEmpty(textureName))
                    textureName = parsedTextureName;
            }

            if (ownerId == 0 || string.IsNullOrEmpty(textureName))
                return false;

            var canonicalFileName = BuildTextureFileName(ownerId, textureName);
            if (string.IsNullOrEmpty(fileName) ||
                (!CachedTextureFileExists(fileName) && CachedTextureFileExists(canonicalFileName)))
            {
                fileName = canonicalFileName;
            }

            return !string.IsNullOrEmpty(fileName);
        }

        static bool TryRemoveCachedTextureMetadata(string textureBaseName)
        {
            ulong ownerId;
            string textureName;
            if (!TryParseTextureKey(textureBaseName, out ownerId, out textureName))
                return false;

            try
            {
                lock (CacheLock)
                {
                    var index = LoadCacheIndex();
                    if (index == null || index.Entries == null)
                        return false;

                    var removed = false;
                    for (var i = index.Entries.Count - 1; i >= 0; i--)
                    {
                        var entry = index.Entries[i];
                        if (entry == null ||
                            entry.OwnerId != ownerId ||
                            !string.Equals(entry.TextureName, textureName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        index.Entries.RemoveAt(i);
                        removed = true;
                    }

                    if (removed)
                        SaveCacheIndex(index);

                    return removed;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryWriteLocalTextureMetadata(string textureBaseName, TextureMetadata metadata)
        {
            try
            {
                if (metadata == null)
                    return false;

                lock (CacheLock)
                {
                    var index = LoadLocalTextureMetadataIndex();
                    UpsertLocalTextureMetadata(index, textureBaseName, metadata);
                    SaveLocalTextureMetadataIndex(index);
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryReadLocalTextureMetadata(string textureBaseName, out TextureMetadata metadata)
        {
            metadata = null;

            try
            {
                if (UseLegacyLocalTextureStorage)
                    return false;

                var key = NormalizeTextureName(textureBaseName);
                if (string.IsNullOrEmpty(key))
                    return false;

                var index = LoadLocalTextureMetadataIndex();
                if (index == null || index.Entries == null)
                    return false;

                metadata = index.Entries.FirstOrDefault(a => MetadataMatchesKey(a, key));
                return metadata != null;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                metadata = null;
                return false;
            }
        }

        static bool TryRemoveLocalTextureMetadata(string textureBaseName)
        {
            try
            {
                var key = NormalizeTextureName(textureBaseName);
                if (string.IsNullOrEmpty(key))
                    return false;

                lock (CacheLock)
                {
                    var index = LoadLocalTextureMetadataIndex();
                    if (index == null || index.Entries == null)
                        return false;

                    var removed = RemoveLocalTextureMetadata(index, key);
                    if (removed)
                        SaveLocalTextureMetadataIndex(index);

                    return removed;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool LocalTextureMetadataExists(string textureBaseName)
        {
            TextureMetadata metadata;
            return TryReadLocalTextureMetadata(textureBaseName, out metadata);
        }

        static LocalTextureMetadataIndex LoadLocalTextureMetadataIndex()
        {
            var index = ReadLocalTextureMetadataIndex();
            if (index.Entries == null)
                index.Entries = new List<TextureMetadata>();

            return index;
        }

        static LocalTextureMetadataIndex ReadLocalTextureMetadataIndex()
        {
            try
            {
                byte[] bytes;
                if (!TryReadLocalArchiveEntry(LOCAL_TEXTURES_FILE, out bytes) || bytes.Length <= 0)
                    return new LocalTextureMetadataIndex();

                var xml = System.Text.Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(xml))
                    return new LocalTextureMetadataIndex();

                return MyAPIGateway.Utilities.SerializeFromXML<LocalTextureMetadataIndex>(xml) ??
                       new LocalTextureMetadataIndex();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new LocalTextureMetadataIndex();
            }
        }

        static LocalTextureMetadataIndex ReadLocalTextureMetadataIndex(List<MinimalZip.Entry> archiveEntries)
        {
            try
            {
                if (archiveEntries == null)
                    return new LocalTextureMetadataIndex();

                for (var i = archiveEntries.Count - 1; i >= 0; i--)
                {
                    var entry = archiveEntries[i];
                    if (entry == null ||
                        !string.Equals(entry.Name, LOCAL_TEXTURES_FILE, StringComparison.OrdinalIgnoreCase) ||
                        entry.Data == null ||
                        entry.Data.Length <= 0)
                    {
                        continue;
                    }

                    var xml = System.Text.Encoding.UTF8.GetString(entry.Data);
                    if (string.IsNullOrWhiteSpace(xml))
                        return new LocalTextureMetadataIndex();

                    return MyAPIGateway.Utilities.SerializeFromXML<LocalTextureMetadataIndex>(xml) ??
                           new LocalTextureMetadataIndex();
                }

                return new LocalTextureMetadataIndex();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new LocalTextureMetadataIndex();
            }
        }

        static void SaveLocalTextureMetadataIndex(LocalTextureMetadataIndex index)
        {
            try
            {
                if (index == null)
                    return;

                if (index.Entries == null)
                    index.Entries = new List<TextureMetadata>();

                var xml = MyAPIGateway.Utilities.SerializeToXML(index);
                if (string.IsNullOrWhiteSpace(xml))
                    return;

                TryWriteLocalArchiveEntry(LOCAL_TEXTURES_FILE, System.Text.Encoding.UTF8.GetBytes(xml));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
            }
        }

        static bool TryUpsertLocalTextureMetadataIndex(
            List<MinimalZip.Entry> archiveEntries,
            LocalTextureMetadataIndex index)
        {
            try
            {
                if (archiveEntries == null || index == null)
                    return false;

                if (index.Entries == null)
                    index.Entries = new List<TextureMetadata>();

                var xml = MyAPIGateway.Utilities.SerializeToXML(index);
                if (string.IsNullOrWhiteSpace(xml))
                    return false;

                UpsertArchiveEntry(
                    archiveEntries,
                    LOCAL_TEXTURES_FILE,
                    System.Text.Encoding.UTF8.GetBytes(xml));
                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static string GetTextureMetadataKey(TextureMetadata metadata)
        {
            if (metadata == null)
                return string.Empty;

            var key = NormalizeTextureName(metadata.RegistrationName);
            if (!string.IsNullOrEmpty(key))
                return key;

            key = NormalizeTextureName(metadata.TextureName);
            if (!string.IsNullOrEmpty(key))
                return key;

            return NormalizeTextureName(metadata.SourceFileName);
        }

        static void UpsertLocalTextureMetadata(
            LocalTextureMetadataIndex index,
            string textureBaseName,
            TextureMetadata metadata)
        {
            if (index.Entries == null)
                index.Entries = new List<TextureMetadata>();

            var key = NormalizeTextureName(textureBaseName);
            if (!string.IsNullOrEmpty(key))
                RemoveLocalTextureMetadata(index, key);

            if (!string.IsNullOrWhiteSpace(metadata.RegistrationName))
                RemoveLocalTextureMetadata(index, NormalizeTextureName(metadata.RegistrationName));
            if (!string.IsNullOrWhiteSpace(metadata.TextureName))
                RemoveLocalTextureMetadata(index, NormalizeTextureName(metadata.TextureName));
            if (!string.IsNullOrWhiteSpace(metadata.SourceFileName))
                RemoveLocalTextureMetadata(index, NormalizeTextureName(metadata.SourceFileName));

            index.Entries.Add(metadata);
        }

        static bool RemoveLocalTextureMetadata(LocalTextureMetadataIndex index, string key)
        {
            if (index == null || index.Entries == null || string.IsNullOrEmpty(key))
                return false;

            var removed = false;
            for (var i = index.Entries.Count - 1; i >= 0; i--)
            {
                if (!MetadataMatchesKey(index.Entries[i], key))
                    continue;

                index.Entries.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        static bool MetadataMatchesKey(TextureMetadata metadata, string key)
        {
            if (metadata == null || string.IsNullOrEmpty(key))
                return false;

            return string.Equals(NormalizeTextureName(metadata.RegistrationName), key, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeTextureName(metadata.TextureName), key, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeTextureName(metadata.SourceFileName), key, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryWriteBinaryFile(string fileName, byte[] bytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !IsValidTexturePayload(bytes))
                    return false;

                using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    writer.Write(bytes);

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryWriteCachedTextureFile(string fileName, byte[] bytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !IsValidTexturePayload(bytes))
                    return false;

                var entryName = NormalizeCachedImageEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                lock (CacheLock)
                {
                    var entries = ReadCachedImagesArchiveEntries();
                    UpsertArchiveEntry(entries, entryName, bytes);
                    WriteCachedImagesArchiveEntries(entries);
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryWriteLocalTextureFile(string fileName, byte[] bytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !IsReadableDdsTexturePayload(bytes))
                    return false;

                if (UseLegacyLocalTextureStorage)
                    return TryWriteLooseLocalTextureFile(fileName, bytes);

                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                lock (CacheLock)
                {
                    var entries = ReadTextureArchiveEntries(LOCAL_TEXTURES);
                    UpsertArchiveEntry(entries, entryName, bytes);
                    WriteTextureArchiveEntries(LOCAL_TEXTURES, entries);
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryWriteLocalTextureFiles(IEnumerable<MinimalZip.Entry> textureEntries)
        {
            return TryWriteLocalTextureFilesWithMetadata(textureEntries, null);
        }

        public static bool TryWriteLocalTextureFilesWithMetadata(
            IEnumerable<MinimalZip.Entry> textureEntries,
            IEnumerable<TextureMetadata> metadataEntries)
        {
            try
            {
                if (textureEntries == null)
                    return false;

                var pendingEntries = new List<MinimalZip.Entry>();
                foreach (var entry in textureEntries)
                {
                    if (entry == null || !IsReadableDdsTexturePayload(entry.Data))
                        return false;

                    var entryName = NormalizeTextureArchiveEntryName(entry.Name);
                    if (string.IsNullOrEmpty(entryName))
                        return false;

                    pendingEntries.Add(new MinimalZip.Entry(entryName, entry.Data));
                }

                var pendingMetadata = new List<TextureMetadata>();
                if (metadataEntries != null)
                {
                    foreach (var metadata in metadataEntries)
                    {
                        if (metadata == null)
                            return false;

                        pendingMetadata.Add(metadata);
                    }
                }

                if (pendingEntries.Count == 0 && pendingMetadata.Count == 0)
                    return true;

                if (UseLegacyLocalTextureStorage)
                    return TryWriteLooseLocalTextureFilesWithMetadata(pendingEntries, pendingMetadata);

                lock (CacheLock)
                {
                    var entries = ReadTextureArchiveEntries(LOCAL_TEXTURES);
                    for (var i = 0; i < pendingEntries.Count; i++)
                    {
                        var entry = pendingEntries[i];
                        UpsertArchiveEntry(entries, entry.Name, entry.Data);
                    }

                    if (pendingMetadata.Count > 0)
                    {
                        var index = ReadLocalTextureMetadataIndex(entries);
                        if (index.Entries == null)
                            index.Entries = new List<TextureMetadata>();

                        for (var i = 0; i < pendingMetadata.Count; i++)
                        {
                            var metadata = pendingMetadata[i];
                            var key = GetTextureMetadataKey(metadata);
                            if (string.IsNullOrEmpty(key))
                                return false;

                            UpsertLocalTextureMetadata(index, key, metadata);
                        }

                        if (!TryUpsertLocalTextureMetadataIndex(entries, index))
                            return false;
                    }

                    WriteTextureArchiveEntries(LOCAL_TEXTURES, entries);
                }

                for (var i = 0; i < pendingMetadata.Count; i++)
                    TryDeleteLocalStorageFile(GetTextureMetaFileName(GetTextureMetadataKey(pendingMetadata[i])));

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryWriteLooseLocalTextureFilesWithMetadata(
            List<MinimalZip.Entry> textureEntries,
            List<TextureMetadata> metadataEntries)
        {
            lock (CacheLock)
            {
                for (var i = 0; i < textureEntries.Count; i++)
                {
                    var entry = textureEntries[i];
                    if (entry == null || !TryWriteLooseLocalTextureFile(entry.Name, entry.Data))
                        return false;
                }

                for (var i = 0; i < metadataEntries.Count; i++)
                {
                    var metadata = metadataEntries[i];
                    var key = GetTextureMetadataKey(metadata);
                    if (string.IsNullOrEmpty(key) || !TryWriteLegacyTextureMetadata(key, metadata))
                        return false;
                }
            }

            return true;
        }

        public static void MigrateCachedTextureStorageToZip()
        {
            lock (CacheLock)
            {
                try
                {
                    var legacyIndex = ReadLegacyCacheIndex();
                    if (legacyIndex?.Entries == null || legacyIndex.Entries.Count == 0)
                        return;

                    byte[] existingZippedIndexBytes;
                    var hasZippedIndex = TryReadCachedArchiveEntry(CACHED_TEXTURES_FILE, out existingZippedIndexBytes);
                    var mergedIndex = LoadCacheIndex();
                    if (mergedIndex.Entries == null)
                        mergedIndex.Entries = new List<CachedTextureEntry>();

                    var changed = !hasZippedIndex;
                    for (var i = 0; i < legacyIndex.Entries.Count; i++)
                    {
                        var entry = legacyIndex.Entries[i];
                        if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
                            continue;

                        var existing = mergedIndex.Entries.FirstOrDefault(a =>
                            a != null &&
                            a.OwnerId == entry.OwnerId &&
                            string.Equals(a.TextureName, entry.TextureName, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            mergedIndex.Entries.Add(entry);
                            changed = true;
                        }

                        if (CachedTextureFileExists(entry.FileName))
                            continue;

                        byte[] bytes;
                        if (!TryReadBinaryFile(entry.FileName, out bytes))
                            continue;

                        if (TryWriteCachedTextureFile(entry.FileName, bytes))
                        {
                            changed = true;
                            TryDeleteLocalStorageFile(entry.FileName);
                        }
                    }

                    if (changed)
                        SaveCacheIndex(mergedIndex);

                    byte[] zippedIndexBytes;
                    if (TryReadCachedArchiveEntry(CACHED_TEXTURES_FILE, out zippedIndexBytes) &&
                        zippedIndexBytes != null &&
                        zippedIndexBytes.Length > 0)
                    {
                        TryDeleteLocalStorageFile(CACHED_TEXTURES_FILE);

                        for (var i = 0; i < mergedIndex.Entries.Count; i++)
                        {
                            var entry = mergedIndex.Entries[i];
                            if (entry == null)
                                continue;

                            TryDeleteLocalStorageFile(GetTextureMetaFileName(entry.RegistrationName));
                            TryDeleteLocalStorageFile(
                                GetTextureMetaFileName(Path.GetFileNameWithoutExtension(entry.FileName)));
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                }
            }
        }

        public static bool TryMigrateLocalTextureFileToArchive(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                if (UseLegacyLocalTextureStorage)
                    return LooseLocalTextureFileExists(fileName);

                if (LocalTextureFileExists(fileName))
                    return true;

                byte[] bytes;
                if (!TryReadDdsFile(fileName, out bytes))
                    return false;

                if (!TryWriteLocalTextureFile(fileName, bytes))
                    return false;

                TryDeleteLocalStorageFile(fileName);
                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryMoveBinaryFile( string sourceFileName, string destinationFileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(destinationFileName))
                    return false;

                if (string.Equals(sourceFileName, destinationFileName, StringComparison.OrdinalIgnoreCase))
                    return true;

                byte[] bytes;
                if (!TryReadBinaryFile(sourceFileName, out bytes))
                    return false;

                if (!TryWriteBinaryFile(destinationFileName, bytes))
                    return false;

                MyAPIGateway.Utilities.DeleteFileInLocalStorage(sourceFileName, typeof(LcdModSessionComponent));
                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static bool TryCacheTexture( ulong ownerId, string textureName, byte[] bytes, TextureMetadata metadata = null)
        {
            var normalizedTextureName = NormalizeTextureName(textureName);
            if (string.IsNullOrWhiteSpace(normalizedTextureName))
                return false;

            ulong parsedOwnerId;
            string parsedTextureName;
            if (TryParseTextureKey(normalizedTextureName, out parsedOwnerId, out parsedTextureName) &&
                parsedOwnerId == ownerId)
            {
                normalizedTextureName = parsedTextureName;
            }

            var registrationName = BuildTextureKey(ownerId, normalizedTextureName);
            var fileName = BuildTextureFileName(ownerId, normalizedTextureName);
            if (!TryWriteCachedTextureFile(fileName, bytes))
                return false;

            int width;
            int height;
            if (TryGetDdsDimensions(bytes, out width, out height))
            {
                var cachedMetadata = metadata ?? new TextureMetadata();
                cachedMetadata.OwnerSteamId = ownerId;
                cachedMetadata.TextureName = normalizedTextureName;
                cachedMetadata.RegistrationName = registrationName;
                cachedMetadata.SourceFileName = fileName;
                cachedMetadata.OwnerName = NormalizeOwnerName(cachedMetadata.OwnerName);
                if (cachedMetadata.Width <= 0)
                    cachedMetadata.Width = width;
                if (cachedMetadata.Height <= 0)
                    cachedMetadata.Height = height;
                cachedMetadata.LastUpdatedUtcTicks = DateTime.UtcNow.Ticks;

                RegisterCachedTexture(ownerId, normalizedTextureName, bytes.Length, fileName, cachedMetadata);
                TryDeleteLocalStorageFile(GetTextureMetaFileName(registrationName));
                TryDeleteLocalStorageFile(GetTextureMetaFileName(Path.GetFileNameWithoutExtension(fileName)));
            }
            else
            {
                RegisterCachedTexture(ownerId, normalizedTextureName, bytes.Length, fileName, metadata);
            }

            return true;
        }

        public static bool TryLoadCachedTexture( ulong ownerId, string textureName, out byte[] bytes)
        {
            bytes = null;

            var normalizedTextureName = NormalizeTextureName(textureName);
            ulong parsedOwnerId;
            string parsedTextureName;
            if (TryParseTextureKey(normalizedTextureName, out parsedOwnerId, out parsedTextureName) &&
                parsedOwnerId == ownerId)
            {
                normalizedTextureName = parsedTextureName;
            }

            string fileName;
            if (!TryReadTextureBytes(ownerId, normalizedTextureName, out bytes, out fileName))
                return false;

            TextureMetadata metadata = null;
            int width;
            int height;
            if (TryGetDdsDimensions(bytes, out width, out height))
            {
                var registrationName = BuildTextureKey(ownerId, normalizedTextureName);
                metadata = new TextureMetadata
                {
                    OwnerSteamId = ownerId,
                    OwnerName = string.Empty,
                    RegistrationName = registrationName,
                    TextureName = normalizedTextureName,
                    SourceFileName = fileName,
                    Width = width,
                    Height = height,
                    LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            RegisterCachedTexture(ownerId, normalizedTextureName, bytes.Length, fileName, metadata);
            TryDeleteLocalStorageFile(GetTextureMetaFileName(BuildTextureKey(ownerId, normalizedTextureName)));
            TryDeleteLocalStorageFile(GetTextureMetaFileName(Path.GetFileNameWithoutExtension(fileName)));
            return true;
        }

        public static bool TryGetCachedTextureMetadata(ulong ownerId, string textureName, out TextureMetadata metadata)
        {
            metadata = null;

            try
            {
                var index = LoadCacheIndex();
                if (index == null || index.Entries == null)
                    return false;

                var normalizedTextureName = NormalizeTextureName(textureName);
                ulong parsedOwnerId;
                string parsedTextureName;
                if (TryParseTextureKey(normalizedTextureName, out parsedOwnerId, out parsedTextureName) &&
                    parsedOwnerId == ownerId)
                {
                    normalizedTextureName = parsedTextureName;
                }

                var entry = index.Entries.FirstOrDefault(a =>
                    a.OwnerId == ownerId &&
                    string.Equals(a.TextureName, normalizedTextureName, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return false;

                metadata = new TextureMetadata
                {
                    OwnerSteamId = entry.OwnerId,
                    OwnerName = entry.OwnerName,
                    RegistrationName = BuildTextureKey(entry.OwnerId, entry.TextureName),
                    TextureName = entry.TextureName,
                    SourceFileName = entry.FileName,
                    Width = entry.Width,
                    Height = entry.Height,
                    LastUpdatedUtcTicks = entry.LastUpdatedUtcTicks
                };

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                metadata = null;
                return false;
            }
        }

        public static List<CachedTextureEntry> GetCachedTextureEntries()
        {
            try
            {
                var index = LoadCacheIndex();
                if (index == null || index.Entries == null)
                    return new List<CachedTextureEntry>();

                return index.Entries
                    .Where(a => a != null)
                    .Select(a => new CachedTextureEntry
                    {
                        OwnerId = a.OwnerId,
                        OwnerName = a.OwnerName,
                        RegistrationName = BuildTextureKey(a.OwnerId, a.TextureName),
                        TextureName = a.TextureName,
                        FileName = a.FileName,
                        SizeBytes = a.SizeBytes,
                        Width = a.Width,
                        Height = a.Height,
                        LastUpdatedUtcTicks = a.LastUpdatedUtcTicks
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new List<CachedTextureEntry>();
            }
        }

        public static int ClearCachedTextures()
        {
            var deleted = 0;
            lock (CacheLock)
            {
                var entries = GetCachedTextureEntries();
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null)
                        continue;

                    if (MyAPIGateway.Session.Player.SteamUserId != 0 && entry.OwnerId == MyAPIGateway.Session.Player.SteamUserId)
                        continue;

                    var removedLooseFile = TryDeleteLocalStorageFile(entry.FileName);
                    var removedArchiveEntry = TryRemoveCachedTextureFile(entry.FileName);
                    if (removedLooseFile || removedArchiveEntry)
                        deleted++;

                    TryDeleteLocalStorageFile(GetTextureMetaFileName(entry.RegistrationName));
                    TryDeleteLocalStorageFile(GetTextureMetaFileName(Path.GetFileNameWithoutExtension(entry.FileName)));
                }

                var preservedEntries = MyAPIGateway.Session.Player.SteamUserId == 0
                    ? new List<CachedTextureEntry>()
                    : entries.Where(a => a != null && a.OwnerId == MyAPIGateway.Session.Player.SteamUserId).ToList();

                TryDeleteLocalStorageFile(CACHED_TEXTURES_FILE);
                if (preservedEntries.Count == 0)
                    TryDeleteLocalStorageFile(CACHED_TEXTURES);

                if (preservedEntries.Count > 0)
                {
                    var index = new CachedTextureIndex { Entries = preservedEntries };
                    SaveCacheIndex(index);
                }
            }

            return deleted;
        }

        static bool TryDeleteLocalStorageFile(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                    return false;

                MyAPIGateway.Utilities.DeleteFileInLocalStorage(fileName, typeof(LcdModSessionComponent));
                InvalidateTextureArchiveCache(fileName);
                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        public static void RegisterCachedTexture(ulong ownerId, string textureName, int byteCount, string fileName = null, TextureMetadata metadata = null)
        {
            lock (CacheLock)
            {
                var index = LoadCacheIndex();
                var normalizedTextureName = NormalizeTextureName(textureName);
                if (string.IsNullOrEmpty(normalizedTextureName))
                    return;

                ulong parsedOwnerId;
                string parsedTextureName;
                if (TryParseTextureKey(normalizedTextureName, out parsedOwnerId, out parsedTextureName) &&
                    parsedOwnerId == ownerId)
                {
                    normalizedTextureName = parsedTextureName;
                }

                fileName = string.IsNullOrEmpty(fileName) ? BuildTextureFileName(ownerId, normalizedTextureName) : fileName;
                var existing = index.Entries.FirstOrDefault(a =>
                    a.OwnerId == ownerId &&
                    string.Equals(a.TextureName, normalizedTextureName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new CachedTextureEntry();
                    index.Entries.Add(existing);
                }

                existing.OwnerId = ownerId;
                if (metadata != null)
                    existing.OwnerName = NormalizeOwnerName(metadata.OwnerName);
                else if (existing.OwnerName == null)
                    existing.OwnerName = string.Empty;

                existing.RegistrationName = metadata != null &&
                                            !string.IsNullOrWhiteSpace(metadata.RegistrationName)
                    ? NormalizeTextureName(metadata.RegistrationName)
                    : BuildTextureKey(ownerId, normalizedTextureName);
                existing.TextureName = normalizedTextureName;
                existing.FileName = fileName;

                if (byteCount > 0 || existing.SizeBytes <= 0)
                    existing.SizeBytes = byteCount;
                if (metadata != null && metadata.Width > 0)
                    existing.Width = metadata.Width;
                if (metadata != null && metadata.Height > 0)
                    existing.Height = metadata.Height;

                existing.LastUpdatedUtcTicks = metadata != null && metadata.LastUpdatedUtcTicks > 0
                    ? metadata.LastUpdatedUtcTicks
                    : DateTime.UtcNow.Ticks;

                SaveCacheIndex(index);
            }
        }

        static bool CachedOrLocalTextureFileExists(string fileName)
        {
            return LocalTextureFileExists(fileName) ||
                   CachedTextureFileExists(fileName) ||
                   MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModSessionComponent));
        }

        static bool CachedTextureFileExists(string fileName)
        {
            var entryName = NormalizeCachedImageEntryName(fileName);
            return !string.IsNullOrEmpty(entryName) &&
                   TextureArchiveEntryExists(CACHED_TEXTURES, entryName);
        }

        static bool LocalTextureFileExists(string fileName)
        {
            var entryName = NormalizeTextureArchiveEntryName(fileName);
            return !string.IsNullOrEmpty(entryName) &&
                   TextureArchiveEntryExists(LOCAL_TEXTURES, entryName);
        }

        static bool LooseLocalTextureFileExists(string fileName)
        {
            var entryName = NormalizeTextureArchiveEntryName(fileName);
            return !string.IsNullOrEmpty(entryName) &&
                   MyAPIGateway.Utilities.FileExistsInLocalStorage(entryName, typeof(LcdModSessionComponent));
        }

        public static List<string> GetLocalTextureRegistrationNames()
        {
            try
            {
                lock (CacheLock)
                {
                    var archiveEntries = ReadTextureArchiveEntries(LOCAL_TEXTURES);
                    if (archiveEntries == null || archiveEntries.Count == 0)
                        return new List<string>();

                    var metadataIndex = ReadLocalTextureMetadataIndex(archiveEntries);
                    var registrationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < archiveEntries.Count; i++)
                    {
                        var entry = archiveEntries[i];
                        if (entry == null || !IsReadableDdsTexturePayload(entry.Data))
                            continue;

                        var entryName = NormalizeTextureArchiveEntryName(entry.Name);
                        if (string.IsNullOrEmpty(entryName))
                            continue;

                        var metadata = FindLocalTextureMetadataForEntry(metadataIndex, entryName);
                        var registrationName = GetLocalTextureRegistrationName(metadata, entryName);
                        if (!string.IsNullOrEmpty(registrationName))
                            registrationNames.Add(registrationName);
                    }

                    return registrationNames.ToList();
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new List<string>();
            }
        }

        public static bool TryExtractLocalTextureArchiveToLooseFiles(out List<string> registrationNames)
        {
            registrationNames = new List<string>();

            try
            {
                lock (CacheLock)
                {
                    var archiveEntries = ReadTextureArchiveEntries(LOCAL_TEXTURES);
                    if (archiveEntries == null || archiveEntries.Count == 0)
                        return false;

                    var metadataIndex = ReadLocalTextureMetadataIndex(archiveEntries);
                    for (var i = 0; i < archiveEntries.Count; i++)
                    {
                        var entry = archiveEntries[i];
                        if (entry == null || !IsReadableDdsTexturePayload(entry.Data))
                            continue;

                        var entryName = NormalizeTextureArchiveEntryName(entry.Name);
                        if (string.IsNullOrEmpty(entryName))
                            continue;

                        if (!TryWriteLooseLocalTextureFile(entryName, entry.Data))
                            continue;

                        var metadata = FindLocalTextureMetadataForEntry(metadataIndex, entryName);
                        if (metadata != null)
                            TryWriteLegacyTextureMetadata(GetTextureMetadataKey(metadata), metadata);

                        var registrationName = GetLocalTextureRegistrationName(metadata, entryName);
                        if (!string.IsNullOrEmpty(registrationName))
                            registrationNames.Add(registrationName);
                    }
                }

                return registrationNames.Count > 0;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                registrationNames = new List<string>();
                return false;
            }
        }

        public static bool TryExtractLocalTextureFileToLooseStorage(string fileName)
        {
            try
            {
                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                if (LooseLocalTextureFileExists(entryName))
                    return true;

                byte[] bytes;
                return TryReadLocalTextureFile(entryName, out bytes) &&
                       TryWriteLooseLocalTextureFile(entryName, bytes);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static TextureMetadata FindLocalTextureMetadataForEntry(
            LocalTextureMetadataIndex metadataIndex,
            string entryName)
        {
            if (metadataIndex == null || metadataIndex.Entries == null || string.IsNullOrEmpty(entryName))
                return null;

            for (var i = 0; i < metadataIndex.Entries.Count; i++)
            {
                var metadata = metadataIndex.Entries[i];
                if (metadata == null)
                    continue;

                if (string.Equals(
                        NormalizeTextureArchiveEntryName(metadata.SourceFileName),
                        entryName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        NormalizeTextureArchiveEntryName(metadata.TextureName),
                        entryName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        NormalizeTextureArchiveEntryName(metadata.RegistrationName),
                        entryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return metadata;
                }
            }

            return null;
        }

        static string GetLocalTextureRegistrationName(TextureMetadata metadata, string entryName)
        {
            if (metadata != null)
            {
                var registrationName = NormalizeTextureName(metadata.RegistrationName);
                if (!string.IsNullOrEmpty(registrationName))
                    return registrationName;

                var textureName = NormalizeTextureName(metadata.TextureName);
                if (!string.IsNullOrEmpty(textureName))
                {
                    return metadata.OwnerSteamId == 0
                        ? textureName
                        : BuildTextureKey(metadata.OwnerSteamId, textureName);
                }
            }

            return NormalizeTextureName(entryName);
        }

        public static bool TryGetCachedTexturePath(string fileName, out string path)
        {
            path = string.Empty;

            if (!CachedTextureFileExists(fileName))
                return false;

            var entryName = NormalizeCachedImageEntryName(fileName);
            if (string.IsNullOrEmpty(entryName))
                return false;

            path = Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName,
                CACHED_TEXTURES,
                entryName);
            path = path.Replace("/", "\\");
            return true;
        }

        public static bool TryGetLocalTexturePath(string fileName, out string path)
        {
            path = string.Empty;

            if (UseLegacyLocalTextureStorage)
                return false;

            if (!LocalTextureFileExists(fileName))
                return false;

            var entryName = NormalizeTextureArchiveEntryName(fileName);
            if (string.IsNullOrEmpty(entryName))
                return false;

            path = Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName,
                LOCAL_TEXTURES,
                entryName);
            path = path.Replace("/", "\\");
            return true;
        }

        static bool TryReadCachedOrLocalTextureFile(string fileName, out byte[] bytes)
        {
            if (!TryReadCachedOrLocalTextureFileRaw(fileName, out bytes))
                return false;

            return IsValidTexturePayload(bytes);
        }

        static bool TryReadCachedOrLocalTextureFileRaw(string fileName, out byte[] bytes)
        {
            if (UseLegacyLocalTextureStorage && TryReadBinaryFileRaw(fileName, out bytes))
                return true;

            if (TryReadLocalTextureFile(fileName, out bytes))
                return true;

            if (TryReadCachedTextureFile(fileName, out bytes))
                return true;

            return TryReadBinaryFileRaw(fileName, out bytes);
        }

        static bool TryWriteLooseLocalTextureFile(string fileName, byte[] bytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !IsReadableDdsTexturePayload(bytes))
                    return false;

                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(entryName, typeof(LcdModSessionComponent)))
                    writer.Write(bytes);

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryReadCachedTextureFile(string fileName, out byte[] bytes)
        {
            bytes = null;

            try
            {
                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                lock (CacheLock)
                {
                    List<MinimalZip.Entry> entries;
                    if (!TryReadTextureArchive(CACHED_TEXTURES, out entries))
                        return false;

                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (entry == null ||
                            !string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase) ||
                            !IsValidTexturePayload(entry.Data))
                        {
                            continue;
                        }

                        bytes = entry.Data;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                bytes = null;
                return false;
            }
        }

        static bool TryReadLocalTextureFile(string fileName, out byte[] bytes)
        {
            bytes = null;

            try
            {
                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                lock (CacheLock)
                {
                    List<MinimalZip.Entry> entries;
                    if (!TryReadTextureArchive(LOCAL_TEXTURES, out entries))
                        return false;

                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (entry == null ||
                            !string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase) ||
                            !IsReadableDdsTexturePayload(entry.Data))
                        {
                            continue;
                        }

                        bytes = entry.Data;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                bytes = null;
                return false;
            }
        }

        public static bool TryRemoveLocalTextureFile(string fileName)
        {
            return TryRemoveTextureArchiveFile(LOCAL_TEXTURES, fileName);
        }

        static bool TryRemoveCachedTextureFile(string fileName)
        {
            return TryRemoveTextureArchiveFile(CACHED_TEXTURES, fileName);
        }

        static bool TryRemoveTextureArchiveFile(string archiveFileName, string fileName)
        {
            try
            {
                var entryName = NormalizeTextureArchiveEntryName(fileName);
                if (string.IsNullOrEmpty(entryName))
                    return false;

                lock (CacheLock)
                {
                    var entries = ReadTextureArchiveEntries(archiveFileName);
                    if (!RemoveArchiveEntry(entries, entryName))
                        return false;

                    WriteTextureArchiveEntries(archiveFileName, entries);
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static string NormalizeCachedImageEntryName(string fileName)
        {
            return NormalizeTextureArchiveEntryName(fileName);
        }

        static string NormalizeTextureArchiveEntryName(string fileName)
        {
            var normalized = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (!normalized.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                normalized = Path.GetFileNameWithoutExtension(normalized) + ".dds";

            return normalized.Replace('\\', '/');
        }

        static List<MinimalZip.Entry> ReadCachedImagesArchiveEntries()
        {
            return ReadTextureArchiveEntries(CACHED_TEXTURES);
        }

        static List<MinimalZip.Entry> ReadTextureArchiveEntries(string archiveFileName)
        {
            try
            {
                List<MinimalZip.Entry> cachedEntries;
                if (TryGetTextureArchiveCache(archiveFileName, out cachedEntries))
                    return cachedEntries;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                    return new List<MinimalZip.Entry>();

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return new List<MinimalZip.Entry>();

                    if (reader.BaseStream.Length - reader.BaseStream.Position <= 0)
                        return new List<MinimalZip.Entry>();

                    var entries = MinimalZip.Read(reader.BaseStream);
                    UpdateTextureArchiveCache(archiveFileName, entries);
                    return CloneArchiveEntries(entries);
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new List<MinimalZip.Entry>();
            }
        }

        static bool TryReadTextureArchive(string archiveFileName, out List<MinimalZip.Entry> entries)
        {
            entries = null;

            try
            {
                if (TryGetTextureArchiveCache(archiveFileName, out entries))
                    return true;

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    if (reader.BaseStream.Length - reader.BaseStream.Position <= 0)
                        return false;

                    entries = MinimalZip.Read(reader.BaseStream);
                    UpdateTextureArchiveCache(archiveFileName, entries);
                    entries = CloneArchiveEntries(entries);
                    return entries != null;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                entries = null;
                return false;
            }
        }

        static bool TextureArchiveEntryExists(string archiveFileName, string entryName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(archiveFileName) ||
                    string.IsNullOrWhiteSpace(entryName))
                {
                    return false;
                }

                List<MinimalZip.Entry> cachedEntries;
                if (TryGetTextureArchiveCache(archiveFileName, out cachedEntries))
                    return ArchiveEntryExists(cachedEntries, entryName);

                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                    return false;

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return false;

                    if (reader.BaseStream.Length - reader.BaseStream.Position <= 0)
                        return false;

                    return MinimalZip.ContainsEntry(reader.BaseStream, entryName, true);
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return false;
            }
        }

        static bool TryReadCachedArchiveEntry(string entryName, out byte[] bytes)
        {
            return TryReadArchiveEntry(CACHED_TEXTURES, entryName, out bytes);
        }

        static bool TryReadLocalArchiveEntry(string entryName, out byte[] bytes)
        {
            return TryReadArchiveEntry(LOCAL_TEXTURES, entryName, out bytes);
        }

        static bool TryReadArchiveEntry(string archiveFileName, string entryName, out byte[] bytes)
        {
            bytes = null;

            if (string.IsNullOrWhiteSpace(entryName))
                return false;

            List<MinimalZip.Entry> entries;
            if (!TryReadTextureArchive(archiveFileName, out entries))
                return false;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || !string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                bytes = entry.Data;
                return bytes != null;
            }

            return false;
        }

        static void TryWriteCachedArchiveEntry(string entryName, byte[] bytes)
        {
            TryWriteArchiveEntry(CACHED_TEXTURES, entryName, bytes);
        }

        static void TryWriteLocalArchiveEntry(string entryName, byte[] bytes)
        {
            TryWriteArchiveEntry(LOCAL_TEXTURES, entryName, bytes);
        }

        static void TryWriteArchiveEntry(string archiveFileName, string entryName, byte[] bytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entryName) || bytes == null) return;

                lock (CacheLock)
                {
                    var entries = ReadTextureArchiveEntries(archiveFileName);
                    UpsertArchiveEntry(entries, entryName, bytes);
                    WriteTextureArchiveEntries(archiveFileName, entries);
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
            }
        }

        static void WriteCachedImagesArchiveEntries(List<MinimalZip.Entry> entries)
        {
            WriteTextureArchiveEntries(CACHED_TEXTURES, entries);
        }

        static void WriteTextureArchiveEntries(string archiveFileName, List<MinimalZip.Entry> entries)
        {
            using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(archiveFileName, typeof(LcdModSessionComponent)))
            {
                writer.BaseStream.SetLength(0);
                writer.BaseStream.Position = 0;
                MinimalZip.Write(writer.BaseStream, entries ?? new List<MinimalZip.Entry>());
            }

            UpdateTextureArchiveCache(archiveFileName, entries);
        }

        static bool TryGetTextureArchiveCache(string archiveFileName, out List<MinimalZip.Entry> entries)
        {
            entries = null;

            if (string.IsNullOrWhiteSpace(archiveFileName))
                return false;

            List<MinimalZip.Entry> cachedEntries;
            lock (CacheLock)
            {
                if (!TextureArchiveEntryCache.TryGetValue(archiveFileName, out cachedEntries))
                    return false;
            }

            entries = CloneArchiveEntries(cachedEntries);
            return true;
        }

        static void UpdateTextureArchiveCache(string archiveFileName, List<MinimalZip.Entry> entries)
        {
            if (string.IsNullOrWhiteSpace(archiveFileName))
                return;

            lock (CacheLock)
            {
                TextureArchiveEntryCache[archiveFileName] =
                    CloneArchiveEntries(entries ?? new List<MinimalZip.Entry>());
            }
        }

        static void InvalidateTextureArchiveCache(string archiveFileName)
        {
            if (string.IsNullOrWhiteSpace(archiveFileName))
                return;

            if (string.Equals(archiveFileName, LOCAL_TEXTURES, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(archiveFileName, CACHED_TEXTURES, StringComparison.OrdinalIgnoreCase))
            {
                lock (CacheLock)
                    TextureArchiveEntryCache.Remove(archiveFileName);
            }
        }

        static List<MinimalZip.Entry> CloneArchiveEntries(List<MinimalZip.Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<MinimalZip.Entry>();

            var clone = new List<MinimalZip.Entry>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null)
                    clone.Add(entry);
            }

            return clone;
        }

        static bool ArchiveEntryExists(List<MinimalZip.Entry> entries, string entryName)
        {
            if (entries == null || string.IsNullOrWhiteSpace(entryName))
                return false;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null &&
                    string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static void UpsertArchiveEntry(List<MinimalZip.Entry> entries, string entryName, byte[] bytes)
        {
            RemoveArchiveEntry(entries, entryName);
            entries.Add(new MinimalZip.Entry(entryName, bytes));
        }

        static bool RemoveArchiveEntry(List<MinimalZip.Entry> entries, string entryName)
        {
            var removed = false;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null || !string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                entries.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        static CachedTextureIndex LoadCacheIndex()
        {
            var index = ReadCacheIndex();
            if (index.Entries == null)
                index.Entries = new List<CachedTextureEntry>();

            return index;
        }

        static CachedTextureIndex ReadCacheIndex()
        {
            try
            {
                byte[] bytes;
                if (TryReadCachedArchiveEntry(CACHED_TEXTURES_FILE, out bytes) && bytes.Length > 0)
                {
                    var xml = System.Text.Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrWhiteSpace(xml))
                        return MyAPIGateway.Utilities.SerializeFromXML<CachedTextureIndex>(xml) ??
                               new CachedTextureIndex();
                }

                return ReadLegacyCacheIndex();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new CachedTextureIndex();
            }
        }

        static CachedTextureIndex ReadLegacyCacheIndex()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(CACHED_TEXTURES_FILE, typeof(LcdModSessionComponent)))
                    return new CachedTextureIndex();

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(CACHED_TEXTURES_FILE, typeof(LcdModSessionComponent)))
                {
                    if (reader == null)
                        return new CachedTextureIndex();

                    var xml = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(xml))
                        return new CachedTextureIndex();

                    return MyAPIGateway.Utilities.SerializeFromXML<CachedTextureIndex>(xml) ?? new CachedTextureIndex();
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
                return new CachedTextureIndex();
            }
        }

        static void SaveCacheIndex(CachedTextureIndex index)
        {
            try
            {
                if (index == null)
                    return;

                var xml = MyAPIGateway.Utilities.SerializeToXML(index);
                if (string.IsNullOrWhiteSpace(xml))
                    return;

                TryWriteCachedArchiveEntry(CACHED_TEXTURES_FILE, System.Text.Encoding.UTF8.GetBytes(xml));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(TextureTransferHelper));
            }
        }

        [Serializable]
        public sealed class CachedTextureIndex
        {
            public List<CachedTextureEntry> Entries { get; set; } = new List<CachedTextureEntry>();
        }

        [Serializable]
        public sealed class CachedTextureEntry
        {
            public ulong OwnerId { get; set; }
            public string OwnerName { get; set; }
            public string RegistrationName { get; set; }
            public string TextureName { get; set; }
            public string FileName { get; set; }
            public int SizeBytes { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public long LastUpdatedUtcTicks { get; set; }
        }

        [Serializable]
        public sealed class LocalTextureMetadataIndex
        {
            public List<TextureMetadata> Entries { get; set; } = new List<TextureMetadata>();
        }

        [Serializable]
        [ProtoBuf.ProtoContract]
        public sealed class TextureMetadata
        {
            [ProtoBuf.ProtoMember(1)] public ulong OwnerSteamId { get; set; }
            [ProtoBuf.ProtoMember(2)] public string OwnerName { get; set; }
            [ProtoBuf.ProtoMember(3)] public string RegistrationName { get; set; }
            [ProtoBuf.ProtoMember(4)] public string TextureName { get; set; }
            [ProtoBuf.ProtoMember(5)] public string SourceFileName { get; set; }
            [ProtoBuf.ProtoMember(6)] public int Width { get; set; }
            [ProtoBuf.ProtoMember(7)] public int Height { get; set; }
            [ProtoBuf.ProtoMember(8)] public long LastUpdatedUtcTicks { get; set; }
        }
    }
}
