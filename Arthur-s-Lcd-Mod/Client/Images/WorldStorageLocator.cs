using System;
using System.IO;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using VRage.Utils;

namespace LcdMod.Client.Images
{
    /// <summary>
    /// Finds the physical folder behind the world-storage API by writing a uniquely named canary and
    /// locating it: first under the reported save path, then by enumerating the saves folder, then by the
    /// session-name path. Resolution is lazy, runs off the game thread, and is cached per reported path.
    /// </summary>
    internal static class WorldStorageLocator
    {
        const string SENTINEL_PREFIX = "SentinelCanary_";
        const string SENTINEL_EXTENSION = ".tmp";

        static readonly object Lock = new object();
        static string _reportedSavePath;
        static string _storageRoot;

        public static void Reset()
        {
            lock (Lock)
            {
                _reportedSavePath = null;
                _storageRoot = null;
            }
        }

        public static bool TryGetResolvedRoot(out string storageRoot)
        {
            lock (Lock)
            {
                storageRoot = _storageRoot;
                return storageRoot != null;
            }
        }

        public static bool TryGetStorageRoot(out string storageRoot)
        {
            lock (Lock)
            {
                string reported = NormalizePath(MyAPIGateway.Session.CurrentPath);
                if (_storageRoot != null && string.Equals(reported, _reportedSavePath, StringComparison.OrdinalIgnoreCase))
                {
                    storageRoot = _storageRoot;
                    return true;
                }

                string savePath = RunSelfTest(reported);
                if (savePath == null)
                {
                    storageRoot = null;
                    return false;
                }

                _reportedSavePath = reported;
                _storageRoot = StorageRootFor(savePath);
                storageRoot = _storageRoot;
                return true;
            }
        }

        public static string ToRendererPath(string storageRoot, string fileName)
        {
            return (storageRoot + "/" + fileName).Replace('/', '\\');
        }

        static string RunSelfTest(string reported)
        {
            string sentinel = SENTINEL_PREFIX + Guid.NewGuid().ToString("N") + SENTINEL_EXTENSION;
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(sentinel, typeof(LcdModSessionComponent)))
                    writer.Write("image storage");
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "World-storage canary could not be written: " + e.Message);
                return null;
            }

            try
            {
                string resolved = null;
                string source = null;
                if (SentinelExists(reported, sentinel))
                {
                    resolved = reported;
                    source = "reported";
                }

                if (resolved == null)
                {
                    resolved = FindInSaves(sentinel);
                    source = "enumerated";
                }

                if (resolved == null)
                {
                    string sessionNamed = ResolveSessionNamedSavePath();
                    if (sessionNamed != null && SentinelExists(sessionNamed, sentinel))
                    {
                        resolved = sessionNamed;
                        source = "session-name";
                    }
                }

                if (resolved == null)
                {
                    LogHelper.Log(MyLogSeverity.Warning,
                        "World-storage canary could not be located. ReportedPath='" + (reported ?? "<null>") + "'");
                    return null;
                }

                LogHelper.Log(
                    string.Equals(resolved, reported, StringComparison.OrdinalIgnoreCase)
                        ? MyLogSeverity.Info
                        : MyLogSeverity.Warning,
                    "World-storage images resolved via " + source + " lookup: '" + resolved + "'");
                return resolved;
            }
            finally
            {
                try
                {
                    if (MyAPIGateway.Utilities.FileExistsInWorldStorage(sentinel, typeof(LcdModSessionComponent)))
                        MyAPIGateway.Utilities.DeleteFileInWorldStorage(sentinel, typeof(LcdModSessionComponent));
                }
                catch (Exception e)
                {
                    LogHelper.Log(MyLogSeverity.Warning, "Could not delete world-storage canary '" + sentinel + "': " + e.Message);
                }
            }
        }

        static bool SentinelExists(string savePath, string sentinel)
        {
            if (string.IsNullOrWhiteSpace(savePath))
                return false;

            string storageRoot = StorageRootFor(savePath);
            string expected = storageRoot + "/" + sentinel;
            try
            {
                string[] found = PathUtils.GetFilesRecursively(storageRoot, sentinel);
                if (found == null)
                    return false;

                for (int i = 0; i < found.Length; i++)
                {
                    if (string.Equals(NormalizePath(found[i]), expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        static string FindInSaves(string sentinel)
        {
            try
            {
                string savesRoot = NormalizePath(Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Saves"));
                string suffix = "/Storage/" + MyAPIGateway.Utilities.GamePaths.ModScopeName + "/" + sentinel;
                string[] found = PathUtils.GetFilesRecursively(savesRoot, sentinel);
                if (found == null)
                    return null;

                string resolved = null;
                for (int i = 0; i < found.Length; i++)
                {
                    string normalized = NormalizePath(found[i]);
                    if (!normalized.StartsWith(savesRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                        !normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string candidate = normalized.Substring(0, normalized.Length - suffix.Length);
                    if (resolved != null && !string.Equals(resolved, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        LogHelper.Log(MyLogSeverity.Warning,
                            "World-storage canary found in several saves; refusing to choose. '" + resolved +
                            "' and '" + candidate + "'");
                        return null;
                    }

                    resolved = candidate;
                }

                return resolved;
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not enumerate saves for the world-storage canary: " + e.Message);
                return null;
            }
        }

        static string ResolveSessionNamedSavePath()
        {
            string savesRoot = NormalizePath(MyAPIGateway.Utilities.GamePaths.SavesPath);
            string saveName = MyAPIGateway.Session.Name;
            if (string.IsNullOrWhiteSpace(savesRoot) || string.IsNullOrWhiteSpace(saveName))
                return null;

            const string workshopPrefix = "(Workshop) ";
            if (saveName.StartsWith(workshopPrefix, StringComparison.OrdinalIgnoreCase))
                saveName = saveName.Substring(workshopPrefix.Length);

            return savesRoot + "/" + saveName.Replace(':', '-');
        }

        static string StorageRootFor(string savePath)
        {
            return NormalizePath(savePath) + "/Storage/" + MyAPIGateway.Utilities.GamePaths.ModScopeName;
        }

        static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? path : path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
