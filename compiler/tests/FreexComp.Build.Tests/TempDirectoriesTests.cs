namespace FreexComp.Build.Tests;

/// <summary>C-292: the shared temp-folder cleanup of this test project never fails a test.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TempDirectoriesTests
{
    private static string NewDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        File.WriteAllText(Path.Combine(dir, "bin", "Hello.exe"), "exe");
        return dir;
    }

    [Fact]
    public void Delete_RemovesTheFolder_AndAMissingFolderIsFine()
    {
        var dir = NewDirectory();

        Assert.True(TempDirectories.Delete(dir));
        Assert.False(Directory.Exists(dir));
        Assert.True(TempDirectories.Delete(dir));
    }

    /// <summary>The give-up path: a file stays open, every attempt fails, the helper waits between attempts, returns false and throws nothing.</summary>
    [Fact]
    public void Delete_GivesUpOnAFileThatStaysOpen_WithoutThrowing()
    {
        var dir = NewDirectory();
        var waits = new List<int>();
        bool removed;
        using (new FileStream(Path.Combine(dir, "bin", "Hello.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            removed = TempDirectories.Delete(dir, attempts: 3, waits.Add);
        }

        Assert.False(removed);
        Assert.Equal([1, 2], waits);
        Assert.True(File.Exists(Path.Combine(dir, "bin", "Hello.exe")));
        Assert.True(TempDirectories.Delete(dir));                               // the handle is closed: now it goes
        Assert.False(Directory.Exists(dir));
    }

    /// <summary>A handle released while the helper waits: the retry succeeds.</summary>
    [Fact]
    public void Delete_RetriesUntilTheHandleIsReleased()
    {
        var dir = NewDirectory();
        var held = new FileStream(Path.Combine(dir, "bin", "Hello.exe"), FileMode.Open, FileAccess.Read, FileShare.None);
        var waits = 0;

        var removed = TempDirectories.Delete(dir, attempts: 3, _ =>
        {
            waits++;
            held.Dispose();
        });

        Assert.True(removed);
        Assert.Equal(1, waits);
        Assert.False(Directory.Exists(dir));
    }
}
