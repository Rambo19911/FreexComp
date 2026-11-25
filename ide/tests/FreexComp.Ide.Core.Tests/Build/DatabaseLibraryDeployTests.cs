using FreexComp.Build;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// I-225: the IDE build puts the database engine library beside the program of a project whose resolved unit list holds
/// the unit that loads it, and beside no other program.
/// </summary>
[Trait("Phase", "8")]
public sealed class DatabaseLibraryDeployTests
{
    private const string Home = @"G:\home";
    private const string Library = Home + @"\sqlite\" + DatabaseLibraryDeployer.FileName;
    private const string Exe = BuildTestBed.Dir + @"\bin\Hello.exe";
    private const string Deployed = BuildTestBed.Dir + @"\bin\" + DatabaseLibraryDeployer.FileName;

    /// <summary>The real RTL sources: the unit list needs a front end that starts (System.pas and its include files).</summary>
    private static readonly Lazy<(string Name, string Text)[]> Rtl = new(() =>
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        return [.. Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json"))
            .Where(File.Exists).Select(p => (Path.GetFileName(p), File.ReadAllText(p)))];
    });

    /// <summary>
    /// The Hello project with the real RTL; <paramref name="data"/> makes Unit1 use the unit that loads the library, so the
    /// program reaches it only through another unit (the resolved list, not the program's own uses clause).
    /// </summary>
    private static async Task<(BuildTestBed Bed, RecordingFileSystem Files)> OpenAsync(bool data, bool library = true)
    {
        RecordingFileSystem? recording = null;
        var bed = new BuildTestBed(o =>
        {
            o.FileSystem = recording = new RecordingFileSystem(o.FileSystem);
            o.ToolchainEnvironment = new ToolchainEnvironment(Home, @"G:\ide", []);
        });
        foreach (var (name, text) in Rtl.Value)
            bed.File(@"G:\rtl\" + name, text);
        if (library)
            bed.File(Library, "library v1");
        await bed.OpenHelloAsync();
        if (data)
        {
            bed.File(BuildTestBed.Dir + @"\" + DatabaseLibraryDeployer.BindingUnit + ".pas",
                $"unit {DatabaseLibraryDeployer.BindingUnit};\ninterface\nimplementation\nend.\n");
            bed.File(BuildTestBed.UnitPath, $"unit Unit1;\ninterface\nuses {DatabaseLibraryDeployer.BindingUnit};\nimplementation\nend.\n");
        }

        return (bed, recording!);
    }

    private static async Task<BuildResult?> BuildAsync(BuildTestBed bed, BuildKind kind = BuildKind.Build)
    {
        bed.Engine.Enqueue(FakeBuildEngine.Success(BuildTestBed.ProjectPath, Exe));
        bed.File(Exe, "exe"); // the engine created the output folder
        return await bed.Services.Build.BuildAsync(kind);
    }

    private static int Copies(RecordingFileSystem files) => files.Calls.Count(c => c == "WriteAllBytes " + Deployed);

    private static string CopiedLine => string.Format(Strings.Msg_Build_DatabaseLibraryCopied, DatabaseLibraryDeployer.FileName);

    [Fact(Timeout = 30000)]
    [Trait("Task", "I-225")]
    [Trait("Req", "IDE-P3-095")]
    public Task DataProject_LibraryIsCopiedBesideExe() => TestUiThread.RunAsync(async () =>
    {
        var (bed, files) = await OpenAsync(data: true);

        var result = await BuildAsync(bed);

        Assert.True(result!.Success);
        Assert.Equal("library v1", bed.Text(Deployed));
        Assert.Contains(CopiedLine, bed.OutputText(OutputChannels.Build));
        var summary = Assert.Single(bed.Services.Messages.Items); // no warning row; the summary is last
        Assert.True(summary.IsSummary);

        // The same file is there already: not written again, nothing reported.
        await BuildAsync(bed);
        Assert.Equal(1, Copies(files));
        Assert.DoesNotContain(CopiedLine, bed.OutputText(OutputChannels.Build));

        // A different file beside the program (another version, same length or not) is replaced.
        bed.File(Deployed, "library v0");
        await BuildAsync(bed, BuildKind.Rebuild);
        Assert.Equal("library v1", bed.Text(Deployed));
        Assert.Equal(2, Copies(files));
    });

    [Fact(Timeout = 30000)]
    [Trait("Task", "I-225")]
    [Trait("Req", "IDE-P3-095")]
    public Task OtherProject_GetsNoLibrary() => TestUiThread.RunAsync(async () =>
    {
        var (bed, files) = await OpenAsync(data: false);

        var result = await BuildAsync(bed);

        Assert.True(result!.Success);
        Assert.False(bed.Fs.FileExists(Deployed));
        Assert.DoesNotContain(files.Calls, c => c.Contains(Library, StringComparison.OrdinalIgnoreCase) && c.StartsWith("Read", StringComparison.Ordinal));
        Assert.True(Assert.Single(bed.Services.Messages.Items).IsSummary);
    });

    [Fact(Timeout = 30000)]
    [Trait("Task", "I-225")]
    [Trait("Req", "IDE-P3-095")]
    public Task DataProject_CleanAndFailedBuildCopyNothing() => TestUiThread.RunAsync(async () =>
    {
        var (bed, _) = await OpenAsync(data: true);

        await BuildAsync(bed, BuildKind.Clean);
        Assert.False(bed.Fs.FileExists(Deployed));

        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath,
            FakeBuildEngine.Diag("E2003", FreexComp.Compiler.DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 1, 1)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.False(bed.Fs.FileExists(Deployed));
    });

    [Fact(Timeout = 30000)]
    [Trait("Task", "I-225")]
    [Trait("Req", "IDE-P3-095")]
    public Task DataProject_MissingLibraryIsReported() => TestUiThread.RunAsync(async () =>
    {
        var (bed, _) = await OpenAsync(data: true, library: false);

        var result = await BuildAsync(bed);

        Assert.True(result!.Success); // the program was built; it cannot open a database
        Assert.False(bed.Fs.FileExists(Deployed));
        var text = string.Format(Strings.Msg_Build_DatabaseLibraryMissing, DatabaseLibraryDeployer.FileName);
        Assert.Contains(DatabaseLibraryDeployer.FileName, text, StringComparison.Ordinal);
        var messages = bed.Services.Messages;
        Assert.Equal([(MessageSeverity.Warning, text, false), (MessageSeverity.Info, bed.Services.Build.ResultText!, true)],
            messages.Items.Select(i => (i.Severity, i.Text, i.IsSummary)));
        Assert.Equal((0, 1), (messages.ErrorCount, messages.WarningCount));
        Assert.Contains(text, bed.OutputText(OutputChannels.Build));
    });

    /// <summary>
    /// The task's acceptance over the real build engine and toolchain: the IDE build of the data-access sample leaves the
    /// library of the pinned toolchain beside the program; the build of a console program leaves none.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Task", "I-225")]
    [Trait("Req", "IDE-P3-095")]
    [Trait("Category", "Toolchain")]
    public Task RealBuild_DataSampleGetsLibrary_ConsoleProgramGetsNone() => TestUiThread.RunAsync(async () =>
    {
        var repo = SpecTables.RepoRootPath();
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "scripts", "toolchain.json")));
        var installDir = config.RootElement.GetProperty("sqlite").GetProperty("installDir").GetString()!; // the pinned toolchain's folder
        var library = Path.Combine(repo, installDir.Replace('/', Path.DirectorySeparatorChar), DatabaseLibraryDeployer.FileName);
        Assert.True(File.Exists(library), "database library not found (run scripts/bootstrap.ps1): " + library);
        using var temp = new TempDirectory();

        var crud = temp.Combine("crud");
        Directory.CreateDirectory(crud);
        foreach (var file in Directory.GetFiles(Path.Combine(repo, "data", "samples", "crud")))
            File.Copy(file, Path.Combine(crud, Path.GetFileName(file)));
        var crudExe = await RealBuildAsync(temp, Path.Combine(crud, "Crud.pproj"));
        var deployed = Path.Combine(Path.GetDirectoryName(crudExe)!, DatabaseLibraryDeployer.FileName);
        Assert.True(File.Exists(deployed), "no library beside " + crudExe);
        Assert.Equal(File.ReadAllBytes(library), File.ReadAllBytes(deployed));

        var hello = temp.Combine("hello");
        Directory.CreateDirectory(hello);
        File.WriteAllText(Path.Combine(hello, "Hello.pproj"),
            """{ "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas", "files": [ { "path": "Hello.pas" } ] }""");
        File.WriteAllText(Path.Combine(hello, "Hello.pas"), "program Hello;\nbegin\n  WriteLn('hello');\nend.\n");
        var helloExe = await RealBuildAsync(temp, Path.Combine(hello, "Hello.pproj"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(helloExe)!, "*.dll"));
    });

    /// <summary>Opens the project in a composition over the real file system, build engine and toolchain; builds it; returns the program.</summary>
    private static async Task<string> RealBuildAsync(TempDirectory temp, string projectPath)
    {
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = new Core.Platform.PhysicalFileSystem();
            o.BuildEngine = new BuildEngine(FreexComp.Compiler.PhysicalFileSystem.Instance);
            o.ToolchainEnvironment = new ToolchainEnvironment(null, AppContext.BaseDirectory, []);
            o.UserDir = temp.Combine("user-" + Path.GetFileNameWithoutExtension(projectPath));
        });
        Assert.True(await services.Projects.OpenAsync(projectPath));
        var result = await services.Build.BuildAsync(BuildKind.Build);
        var errors = result?.Projects.SelectMany(p => p.Diagnostics).Where(d => d.Severity == FreexComp.Compiler.DiagnosticSeverity.Error).ToList();
        Assert.True(result is { Success: true, OutputPath: not null } && File.Exists(result.OutputPath),
            $"build of {projectPath} failed (run scripts/bootstrap.ps1 when the toolchain is missing): {services.Build.ResultText}\n"
            + string.Join("\n", errors ?? []));
        return result!.OutputPath!;
    }
}
