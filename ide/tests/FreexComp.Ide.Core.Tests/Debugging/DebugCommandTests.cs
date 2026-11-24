using System.Text.Json;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// Execution control (I-141; menus.md §7, the debug-session interface §6): the Run commands call the session's methods
/// with the thread of the stop, and are available per debug state — over real sessions and the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class DebugCommandTests
{
    private const int Thread = 7;

    private static readonly string ThreadSeven = DebugScenarios.ThreadList((Thread, "main"));

    private static string Stopped(string reason, string more = "") => DebugScenarios.StoppedEvent(reason, more, Thread);

    private static string At(string path, int line) => DebugScenarios.AtStop([("Run", path, line)], threads: ThreadSeven);

    /// <summary>Runs <paramref name="command"/> and waits for the stop it leads to.</summary>
    private static async Task<StopInfo> StopAfterAsync(DebugTestBed bed, string command)
    {
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StopInfo stop) => stopped.TrySetResult(stop);
        bed.Debug.Stopped += OnStopped;
        try
        {
            await bed.Services.Commands.ExecuteAsync(command);
            await DebugTestBed.WaitAsync(stopped.Task, "the stop after " + command);
            return await stopped.Task;
        }
        finally
        {
            bed.Debug.Stopped -= OnStopped;
        }
    }

    private static int ThreadOf(string arguments) => JsonDocument.Parse(arguments).RootElement.GetProperty("threadId").GetInt32();

    private static string Lines(string setBreakpoints) =>
        string.Join(",", JsonDocument.Parse(setBreakpoints).RootElement.GetProperty("breakpoints").EnumerateArray().Select(b => b.GetProperty("line").GetInt32()));

    /// <summary>
    /// run.stepOver / stepInto / stepOut → <c>StepOverAsync</c> / <c>StepIntoAsync</c> / <c>StepOutAsync</c> with
    /// <c>StopInfo.ThreadId</c>; run.runToCursor → <c>RunToLineAsync</c>; run.start while paused → <c>ContinueAsync</c>;
    /// run.pause → <c>PauseAsync</c>; run.showExecutionPoint; Run to Cursor without a session starts with a temporary breakpoint.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-001")]
    public Task CommandsCallSessionMethods() => TestUiThread.RunAsync(CommandsCallSessionMethodsOnUiThread);

    private static async Task CommandsCallSessionMethodsOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var unit = bed.UnitPath;
        var document = await bed.OpenDocumentAsync(unit);
        var commands = bed.Services.Commands;
        var sent = bed.Sessions;
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("entry")}} ] }, {{At(unit, 7)}},
              { "on": "next", "expectArguments": { "threadId": {{Thread}} }, "respond": {}, "thenEvents": [ {{Stopped("step")}} ] }, {{At(unit, 8)}},
              { "on": "stepIn", "expectArguments": { "threadId": {{Thread}} }, "respond": {}, "thenEvents": [ {{Stopped("step")}} ] }, {{At(unit, 9)}},
              { "on": "stepOut", "expectArguments": { "threadId": {{Thread}} }, "respond": {}, "thenEvents": [ {{Stopped("step")}} ] }, {{At(bed.MainPath, 4)}},
              { "on": "setBreakpoints", "expectArguments": { "source": { "path": {{DebugTestBed.Json(unit)}} }, "breakpoints": [ { "line": 9 } ] },
                "respond": { "breakpoints": [ { "id": 40, "verified": true, "line": 9 } ] } },
              { "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{Stopped("breakpoint", """, "hitBreakpointIds": [40]""")}} ] },
              {{At(unit, 9)}},
              { "on": "setBreakpoints", "expectArguments": { "breakpoints": [] }, "respond": { "breakpoints": [] } },
              {{DebugScenarios.Continue()}},
              { "on": "pause", "respond": {}, "thenEvents": [ {{Stopped("pause")}} ] }, {{At(unit, 8)}},
              {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
            """);

        // F7 / F10 without a session start the program and stop at its first line (stop on entry).
        var stop = await StopAfterAsync(bed, "run.stepInto");
        Assert.Contains("\"stopOnEntry\":true", Assert.Single(sent.SentArguments("launch")), StringComparison.Ordinal);
        Assert.Equal((StopReason.Entry, Thread, 7), (stop.Reason, stop.ThreadId, stop.Location!.Line));
        Assert.Equal(Thread, bed.Debug.CurrentThreadId);

        // The steps name the thread that stopped.
        stop = await StopAfterAsync(bed, "run.stepOver");
        Assert.Equal((StopReason.Step, 8), (stop.Reason, stop.Location!.Line));
        Assert.Equal(Thread, ThreadOf(Assert.Single(sent.SentArguments("next"))));
        stop = await StopAfterAsync(bed, "run.stepInto");
        Assert.Equal(9, stop.Location!.Line);
        Assert.Equal(Thread, ThreadOf(Assert.Single(sent.SentArguments("stepIn"))));
        stop = await StopAfterAsync(bed, "run.stepOut");
        Assert.Equal((bed.MainPath, 4), (stop.Location!.Path, stop.Location.Line));
        Assert.Equal(Thread, ThreadOf(Assert.Single(sent.SentArguments("stepOut"))));
        Assert.Equal(bed.MainPath, bed.Services.Documents.ActiveDocument?.FilePath); // the editor follows the stop

        // Run to Cursor while paused: the line of the caret in the active text document.
        Assert.Same(document, (await bed.Services.Documents.OpenAsync(unit)).Value);
        bed.Services.Navigation.GoToLine(document, 9, null);
        stop = await StopAfterAsync(bed, "run.runToCursor");
        Assert.Equal((StopReason.RunToLine, 9), (stop.Reason, stop.Location!.Line));
        Assert.Equal(["9", ""], sent.SentArguments("setBreakpoints").Select(Lines));
        Assert.Empty(bed.Breakpoints.All); // the session's temporary breakpoint is none of the user's

        // F5 while paused continues; Pause stops the running program.
        await commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.DebugRunning);
        Assert.Null(bed.Debug.CurrentStop);
        Assert.Null(bed.Debug.CurrentThreadId);
        stop = await StopAfterAsync(bed, "run.pause");
        Assert.Equal((StopReason.Pause, 8), (stop.Reason, stop.Location!.Line));
        Assert.Single(sent.SentRequests("pause"));

        // Show Execution Point returns to the stop from anywhere.
        await bed.OpenDocumentAsync(bed.MainPath);
        bed.Services.Navigation.GoToLine(document, 1, null);
        await commands.ExecuteAsync("run.showExecutionPoint");
        Assert.Same(document, bed.Services.Documents.ActiveDocument);
        Assert.Equal(8, document.ViewState.Caret.Line);

        await commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.Idle);
        Assert.Equal(3, sent.SentRequests("continue").Count); // Run to Cursor and the two F5
        Assert.Contains("SCENARIO-COMPLETE", sent.AdapterStderr());

        // DBG-232: Run to Cursor without a session starts the program (no stop on entry) with a temporary breakpoint on
        // the caret's line in the launch set; it is taken away at the first stop and never listed.
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "expectArguments": { "source": { "path": {{DebugTestBed.Json(unit)}} }, "breakpoints": [ { "line": 8 } ] },
                "respond": { "breakpoints": [ { "id": 50, "verified": true, "line": 8 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("breakpoint", """, "hitBreakpointIds": [50]""")}} ] }, {{At(unit, 8)}},
              { "on": "setBreakpoints", "expectArguments": { "breakpoints": [] }, "respond": { "breakpoints": [] } } ] }
            """);
        var requestsBefore = sent.SentArguments("setBreakpoints").Count;
        bed.Services.Navigation.GoToLine(document, 8, null);
        Assert.True(commands.CanExecute("run.runToCursor"));
        stop = await StopAfterAsync(bed, "run.runToCursor");
        Assert.Equal(8, stop.Location!.Line);
        Assert.Contains("\"stopOnEntry\":false", sent.SentArguments("launch")[^1], StringComparison.Ordinal);
        Assert.Empty(bed.Breakpoints.All);
        await DebugTestBed.WaitAsync(bed.Breakpoints.WhenSyncedAsync(), "the temporary breakpoint removed");
        Assert.Equal(["8", ""], sent.SentArguments("setBreakpoints").Skip(requestsBefore).Select(Lines));

        await bed.Debug.StopAsync();
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Contains("SCENARIO-COMPLETE", sent.AdapterStderr().Skip(1));
    }

    /// <summary>menus.md §7: the conditions of the Run commands in every debug state.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-CMD-012")]
    public Task AvailabilityPerDebugState() => TestUiThread.RunAsync(AvailabilityPerDebugStateOnUiThread);

    private static async Task AvailabilityPerDebugStateOnUiThread()
    {
        using var bed = new DebugTestBed();
        var commands = bed.Services.Commands;
        string[] ids =
        [
            "run.start", "run.pause", "run.stepOver", "run.stepInto", "run.stepOut", "run.runToCursor", "run.showExecutionPoint", "run.evaluate",
            "run.addWatch", "run.stop", "run.restart",
        ];
        string Available() => string.Join(" ", ids.Where(id => commands.CanExecute(id)).Select(id => id[4..]));

        // No project: nothing can run.
        Assert.Equal("", Available());

        // Project, idle, no document: Run and the steps that start the program.
        await bed.OpenHelloAsync();
        await bed.Services.Documents.CloseAllAsync();
        Assert.Equal("start stepOver stepInto", Available());

        // … with a text document: Run to Cursor (starts with a temporary breakpoint) and Add Watch.
        await bed.OpenDocumentAsync(bed.UnitPath);
        Assert.Equal("start stepOver stepInto runToCursor addWatch", Available());

        bed.Sessions.UseScenario($$"""
            { "steps": [ {{DebugTestBed.StopAtStart(bed.UnitPath, 7)}}, {{DebugScenarios.Continue()}} ] }
            """);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, _) => stopped.TrySetResult();
        Assert.True(await bed.Debug.StartAsync());
        await DebugTestBed.WaitAsync(stopped.Task, "the stop");

        // Paused: Continue, every step, Run to Cursor, Show Execution Point, Evaluate; not Pause.
        Assert.Equal(IdeState.Paused, bed.State);
        Assert.Equal("start stepOver stepInto stepOut runToCursor showExecutionPoint evaluate addWatch stop restart", Available());

        // Paused without a text document: no Run to Cursor, no Add Watch.
        await bed.Services.Documents.CloseAllAsync();
        Assert.Equal("start stepOver stepInto stepOut showExecutionPoint evaluate stop restart", Available());
        await bed.OpenDocumentAsync(bed.UnitPath);

        // Debugging, the program running: Pause, Stop and Restart only (and Add Watch, which needs no session).
        await commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.DebugRunning);
        Assert.Equal("pause addWatch stop restart", Available());

        await bed.Debug.StopAsync();
        Assert.Equal("start stepOver stepInto runToCursor addWatch", Available());
    }

    /// <summary>
    /// DBG-225 (I-222): Step Over as the first command starts the program with <c>StopAtMainBody</c> — the first stop is a
    /// step stop on the first statement of the main block (from the program's type information), with the execution
    /// line there; the entry stop and the run to the main block are the session's own. The next step is a plain step.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-225")]
    public Task FirstStepStopsAtTheFirstStatementOfTheProgram() => TestUiThread.RunAsync(FirstStepStopsAtTheFirstStatementOfTheProgramOnUiThread);

    private static async Task FirstStepStopsAtTheFirstStatementOfTheProgramOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var main = bed.MainPath; // "begin" on line 3, the first statement "Run;" on line 4
        bed.File(Path.ChangeExtension(bed.Exe, ".pdi"), DebugScenarios.TypeInfo("Hello", bed.UnitPath, (main, 3)));
        var document = await bed.OpenDocumentAsync(main);
        await bed.OpenDocumentAsync(bed.UnitPath);
        bed.Sessions.UseScenario($$"""
            { "steps": [
              {{DebugScenarios.FirstStep(main, 4, ThreadSeven, Thread)}},
              {{DebugScenarios.AtStop([("Hello", main, 4)], threads: ThreadSeven)}},
              { "on": "next", "expectArguments": { "threadId": {{Thread}} }, "respond": {}, "thenEvents": [ {{Stopped("step")}} ] },
              {{DebugScenarios.AtStop([("Hello", main, 5)], threads: ThreadSeven)}} ] }
            """);

        var stop = await StopAfterAsync(bed, "run.stepOver");

        Assert.Equal((StopReason.Step, Thread, new SourceLocation(main, 4, 1)), (stop.Reason, stop.ThreadId, stop.Location));
        Assert.Equal(IdeState.Paused, bed.State);
        Assert.Equal((main, 4), (bed.Services.State.Current.PausedFile, bed.Services.State.Current.PausedLine));
        Assert.Same(document, bed.Services.Documents.ActiveDocument);
        Assert.Equal(4, document.ViewState.Caret.Line);
        Assert.Equal(FreexComp.Ide.Core.Editing.LineGlyphKind.Execution, document.Decorations.GlyphAt(4)?.Kind);
        Assert.Null(document.Decorations.GlyphAt(3));

        // One launch that stops by itself; the temporary breakpoint on the main block (line 3, bound to line 4) is the
        // session's: sent, then removed, never listed.
        Assert.Contains("\"stopOnEntry\":true", Assert.Single(bed.Sessions.SentArguments("launch")), StringComparison.Ordinal);
        Assert.Equal(["3", ""], bed.Sessions.SentArguments("setBreakpoints").Select(Lines));
        Assert.Empty(bed.Breakpoints.All);
        Assert.Single(bed.Sessions.SentRequests("continue"));
        Assert.Equal(1, bed.Debug.Log.Count(row => row.Kind == FreexComp.Ide.Core.Debugging.DebugLogKind.Breakpoint)); // the entry stop is not a row

        // The step that follows steps in the main block.
        stop = await StopAfterAsync(bed, "run.stepOver");
        Assert.Equal((StopReason.Step, 5), (stop.Reason, stop.Location!.Line));
        Assert.Single(bed.Sessions.SentRequests("next"));
        Assert.Single(bed.Sessions.SentRequests("continue"));

        await bed.Debug.StopAsync();
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
    }

    /// <summary>
    /// DBG-224 (I-222): a second execution-control request that arrives while the first is not yet acknowledged (F8 held
    /// down) is ignored — one <c>next</c> reaches the adapter and nothing is reported.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-224")]
    public Task SecondControlRequestInFlightIsIgnored() => TestUiThread.RunAsync(SecondControlRequestInFlightIsIgnoredOnUiThread);

    private static async Task SecondControlRequestInFlightIsIgnoredOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var unit = bed.UnitPath;
        var commands = bed.Services.Commands;
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("entry")}} ] }, {{At(unit, 7)}},
              { "on": "next", "respond": {}, "thenEvents": [ {{Stopped("step")}} ] }, {{At(unit, 8)}},
              {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
            """);
        await StopAfterAsync(bed, "run.stepOver");
        status.Clear();

        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, stop) => stopped.TrySetResult(stop);
        var first = commands.ExecuteAsync("run.stepOver");
        var second = commands.ExecuteAsync("run.stepOver"); // the key repeats before the first request is answered
        var third = commands.ExecuteAsync("run.stepInto");
        await DebugTestBed.WaitAsync(Task.WhenAll(first, second, third), "the step commands");
        await DebugTestBed.WaitAsync(stopped.Task, "the stop after the step");

        var step = await stopped.Task;
        Assert.Equal((StopReason.Step, 8), (step.Reason, step.Location!.Line));
        Assert.Single(bed.Sessions.SentRequests("next"));
        Assert.Empty(bed.Sessions.SentRequests("stepIn"));
        Assert.DoesNotContain(bed.Log.Lines, l => l.StartsWith("WARN debug:", StringComparison.Ordinal));
        Assert.NotEmpty(status);
        Assert.All(status, message => Assert.Equal(step.Description, message)); // the stop, and no failure text before it

        await commands.ExecuteAsync("run.start");
        await bed.WaitForStateAsync(IdeState.Idle);
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
    }

    /// <summary>
    /// DBG-065 (I-222): Run to Cursor without a session on a line whose breakpoint has a condition stops there — the
    /// temporary breakpoint of the launch set bypasses the condition; after the stop the user's breakpoint is the
    /// session's again.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-232")]
    public Task RunToCursorStopsOnLineWithConditionalBreakpoint() => TestUiThread.RunAsync(RunToCursorStopsOnLineWithConditionalBreakpointOnUiThread);

    private static async Task RunToCursorStopsOnLineWithConditionalBreakpointOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var unit = bed.UnitPath;
        var document = await bed.OpenDocumentAsync(unit);
        var user = bed.Breakpoints.Set(new BreakpointInfo(unit, 8, Condition: "1 > 2"));
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 50, "verified": true, "line": 8 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("breakpoint", """, "hitBreakpointIds": [50]""")}} ] }, {{At(unit, 8)}},
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 51, "verified": true, "line": 8 } ] } } ] }
            """);
        bed.Services.Navigation.GoToLine(document, 8, null);

        var stop = await StopAfterAsync(bed, "run.runToCursor");
        Assert.Equal((StopReason.Breakpoint, 8), (stop.Reason, stop.Location!.Line));
        await DebugTestBed.WaitAsync(bed.Breakpoints.WhenSyncedAsync(), "the temporary breakpoint removed");

        // Each request carries one breakpoint of line 8: first the temporary one (the user's is displaced), then the user's.
        Assert.Equal(["8", "8"], bed.Sessions.SentArguments("setBreakpoints").Select(Lines));
        Assert.Same(user, Assert.Single(bed.Breakpoints.All));
        Assert.Equal((BreakpointState.Verified, null, "1 > 2"), (user.State, user.Message, user.Condition));
        Assert.Equal(FreexComp.Ide.Core.Editing.LineGlyphKind.BreakpointConditional, FreexComp.Ide.Core.Debugging.BreakpointService.GlyphOf(user).Kind);

        await bed.Debug.StopAsync();
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Contains("SCENARIO-COMPLETE", bed.Sessions.AdapterStderr());
    }

    /// <summary>
    /// IDE-TGT-014: for a library project Start Debugging, Step Over / Step Into as the first command and Run to Cursor
    /// are disabled (also with a host application); asked for all the same, the debug service answers
    /// <c>Msg.Run.NotRunnable</c>. Breakpoints can still be set and are kept. Run Without Debugging stays available.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-014")]
    public Task LibraryProject_DebugCommandsDisabled() => TestUiThread.RunAsync(LibraryProject_DebugCommandsDisabledOnUiThread);

    private static async Task LibraryProject_DebugCommandsDisabledOnUiThread()
    {
        using var bed = new DebugTestBed();
        var commands = bed.Services.Commands;
        bed.File(bed.ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "library", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
              "run": { "arguments": "", "workingDir": "", "hostApplication": "Loader.exe" },
              "files": [ { "path": "Hello.pas" } ] }
            """);
        bed.File(bed.MainPath, "library Hello;\n\nfunction Add(A, B: Integer): Integer; stdcall;\nbegin\n  Result := A + B;\nend;\n\nexports\n  Add;\n\nbegin\nend.\n");
        Assert.True(await bed.Services.Projects.OpenAsync(bed.ProjectPath));
        var main = await bed.OpenDocumentAsync(bed.MainPath);
        bed.Services.Documents.Activate(main);

        string[] ids = ["run.start", "run.stepOver", "run.stepInto", "run.runToCursor", "run.stepOut", "run.pause", "run.stop"];
        Assert.Equal([], ids.Where(id => commands.CanExecute(id)));
        Assert.True(commands.CanExecute("run.startWithoutDebugging"));
        Assert.True(commands.CanExecute("run.parameters"));

        Assert.False(await bed.Debug.StartAsync());
        Assert.Equal(Strings.Msg_Run_NotRunnable, bed.Services.Status.Message);
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Empty(bed.Engine.Requests);

        // Breakpoints: set, listed and kept.
        Assert.True(commands.CanExecute("run.toggleBreakpoint"));
        main.Selection = TextSelection.At(main.Buffer.Text.IndexOf("Result", StringComparison.Ordinal));
        await commands.ExecuteAsync("run.toggleBreakpoint");
        var breakpoint = Assert.Single(bed.Breakpoints.All);
        Assert.Equal((bed.MainPath, 5), (breakpoint.Path, breakpoint.Line));

        // The same project as a console program: the commands are back.
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(bed.Services.Projects.Definition! with { AppType = "console" }));
        Assert.Equal(["run.start", "run.stepOver", "run.stepInto", "run.runToCursor"], ids.Where(id => commands.CanExecute(id)));
    }
}
