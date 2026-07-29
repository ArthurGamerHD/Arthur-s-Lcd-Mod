using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using LcdMod.Common.Zip;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class MinimalZipTests
{
    const string HelloWorld = "hello world";

    [Fact]
    public void Write_CreatesTextFileReadableByZipDependency()
    {
        using var archive = new MemoryStream();

        MinimalZip.Write(archive, new[]
        {
            new MinimalZip.Entry("hello.txt", Encoding.UTF8.GetBytes(HelloWorld))
        });

        var entries = ReadWithSharpZipLib(archive);

        Assert.Single(entries);
        Assert.Equal(HelloWorld, Encoding.UTF8.GetString(entries["hello.txt"]));
    }

    [Fact]
    public void WriteBytes_CreatesTextFileReadableByZipDependency()
    {
        byte[] archive = MinimalZip.WriteBytes(new[]
        {
            new MinimalZip.Entry("hello.txt", Encoding.UTF8.GetBytes(HelloWorld))
        });

        var entries = ReadWithSharpZipLib(archive);

        Assert.Single(entries);
        Assert.Equal(HelloWorld, Encoding.UTF8.GetString(entries["hello.txt"]));
    }

    [Fact]
    public void AddEntry_AddsTextFileToExistingArchive()
    {
        using var archive = CreateArchive(
            ("existing.txt", "keep me"));

        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            MinimalZip.AddEntry(
                writer,
                "hello.txt",
                Encoding.UTF8.GetBytes(HelloWorld));
        }

        var entries = ReadWithSharpZipLib(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("keep me", Encoding.UTF8.GetString(entries["existing.txt"]));
        Assert.Equal(HelloWorld, Encoding.UTF8.GetString(entries["hello.txt"]));
    }

    [Fact]
    public void AddEntries_AddsTextFilesToExistingArchive()
    {
        using var archive = CreateArchive(
            ("existing.txt", "keep me"));

        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            MinimalZip.AddEntries(
                writer,
                new[]
                {
                    new MinimalZip.Entry("hello.txt", Encoding.UTF8.GetBytes(HelloWorld)),
                    new MinimalZip.Entry("folder/goodbye.txt", Encoding.UTF8.GetBytes("goodbye"))
                });
        }

        var entries = ReadWithSharpZipLib(archive);

        Assert.Equal(3, entries.Count);
        Assert.Equal("keep me", Encoding.UTF8.GetString(entries["existing.txt"]));
        Assert.Equal(HelloWorld, Encoding.UTF8.GetString(entries["hello.txt"]));
        Assert.Equal("goodbye", Encoding.UTF8.GetString(entries["folder/goodbye.txt"]));
    }

    [Fact]
    public void AddEntries_WithReplaceExisting_ReplacesExistingAndBatchDuplicates()
    {
        using var archive = CreateArchive(
            ("existing.txt", "old"),
            ("keep.txt", "keep me"));

        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            MinimalZip.AddEntries(
                writer,
                new[]
                {
                    new MinimalZip.Entry("existing.txt", Encoding.UTF8.GetBytes("new")),
                    new MinimalZip.Entry("existing.txt", Encoding.UTF8.GetBytes("newer"))
                },
                replaceExisting: true);
        }

        var entries = ReadWithSharpZipLib(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("newer", Encoding.UTF8.GetString(entries["existing.txt"]));
        Assert.Equal("keep me", Encoding.UTF8.GetString(entries["keep.txt"]));
    }

    [Fact]
    public void AddEntry_PreservesExistingEntryCreationTimeWhenRewritingArchive()
    {
        var creationTime = new DateTime(2021, 4, 5, 6, 7, 8, DateTimeKind.Local);
        using var archive = new MemoryStream();

        MinimalZip.Write(archive, new[]
        {
            new MinimalZip.Entry(
                "existing.txt",
                Encoding.UTF8.GetBytes("keep me"),
                creationTime)
        });

        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            MinimalZip.AddEntry(
                writer,
                "hello.txt",
                Encoding.UTF8.GetBytes(HelloWorld));
        }

        Assert.Equal(creationTime, ReadCreationTimeWithSharpZipLib(archive, "existing.txt"));
    }

    [Fact]
    public void RemoveEntry_RemovesTextFileFromExistingArchive()
    {
        using var archive = CreateArchive(
            ("hello.txt", HelloWorld),
            ("existing.txt", "keep me"));

        bool removed;
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            removed = MinimalZip.RemoveEntry(writer, "hello.txt");
        }

        var entries = ReadWithSharpZipLib(archive);

        Assert.True(removed);
        Assert.Single(entries);
        Assert.False(entries.ContainsKey("hello.txt"));
        Assert.Equal("keep me", Encoding.UTF8.GetString(entries["existing.txt"]));
    }

    [Fact]
    public void RemoveEntries_RemovesTextFilesFromExistingArchive()
    {
        using var archive = CreateArchive(
            ("hello.txt", HelloWorld),
            ("goodbye.txt", "goodbye"),
            ("existing.txt", "keep me"));

        int removed;
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            removed = MinimalZip.RemoveEntries(
                writer,
                new[] { "hello.txt", "missing.txt", "goodbye.txt" });
        }

        var entries = ReadWithSharpZipLib(archive);

        Assert.Equal(2, removed);
        Assert.Single(entries);
        Assert.False(entries.ContainsKey("hello.txt"));
        Assert.False(entries.ContainsKey("goodbye.txt"));
        Assert.Equal("keep me", Encoding.UTF8.GetString(entries["existing.txt"]));
    }

    [Fact]
    public void ContainsEntry_FindsEntryFromCentralDirectory()
    {
        using var archive = CreateArchive(
            ("hello.txt", HelloWorld),
            ("folder/goodbye.txt", "goodbye"));

        Assert.True(MinimalZip.ContainsEntry(archive, "hello.txt"));
        Assert.True(MinimalZip.ContainsEntry(archive, "FOLDER/goodbye.txt", ignoreCase: true));
        Assert.False(MinimalZip.ContainsEntry(archive, "missing.txt"));
    }

    static MemoryStream CreateArchive(params (string Name, string Text)[] source)
    {
        var archive = new MemoryStream();
        MinimalZip.Write(
            archive,
            source.Select(entry =>
                new MinimalZip.Entry(entry.Name, Encoding.UTF8.GetBytes(entry.Text))));
        archive.Position = 0;
        return archive;
    }

    static Dictionary<string, byte[]> ReadWithSharpZipLib(MemoryStream archive)
    {
        return ReadWithSharpZipLib(archive.ToArray());
    }

    static Dictionary<string, byte[]> ReadWithSharpZipLib(byte[] archiveBytes)
    {
        using var zip = new ZipFile(new MemoryStream(archiveBytes));
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (ZipEntry entry in zip)
        {
            if (!entry.IsFile)
                continue;

            using var input = zip.GetInputStream(entry);
            using var output = new MemoryStream();
            input.CopyTo(output);
            entries.Add(entry.Name, output.ToArray());
        }

        return entries;
    }

    static DateTime ReadCreationTimeWithSharpZipLib(MemoryStream archive, string name)
    {
        using var zip = new ZipFile(new MemoryStream(archive.ToArray()));
        var entry = zip.GetEntry(name);

        Assert.NotNull(entry);

        return entry.DateTime;
    }
}
