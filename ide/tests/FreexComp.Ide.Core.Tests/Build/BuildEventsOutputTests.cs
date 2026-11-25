using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// Build events in the Output and Messages windows (phase8 build-events.md §2, card F09-007): the engine is the fake one, its
/// scripted events are the ones the real engine raises for event commands; no command is ever started here. The project lies
/// in a trusted folder, so no question is asked.
/// </summary>
[Trait("Phase", "8")]
public sealed class BuildEventsOutputTests
{
    private const string Events = """, "events": { "preBuild": [ "echo pre" ], "postBuild": [ "echo post" ] }""";
    private const string Configurations =
        """, "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } }""";
    private const string Exe = BuildTestBed.Dir + @"\bin\Debug\Hello.exe";
    private const string Project = BuildTestBed.ProjectPath;

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static async Task<BuildTestBed> StartAsync()
    {
        var bed = new BuildTestBed();
        bed.Engine.DefaultOutputPath = Exe;
        bed.Fs.CreateDirectory(BuildTestBed.Dir + @"\bin\Debug");
        await bed.OpenHelloAsync(extra: Configurations + Events);
        bed.Services.Trust.Store.TrustFolder(@"C:\p");
        return bed;
    }

    private static BuildEvent Info(string text) => new BuildEvent.OutputLine(DateTime.UtcNow, Project, OutputSource.Info, text);

    private static BuildEvent Tool(string text) => new BuildEvent.OutputLine(DateTime.UtcNow, Project, OutputSource.Tool, text);

    private static BuildEvent Command(string text) => new BuildEvent.CommandLine(DateTime.UtcNow, Project, text);

    private static IReadOnlyList<OutputLine> Lines(BuildTestBed bed)
    {
        bed.FlushOutput();
        return bed.Services.Output.GetLines(OutputChannels.Build);
    }

    /// <summary>IDE-EVT-002: progress lines as IDE lines, the expanded command as a command line, command output unchanged.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-002")]
    [Trait("Req", "IDE-EVT-005")]
    public Task ProgressCommandAndOutputLinesAppearInOrder() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        bed.Services.Settings.Set(SettingsCatalog.BuildShowCommandLines, true);
        const string copy = @"copy /y ""C:\p\Hello\bin\Debug\Hello.exe"" deploy";
        bed.Engine.Enqueue(FakeBuildEngine.Success(Project, Exe,
            Info("Running pre-build command 1 of 1"),
            Command("echo pre"),
            Tool("  pre\t"),                                   // not trimmed
            Info("Compiling Unit1"),
            Info("Running post-build command 1 of 1"),
            Command(copy),
            Tool("error: 1 file(s) copied."),                  // not coloured by its content
            Tool("")));

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.True(result!.Success);
        Assert.True(Assert.Single(bed.Engine.Requests).Verbose);
        Assert.Equal(
            [
                new OutputLine(F(Strings.Msg_Build_StartedHeader, "Hello", "Debug", "win64"), OutputKind.Ide),
                new OutputLine("Running pre-build command 1 of 1", OutputKind.Ide),
                new OutputLine(F(Strings.Msg_Build_CommandLine, "echo pre"), OutputKind.Command),
                new OutputLine("  pre\t", OutputKind.Normal),
                new OutputLine("Compiling Unit1", OutputKind.Ide),
                new OutputLine("Running post-build command 1 of 1", OutputKind.Ide),
                new OutputLine(F(Strings.Msg_Build_CommandLine, copy), OutputKind.Command),
                new OutputLine("error: 1 file(s) copied.", OutputKind.Normal),
                new OutputLine("", OutputKind.Normal),
                new OutputLine(BuildService.Summary(result, BuildKind.Build, "Hello"), OutputKind.Success),
            ],
            Lines(bed));
        // IDE-EVT-005: a build that ran commands is not "up to date"; the summary is the success one.
        Assert.False(result.UpToDate);
        Assert.NotEqual(Strings.Msg_Build_UpToDate, bed.Services.Build.ResultText);

        // build.showCommandLines off (the default): the engine is not asked for command lines and none is shown.
        bed.Services.Settings.Set(SettingsCatalog.BuildShowCommandLines, false);
        bed.Engine.Enqueue(FakeBuildEngine.Success(Project, Exe,
            Info("Running pre-build command 1 of 1"), Tool("pre"), Info("Running post-build command 1 of 1"), Tool("post")));
        Assert.True((await bed.Services.Build.BuildAsync(BuildKind.Build))!.Success);
        Assert.False(bed.Engine.Requests[1].Verbose);
        var lines = Lines(bed);
        Assert.DoesNotContain(lines, l => l.Kind == OutputKind.Command);
        Assert.Equal(
            ["Running pre-build command 1 of 1", "pre", "Running post-build command 1 of 1", "post"],
            lines.Skip(1).Take(4).Select(l => l.Text));
    });

    /// <summary>IDE-EVT-003: a line a command wrote is never a Messages row, whatever it looks like.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-003")]
    public Task CommandOutputNeverBecomesARow() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        const string shaped = @"C:\p\Hello\X.pas(3,1): error E2001: Syntax error";
        bed.Engine.Enqueue(FakeBuildEngine.Success(Project, Exe,
            Info("Running pre-build command 1 of 1"), Tool(shaped), Tool("Unit1.pas(1,1): warning W1000: x")));

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.True(result!.Success);
        Assert.Contains(new OutputLine(shaped, OutputKind.Normal), Lines(bed));
        Assert.All(bed.Services.Messages.Items, m => Assert.True(m.IsSummary));
        Assert.Equal((0, 0, 0), (bed.Services.Messages.ErrorCount, bed.Services.Messages.WarningCount, bed.Services.Messages.HintCount));
    });

    /// <summary>IDE-EVT-003, 004: an event diagnostic is one ordinary error row at the project file; the run commands start nothing.</summary>
    [Theory(Timeout = 30000)]
    [InlineData("E9020", "Post-build command 1 ended with exit code 3: echo post")]
    [InlineData("E9022", "Unknown macro $(OutDir) in pre-build command 1")]
    [InlineData("E9023", "Pre-build command 1 contains a control character")]
    [Trait("Req", "IDE-EVT-003")]
    [Trait("Req", "IDE-EVT-004")]
    public Task FailedCommandIsOneErrorRowAndStartsNoProgram(string code, string text) => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var diagnostic = FakeBuildEngine.Diag(code, DiagnosticSeverity.Error, text, Project);
        FakeBuild Failed() => new(
            [Info("Running post-build command 1 of 1"), Tool("post"), new BuildEvent.Diagnostic(DateTime.UtcNow, Project, diagnostic)],
            FakeBuildEngine.Result(Project, success: false, errors: 1));

        // No output file: nothing is asked, nothing starts.
        bed.Engine.Enqueue(Failed());
        Assert.False(await bed.Services.Run.StartAsync(withDebugger: true)); // run.start
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        var row = Assert.Single(bed.Services.Messages.Items, m => !m.IsSummary);
        Assert.Equal((code, MessageSeverity.Error, text), (row.Code, row.Severity, row.Text));
        Assert.Null(row.Location);       // the project file, no line: a project-level row like E9021 (IDE-TRUST-012)
        Assert.False(row.IsNavigable);
        Assert.Equal(1, bed.Services.Messages.ErrorCount);
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Messages.Items[^1].Text); // the failure summary of IDE-P3-034
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Build.ResultText);
        var output = Lines(bed);
        Assert.Contains(new OutputLine(diagnostic.ToText(), OutputKind.Error), output);
        Assert.Equal(new OutputLine(Strings.Msg_Build_FailedOne, OutputKind.Failure), output[^1]);

        // The output file exists (a post-build command failed after the link step): still a failed build. The run commands
        // behave as after any failed build (IDE-P3-080: the question, default Cancel) and start no program by themselves.
        bed.File(Exe, "MZ");
        bed.Dialogs.Enqueue("Msg.Run.BuildFailedRunPrevious", "Cancel", "Cancel", "Cancel");
        bed.Engine.Enqueue(Failed());
        Assert.False(await bed.Services.Run.StartAsync(withDebugger: true));
        bed.Engine.Enqueue(Failed());
        Assert.False(await bed.Services.Run.StartAsync(withDebugger: false));
        bed.Engine.Enqueue(Failed());
        Assert.False(await bed.Services.Run.RestartAsync());
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(4, bed.Engine.Requests.Count);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    });

    /// <summary>
    /// IDE-EVT-006: Cancel Build while a command runs cancels the engine's token — the engine stops the command; the IDE
    /// starts, stops and watches no process of its own — and the summary is the cancellation one.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-006")]
    public Task CancelWhileACommandRunsCancelsTheEngine() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var held = bed.Engine.Enqueue(new FakeBuild([Info("Running pre-build command 1 of 1"), Tool("working")],
            FakeBuildEngine.Result(Project, success: true)).Held());

        var running = bed.Services.Build.BuildAsync(BuildKind.Build);
        await held.Reached; // the command "runs"
        Assert.Equal(IdeState.Building, bed.Services.State.Current.State);
        Assert.True(bed.Services.Commands.CanExecute("project.cancelBuild"));
        await bed.Services.Commands.ExecuteAsync("project.cancelBuild");
        Assert.True(held.Token.IsCancellationRequested);
        var result = await running;

        Assert.True(result!.Canceled);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Equal(Strings.Msg_Build_Canceled, bed.Services.Build.ResultText);
        var lines = Lines(bed);
        Assert.Equal(["Running pre-build command 1 of 1", "working"], lines.Skip(1).Take(2).Select(l => l.Text));
        Assert.Equal(new OutputLine(Strings.Msg_Build_Canceled, OutputKind.Failure), lines[^1]);
        Assert.All(bed.Services.Messages.Items, m => Assert.True(m.IsSummary)); // the cancellation is no error row
        Assert.Empty(bed.Processes.Started);
    });

    /// <summary>
    /// IDE-EVT-001, IDE-P3-036 (reworded): the IDE writes no line of its own about the events of a project. Replaces
    /// <c>BuildServiceTests.BuildEventsAreIgnoredOncePerBuild</c>, whose rule is gone.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-001")]
    [Trait("Req", "IDE-P3-036")]
    public Task NoIgnoredLineForAProjectWithEvents() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        bed.Engine.Enqueue(FakeBuildEngine.Success(Project, Exe, Info("Running pre-build command 1 of 1"), Info("Running post-build command 1 of 1")));

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        // Every line is the header, a line the engine raised, or the summary.
        Assert.Equal(
            [
                F(Strings.Msg_Build_StartedHeader, "Hello", "Debug", "win64"),
                "Running pre-build command 1 of 1",
                "Running post-build command 1 of 1",
                BuildService.Summary(result!, BuildKind.Build, "Hello"),
            ],
            bed.OutputText(OutputChannels.Build));
        bed.Services.Settings.Set(SettingsCatalog.OutputClearOnBuild, false);
        var second = await bed.Services.Build.BuildAsync(BuildKind.Rebuild);
        // The second build adds its header and its summary, nothing else.
        Assert.Equal(
            [F(Strings.Msg_Build_StartedHeader, "Hello", "Debug", "win64"), BuildService.Summary(second!, BuildKind.Rebuild, "Hello")],
            bed.OutputText(OutputChannels.Build).Skip(4));
        Assert.Null(Strings.ResourceManager.GetString("Msg_Project_EventsIgnored", CultureInfo.InvariantCulture)); // the resource is removed
    });
}
