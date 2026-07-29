using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using LcdMod.Common.Zip;
using Sandbox.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal static class AudioLibraryStorage
    {
        public const string ARCHIVE_FILE_NAME = "local_audio.zip";
        public const string METADATA_ENTRY_NAME = "audio.xml";

        const string SOURCE_FOLDER = "source";
        const string RUNTIME_FOLDER = "runtime";

        static readonly object ArchiveLock = new object();
        static List<MinimalZip.Entry> _cachedEntries;
        static readonly Dictionary<string, byte[]> RuntimeCache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static AudioLibraryMetadata LoadMetadata()
        {
            try
            {
                byte[] bytes;
                if (!TryReadArchiveEntry(METADATA_ENTRY_NAME, out bytes) || bytes == null || bytes.Length == 0)
                    return new AudioLibraryMetadata();

                var xml = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(xml))
                    return new AudioLibraryMetadata();

                var metadata = MyAPIGateway.Utilities.SerializeFromXML<AudioLibraryMetadata>(xml) ?? new AudioLibraryMetadata();
                if (metadata.Assets == null)
                    metadata.Assets = new List<AudioAssetMetadata>();
                return metadata;
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read local audio metadata: " + error.Message);
                return new AudioLibraryMetadata();
            }
        }

        public static bool TrySaveImportedAsset(
            AudioAssetMetadata asset,
            byte[] sourceBytes,
            byte[] runtimeWaveBytes,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (asset == null)
            {
                failureReason = "Missing audio metadata.";
                return false;
            }

            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                failureReason = "Missing source WAV bytes.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(asset.SourceArchivePath))
                asset.SourceArchivePath = BuildSourceEntryPath(asset.Id, asset.SourcePath, asset.SourceSha256);
            if (string.IsNullOrWhiteSpace(asset.RuntimePath))
                asset.RuntimePath = BuildRuntimeEntryPath(asset.Id, asset.RuntimeSha256);

            if (string.IsNullOrWhiteSpace(asset.SourceArchivePath) ||
                string.IsNullOrWhiteSpace(asset.RuntimePath))
            {
                failureReason = "Could not resolve archive paths for imported audio.";
                return false;
            }

            try
            {
                lock (ArchiveLock)
                {
                    var entries = ReadArchiveEntries();
                    UpsertArchiveEntry(entries, asset.SourceArchivePath, sourceBytes);
                    RemoveArchiveEntry(entries, asset.RuntimePath);

                    var metadata = ReadMetadataFromEntries(entries);
                    if (metadata.Assets == null)
                        metadata.Assets = new List<AudioAssetMetadata>();

                    RemoveAsset(metadata, asset.Id);
                    metadata.Version = Math.Max(metadata.Version, 2);
                    metadata.Assets.Add(asset);
                    UpsertArchiveEntry(entries, METADATA_ENTRY_NAME, Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(metadata)));

                    WriteArchiveEntries(entries);
                    CacheRuntimeWaveLocked(asset, runtimeWaveBytes);
                }

                return true;
            }
            catch (Exception error)
            {
                failureReason = "Could not save imported audio archive: " + error.Message;
                LogHelper.Log(MyLogSeverity.Warning, failureReason);
                return false;
            }
        }

        public static bool TryReadRuntimeWave(AudioAssetMetadata asset, out byte[] runtimeWaveBytes, out string failureReason)
        {
            runtimeWaveBytes = null;
            failureReason = string.Empty;

            if (asset == null || string.IsNullOrWhiteSpace(asset.RuntimePath))
            {
                failureReason = "Invalid local audio metadata.";
                return false;
            }

            lock (ArchiveLock)
            {
                if (TryGetCachedRuntimeWaveLocked(asset, out runtimeWaveBytes))
                    return true;
            }

            byte[] sourceBytes;
            if (string.IsNullOrWhiteSpace(asset.SourceArchivePath) ||
                !TryReadArchiveEntry(asset.SourceArchivePath, out sourceBytes) ||
                sourceBytes == null || sourceBytes.Length == 0)
            {
                failureReason = "Local audio source entry not found: " + asset.SourceArchivePath;
                return false;
            }

            var work = new AudioImportWork
            {
                AssetId = asset.Id,
                OwnerSteamId = asset.OwnerSteamId,
                SourcePath = asset.SourcePath,
                SourceBytes = sourceBytes,
                SourceSha256 = asset.SourceSha256
            };

            AudioImportProcessor.ProcessImport(work);
            if (work.Error != null)
            {
                failureReason = "Could not rebuild runtime WAV: " + work.Error.Message;
                return false;
            }

            if (work.RuntimeWaveBytes == null || work.RuntimeWaveBytes.Length == 0)
            {
                failureReason = "Rebuilt runtime WAV is empty.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(asset.RuntimeSha256) &&
                !string.Equals(asset.RuntimeSha256, work.RuntimeSha256, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Rebuilt runtime WAV hash does not match metadata.";
                return false;
            }

            CacheRuntimeWave(asset, work.RuntimeWaveBytes);
            runtimeWaveBytes = CloneBytes(work.RuntimeWaveBytes);
            return true;
        }

        public static bool RuntimeWaveExists(AudioAssetMetadata asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.RuntimePath))
                return false;

            lock (ArchiveLock)
            {
                byte[] cached;
                if (TryGetCachedRuntimeWaveLocked(asset, out cached))
                    return true;
            }

            byte[] bytes;
            return !string.IsNullOrWhiteSpace(asset.SourceArchivePath) &&
                   TryReadArchiveEntry(asset.SourceArchivePath, out bytes) &&
                   bytes != null &&
                   bytes.Length > 0;
        }

        public static bool TryDeleteAsset(AudioAssetMetadata asset, out string failureReason)
        {
            failureReason = string.Empty;

            if (asset == null)
            {
                failureReason = "Missing audio metadata.";
                return false;
            }

            if (MyAPIGateway.Utilities == null)
            {
                failureReason = "Local storage is not available.";
                return false;
            }

            try
            {
                lock (ArchiveLock)
                {
                    var entries = ReadArchiveEntries();
                    var metadata = ReadMetadataFromEntries(entries);
                    if (metadata.Assets == null)
                        metadata.Assets = new List<AudioAssetMetadata>();

                    var removedAssets = new List<AudioAssetMetadata>();
                    for (var i = metadata.Assets.Count - 1; i >= 0; i--)
                    {
                        var existing = metadata.Assets[i];
                        if (!AudioAssetMatches(existing, asset))
                            continue;

                        removedAssets.Add(existing);
                        metadata.Assets.RemoveAt(i);
                    }

                    var removedArchiveEntries = RemoveAssetArchiveEntries(entries, asset);
                    for (var i = 0; i < removedAssets.Count; i++)
                    {
                        removedArchiveEntries = RemoveAssetArchiveEntries(entries, removedAssets[i]) || removedArchiveEntries;
                        RemoveRuntimeCacheLocked(removedAssets[i]);
                    }

                    RemoveRuntimeCacheLocked(asset);

                    if (removedAssets.Count == 0 && !removedArchiveEntries)
                    {
                        failureReason = "Local audio asset was not found.";
                        return false;
                    }

                    metadata.Version = Math.Max(metadata.Version, 2);
                    UpsertArchiveEntry(entries, METADATA_ENTRY_NAME, Encoding.UTF8.GetBytes(MyAPIGateway.Utilities.SerializeToXML(metadata)));
                    WriteArchiveEntries(entries);
                }

                return true;
            }
            catch (Exception error)
            {
                failureReason = "Could not delete local audio: " + error.Message;
                LogHelper.Log(MyLogSeverity.Warning, failureReason);
                return false;
            }
        }

        public static string BuildSourceEntryPath(string assetId, string sourcePath, string sourceSha256)
        {
            var baseName = NormalizeAssetId(assetId);
            if (string.IsNullOrEmpty(baseName))
                baseName = NormalizeAssetId(Path.GetFileNameWithoutExtension(sourcePath));
            if (string.IsNullOrEmpty(baseName))
                baseName = "audio";

            var hash = NormalizeHashPrefix(sourceSha256);
            return SOURCE_FOLDER + "/" + baseName + (string.IsNullOrEmpty(hash) ? string.Empty : "_" + hash) + ".wav";
        }

        public static string BuildRuntimeEntryPath(string assetId, string runtimeSha256)
        {
            var baseName = NormalizeAssetId(assetId);
            if (string.IsNullOrEmpty(baseName))
                baseName = "audio";

            var hash = NormalizeHashPrefix(runtimeSha256);
            return RUNTIME_FOLDER + "/" + baseName + (string.IsNullOrEmpty(hash) ? string.Empty : "_" + hash) + "_runtime.wav";
        }

        static AudioLibraryMetadata ReadMetadataFromEntries(List<MinimalZip.Entry> entries)
        {
            try
            {
                byte[] bytes;
                if (!TryFindEntryData(entries, METADATA_ENTRY_NAME, out bytes) || bytes == null || bytes.Length == 0)
                    return new AudioLibraryMetadata();

                var xml = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(xml))
                    return new AudioLibraryMetadata();

                var metadata = MyAPIGateway.Utilities.SerializeFromXML<AudioLibraryMetadata>(xml) ?? new AudioLibraryMetadata();
                if (metadata.Assets == null)
                    metadata.Assets = new List<AudioAssetMetadata>();
                return metadata;
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read local audio metadata index: " + error.Message);
                return new AudioLibraryMetadata();
            }
        }

        static void RemoveAsset(AudioLibraryMetadata metadata, string assetId)
        {
            if (metadata == null || metadata.Assets == null || string.IsNullOrWhiteSpace(assetId))
                return;

            for (var i = metadata.Assets.Count - 1; i >= 0; i--)
            {
                var asset = metadata.Assets[i];
                if (asset != null && string.Equals(asset.Id, assetId, StringComparison.OrdinalIgnoreCase))
                    metadata.Assets.RemoveAt(i);
            }
        }

        static bool AudioAssetMatches(AudioAssetMetadata left, AudioAssetMetadata right)
        {
            if (left == null || right == null)
                return false;

            if (!string.IsNullOrWhiteSpace(left.Id) &&
                !string.IsNullOrWhiteSpace(right.Id) &&
                string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase))
                return true;

            if (SameArchivePath(left.SourceArchivePath, right.SourceArchivePath))
                return true;

            if (SameArchivePath(left.RuntimePath, right.RuntimePath))
                return true;

            return false;
        }

        static bool SameArchivePath(string left, string right)
        {
            left = NormalizeArchiveEntryName(left);
            right = NormalizeArchiveEntryName(right);
            return !string.IsNullOrEmpty(left) &&
                   !string.IsNullOrEmpty(right) &&
                   string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        static bool RemoveAssetArchiveEntries(List<MinimalZip.Entry> entries, AudioAssetMetadata asset)
        {
            if (asset == null)
                return false;

            var removed = RemoveArchiveEntry(entries, asset.SourceArchivePath);
            removed = RemoveArchiveEntry(entries, asset.RuntimePath) || removed;
            return removed;
        }

        static bool TryReadArchiveEntry(string entryName, out byte[] bytes)
        {
            bytes = null;

            entryName = NormalizeArchiveEntryName(entryName);
            if (string.IsNullOrEmpty(entryName))
                return false;

            lock (ArchiveLock)
            {
                var entries = ReadArchiveEntries();
                return TryFindEntryData(entries, entryName, out bytes);
            }
        }

        static bool TryFindEntryData(List<MinimalZip.Entry> entries, string entryName, out byte[] bytes)
        {
            bytes = null;
            if (entries == null || string.IsNullOrEmpty(entryName))
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

        static List<MinimalZip.Entry> ReadArchiveEntries()
        {
            if (_cachedEntries != null)
                return CloneEntries(_cachedEntries);

            try
            {
                if (MyAPIGateway.Utilities == null ||
                    !MyAPIGateway.Utilities.FileExistsInLocalStorage(ARCHIVE_FILE_NAME, typeof(LcdModClientComponent)))
                {
                    _cachedEntries = new List<MinimalZip.Entry>();
                    return new List<MinimalZip.Entry>();
                }

                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(ARCHIVE_FILE_NAME, typeof(LcdModClientComponent)))
                {
                    if (reader == null || reader.BaseStream.Length - reader.BaseStream.Position <= 0)
                    {
                        _cachedEntries = new List<MinimalZip.Entry>();
                        return new List<MinimalZip.Entry>();
                    }

                    var entries = MinimalZip.Read(reader.BaseStream);
                    _cachedEntries = CloneEntries(entries);
                    return CloneEntries(entries);
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read local audio archive: " + error.Message);
                _cachedEntries = new List<MinimalZip.Entry>();
                return new List<MinimalZip.Entry>();
            }
        }

        static void WriteArchiveEntries(List<MinimalZip.Entry> entries)
        {
            using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(ARCHIVE_FILE_NAME, typeof(LcdModClientComponent)))
            {
                writer.BaseStream.SetLength(0);
                writer.BaseStream.Position = 0;
                MinimalZip.Write(writer.BaseStream, entries ?? new List<MinimalZip.Entry>());
            }

            _cachedEntries = CloneEntries(entries ?? new List<MinimalZip.Entry>());
        }

        static void UpsertArchiveEntry(List<MinimalZip.Entry> entries, string entryName, byte[] bytes)
        {
            entryName = NormalizeArchiveEntryName(entryName);
            if (entries == null || string.IsNullOrEmpty(entryName) || bytes == null)
                return;

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                    entries.RemoveAt(i);
            }

            entries.Add(new MinimalZip.Entry(entryName, bytes));
        }

        static bool RemoveArchiveEntry(List<MinimalZip.Entry> entries, string entryName)
        {
            entryName = NormalizeArchiveEntryName(entryName);
            if (entries == null || string.IsNullOrEmpty(entryName))
                return false;

            var removed = false;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                {
                    entries.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        static void CacheRuntimeWave(AudioAssetMetadata asset, byte[] runtimeWaveBytes)
        {
            lock (ArchiveLock)
                CacheRuntimeWaveLocked(asset, runtimeWaveBytes);
        }

        static void CacheRuntimeWaveLocked(AudioAssetMetadata asset, byte[] runtimeWaveBytes)
        {
            var key = NormalizeArchiveEntryName(asset == null ? null : asset.RuntimePath);
            if (string.IsNullOrEmpty(key) || runtimeWaveBytes == null || runtimeWaveBytes.Length == 0)
                return;

            RuntimeCache[key] = CloneBytes(runtimeWaveBytes);
        }

        static bool TryGetCachedRuntimeWaveLocked(AudioAssetMetadata asset, out byte[] runtimeWaveBytes)
        {
            runtimeWaveBytes = null;
            var key = NormalizeArchiveEntryName(asset == null ? null : asset.RuntimePath);
            if (string.IsNullOrEmpty(key))
                return false;

            byte[] cached;
            if (!RuntimeCache.TryGetValue(key, out cached) || cached == null || cached.Length == 0)
                return false;

            runtimeWaveBytes = CloneBytes(cached);
            return true;
        }

        static void RemoveRuntimeCacheLocked(AudioAssetMetadata asset)
        {
            var key = NormalizeArchiveEntryName(asset == null ? null : asset.RuntimePath);
            if (!string.IsNullOrEmpty(key))
                RuntimeCache.Remove(key);
        }

        static List<MinimalZip.Entry> CloneEntries(List<MinimalZip.Entry> entries)
        {
            var clone = new List<MinimalZip.Entry>();
            if (entries == null)
                return clone;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;

                var data = entry.Data == null ? Array.Empty<byte>() : (byte[])entry.Data.Clone();
                clone.Add(new MinimalZip.Entry(entry.Name, data, entry.CreationTime));
            }

            return clone;
        }

        static byte[] CloneBytes(byte[] bytes)
        {
            return bytes == null ? null : (byte[])bytes.Clone();
        }

        static string NormalizeArchiveEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
                return string.Empty;

            entryName = entryName.Trim().Replace('\\', '/');
            while (entryName.StartsWith("/", StringComparison.Ordinal))
                entryName = entryName.Substring(1);

            var parts = entryName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                var part = NormalizeEntrySegment(parts[i]);
                if (string.IsNullOrEmpty(part))
                    continue;

                if (builder.Length > 0)
                    builder.Append('/');
                builder.Append(part);
            }

            return builder.ToString();
        }

        static string NormalizeEntrySegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalidChars.Length; i++)
                value = value.Replace(invalidChars[i], '_');

            value = value.Replace(':', '_');
            return value;
        }

        static string NormalizeAssetId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    builder.Append(c);
                else if (char.IsWhiteSpace(c))
                    builder.Append('_');
            }

            return builder.ToString().Trim('_');
        }

        static string NormalizeHashPrefix(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return string.Empty;

            hash = hash.Trim();
            return hash.Length <= 8 ? hash : hash.Substring(0, 8);
        }
    }
}
