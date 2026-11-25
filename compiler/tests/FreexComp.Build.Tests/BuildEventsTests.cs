using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-328: the build event commands of a project in the engine (build-events.md CMP-1256…1277) — in-memory file system, fake
/// tools and the recording fake command runner of <see cref="BuildWorkspace.Commands"/>; no test here starts a process.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class BuildEventsTests
{
    private const string TrustFile = @"C:\user\roaming\trust.json";
    private const string BrokenProgram = "program Hello; begin Foo := 1 end.";

    private static BuildWorkspace Workspace(string events, string extra = "", string program = BuildWorkspace.HelloProgram) =>
        new(program, BuildWorkspace.ProjectJson("Hello", extra: $"\"events\": {events}{(extra.Length > 0 ? ", " + extra : "")}"));

    /// <summary>The caller vouches for the commands (as <c>pbuild --trust-events</c>): behind the gate.</summary>
    private static BuildRequest Trusting(BuildWorkspace w, BuildTarget target = BuildTarget.Build, string path = BuildWorkspace.ProjectPath) =>
        w.Request(target, path) with { Events = EventsPolicy.Run };

    private static ImmutableArray<string> Diagnostics(EventLog log) => [.. log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())];

    /// <summary>Adds <c>C:\g\&lt;name&gt;\&lt;name&gt;.pproj</c> with a program <c>&lt;name&gt;.pas</c>.</summary>
    private static void AddProject(BuildWorkspace w, string name, string extra = "", string? program = null)
    {
        w.Fs.Add($@"C:\g\{name}\{name}.pproj", BuildWorkspace.ProjectJson(name, name + ".pas", extra));
        w.Fs.Add($@"C:\g\{name}\{name}.pas", program ?? $"program {name}; begin Writeln('{name}') end.");
    }

    [Fact]
    [Trait("Req", "CMP-1260")]
    [Trait("Req", "CMP-1261")]
    [Trait("Req", "CMP-1264")]
    public async Task PreBuildRunsBeforeCompile_PostBuildAfterLink()
    {
        var w = Workspace("""{ "preBuild": ["gen $(Config)", "check"], "postBuild": ["pack \"$(OutputPath)\""] }""");
        var toolsStartedAtCommand = new List<(string Command, string Tools, bool Exe)>();
        w.Commands.Behavior = (command, _) =>
        {
            toolsStartedAtCommand.Add((command, string.Join(",", w.Tools.Calls.Distinct().Order(StringComparer.Ordinal)), w.Fs.FileExists(BuildWorkspace.Exe)));
            return (["out of " + command.Split(' ')[0]], [], 0);
        };

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.True(result.Success);
        Assert.Empty(Diagnostics(log));
        // pre-build before any tool started; post-build after the linker wrote the output
        Assert.Equal(
            [("gen Debug", "", false), ("check", "", false), (@"pack ""C:\p\bin\Debug\Hello.exe""", "clang.exe,lld-link.exe,llvm-dlltool.exe", true)],
            toolsStartedAtCommand);
        Assert.Equal(
            ["started Hello.pproj Debug win64 Build",
             "info Running pre-build command 1 of 2", "tool out of gen",
             "info Running pre-build command 2 of 2", "tool out of check",
             "info Compiling System", "info Compiling Hello", "info Linking Hello.exe", "tool fake lld-link done",
             "info Running post-build command 1 of 1", "tool out of pack",
             "finished Hello.pproj success=True upToDate=False 0/0/0"],
            log.Lines);
        Assert.DoesNotContain(log.Events, e => e is BuildEvent.CommandLine);    // without Verbose the expanded text is not shown

        // Verbose: after each Info line a CommandLine event with the expanded command exactly, no unit and no DLL
        var verbose = Workspace("""{ "preBuild": ["gen $(Config)"], "postBuild": ["pack \"$(OutputPath)\""] }""");
        var (_, verboseLog) = await verbose.RunAsync(Trusting(verbose) with { Verbose = true });
        var events = verboseLog.Events;
        foreach (var (info, text) in new[] { ("Running pre-build command 1 of 1", "gen Debug"), ("Running post-build command 1 of 1", @"pack ""C:\p\bin\Debug\Hello.exe""") })
        {
            var at = events.IndexOf(events.Single(e => e is BuildEvent.OutputLine { Source: OutputSource.Info } line && line.Text == info));
            var echo = Assert.IsType<BuildEvent.CommandLine>(events[at + 1]);
            Assert.Equal(text, echo.Text);
            Assert.Null(echo.Unit);
            Assert.Null(echo.Dll);
        }

        Assert.Equal(["gen Debug", @"pack ""C:\p\bin\Debug\Hello.exe"""], verbose.Commands.Commands);
    }

    [Fact]
    [Trait("Req", "CMP-1263")]
    public async Task CommandsRunInTheProjectDirectory()
    {
        // run.workingDir plays no part
        var w = Workspace("""{ "preBuild": ["a"], "postBuild": ["b"] }""", """ "run": { "arguments": "", "workingDir": "elsewhere" }""");

        var (result, _) = await w.RunAsync(Trusting(w, BuildTarget.Run));

        Assert.True(result.Success);
        Assert.Equal([("a", @"C:\p"), ("b", @"C:\p")], w.Commands.Calls);
        Assert.Equal(@"C:\p\elsewhere", Assert.Single(w.Tools.Programs).WorkingDirectory);
    }

    [Fact]
    [Trait("Req", "CMP-1265")]
    [Trait("Req", "CMP-1274")]
    public async Task OutputLinesBecomeToolOutput()
    {
        const string LooksLikeADiagnostic = @"C:\p\Hello.pas(1,1): error E2003: Undeclared identifier 'Foo'";
        var w = Workspace("""{ "preBuild": ["talk"], "postBuild": [] }""", """ "files": [{ "path": "Hello.pas" }, { "path": "Gone.pas" }]""");
        w.Commands.Behavior = (_, _) => (["out 1", LooksLikeADiagnostic, "out 3"], ["err 1", "warning W1000: also not parsed"], 0);

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.True(result.Success);                                            // nothing of the output is parsed into a diagnostic
        Assert.Equal((0, 1), (result.Errors, result.Warnings));
        Assert.Equal([@"C:\p\Hello.pproj(0,0): warning W9002: File 'C:\p\Gone.pas' listed in the project does not exist"], Diagnostics(log));
        var lines = log.Lines;
        Assert.Equal(
            ["info Running pre-build command 1 of 1", "tool out 1", "tool " + LooksLikeADiagnostic, "tool out 3", "tool err 1", "tool warning W1000: also not parsed", "info Compiling System"],
            lines[1..8]);
        Assert.True(lines.IndexOf("tool err 1") < lines.IndexOf("diagnostic W9002"));     // the diagnostics come after the commands' output
    }

    [Theory]
    [Trait("Req", "CMP-1266")]
    [InlineData("success")]
    [InlineData("always")]
    public async Task FailingPreBuild_E9020_NoCompileNoPostBuild(string postBuildWhen)
    {
        var w = Workspace($$"""{ "preBuild": ["one", "two $(Config)", "three"], "postBuild": ["after"], "postBuildWhen": "{{postBuildWhen}}" }""");
        w.Commands.Behavior = (command, _) => ([], ["cannot"], command.StartsWith("two", StringComparison.Ordinal) ? 2 : 0);

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.False(result.Success);
        Assert.Equal(1, result.Errors);
        // the command as stored, not expanded
        Assert.Equal([@"C:\p\Hello.pproj(0,0): error E9020: Build event failed: pre-build command 2 ended with exit code 2: two $(Config)"], Diagnostics(log));
        Assert.Equal(["one", "two Debug"], w.Commands.Commands);                // the third is not run, nor the post-build list
        Assert.Empty(w.Tools.Calls);                                            // no compile step
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\p"));
        Assert.Equal(
            ["started Hello.pproj Debug win64 Build", "info Running pre-build command 1 of 3", "tool cannot", "info Running pre-build command 2 of 3", "tool cannot",
             "diagnostic E9020", "finished Hello.pproj success=False upToDate=False 1/0/0"],
            log.Lines);
    }

    [Fact]
    [Trait("Req", "CMP-1266")]
    public async Task FailingPostBuild_E9020_OutputStays()
    {
        var w = Workspace("""{ "preBuild": [], "postBuild": ["pack \"$(OutputPath)\"", "never"] }""");
        w.Commands.Behavior = (_, _) => ([], [], 3);

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.False(result.Success);
        Assert.Null(result.OutputPath);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E9020: Build event failed: post-build command 1 ended with exit code 3: pack ""$(OutputPath)"""],
            Diagnostics(log));
        Assert.Equal([@"pack ""C:\p\bin\Debug\Hello.exe"""], w.Commands.Commands);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));                       // the output is complete: it stays
        Assert.Equal("finished Hello.pproj success=False upToDate=False 1/0/0", log.Lines[^1]);

        // a negative exit code is written as it is
        w.Commands.Behavior = (_, _) => ([], [], -1073741510);
        var (_, negativeLog) = await w.RunAsync(Trusting(w));
        Assert.EndsWith("post-build command 1 ended with exit code -1073741510: pack \"$(OutputPath)\"", Assert.Single(Diagnostics(negativeLog)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-1267")]
    public async Task PostBuildWhen_SuccessSkipsAfterFailedCompile_AlwaysRuns()
    {
        const string Events = """{ "preBuild": ["pre"], "postBuild": ["post \"$(OutputPath)\""], "postBuildWhen": "WHEN" }""";
        static string With(string when) => Events.Replace("WHEN", when, StringComparison.Ordinal);
        const string Post = @"post ""C:\p\bin\Debug\Hello.exe""";

        // success (also the default): a failed compile step → no post-build
        foreach (var events in new[] { With("success"), Events.Replace(""", "postBuildWhen": "WHEN" """.Trim(), "", StringComparison.Ordinal) })
        {
            var w = Workspace(events, program: BrokenProgram);
            var (result, log) = await w.RunAsync(Trusting(w));
            Assert.False(result.Success);
            Assert.Equal(["pre"], w.Commands.Commands);
            Assert.Equal(["E2003"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
        }

        // always: it runs after a failed compile step — the macros keep their values — and the project stays failed
        var always = Workspace(With("always"), program: BrokenProgram);
        var (alwaysResult, alwaysLog) = await always.RunAsync(Trusting(always));
        Assert.False(alwaysResult.Success);
        Assert.Equal(["pre", Post], always.Commands.Commands);
        Assert.False(always.Fs.FileExists(BuildWorkspace.Exe));
        Assert.Equal(["E2003"], alwaysLog.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
        Assert.Equal("finished Hello.pproj success=False upToDate=False 1/0/0", alwaysLog.Lines[^1]);

        // … after a failed link step too; a failing command adds its E9020
        var link = Workspace(With("always"));
        link.Tools.LinkFailure = (["lld-link: error: undefined symbol: X"], 1);
        link.Commands.Behavior = (command, _) => ([], [], command == "pre" ? 0 : 5);
        var (linkResult, linkLog) = await link.RunAsync(Trusting(link));
        Assert.False(linkResult.Success);
        Assert.Equal(["pre", Post], link.Commands.Commands);
        var linkDiagnostics = Diagnostics(linkLog);
        Assert.Equal(2, linkDiagnostics.Length);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9020: Build event failed: post-build command 1 ended with exit code 5: post ""$(OutputPath)""", linkDiagnostics[^1]);

        // … and after a successful build, as with success
        var fine = Workspace(With("always"));
        Assert.True((await fine.RunAsync(Trusting(fine))).Result.Success);
        Assert.Equal(["pre", Post], fine.Commands.Commands);

        // in both modes not when the main source is missing (E9004) …
        foreach (var when in new[] { "success", "always" })
        {
            var missing = Workspace(With(when));
            missing.Fs.Delete(BuildWorkspace.MainPath);
            var (missingResult, missingLog) = await missing.RunAsync(Trusting(missing));
            Assert.False(missingResult.Success);
            Assert.Equal(["E9004"], missingLog.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
            Assert.Equal(["pre"], missing.Commands.Commands);

            // … nor after a failed pre-build command
            var failed = Workspace(With(when));
            failed.Commands.Behavior = (_, _) => ([], [], 1);
            Assert.False((await failed.RunAsync(Trusting(failed))).Result.Success);
            Assert.Equal(["pre"], failed.Commands.Commands);
        }
    }

    [Theory]
    [Trait("Req", "CMP-1259")]
    [InlineData(BuildTarget.Build)]
    [InlineData(BuildTarget.Rebuild)]
    [InlineData(BuildTarget.Run)]
    public async Task Validation_UnknownMacroOrControlCharacter_FailsBeforeAnythingRuns(BuildTarget target)
    {
        // a first build without events, so that there is something a rebuild could delete
        var w = new BuildWorkspace();
        Assert.True((await w.RunAsync(w.Request())).Result.Success);
        var calls = w.Tools.Calls.Length;
        var written = w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe);
        var objects = w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*").ToList();
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """
            "events": {
              "preBuild": ["fine $(config)", "echo $(Nope) $(PROJECTNAME) $(Other_1) $(nope)", "  "],
              "postBuild": ["line1\nline2", "tab\tis fine", "del\u007f $(Missing)", "cr\r", "nul\u0000"]
            }
            """));

        var (result, log) = await w.RunAsync(Trusting(w, target));

        Assert.False(result.Success);
        // all findings, the pre-build list first, in list order; numbers count runnable commands
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E9022: Unknown macro '$(Nope)' in pre-build command 2 (known: ProjectDir, ProjectName, Config, Platform, OutputDir, OutputPath)",
             @"C:\p\Hello.pproj(0,0): error E9022: Unknown macro '$(Other_1)' in pre-build command 2 (known: ProjectDir, ProjectName, Config, Platform, OutputDir, OutputPath)",
             @"C:\p\Hello.pproj(0,0): error E9023: Build event not run: post-build command 1 contains a control character",
             @"C:\p\Hello.pproj(0,0): error E9022: Unknown macro '$(Missing)' in post-build command 3 (known: ProjectDir, ProjectName, Config, Platform, OutputDir, OutputPath)",
             @"C:\p\Hello.pproj(0,0): error E9023: Build event not run: post-build command 3 contains a control character",
             @"C:\p\Hello.pproj(0,0): error E9023: Build event not run: post-build command 4 contains a control character",
             @"C:\p\Hello.pproj(0,0): error E9023: Build event not run: post-build command 5 contains a control character"],
            Diagnostics(log));
        Assert.Equal(["started Hello.pproj Debug win64 " + target, .. Enumerable.Repeat("diagnostic", 7), "finished Hello.pproj success=False upToDate=False 7/0/0"],
            log.Lines.Select(l => l.StartsWith("diagnostic ", StringComparison.Ordinal) ? "diagnostic" : l));
        Assert.Empty(w.Commands.Calls);                                         // not even the valid first command
        Assert.Empty(w.Tools.Programs);
        Assert.Equal(calls, w.Tools.Calls.Length);                              // no process
        Assert.Equal(written, w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe));    // nothing written, nothing deleted (also for Rebuild)
        Assert.Equal(objects, w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
    }

    [Fact]
    [Trait("Req", "CMP-1262")]
    [Trait("Req", "CMP-1264")]
    public async Task BlankCommandsAreNotRunAndNotCounted()
    {
        var w = Workspace("""{ "preBuild": ["", "   ", "a", "\t", " b "], "postBuild": [" ", "c", ""] }""");

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.True(result.Success);
        Assert.Equal(["a", " b ", "c"], w.Commands.Commands);                   // nothing is trimmed
        Assert.Equal(
            ["Running pre-build command 1 of 2", "Running pre-build command 2 of 2", "Running post-build command 1 of 1"],
            log.Info.Where(l => l.StartsWith("Running ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Req", "CMP-1262")]
    public async Task EventsRunOnEveryBuild_ProjectIsNeverUpToDate()
    {
        foreach (var events in new[] { """{ "preBuild": ["pre"], "postBuild": ["post"] }""", """{ "preBuild": [], "postBuild": ["post"] }""", """{ "preBuild": ["pre"] }""" })
        {
            var w = Workspace(events);
            var (first, _) = await w.RunAsync(Trusting(w));
            var calls = w.Tools.Calls.Length;
            var commands = w.Commands.Commands;
            var written = w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe);

            var (second, log) = await w.RunAsync(Trusting(w));

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.Equal([.. commands, .. commands], w.Commands.Commands);      // the commands again
            Assert.False(second.UpToDate);
            Assert.False(second.Projects[0].UpToDate);
            Assert.Equal(BuildWorkspace.Exe, second.OutputPath);
            Assert.DoesNotContain("Hello: up to date", log.Info);
            Assert.All(log.Info, line => Assert.StartsWith("Running ", line, StringComparison.Ordinal));
            Assert.Equal("finished Hello.pproj success=True upToDate=False 0/0/0", log.Lines[^1]);
            Assert.Equal(calls, w.Tools.Calls.Length);                          // no unit recompiled, nothing linked: those steps stay incremental
            Assert.Equal(written, w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe));
        }
    }

    [Theory]
    [Trait("Req", "CMP-1262")]
    [InlineData("""{ "preBuild": [], "postBuild": [] }""")]
    [InlineData("""{ "preBuild": ["", "  "], "postBuild": ["\t"], "postBuildWhen": "always" }""")]
    public async Task NoRunnableCommand_BehavesAsBefore(string events)
    {
        var w = Workspace(events);

        var (first, firstLog) = await w.RunAsync(Trusting(w));
        var (second, secondLog) = await w.RunAsync(Trusting(w));

        Assert.True(first.Success);
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.exe"], firstLog.Info);
        Assert.True(second.UpToDate);
        Assert.Equal(["Hello: up to date"], secondLog.Info);
        Assert.Equal("finished Hello.pproj success=True upToDate=True 0/0/0", secondLog.Lines[^1]);
        Assert.Empty(w.Commands.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-1261")]
    public async Task PreBuildMayCreateTheMainSourceAndListedFiles()
    {
        var w = Workspace("""{ "preBuild": ["generate"], "postBuild": [] }""", """ "files": [{ "path": "Hello.pas" }, { "path": "Gen.pas" }]""");
        w.Fs.Delete(BuildWorkspace.MainPath);
        w.Commands.Before = (_, _) =>
        {
            w.Fs.Add(BuildWorkspace.MainPath, "program Hello; uses Gen; begin Writeln(Answer) end.");
            w.Fs.Add(@"C:\p\Gen.pas", "unit Gen; interface const Answer = 42; implementation end.");
            return Task.CompletedTask;
        };

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.True(result.Success);                                            // the checks of the files come after pre-build: no E9004, no W9002
        Assert.Empty(Diagnostics(log));
        Assert.Equal(["Running pre-build command 1 of 1", "Compiling System", "Compiling Gen", "Compiling Hello", "Linking Hello.exe"], log.Info);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-1252")]
    [Trait("Req", "CMP-1257")]
    public async Task PerConfigurationEventsReplaceProjectEvents()
    {
        var w = Workspace("""{ "preBuild": ["project pre"], "postBuild": ["project post"] }""", """
            "configurations": {
              "Debug": { "optimization": 0 },
              "Release": { "optimization": 2, "events": { "preBuild": ["release $(Config) $(OutputDir)"] } },
              "Demo": { "inherits": "Release" },
              "Quiet": { "inherits": "Release", "events": {} }
            }
            """);

        async Task<ImmutableArray<string>> Build(string configuration)
        {
            var before = w.Commands.Calls.Length;
            var (result, _) = await w.RunAsync(Trusting(w) with { Configuration = configuration });
            Assert.True(result.Success);
            return w.Commands.Commands[before..];
        }

        Assert.Equal(["project pre", "project post"], await Build("Debug"));
        Assert.Equal([@"release Release C:\p\bin\Release"], await Build("Release"));           // replaced as a whole: no project post-build
        Assert.Equal([@"release Demo C:\p\bin\Demo"], await Build("Demo"));                    // inherited; the macros are those of the built configuration
        Assert.Equal([@"release Demo C:\p\bin\Demo"], await Build("demo"));                    // Config as spelled in the project file
        Assert.Empty(await Build("Quiet"));                                                    // {} runs nothing
        var (again, log) = await w.RunAsync(Trusting(w) with { Configuration = "Quiet" });
        Assert.True(again.UpToDate);
        Assert.Equal(["Hello: up to date"], log.Info);
    }

    [Fact]
    [Trait("Req", "CMP-1260")]
    [Trait("Req", "CMP-1261")]
    public async Task Rebuild_CleansThenRunsEvents_CleanRunsNone()
    {
        var w = Workspace("""{ "preBuild": ["pre"], "postBuild": ["post"] }""");
        Assert.True((await w.RunAsync(w.Request() with { Events = EventsPolicy.Skip })).Result.Success);
        Assert.Empty(w.Commands.Calls);
        var seen = new List<string>();
        w.Commands.Behavior = (command, _) =>
        {
            seen.Add($"{command}: exe={w.Fs.FileExists(BuildWorkspace.Exe)} obj={w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*").Any()}");
            return ([], [], 0);
        };

        var (rebuild, rebuildLog) = await w.RunAsync(Trusting(w, BuildTarget.Rebuild));

        Assert.True(rebuild.Success);
        Assert.Equal(["pre: exe=False obj=False", "post: exe=True obj=True"], seen);            // Clean, then pre-build, compile and link, post-build
        Assert.Equal(
            ["Running pre-build command 1 of 1", "Compiling System", "Compiling Hello", "Linking Hello.exe", "Running post-build command 1 of 1"],
            rebuildLog.Info);

        // Clean runs no command — trusted or not
        seen.Clear();
        var (clean, cleanLog) = await w.RunAsync(Trusting(w, BuildTarget.Clean));
        Assert.True(clean.Success);
        Assert.Empty(seen);
        Assert.Equal(["started Hello.pproj Debug win64 Clean", "finished Hello.pproj success=True upToDate=False 0/0/0"], cleanLog.Lines);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-1261")]
    [Trait("Req", "CMP-1274")]
    public async Task RunTarget_PostBuildFinishesBeforeTheProgram()
    {
        var w = Workspace("""{ "preBuild": [], "postBuild": ["post"] }""");
        w.Tools.Program = (_, _, _) => (["hello"], [], 0);
        w.Commands.Behavior = (_, _) => (["post out"], [], 0);

        var (result, log) = await w.RunAsync(Trusting(w, BuildTarget.Run));

        Assert.True(result.Success);
        Assert.Equal(0, result.RunExitCode);
        var lines = log.Lines;
        Assert.Equal(
            ["tool fake lld-link done", "info Running post-build command 1 of 1", "tool post out", "programstdout hello", "info Program exited with code 0"],
            lines[lines.IndexOf("tool fake lld-link done")..^1]);

        // a failed post-build command: the program is not started; E9020 is an ordinary build diagnostic
        w.Commands.Behavior = (_, _) => ([], [], 1);
        var (failed, failedLog) = await w.RunAsync(Trusting(w, BuildTarget.Run));
        Assert.False(failed.Success);
        Assert.Null(failed.RunExitCode);
        Assert.Single(w.Tools.Programs);                                        // only the first run
        Assert.Equal(["diagnostic E9020", "finished Hello.pproj success=False upToDate=False 1/0/0"], failedLog.Lines[^2..]);
    }

    [Fact]
    [Trait("Req", "CMP-1269")]
    public async Task Cancellation_StopsTheCommandAndRunsNoFurther()
    {
        // while a pre-build command runs
        var w = Workspace("""{ "preBuild": ["first", "second"], "postBuild": ["after"], "postBuildWhen": "always" }""");
        using var cts = new CancellationTokenSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Commands.Before = async (_, ct) =>
        {
            running.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);                             // the runner's duty: it ends when the build is cancelled
        };

        var run = w.RunAsync(Trusting(w), cts.Token);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        cts.Cancel();
        var (result, log) = await run;

        Assert.True(result.Canceled);
        Assert.False(result.Success);
        Assert.Equal(["first"], w.Commands.Commands);                           // no further command, also not the `always` post-build list
        Assert.Empty(w.Tools.Calls);
        Assert.Equal(
            ["started Hello.pproj Debug win64 Build", "info Running pre-build command 1 of 2", "diagnostic E9010", "finished Hello.pproj success=False upToDate=False 1/0/0"],
            log.Lines);

        // while the linker runs: the `always` post-build list is not run either
        var link = Workspace("""{ "preBuild": ["first"], "postBuild": ["after"], "postBuildWhen": "always" }""");
        using var linkCts = new CancellationTokenSource();
        var linking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Tools.BeforeTool = async (tool, _, ct) =>
        {
            if (tool == "lld-link.exe")
            {
                linking.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        var linkRun = link.RunAsync(Trusting(link), linkCts.Token);
        await linking.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        linkCts.Cancel();
        var (linkResult, linkLog) = await linkRun;
        Assert.True(linkResult.Canceled);
        Assert.Equal(["first"], link.Commands.Commands);
        Assert.Equal("diagnostic E9010", linkLog.Lines[^2]);

        // while a post-build command runs: the output the build produced is complete and stays
        var post = Workspace("""{ "preBuild": [], "postBuild": ["slow", "never"] }""");
        using var postCts = new CancellationTokenSource();
        var posting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        post.Commands.Before = async (_, ct) =>
        {
            posting.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        var postRun = post.RunAsync(Trusting(post), postCts.Token);
        await posting.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        postCts.Cancel();
        var (postResult, _) = await postRun;
        Assert.True(postResult.Canceled);
        Assert.Equal(["slow"], post.Commands.Commands);
        Assert.True(post.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-1268")]
    public async Task CommandThatCannotStart_E9020MinusOne()
    {
        var w = Workspace("""{ "preBuild": ["start me", "next"], "postBuild": [] }""");
        w.Commands.Behavior = (_, _) => throw new System.ComponentModel.Win32Exception(2, "The command interpreter was not found");

        var (result, log) = await w.RunAsync(Trusting(w));

        Assert.False(result.Success);
        Assert.Equal(
            ["started Hello.pproj Debug win64 Build", "info Running pre-build command 1 of 2", "info The command interpreter was not found",
             "diagnostic E9020", "finished Hello.pproj success=False upToDate=False 1/0/0"],
            log.Lines);
        Assert.Equal([@"C:\p\Hello.pproj(0,0): error E9020: Build event failed: pre-build command 1 ended with exit code -1: start me"], Diagnostics(log));
        Assert.Equal(["start me"], w.Commands.Commands);
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-1260")]
    [Trait("Req", "CMP-1277")]
    public async Task Group_EachProjectRunsItsOwnEvents_FailureSkipsDependants()
    {
        const string Group = @"C:\g\All.pgroup";
        BuildWorkspace Bed()
        {
            var w = new BuildWorkspace();
            AddProject(w, "Lib", """ "events": { "preBuild": ["lib $(ProjectName) $(ProjectDir)"], "postBuild": ["lib done $(OutputPath)"] }""");
            AddProject(w, "App", """ "dependencies": ["../Lib/Lib.pproj"], "events": { "preBuild": ["app $(ProjectName) $(OutputDir) $(Config) $(Platform)"], "postBuild": [] }""");
            AddProject(w, "Tool", """ "events": { "preBuild": [], "postBuild": ["tool $(ProjectName)"] }""");
            AddProject(w, "Plain");
            w.Fs.Add(Group, """{ "formatVersion": 1, "projects": ["Tool/Tool.pproj", "App/App.pproj", "Lib/Lib.pproj", "Plain/Plain.pproj"], "activeProject": "App/App.pproj" }""");
            return w;
        }

        // each project its own commands, macro values and directory, inside its own Started … Finished bracket
        var w = Bed();
        var (result, log) = await w.RunAsync(Trusting(w, path: Group));

        Assert.True(result.Success);
        Assert.Equal(
            [("tool Tool", @"C:\g\Tool"),
             (@"lib Lib C:\g\Lib", @"C:\g\Lib"), (@"lib done C:\g\Lib\bin\Debug\Lib.exe", @"C:\g\Lib"),
             (@"app App C:\g\App\bin\Debug Debug win64", @"C:\g\App")],
            w.Commands.Calls.OrderBy(c => Array.IndexOf(["Tool", "Lib", "App"], Path.GetFileName(c.WorkingDirectory))));
        var bracket = "";
        foreach (var e in log.Events)
        {
            switch (e)
            {
                case BuildEvent.Started s:
                    bracket = Path.GetFileNameWithoutExtension(s.ProjectPath);
                    break;
                case BuildEvent.OutputLine { Source: OutputSource.Info } line when line.Text.StartsWith("Running ", StringComparison.Ordinal):
                    Assert.Equal(Path.GetFileNameWithoutExtension(line.ProjectPath), bracket);
                    Assert.NotEqual("Plain", bracket);
                    break;
            }
        }

        Assert.Equal(4, log.Info.Count(l => l.StartsWith("Running ", StringComparison.Ordinal)));

        // a project that fails because of an event fails like any other: its dependant is skipped, independent projects are built
        var failing = Bed();
        failing.Commands.Behavior = (command, _) => ([], [], command.StartsWith("lib ", StringComparison.Ordinal) ? 9 : 0);
        var (failed, failedLog) = await failing.RunAsync(Trusting(failing, path: Group));

        Assert.False(failed.Success);
        Assert.Equal(1, failed.Errors);
        Assert.Contains("info App: skipped because Lib failed", failedLog.Lines);
        Assert.Equal([@"C:\g\Lib\Lib.pproj(0,0): error E9020: Build event failed: pre-build command 1 ended with exit code 9: lib $(ProjectName) $(ProjectDir)"], Diagnostics(failedLog));
        Assert.DoesNotContain(failing.Commands.Commands, c => c.StartsWith("app ", StringComparison.Ordinal));       // the skipped dependant runs none
        Assert.Contains("tool Tool", failing.Commands.Commands);
        Assert.True(failing.Fs.FileExists(@"C:\g\Tool\bin\Debug\Tool.exe"));
        Assert.True(failing.Fs.FileExists(@"C:\g\Plain\bin\Debug\Plain.exe"));
        Assert.False(failing.Fs.FileExists(@"C:\g\App\bin\Debug\App.exe"));
        Assert.Equal(
            new Dictionary<string, bool> { ["Tool"] = true, ["Lib"] = false, ["App"] = false, ["Plain"] = true },
            failed.Projects.ToDictionary(p => Path.GetFileNameWithoutExtension(p.ProjectPath), p => p.Success));
    }

    /// <summary>
    /// The safety rule of the card: no command of a project reaches the runner unless that project passed the trust gate in the
    /// same call. Besides recording, the runner of this test fails the build loudly if it is ever reached.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1239")]
    [Trait("Req", "CMP-1260")]
    [Trait("Req", "CMP-1276")]
    [InlineData("untrusted, no trust list")]
    [InlineData("untrusted, empty trust list")]
    [InlineData("untrusted, another project and another folder are trusted")]
    [InlineData("changed since it was trusted")]
    [InlineData("changed in a configuration since it was trusted")]
    [InlineData("unreadable trust list")]
    [InlineData("commands only in another configuration, untrusted")]
    [InlineData("policy Skip")]
    [InlineData("policy Skip, commands in the built configuration")]
    [InlineData("target Clean, trusted")]
    [InlineData("target Clean, policy Run")]
    [InlineData("target Run, untrusted")]
    [InlineData("target Rebuild, untrusted")]
    [InlineData("dependant skipped because its dependency failed")]
    [InlineData("untrusted dependency of a trusted project")]
    [InlineData("untrusted member of a group")]
    public async Task NoCommandWithoutAPassedGate(string scenario)
    {
        const string Events = """ "events": { "preBuild": ["pre $(ProjectName)"], "postBuild": ["post"], "postBuildWhen": "always" }""";
        const string ReleaseOnly = """ "configurations": { "Debug": { "optimization": 0 }, "Release": { "optimization": 2, "events": { "preBuild": ["release pre"], "postBuild": ["release post"] } } }""";
        const string Group = @"C:\g\All.pgroup";
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: Events));
        w.Commands.Behavior = (command, _) => throw new InvalidOperationException($"the command '{command}' reached the runner");
        var store = new FileTrustStore(w.Fs, TrustFile);
        var request = w.Request() with { TrustStore = store };
        var expectSuccess = false;
        switch (scenario)
        {
            case "untrusted, no trust list":
                request = w.Request();
                break;
            case "untrusted, empty trust list":
                break;
            case "untrusted, another project and another folder are trusted":
                store.TrustFolder(@"C:\p\sub");
                store.TrustFolder(@"C:\pp");
                store.TrustProject(@"C:\p\Other.pproj", ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!));
                break;
            case "changed since it was trusted":
                store.TrustProject(BuildWorkspace.ProjectPath, ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!));
                w.Fs.Add(BuildWorkspace.ProjectPath, w.Fs.ReadAllText(BuildWorkspace.ProjectPath).Replace("\"post\"", "\"post \"", StringComparison.Ordinal));
                break;
            case "changed in a configuration since it was trusted":
                store.TrustProject(BuildWorkspace.ProjectPath, ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!));
                w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: Events + "," + ReleaseOnly));
                break;
            case "unreadable trust list":
                w.Fs.Add(TrustFile, """{ "formatVersion": 1, "folders": ["C:\\p"], """);
                break;
            case "commands only in another configuration, untrusted":
                w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: ReleaseOnly));
                break;
            case "policy Skip":
                request = w.Request() with { Events = EventsPolicy.Skip };
                expectSuccess = true;
                break;
            case "policy Skip, commands in the built configuration":
                w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: Events + "," + ReleaseOnly));
                request = w.Request() with { Events = EventsPolicy.Skip, Configuration = "Release" };
                expectSuccess = true;
                break;
            case "target Clean, trusted":
                store.TrustFolder(@"C:\p");
                request = w.Request(BuildTarget.Clean) with { TrustStore = store };
                expectSuccess = true;
                break;
            case "target Clean, policy Run":
                request = w.Request(BuildTarget.Clean) with { Events = EventsPolicy.Run };
                expectSuccess = true;
                break;
            case "target Run, untrusted":
                request = w.Request(BuildTarget.Run) with { TrustStore = store };
                break;
            case "target Rebuild, untrusted":
                request = w.Request(BuildTarget.Rebuild) with { TrustStore = store };
                break;
            case "dependant skipped because its dependency failed":
                // the dependant has commands and the caller vouches for them — but it is not built, so none is run
                AddProject(w, "Lib", program: "program Lib; begin Foo := 1 end.");
                AddProject(w, "App", """ "dependencies": ["../Lib/Lib.pproj"],""" + Events);
                request = w.Request(path: @"C:\g\App\App.pproj") with { Events = EventsPolicy.Run };
                break;
            case "untrusted dependency of a trusted project":
                AddProject(w, "Lib", Events);
                AddProject(w, "App", """ "dependencies": ["../Lib/Lib.pproj"],""" + Events);
                store.TrustProject(@"C:\g\App\App.pproj", ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(@"C:\g\App\App.pproj", w.Fs).Value!));
                request = w.Request(path: @"C:\g\App\App.pproj") with { TrustStore = store };
                break;
            case "untrusted member of a group":
                AddProject(w, "A", Events);
                AddProject(w, "Plain");
                w.Fs.Add(Group, """{ "formatVersion": 1, "projects": ["Plain/Plain.pproj", "A/A.pproj"], "activeProject": "A/A.pproj" }""");
                request = w.Request(path: Group) with { TrustStore = store };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var (result, log) = await w.RunAsync(request);

        Assert.Empty(w.Commands.Calls);                                         // nothing reached the command runner
        Assert.DoesNotContain(log.Info, l => l.StartsWith("Running ", StringComparison.Ordinal));
        Assert.Equal(expectSuccess, result.Success);
        Assert.False(result.Canceled);
    }
}
