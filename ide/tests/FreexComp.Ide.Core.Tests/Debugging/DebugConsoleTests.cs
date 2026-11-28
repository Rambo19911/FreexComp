using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using Microsoft.Win32.SafeHandles;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// F14-047 (IDE-P3-096 … 102): a debugged console program follows <c>run.consoleMode</c>. In <c>external</c> mode the
/// adapter's <c>runInTerminal</c> request is served by the debug console — the command started in the console host
/// through the (fake) process launcher; in <c>output</c> mode the program is captured with its input at its end. Real
/// sessions over the scripted adapter, whose <c>launch</c> step sends the reverse request as the pinned adapter does.
/// </summary>
[Trait("Phase", "8")]
[Trait("Category", "Integration")]
public sealed class DebugConsoleTests
{
    private const string Adapter = @"C:\llvm\bin\lldb-dap.exe";
    private const string CommPipe = @"\\.\pipe\lldb-dap-run-in-terminal-comm-1";
    private const int ProgramId = 777;

    /// <summary>The debugger's texts (DBG-558, <c>debuggee-console.md</c> §4.1).</summary>
    private const string ConsoleStartFailed = "The program's console window could not be opened: ";

    private const string ConsoleCommandEnded = "The program could not be started in its console window (exit code {0}).";

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string Cwd(DebugTestBed bed) => Path.GetDirectoryName(bed.Exe)!;

    /// <summary>The command the adapter asks for: its own executable in launcher mode, then the program and its arguments.</summary>
    private static string[] Command(DebugTestBed bed) => [Adapter, "--comm-file", CommPipe, "--launch-target", bed.Exe, "-n", "a b"];

    /// <summary>
    /// The terminal-mode launch as the pinned adapter runs it: the reverse request, then (answered) the configuration and
    /// the program's process; <paramref name="more"/> adds steps, <paramref name="env"/> members of the request's arguments.
    /// </summary>
    private static string TerminalScenario(DebugTestBed bed, string more = "", string env = "") => $$"""
        { "steps": [
          { "on": "launch", "respond": {}, "thenReverseRequest": { "command": "runInTerminal", "arguments": { "kind": "external", "title": "ignored",
              "cwd": {{DebugTestBed.Json(Cwd(bed))}}, "args": [ {{string.Join(", ", Command(bed).Select(DebugTestBed.Json))}} ]{{env}} } } },
          { "on": "setExceptionBreakpoints", "respond": {}, "thenEvents": [
            { "event": "process", "body": { "name": "Hello.exe", "systemProcessId": {{ProgramId}}, "startMethod": "attach" } } ] }{{more}} ] }
        """;

    /// <summary>Steps: the configuration ends with the program's own exit.</summary>
    private static string ExitsAfterConfiguration(int code = 0) =>
        $$""", { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugTestBed.Exit(code)}} ] }""";

    private static async Task<DebugTestBed> ExternalAsync(string scenario = "")
    {
        var bed = new DebugTestBed();
        await bed.OpenHelloAsync(""", "run": { "arguments": "-n \"a b\"", "workingDir": "" }""");
        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "external");
        bed.Sessions.UseScenario(scenario.Length > 0 ? scenario : TerminalScenario(bed));
        return bed;
    }

    /// <summary>IDE-P3-096, 097, 100: external mode — the console host runs the adapter's command; Output says so and stays where it is.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-096")]
    [Trait("Req", "IDE-P3-097")]
    [Trait("Req", "IDE-P3-100")]
    public Task ExternalMode_ConsoleProgram_RunsInTheConsoleHost() => TestUiThread.RunAsync(ExternalMode_ConsoleProgram_RunsInTheConsoleHostOnUiThread);

    private static async Task ExternalMode_ConsoleProgram_RunsInTheConsoleHostOnUiThread()
    {
        using var bed = await ExternalAsync();
        var shown = 0;
        bed.Debug.SelectProgramOutput = () => shown++;
        Assert.True(bed.Services.Settings.Get(SettingsCatalog.OutputAutoShowOnRun));

        Assert.True(await bed.Debug.StartAsync());

        // One start: the console host, nothing captured, the command's first element as the file, the rest joined.
        var host = Assert.Single(bed.Processes.Started);
        Assert.Equal(
            new ProcessStartRequest(Adapter, CommandLine.Join(Command(bed)[1..]), Cwd(bed)) { Capture = false, ConsoleHost = true, ConsoleTitle = "Hello.exe" },
            host.Request);
        Assert.Equal("--comm-file \\\\.\\pipe\\lldb-dap-run-in-terminal-comm-1 --launch-target " + bed.Exe + " -n \"a b\"", host.Request.Arguments);

        // The protocol: the capability, the console instead of stdio, the answer with the host's process id.
        Assert.Contains("\"supportsRunInTerminalRequest\":true", Assert.Single(bed.Sessions.SentRequests("initialize")), StringComparison.Ordinal);
        var launch = JsonDocument.Parse(Assert.Single(bed.Sessions.SentArguments("launch"))).RootElement;
        Assert.Equal("externalTerminal", launch.GetProperty("console").GetString());
        Assert.False(launch.TryGetProperty("stdio", out _));
        var answer = Assert.Single(bed.Sessions.SentResponses("runInTerminal"));
        Assert.Contains("\"success\":true", answer, StringComparison.Ordinal);
        Assert.Contains($"\"body\":{{\"shellProcessId\":{host.ProcessId}}}", answer, StringComparison.Ordinal);

        // Output › Program: started (the program's process id, not the host's) and the console note; the window is not shown.
        Assert.Equal(ProgramId, bed.Debug.Session!.ProcessId);
        Assert.Equal(
            [new OutputLine(F(Strings.Msg_Run_Started, bed.Exe, "-n \"a b\"", ProgramId), OutputKind.Ide), new OutputLine(Strings.Run_ExternalConsoleNote, OutputKind.Ide)],
            bed.Output(OutputChannels.Program));
        Assert.Equal(0, shown);

        await bed.Debug.ShutdownAsync();
        Assert.Equal(1, host.KillCount);
        Assert.True(host.IsDisposed);
    }

    /// <summary>IDE-P3-096: output mode — captured as before, the program's input at its end while the session runs.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-096")]
    public Task OutputMode_ConsoleProgram_CapturedWithInputAtEnd() => TestUiThread.RunAsync(OutputMode_ConsoleProgram_CapturedWithInputAtEndOnUiThread);

    private static async Task OutputMode_ConsoleProgram_CapturedWithInputAtEndOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        Assert.Equal("output", bed.Services.Settings.Get(SettingsCatalog.RunConsoleMode)); // the test bed's preset
        bed.Sessions.UseScenario("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "output", "body": { "category": "stdout", "output": "Name? \r\n" } } ] } ] }
            """);

        Assert.True(await bed.Debug.StartAsync());
        Assert.Empty(bed.Processes.Started);
        Assert.Contains("\"supportsRunInTerminalRequest\":false", Assert.Single(bed.Sessions.SentRequests("initialize")), StringComparison.Ordinal);
        var launch = JsonDocument.Parse(Assert.Single(bed.Sessions.SentArguments("launch"))).RootElement;
        Assert.False(launch.TryGetProperty("console", out _));

        // In the program's place: the standard-input pipe reports its end while the session still runs.
        using (var reader = await ConnectStdinAsync(launch))
        {
            var read = await CurrentTest.Deadline.WaitAsync(reader.ReadAsync(new byte[1]).AsTask(), "the end of the program's input");
            Assert.Equal(0, read);
        }

        Assert.Equal(DebugState.Running, bed.Debug.Session!.State);
        await bed.Debug.StopAsync();
        Assert.Contains(bed.Output(OutputChannels.Program), l => l == new OutputLine("Name? ", OutputKind.Normal));
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>The card's "unchanged": a window program and a library debugged through its host ignore <c>external</c>.</summary>
    [Theory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData("gui")]
    [InlineData("library")]
    [Trait("Req", "IDE-P3-096")]
    public Task WindowProgram_AndLibraryThroughHost_Unchanged(string appType) =>
        TestUiThread.RunAsync(() => WindowProgram_AndLibraryThroughHost_UnchangedOnUiThread(appType));

    private static async Task WindowProgram_AndLibraryThroughHost_UnchangedOnUiThread(string appType)
    {
        using var bed = new DebugTestBed();
        var run = appType == "library" ? """ "run": { "arguments": "", "workingDir": "", "hostApplication": "host/Loader.exe" },""" : "";
        bed.File(bed.ProjectPath, $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "{{appType}}", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },{{run}}
              "files": [ { "path": "Hello.pas" } ] }
            """);
        bed.File(bed.MainPath, appType == "library" ? "library Hello;\nbegin\nend.\n" : "program Hello;\nbegin\nend.\n");
        if (appType == "library")
        {
            bed.File(Path.Combine(bed.Dir, @"host\Loader.exe"), "");
            bed.Engine.DefaultOutputPath = bed.File(Path.Combine(bed.Dir, @"bin\Debug\Hello.dll"), "");
        }

        Assert.True(await bed.Services.Projects.OpenAsync(bed.ProjectPath));
        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "external");
        bed.Sessions.UseScenario("{}");

        Assert.True(await bed.Debug.StartAsync());
        Assert.Empty(bed.Processes.Started);
        Assert.Contains("\"supportsRunInTerminalRequest\":false", Assert.Single(bed.Sessions.SentRequests("initialize")), StringComparison.Ordinal);
        var launch = JsonDocument.Parse(Assert.Single(bed.Sessions.SentArguments("launch"))).RootElement;
        Assert.False(launch.TryGetProperty("console", out _));
        using (var reader = await ConnectStdinAsync(launch))
            Assert.True(PeekNamedPipe(reader.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, out _, IntPtr.Zero), "the program's input is still open");
        Assert.DoesNotContain(bed.Output(OutputChannels.Program), l => l.Text == Strings.Run_ExternalConsoleNote);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>
    /// IDE-P3-101, 097: the program ends by itself — the window stays as the waiting host (not killed, not closed); the
    /// next start of a program in any way closes it first.
    /// </summary>
    [Theory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData("debug external")]
    [InlineData("debug output")]
    [InlineData("run")]
    [Trait("Req", "IDE-P3-101")]
    [Trait("Req", "IDE-P3-097")]
    public Task ProgramExits_WindowStaysAsTheWaitingHost_NextStartClosesIt(string next) =>
        TestUiThread.RunAsync(() => ProgramExits_WindowStaysAsTheWaitingHost_NextStartClosesItOnUiThread(next));

    private static async Task ProgramExits_WindowStaysAsTheWaitingHost_NextStartClosesItOnUiThread(string next)
    {
        var bed = await ExternalAsync();
        using (bed)
        {
            bed.Sessions.UseScenario(TerminalScenario(bed, ExitsAfterConfiguration()));
            Assert.True(await bed.Debug.StartAsync());
            await bed.WaitForStateAsync(IdeState.Idle);
            var first = Assert.Single(bed.Processes.Started);
            Assert.False(first.IsDisposed);
            Assert.Equal(0, first.KillCount);
            Assert.Contains(bed.Output(OutputChannels.Program),
                l => l == new OutputLine(F(Strings.Msg_Run_Exited, "0", 0.0.ToString("0.0", CultureInfo.CurrentCulture)), OutputKind.Ide));

            bed.Processes.Starting += _ => Assert.True(first.IsDisposed); // closed before anything new starts
            switch (next)
            {
                case "debug external":
                    bed.Sessions.UseScenario(TerminalScenario(bed));
                    Assert.True(await bed.Debug.StartAsync());
                    Assert.Equal(2, bed.Processes.Started.Count);
                    break;
                case "debug output":
                    bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, "output");
                    bed.Sessions.UseScenario("{}");
                    Assert.True(await bed.Debug.StartAsync());
                    break;
                default:
                    Assert.True(await bed.Services.Run.StartAsync());
                    Assert.True(bed.Processes.Last.Request.ConsoleHost);
                    break;
            }

            Assert.True(first.IsDisposed);
            Assert.Equal(0, first.KillCount); // closed as a waiting window: its job, not a kill of a running program
            await bed.Debug.ShutdownAsync();
            await bed.Services.Run.StopAsync();
            await DebugTestBed.WaitAsync(bed.Services.Run.WhenExitsPostedAsync(), "the run's exit");
        }
    }

    /// <summary>IDE-P3-101: Stop closes the console at once (tree killed, job closed), reported as terminated.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-101")]
    public Task Stop_ClosesTheConsoleAtOnce() => TestUiThread.RunAsync(Stop_ClosesTheConsoleAtOnceOnUiThread);

    private static async Task Stop_ClosesTheConsoleAtOnceOnUiThread()
    {
        using var bed = await ExternalAsync();
        Assert.True(await bed.Debug.StartAsync());
        var host = bed.Processes.Last;

        await bed.Services.Commands.ExecuteAsync("run.stop");

        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Equal(1, host.KillCount);
        Assert.True(host.IsDisposed);
        Assert.Equal(new OutputLine(Strings.Msg_Run_Terminated, OutputKind.Ide), bed.Output(OutputChannels.Program)[^1]);
        Assert.Empty(bed.Dialogs.Requests);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>IDE-P3-101: Restart closes the console and opens a new one for the new session.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-101")]
    public Task Restart_OpensANewConsole() => TestUiThread.RunAsync(Restart_OpensANewConsoleOnUiThread);

    private static async Task Restart_OpensANewConsoleOnUiThread()
    {
        using var bed = await ExternalAsync();
        Assert.True(await bed.Debug.StartAsync());
        var first = bed.Processes.Last;
        var session = bed.Debug.Session;

        await bed.Services.Commands.ExecuteAsync("run.restart");

        Assert.Equal(2, bed.Processes.Started.Count);
        Assert.Equal((1, true), (first.KillCount, first.IsDisposed));
        var second = bed.Processes.Last;
        Assert.Equal((0, false), (second.KillCount, second.IsDisposed));
        Assert.NotSame(session, bed.Debug.Session);
        Assert.Equal(IdeState.DebugRunning, bed.State);
        Assert.Equal(2, bed.Sessions.SentResponses("runInTerminal").Count);
        await bed.Debug.ShutdownAsync();
        Assert.True(second.IsDisposed);
    }

    /// <summary>IDE-P3-101: the user closes the console window — the session ends by itself, no dialog.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-101")]
    public Task ConsoleClosedByTheUser_EndsTheSession() => TestUiThread.RunAsync(ConsoleClosedByTheUser_EndsTheSessionOnUiThread);

    private static async Task ConsoleClosedByTheUser_EndsTheSessionOnUiThread()
    {
        using var bed = await ExternalAsync();
        Assert.True(await bed.Debug.StartAsync());
        var host = bed.Processes.Last;

        host.HostEnded();
        await bed.WaitForStateAsync(IdeState.Idle);

        Assert.Null(bed.Debug.Session);
        Assert.Equal(new OutputLine(Strings.Msg_Run_Terminated, OutputKind.Ide), bed.Output(OutputChannels.Program)[^1]);
        Assert.Empty(bed.Dialogs.Requests);
        Assert.True(host.IsDisposed);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>IDE-P3-101 ("every debug start, in any mode, first closes a waiting host"): a window left by Run Without Debugging.</summary>
    [Theory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData("external")]
    [InlineData("output")]
    [Trait("Req", "IDE-P3-101")]
    public Task DebugStartInAnyMode_ClosesAWaitingRunWindow(string mode) =>
        TestUiThread.RunAsync(() => DebugStartInAnyMode_ClosesAWaitingRunWindowOnUiThread(mode));

    private static async Task DebugStartInAnyMode_ClosesAWaitingRunWindowOnUiThread(string mode)
    {
        using var bed = await ExternalAsync();
        Assert.True(await bed.Services.Run.StartAsync());
        var run = bed.Processes.Last;
        Assert.True(run.Request.ConsoleHost);
        run.Exit(0);
        await bed.WaitForStateAsync(IdeState.Idle); // the exit is applied: the window waits at its key prompt
        Assert.False(run.IsDisposed); // the waiting window

        bed.Services.Settings.Set(SettingsCatalog.RunConsoleMode, mode);
        if (mode == "output")
            bed.Sessions.UseScenario("{}");
        bed.Processes.Starting += _ => Assert.True(run.IsDisposed);
        Assert.True(await bed.Debug.StartAsync());

        Assert.True(run.IsDisposed);
        Assert.Equal(0, run.KillCount);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>IDE-P3-102: the host cannot be started — <c>Msg.Debug.StartFailed</c> with the reason; Run Without Debugging is offered.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-102")]
    public Task HostCannotStart_ShowsStartFailed_OffersRunWithoutDebugging() =>
        TestUiThread.RunAsync(HostCannotStart_ShowsStartFailed_OffersRunWithoutDebuggingOnUiThread);

    private static async Task HostCannotStart_ShowsStartFailed_OffersRunWithoutDebuggingOnUiThread()
    {
        using var bed = await ExternalAsync();
        bed.Sessions.DisconnectWait = TimeSpan.Zero; // the adapter is silent after a refused request, as the pinned one
        bed.Processes.NextStartFails = 5;
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "RunWithoutDebugging");

        Assert.True(await bed.Debug.StartAsync()); // the answer runs the program without the debugger

        var request = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal("Msg.Debug.StartFailed", request.MessageId);
        Assert.Equal([ConsoleStartFailed + new Win32Exception(5).Message], request.Args);
        Assert.Contains("\"success\":false", Assert.Single(bed.Sessions.SentResponses("runInTerminal")), StringComparison.Ordinal);
        var run = Assert.Single(bed.Processes.Started); // the failed start created nothing; this is Run Without Debugging
        Assert.Equal((bed.Exe, true), (run.Request.FileName, run.Request.ConsoleHost));
        Assert.Null(bed.Debug.Session);
        await bed.Services.Run.StopAsync();
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>IDE-P3-102: the command ends before the program runs — <c>Msg.Debug.StartFailed</c>, no window left.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-102")]
    public Task CommandEndsEarly_ShowsStartFailed_NoWindowLeft() => TestUiThread.RunAsync(CommandEndsEarly_ShowsStartFailed_NoWindowLeftOnUiThread);

    private static async Task CommandEndsEarly_ShowsStartFailed_NoWindowLeftOnUiThread()
    {
        using var bed = await ExternalAsync();
        bed.Sessions.UseScenario($$"""
            { "initializedAfter": "never", "steps": [
              { "on": "launch", "respond": {}, "thenReverseRequest": { "command": "runInTerminal", "arguments": {
                  "cwd": {{DebugTestBed.Json(Cwd(bed))}}, "args": [ {{string.Join(", ", Command(bed).Select(DebugTestBed.Json))}} ] } } } ] }
            """);
        bed.Processes.Launched += process => process.Exit(5); // the launcher ends at once, before the program runs
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "Cancel");

        Assert.False(await bed.Debug.StartAsync());

        Assert.Equal([F(ConsoleCommandEnded, 5)], Assert.Single(bed.Dialogs.Requests).Args);
        var host = Assert.Single(bed.Processes.Started);
        Assert.True(host.IsDisposed); // no window left
        Assert.Equal(IdeState.Idle, bed.State);
        Assert.Null(bed.Debug.Session);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>IDE-P3-097: the IDE gives the program no environment of its own; a request with one is refused.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-097")]
    public Task EnvironmentIsRefused() => TestUiThread.RunAsync(EnvironmentIsRefusedOnUiThread);

    private static async Task EnvironmentIsRefusedOnUiThread()
    {
        using var bed = await ExternalAsync();
        bed.Sessions.UseScenario(TerminalScenario(bed, env: """, "env": { "FREEXCOMP_CONSOLE_TEST": "1" }"""));
        bed.Sessions.DisconnectWait = TimeSpan.Zero;
        bed.Dialogs.Enqueue("Msg.Debug.StartFailed", "Cancel");

        Assert.False(await bed.Debug.StartAsync());

        Assert.Equal([ConsoleStartFailed + DebugConsole.EnvironmentRefused], Assert.Single(bed.Dialogs.Requests).Args);
        Assert.Empty(bed.Processes.Started);
        Assert.Equal(IdeState.Idle, bed.State);
        await bed.Debug.ShutdownAsync();
    }

    /// <summary>The client end of the session's standard-input pipe (<c>stdio[0]</c> of the launch), connected in the program's place.</summary>
    private static async Task<NamedPipeClientStream> ConnectStdinAsync(JsonElement launch)
    {
        var path = launch.GetProperty("stdio")[0].GetString()!;
        const string prefix = @"\\.\pipe\";
        Assert.StartsWith(prefix, path, StringComparison.Ordinal);
        var client = new NamedPipeClientStream(".", path[prefix.Length..], PipeDirection.In);
        await DebugTestBed.WaitAsync(client.ConnectAsync(CurrentTest.Deadline.Token), "the program's input pipe");
        return client;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafePipeHandle pipe, IntPtr buffer, int size, IntPtr read, out int available, IntPtr left);
}
