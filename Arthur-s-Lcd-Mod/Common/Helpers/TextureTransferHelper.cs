using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Common.Helpers
{
    public static class TextureTransferHelper
    {

        static readonly object CacheLock = new object();

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
                if (!TryReadBinaryFile(candidate, out bytes))
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

            if (typeof(LcdModSessionComponent) == null)
            {
                failureReason = "storage owner is null";
                return false;
            }

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
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(candidate, typeof(LcdModSessionComponent)))
                    continue;

                foundFile = true;
                byte[] candidateBytes;
                if (!TryReadBinaryFileRaw(candidate, out candidateBytes))
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
                if (typeof(LcdModSessionComponent) == null || string.IsNullOrWhiteSpace(fileName))
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

        public static bool TryMoveBinaryFile( string sourceFileName, string destinationFileName)
        {
            try
            {
                if (typeof(LcdModSessionComponent) == null || string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(destinationFileName))
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
            if (!TryWriteBinaryFile(fileName, bytes))
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

                TryWriteTextureMetadata(registrationName, cachedMetadata);
                RegisterCachedTexture(ownerId, normalizedTextureName, bytes.Length, fileName, cachedMetadata);
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

            int width;
            int height;
            if (TryGetDdsDimensions(bytes, out width, out height))
            {
                var registrationName = BuildTextureKey(ownerId, normalizedTextureName);
                TryWriteTextureMetadata(registrationName, new TextureMetadata
                {
                    OwnerSteamId = ownerId,
                    OwnerName = string.Empty,
                    RegistrationName = registrationName,
                    TextureName = normalizedTextureName,
                    SourceFileName = fileName,
                    Width = width,
                    Height = height,
                    LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
                });
            }

            RegisterCachedTexture(ownerId, normalizedTextureName, bytes.Length, fileName);
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

                    if (TryDeleteLocalStorageFile(entry.FileName))
                        deleted++;

                    TryDeleteLocalStorageFile(GetTextureMetaFileName(entry.RegistrationName));
                    TryDeleteLocalStorageFile(GetTextureMetaFileName(Path.GetFileNameWithoutExtension(entry.FileName)));
                }

                TryDeleteLocalStorageFile(CACHED_TEXTURES_FILE);

                var preservedEntries = MyAPIGateway.Session.Player.SteamUserId == 0
                    ? new List<CachedTextureEntry>()
                    : entries.Where(a => a != null && a.OwnerId == MyAPIGateway.Session.Player.SteamUserId).ToList();
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
                existing.OwnerName = metadata != null ? NormalizeOwnerName(metadata.OwnerName) : string.Empty;
                existing.RegistrationName = BuildTextureKey(ownerId, normalizedTextureName);
                existing.TextureName = normalizedTextureName;
                existing.FileName = fileName;
                existing.SizeBytes = byteCount;
                existing.Width = metadata?.Width ?? 0;
                existing.Height = metadata?.Height ?? 0;
                existing.LastUpdatedUtcTicks = DateTime.UtcNow.Ticks;

                SaveCacheIndex(index);
            }
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

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(CACHED_TEXTURES_FILE, typeof(LcdModSessionComponent)))
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(index));
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
