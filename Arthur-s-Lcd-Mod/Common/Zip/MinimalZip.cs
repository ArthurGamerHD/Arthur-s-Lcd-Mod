using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Common.Zip
{
    public static class MinimalZip
    {
        private const uint LOCAL_SIGNATURE = 0x04034B50;
        private const uint CENTRAL_SIGNATURE = 0x02014B50;
        private const uint END_SIGNATURE = 0x06054B50;

        private const ushort STORED_METHOD = 0;
        private const ushort UTF8_FLAG = 0x0800;
        private const ushort DESCRIPTOR_FLAG = 0x0008;

        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, true);

        public sealed class Entry
        {
            public string Name { get; }
            public byte[] Data { get; }
            public DateTime CreationTime
            {
                get { return DecodeDosDateTime(DosDate, DosTime); }
            }

            internal ushort DosTime { get; }
            internal ushort DosDate { get; }

            public Entry(string name, byte[] data)
                : this(name, data, DateTime.UtcNow)
            {
            }

            public Entry(string name, byte[] data, DateTime creationTime)
                : this(name, data, EncodeDosTime(creationTime), EncodeDosDate(creationTime))
            {
            }

            internal Entry(string name, byte[] data, ushort dosTime, ushort dosDate)
            {
                Name = name;

                if (Name == null)
                    throw new ArgumentNullException(nameof(name));

                Data = data;

                if (Data == null)
                    throw new ArgumentNullException(nameof(data));

                DosTime = dosTime;
                DosDate = dosDate;
            }
        }

        private sealed class DirectoryEntry
        {
            public string Name = "";
            public byte[] NameBytes = Array.Empty<byte>();
            public uint Crc;
            public uint Size;
            public uint LocalOffset;
            public ushort Flags;
            public ushort DosTime;
            public ushort DosDate;
        }

        public static ushort GetTime()
        {
            return EncodeDosTime(DateTime.UtcNow);
        }

        private static ushort EncodeDosTime(DateTime dateTime)
        {
            DateTime local = ToLocalDateTime(dateTime);

            int hour = local.Hour;                 // 0-23
            int minute = local.Minute;           // 0-59
            int second = local.Second;           // 0-59

            int dosHour = hour;                  // stored as value/2, so keep full hour then divide below
            int dosMinute = minute;
            int dosSecond2 = second / 2;       // 0-29

            ushort dosTime =
                (ushort)(((dosHour & 0x1F) << 11) |
                         ((dosMinute & 0x3F) << 5) |
                         (dosSecond2 & 0x1F));

            return dosTime;
        }


        public static ushort GetDate()
        {
            return EncodeDosDate(DateTime.UtcNow);
        }

        private static ushort EncodeDosDate(DateTime dateTime)
        {
            DateTime local = ToLocalDateTime(dateTime);

            int year = local.Year;              // valid range typically 1980-2107 for DOS
            int month = local.Month;           // 1-12
            int day = local.Day;              // 1-31

            // Clamp to DOS representable range
            year = MathHelper.Clamp(year, 1980, 2107);

            int yearsSince1980 = year - 1980;

            ushort dosDate =
                (ushort)(((yearsSince1980 & 0x7F) << 9) |
                         ((month & 0x0F) << 5) |
                         (day & 0x1F));

            return dosDate;
        }

        private static DateTime ToLocalDateTime(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified)
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Local);

            return dateTime.Kind == DateTimeKind.Utc
                ? dateTime.ToLocalTime()
                : dateTime;
        }

        private static DateTime DecodeDosDateTime(ushort dosDate, ushort dosTime)
        {
            int year = MathHelper.Clamp(
                1980 + ((dosDate >> 9) & 0x7F),
                1980,
                2107);
            int month = MathHelper.Clamp((dosDate >> 5) & 0x0F, 1, 12);
            int day = MathHelper.Clamp(
                dosDate & 0x1F,
                1,
                DateTime.DaysInMonth(year, month));
            int hour = MathHelper.Clamp((dosTime >> 11) & 0x1F, 0, 23);
            int minute = MathHelper.Clamp((dosTime >> 5) & 0x3F, 0, 59);
            int second = MathHelper.Clamp((dosTime & 0x1F) * 2, 0, 59);

            return new DateTime(
                year,
                month,
                day,
                hour,
                minute,
                second,
                DateTimeKind.Local);
        }
    
        public static void Write(Stream output, IEnumerable<Entry> entries)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            if (!output.CanWrite || !output.CanSeek)
                throw new ArgumentException(
                    "Output must be writable and seekable.", nameof(output));

            // Offsets in this implementation are relative to stream position zero.
            if (output.Position != 0 || output.Length != 0)
                throw new ArgumentException(
                    "Output must be an empty stream positioned at zero.",
                    nameof(output));

            var source = new List<Entry>(entries);

            if (source.Count > ushort.MaxValue)
                throw new NotSupportedException("ZIP64 is required.");

            var directory = new List<DirectoryEntry>(source.Count);

            using (var writer =
                   new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                foreach (Entry sourceEntry in source)
                {
                    string name = NormalizeName(sourceEntry.Name);
                    byte[] nameBytes = Utf8.GetBytes(name);

                    if (nameBytes.Length > ushort.MaxValue)
                        throw new NotSupportedException("Entry name is too long.");

                    uint localOffset = ToUInt32(
                        output.Position,
                        "Archive exceeds the non-ZIP64 size limit.");

                    uint size = checked((uint)sourceEntry.Data.Length);
                    uint crc = CalculateCrc32(sourceEntry.Data);
                    ushort dosTime = sourceEntry.DosTime;
                    ushort dosDate = sourceEntry.DosDate;

                    // Local file header.
                    writer.Write(LOCAL_SIGNATURE);
                    writer.Write((ushort)20); // Version needed: 2.0
                    writer.Write(UTF8_FLAG);
                    writer.Write(STORED_METHOD);
                    writer.Write(dosTime);
                    writer.Write(dosDate);
                    writer.Write(crc);
                    writer.Write(size); // Compressed size
                    writer.Write(size); // Uncompressed size
                    writer.Write((ushort)nameBytes.Length);
                    writer.Write((ushort)0); // Extra-field length

                    writer.Write(nameBytes);
                    writer.Write(sourceEntry.Data);

                    directory.Add(new DirectoryEntry
                    {
                        Name = name,
                        NameBytes = nameBytes,
                        Crc = crc,
                        Size = size,
                        LocalOffset = localOffset,
                        Flags = UTF8_FLAG,
                        DosTime = dosTime,
                        DosDate = dosDate
                    });
                }

                long centralStart = output.Position;

                uint centralOffset = ToUInt32(
                    centralStart,
                    "Archive exceeds the non-ZIP64 size limit.");

                foreach (DirectoryEntry entry in directory)
                {
                    // Central-directory file header.
                    writer.Write(CENTRAL_SIGNATURE);
                    writer.Write((ushort)20); // Made by: MS-DOS, 2.0
                    writer.Write((ushort)20); // Version needed: 2.0
                    writer.Write(entry.Flags);
                    writer.Write(STORED_METHOD);
                    writer.Write(entry.DosTime);
                    writer.Write(entry.DosDate);
                    writer.Write(entry.Crc);
                    writer.Write(entry.Size);
                    writer.Write(entry.Size);
                    writer.Write((ushort)entry.NameBytes.Length);
                    writer.Write((ushort)0); // Extra-field length
                    writer.Write((ushort)0); // File-comment length
                    writer.Write((ushort)0); // Starting disk
                    writer.Write((ushort)0); // Internal attributes
                    writer.Write((uint)0); // External attributes
                    writer.Write(entry.LocalOffset);
                    writer.Write(entry.NameBytes);
                }

                uint centralSize = ToUInt32(
                    output.Position - centralStart,
                    "Central directory exceeds the non-ZIP64 size limit.");

                // End of central directory.
                writer.Write(END_SIGNATURE);
                writer.Write((ushort)0); // This disk
                writer.Write((ushort)0); // Central-directory disk
                writer.Write((ushort)directory.Count);
                writer.Write((ushort)directory.Count);
                writer.Write(centralSize);
                writer.Write(centralOffset);
                writer.Write((ushort)0); // ZIP-comment length
            }
        }

        public static List<Entry> Read(Stream input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (!input.CanRead || !input.CanSeek)
                throw new ArgumentException(
                    "Input must be readable and seekable.", nameof(input));

            long endOffset = FindEndRecord(input);
            var directory = new List<DirectoryEntry>();

            using (var reader =
                   new BinaryReader(input, Encoding.UTF8, leaveOpen: true))
            {
                input.Position = endOffset;

                Require(
                    reader.ReadUInt32() == END_SIGNATURE,
                    "Invalid end-of-central-directory signature.");

                ushort disk = reader.ReadUInt16();
                ushort centralDisk = reader.ReadUInt16();
                ushort entriesOnDisk = reader.ReadUInt16();
                ushort entryCount = reader.ReadUInt16();
                uint centralSize = reader.ReadUInt32();
                uint centralOffset = reader.ReadUInt32();
                ushort commentLength = reader.ReadUInt16();

                Require(
                    disk == 0 &&
                    centralDisk == 0 &&
                    entriesOnDisk == entryCount,
                    "Multi-disk ZIP files are not supported.");

                Require(
                    centralSize != uint.MaxValue &&
                    centralOffset != uint.MaxValue,
                    "ZIP64 is not supported.");

                Require(
                    (ulong)centralOffset + centralSize <= (ulong)endOffset,
                    "Invalid central-directory bounds.");

                Require(
                    endOffset + 22L + commentLength == input.Length,
                    "Invalid ZIP comment length.");

                input.Position = centralOffset;

                for (int i = 0; i < entryCount; i++)
                {
                    Require(
                        reader.ReadUInt32() == CENTRAL_SIGNATURE,
                        "Invalid central-directory entry.");

                    reader.ReadUInt16(); // Version made by
                    reader.ReadUInt16(); // Version needed

                    ushort flags = reader.ReadUInt16();
                    ushort method = reader.ReadUInt16();

                    ushort dosTime = reader.ReadUInt16();
                    ushort dosDate = reader.ReadUInt16();

                    uint crc = reader.ReadUInt32();
                    uint compressedSize = reader.ReadUInt32();
                    uint uncompressedSize = reader.ReadUInt32();

                    ushort nameLength = reader.ReadUInt16();
                    ushort extraLength = reader.ReadUInt16();
                    ushort fileCommentLength = reader.ReadUInt16();
                    ushort startDisk = reader.ReadUInt16();

                    reader.ReadUInt16(); // Internal attributes
                    reader.ReadUInt32(); // External attributes

                    uint localOffset = reader.ReadUInt32();

                    Require(
                        startDisk == 0,
                        "Multi-disk ZIP files are not supported.");

                    Require(
                        (flags & 0x0001) == 0,
                        "Encrypted entries are not supported.");

                    Require(
                        (flags & ~(UTF8_FLAG | DESCRIPTOR_FLAG)) == 0,
                        "Unsupported ZIP entry flags.");

                    Require(
                        (flags & UTF8_FLAG) != 0,
                        "Only UTF-8 entry names are supported.");

                    Require(
                        method == STORED_METHOD,
                        "Only stored entries are supported.");

                    Require(
                        compressedSize == uncompressedSize,
                        "Stored entry has inconsistent sizes.");

                    Require(
                        compressedSize != uint.MaxValue &&
                        localOffset != uint.MaxValue,
                        "ZIP64 is not supported.");

                    byte[] nameBytes = ReadExactly(input, nameLength);
                    string name = Utf8.GetString(nameBytes);

                    Skip(input, (long)extraLength + fileCommentLength);

                    directory.Add(new DirectoryEntry
                    {
                        Name = name,
                        NameBytes = nameBytes,
                        Crc = crc,
                        Size = compressedSize,
                        LocalOffset = localOffset,
                        Flags = flags,
                        DosTime = dosTime,
                        DosDate = dosDate
                    });
                }

                Require(
                    input.Position <= (long)centralOffset + centralSize,
                    "Central directory exceeds its declared size.");

                var result = new List<Entry>(directory.Count);

                foreach (DirectoryEntry entry in directory)
                {
                    input.Position = entry.LocalOffset;

                    Require(
                        reader.ReadUInt32() == LOCAL_SIGNATURE,
                        "Invalid local-file-header signature.");

                    reader.ReadUInt16(); // Version needed

                    ushort localFlags = reader.ReadUInt16();
                    ushort localMethod = reader.ReadUInt16();

                    reader.ReadUInt16(); // Time
                    reader.ReadUInt16(); // Date

                    uint localCrc = reader.ReadUInt32();
                    uint localCompressedSize = reader.ReadUInt32();
                    uint localUncompressedSize = reader.ReadUInt32();

                    ushort localNameLength = reader.ReadUInt16();
                    ushort localExtraLength = reader.ReadUInt16();

                    Require(
                        localFlags == entry.Flags,
                        "Local and central entry flags differ.");

                    Require(
                        (localFlags & 0x0001) == 0,
                        "Encrypted entries are not supported.");

                    Require(
                        localMethod == STORED_METHOD,
                        "Unsupported local compression method.");

                    byte[] localName =
                        ReadExactly(input, localNameLength);

                    Require(
                        BytesEqual(localName, entry.NameBytes),
                        "Local and central entry names differ.");

                    Skip(input, localExtraLength);

                    // When bit 3 is set, sizes may be in a data descriptor.
                    if ((localFlags & DESCRIPTOR_FLAG) == 0)
                    {
                        Require(
                            localCrc == entry.Crc &&
                            localCompressedSize == entry.Size &&
                            localUncompressedSize == entry.Size,
                            "Local and central metadata differ.");
                    }

                    if (entry.Size > int.MaxValue)
                    {
                        throw new NotSupportedException(
                            "This byte[] API does not support entries over 2 GiB.");
                    }

                    byte[] data =
                        ReadExactly(input, checked((int)entry.Size));

                    Require(
                        CalculateCrc32(data) == entry.Crc,
                        "CRC-32 validation failed for " + entry.Name);

                    result.Add(new Entry(
                        entry.Name,
                        data,
                        entry.DosTime,
                        entry.DosDate));
                }

                return result;
            }
        }

        public static bool ContainsEntry(Stream input, string name, bool ignoreCase = false)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (!input.CanRead || !input.CanSeek)
                throw new ArgumentException(
                    "Input must be readable and seekable.", nameof(input));

            string normalizedName = NormalizeName(name);
            long endOffset = FindEndRecord(input);

            using (var reader =
                   new BinaryReader(input, Encoding.UTF8, leaveOpen: true))
            {
                input.Position = endOffset;

                Require(
                    reader.ReadUInt32() == END_SIGNATURE,
                    "Invalid end-of-central-directory signature.");

                ushort disk = reader.ReadUInt16();
                ushort centralDisk = reader.ReadUInt16();
                ushort entriesOnDisk = reader.ReadUInt16();
                ushort entryCount = reader.ReadUInt16();
                uint centralSize = reader.ReadUInt32();
                uint centralOffset = reader.ReadUInt32();
                ushort commentLength = reader.ReadUInt16();

                Require(
                    disk == 0 &&
                    centralDisk == 0 &&
                    entriesOnDisk == entryCount,
                    "Multi-disk ZIP files are not supported.");

                Require(
                    centralSize != uint.MaxValue &&
                    centralOffset != uint.MaxValue,
                    "ZIP64 is not supported.");

                Require(
                    (ulong)centralOffset + centralSize <= (ulong)endOffset,
                    "Invalid central-directory bounds.");

                Require(
                    endOffset + 22L + commentLength == input.Length,
                    "Invalid ZIP comment length.");

                input.Position = centralOffset;

                for (int i = 0; i < entryCount; i++)
                {
                    Require(
                        reader.ReadUInt32() == CENTRAL_SIGNATURE,
                        "Invalid central-directory entry.");

                    reader.ReadUInt16(); // Version made by
                    reader.ReadUInt16(); // Version needed

                    ushort flags = reader.ReadUInt16();
                    ushort method = reader.ReadUInt16();

                    reader.ReadUInt16(); // Time
                    reader.ReadUInt16(); // Date
                    reader.ReadUInt32(); // CRC

                    uint compressedSize = reader.ReadUInt32();
                    uint uncompressedSize = reader.ReadUInt32();

                    ushort nameLength = reader.ReadUInt16();
                    ushort extraLength = reader.ReadUInt16();
                    ushort fileCommentLength = reader.ReadUInt16();
                    ushort startDisk = reader.ReadUInt16();

                    reader.ReadUInt16(); // Internal attributes
                    reader.ReadUInt32(); // External attributes

                    uint localOffset = reader.ReadUInt32();

                    Require(
                        startDisk == 0,
                        "Multi-disk ZIP files are not supported.");

                    Require(
                        (flags & 0x0001) == 0,
                        "Encrypted entries are not supported.");

                    Require(
                        (flags & ~(UTF8_FLAG | DESCRIPTOR_FLAG)) == 0,
                        "Unsupported ZIP entry flags.");

                    Require(
                        (flags & UTF8_FLAG) != 0,
                        "Only UTF-8 entry names are supported.");

                    Require(
                        method == STORED_METHOD,
                        "Only stored entries are supported.");

                    Require(
                        compressedSize == uncompressedSize,
                        "Stored entry has inconsistent sizes.");

                    Require(
                        compressedSize != uint.MaxValue &&
                        localOffset != uint.MaxValue,
                        "ZIP64 is not supported.");

                    byte[] nameBytes = ReadExactly(input, nameLength);
                    string entryName = Utf8.GetString(nameBytes);

                    Skip(input, (long)extraLength + fileCommentLength);

                    if (string.Equals(
                            entryName,
                            normalizedName,
                            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                Require(
                    input.Position <= (long)centralOffset + centralSize,
                    "Central directory exceeds its declared size.");

                return false;
            }
        }

        private static string NormalizeName(string name)
        {
            name = name.Replace('\\', '/');

            if (name.Length == 0 ||
                name[0] == '/' ||
                name.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "Entry names must be relative paths.", nameof(name));
            }

            if (name.Length >= 2 &&
                char.IsLetter(name[0]) &&
                name[1] == ':')
            {
                throw new ArgumentException(
                    "Drive-qualified names are not allowed.", nameof(name));
            }

            string[] parts = name.Split('/');

            foreach (string part in parts)
            {
                if (part == "..")
                {
                    throw new ArgumentException(
                        "Parent-path segments are not allowed.", nameof(name));
                }
            }

            return name;
        }

        private static long FindEndRecord(Stream input)
        {
            const int minimumEndSize = 22;

            if (input.Length < minimumEndSize)
                throw new Exception("Not a ZIP archive.");

            int tailLength = checked((int)Math.Min(
                input.Length,
                minimumEndSize + (long)ushort.MaxValue));

            long tailStart = input.Length - tailLength;
            input.Position = tailStart;

            byte[] tail = ReadExactly(input, tailLength);

            for (int i = tail.Length - minimumEndSize; i >= 0; i--)
            {
                if (tail[i] == 0x50 &&
                    tail[i + 1] == 0x4B &&
                    tail[i + 2] == 0x05 &&
                    tail[i + 3] == 0x06)
                {
                    int commentLength =
                        tail[i + 20] | (tail[i + 21] << 8);

                    if (i + minimumEndSize + commentLength == tail.Length)
                        return tailStart + i;
                }
            }

            throw new Exception(
                "End-of-central-directory record not found.");
        }

        private static byte[] ReadExactly(Stream input, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);

                if (read == 0)
                    throw new Exception();

                offset += read;
            }

            return buffer;
        }

        private static void Skip(Stream input, long count)
        {
            if (count < 0 || input.Position > input.Length - count)
                throw new Exception();

            input.Position += count;
        }

        private static uint ToUInt32(long value, string message)
        {
            if (value < 0 || value > uint.MaxValue)
                throw new NotSupportedException(message);

            return (uint)value;
        }

        private static uint CalculateCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;

            foreach (byte value in data)
            {
                crc ^= value;

                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^
                          ((crc & 1) != 0 ? 0xEDB88320u : 0u);
                }
            }

            return ~crc;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        public static void AddEntry(
            BinaryWriter archive,
            string name,
            byte[] data,
            bool replaceExisting = false)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            AddEntries(
                archive,
                new[] { new Entry(name, data) },
                replaceExisting);
        }

        public static void AddEntries(
            BinaryWriter archive,
            IEnumerable<Entry> entriesToAdd,
            bool replaceExisting = false)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));

            if (entriesToAdd == null)
                throw new ArgumentNullException(nameof(entriesToAdd));

            var stream = archive.BaseStream;
        
            ValidateUpdateStream(stream);

            var additions = NormalizeEntries(entriesToAdd);
            if (additions.Count == 0)
                return;

            List<Entry> entries = ReadEntriesForUpdate(stream);
            var addedNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < additions.Count; i++)
            {
                Entry addition = additions[i];
                bool duplicateInBatch = !addedNames.Add(addition.Name);

                if (duplicateInBatch && !replaceExisting)
                {
                    throw new InvalidOperationException(
                        "An entry named '" + addition.Name + "' already exists.");
                }
            }

            for (int i = 0; i < additions.Count; i++)
            {
                Entry addition = additions[i];
                bool found = false;

                // ZIP archives may technically contain duplicate names.
                // Remove all matching entries when replacing.
                for (int j = entries.Count - 1; j >= 0; j--)
                {
                    string existingName = NormalizeName(entries[j].Name);

                    if (string.Equals(
                            existingName,
                            addition.Name,
                            StringComparison.Ordinal))
                    {
                        found = true;

                        if (replaceExisting)
                            entries.RemoveAt(j);
                    }
                }

                if (found && !replaceExisting)
                {
                    throw new InvalidOperationException(
                        "An entry named '" + addition.Name + "' already exists.");
                }

                entries.Add(addition);
            }

            RewriteArchive(archive, entries);
        }

        public static bool RemoveEntry(BinaryWriter archive, string name)
        {
            return RemoveEntries(archive, new[] { name }) > 0;
        }

        public static int RemoveEntries(BinaryWriter archive, IEnumerable<string> names)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));

            if (names == null)
                throw new ArgumentNullException(nameof(names));

            var stream = archive.BaseStream;
        
            ValidateUpdateStream(stream);

            var normalizedNames = NormalizeNames(names);
            if (normalizedNames.Count == 0)
                return 0;

            List<Entry> entries = ReadEntriesForUpdate(stream);

            int removed = 0;

            // Remove every entry with any exact, case-sensitive ZIP path.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                string existingName = NormalizeName(entries[i].Name);

                if (normalizedNames.Contains(existingName))
                {
                    entries.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
                RewriteArchive(archive, entries);

            return removed;
        }

        private static List<Entry> NormalizeEntries(IEnumerable<Entry> entries)
        {
            var result = new List<Entry>();

            foreach (Entry entry in entries)
            {
                if (entry == null)
                    throw new ArgumentNullException(nameof(entries));

                result.Add(new Entry(
                    NormalizeName(entry.Name),
                    entry.Data,
                    entry.DosTime,
                    entry.DosDate));
            }

            return result;
        }

        private static HashSet<string> NormalizeNames(IEnumerable<string> names)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach (string name in names)
                result.Add(NormalizeName(name));

            return result;
        }

        private static List<Entry> ReadEntriesForUpdate(Stream archive)
        {
            if (archive.Length == 0)
                return new List<Entry>();

            archive.Position = 0;
            return Read(archive);
        }

        private static void RewriteArchive(BinaryWriter archive, List<Entry> entries)
        {
            // Build the new archive before modifying the original one.
            // This prevents read/write overlap on the same stream.
            using (var replacement = MyAPIGateway.Utilities.WriteBinaryFileInGlobalStorage("_tmp.zip"))
            {
                var tempStream = replacement.BaseStream;
                var stream = archive.BaseStream;
                Write(tempStream, entries);

                stream.Position = 0;
                stream.SetLength(0);

                tempStream.Position = 0;
                tempStream.CopyTo(stream);

                archive.Flush();
                stream.Position = 0;
            }
        }

        private static void ValidateUpdateStream(Stream archive)
        {
            if (!archive.CanRead ||
                !archive.CanWrite ||
                !archive.CanSeek)
            {
                throw new ArgumentException(
                    "Archive stream must be readable, writable, and seekable.",
                    nameof(archive));
            }
        }
    }
}
