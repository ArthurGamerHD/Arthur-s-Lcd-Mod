using System;
using System.Collections.Generic;
using System.IO;
using Adk.Compression.Zip;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Thread-safe, in-memory files for Doom's worker. ZIP encoding and Space
    /// Engineers storage access are performed separately on the main thread.
    /// </summary>
    public sealed class DoomVirtualFileSystem
    {
        readonly object _gate = new object();
        Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long _revision;
        long _savedRevision;

        public void Load(Stream archive)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (archive != null)
            {
                foreach (var entry in MinimalZip.Read(archive))
                {
                    if (entry.Name.EndsWith("/", StringComparison.Ordinal))
                        continue;
                    files[NormalizePath(entry.Name)] = (byte[])entry.Data.Clone();
                }
            }

            lock (_gate)
            {
                _files = files;
                _revision = 0;
                _savedRevision = 0;
            }
        }

        public bool Exists(string path)
        {
            string normalized;
            if (!TryNormalizePath(path, out normalized))
                return false;
            lock (_gate)
                return _files.ContainsKey(normalized);
        }

        public byte[] ReadAllBytes(string path)
        {
            var normalized = NormalizePath(path);
            lock (_gate)
            {
                byte[] bytes;
                if (!_files.TryGetValue(normalized, out bytes))
                    throw new FileNotFoundException("Doom profile file not found.", normalized);
                return (byte[])bytes.Clone();
            }
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            var normalized = NormalizePath(path);
            lock (_gate)
            {
                _files[normalized] = (byte[])bytes.Clone();
                _revision++;
            }
        }

        public bool TryCreateArchive(out byte[] archive, out long revision)
        {
            List<MinimalZip.Entry> entries;
            lock (_gate)
            {
                if (_revision == _savedRevision)
                {
                    archive = null;
                    revision = _revision;
                    return false;
                }

                revision = _revision;
                entries = new List<MinimalZip.Entry>(_files.Count);
                foreach (var file in _files)
                    entries.Add(new MinimalZip.Entry(file.Key, file.Value, MinimalZip.CompressionMode.Deflate));
            }

            archive = MinimalZip.WriteBytes(entries);
            return true;
        }

        public void MarkSaved(long revision)
        {
            lock (_gate)
                if (revision > _savedRevision && revision <= _revision)
                    _savedRevision = revision;
        }

        static string NormalizePath(string path)
        {
            string normalized;
            if (!TryNormalizePath(path, out normalized))
                throw new ArgumentException("Invalid Doom profile path.", nameof(path));
            return normalized;
        }

        static bool TryNormalizePath(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');
            if (path[0] == '/' || path.IndexOf(':') >= 0)
                return false;

            var segments = path.Split('/');
            foreach (var segment in segments)
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;

            normalized = string.Join("/", segments);
            return true;
        }
    }
}
