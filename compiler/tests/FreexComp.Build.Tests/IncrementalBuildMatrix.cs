using System.Collections.Concurrent;
using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-148: every row of build-engine.md §5 (what makes a build do work), with the pinned toolchain on disk. Each case builds a fresh
/// project once, applies one change and checks which units are recompiled (<c>Compiling</c> Info lines), whether it links and which
/// processes start.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class IncrementalBuildMatrix
{
    [Fact]
    [Trait("Req", "CMP-830")]
    public async Task Nothing_Changed_StartsNoProcess_AndIsUpToDate()
    {
        using var p = await MatrixProject.CreateBuiltAsync();

        var b = await p.BuildAsync();

        Assert.True(b.Result.UpToDate);
        Assert.Equal(["Matrix: up to date"], b.Log.Info);
        Assert.Empty(b.Processes);
    }

    [Fact]
    public async Task UnitImplementation_Edited_OnlyThatUnitIsRecompiled_AndRelinked()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Base.pas", "Result := 1;", "Result := 2;");

        var b = await p.BuildAsync();

        b.AssertWork(["Base"], link: true);
    }

    [Fact]
    public async Task UnitInterface_Edited_ItAndUnitsThatRecordedItsHash_NotFurther()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Base.pas", "interface\n", "interface\nfunction Extra: Integer;\n");
        p.Edit("Base.pas", "implementation\n", "implementation\nfunction Extra: Integer; begin Result := 3 end;\n");

        var b = await p.BuildAsync();

        // Mid uses Base (its interface hash of Base differs); the program uses Mid, whose own interface did not change
        b.AssertWork(["Base", "Mid"], link: true);
    }

    [Fact]
    public async Task IncludeFile_Edited_OwningUnitIsRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Inc.inc", "41", "42");

        var b = await p.BuildAsync();

        b.AssertWork(["Inc"], link: true);
    }

    [Fact]
    public async Task FormFile_Edited_OwningUnitIsRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("UMain.form", "'Hi'", "'Ho'");

        var b = await p.BuildAsync();

        b.AssertWork(["UMain"], link: true);
    }

    [Fact]
    public async Task Define_Changed_AllUnitsAreRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Matrix.pproj", "\"defines\": []", "\"defines\": [\"EXTRA\"]");

        var b = await p.BuildAsync();

        b.AssertWork(MatrixProject.AllUnits, link: true);
    }

    [Fact]
    public async Task DebugInfo_Changed_AllUnitsAreRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Matrix.pproj", "\"debugInfo\": true", "\"debugInfo\": false");

        var b = await p.BuildAsync();

        b.AssertWork(MatrixProject.AllUnits, link: true);
    }

    [Fact]
    public async Task OtherConfiguration_UsesItsOwnObjectDirectory_ThenTheFirstIsStillUpToDate()
    {
        using var p = await MatrixProject.CreateBuiltAsync();

        var release = await p.BuildAsync("Release");
        var debug = await p.BuildAsync("Debug");

        release.AssertWork(MatrixProject.AllUnits, link: true);
        Assert.True(Directory.Exists(Path.Combine(p.Directory, "obj", "Release")));
        Assert.True(debug.Result.UpToDate);
        Assert.Empty(debug.Processes);
    }

    [Fact]
    public async Task SearchPath_Changed_UnitResolvesToAnotherFile_ItAndItsDependantsAreRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Matrix.pproj", "\"searchPaths\": [\"lib1\"]", "\"searchPaths\": [\"lib2\"]");

        var b = await p.BuildAsync();

        b.AssertWork(["Alt", "Matrix"], link: true);
    }

    [Fact]
    public async Task DefFile_Edited_ImportLibraryRegenerated_AndRelinked_NothingCompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("mylib.def", "MyFunc", "MyFunc\n  MyOther");

        var b = await p.BuildAsync();

        b.AssertWork([], link: true);
        Assert.Contains("llvm-dlltool.exe", b.Processes);
    }

    [Fact]
    public async Task CompilerUpdated_EverythingIsRecompiled()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        foreach (var pcu in Directory.GetFiles(Path.Combine(p.Directory, "obj", "Debug"), "*.pcu"))
        {
            var text = File.ReadAllText(pcu);
            Assert.Contains(PcuFingerprintPrefix, text, StringComparison.Ordinal);
            File.WriteAllText(pcu, text.Replace(PcuFingerprintPrefix, PcuFingerprintPrefix + "older ", StringComparison.Ordinal));     // an older compiler wrote it
        }

        var b = await p.BuildAsync();

        b.AssertWork(MatrixProject.AllUnits, link: true);
    }

    [Fact]
    public async Task OutputExe_Deleted_RelinkOnly()
    {
        using var p = await MatrixProject.CreateBuiltAsync();
        File.Delete(p.Exe);

        var b = await p.BuildAsync();

        b.AssertWork([], link: true);
        Assert.Equal(["lld-link.exe"], b.Processes.Where(n => n != "clang.exe --version"));
        Assert.True(File.Exists(p.Exe));
    }

    // ---------------------------------------------------------------- resource inputs (resource-compiler.md §5; build-engine.md §5)

    /// <summary>
    /// The matrix project with two scripts (<c>app.rc</c> includes <c>ids.h</c> and names <c>data.bin</c>; <c>other.rc</c>) and a
    /// compiled resource file <c>pre.res</c>, built once more: both scripts compiled, relinked.
    /// </summary>
    private static async Task<MatrixProject> CreateBuiltWithResourcesAsync()
    {
        var p = await MatrixProject.CreateBuiltAsync();
        p.Edit("Matrix.pproj", "\"defines\": [],\n  \"configurations\"", "\"defines\": [],\n  \"files\": [ { \"path\": \"res/app.rc\" }, { \"path\": \"other.rc\" }, { \"path\": \"pre.res\" } ],\n  \"configurations\"");
        p.Write(@"res\app.rc", "#include \"ids.h\"\n// the application's data\nID_DATA RCDATA \"data.bin\"\n");
        p.Write(@"res\ids.h", "#define ID_DATA 7\n");
        p.Write(@"res\data.bin", "first");
        p.Write("other.rc", "1 RCDATA BEGIN 1, 2 END\n");
        File.WriteAllBytes(Path.Combine(p.Directory, "pre.res"), FakeResourceTools.File((10, 99, [1, 2, 3])));

        var b = await p.BuildAsync();

        AssertResourceWork(b, ["app.rc", "other.rc"], link: true);
        return p;
    }

    /// <summary>No unit recompiled; exactly <paramref name="scripts"/> compiled (two processes each), linked or not.</summary>
    private static void AssertResourceWork(MatrixBuild b, string[] scripts, bool link)
    {
        Assert.True(b.Result.Success, string.Join("\n", b.Log.Lines));
        Assert.Equal(
            [.. scripts.Select(s => "Compiling resources " + s), .. link ? new[] { "Linking Matrix.exe" } : []],
            b.Log.Info.Where(l => l.StartsWith("Compiling ", StringComparison.Ordinal) || l.StartsWith("Linking ", StringComparison.Ordinal)));
        Assert.Equal(
            [.. scripts.SelectMany(_ => new[] { "clang.exe", "llvm-rc.exe" }), .. link ? new[] { "lld-link.exe" } : []],
            b.Processes.Where(n => n != "clang.exe --version"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1317")]
    [Trait("Req", "CMP-1318")]
    public async Task Resource_NothingChanged_StartsNoProcess()
    {
        using var p = await CreateBuiltWithResourcesAsync();

        var b = await p.BuildAsync();

        Assert.True(b.Result.UpToDate);
        Assert.Equal(["Matrix: up to date"], b.Log.Info);
        Assert.Empty(b.Processes);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1307")]
    [Trait("Req", "CMP-1317")]
    public async Task Resource_ScriptIncludeOrDataFileEdited_RecompilesThatScriptAndRelinks()
    {
        using var p = await CreateBuiltWithResourcesAsync();

        p.Edit(@"res\app.rc", "ID_DATA RCDATA", "8 RCDATA \"data.bin\"\nID_DATA RCDATA");          // the script
        AssertResourceWork(await p.BuildAsync(), ["app.rc"], link: true);

        p.Edit(@"res\ids.h", "7", "70");                                                          // a file it includes
        AssertResourceWork(await p.BuildAsync(), ["app.rc"], link: true);

        p.Edit(@"res\data.bin", "first", "second");                                               // a data file it names
        AssertResourceWork(await p.BuildAsync(), ["app.rc"], link: true);

        // CMP-1318: compiled again, but the same bytes come out — no link
        p.Edit(@"res\app.rc", "// the application's data", "// only this comment changed");
        AssertResourceWork(await p.BuildAsync(), ["app.rc"], link: false);
        Assert.Empty((await p.BuildAsync()).Processes);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1318")]
    public async Task Resource_ResContentChanged_RelinksOnly()
    {
        using var p = await CreateBuiltWithResourcesAsync();
        File.WriteAllBytes(Path.Combine(p.Directory, "pre.res"), FakeResourceTools.File((10, 99, [4, 5, 6])));

        var b = await p.BuildAsync();

        AssertResourceWork(b, [], link: true);
    }

    /// <summary>How the compiler fingerprint starts in a <c>.pcu</c> (<c>"compilerFingerprint":"opc …"</c>).</summary>
    private const string PcuFingerprintPrefix = "\"compilerFingerprint\":\"opc ";
}

/// <summary>
/// A console project on disk: program <c>Matrix</c> uses <c>Mid</c> (which uses <c>Base</c> in its implementation), <c>Inc</c>
/// (an include file), <c>UMain</c> (a <c>{$FORM}</c> file), <c>Alt</c> (found through <c>searchPaths</c>) and <c>Ext</c> (an
/// <c>external</c> routine of <c>mylib.dll</c> with <c>mylib.def</c> beside it).
/// </summary>
internal sealed class MatrixProject : IDisposable
{
    public static readonly ImmutableArray<string> AllUnits = ["Alt", "Base", "Ext", "Inc", "Matrix", "Mid", "System", "UMain"];

    private MatrixProject(string directory) => Directory = directory;

    public string Directory { get; }

    public string Exe => Path.Combine(Directory, "bin", "Debug", "Matrix.exe");

    public static async Task<MatrixProject> CreateBuiltAsync()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var p = new MatrixProject(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        System.IO.Directory.CreateDirectory(p.Directory);
        p.Write("Matrix.pproj", """
            {
              "formatVersion": 1,
              "name": "Matrix",
              "mainSource": "Matrix.pas",
              "searchPaths": ["lib1"],
              "defines": [],
              "configurations": {
                "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" }
              }
            }
            """);
        p.Write("Matrix.pas", "program Matrix;\nuses Mid, Inc, UMain, Alt, Ext;\nbegin\n  Writeln(MidValue, IncValue, AltValue)\nend.\n");
        p.Write("Base.pas", "unit Base;\ninterface\nfunction BaseValue: Integer;\nimplementation\nfunction BaseValue: Integer;\nbegin\n  Result := 1;\nend;\nend.\n");
        p.Write("Mid.pas", "unit Mid;\ninterface\nfunction MidValue: Integer;\nimplementation\nuses Base;\nfunction MidValue: Integer;\nbegin\n  Result := BaseValue + 1;\nend;\nend.\n");
        p.Write("Inc.pas", "unit Inc;\ninterface\nfunction IncValue: Integer;\nimplementation\nfunction IncValue: Integer;\nbegin\n  Result := {$I Inc.inc};\nend;\nend.\n");
        p.Write("Inc.inc", "41");
        p.Write(@"lib1\Alt.pas", "unit Alt;\ninterface\nfunction AltValue: Integer;\nimplementation\nfunction AltValue: Integer; begin Result := 1 end;\nend.\n");
        p.Write(@"lib2\Alt.pas", "unit Alt;\ninterface\nfunction AltValue: Integer;\nimplementation\nfunction AltValue: Integer; begin Result := 2 end;\nend.\n");
        p.Write("Ext.pas", "unit Ext;\ninterface\nprocedure MyFunc; external 'mylib.dll';\nimplementation\nend.\n");
        p.Write("mylib.def", "LIBRARY mylib.dll\nEXPORTS\n  MyFunc\n");
        var golden = Path.Combine(RepoPaths.Root, "tests", "compiler", "m2.5", "FormEmbed");
        File.Copy(Path.Combine(golden, "UMain.pas"), Path.Combine(p.Directory, "UMain.pas"));
        File.Copy(Path.Combine(golden, "UMain.form"), Path.Combine(p.Directory, "UMain.form"));

        var first = await p.BuildAsync();
        Assert.True(first.Result.Success, string.Join("\n", first.Log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
        first.AssertWork(AllUnits, link: true);
        return p;
    }

    public void Write(string relative, string text)
    {
        var path = Path.Combine(Directory, relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Replaces <paramref name="oldText"/> (must occur) in a project file.</summary>
    public void Edit(string relative, string oldText, string newText)
    {
        var path = Path.Combine(Directory, relative);
        var text = File.ReadAllText(path);
        Assert.Contains(oldText, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    public async Task<MatrixBuild> BuildAsync(string? configuration = null)
    {
        var runner = new RecordingRunner();
        var log = new EventLog();
        var result = await new BuildEngine(PhysicalFileSystem.Instance, runner).RunAsync(
            new BuildRequest
            {
                ProjectPath = Path.Combine(Directory, "Matrix.pproj"),
                Configuration = configuration,
                ToolchainDirectory = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin"),
                RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
            },
            log,
            TestContext.Current.CancellationToken);
        return new MatrixBuild(result, log, runner.Started);
    }

    public void Dispose() => TempDirectories.Delete(Directory);          // C-292: a scanner may still hold a file

    /// <summary>
    /// Real processes, recording each start (<c>clang.exe --version</c> for the version check). The removal of environment
    /// variables is passed on to the real runner (C-336).
    /// </summary>
    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly ConcurrentQueue<string> _started = new();

        public ImmutableArray<string> Started => [.. _started];

        public Task<int> RunAsync(
            string fileName, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct) =>
            RunAsync(fileName, arguments, workingDirectory, [], onStdOut, onStdErr, ct);

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyList<string> removedEnvironment,
            Action<string> onStdOut,
            Action<string> onStdErr,
            CancellationToken ct)
        {
            _started.Enqueue(Path.GetFileName(fileName) + (arguments is ["--version"] ? " --version" : ""));
            return ProcessRunner.Instance.RunAsync(fileName, arguments, workingDirectory, removedEnvironment, onStdOut, onStdErr, ct);
        }
    }
}

internal sealed record MatrixBuild(BuildResult Result, EventLog Log, ImmutableArray<string> Processes)
{
    /// <summary>Exactly <paramref name="units"/> recompiled (any order), linked or not, success; clang ran once per recompiled unit.</summary>
    public void AssertWork(IEnumerable<string> units, bool link)
    {
        var expected = units.Order(StringComparer.Ordinal).ToList();
        Assert.True(Result.Success, string.Join("\n", Log.Lines));
        Assert.Equal(expected, Log.Info.Where(l => l.StartsWith("Compiling ", StringComparison.Ordinal)).Select(l => l["Compiling ".Length..]).Order(StringComparer.Ordinal));
        Assert.Equal(link, Log.Info.Contains("Linking Matrix.exe"));
        Assert.Equal(expected.Count, Processes.Count(p => p == "clang.exe"));
        Assert.Equal(link ? 1 : 0, Processes.Count(p => p == "lld-link.exe"));
        Assert.False(Result.UpToDate);
    }
}
