using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-145: the <c>Run</c> target (build-engine.md CMP-833).</summary>
[Trait("Phase", "3")]
public sealed class RunTests
{
    private static BuildRequest RunRequest(BuildWorkspace w) => w.Request(BuildTarget.Run);

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Run_ForwardsStdoutAndStderr_ReturnsTheExitCode_InsideTheProjectBracket()
    {
        var w = new BuildWorkspace();
        w.Tools.Program = (_, _, _) => (["out 1", "out 2"], ["err 1"], 3);

        var (result, log) = await w.RunAsync(RunRequest(w));

        Assert.Equal(
            [
                "started Hello.pproj Debug win64 Run",
                "info Compiling Hello",
                "info Compiling System",
                "info Linking Hello.exe",
                "tool fake lld-link done",
                "programstdout out 1",
                "programstdout out 2",
                "programstderr err 1",
                "info Program exited with code 3",
                "finished Hello.pproj success=True upToDate=False 0/0/0",
            ],
            log.Lines);
        Assert.True(result.Success);                                                  // the build, not the exit code
        Assert.Equal(3, result.RunExitCode);
        Assert.Equal(BuildWorkspace.Exe, Assert.Single(w.Tools.Programs).FileName);
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Run_UpToDateProject_StillRuns()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());

        var (result, log) = await w.RunAsync(RunRequest(w));

        Assert.Equal(["Hello: up to date", "Program exited with code 0"], log.Info);
        Assert.Equal(0, result.RunExitCode);
        Assert.True(result.UpToDate);
    }

    [Theory]
    [Trait("Req", "CMP-833")]
    [InlineData("", new string[0])]
    [InlineData("a  b\tc", new[] { "a", "b", "c" })]
    [InlineData("\"b c\" d", new[] { "b c", "d" })]
    [InlineData("d\\\"e", new[] { "d\"e" })]
    [InlineData("\"f\\\\\" g", new[] { "f\\", "g" })]
    [InlineData("C:\\dir\\file.txt \"\"", new[] { "C:\\dir\\file.txt", "" })]
    [InlineData("a\\\\\\\\\"b c\"", new[] { "a\\\\b c" })]
    [InlineData("\"say \"\"hi\"\"\"", new[] { "say \"hi\"" })]
    public async Task Run_ProjectArguments_AreSplitWithWindowsRules(string arguments, string[] expected)
    {
        var json = BuildWorkspace.ProjectJson("Hello", extra: $$""" "run": { "arguments": {{System.Text.Json.JsonSerializer.Serialize(arguments)}}, "workingDir": "" }""");
        var w = new BuildWorkspace(projectJson: json);

        await w.RunAsync(RunRequest(w));

        Assert.Equal(expected, Assert.Single(w.Tools.Programs).Arguments);
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Run_RequestArguments_OverrideTheProjectArguments()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "run": { "arguments": "from project", "workingDir": "" }"""));

        await w.RunAsync(RunRequest(w) with { RunArguments = "from \"the request\"" });

        Assert.Equal(["from", "the request"], Assert.Single(w.Tools.Programs).Arguments);
    }

    [Theory]
    [Trait("Req", "CMP-833")]
    [InlineData("", @"C:\p\bin\Debug")]
    [InlineData("data", @"C:\p\data")]
    [InlineData("../shared", @"C:\shared")]
    [InlineData(@"D:\abs", @"D:\abs")]
    public async Task Run_WorkingDirectory_IsRelativeToTheProject_EmptyIsTheExeDirectory(string workingDir, string expected)
    {
        var json = BuildWorkspace.ProjectJson("Hello", extra: $$""" "run": { "arguments": "", "workingDir": {{System.Text.Json.JsonSerializer.Serialize(workingDir)}} }""");
        var w = new BuildWorkspace(projectJson: json);

        await w.RunAsync(RunRequest(w));

        Assert.Equal(expected, Assert.Single(w.Tools.Programs).WorkingDirectory);
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Run_FailedBuild_DoesNotStartTheProgram()
    {
        var w = new BuildWorkspace("program Hello; begin Foo := 1 end.");

        var (result, log) = await w.RunAsync(RunRequest(w));

        Assert.False(result.Success);
        Assert.Null(result.RunExitCode);
        Assert.Empty(w.Tools.Programs);
        Assert.DoesNotContain(log.Info, l => l.StartsWith("Program exited", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Run_ExeMissingAfterASuccessfulBuild_IsE9009()
    {
        var w = new BuildWorkspace();
        w.Tools.LinkWritesNothing = true;

        var (result, log) = await w.RunAsync(RunRequest(w));

        Assert.False(result.Success);
        Assert.Null(result.RunExitCode);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9009: Cannot run: project output 'C:\p\bin\Debug\Hello.exe' does not exist",
            log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Empty(w.Tools.Programs);
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    [Trait("Req", "CMP-801")]
    public async Task Run_BuildDiagnosticsArriveBeforeTheProgramOutput()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "events": { "preBuild": ["x"], "postBuild": [] }"""));
        w.Tools.Program = (_, _, _) => (["hello"], [], 0);

        var (_, log) = await w.RunAsync(RunRequest(w));

        var lines = log.Lines;
        Assert.True(lines.IndexOf("diagnostic W9001") < lines.IndexOf("programstdout hello"));
        Assert.Equal("finished Hello.pproj success=True upToDate=False 0/1/0", lines[^1]);
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    [Trait("Req", "CMP-803")]
    public async Task Run_Cancel_StopsTheProgram_E9010()
    {
        var w = new BuildWorkspace();
        using var cts = new CancellationTokenSource();
        var running = new TaskCompletionSource();
        w.Tools.BeforeTool = async (tool, _, ct) =>
        {
            if (tool == "Hello.exe")
            {
                running.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };

        var run = w.RunAsync(RunRequest(w), cts.Token);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        cts.Cancel();
        var (result, log) = await run;

        Assert.True(result.Canceled);
        Assert.Null(result.RunExitCode);
        Assert.Equal(["diagnostic E9010", "finished Hello.pproj success=False upToDate=False 1/0/0"], log.Lines[^2..]);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));                           // a complete exe is not a partial output
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "CMP-833")]
    public async Task Run_NotRedirected_TheChildInheritsTheConsole_AndItsExitCodeIsReturned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var llvm = Path.Combine(dir, "llvm");
        Directory.CreateDirectory(llvm);
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            File.WriteAllText(Path.Combine(llvm, tool), "");
        }

        File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello", extra: """ "run": { "arguments": "/c exit 7", "workingDir": "" }"""));
        File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);
        var tools = new FakeToolchain(PhysicalFileSystem.Instance)
        {
            LinkCopiesFrom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),     // a real console exe
        };
        var log = new EventLog();
        try
        {
            var result = await new BuildEngine(PhysicalFileSystem.Instance, tools).RunAsync(
                new BuildRequest
                {
                    ProjectPath = Path.Combine(dir, "Hello.pproj"),
                    Target = BuildTarget.Run,
                    RedirectRunOutput = false,
                    ToolchainDirectory = llvm,
                    RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                },
                log,
                TestContext.Current.CancellationToken);

            Assert.True(result.Success, string.Join("\n", log.Lines));
            Assert.Equal(7, result.RunExitCode);
            Assert.Empty(tools.Programs);                                              // not through the redirecting runner
            Assert.Contains("Program exited with code 7", log.Info);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
