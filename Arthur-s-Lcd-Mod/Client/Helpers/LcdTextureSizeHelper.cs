using System;
using System.Collections.Generic;
using System.IO;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    public static class LcdTextureSizeHelper
    {
        static readonly Dictionary<string, CachedTextureSize> Cache =
            new Dictionary<string, CachedTextureSize>(StringComparer.OrdinalIgnoreCase);

        static readonly object PendingMeasurementsLock = new object();
        static readonly HashSet<string> PendingMeasurements =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetTextureSize(string textureId, out Vector2I size)
        {
            size = Vector2I.Zero;
            if (string.IsNullOrWhiteSpace(textureId))
                return false;

            if (!TextureHelper.CanRenderTexture(textureId))
                return false;

            MyLCDTextureDefinition definition;
            try
            {
                definition = MyDefinitionManager.Static.GetDefinition<MyLCDTextureDefinition>(
                    MyStringHash.GetOrCompute(textureId));
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(LcdTextureSizeHelper));
                return false;
            }

            return TryGetTextureSize(definition, out size);
        }

        public static bool TryGetTextureSize(MyLCDTextureDefinition definition, out Vector2I size)
        {
            size = Vector2I.Zero;
            if (definition == null)
                return false;

            if (!TextureHelper.CanRenderTexture(definition.Id.SubtypeName))
                return false;

            if (TryReadMetadataTextureSize(definition, out size))
                return true;

            var paths = GetSourceCandidates(definition);
            for (int i = 0; i < paths.Count; i++)
            {
                if (TryReadCachedImageSize(paths[i], out size))
                    return true;
            }

            return false;
        }

        static bool TryReadMetadataTextureSize(MyLCDTextureDefinition definition, out Vector2I size)
        {
            size = Vector2I.Zero;
            if (definition == null)
                return false;

            TextureTransferHelper.TextureMetadata invalidMetadata;
            if (TryReadMetadataTextureSize(definition.Id.SubtypeName, out size, out invalidMetadata))
                return true;
            if (invalidMetadata != null)
                QueueAsyncTextureSizeMeasurement(definition.Id.SubtypeName, invalidMetadata, GetSourceCandidates(definition));

            var paths = GetSourceCandidates(definition);
            for (int i = 0; i < paths.Count; i++)
            {
                var baseName = TextureTransferHelper.NormalizeTextureName(Path.GetFileNameWithoutExtension(paths[i]));
                if (TryReadMetadataTextureSize(baseName, out size, out invalidMetadata))
                    return true;
                if (invalidMetadata != null)
                    QueueAsyncTextureSizeMeasurement(baseName, invalidMetadata, paths);
            }

            return false;
        }

        static bool TryReadMetadataTextureSize(string metadataKey, out Vector2I size,
            out TextureTransferHelper.TextureMetadata invalidMetadata)
        {
            size = Vector2I.Zero;
            invalidMetadata = null;
            if (string.IsNullOrWhiteSpace(metadataKey))
                return false;

            TextureTransferHelper.TextureMetadata metadata;
            if (!TextureTransferHelper.TryReadTextureMetadata(metadataKey, out metadata) || metadata == null)
                return false;

            if (metadata.Width <= 0 || metadata.Height <= 0)
            {
                invalidMetadata = metadata;
                return false;
            }

            size = new Vector2I(metadata.Width, metadata.Height);
            return true;
        }

        static void QueueAsyncTextureSizeMeasurement(string metadataKey,
            TextureTransferHelper.TextureMetadata metadata,
            IEnumerable<string> sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(metadataKey) || metadata == null)
                return;

            var sourceFileName = Path.GetFileName(metadata.SourceFileName);
            if (string.IsNullOrWhiteSpace(sourceFileName))
                return;

            var pendingKey = metadataKey + "|" + sourceFileName;
            lock (PendingMeasurementsLock)
            {
                if (!PendingMeasurements.Add(pendingKey))
                    return;
            }

            byte[] bytes;
            if (!TextureTransferHelper.TryReadTextureFileBytes(sourceFileName, out bytes))
            {
                FinishAsyncTextureSizeMeasurement(pendingKey);
                return;
            }

            var work = new TextureSizeMeasurementWork
            {
                PendingKey = pendingKey,
                MetadataKey = metadataKey,
                SourceFileName = sourceFileName,
                SourcePaths = CopySourcePaths(sourcePaths),
                TextureBytes = bytes,
                TempFileName = BuildTempTextureFileName(metadataKey, sourceFileName)
            };
            work.TempPath = BuildLocalStoragePath(work.TempFileName);

            MyAPIGateway.Parallel.Start(
                delegate { MeasureTextureSizeFromTempFile(work); },
                delegate { CompleteTextureSizeMeasurement(work); });
        }

        static List<string> CopySourcePaths(IEnumerable<string> sourcePaths)
        {
            var result = new List<string>();
            if (sourcePaths == null)
                return result;

            foreach (var sourcePath in sourcePaths)
                AddSourceCandidate(result, sourcePath);

            return result;
        }

        static string BuildTempTextureFileName(string metadataKey, string sourceFileName)
        {
            var baseName = TextureTransferHelper.NormalizeTextureName(metadataKey);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = TextureTransferHelper.NormalizeTextureName(sourceFileName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "texture";

            return baseName + "_tmp.dds";
        }

        static string BuildLocalStoragePath(string fileName)
        {
            return Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName,
                fileName);
        }

        static void MeasureTextureSizeFromTempFile(TextureSizeMeasurementWork work)
        {
            if (work == null)
                return;

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(
                           work.TempFileName,
                           typeof(LcdModSessionComponent)))
                {
                    writer.Write(work.TextureBytes);
                }

                Vector2I size;
                if (TryReadImageSizeFromPath(work.TempPath, out size) && size.X > 0 && size.Y > 0)
                {
                    work.Size = size;
                    work.Found = true;
                }
            }
            catch (Exception e)
            {
                work.Exception = e;
            }
            finally
            {
                TryDeleteTempTexture(work.TempFileName);
            }
        }

        static void CompleteTextureSizeMeasurement(TextureSizeMeasurementWork work)
        {
            if (work == null)
                return;

            FinishAsyncTextureSizeMeasurement(work.PendingKey);

            if (work.Exception != null)
            {
                ErrorHandlerHelper.LogError(work.Exception, typeof(LcdTextureSizeHelper));
                return;
            }

            if (!work.Found || work.Size.X <= 0 || work.Size.Y <= 0)
                return;

            TextureTransferHelper.TextureMetadata metadata;
            if (!TextureTransferHelper.TryReadTextureMetadata(work.MetadataKey, out metadata) || metadata == null)
                metadata = new TextureTransferHelper.TextureMetadata();

            metadata.RegistrationName = string.IsNullOrWhiteSpace(metadata.RegistrationName)
                ? work.MetadataKey
                : metadata.RegistrationName;
            metadata.SourceFileName = string.IsNullOrWhiteSpace(metadata.SourceFileName)
                ? work.SourceFileName
                : metadata.SourceFileName;
            metadata.Width = work.Size.X;
            metadata.Height = work.Size.Y;
            metadata.LastUpdatedUtcTicks = DateTime.UtcNow.Ticks;
            TextureTransferHelper.TryWriteTextureMetadata(work.MetadataKey, metadata);

            SetCachedTextureSize(work.MetadataKey, work.Size);
            SetCachedTextureSize(work.SourceFileName, work.Size);
            for (int i = 0; i < work.SourcePaths.Count; i++)
                SetCachedTextureSize(work.SourcePaths[i], work.Size);
        }

        static void FinishAsyncTextureSizeMeasurement(string pendingKey)
        {
            if (string.IsNullOrWhiteSpace(pendingKey))
                return;

            lock (PendingMeasurementsLock)
                PendingMeasurements.Remove(pendingKey);
        }

        static void SetCachedTextureSize(string cacheKey, Vector2I size)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || size.X <= 0 || size.Y <= 0)
                return;

            Cache[NormalizeCacheKey(cacheKey)] = new CachedTextureSize
            {
                Found = true,
                Size = size
            };
        }

        static void TryDeleteTempTexture(string tempFileName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempFileName) &&
                    MyAPIGateway.Utilities.FileExistsInLocalStorage(
                        tempFileName,
                        typeof(LcdModSessionComponent)))
                {
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(
                        tempFileName,
                        typeof(LcdModSessionComponent));
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, e.ToString());
            }
        }

        public static bool TryGetTextureAspectRatio(string textureId, out float aspectRatio)
        {
            aspectRatio = 0f;

            Vector2I size;
            if (!TryGetTextureSize(textureId, out size) || size.Y <= 0)
                return false;

            aspectRatio = (float)size.X / size.Y;
            return aspectRatio > 0f;
        }

        public static bool TryGetTextureAspectRatio(MyLCDTextureDefinition definition, out float aspectRatio)
        {
            aspectRatio = 0f;

            Vector2I size;
            if (!TryGetTextureSize(definition, out size) || size.Y <= 0)
                return false;

            aspectRatio = (float)size.X / size.Y;
            return aspectRatio > 0f;
        }

        static List<string> GetSourceCandidates(MyLCDTextureDefinition definition)
        {
            var result = new List<string>(2);
            AddSourceCandidate(result, definition.SpritePath);
            AddSourceCandidate(result, definition.TexturePath);
            return result;
        }

        static void AddSourceCandidate(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
                return;

            for (int i = 0; i < paths.Count; i++)
                if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
                    return;

            paths.Add(path);
        }

        static bool TryReadCachedImageSize(string sourcePath, out Vector2I size)
        {
            size = Vector2I.Zero;
            if (string.IsNullOrWhiteSpace(sourcePath))
                return false;

            var cacheKey = NormalizeCacheKey(sourcePath);
            CachedTextureSize cached;
            if (Cache.TryGetValue(cacheKey, out cached))
            {
                size = cached.Size;
                return cached.Found;
            }

            cached = new CachedTextureSize();
            Cache[cacheKey] = cached;

            if (!TryReadImageSize(sourcePath, out size))
            {
                cached.Found = false;
                cached.Size = Vector2I.Zero;
                return false;
            }

            cached.Found = true;
            cached.Size = size;
            size = cached.Size;
            return true;
        }

        static bool TryReadImageSize(string sourcePath, out Vector2I size)
        {
            size = Vector2I.Zero;

            try
            {
                var candidates = GetReadablePathCandidates(sourcePath);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (TryReadImageSizeFromPath(candidates[i], out size))
                        return true;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(LcdTextureSizeHelper));
            }

            LogHelper.LogOnce("texture-size:missing:" + sourcePath,
                "unable to read texture dimensions from source: " + sourcePath);
            return false;
        }

        static bool TryReadImageSizeFromPath(string path, out Vector2I size)
        {
            size = Vector2I.Zero;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                int width;
                int height;
                if (MyImageHeaderUtils.Read_PNG_Dimensions(path, out width, out height) &&
                    width > 0 &&
                    height > 0)
                {
                    size = new Vector2I(width, height);
                    return true;
                }

                return false;
            }

            MyImageHeaderUtils.DDS_HEADER header;
            if (MyImageHeaderUtils.Read_DDS_HeaderData(path, out header) &&
                header.dwWidth > 0 &&
                header.dwHeight > 0)
            {
                size = new Vector2I((int)header.dwWidth, (int)header.dwHeight);
                return true;
            }

            return false;
        }

        static List<string> GetReadablePathCandidates(string sourcePath)
        {
            var result = new List<string>();
            AddSourceCandidate(result, sourcePath);

            var contentPath = ToContentPath(sourcePath);
            var utilities = MyAPIGateway.Utilities;
            if (!string.IsNullOrWhiteSpace(contentPath))
            {
                if (!Path.IsPathRooted(contentPath))
                {
                    var session = MyAPIGateway.Session;
                    if (session != null && session.Mods != null)
                    {
                        for (int i = 0; i < session.Mods.Count; i++)
                            AddSourceCandidate(result, Path.Combine(session.Mods[i].GetPath(), contentPath));
                    }

                    AddSourceCandidate(result, Path.Combine(utilities.GamePaths.ContentPath, contentPath));
                }

                AddSourceCandidate(result, contentPath);
            }

            var localFile = Path.GetFileName(sourcePath);
            if (!string.IsNullOrWhiteSpace(localFile))
            {
                try
                {
                    if (utilities.FileExistsInLocalStorage(localFile, typeof(LcdModSessionComponent)))
                    {
                        AddSourceCandidate(result, Path.Combine(
                            utilities.GamePaths.UserDataPath,
                            "Storage",
                            utilities.GamePaths.ModScopeName,
                            localFile));
                    }
                }
                catch (Exception e)
                {
                    LogHelper.Log(MyLogSeverity.Warning, e.ToString());
                }
            }

            return result;
        }

        static string ToContentPath(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return string.Empty;

            var path = sourcePath.Trim().Replace('\\', '/');
            const string contentMarker = "/Content/";

            var contentIndex = path.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
            if (contentIndex >= 0)
                path = path.Substring(contentIndex + contentMarker.Length);

            while (path.StartsWith("/", StringComparison.Ordinal))
                path = path.Substring(1);

            if (path.StartsWith("244850/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("244850/".Length);
                var separator = path.IndexOf("/", StringComparison.Ordinal);
                if (separator >= 0 && separator < path.Length - 1)
                    path = path.Substring(separator + 1);
            }

            return path;
        }

        static string NormalizeCacheKey(string sourcePath)
        {
            return string.IsNullOrWhiteSpace(sourcePath)
                ? string.Empty
                : sourcePath.Trim().Replace('\\', '/');
        }

        class CachedTextureSize
        {
            public bool Found;
            public Vector2I Size;
        }

        sealed class TextureSizeMeasurementWork
        {
            public string PendingKey;
            public string MetadataKey;
            public string SourceFileName;
            public List<string> SourcePaths = new List<string>();
            public byte[] TextureBytes;
            public string TempFileName;
            public string TempPath;
            public Vector2I Size;
            public bool Found;
            public Exception Exception;
        }
    }
}
