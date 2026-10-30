using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adk.Compression.Zip;
using LcdMod.Common.Helpers;
using LcdMod.Common.Images;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using VRage.Utils;

namespace LcdMod.Client.Images
{
    /// <summary>
    /// The player's own images, kept in local_textures.zip. Loose .png/.apng/.dds files dropped into the mod
    /// storage folder are moved into the archive, and each source is encoded once into a grid payload
    /// cached beside it, so picking an image never re-encodes it.
    /// </summary>
    internal sealed class LocalImageLibrary
    {
        public sealed class Item
        {
            public string Name;
            public string EntryName;
            public EncodedGridImage Image;
        }

        const string CACHE_FOLDER = ".cache/";
        const string CACHE_EXTENSION = ".bin";
        const string FAILURE_EXTENSION = ".failed";
        const string README_FILE = "readme-for-texture-import.txt";

        static readonly string[] ObsoleteFiles =
        {
            "png-to-dds.bat",
            "png-to-dds.sh",
            "tools_path.txt",
            "import.txt",
            "cached_textures.zip",
            "cached_textures.xml",
            "local_textures.xml"
        };

        public static LocalImageLibrary Instance { get; private set; }

        readonly object _archiveLock = new object();
        readonly List<Item> _items = new List<Item>();
        volatile bool _stopped;
        bool _scanning;
        bool _rescanRequested;
        volatile bool _reportFailures;

        public event Action Changed;

        public IReadOnlyList<Item> Items => _items;
        public bool IsScanning => _scanning;

        public void Load()
        {
            Instance = this;
            _stopped = false;
            DeleteObsoleteFiles();
            WriteReadme();
            Rescan(false);
        }

        public void Unload()
        {
            _stopped = true;
            _items.Clear();
            Changed = null;
            if (Instance == this)
                Instance = null;
        }

        public bool TryGetDisplayName(string spriteName, out string name)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (GridImageCodec.ToSpriteName(_items[i].Image.Id) == spriteName)
                {
                    name = _items[i].Name;
                    return true;
                }
            }

            name = null;
            return false;
        }

        /// <summary>
        /// Imports and encodes new images. Sources that failed before are skipped without re-encoding;
        /// <paramref name="reportKnownFailures"/> repeats their stored reasons, which only an explicit rescan asks for.
        /// </summary>
        public void Rescan(bool reportKnownFailures)
        {
            _reportFailures |= reportKnownFailures;
            if (_scanning)
            {
                _rescanRequested = true;
                return;
            }

            _scanning = true;
            MyAPIGateway.Parallel.StartBackground(Scan, OnScanFinished);
        }

        public bool Remove(string name)
        {
            if (_scanning || string.IsNullOrWhiteSpace(name))
                return false;

            name = name.Trim();
            string entryName = null;
            lock (_archiveLock)
            {
                List<MinimalZip.Entry> entries = ReadArchive();
                for (int i = 0; i < entries.Count && entryName == null; i++)
                {
                    if (IsSourceName(entries[i].Name) &&
                        (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetFileNameWithoutExtension(entries[i].Name), name, StringComparison.OrdinalIgnoreCase)))
                    {
                        entryName = entries[i].Name;
                        string hash = SourceHash(entries[i].Data);
                        entries.RemoveAt(i);
                        entries.RemoveAll(entry => entry.Name == CacheNameFor(hash) || entry.Name == FailureNameFor(hash));
                    }
                }

                if (entryName == null)
                    return false;

                WriteArchive(entries);
            }

            _items.RemoveAll(item => string.Equals(item.EntryName, entryName, StringComparison.OrdinalIgnoreCase));
            RaiseChanged();
            return true;
        }

        void OnScanFinished()
        {
            _scanning = false;
            if (!_stopped)
                RaiseChanged();
            if (_rescanRequested && !_stopped)
            {
                _rescanRequested = false;
                Rescan(false);
            }
        }

        void Scan()
        {
            try
            {
                lock (_archiveLock)
                    ScanLocked();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(LocalImageLibrary));
            }
        }

        void ScanLocked()
        {
            List<MinimalZip.Entry> entries = ReadArchive();
            List<string> imported = ImportLooseFiles(entries);
            bool dirty = imported.Count > 0;
            if (dirty)
            {
                WriteArchive(entries);
                for (int i = 0; i < imported.Count; i++)
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(imported[i], typeof(LcdModSessionComponent));
            }

            bool reportFailures = _reportFailures;
            _reportFailures = false;
            var usedCaches = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<MinimalZip.Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsSourceName(entries[i].Name))
                    sources.Add(entries[i]);
            }

            for (int i = 0; i < sources.Count && !_stopped; i++)
            {
                MinimalZip.Entry source = sources[i];
                string hash = SourceHash(source.Data);
                string cacheName = CacheNameFor(hash);
                string failureName = FailureNameFor(hash);
                usedCaches.Add(cacheName);
                usedCaches.Add(failureName);

                byte[] failure = FindData(entries, failureName);
                if (failure != null)
                {
                    if (reportFailures)
                        ReportFailure(source.Name, Encoding.UTF8.GetString(failure));
                    continue;
                }

                EncodedGridImage image = ReadCache(FindData(entries, cacheName));
                if (image == null)
                {
                    string error;
                    if (!GridImageCodec.TryEncode(source.Name, source.Data, out image, out error))
                    {
                        entries.Add(new MinimalZip.Entry(failureName, Encoding.UTF8.GetBytes(error ?? string.Empty)));
                        dirty = true;
                        ReportFailure(source.Name, error);
                        continue;
                    }

                    entries.Add(new MinimalZip.Entry(cacheName, MyAPIGateway.Utilities.SerializeToBinary(image)));
                    dirty = true;
                }

                var item = new Item
                {
                    Name = Path.GetFileNameWithoutExtension(source.Name),
                    EntryName = source.Name,
                    Image = image
                };
                MyAPIGateway.Utilities.InvokeOnGameThread(() => Publish(item));
            }

            if (!_stopped)
            {
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sources.Count; i++)
                    present.Add(sources[i].Name);
                MyAPIGateway.Utilities.InvokeOnGameThread(() => RemoveMissing(present));

                int removed = entries.RemoveAll(entry =>
                    entry.Name.StartsWith(CACHE_FOLDER, StringComparison.Ordinal) && !usedCaches.Contains(entry.Name));
                removed += entries.RemoveAll(entry =>
                    !entry.Name.StartsWith(CACHE_FOLDER, StringComparison.Ordinal) && !IsSourceName(entry.Name));
                dirty |= removed > 0;
            }

            if (dirty)
                WriteArchive(entries);
        }

        void Publish(Item item)
        {
            if (_stopped || Instance != this)
                return;

            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].EntryName, item.EntryName, StringComparison.OrdinalIgnoreCase) &&
                    _items[i].Image.Id == item.Image.Id)
                {
                    return;
                }
            }

            _items.RemoveAll(existing => string.Equals(existing.EntryName, item.EntryName, StringComparison.OrdinalIgnoreCase));
            _items.Add(item);
            _items.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            GridImageClientService.Instance?.RegisterLocal(item.Image);
            RaiseChanged();
        }

        void RemoveMissing(HashSet<string> present)
        {
            if (!_stopped && Instance == this && _items.RemoveAll(item => !present.Contains(item.EntryName)) > 0)
                RaiseChanged();
        }

        void RaiseChanged()
        {
            Action changed = Changed;
            if (changed != null)
                changed();
        }

        static List<string> ImportLooseFiles(List<MinimalZip.Entry> entries)
        {
            var imported = new List<string>();
            string storageRoot = LocalStorageRoot();
            var files = new List<string>();
            try
            {
                files.AddRange(PathUtils.GetFilesRecursively(storageRoot, "*.png"));
                files.AddRange(PathUtils.GetFilesRecursively(storageRoot, "*.apng"));
                files.AddRange(PathUtils.GetFilesRecursively(storageRoot, "*.dds"));
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not list local images: " + e.Message);
                return imported;
            }

            for (int i = 0; i < files.Count; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (!IsSourceName(fileName) ||
                    !string.Equals(Normalize(Path.GetDirectoryName(files[i])), storageRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byte[] data;
                using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    long length = reader.BaseStream.Length;
                    if (length <= 0 || length > int.MaxValue)
                        continue;

                    data = reader.ReadBytes((int)length);
                }

                if (!ContainsData(entries, data))
                    entries.Add(new MinimalZip.Entry(UniqueName(entries, fileName), data, CompressionFor(fileName)));

                imported.Add(fileName);
            }

            return imported;
        }

        static List<MinimalZip.Entry> ReadArchive()
        {
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(Constants.LOCAL_TEXTURES, typeof(LcdModSessionComponent)))
                return new List<MinimalZip.Entry>();

            using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(Constants.LOCAL_TEXTURES, typeof(LcdModSessionComponent)))
            {
                if (reader.BaseStream.Length <= 0)
                    return new List<MinimalZip.Entry>();

                var entries = new List<MinimalZip.Entry>();
                List<MinimalZip.Entry> read = MinimalZip.Read(reader.BaseStream);
                for (int i = 0; i < read.Count; i++)
                    entries.Add(new MinimalZip.Entry(read[i].Name, read[i].Data, CompressionFor(read[i].Name)));

                return entries;
            }
        }

        static void WriteArchive(List<MinimalZip.Entry> entries)
        {
            if (entries.Count == 0)
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(Constants.LOCAL_TEXTURES, typeof(LcdModSessionComponent)))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(Constants.LOCAL_TEXTURES, typeof(LcdModSessionComponent));
                return;
            }

            using (BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(Constants.LOCAL_TEXTURES, typeof(LcdModSessionComponent)))
            {
                writer.BaseStream.SetLength(0);
                writer.BaseStream.Position = 0;
                MinimalZip.Write(writer.BaseStream, entries);
            }
        }

        static EncodedGridImage ReadCache(byte[] data)
        {
            if (data == null)
                return null;

            try
            {
                EncodedGridImage image = MyAPIGateway.Utilities.SerializeFromBinary<EncodedGridImage>(data);
                return image != null && GridImageCodec.IsValidId(image.Id) && image.Payload != null ? image : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void ReportFailure(string sourceName, string error)
        {
            string message = "Image " + sourceName + " was not imported: " + error +
                             ". Fix the file and copy it again, or run /lcdmod removelocaltexture " +
                             Path.GetFileNameWithoutExtension(sourceName);
            LogHelper.Log(MyLogSeverity.Warning, message);
            MyAPIGateway.Utilities.InvokeOnGameThread(() => MyAPIGateway.Utilities.ShowNotification(message, 8000));
        }

        static string SourceHash(byte[] source)
        {
            return ImageSha256.ComputeHex(source, 16);
        }

        static string CacheNameFor(string sourceHash)
        {
            return CACHE_FOLDER + sourceHash + CACHE_EXTENSION;
        }

        static string FailureNameFor(string sourceHash)
        {
            return CACHE_FOLDER + sourceHash + FAILURE_EXTENSION;
        }

        static bool IsSourceName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf('/') < 0 &&
                   !name.StartsWith(GridImageCodec.SPRITE_PREFIX, StringComparison.Ordinal) &&
                   (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".apng", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
        }

        static MinimalZip.CompressionMode CompressionFor(string name)
        {
            return name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                ? MinimalZip.CompressionMode.Auto
                : MinimalZip.CompressionMode.Stored;
        }

        static byte[] FindData(List<MinimalZip.Entry> entries, string name)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return entries[i].Data;
            }

            return null;
        }

        static bool ContainsData(List<MinimalZip.Entry> entries, byte[] data)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                byte[] existing = entries[i].Data;
                if (existing.Length != data.Length || !IsSourceName(entries[i].Name))
                    continue;

                bool equal = true;
                for (int b = 0; b < data.Length && equal; b++)
                    equal = existing[b] == data[b];

                if (equal)
                    return true;
            }

            return false;
        }

        static string UniqueName(List<MinimalZip.Entry> entries, string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string candidate = fileName;
            for (int suffix = 2; FindData(entries, candidate) != null; suffix++)
                candidate = baseName + "-" + suffix + extension;

            return candidate;
        }

        static string LocalStorageRoot()
        {
            return Normalize(Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName));
        }

        static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/').TrimEnd('/');
        }

        static void DeleteObsoleteFiles()
        {
            for (int i = 0; i < ObsoleteFiles.Length; i++)
            {
                try
                {
                    if (MyAPIGateway.Utilities.FileExistsInLocalStorage(ObsoleteFiles[i], typeof(LcdModSessionComponent)))
                        MyAPIGateway.Utilities.DeleteFileInLocalStorage(ObsoleteFiles[i], typeof(LcdModSessionComponent));
                }
                catch (Exception e)
                {
                    LogHelper.Log(MyLogSeverity.Warning, "Could not delete obsolete file " + ObsoleteFiles[i] + ": " + e.Message);
                }
            }
        }

        static void WriteReadme()
        {
            string storageRoot = LocalStorageRoot();
            if (storageRoot.StartsWith("z:", StringComparison.OrdinalIgnoreCase))
                storageRoot = storageRoot.Substring(2);

            using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(README_FILE, typeof(LcdModSessionComponent)))
            {
                writer.Write(
                    "Arthur's Lcd Mod - Custom Images\n" +
                    "================================\n\n" +
                    "Copy .png, .apng (animated PNG) or .dds images into this folder:\n" +
                    "   " + storageRoot + "\n\n" +
                    "They are imported automatically the next time you load a world, or right away with:\n" +
                    "   /lcdmod importtextures\n\n" +
                    "Imported files are moved into " + Constants.LOCAL_TEXTURES + " and show up in the image pickers.\n" +
                    "PNG images are converted for you; no external tools are needed.\n" +
                    "Animated PNGs play on Picture Frame screens, in step for every player.\n\n" +
                    "When you put an image on a screen, it is embedded into the grid, so it travels with the grid,\n" +
                    "its blueprints and saves, and other players see it without you being online.\n\n" +
                    "Limits:\n" +
                    "- At most " + Constants.MAX_IMAGE_DIMENSION + "x" + Constants.MAX_IMAGE_DIMENSION + " pixels.\n" +
                    "- At most " + Constants.MAX_IMAGE_PAYLOAD_BYTES / 1024 + " KiB after compression per image, and " +
                    Constants.MAX_GRID_IMAGE_PAYLOAD_BYTES / 1024 + " KiB per grid.\n" +
                    "- DDS width and height must be divisible by 4 (PNG images are padded automatically).\n\n" +
                    "To remove an image from your library, run:\n" +
                    "   /lcdmod removelocaltexture <name>\n");
            }
        }
    }
}
