using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Data.Tests;

/// <summary>
/// The tests that start programs with windows share one collection, so that they run one after the other: two
/// programs that both move the keyboard focus are never on the desktop at the same time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProgramRuns
{
    public const string Name = "Program runs";
}

/// <summary>Where the CRUD sample lives (crud-sample.md §1).</summary>
public static class CrudSample
{
    public static string Directory => Path.Combine(DataCorpus.RepositoryRoot, "data", "samples", "crud");

    public static string ExpectedResult => Path.Combine(Directory, "expected", "autotest.txt");

    public static string PbuildPath => Path.Combine(AppContext.BaseDirectory, "pbuild.exe");
}

/// <summary>
/// The sample built once for the tests of <see cref="CrudSampleTests"/> (crud-sample.md §4): the project folder is copied
/// to a temporary folder (its relative search paths rewritten to the absolute folders they name, nothing else), built
/// there with <c>pbuild --config Debug</c>, and the SQLite library copied next to the exe (DAT-362). A failure is kept in
/// <see cref="Failure"/> for the tests to report; nothing is written into the repository.
/// </summary>
public sealed class CrudBuild : IAsyncLifetime
{
    public string Work { get; } = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "crud-" + Guid.NewGuid().ToString("N"));

    public string ProjectDirectory => Path.Combine(Work, "crud");

    public string ExePath => Path.Combine(ProjectDirectory, "bin", "Debug", "Crud.exe");

    /// <summary>Null when the build succeeded; else what failed, with the output of the build.</summary>
    public string? Failure { get; private set; }

    public string Output { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            Failure = await BuildAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Failure = e.GetType().Name + ": " + e.Message;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Failure is null)
        {
            GoldenProgramRunner.TryDelete(Work);                                     // kept on failure for inspection
        }

        return ValueTask.CompletedTask;
    }

    private async Task<string?> BuildAsync()
    {
        var project = Path.Combine(CrudSample.Directory, "Crud.pproj");
        if (!File.Exists(project))
        {
            return project + " does not exist";
        }

        if (GoldenProgramRunner.MissingPrerequisites(DataCorpus.ToolchainDirectory, CrudSample.PbuildPath, DataCorpus.SqliteLibrary) is { } missing)
        {
            return missing;
        }

        System.IO.Directory.CreateDirectory(ProjectDirectory);
        foreach (var file in System.IO.Directory.EnumerateFiles(CrudSample.Directory))
        {
            File.Copy(file, Path.Combine(ProjectDirectory, Path.GetFileName(file)));
        }

        // the copy is outside the repository: the search paths keep naming the folders of the original
        var copy = Path.Combine(ProjectDirectory, "Crud.pproj");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(copy).ConfigureAwait(false))!;
        var paths = json["searchPaths"]!.AsArray();
        for (var i = 0; i < paths.Count; i++)
        {
            paths[i] = Path.GetFullPath(paths[i]!.GetValue<string>(), CrudSample.Directory);
        }

        await File.WriteAllTextAsync(copy, json.ToJsonString(), new UTF8Encoding(false)).ConfigureAwait(false);

        var build = await GoldenProgramRunner.RunProcessAsync(
            CrudSample.PbuildPath,
            ["Crud.pproj", "--config", "Debug", "--rtl", DataCorpus.RtlDirectory, "--toolchain", DataCorpus.ToolchainDirectory],
            ProjectDirectory, GoldenProgramRunner.CompileLimit, CancellationToken.None).ConfigureAwait(false);
        Output = GoldenProgramRunner.Normalize(build.StdOut) + build.StdErr;
        if (build.Stopped is { } stopped)
        {
            return "pbuild " + stopped;
        }

        if (build.ExitCode != 0)
        {
            return $"pbuild exit code {build.ExitCode}, expected 0\n{Output}";
        }

        if (!File.Exists(ExePath))
        {
            return $"pbuild succeeded but {ExePath} does not exist\n{Output}";
        }

        File.Copy(DataCorpus.SqliteLibrary, Path.Combine(Path.GetDirectoryName(ExePath)!, Path.GetFileName(DataCorpus.SqliteLibrary)));
        return null;
    }
}

/// <summary>The sample form as a file of the form format (testing.md §2).</summary>
public sealed class CrudFormTests
{
    /// <summary>
    /// <c>MainForm.form</c> is read by the form library of the designer with the component metadata of the framework and
    /// of the data area, without any diagnostic, and written back byte-identically: the properties of the data components
    /// fit the form format and the file is in canonical form.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DAT-343")]
    public void Crud_Form_RoundTrips()
    {
        var path = Path.Combine(CrudSample.Directory, "MainForm.form");
        Assert.True(File.Exists(path), path + " does not exist");
        var catalog = CatalogLoader.Load(
        [
            Path.Combine(DataCorpus.RepositoryRoot, "ui-framework", "design", "index.json"),
            Path.Combine(DataCorpus.RepositoryRoot, "data", "design", "index.json"),
        ]);
        Assert.True(catalog.Problems.IsEmpty, string.Join("\n", catalog.Problems));

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "the form starts with a byte order mark");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.DoesNotContain('\r', text);

        var parsed = FormParser.Parse(text, path, catalog.Catalog, rootAncestorClass: "TForm");

        Assert.True(parsed.Diagnostics.IsEmpty, string.Join("\n", parsed.Diagnostics));
        var written = FormWriter.Write(parsed.Document!);
        Assert.True(
            string.Equals(text, written, StringComparison.Ordinal),
            "MainForm.form is not in canonical form; the form library writes it as:\n" + written);
    }
}

/// <summary>
/// The CRUD sample built with <c>pbuild</c> and driven by its own self-test (crud-sample.md §4):
/// <c>Crud.exe --db &lt;file&gt; --autotest &lt;result file&gt;</c> runs the script of §5 through the controls and writes one line
/// per step, the verdict and the rows of the table. Every test runs in its own temporary folder. A program that cannot
/// create its window (exit code 3) is a failure, never a skip. The time limit is a safety net against a hung process.
/// </summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
[Collection(ProgramRuns.Name)]
public sealed class CrudSampleTests(CrudBuild build) : IClassFixture<CrudBuild>
{
    public static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);

    private sealed record Run(int ExitCode, string[] Lines, string Raw, string Console);

    [Fact]
    [Trait("Req", "DAT-362")]
    public void Crud_Builds()
    {
        Assert.True(build.Failure is null, build.Failure);
        Assert.True(File.Exists(build.ExePath), build.ExePath + " does not exist");
        // no diagnostic names a file of the sample: it builds without warnings of its own
        var own = build.Output.Split('\n').Where(l => l.StartsWith(build.ProjectDirectory, StringComparison.OrdinalIgnoreCase)
            && (l.Contains("): warning ", StringComparison.Ordinal) || l.Contains("): error ", StringComparison.Ordinal))).ToList();
        Assert.True(own.Count == 0, string.Join("\n", own));
    }

    /// <summary>The Phase 7 exit test: a fresh database, every step passes, the file holds the expected rows.</summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "DAT-360")]
    [Trait("Req", "DAT-361")]
    [Trait("Req", "DAT-365")]
    [Trait("Req", "DAT-366")]
    [Trait("Req", "DAT-367")]
    [Trait("Req", "DAT-368")]
    [Trait("Req", "DAT-370")]
    [Trait("Req", "DAT-371")]
    [Trait("Req", "DAT-372")]
    [Trait("Req", "DAT-373")]
    [Trait("Req", "DAT-374")]
    public async Task Crud_AutoTest_PassesAndLeavesExpectedDbState()
    {
        Assert.True(build.Failure is null, build.Failure);
        var folder = NewRunFolder();

        var run = await RunAsync(build.ExePath, folder);

        Assert.True(run.ExitCode == 0, $"exit code {run.ExitCode}, expected 0\n{run.Raw}\n{run.Console}\n(run folder: {folder})");
        var expected = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(CrudSample.ExpectedResult, TestContext.Current.CancellationToken));
        Assert.Equal(expected, GoldenProgramRunner.Normalize(run.Raw));
        // DAT-374: UTF-8 without a byte order mark, every line ends with CR LF
        var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, "result.txt"), TestContext.Current.CancellationToken);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "the result file starts with a byte order mark");
        Assert.Equal(expected.Replace("\n", "\r\n", StringComparison.Ordinal), run.Raw);
        Assert.True(File.Exists(Path.Combine(folder, "contacts.db")), "the database file named by --db was not created");
        GoldenProgramRunner.TryDelete(folder);
    }

    /// <summary>
    /// A second run on the database the first run left: the rows are still in the file, so the first step reports the
    /// three records, the run fails and says so through the result file and the exit code.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "DAT-372")]
    [Trait("Req", "DAT-373")]
    public async Task Crud_SecondRun_KeepsData()
    {
        Assert.True(build.Failure is null, build.Failure);
        var folder = NewRunFolder();
        var first = await RunAsync(build.ExePath, folder);
        Assert.True(first.ExitCode == 0, $"first run: exit code {first.ExitCode}, expected 0\n{first.Raw}\n{first.Console}\n(run folder: {folder})");
        File.Delete(Path.Combine(folder, "result.txt"));

        var second = await RunAsync(build.ExePath, folder);

        Assert.True(second.ExitCode == 1, $"second run: exit code {second.ExitCode}, expected 1\n{second.Raw}\n{second.Console}\n(run folder: {folder})");
        Assert.Equal("FAIL S01 initial state: RecordCount=3", second.Lines[0]);
        // one line per step whatever its outcome, then the verdict, then the rows
        Assert.Equal(15, second.Lines.TakeWhile(l => l != "RESULT FAIL").Count(l => l.StartsWith("PASS S", StringComparison.Ordinal) || l.StartsWith("FAIL S", StringComparison.Ordinal)));
        Assert.Equal("RESULT FAIL", second.Lines[15]);
        Assert.DoesNotContain("RESULT PASS", second.Lines);
        Assert.All(second.Lines.Skip(16), l => Assert.StartsWith("DUMP ", l, StringComparison.Ordinal));
        Assert.Contains("DUMP 1|Ada Lovelace|ada@example.org|37|1|First", second.Lines);
        GoldenProgramRunner.TryDelete(folder);
    }

    /// <summary>
    /// DAT-362: without the SQLite library beside the exe (and none on the search path of the process) the self-test
    /// reports the start-up failure in the result file and ends with exit code 1.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "DAT-362")]
    public async Task Crud_MissingLibrary_ReportsStartupFailure()
    {
        Assert.True(build.Failure is null, build.Failure);
        var folder = NewRunFolder();
        var exe = Path.Combine(folder, "Crud.exe");
        File.Copy(build.ExePath, exe);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.False(File.Exists(Path.Combine(system, "sqlite3.dll")), "this machine has sqlite3.dll in its system folder: the test cannot hide the library");

        var run = await RunAsync(exe, folder, new Dictionary<string, string> { ["PATH"] = system });

        Assert.True(run.ExitCode == 1, $"exit code {run.ExitCode}, expected 1\n{run.Raw}\n{run.Console}\n(run folder: {folder})");
        Assert.Equal(["FAIL S00 startup: Cannot load SQLite library \"sqlite3.dll\"", "RESULT FAIL"], run.Lines);
        GoldenProgramRunner.TryDelete(folder);
    }

    private static string NewRunFolder() =>
        System.IO.Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "crud-run-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>One run of the self-test with the database and the result file in <paramref name="folder"/>.</summary>
    private static async Task<Run> RunAsync(string exe, string folder, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = Path.Combine(folder, "result.txt");
        var process = await GoldenProgramRunner.RunProcessAsync(
            exe, ["--db", Path.Combine(folder, "contacts.db"), "--autotest", result], folder, RunLimit, TestContext.Current.CancellationToken, environment);
        Assert.True(process.Stopped is null, $"Crud.exe {process.Stopped} (run folder: {folder})");
        var console = $"--- stdout\n{process.StdOut}\n--- stderr\n{process.StdErr}";
        Assert.True(process.ExitCode != 3, $"Crud.exe could not create its window (exit code 3): no interactive window station\n{console}");
        Assert.True(File.Exists(result), $"no result file was written (exit code {process.ExitCode})\n{console}\n(run folder: {folder})");
        var raw = new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(result, TestContext.Current.CancellationToken));
        return new Run(process.ExitCode, GoldenProgramRunner.Normalize(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries), raw, console);
    }
}
