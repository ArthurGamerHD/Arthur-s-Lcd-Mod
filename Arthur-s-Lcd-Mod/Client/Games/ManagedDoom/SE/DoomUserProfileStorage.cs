using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Main-thread bridge between Doom's virtual files and Space Engineers'
    /// per-mod local storage. Doom's worker never calls the storage API.
    /// </summary>
    internal static class DoomUserProfileStorage
    {
        internal const string ArchiveName = "DoomUserProfile.zip";
        internal const string VirtualDirectory = "profile";

        static readonly DoomVirtualFileSystem Files = new DoomVirtualFileSystem();
        static volatile bool _loaded;
        static long _lastFailedRevision = -1;

        internal static void Initialize()
        {
            if (_loaded)
                return;

            var utilities = MyAPIGateway.Utilities;
            if (utilities.FileExistsInLocalStorage(ArchiveName, typeof(LcdModClientComponent)))
            {
                using (var reader = utilities.ReadBinaryFileInLocalStorage(ArchiveName, typeof(LcdModClientComponent)))
                {
                    if (reader == null)
                        throw new InvalidOperationException("Could not open the Doom user profile.");
                    Files.Load(reader.BaseStream);
                }
            }
            else
            {
                Files.Load(null);
            }

            _loaded = true;
        }

        internal static bool Exists(string path)
        {
            return _loaded && Files.Exists(path);
        }

        internal static byte[] ReadAllBytes(string path)
        {
            EnsureLoaded();
            return Files.ReadAllBytes(path);
        }

        internal static IEnumerable<string> ReadLines(string path)
        {
            var lines = new List<string>();
            var content = Encoding.UTF8.GetString(ReadAllBytes(path));
            int start = 0;
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] != '\r' && content[i] != '\n')
                    continue;
                lines.Add(content.Substring(start, i - start));
                if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
            if (start < content.Length)
                lines.Add(content.Substring(start));
            return lines;
        }

        internal static void WriteAllBytes(string path, byte[] bytes)
        {
            EnsureLoaded();
            Files.WriteAllBytes(path, bytes);
        }

        internal static void FlushPending()
        {
            if (!_loaded)
                return;

            byte[] archive;
            long revision = -1;
            try
            {
                if (!Files.TryCreateArchive(out archive, out revision))
                    return;

                var utilities = MyAPIGateway.Utilities;
                using (var writer = utilities.WriteBinaryFileInLocalStorage(ArchiveName, typeof(LcdModClientComponent)))
                {
                    if (writer == null)
                        throw new InvalidOperationException("Could not open the Doom user profile for writing.");
                    writer.BaseStream.SetLength(0);
                    writer.BaseStream.Position = 0;
                    writer.Write(archive);
                }

                Files.MarkSaved(revision);
                _lastFailedRevision = -1;
            }
            catch (Exception error)
            {
                if (revision != _lastFailedRevision)
                {
                    LogHelper.Log(MyLogSeverity.Warning,
                        "Could not save " + ArchiveName + ": " + error.Message);
                    _lastFailedRevision = revision;
                }
            }
        }

        static void EnsureLoaded()
        {
            if (!_loaded)
                throw new InvalidOperationException("The Doom user profile was not initialized.");
        }
    }
}
