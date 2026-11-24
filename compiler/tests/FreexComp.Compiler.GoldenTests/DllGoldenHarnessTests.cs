using System.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// Self-tests of the scenario harness (dll-output.md §10.3, task C-290): <c>steps.txt</c> parsing, the command line of each
/// build step, the order of execution and every comparison — with a recorded process start, no tool runs.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class DllGoldenHarnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _scenario;
    private readonly DllScenarioSetup _setup = new(@"X:\tools\opc.exe", ["--rtl", @"X:\rtl", "--toolchain", @"X:\llvm"]);
    private readonly List<(string File, string[] Arguments, string Directory)> _calls = [];
    private readonly HashSet<string> _workDirectories = new(StringComparer.OrdinalIgnoreCase);
    private string? _work;

    public DllGoldenHarnessTests()
    {
        _scenario = Path.Combine(_root, "Sample");
        Directory.CreateDirectory(_scenario);
    }

    public void Dispose()
    {
        // the harness keeps the work directory of a failed scenario
        foreach (var directory in _workDirectories.Append(_root).Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void Put(string name, string text) => File.WriteAllBytes(Path.Combine(_scenario, name), Encoding.UTF8.GetBytes(text));

    /// <summary>Records the call; a build step "produces" its <c>-o</c> file, the host prints <paramref name="stdout"/> and leaves <paramref name="marker"/>.</summary>
    private DllScenarioProcess Process(string stdout = "sum=5\r\ndone\r\n", int exitCode = 0, string? marker = "unloaded.txt", int opcExitCode = 0) =>
        (file, arguments, directory, _) =>
        {
            _calls.Add((file, [.. arguments], directory));
            if (file == _setup.OpcPath)
            {
                var output = arguments[arguments.ToList().IndexOf("-o") + 1];
                _work = Path.GetDirectoryName(output)!;
                _workDirectories.Add(_work);
                File.WriteAllText(output, "image");
                return Task.FromResult(new DllScenarioProcessResult(opcExitCode, opcExitCode == 0 ? "" : "lib.pas(1,1): error E1100: Syntax error\r\n", null));
            }

            if (marker is not null)
            {
                File.WriteAllText(Path.Combine(directory, marker), "");
            }

            return Task.FromResult(new DllScenarioProcessResult(exitCode, stdout, null));
        };

    private async Task<string?> RunAsync(bool o2 = false, DllScenarioProcess? process = null)
    {
        var failure = await DllScenario.RunAsync(_scenario, o2, _setup, TestContext.Current.CancellationToken, process ?? Process());
        const string kept = "\n(work directory: ";
        if (failure?.LastIndexOf(kept, StringComparison.Ordinal) is { } at and >= 0)
        {
            _workDirectories.Add(failure[(at + kept.Length)..^1]);
        }

        return failure;
    }

    private void PutSample(string steps = "dll lib.pas -g\nexe host.pas\nrun host.exe one two\nexists unloaded.txt\n")
    {
        Put("steps.txt", steps);
        Put("host.out", "sum=5\ndone\n");
    }

    [Fact]
    public async Task StepsFileParsedAndExecutedInOrder()
    {
        // parsing: one step per line; blank lines and # lines are ignored; words are separated by blanks
        var steps = DllScenario.Parse("\uFEFF# the library first\r\ndll lib.pas -g  -D  X\r\n\r\nexe host.pas\r\nrun host.exe one two\r\nexists unloaded.txt\r\n");
        Assert.Equal(
            ["2 dll lib.pas [-g -D X]", "4 exe host.pas []", "5 run host.exe [one two]", "6 exists unloaded.txt []"],
            steps.Select(s => $"{s.Line} {s.Verb} {s.Target} [{string.Join(' ', s.Arguments)}]"));

        // execution: each build step is one opc run in the scenario folder, then the host in the work directory, in file order
        PutSample();
        Assert.Null(await RunAsync());

        Assert.Equal(3, _calls.Count);
        var work = _work!;
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "FreexComp.Tests"), work, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                Path.Combine(_scenario, "lib.pas"), "-o", Path.Combine(work, "lib.dll"), "--obj-dir", Path.Combine(work, "obj", "lib"), "-L", work,
                "--rtl", @"X:\rtl", "--toolchain", @"X:\llvm", "-O0", "-g",
            ],
            _calls[0].Arguments);
        Assert.Equal((_setup.OpcPath, _scenario), (_calls[0].File, _calls[0].Directory));
        Assert.Equal(
            [
                Path.Combine(_scenario, "host.pas"), "-o", Path.Combine(work, "host.exe"), "--obj-dir", Path.Combine(work, "obj", "host"), "-L", work,
                "--rtl", @"X:\rtl", "--toolchain", @"X:\llvm", "-O0",
            ],
            _calls[1].Arguments);
        Assert.Equal((Path.Combine(work, "host.exe"), work), (_calls[2].File, _calls[2].Directory));
        Assert.Equal(["one", "two"], _calls[2].Arguments);
        Assert.False(Directory.Exists(work), "the work directory of a passed scenario is deleted");

        // -O2 replaces -O0, at the same place
        _calls.Clear();
        Assert.Null(await RunAsync(o2: true));
        Assert.Equal("-O2", _calls[0].Arguments[11]);
        Assert.DoesNotContain("-O0", _calls[1].Arguments);
    }

    [Theory]
    [InlineData("link lib.pas\nrun host.exe\n", "steps.txt(1): unknown verb (expected dll, exe, run or exists): 'link lib.pas'")]
    [InlineData("dll\nrun host.exe\n", "steps.txt(1): missing file name: 'dll'")]
    [InlineData("dll lib.dll\nrun host.exe\n", "steps.txt(1): a build step names a .pas file: 'dll lib.dll'")]
    [InlineData("exe host.pas\nrun host\n", "steps.txt(2): a run step names an .exe file: 'run host'")]
    [InlineData("exe host.pas\nexists a.txt\nrun host.exe\n", "steps.txt(2): an exists step follows a run step: 'exists a.txt'")]
    [InlineData("run host.exe\nexists a.txt b.txt\n", "steps.txt(2): an exists step names one file: 'exists a.txt b.txt'")]
    [InlineData("dll lib.pas\nexe host.pas\n", "steps.txt: no run step")]
    public async Task InvalidStepsFile_IsAFailureNamingTheLine(string steps, string message)
    {
        Assert.Equal(message, Assert.Throws<FormatException>(() => DllScenario.Parse(steps)).Message);

        PutSample(steps);
        Assert.Equal("Sample: " + message, await RunAsync());
        Assert.Empty(_calls);                                                   // nothing is started
    }

    [Fact]
    public async Task EveryExpectationIsCompared_AndTheFirstFailureStopsTheScenario()
    {
        PutSample();

        // a failed build step: later steps do not run; the failure carries opc's output and the kept work directory
        var failure = await RunAsync(process: Process(opcExitCode: 1));
        Assert.StartsWith("Sample: steps.txt(1) dll lib.pas: opc exit code 1, expected 0\nlib.pas(1,1): error E1100: Syntax error\n", failure, StringComparison.Ordinal);
        Assert.EndsWith($"\n(work directory: {_work})", failure, StringComparison.Ordinal);
        Assert.True(Directory.Exists(_work));
        Assert.Single(_calls);

        // stdout is compared after CRLF→LF, byte for byte otherwise
        Assert.Null(await RunAsync(process: Process("sum=5\ndone\n")));
        failure = await RunAsync(process: Process("sum=6\r\ndone\r\n"));
        Assert.StartsWith("Sample: steps.txt(3) run host.exe: stdout differs\n--- expected\nsum=5\ndone\n\n--- actual\nsum=6\ndone\n", failure, StringComparison.Ordinal);
        Assert.Contains("stdout differs", await RunAsync(process: Process("sum=5\r\ndone")), StringComparison.Ordinal);

        // the exit code is 0 unless <host>.exit says otherwise
        Assert.StartsWith("Sample: steps.txt(3) run host.exe: exit code 3, expected 0\nstdout:\nsum=5\ndone\n", await RunAsync(process: Process(exitCode: 3)), StringComparison.Ordinal);
        Put("host.exit", "3\n");
        Assert.Null(await RunAsync(process: Process(exitCode: 3)));
        Assert.Contains("exit code 0, expected 3", await RunAsync(), StringComparison.Ordinal);
        File.Delete(Path.Combine(_scenario, "host.exit"));

        // exists: the file must be in the work directory once the host has exited
        Assert.StartsWith("Sample: steps.txt(4) exists unloaded.txt: the file does not exist in the work directory", await RunAsync(process: Process(marker: null)), StringComparison.Ordinal);

        // a limit or cancellation that stopped a process is reported with its state
        failure = await RunAsync(process: (_, _, _, _) => Task.FromResult(new DllScenarioProcessResult(null, "", "CPU limit 20.0 s reached: opc.exe stopped")));
        Assert.StartsWith("Sample: steps.txt(1) dll lib.pas: opc stopped: CPU limit 20.0 s reached: opc.exe stopped", failure, StringComparison.Ordinal);

        // a run step without its .out file
        File.Delete(Path.Combine(_scenario, "host.out"));
        Assert.StartsWith("Sample: steps.txt(3) run host.exe: host.out is missing", await RunAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_FoldersWithStepsFile_ByName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Zeta"));
        File.WriteAllText(Path.Combine(_root, "Zeta", "steps.txt"), "run a.exe\n");
        Directory.CreateDirectory(Path.Combine(_root, "NoSteps"));
        Put("steps.txt", "run a.exe\n");

        Assert.Equal(["Sample", "Zeta"], DllScenario.Discover(_root).Select(Path.GetFileName));
        Assert.Empty(DllScenario.Discover(Path.Combine(_root, "missing")));
    }
}
