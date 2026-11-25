using System.Text;
using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-323: the per-user trust list file (project-trust.md CMP-1230…1236), over the in-memory file system.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TrustStoreTests
{
    private const string TrustFile = @"C:\user\roaming\trust.json";
    private const string HashA = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static TrustEntry Folder(string path) => new(TrustEntryKind.Folder, path, null);

    private static TrustEntry Project(string path, string? hash) => new(TrustEntryKind.Project, path, hash);

    [Fact]
    [Trait("Req", "CMP-1230")]
    public void DefaultPath_FollowsTheUserDirectoryRule()
    {
        var redirected = new Dictionary<string, string?> { ["FREEXCOMP_USER_DIR"] = @"C:\u", ["APPDATA"] = @"C:\Users\x\AppData\Roaming" };
        var plain = new Dictionary<string, string?> { ["FREEXCOMP_USER_DIR"] = "", ["APPDATA"] = @"C:\Users\x\AppData\Roaming" };
        var unset = new Dictionary<string, string?> { ["APPDATA"] = @"C:\Users\x\AppData\Roaming" };

        Assert.Equal(@"C:\u\roaming\trust.json", ProjectTrust.DefaultStorePath(redirected.GetValueOrDefault));
        Assert.Equal(@"C:\Users\x\AppData\Roaming\FreexComp\trust.json", ProjectTrust.DefaultStorePath(plain.GetValueOrDefault));
        Assert.Equal(@"C:\Users\x\AppData\Roaming\FreexComp\trust.json", ProjectTrust.DefaultStorePath(unset.GetValueOrDefault));
    }

    [Fact]
    [Trait("Req", "CMP-1230")]
    [Trait("Req", "CMP-1232")]
    public void MissingFileIsEmptyAndIsNotCreated()
    {
        var fs = new InMemoryFileSystem();
        var store = new FileTrustStore(fs, TrustFile);

        var snapshot = store.Read();

        Assert.Equal(TrustFile, store.FilePath);
        Assert.Empty(snapshot.Entries);
        Assert.Null(snapshot.ReadProblem);
        Assert.False(store.Remove(Folder(@"C:\Work")));
        Assert.False(fs.FileExists(TrustFile));
        Assert.Empty(fs.EnumerateFiles(@"C:\user\roaming", "*"));
    }

    [Fact]
    [Trait("Req", "CMP-1231")]
    [Trait("Req", "CMP-1234")]
    [Trait("Req", "CMP-1236")]
    public void WriteThenRead_RoundTripsInCanonicalForm()
    {
        var fs = new InMemoryFileSystem();
        fs.Add(TrustFile, $$"""
            { "note": "keep", "formatVersion": 1, "folders": ["C:\\Work"],
              "projects": [ { "since": 5, "path": "C:\\p\\Hello.pproj", "commands": "{{HashA}}" } ] }
            """);
        var store = new FileTrustStore(fs, TrustFile);

        store.TrustFolder(@"c:\work\");                         // equal to the stored one (case, trailing separator): not added
        store.TrustFolder(@"D:\");
        store.TrustFolder(@"C:\Other\Sub\..");
        store.TrustProject(@"C:\P\HELLO.pproj", HashB);         // the existing entry of that path: hash replaced
        store.TrustProject(@"C:\q\Lib.pproj", HashA);
        store.TrustProject(@"C:\q\Lib.pproj", HashA);

        var bytes = fs.ReadAllBytes(TrustFile);
        Assert.Equal((byte)'{', bytes[0]);                      // no byte-order mark
        Assert.Equal(
            $$"""
            {
              "formatVersion": 1,
              "folders": [
                "C:\\Work",
                "D:\\",
                "C:\\Other"
              ],
              "projects": [
                {
                  "path": "C:\\p\\Hello.pproj",
                  "commands": "{{HashB}}",
                  "since": 5
                },
                {
                  "path": "C:\\q\\Lib.pproj",
                  "commands": "{{HashA}}"
                }
              ],
              "note": "keep"
            }

            """.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(bytes));
        var snapshot = store.Read();
        Assert.Null(snapshot.ReadProblem);
        Assert.Equal(
            [Folder(@"C:\Work"), Folder(@"D:\"), Folder(@"C:\Other"), Project(@"C:\p\Hello.pproj", HashB), Project(@"C:\q\Lib.pproj", HashA)],
            snapshot.Entries);
        Assert.Equal([TrustFile], fs.EnumerateFiles(@"C:\user\roaming", "*"));        // no temporary file is left
    }

    [Theory]
    [Trait("Req", "CMP-1232")]
    [InlineData("{ \"formatVersion\": 1, \"folders\": [", "invalid JSON")]
    [InlineData("[]", "invalid entry")]
    [InlineData("""{ "formatVersion": 1, "folders": "C:\\Work", "projects": [] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": "1", "folders": ["C:\\Work"], "projects": [] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": 2, "folders": ["C:\\Work"], "projects": [] }""", "unsupported format version 2")]
    [InlineData("""{ "formatVersion": 1, "folders": ["C:\\Work", "Work"], "projects": [] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": 1, "folders": ["C:\\Work"], "projects": [{ "path": "p\\Hello.pproj", "commands": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": 1, "folders": ["C:\\Work"], "projects": [{ "path": "C:\\p\\Hello.pproj", "commands": "sha256:AAAAaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": 1, "folders": ["C:\\Work"], "projects": [{ "path": "C:\\p\\Hello.pproj", "commands": "md5:aaaa" }] }""", "invalid entry")]
    [InlineData("""{ "formatVersion": 1, "folders": ["C:\\Work"], "projects": [{ "path": "C:\\p\\Hello.pproj" }] }""", "invalid entry")]
    public void UnreadableFileTrustsNothingAndNamesTheReason(string text, string reason)
    {
        var fs = new InMemoryFileSystem();
        fs.Add(TrustFile, text);
        var store = new FileTrustStore(fs, TrustFile);

        var snapshot = store.Read();

        Assert.Empty(snapshot.Entries);                         // the valid folder entry next to the bad one is not trusted either
        Assert.Equal(reason, snapshot.ReadProblem);
        Assert.Equal(text, fs.ReadAllText(TrustFile));          // a read never rewrites

        store.TrustFolder(@"C:\New");

        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"folders\": [\n    \"C:\\\\New\"\n  ],\n  \"projects\": []\n}\n", fs.ReadAllText(TrustFile));
        var after = store.Read();
        Assert.Equal([Folder(@"C:\New")], after.Entries);
        Assert.Null(after.ReadProblem);
    }

    [Fact]
    [Trait("Req", "CMP-1232")]
    [Trait("Req", "CMP-1234")]
    public void FileSystemFailure_IsTheReadProblem_AndAFailedWriteThrowsIOException()
    {
        var store = new FileTrustStore(new FailingFileSystem(), TrustFile);

        var snapshot = store.Read();

        Assert.Empty(snapshot.Entries);
        Assert.Equal("the file is locked", snapshot.ReadProblem);
        Assert.Throws<IOException>(() => store.TrustFolder(@"C:\Work"));
    }

    [Fact]
    [Trait("Req", "CMP-1236")]
    public void RemoveDeletesExactlyOneEntry()
    {
        var fs = new InMemoryFileSystem();
        var store = new FileTrustStore(fs, TrustFile);
        store.TrustFolder(@"C:\Work");
        store.TrustFolder(@"C:\Work\Sub");
        store.TrustProject(@"C:\Work\A.pproj", HashA);
        store.TrustProject(@"C:\Work\B.pproj", HashB);

        Assert.True(store.Remove(Folder(@"c:\WORK")));                      // path comparison of CMP-1235
        Assert.False(store.Remove(Folder(@"C:\Work")));                     // already gone
        Assert.False(store.Remove(Folder(@"C:\Work\A.pproj")));             // a project entry is not a folder entry
        Assert.True(store.Remove(Project(@"C:\Work\A.pproj", null)));

        Assert.Equal([Folder(@"C:\Work\Sub"), Project(@"C:\Work\B.pproj", HashB)], store.Read().Entries);
    }

    /// <summary>A file that exists and can be neither read nor replaced.</summary>
    private sealed class FailingFileSystem : IFileSystem
    {
        public bool FileExists(string path) => true;

        public string ReadAllText(string path) => throw new IOException("the file is locked");

        public byte[] ReadAllBytes(string path) => throw new IOException("the file is locked");

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => [];

        public IEnumerable<string> EnumerateDirectories(string directory) => [];

        public DateTime GetLastWriteTimeUtc(string path) => default;

        public long GetLength(string path) => 0;

        public void WriteAllBytes(string path, byte[] data) => throw new UnauthorizedAccessException("no access");

        public void Delete(string path)
        {
        }

        public void CreateDirectory(string path)
        {
        }
    }
}
