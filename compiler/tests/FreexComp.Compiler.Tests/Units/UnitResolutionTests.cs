using System.Text.Json;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-314 (api.md §1): <c>Compilation.ResolveUnitPath</c> maps a unit name to the file a uses entry of the main file would load —
/// LANG-510 search order, LANG-533 alias table, LANG-535 unit scope names — without parsing or binding any unit.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
[Trait("Req", "LANG-510")]
public sealed class UnitResolutionTests
{
    /// <summary>Counts what the compilation reads through the real file system.</summary>
    private sealed class RecordingFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner = PhysicalFileSystem.Instance;

        public List<string> Reads { get; } = [];

        public bool FileExists(string path) => _inner.FileExists(path);

        public string ReadAllText(string path)
        {
            Reads.Add(path);
            return _inner.ReadAllText(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            Reads.Add(path);
            return _inner.ReadAllBytes(path);
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => _inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => _inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);

        public long GetLength(string path) => _inner.GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => throw new InvalidOperationException("Resolving a unit writes nothing: " + path);

        public void Delete(string path) => throw new InvalidOperationException("Resolving a unit deletes nothing: " + path);

        public void CreateDirectory(string path) => throw new InvalidOperationException("Resolving a unit creates nothing: " + path);
    }

    /// <summary>The source directories of the libraries: the run-time library with its API units, the UI framework, data access.</summary>
    private static readonly string[] LibraryDirectories =
    [
        TestRtl.Directory,
        Path.Combine(RepoPaths.Root, "rtl", "winapi", "src"),
        Path.Combine(RepoPaths.Root, "ui-framework", "src"),
        Path.Combine(RepoPaths.Root, "data", "src"),
    ];

    private static (Compilation Compilation, RecordingFileSystem Files) Create()
    {
        var fs = new RecordingFileSystem();
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(RepoPaths.Root, "rtl", "$none", "P.pas"),       // no such directory: nothing but the libraries is searched
            RtlDirectory = TestRtl.Directory,
            SearchPaths = [.. LibraryDirectories.Skip(2)],
        };
        return (Compilation.Create(options, [], fs), fs);
    }

    [Fact]
    public void ResolveOnly_DoesNotBind()
    {
        var (compilation, fs) = Create();
        var sources = LibraryDirectories.SelectMany(d => Directory.GetFiles(d, "*.pas")).Order(StringComparer.Ordinal).ToList();
        Assert.True(sources.Count > 30, "the library sources were not found");

        // every library unit resolves to its source, whatever the case of the name
        foreach (var source in sources)
        {
            var name = Path.GetFileNameWithoutExtension(source);
            Assert.True(PathUtil.AreEqual(source, compilation.ResolveUnitPath(name) ?? ""), name);
            Assert.True(PathUtil.AreEqual(source, compilation.ResolveUnitPath(name.ToUpperInvariant()) ?? ""), name);
        }

        // every alias of the table (LANG-533) resolves to the file of its canonical unit
        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRtl.Directory, "unit-aliases.json")));
        var aliases = table.RootElement.GetProperty("aliases").EnumerateObject().ToList();
        Assert.NotEmpty(aliases);
        foreach (var alias in aliases)
        {
            var canonical = compilation.ResolveUnitPath(alias.Value.GetString()!);
            Assert.True(canonical is not null, alias.Value.GetString());
            Assert.Equal(canonical, compilation.ResolveUnitPath(alias.Name));
        }

        Assert.Null(compilation.ResolveUnitPath("NoSuchUnit"));
        Assert.Null(compilation.ResolveUnitPath("No.Such.Unit"));

        // nothing was analysed, and no source was read: only the alias table
        Assert.False(compilation.IsAnalyzed);
        Assert.All(fs.Reads, path => Assert.Equal("unit-aliases.json", Path.GetFileName(path)));

        // FindUnit keeps its behaviour: the same file, bound
        var system = compilation.FindUnit("System");
        Assert.NotNull(system);
        Assert.True(PathUtil.AreEqual(system.Path, compilation.ResolveUnitPath("system") ?? ""));
        Assert.True(compilation.IsAnalyzed);
        Assert.Contains(fs.Reads, path => Path.GetFileName(path) == "System.pas");
    }
}
