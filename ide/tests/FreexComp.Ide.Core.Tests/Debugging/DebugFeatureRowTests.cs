using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// Rows of the debugger's feature list that are the IDE's to implement (the debugger's features §1, §6, §7): running
/// without the debugger, where the debuggee's output goes, and the thread the step commands name.
/// </summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class DebugFeatureRowTests
{
    /// <summary>DBG-262: Run Without Debugging starts the program directly; no debug session is made for it.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-262")]
    public Task RunWithoutDebuggingDoesNotInvolveTheDebugger() => TestUiThread.RunAsync(RunWithoutDebuggingDoesNotInvolveTheDebuggerOnUiThread);

    private static async Task RunWithoutDebuggingDoesNotInvolveTheDebuggerOnUiThread()
    {
        using var bed = new DebugTestBed();
        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "output");
        await bed.OpenHelloAsync();
        bed.Sessions.UseScenario("{}");

        await bed.Services.Commands.ExecuteAsync("run.startWithoutDebugging");

        Assert.Equal(IdeState.Running, bed.State);
        Assert.Equal(bed.Exe, Assert.Single(bed.Processes.Started).Request.FileName);
        Assert.Empty(bed.Sessions.Requested);
        Assert.Empty(bed.Sessions.Sessions);
        Assert.Null(bed.Debug.Session);
        Assert.Empty(bed.Debug.Log);

        bed.Processes.Last.Exit(0);
        await bed.WaitForStateAsync(IdeState.Idle);
        Assert.Empty(bed.Sessions.Sessions);
        Assert.True(bed.Debug.IsIdle);
    }

    /// <summary>
    /// DBG-263: the text of a log point (<c>Output(LogPoint)</c>) goes to the debug Event Log as a Breakpoint row and to
    /// the <c>debugger</c> Output channel — not to the program's output, where <c>Stdout</c> goes; the program does not stop.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-263")]
    public Task LogPointTextGoesToTheEventLog() => TestUiThread.RunAsync(LogPointTextGoesToTheEventLogOnUiThread);

    private static async Task LogPointTextGoesToTheEventLogOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        bed.Breakpoints.Load([new BreakpointInfo(bed.UnitPath, 7, LogMessage: "reached Run")]);
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 5, "verified": true, "line": 7 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                  { "event": "output", "body": { "category": "stdout", "output": "a\r\n" } },
                  {{DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [5]""")}} ] },
              {{DebugScenarios.Threads}},
              {{DebugScenarios.Stack(bed.UnitPath, 7)}},
              {{DebugScenarios.Continue(DebugTestBed.Exit())}} ] }
            """);
        var stops = 0;
        bed.Debug.Stopped += (_, _) => stops++;

        Assert.True(await bed.Debug.StartAsync());
        await bed.WaitForStateAsync(IdeState.Idle);

        await bed.WaitForScenarioCompleteAsync();
        Assert.True(bed.Sessions.AdapterStderr().Contains("SCENARIO-COMPLETE"), string.Join("\n", bed.Sessions.AdapterStderr()));
        Assert.Equal(0, stops);
        Assert.Contains(bed.Debug.Log, row => row.Kind == DebugLogKind.Breakpoint && row.Message == "reached Run");
        Assert.Contains(bed.Output(OutputChannels.Debugger), line => line.Text == "reached Run");
        var program = bed.Output(OutputChannels.Program);
        Assert.Contains(program, line => line == new OutputLine("a", OutputKind.Normal));
        Assert.DoesNotContain(program, line => line.Text.Contains("reached Run", StringComparison.Ordinal));
        Assert.DoesNotContain(bed.Debug.Log, row => row.Message == "a"); // the program's own output is no Event Log row
    }

    /// <summary>
    /// DBG-255: after another thread was made the current thread, its stack is the one requested and the step commands
    /// name that thread.
    /// </summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "DBG-255")]
    public Task StepCommandsNameTheCurrentThread() => TestUiThread.RunAsync(StepCommandsNameTheCurrentThreadOnUiThread);

    private static async Task StepCommandsNameTheCurrentThreadOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var unit = bed.UnitPath;
        var threads = DebugScenarios.ThreadList((1, "main"), (9, "worker"));
        (string, string, int)[] mainFrames = [("Run", unit, 7)];
        (string, string, int)[] workerFrames = [("Worker", unit, 9)];
        bed.Sessions.UseScenario($$"""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
              {{DebugScenarios.AtStop(mainFrames, threads: threads)}},
              {{DebugScenarios.Stack(200, workerFrames)}},
              {{DebugScenarios.Stack(200, workerFrames)}}, {{DebugScenarios.NoScopes}},
              { "on": "next", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("step", threadId: 9)}} ] },
              {{DebugScenarios.AtStop(workerFrames, threads: threads)}},
              {{DebugScenarios.Stack(mainFrames)}} ] }
            """);
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, stop) => stopped.TrySetResult(stop);
        Assert.True(await bed.Debug.StartAsync());
        await DebugTestBed.WaitAsync(stopped.Task, "the stop");
        Assert.Equal(1, (await stopped.Task).ThreadId);
        await DebugTestBed.WaitAsync(bed.Debug.WhenViewsSettledAsync(), "the debug views' requests");

        await DebugTestBed.WaitAsync(bed.Debug.SwitchThreadAsync(9), "the switch to thread 9");
        await DebugTestBed.WaitAsync(bed.Debug.WhenViewsSettledAsync(), "the debug views' requests");
        Assert.Equal(9, bed.Debug.CurrentThreadId);
        Assert.Contains("\"threadId\":9", bed.Sessions.SentArguments("stackTrace")[^1], StringComparison.Ordinal);

        var next = bed.Sessions.WaitForRequestAsync("next");
        await bed.Services.Commands.ExecuteAsync("run.stepOver");
        await DebugTestBed.WaitAsync(next, "the step request");
        Assert.Contains("\"threadId\":9", Assert.Single(bed.Sessions.SentArguments("next")), StringComparison.Ordinal);

        await bed.Debug.ShutdownAsync();
        Assert.Equal(IdeState.Idle, bed.State);
    }
}
