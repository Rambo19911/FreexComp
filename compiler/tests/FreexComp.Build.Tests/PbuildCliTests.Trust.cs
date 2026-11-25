using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>C-324: <c>pbuild --trust-events</c> / <c>--no-events</c> and the trust list file (project-trust.md CMP-1247…1249).</summary>
public sealed partial class PbuildCliTests
{
    private const string TrustFile = @"C:\user\roaming\trust.json";
    private const string TrustE9021 =
        @"C:\p\Hello.pproj(0,0): error E9021: Project 'C:\p\Hello.pproj' has build event commands and is not trusted (pbuild: --trust-events runs them, --no-events builds without them)";
    private const string TrustW9001 = @"C:\p\Hello.pproj(0,0): warning W9001: Project events are not supported yet and were ignored";

    private static readonly string[] PlainBuildOutput = ["Compiling System", "Compiling Hello", "Linking Hello.exe", "fake lld-link done"];

    private static BuildWorkspace WithEvents() =>
        new(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "events": { "preBuild": ["echo hi"], "postBuild": ["echo done"] }"""));

    /// <summary>As <see cref="Run(BuildWorkspace, string[])"/>, with the trust list at <paramref name="trustFile"/> of the in-memory file system.</summary>
    private static (int ExitCode, string[] Out) RunWithTrust(BuildWorkspace w, string? trustFile, params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var env = new PbuildEnvironment(@"C:\p", stdout, new StringWriter(), w.Fs)
        {
            ProcessRunner = w.Tools,
            ToolchainDirectory = BuildWorkspace.Llvm,
            RtlDirectory = BuildWorkspace.Rtl,
            TrustFilePath = trustFile,
        };
        var code = PbuildCommand.Run(args, env, TestContext.Current.CancellationToken);
        var lines = stdout.ToString().Split('\n');
        return (code, lines[^1].Length == 0 ? lines[..^1] : lines);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1247")]
    [Trait("Req", "CMP-1248")]
    [InlineData(TrustFile)]
    [InlineData(null)]
    public void Trust_UntrustedProjectExitsOneWithE9021(string? trustFile)
    {
        var w = WithEvents();

        var (code, output) = RunWithTrust(w, trustFile, "Hello.pproj");
        var (jsonCode, json) = RunWithTrust(w, trustFile, "Hello.pproj", "--diagnostics", "json");
        var (runCode, run) = RunWithTrust(w, trustFile, "Hello.pproj", "--target", "run");

        Assert.Equal(1, code);
        Assert.Equal(TrustE9021, output[0]);
        Assert.Matches("^Build failed " + Summary, output[1]);
        Assert.Equal(2, output.Length);
        Assert.Equal(1, jsonCode);
        Assert.Contains(json, l => l.Contains("\"severity\":\"error\",\"code\":\"E9021\"", StringComparison.Ordinal));     // an ordinary diagnostic object
        Assert.Equal(1, runCode);
        Assert.Equal(TrustE9021, run[0]);
        Assert.Empty(w.Tools.Calls);                                            // nothing run
        Assert.Empty(w.Tools.Programs);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(TrustFile));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1241")]
    [Trait("Req", "CMP-1248")]
    public void Trust_NoEventsBuildsAndRunsNoCommand()
    {
        var w = WithEvents();
        var plain = new BuildWorkspace();

        var (code, output) = RunWithTrust(w, TrustFile, "Hello.pproj", "--no-events");
        var (plainCode, plainOutput) = RunWithTrust(plain, TrustFile, "Hello.pproj", "--no-events");
        var (cleanCode, _) = RunWithTrust(w, TrustFile, "Hello.pproj", "--no-events", "--target", "clean");

        Assert.Equal(0, code);
        Assert.Equal(PlainBuildOutput, output[..^1]);                           // as if the project had no events: no W9001
        Assert.Matches(@"^Build succeeded \(0 errors, 0 warnings, ", output[^1]);
        Assert.Empty(w.Tools.Programs);                                         // no command was run
        Assert.All(w.Tools.Calls, c => Assert.Contains(c, new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" }));
        Assert.Equal(0, plainCode);                                             // accepted and without effect for a project without commands
        Assert.Equal(PlainBuildOutput, plainOutput[..^1]);
        Assert.Equal(0, cleanCode);                                             // … and for --target clean
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(TrustFile));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1242")]
    [Trait("Req", "CMP-1247")]
    [Trait("Req", "CMP-1249")]
    public void Trust_TrustEventsPassesTheGateAndWritesNothing()
    {
        var w = WithEvents();
        var plain = new BuildWorkspace();

        var (code, output) = RunWithTrust(w, TrustFile, "Hello.pproj", "--trust-events");
        var (again, afterwards) = RunWithTrust(w, TrustFile, "Hello.pproj");
        var (plainCode, plainOutput) = RunWithTrust(plain, TrustFile, "Hello.pproj", "--trust-events");
        var (cleanCode, _) = RunWithTrust(w, TrustFile, "Hello.pproj", "--trust-events", "--target", "clean");

        Assert.Equal(0, code);
        Assert.Equal([.. PlainBuildOutput, TrustW9001], output[..^1]);           // behind the gate: the events reach the execution step
        Assert.False(w.Fs.FileExists(TrustFile));                                // nothing is remembered …
        Assert.Equal(1, again);                                                 // … so the next plain invocation is gated again
        Assert.Equal(TrustE9021, afterwards[0]);
        Assert.Equal(0, plainCode);
        Assert.Equal(PlainBuildOutput, plainOutput[..^1]);
        Assert.Equal(0, cleanCode);
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\user\roaming", "*"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1247")]
    [Trait("Req", "CMP-1244")]
    public void Trust_TrustedByFileBuilds()
    {
        var w = WithEvents();
        w.Fs.Add(TrustFile, """{ "formatVersion": 1, "folders": ["C:\\p"], "projects": [] }""");
        var broken = WithEvents();
        broken.Fs.Add(TrustFile, """{ "formatVersion": 7, "folders": ["C:\\p"], "projects": [] }""");

        var (code, output) = RunWithTrust(w, TrustFile, "Hello.pproj");
        var (brokenCode, brokenOutput) = RunWithTrust(broken, TrustFile, "Hello.pproj");

        Assert.Equal(0, code);
        Assert.Equal([.. PlainBuildOutput, TrustW9001], output[..^1]);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.Equal(1, brokenCode);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): warning W9003: Trust list 'C:\user\roaming\trust.json' cannot be read (unsupported format version 7); no project is trusted",
             TrustE9021],
            brokenOutput[..^1]);
        Assert.Empty(broken.Tools.Calls);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1247")]
    [InlineData("--trust-events", "--no-events")]
    [InlineData("--no-events", "--trust-events")]
    public void Trust_BothOptionsAreAnError(string first, string second)
    {
        var w = WithEvents();

        var (code, output) = RunWithTrust(w, TrustFile, "Hello.pproj", first, second);

        Assert.Equal(1, code);
        Assert.Equal($@"C:\p\Hello.pproj(0,0): error E0005: Invalid command-line option '{second}'", output[0]);
        Assert.Equal(2, output.Length);
        Assert.Empty(w.Tools.Calls);
    }
}
