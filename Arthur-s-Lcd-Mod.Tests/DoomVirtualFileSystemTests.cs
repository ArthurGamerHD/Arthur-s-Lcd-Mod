using System.Text;
using ManagedDoom.SE;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class DoomVirtualFileSystemTests
{
    [Fact]
    public void SaveSlotsRoundTripThroughZipAndCanBeOverwritten()
    {
        var files = new DoomVirtualFileSystem();
        files.Load(null);
        files.WriteAllBytes("profile\\doomsav0.dsg", Encoding.ASCII.GetBytes("FIRST"));
        files.WriteAllBytes("profile/doomsav1.dsg", Encoding.ASCII.GetBytes("SECOND"));

        Assert.True(files.TryCreateArchive(out var archive, out var revision));
        files.MarkSaved(revision);
        Assert.False(files.TryCreateArchive(out _, out _));

        var restored = new DoomVirtualFileSystem();
        using (var stream = new MemoryStream(archive))
            restored.Load(stream);
        Assert.True(restored.Exists("profile/doomsav0.dsg"));
        Assert.Equal("FIRST", Encoding.ASCII.GetString(restored.ReadAllBytes("profile/doomsav0.dsg")));
        Assert.Equal("SECOND", Encoding.ASCII.GetString(restored.ReadAllBytes("profile/doomsav1.dsg")));

        restored.WriteAllBytes("profile/doomsav0.dsg", Encoding.ASCII.GetBytes("REPLACED"));
        Assert.True(restored.TryCreateArchive(out archive, out _));
        var again = new DoomVirtualFileSystem();
        using (var stream = new MemoryStream(archive))
            again.Load(stream);
        Assert.Equal("REPLACED", Encoding.ASCII.GetString(again.ReadAllBytes("profile/doomsav0.dsg")));
        Assert.Equal("SECOND", Encoding.ASCII.GetString(again.ReadAllBytes("profile/doomsav1.dsg")));
    }

    [Fact]
    public void WritesDuringFlushRemainPendingAndFilesAreCopied()
    {
        var files = new DoomVirtualFileSystem();
        files.Load(null);
        var input = Encoding.ASCII.GetBytes("old");
        files.WriteAllBytes("profile/doomsav0.dsg", input);
        input[0] = (byte)'X';

        Assert.True(files.TryCreateArchive(out _, out var firstRevision));
        files.WriteAllBytes("profile/doomsav1.dsg", Encoding.ASCII.GetBytes("new"));
        files.MarkSaved(firstRevision);
        Assert.True(files.TryCreateArchive(out _, out _));

        var read = files.ReadAllBytes("profile/doomsav0.dsg");
        Assert.Equal("old", Encoding.ASCII.GetString(read));
        read[0] = (byte)'Y';
        Assert.Equal("old", Encoding.ASCII.GetString(files.ReadAllBytes("profile/doomsav0.dsg")));
    }

    [Fact]
    public void PathsCannotEscapeTheVirtualProfile()
    {
        var files = new DoomVirtualFileSystem();
        files.Load(null);
        Assert.False(files.Exists("../doomsav0.dsg"));
        Assert.Throws<ArgumentException>(() => files.WriteAllBytes("profile/../outside", new byte[1]));
        Assert.Throws<ArgumentException>(() => files.ReadAllBytes("/absolute/path"));
    }
}
