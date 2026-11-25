using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-144: the event protocol of build-engine.md CMP-800…804 and the Build / Rebuild targets (CMP-830, 831, 834, 835).</summary>
[Trait("Phase", "3")]
public sealed class BuildEventOrderTests
{
    [Fact]
    [Trait("Req", "CMP-800")]
    [Trait("Req", "CMP-802")]
    [Trait("Req", "CMP-830")]
    public async Task Build_Success_StartedProgressLinesFinished()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request());

        Assert.Equal(
            [
                "started Hello.pproj Debug win64 Build",
                "info Compiling System",
                "info Compiling Hello",
                "info Linking Hello.exe",
                "tool fake lld-link done",
                "finished Hello.pproj success=True upToDate=False 0/0/0",
            ],
            log.Lines);
        Assert.True(result.Success);
        Assert.False(result.Canceled);
        var project = Assert.Single(result.Projects);
        Assert.Equal((BuildWorkspace.ProjectPath, "Debug", true, false, BuildWorkspace.Exe), (project.ProjectPath, project.Configuration, project.Success, project.UpToDate, project.OutputPath));
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.Null(result.RunExitCode);
    }

    [Fact]
    [Trait("Req", "CMP-801")]
    [Trait("Req", "CMP-804")]
    public async Task Build_CompileError_DiagnosticEventsBeforeFinished_NoProcess()
    {
        var w = new BuildWorkspace("program Hello; begin Foo := 1; Bar := 2 end.");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.Equal(
            [
                "started Hello.pproj Debug win64 Build",
                "diagnostic E2003",
                "diagnostic E2003",
                "finished Hello.pproj success=False upToDate=False 2/0/0",
            ],
            log.Lines);
        var messages = log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()).ToList();
        Assert.Equal([@"C:\p\Hello.pas(1,22): error E2003: Undeclared identifier 'Foo'", @"C:\p\Hello.pas(1,32): error E2003: Undeclared identifier 'Bar'"], messages);
        Assert.False(result.Success);
        Assert.Equal((2, 0, 0), (result.Errors, result.Warnings, result.Hints));
        Assert.Null(Assert.Single(result.Projects).OutputPath);
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-830")]
    public async Task Build_Twice_SecondReportsUpToDate_AndStartsNoProcess()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());
        var calls = w.Tools.Calls.Length;

        var (result, log) = await w.RunAsync(w.Request());

        Assert.Equal(
            [
                "started Hello.pproj Debug win64 Build",
                "info Hello: up to date",
                "finished Hello.pproj success=True upToDate=True 0/0/0",
            ],
            log.Lines);
        Assert.True(Assert.Single(result.Projects).UpToDate);
        Assert.Equal(BuildWorkspace.Exe, result.Projects[0].OutputPath);
        Assert.Equal(calls, w.Tools.Calls.Length);
    }

    [Fact]
    [Trait("Req", "CMP-831")]
    public async Task Rebuild_CompilesEverythingAgain()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());
        w.Fs.Add(@"C:\p\obj\Debug\stale.txt", "x");

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Rebuild));

        Assert.True(result.Success);
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.exe"], log.Info);
        Assert.Equal("started Hello.pproj Debug win64 Rebuild", log.Lines[0]);
        Assert.False(w.Fs.FileExists(@"C:\p\obj\Debug\stale.txt"));
    }

    [Fact]
    [Trait("Req", "CMP-800")]
    public async Task Verbose_RaisesCommandLineEvents_BeforeTheToolRuns()
    {
        var w = new BuildWorkspace();

        var (_, log) = await w.RunAsync(w.Request() with { Verbose = true });

        var lines = log.Lines;
        Assert.Contains("command clang.exe", lines);
        Assert.Contains("command llvm-dlltool.exe", lines);
        var link = lines.IndexOf("command lld-link.exe");
        Assert.True(link > lines.IndexOf("info Linking Hello.exe"));
        Assert.Equal(link + 1, lines.IndexOf("tool fake lld-link done"));
    }

    [Fact]
    [Trait("Req", "CMP-800")]
    public async Task NotVerbose_RaisesNoCommandLineEvents()
    {
        var w = new BuildWorkspace();

        var (_, log) = await w.RunAsync(w.Request());

        Assert.DoesNotContain(log.Events, e => e is BuildEvent.CommandLine);
    }

    [Fact]
    [Trait("Req", "CMP-803")]
    public async Task CancelReturnsCanceledResult()
    {
        var w = new BuildWorkspace();
        using var cts = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        w.Tools.BeforeTool = async (tool, _, ct) =>
        {
            if (tool == "lld-link.exe")
            {
                w.Fs.Add(BuildWorkspace.Exe, "half-written");
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };

        var run = w.RunAsync(w.Request(), cts.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        cts.Cancel();
        var (result, log) = await run;

        Assert.False(result.Success);
        Assert.True(result.Canceled);
        Assert.Equal(1, result.Errors);
        Assert.Equal("diagnostic E9010", log.Lines[^2]);
        Assert.Equal("finished Hello.pproj success=False upToDate=False 1/0/0", log.Lines[^1]);
        Assert.Single(log.Events.OfType<BuildEvent.Started>());
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9010: Build cancelled", log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));                         // partial output removed
        Assert.Null(result.Projects[0].OutputPath);
    }

    [Fact]
    [Trait("Req", "CMP-804")]
    public async Task MissingProjectFile_IsE9003_NotAnException()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request(path: @"C:\p\Nope.pproj"));

        Assert.Equal(["started Nope.pproj Debug win64 Build", "diagnostic E9003", "finished Nope.pproj success=False upToDate=False 1/0/0"], log.Lines);
        Assert.False(result.Success);
    }

    [Fact]
    [Trait("Req", "CMP-804")]
    public async Task MalformedProjectFile_IsE9002()
    {
        var w = new BuildWorkspace();
        w.Fs.Add(BuildWorkspace.ProjectPath, """{ "formatVersion": 1, "name": "Hello", }""");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Contains("diagnostic E9002", log.Lines);
        Assert.Single(log.Events.OfType<BuildEvent.Finished>());
    }

    [Fact]
    [Trait("Req", "CMP-842")]
    public async Task UnknownConfiguration_IsE9005_AndMatchingIsCaseInsensitive()
    {
        var w = new BuildWorkspace();

        var (bad, badLog) = await w.RunAsync(w.Request() with { Configuration = "Profile" });
        var (good, goodLog) = await w.RunAsync(w.Request() with { Configuration = "release" });

        Assert.False(bad.Success);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9005: Unknown configuration 'Profile'", badLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.True(good.Success);
        Assert.Equal("started Hello.pproj Release win64 Build", goodLog.Lines[0]);
        Assert.Equal("Release", good.Projects[0].Configuration);
        Assert.True(w.Fs.FileExists(@"C:\p\bin\Release\Hello.exe"));
    }

    [Fact]
    [Trait("Req", "CMP-835")]
    public async Task MissingListedFiles_AreWarnings_BuildContinues()
    {
        // the events half of the former test: BuildEventsTests.PreBuildRunsBeforeCompile_PostBuildAfterLink (CMP-1275: W9001 is retired)
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra:
            """ "files": [{ "path": "Hello.pas" }, { "path": "Gone.pas" }]"""));

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): warning W9002: File 'C:\p\Gone.pas' listed in the project does not exist"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.Equal("finished Hello.pproj success=True upToDate=False 0/1/0", log.Lines[^1]);
        Assert.Equal(1, result.Warnings);
    }

    [Fact]
    [Trait("Req", "CMP-835")]
    public async Task MissingMainSource_IsE9004()
    {
        var w = new BuildWorkspace();
        w.Fs.Delete(BuildWorkspace.MainPath);

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9004: Main source 'C:\p\Hello.pas' not found", log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-804")]
    public async Task LinkErrors_AreDiagnostics_AndToolOutputLines()
    {
        var w = new BuildWorkspace();
        w.Tools.LinkFailure = (["lld-link: error: undefined symbol: Foo"], 1);

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Contains("tool lld-link: error: undefined symbol: Foo", log.Lines);
        Assert.Contains(log.Events.OfType<BuildEvent.Diagnostic>(), d => d.Value.Code == "E4005");
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-804")]
    public async Task MissingToolchain_IsE4001_Diagnostic()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request() with { ToolchainDirectory = @"C:\nollvm" });

        Assert.False(result.Success);
        Assert.Contains(log.Events.OfType<BuildEvent.Diagnostic>(), d => d.Value.Code == "E4001" && d.Value.Severity == DiagnosticSeverity.Error);
    }
}
