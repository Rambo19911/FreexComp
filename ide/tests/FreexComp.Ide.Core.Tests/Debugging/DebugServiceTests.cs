using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// <c>IDebugService</c> over real debug sessions and the scripted adapter (I-139; the debug-session interface §6, §6.1).
/// </summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class DebugServiceTests
{
    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// run.start under the debugger: build of the active configuration, <c>Msg.Debug.NoDebugInfo</c> when it has no debug
    /// information, the adapter of the LLVM tools folder, the project folder as user code; stop and restart.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-086")]
    [Trait("Req", "IDE-P3-010")]
    [Trait("Req", "DBG-200")]
    [Trait("Req", "DBG-201")]
    public Task StartStopWithFakeAdapter() => TestUiThread.RunAsync(StartStopWithFakeAdapterOnUiThread);

    private static async Task StartStopWithFakeAdapterOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync(""", "run": { "arguments": "-n 3 \"a b\"", "workingDir": "" }""");
        bed.Sessions.UseScenario("{}");
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.State);
        var sessionChanges = 0;
        bed.Debug.SessionChanged += (_, _) => sessionChanges++;

        // F5: builds, then the program runs under the debugger.
        Assert.True(bed.Services.Commands.CanExecute("run.start"));
        await bed.Services.Commands.ExecuteAsync("run.start");

        Assert.Equal("Debug", Assert.Single(bed.Engine.Requests).Configuration);
        Assert.Empty(bed.Dialogs.Requests);
        Assert.Empty(bed.Processes.Started); // not through the process launcher
        Assert.Equal([IdeState.Building, IdeState.DebugRunning], states);
        Assert.Equal(bed.Exe, bed.Services.State.Current.ProgramPath);
        var session = Assert.Single(bed.Sessions.Sessions);
        Assert.Same(session, bed.Debug.Session);
        Assert.Equal(DebugState.Running, session.State);
        Assert.False(bed.Debug.IsPaused);
        Assert.Equal(1, sessionChanges);

        // The debug-session interface §6.1: adapter path and user-code roots; the launch request.
        var options = Assert.Single(bed.Sessions.Requested);
        Assert.Equal(Path.Combine(bed.LlvmDir, "lldb-dap.exe"), options.AdapterPath);
        Assert.Equal([bed.Dir], options.UserSourceRoots);
        var launch = Assert.Single(bed.Sessions.SentArguments("launch"));
        Assert.Contains("\"program\":" + DebugTestBed.Json(bed.Exe), launch, StringComparison.Ordinal);
        Assert.Contains("""["-n","3","a b"]""", launch, StringComparison.Ordinal);
        Assert.Contains("\"cwd\":" + DebugTestBed.Json(Path.GetDirectoryName(bed.Exe)!), launch, StringComparison.Ordinal);
        Assert.Contains(bed.Output(OutputChannels.Program),
            l => l.Kind == OutputKind.Ide && l.Text.StartsWith("Program started: " + bed.Exe + " -n 3 \"a b\" (process ", StringComparison.Ordinal));

        // While the program runs: Stop and Restart are available, Run is not.
        Assert.False(bed.Services.Commands.CanExecute("run.start"));
        Assert.True(bed.Services.Commands.CanExecute("run.stop"));
        Assert.True(bed.Services.Commands.CanExecute("run.restart"));

        // Shift+F5: the session ends; Idle.
        await bed.Services.Commands.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);
        Assert.Equal(DebugState.Terminated, session.State);
        Assert.Equal(2, sessionChanges);
        Assert.Contains(bed.Output(OutputChannels.Program), l => l == new OutputLine(Strings.Msg_Run_Terminated, OutputKind.Ide));
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
        Assert.False(bed.Services.Commands.CanExecute("run.stop"));

        // IDE-P3-086: a configuration without debug information asks first; Cancel starts nothing.
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        Assert.Equal("Release", bed.Services.Projects.Project!.ActiveConfiguration);
        bed.Dialogs.Enqueue("Msg.Debug.NoDebugInfo", "Cancel", "RunAnyway");
        Assert.False(await bed.Debug.StartAsync());
        Assert.Equal(["Release"], Assert.Single(bed.Dialogs.Requests).Args);
        Assert.Single(bed.Engine.Requests);
        Assert.Single(bed.Sessions.Sessions);
        Assert.Equal(IdeState.Idle, bed.State);

        // Run Anyway starts under the debugger; Restart ends that session and starts another.
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal("Release", bed.Engine.Requests[^1].Configuration);
        Assert.Equal(2, bed.Sessions.Sessions.Count);
        bed.Dialogs.Answer("Msg.Debug.NoDebugInfo", "RunAnyway");
        await bed.Services.Commands.ExecuteAsync("run.restart");
        Assert.Equal(3, bed.Sessions.Sessions.Count);
        Assert.Equal(DebugState.Terminated, bed.Sessions.Sessions[1].State);
        Assert.Same(bed.Sessions.Last, bed.Debug.Session);
        Assert.Equal(IdeState.DebugRunning, bed.State);

        await bed.Debug.StopAsync();
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.True(bed.Debug.IsIdle);
    }

    /// <summary>The debug-session interface §6.1, state rows: Starting / Running / Terminating → Debugging, Stopped → Paused, Terminated → Idle.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-001")]
    public Task StateMappingFollowsSession() => TestUiThread.RunAsync(StateMappingFollowsSessionOnUiThread);

    private static async Task StateMappingFollowsSessionOnUiThread()
    {
        Assert.Equal(
            [IdeState.Idle, IdeState.DebugRunning, IdeState.DebugRunning, IdeState.Paused, IdeState.DebugRunning, IdeState.Idle],
            new[] { DebugState.Idle, DebugState.Starting, DebugState.Running, DebugState.Stopped, DebugState.Terminating, DebugState.Terminated }
                .Select(DebugService.Map));

        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        // Stops at Unit1.pas:7 right after the start; Continue lets the program end with exit code 3.
        bed.Sessions.UseScenario($$"""
            { "steps": [ {{DebugTestBed.StopAtStart(bed.UnitPath, 7)}},
                         { "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{DebugTestBed.Exit(3)}} ] } ] }
            """);
        var states = new List<IdeStateInfo>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current);
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, stop) => stopped.TrySetResult(stop);

        Assert.True(await bed.Debug.StartAsync());
        await DebugTestBed.WaitAsync(stopped.Task, "the stop");

        // Paused: the state carries the stop location; the session's stop is what the service publishes.
        var stop = await stopped.Task;
        Assert.Equal(IdeState.Paused, bed.State);
        Assert.Equal((bed.UnitPath, 7), (bed.Services.State.Current.PausedFile, bed.Services.State.Current.PausedLine));
        Assert.True(bed.Debug.IsPaused);
        Assert.Same(stop, bed.Debug.CurrentStop);
        Assert.Equal(new SourceLocation(bed.UnitPath, 7, 1), stop.Location);
        Assert.Equal(bed.UnitPath, bed.Services.Documents.ActiveDocument?.FilePath); // the editor shows the stop
        Assert.Equal("Hello (Paused) – FreexComp", FreexComp.Ide.Core.ViewModels.WindowTitle.Format("Hello", IdeState.Paused));

        // run.start while paused is Continue.
        Assert.True(bed.Services.Commands.CanExecute("run.start"));
        Assert.Equal(Strings.Button_Continue, bed.Services.Commands.Get("run.start").Title);
        await bed.Services.Commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.Idle);

        Assert.Equal(
            [IdeState.Building, IdeState.DebugRunning, IdeState.Paused, IdeState.DebugRunning, IdeState.Idle],
            states.Select(s => s.State));
        Assert.Null(bed.Debug.Session);
        Assert.False(bed.Debug.IsPaused);
        Assert.Contains(bed.Output(OutputChannels.Program), l => l == new OutputLine(F(Strings.Msg_Run_Exited, "3", 0.0.ToString("0.0", CultureInfo.CurrentCulture)), OutputKind.Ide));
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
        Assert.Equal(Strings.ResourceManager.GetString("Cmd_run_start", CultureInfo.CurrentCulture), bed.Services.Commands.Get("run.start").Title);
    }

    /// <summary>The debug-session interface §6.1: Stdout / Stderr → <c>program</c>, Debugger / LogPoint → <c>debugger</c>; chunks become lines.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-070")]
    [Trait("Req", "DBG-263")]
    public Task OutputCategoriesGoToChannels() => TestUiThread.RunAsync(OutputCategoriesGoToChannelsOnUiThread);

    private static async Task OutputCategoriesGoToChannelsOnUiThread()
    {
        Assert.Equal((OutputChannels.Program, OutputKind.Normal), DebugService.Route(OutputCategory.Stdout));
        Assert.Equal((OutputChannels.Program, OutputKind.Error), DebugService.Route(OutputCategory.Stderr));
        Assert.Equal((OutputChannels.Debugger, OutputKind.Normal), DebugService.Route(OutputCategory.Debugger));
        Assert.Equal((OutputChannels.Debugger, OutputKind.Normal), DebugService.Route(OutputCategory.LogPoint));

        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        Assert.Equal([OutputChannels.Build, OutputChannels.Program, OutputChannels.Debugger, OutputChannels.Designer], bed.Services.Output.Channels);
        bed.Sessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "output", "body": { "category": "stdout", "output": "Hello\r\nWor" } },
                { "event": "output", "body": { "category": "stderr", "output": "oops\r\n" } },
                { "event": "output", "body": { "category": "stdout", "output": "ld\r\nno line end" } },
                {{DebugTestBed.Exit()}} ] } ] }
            """);

        Assert.True(await bed.Debug.StartAsync());
        await bed.WaitForStateAsync(IdeState.Idle);

        var program = bed.Output(OutputChannels.Program);
        Assert.Equal(
            [new OutputLine("Hello", OutputKind.Normal), new OutputLine("oops", OutputKind.Error), new OutputLine("World", OutputKind.Normal),
                new OutputLine("no line end", OutputKind.Normal)],
            program.Where(l => l.Kind != OutputKind.Ide));
        // DBG-403: the program has no type information file; the session says so on the debugger channel.
        var debugger = bed.Output(OutputChannels.Debugger);
        Assert.Contains(debugger, l => l.Text.StartsWith("Type information file not found: " + Path.ChangeExtension(bed.Exe, ".pdi"), StringComparison.Ordinal));
        Assert.DoesNotContain(program, l => l.Text.Contains("Type information", StringComparison.Ordinal));
    }

    /// <summary><c>Msg.Debug.StartFailed</c> for a missing adapter and a refused launch; Idle afterwards; Run Without Debugging.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-001")]
    public Task LaunchFailureShowsMessageAndReturnsToIdle() => TestUiThread.RunAsync(LaunchFailureShowsMessageAndReturnsToIdleOnUiThread);

    private static async Task LaunchFailureShowsMessageAndReturnsToIdleOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();

        // AdapterNotFound: the toolchain folder of the test has no debug adapter.
        bed.Sessions.UseNoAdapter();
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "Cancel");
        Assert.False(await bed.Debug.StartAsync());
        var request = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal("Msg.Debug.StartFailed", request.MessageId);
        Assert.Equal(["Debug adapter not found: " + Path.Combine(bed.LlvmDir, "lldb-dap.exe")], request.Args);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);
        Assert.Equal(DebugState.Terminated, bed.Sessions.Last.State);
        Assert.Empty(bed.Processes.Started);

        // LaunchFailed: the adapter refuses the launch.
        bed.Sessions.UseScenario("""{ "steps": [ { "on": "launch", "fail": "process launch failed: access denied" } ] }""");
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "Cancel");
        Assert.False(await bed.Debug.StartAsync());
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        Assert.Contains("process launch failed: access denied", Assert.Single(bed.Dialogs.Requests[1].Args), StringComparison.Ordinal);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);
        Assert.True(bed.Services.Commands.CanExecute("run.start"));

        // Run Without Debugging starts the program through the process launcher.
        bed.Sessions.UseNoAdapter();
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "RunWithoutDebugging");
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal(IdeState.Running, bed.State);
        Assert.Equal(bed.Exe, bed.Processes.Last.Request.FileName);
        Assert.Null(bed.Debug.Session);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Services.Run.ProgramEnded += (_, _) => ended.TrySetResult();
        bed.Processes.Last.Exit(0);
        await DebugTestBed.WaitAsync(ended.Task, "the program's end");
        Assert.Equal(IdeState.Idle, bed.State);
    }

    /// <summary>IDE-P3-093 with a debug session: closing the project asks <c>Msg.Run.StopOnClose</c>; Cancel keeps both.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-093")]
    public Task ProjectCloseAsksToStopTheSession() => TestUiThread.RunAsync(ProjectCloseAsksToStopTheSessionOnUiThread);

    private static async Task ProjectCloseAsksToStopTheSessionOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        bed.Sessions.UseScenario("""{ "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 7 } ] } } ] }""");
        bed.Breakpoints.Toggle(bed.UnitPath, 7);
        Assert.True(await bed.Debug.StartAsync());

        bed.Dialogs.Enqueue("Msg.Run.StopOnClose", "Cancel", "StopAndClose");
        Assert.False(await bed.Services.Projects.CloseAsync());
        Assert.Equal(["Hello.exe"], bed.Dialogs.Requests[^1].Args);
        Assert.NotNull(bed.Services.Projects.Project);
        Assert.Equal(IdeState.DebugRunning, bed.State);

        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.Null(bed.Services.Projects.Project);
        Assert.Null(bed.Debug.Session);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Equal(DebugState.Terminated, bed.Sessions.Last.State);
        Assert.Empty(bed.Breakpoints.All); // they belong to the closed project (its session file has them)
        Assert.Contains("\"line\": 7", System.IO.File.ReadAllText(bed.SessionPath), StringComparison.Ordinal);
    }

    /// <summary><c>run.arguments</c> is one command-line tail; the session takes the arguments one by one.</summary>
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("  a   b\tc ", new[] { "a", "b", "c" })]
    [InlineData("-n 3 \"a b\"", new[] { "-n", "3", "a b" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    [InlineData(@"C:\dir\ ""C:\my dir\\"" a\""b", new[] { @"C:\dir\", @"C:\my dir\", "a\"b" })]
    [InlineData(@"a\\b pre""mid dle""post", new[] { @"a\\b", "premid dlepost" })]
    [Trait("Req", "DBG-200")]
    public void SplitsRunArguments(string commandLine, string[] expected) => Assert.Equal(expected, DebugService.SplitArguments(commandLine));

    /// <summary>
    /// IDE-LIB-022 / IDE-LIB-025: a session started with the library-stepping choice on gets the project directory, the
    /// project's search paths and the library roots as user-code roots, in this order and without duplicates (ignoring
    /// case); with the choice off the list is the project's alone. Nothing else of the session options differs.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-LIB-022")]
    [Trait("Req", "IDE-LIB-024")]
    [Trait("Req", "IDE-LIB-025")]
    public Task LibraryStepping_AddsLibraryRootsToUserSourceRoots() => TestUiThread.RunAsync(LibraryStepping_AddsLibraryRootsOnUiThread);

    private static async Task LibraryStepping_AddsLibraryRootsOnUiThread()
    {
        using var bed = new DebugTestBed();
        var rtl = Assert.Single(bed.Services.LibrarySource.Roots);
        var framework = Path.Combine(Path.GetDirectoryName(rtl)!, "ui");
        Directory.CreateDirectory(framework);
        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.ToolchainFrameworkDir, framework);
        Assert.Equal([rtl, framework], bed.Services.LibrarySource.Roots);

        // I-259: the Win32 binding directory beside the run-time library is a library root once it exists.
        var winapi = Path.Combine(Path.GetDirectoryName(rtl)!, "winapi", "src");
        Directory.CreateDirectory(winapi);
        Assert.Equal([rtl, framework, winapi], bed.Services.LibrarySource.Roots);

        // The run-time library directory is a search path of the project too, in another letter case: listed once.
        var searched = rtl.ToUpperInvariant();
        await bed.OpenHelloAsync($$""", "searchPaths": ["lib", {{DebugTestBed.Json(searched)}}]""");
        bed.Sessions.UseScenario("{}");
        var lib = Path.Combine(bed.Dir, "lib");

        // Off (the default): as before.
        Assert.True(await bed.Debug.StartAsync());
        var off = bed.Sessions.Requested[^1];
        Assert.Equal([bed.Dir, lib, searched], off.UserSourceRoots);
        await bed.Debug.StopAsync();

        // On: the library roots follow, duplicates removed.
        await bed.Services.Commands.ExecuteAsync("run.toggleLibraryStepping");
        Assert.True(await bed.Debug.StartAsync());
        var on = bed.Sessions.Requested[^1];
        Assert.Equal([bed.Dir, lib, searched, framework, winapi], on.UserSourceRoots);
        Assert.Equal((off.AdapterPath, off.RequestTimeout, off.LaunchTimeout, off.Format), (on.AdapterPath, on.RequestTimeout, on.LaunchTimeout, on.Format)); // IDE-LIB-025: the only input that differs
        await bed.Debug.StopAsync();

        // IDE-LIB-024: the same with a configuration without debug information (the roots are passed; no extra message).
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        bed.Dialogs.Answer("Msg.Debug.NoDebugInfo", "RunAnyway");
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal([bed.Dir, lib, searched, framework, winapi], bed.Sessions.Requested[^1].UserSourceRoots);
        Assert.Equal(["Msg.Debug.NoDebugInfo"], bed.Dialogs.Requests.Select(r => r.MessageId));
        await bed.Debug.StopAsync();

        // Off again.
        await bed.Services.Commands.ExecuteAsync("run.toggleLibraryStepping");
        bed.Dialogs.Answer("Msg.Debug.NoDebugInfo", "RunAnyway");
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal([bed.Dir, lib, searched], bed.Sessions.Requested[^1].UserSourceRoots);
        await bed.Debug.StopAsync();
    }

    /// <summary>
    /// IDE-LIB-023: a stop whose location is a library file opens that file as a protected document (IDE-LIB-002) with the
    /// execution-line marker — independent of the library-stepping choice (off: a breakpoint stop in the library file;
    /// on: a Step Into that ends in it).
    /// </summary>
    [Theory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-LIB-023")]
    public Task StopInLibraryFileOpensProtectedDocument(bool stepping) => TestUiThread.RunAsync(() => StopInLibraryFileOnUiThread(stepping));

    private static async Task StopInLibraryFileOnUiThread(bool stepping)
    {
        using var bed = new DebugTestBed();
        var commands = bed.Services.Commands;
        var root = Assert.Single(bed.Services.LibrarySource.Roots);
        var lib = bed.File(Path.Combine(root, "LibUnit.pas"), "unit LibUnit;\ninterface\nprocedure LibProc;\nimplementation\nprocedure LibProc;\nbegin\nend;\nend.\n");
        await bed.OpenHelloAsync();
        if (stepping)
            await commands.ExecuteAsync("run.toggleLibraryStepping");
        Assert.Equal(stepping, bed.Debug.StepIntoLibrarySource);

        async Task<StopInfo> StopAfterAsync(Func<Task> act)
        {
            var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStopped(object? sender, StopInfo e) => stopped.TrySetResult(e);
            bed.Debug.Stopped += OnStopped;
            await act();
            await DebugTestBed.WaitAsync(stopped.Task, "the stop");
            bed.Debug.Stopped -= OnStopped;
            return await stopped.Task;
        }

        StopInfo stop;
        if (stepping)
        {
            bed.Sessions.UseScenario($$"""
                { "steps": [ {{DebugTestBed.StopAtStart(bed.UnitPath, 7)}}, {{DebugScenarios.StepTo("stepIn", lib, 6)}},
                             {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
                """);
            stop = await StopAfterAsync(async () => Assert.True(await bed.Debug.StartAsync()));
            Assert.Equal(bed.UnitPath, stop.Location!.Path);
            Assert.DoesNotContain(bed.Services.Documents.Documents, d => string.Equals(d.FilePath, lib, StringComparison.OrdinalIgnoreCase));
            stop = await StopAfterAsync(() => commands.ExecuteAsync("run.stepInto"));
            Assert.Equal(StopReason.Step, stop.Reason);
        }
        else
        {
            bed.Sessions.UseScenario($$"""
                { "steps": [ {{DebugTestBed.StopAtStart(lib, 6)}}, {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
                """);
            stop = await StopAfterAsync(async () => Assert.True(await bed.Debug.StartAsync()));
        }

        Assert.Equal(stepping, bed.Sessions.Requested[^1].UserSourceRoots.Contains(root, StringComparer.OrdinalIgnoreCase));
        Assert.Equal((lib, 6), (stop.Location!.Path, stop.Location.Line));
        var document = Assert.IsType<TextDocumentViewModel>(bed.Services.Documents.ActiveDocument);
        Assert.Equal(lib, document.FilePath);
        Assert.True(document.IsLibraryProtected);
        Assert.True(document.Buffer.IsReadOnly);
        Assert.Equal(TextDocumentViewModel.LibrarySourceInfoBarKind, document.VisibleInfoBars[0].Kind);
        Assert.Equal(LineGlyphKind.Execution, document.Decorations.GlyphAt(6)?.Kind);
        Assert.Equal(6, document.ViewState.Caret.Line);
        Assert.Empty(bed.Dialogs.Requests);

        // The marker goes with the stop; the document stays protected.
        await commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.Idle);
        Assert.Null(document.Decorations.GlyphAt(6));
        Assert.True(document.IsLibraryProtected);
    }
}
