using System.Text;

namespace FreexComp.Compiler.Tests;

/// <summary>api.md §1 IFileSystem implementations.</summary>
[Trait("Phase", "1")]
public sealed class FileSystemTests
{
    [Fact]
    [Trait("Req", "CMP-205")]
    public void InMemory_PathsAreNormalisedAndCaseInsensitive()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\A.pas", "x");

        Assert.True(fs.FileExists("c:/P/./a.PAS"));
        Assert.Equal("x", fs.ReadAllText(@"C:\P\sub\..\a.pas"));
        Assert.Equal(1, fs.GetLength(@"C:\p\A.pas"));
        Assert.False(fs.FileExists(@"C:\p\B.pas"));
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllText(@"C:\p\B.pas"));
    }

    [Fact]
    public void InMemory_ReadAllText_StripsBom()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(@"C:\p\a.pas", [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        Assert.Equal("hi", fs.ReadAllText(@"C:\p\a.pas"));
        Assert.Equal(5, fs.ReadAllBytes(@"C:\p\a.pas").Length);
    }

    [Fact]
    public void InMemory_Enumerate_IsNonRecursiveAndMatchesPattern()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\b.pas", "")
            .Add(@"C:\p\A.pas", "")
            .Add(@"C:\p\notes.md", "")
            .Add(@"C:\p\sub\c.pas", "")
            .Add(@"C:\p\sub\deeper\d.pas", "");

        Assert.Equal([@"C:\p\A.pas", @"C:\p\b.pas"], fs.EnumerateFiles(@"C:\p", "*.pas"));
        Assert.Equal([@"C:\p\notes.md"], fs.EnumerateFiles(@"C:\p\", "?otes.*"));
        Assert.Equal([@"C:\p\sub"], fs.EnumerateDirectories(@"C:\p"));
        Assert.Empty(fs.EnumerateFiles(@"C:\none", "*"));
    }

    [Fact]
    public void InMemory_WriteTimesAreDeterministicAndIncreasing()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\a", "1");
        var first = fs.GetLastWriteTimeUtc(@"C:\p\a");
        fs.Add(@"C:\p\a", "2");

        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 1, DateTimeKind.Utc), first);
        Assert.True(fs.GetLastWriteTimeUtc(@"C:\p\a") > first);
        fs.Delete(@"C:\p\a");
        Assert.False(fs.FileExists(@"C:\p\a"));
        fs.Delete(@"C:\p\a");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Physical_WriteAllBytes_CreatesDirectoriesAndLeavesNoTempFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var fs = PhysicalFileSystem.Instance;
        var file = Path.Combine(root, "sub", "a.txt");

        fs.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("ä")]);
        fs.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("ü")]);

        Assert.Equal("ü", fs.ReadAllText(file));
        Assert.Equal([file], fs.EnumerateFiles(Path.Combine(root, "sub"), "*"));
        Assert.Equal(5, fs.GetLength(file));
        fs.Delete(file);
        Assert.False(fs.FileExists(file));
        Directory.Delete(root, recursive: true);
    }
}
