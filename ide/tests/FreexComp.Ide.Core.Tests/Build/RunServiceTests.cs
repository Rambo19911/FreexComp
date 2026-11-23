using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary><c>IRunService</c> (phase3-build-run.md §8, §9) against the fake engine and launcher.</summary>
[Trait("Phase", "3")]
public sealed class RunServiceTests
{
    private const string Configurations =
        """, "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } }""";
    private const string Exe = BuildTestBed.Dir + @"\bin\Debug\Hello.exe";

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static async Task<BuildTestBed> StartAsync(string appType = "console", string run = "")
    {
        var bed = new BuildTestBed();
        bed.Engine.DefaultOutputPath = Exe;
        bed.Fs.CreateDirectory(BuildTestBed.Dir + @"\bin\Debug");
        await bed.OpenHelloAsync(appType, Configurations + run);
        return bed;
    }

    /// <summary>Completes when the run service reports that the program ended (it posts from the exit continuation).</summary>
    private static Task Ended(BuildTestBed bed)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Services.Run.ProgramEnded += (_, _) => ended.TrySetResult();
        return ended.Task;
    }

    /// <summary>IDE-P3-080/081, IDE-P3-002: build, then start with run.arguments and the resolved working directory.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-080")]
    [Trait("Req", "IDE-P3-081")]
    [Trait("Req", "IDE-P3-002")]
    public Task BuildsThenStartsWithArgumentsAndWorkingDir() => TestUiThread.RunAsync(() => BuildsThenStartsWithArgumentsAndWorkingDirOnUiThread()); // I-209

    private async Task BuildsThenStartsWithArgumentsAndWorkingDirOnUiThread()
    {
        var bed = await StartAsync(run: """, "run": { "arguments": "-n 3 \"a b\"", "workingDir": "data" }""");
        bed.Fs.CreateDirectory(BuildTestBed.Dir + @"\data");
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current.State);

        Assert.True(await bed.Services.Run.StartAsync());

        Assert.Single(bed.Engine.Requests);
        var process = bed.Processes.Last;
        Assert.Equal(new ProcessStartRequest(Exe, "-n 3 \"a b\"", BuildTestBed.Dir + @"\data"), process.Request);
        Assert.Equal([IdeState.Building, IdeState.Running], states); // no Idle in between
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, Exe, process.ProcessId), bed.Services.State.Current);
        Assert.Contains(F(Strings.Msg_Run_Started, Exe, "-n 3 \"a b\"", process.ProcessId), bed.OutputText(OutputChannels.Program));

        // An empty working directory is the executable's folder.
        var ended = Ended(bed);
        process.Exit(0);
        await ended;
        var bed2 = await StartAsync();
        Assert.True(await bed2.Services.Run.StartAsync());
        Assert.Equal(new ProcessStartRequest(Exe, "", BuildTestBed.Dir + @"\bin\Debug"), bed2.Processes.Last.Request);
    }

    /// <summary>IDE-P3-080: a failed build offers the executable of the last successful build (default Cancel).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-080")]
    public Task BuildFailedOffersPreviousExecutable() => TestUiThread.RunAsync(() => BuildFailedOffersPreviousExecutableOnUiThread()); // I-209

    private async Task BuildFailedOffersPreviousExecutableOnUiThread()
    {
        var bed = await StartAsync();
        var error = FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 3, 1);

        // No earlier executable: nothing is asked, nothing runs.
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, error));
        Assert.False(await bed.Services.Run.StartAsync());
        Assert.Empty(bed.Processes.Started);
        Assert.DoesNotContain(bed.Dialogs.Requests, r => r.MessageId == "Msg.Run.BuildFailedRunPrevious");
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Equal("E2003", bed.Services.Messages.Items[0].Code);

        // With one: Cancel runs nothing; Run Last Build starts it.
        bed.File(Exe, "MZ");
        bed.Dialogs.Enqueue("Msg.Run.BuildFailedRunPrevious", "Cancel", "RunLastBuild");
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, error));
        Assert.False(await bed.Services.Run.StartAsync());
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, error));
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.Equal(Exe, bed.Processes.Last.Request.FileName);
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);
    }

    /// <summary>IDE-P3-081 (I-190): a successful build whose <c>OutputPath</c> does not exist shows <c>Msg.Run.NoExecutable</c>.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-081")]
    public Task FreshBuildWithoutTheExecutableShowsNoExecutable() => TestUiThread.RunAsync(() => FreshBuildWithoutTheExecutableShowsNoExecutableOnUiThread()); // I-209

    private async Task FreshBuildWithoutTheExecutableShowsNoExecutableOnUiThread()
    {
        var bed = await StartAsync();
        bed.Engine.Enqueue(FakeBuildEngine.Success(BuildTestBed.ProjectPath, Exe)).WritesOutput = false;
        bed.Dialogs.Answer("Msg.Run.NoExecutable", "OK");

        Assert.False(await bed.Services.Run.StartAsync());

        Assert.False(bed.Fs.FileExists(Exe));
        Assert.Empty(bed.Processes.Started);
        var request = Assert.Single(bed.Dialogs.Requests, r => r.MessageId == "Msg.Run.NoExecutable");
        Assert.Equal([Exe], request.Args);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // The default fake build writes its executable, so the next run starts.
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.True(bed.Fs.FileExists(Exe));
        Assert.Equal(Exe, bed.Processes.Last.Request.FileName);
    }

    /// <summary>IDE-P3-084: the exit code and run time are reported; the state returns to Idle; the job is closed.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-084")]
    [Trait("Req", "IDE-P3-072")]
    public Task ExitCodeReportedAndStateIdle() => TestUiThread.RunAsync(() => ExitCodeReportedAndStateIdleOnUiThread()); // I-209

    private async Task ExitCodeReportedAndStateIdleOnUiThread()
    {
        var bed = await StartAsync();
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        Assert.True(await bed.Services.Run.StartAsync());
        var process = bed.Processes.Last;
        bed.Clock.Advance(TimeSpan.FromMilliseconds(1450));
        var ended = Ended(bed);
        process.Exit(0);
        await ended;

        var exited = F(Strings.Msg_Run_Exited, "0", 1.5.ToString("0.0", CultureInfo.CurrentCulture));
        Assert.Equal(exited, bed.OutputText(OutputChannels.Program)[^1]);
        Assert.Contains(exited, status);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Equal(0, bed.Services.Run.LastExitCode);
        Assert.True(process.IsDisposed);
        Assert.Null(bed.Services.Run.Process);

        // A crash code is shown in decimal and hex.
        Assert.True(await bed.Services.Run.StartAsync());
        ended = Ended(bed);
        bed.Processes.Last.Exit(unchecked((int)0xC0000005));
        await ended;
        Assert.StartsWith(F(Strings.Msg_Run_Exited, "-1073741819 (0xC0000005)", "#").Split('#')[0], bed.OutputText(OutputChannels.Program)[^1],
            StringComparison.Ordinal);
        Assert.Equal("256 (0x00000100)", RunService.FormatExitCode(256));
        Assert.Equal("255", RunService.FormatExitCode(255));
    }

    /// <summary>IDE-P3-082 (output mode): captured, no console, stdout and stderr lines in the program channel.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-082")]
    [Trait("Req", "IDE-P3-072")]
    public Task ConsoleModeOutputClosesStdin() => TestUiThread.RunAsync(() => ConsoleModeOutputClosesStdinOnUiThread()); // I-209

    private async Task ConsoleModeOutputClosesStdinOnUiThread()
    {
        var bed = await StartAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var process = bed.Processes.Last;
        Assert.True(process.Request.Capture); // Capture = no console, stdin redirected and closed at once (IProcessLauncher)
        Assert.False(process.Request.ConsoleHost);
        process.WriteStdOut("Hello");
        process.WriteStdErr("oops");
        bed.FlushOutput();
        var lines = bed.Services.Output.GetLines(OutputChannels.Program);
        Assert.Contains(new OutputLine("Hello", OutputKind.Normal), lines);
        Assert.Contains(new OutputLine("oops", OutputKind.Error), lines);
        Assert.DoesNotContain(lines, l => l.Text == Strings.Run_ExternalConsoleNote);
    }

    /// <summary>IDE-P3-082: the product default runs console programs in the console host; GUI programs ignore the setting.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-082")]
    public Task ExternalConsoleIsDefaultForConsoleApps() => TestUiThread.RunAsync(() => ExternalConsoleIsDefaultForConsoleAppsOnUiThread()); // I-209

    private async Task ExternalConsoleIsDefaultForConsoleAppsOnUiThread()
    {
        var bed = await StartAsync();
        bed.Services.Settings.Reset(SettingsCatalog.RunConsoleMode.Name);
        Assert.Equal("external", bed.Services.Settings.Get(SettingsCatalog.RunConsoleMode));
        Assert.True(await bed.Services.Run.StartAsync());
        var request = bed.Processes.Last.Request;
        Assert.True(request.ConsoleHost);
        Assert.False(request.Capture);
        var output = bed.OutputText(OutputChannels.Program);
        Assert.Equal(F(Strings.Msg_Run_Started, Exe, "", bed.Processes.Last.ProcessId), output[0]);
        Assert.Equal(Strings.Run_ExternalConsoleNote, output[1]);

        var gui = await StartAsync(appType: "gui");
        gui.Services.Settings.Reset(SettingsCatalog.RunConsoleMode.Name);
        Assert.True(await gui.Services.Run.StartAsync());
        Assert.True(gui.Processes.Last.Request.Capture);
        Assert.False(gui.Processes.Last.Request.ConsoleHost);
    }

    /// <summary>
    /// IDE-P3-088 (I-138): in external mode the host's <c>exited</c> message ends the run (Msg.Run.Exited, Idle) while the
    /// window stays open at its key prompt (the job is not closed); a host that ends before <c>exited</c> is Terminated.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-088")]
    public Task ExternalConsoleReportsExitWhileWindowStaysOpen() => TestUiThread.RunAsync(() => ExternalConsoleReportsExitWhileWindowStaysOpenOnUiThread()); // I-209

    private async Task ExternalConsoleReportsExitWhileWindowStaysOpenOnUiThread()
    {
        var bed = await StartAsync();
        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "external");
        Assert.True(await bed.Services.Run.StartAsync());
        var host = bed.Processes.Last;
        Assert.True(host.Request.ConsoleHost);
        var ended = Ended(bed);
        host.Exit(3); // the pipe message "exited 3"
        await ended;

        Assert.StartsWith(F(Strings.Msg_Run_Exited, "3", "#").Split('#')[0], bed.OutputText(OutputChannels.Program)[^1], StringComparison.Ordinal);
        Assert.Equal(3, bed.Services.Run.LastExitCode);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Null(bed.Services.Run.Process);
        Assert.False(host.IsDisposed); // the window still shows its output and waits for a key
        Assert.Equal(0, host.KillCount);
        Assert.False(bed.Services.Commands.CanExecute("run.stop"));

        // The window is closed while the program runs: the host ends before "exited" → Program stopped.
        Assert.True(await bed.Services.Run.StartAsync());
        var closed = bed.Processes.Last;
        ended = Ended(bed);
        closed.HostEnded();
        await ended;
        Assert.Equal(Strings.Msg_Run_Terminated, bed.OutputText(OutputChannels.Program)[^1]);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Equal(3, bed.Services.Run.LastExitCode); // unchanged: no exit code was reported
        Assert.True(closed.IsDisposed);
    }

    /// <summary>
    /// IDE-P3-089 (I-138): at most one waiting console window — the next run, project close and IDE exit close it (its
    /// job is closed) without asking.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-089")]
    public Task NextRunClosesWaitingConsoleWindow() => TestUiThread.RunAsync(() => NextRunClosesWaitingConsoleWindowOnUiThread()); // I-209

    private async Task NextRunClosesWaitingConsoleWindowOnUiThread()
    {
        var bed = await StartAsync();
        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "external");
        Assert.True(await bed.Services.Run.StartAsync());
        var first = bed.Processes.Last;
        var ended = Ended(bed);
        first.Exit(0);
        await ended;
        Assert.False(first.IsDisposed);

        // The next run closes the waiting window before the new program starts.
        bed.Processes.Starting += _ => Assert.True(first.IsDisposed);
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.True(first.IsDisposed);
        var second = bed.Processes.Last;
        Assert.NotSame(first, second);

        // Project close: the waiting window is closed, nothing is asked.
        ended = Ended(bed);
        second.Exit(0);
        await ended;
        var asked = bed.Dialogs.Requests.Count;
        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.True(second.IsDisposed);
        Assert.Equal(asked, bed.Dialogs.Requests.Count);

        // IDE exit: the same.
        await bed.Services.Projects.OpenAsync(BuildTestBed.ProjectPath);
        Assert.True(await bed.Services.Run.StartAsync());
        var third = bed.Processes.Last;
        ended = Ended(bed);
        third.Exit(0);
        await ended;
        Assert.True(await bed.Services.Application.ExitAsync());
        Assert.True(third.IsDisposed);
        Assert.Equal(0, third.KillCount);
    }

    /// <summary>
    /// IDE-P3-080/081: a missing working folder is reported; a launch failure is reported. (<c>Msg.Run.NotRunnable</c> for
    /// library/package projects cannot be reached yet: the project reader refuses those application types, E9008.)
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-080")]
    [Trait("Req", "IDE-P3-081")]
    public Task NotRunnableAndMissingWorkingDirMessages() => TestUiThread.RunAsync(() => NotRunnableAndMissingWorkingDirMessagesOnUiThread()); // I-209

    private async Task NotRunnableAndMissingWorkingDirMessagesOnUiThread()
    {
        var bed = await StartAsync(run: """, "run": { "arguments": "", "workingDir": "nowhere" }""");
        bed.Dialogs.Answer("Msg.Run.WorkingDirMissing", "Cancel");
        Assert.False(await bed.Services.Run.StartAsync());
        Assert.Equal([BuildTestBed.Dir + @"\nowhere"], bed.Dialogs.Requests.Single(r => r.MessageId == "Msg.Run.WorkingDirMissing").Args);
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // A launch failure: Msg.Run.StartFailed, Idle.
        var failing = await StartAsync();
        failing.Processes.NextStartFails = 2;
        failing.Dialogs.Answer("Msg.Run.StartFailed", "OK");
        Assert.False(await failing.Services.Run.StartAsync());
        Assert.Equal(Exe, failing.Dialogs.Requests.Single(r => r.MessageId == "Msg.Run.StartFailed").Args[0]);
        Assert.Equal(IdeState.Idle, failing.Services.State.Current.State);
    }

    /// <summary>IDE-P3-090/091: Stop kills the tree and reports Program stopped; a failed kill still ends in Idle.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-090")]
    [Trait("Req", "IDE-P3-091")]
    public Task StopKillsTreeAndReportsStopped() => TestUiThread.RunAsync(() => StopKillsTreeAndReportsStoppedOnUiThread()); // I-209

    private async Task StopKillsTreeAndReportsStoppedOnUiThread()
    {
        var bed = await StartAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var process = bed.Processes.Last;
        await bed.Services.Commands.ExecuteAsync("run.stop");

        Assert.Equal(1, process.KillCount);
        var output = bed.OutputText(OutputChannels.Program);
        Assert.Equal(Strings.Msg_Run_Terminated, output[^1]);
        Assert.DoesNotContain(output, l => l.StartsWith(Strings.Msg_Run_Exited[..10], StringComparison.Ordinal));
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.False(bed.Services.Commands.CanExecute("run.stop"));

        // The kill fails: Msg.Run.KillFailed, Idle forced, the job is not closed.
        Assert.True(await bed.Services.Run.StartAsync());
        var stubborn = bed.Processes.Last;
        stubborn.KillSucceeds = false;
        bed.Dialogs.Answer("Msg.Run.KillFailed", "OK");
        await bed.Services.Run.StopAsync();
        Assert.Contains(bed.Dialogs.Requests, r => r.MessageId == "Msg.Run.KillFailed");
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.False(stubborn.IsDisposed);
    }

    /// <summary>IDE-P3-092: Restart stops the program, then builds and starts again.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-092")]
    public Task RestartStopsThenStarts() => TestUiThread.RunAsync(() => RestartStopsThenStartsOnUiThread()); // I-209

    private async Task RestartStopsThenStartsOnUiThread()
    {
        var bed = await StartAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var first = bed.Processes.Last;
        await bed.Services.Commands.ExecuteAsync("run.restart");

        Assert.Equal(1, first.KillCount);
        Assert.Equal(2, bed.Processes.Started.Count);
        Assert.Equal(2, bed.Engine.Requests.Count);
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, Exe, bed.Processes.Last.ProcessId), bed.Services.State.Current);
    }

    /// <summary>
    /// IDE-P3-004 (I-190): <c>run.start</c> / <c>run.startWithoutDebugging</c> stay available while Running and ask
    /// <c>Msg.Run.AlreadyRunning</c>; Cancel leaves the program running, Restart is <c>run.restart</c>.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-004")]
    public Task RunWhileRunningOffersRestart() => TestUiThread.RunAsync(() => RunWhileRunningOffersRestartOnUiThread()); // I-209

    private async Task RunWhileRunningOffersRestartOnUiThread()
    {
        var bed = await StartAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var first = bed.Processes.Last;
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);
        Assert.True(bed.Services.Commands.CanExecute("run.start"));
        Assert.True(bed.Services.Commands.CanExecute("run.startWithoutDebugging"));

        bed.Dialogs.Answer("Msg.Run.AlreadyRunning", "Cancel");
        await bed.Services.Commands.ExecuteAsync("run.startWithoutDebugging");
        Assert.Equal(["Hello.exe"], bed.Dialogs.Requests.Single(r => r.MessageId == "Msg.Run.AlreadyRunning").Args);
        Assert.Equal(0, first.KillCount);
        Assert.Single(bed.Processes.Started);
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);

        bed.Dialogs.Answer("Msg.Run.AlreadyRunning", "Restart");
        await bed.Services.Commands.ExecuteAsync("run.start");
        Assert.Equal(1, first.KillCount);
        Assert.Equal(2, bed.Processes.Started.Count);
        Assert.Equal(new IdeStateInfo(IdeState.Running, null, false, Exe, bed.Processes.Last.ProcessId), bed.Services.State.Current);
    }

    /// <summary>IDE-P3-093: exit and project close while a program runs ask first and stop it on confirmation.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-093")]
    public Task ExitWhileRunningAsksAndKills() => TestUiThread.RunAsync(() => ExitWhileRunningAsksAndKillsOnUiThread()); // I-209

    private async Task ExitWhileRunningAsksAndKillsOnUiThread()
    {
        var bed = await StartAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var process = bed.Processes.Last;

        bed.Dialogs.Enqueue("Msg.Run.StopOnClose", "Cancel");
        Assert.False(await bed.Services.Projects.CloseAsync());
        Assert.NotNull(bed.Services.Projects.Project);
        Assert.Equal(0, process.KillCount);
        Assert.Equal(["Hello.exe"], bed.Dialogs.Requests[^1].Args);

        bed.Dialogs.Enqueue("Msg.Run.StopOnExit", "Cancel", "StopAndExit");
        Assert.False(await bed.Services.Application.ExitAsync());
        Assert.Equal(IdeState.Running, bed.Services.State.Current.State);
        Assert.Equal(0, process.KillCount);
        Assert.True(await bed.Services.Application.ExitAsync());
        Assert.Equal(1, process.KillCount);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    }
}
