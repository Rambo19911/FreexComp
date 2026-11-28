using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.TestHelper;
using FreexComp.Ide.Tests.Fixtures;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Platform;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F14-047 end to end (debuggee-console.md §8, I-395): a console program debugged from the IDE with the real build
/// engine, the real debug adapter and the real console host. The test, never a person, types the input into the host's
/// console (helper <c>--console-type</c>) and reads its screen (helper <c>--console-key</c>).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class DebugConsoleEndToEndTests
{
    /// <summary>The program of the spec §8; its line numbers are binding (the breakpoint is on line 7).</summary>
    private const string Source = """
        program ConsoleAsk;
        var
          Name: string;
        begin
          Write('Name? ');
          ReadLn(Name);
          WriteLn('Hello, ', Name);
          ReadLn;
        end.

        """;

    private const string Adapter = "lldb-dap.exe";
    private const int HelloLine = 7;

    /// <summary>
    /// Card acceptance 1: in external mode the program runs in the console host's window; the line typed there is read by
    /// <c>ReadLn</c>, the program stops at the breakpoint after the read and the debugger shows the variable's value; after
    /// Continue and one more Enter the program exits with 0 and the window waits at its key prompt with the whole dialogue.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-P3-096")]
    [Trait("Req", "IDE-P3-097")]
    [Trait("Req", "IDE-P3-100")]
    [Trait("Req", "IDE-P3-101")]
    public async Task TypedLineStopsAtTheBreakpointAfterReadLn_LocalsShowIt()
    {
        await using var h = await StartAsync(external: true);
        var debug = h.Services.Debug!;
        h.Services.Breakpoints!.Toggle(ProgramPath(h), HelloLine);
        var adaptersBefore = Adapters();

        await h.ExecuteAsync("run.start");
        var running = await RunningAsync(h);
        using var host = running.Host;
        using var adapter = AdapterOf(adaptersBefore);
        using var program = Open(running.ProgramId);

        // The user types a line with non-ASCII letters: ReadLn returns it, the program stops on the next line.
        await ConsoleHostTests.TypeAsync(host.Id, "Ada Grüße\n", h.Deadline);
        await WaitAsync(h, () => h.Services.State.Current is { State: IdeState.Paused, PausedLine: HelloLine }, "the stop after ReadLn");
        Assert.Equal(StopReason.Breakpoint, debug.CurrentStop!.Reason);
        Assert.Equal(ProgramPath(h), debug.CurrentStop.Location!.Path, ignoreCase: true);

        // Name is a variable of the program block, a global of the program (G-107): its value is shown as a watch.
        Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.WatchesViewModel.ToolName, focus: false));
        var watch = debug.Watches.Add("Name").ToString(CultureInfo.InvariantCulture);
        var watches = h.Services.DebugShell!.Watches;
        await WaitAsync(h, () => watches.Rows.FirstOrDefault(r => r.Key == watch)?.Cells[1].Text == "'Ada Grüße'", "the watch of Name");

        // Continue: the program greets and waits in the second ReadLn; one Enter ends it with 0.
        await h.ExecuteAsync("run.start");
        await WaitAsync(h, () => h.Services.State.Current.State == IdeState.DebugRunning, "the program runs again");
        await ConsoleHostTests.TypeAsync(host.Id, "\n", h.Deadline);
        await WaitAsync(h, () => h.Services.State.Current.State == IdeState.Idle && debug.IsIdle, "the end of the program");
        await h.Deadline.WaitAsync(program.WaitForExitAsync(h.Deadline.Token), "the program's exit");
        Assert.Equal(0, program.ExitCode);

        // Output › Program: started, the note, the exit line; the program's own text is in its window only.
        h.Clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output reaches its channel (IDE-P3-070)
        var output = h.Services.Output.GetLines(OutputChannels.Program).Select(l => l.Text).ToList();
        Assert.True(output.Count == 3, Diagnostics(h));
        Assert.StartsWith("Program started: " + ExePath(h), output[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Strings.Run_ExternalConsoleNote, output[1]);
        Assert.StartsWith("Program exited with code 0 after ", output[2], StringComparison.Ordinal);

        // The window stays as the waiting host; once the host has reported the exit its prompt is on the screen.
        var waiting = h.Services.Run.WaitingHost;
        Assert.NotNull(waiting);
        Assert.Equal(host.Id, waiting.ProcessId);
        Assert.Equal(0, await h.Deadline.WaitAsync(waiting.Exited, "the host's exit report"));
        var lines = await ConsoleHostTests.ScreenAndKeyAsync(host.Id, host, h.Deadline);
        Assert.Equal(ConsoleKeyHelper.TitlePrefix + "ConsoleAsk.exe", lines[0]);
        var prompt = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_PressAnyKey, "0");
        // The dialogue, the line the Enter of the second ReadLn ended, the empty line the host writes, the prompt.
        Assert.True(lines.Length >= 6, string.Join(" | ", lines));
        Assert.Equal(["Name? Ada Grüße", "Hello, Ada Grüße", "", "", prompt], lines[^5..]);
        Assert.Equal(0, host.ExitCode);

        await h.Deadline.WaitAsync(adapter.WaitForExitAsync(h.Deadline.Token), "the adapter's exit");
        Assert.Empty(h.Messages.Requests);
    }

    /// <summary>
    /// Card acceptance 3: Stop while the program waits for input ends the program, the adapter's launcher in the console,
    /// the console host and the adapter; the window closes at once and does not wait at its prompt.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-P3-097")]
    [Trait("Req", "IDE-P3-101")]
    public async Task StopWhileWaitingForInput_LeavesNoProcess()
    {
        await using var h = await StartAsync(external: true);
        var debug = h.Services.Debug!;
        var adaptersBefore = Adapters();

        await h.ExecuteAsync("run.start");
        var running = await RunningAsync(h);
        using var host = running.Host;
        using var adapter = AdapterOf(adaptersBefore);
        using var program = Open(running.ProgramId);
        var launchers = ProcessSnapshot.Children(host.Id).Where(c => c.Name.Equals(Adapter, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(launchers.Count == 1, "the adapter's launcher in the console: " + string.Join(", ", ProcessSnapshot.Children(host.Id)));
        using var launcher = Open(launchers[0].Id);

        await h.ExecuteAsync("run.stop");
        await WaitAsync(h, () => h.Services.State.Current.State == IdeState.Idle && debug.IsIdle, "the end of the session");

        await h.Deadline.WaitAsync(program.WaitForExitAsync(h.Deadline.Token), "the program's exit");
        await h.Deadline.WaitAsync(launcher.WaitForExitAsync(h.Deadline.Token), "the launcher's exit");
        await h.Deadline.WaitAsync(host.WaitForExitAsync(h.Deadline.Token), "the console host's exit");
        await h.Deadline.WaitAsync(adapter.WaitForExitAsync(h.Deadline.Token), "the adapter's exit");
        Assert.Null(h.Services.Run.WaitingHost);
        h.Clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(Strings.Msg_Run_Terminated, h.Services.Output.GetLines(OutputChannels.Program)[^1].Text);
        Assert.Empty(h.Messages.Requests);
    }

    /// <summary>
    /// Card acceptance 2: in output mode no console host starts; the program's reads see the end of the input at once, so
    /// it runs to its end without input; what it wrote is relayed into Output › Program.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-P3-096")]
    public async Task OutputMode_ReadSeesTheEndOfInput_OutputIsRelayed()
    {
        await using var h = await StartAsync(external: false);
        var debug = h.Services.Debug!;

        await h.ExecuteAsync("run.start");
        await WaitAsync(h, () => h.Services.State.Current.State == IdeState.Idle && debug.IsIdle && h.Services.Debug!.Session is null
            && h.DebugSessions.Sessions.Count == 1, "the end of the program");

        h.Clock.Advance(TimeSpan.FromMilliseconds(50));
        var output = h.Services.Output.GetLines(OutputChannels.Program);
        var text = string.Join("\n", output.Where(l => l.Kind == OutputKind.Normal).Select(l => l.Text));
        Assert.True(text.Contains("Name? ", StringComparison.Ordinal) && text.Contains("Hello, ", StringComparison.Ordinal), Diagnostics(h));
        Assert.StartsWith("Program exited with code 0 after ", output[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Strings.Run_ExternalConsoleNote, output.Select(l => l.Text));

        // No console host: the adapter was not offered the terminal, did not ask for one, and the launch had stdio.
        Assert.Empty(h.DebugSessions.SentResponses("runInTerminal"));
        var launch = Assert.Single(h.DebugSessions.SentArguments("launch"));
        Assert.Contains("\"stdio\"", launch, StringComparison.Ordinal);
        Assert.DoesNotContain("\"console\"", launch, StringComparison.Ordinal);
        Assert.Null(h.Services.Run.WaitingHost);
        Assert.Empty(h.Messages.Requests);
    }

    private static async Task<HeadlessHarness> StartAsync(bool external)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            if (external)
                o.Settings["run.consoleMode"] = "external";
            o.InitialFiles[@"ConsoleAsk\ConsoleAsk.pproj"] = """
                { "formatVersion": 1, "name": "ConsoleAsk", "appType": "console", "mainSource": "ConsoleAsk.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
                  "files": [ { "path": "ConsoleAsk.pas" } ] }
                """;
            o.InitialFiles[@"ConsoleAsk\ConsoleAsk.pas"] = Source.Replace("\r\n", "\n", StringComparison.Ordinal);
        });
        try
        {
            var toolchain = h.Services.Toolchain.Resolve();
            if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
                Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
            // A session that cannot start fails the next wait with the box's text in the diagnostics.
            h.Messages.Answer("Msg.Debug.StartFailed", "Cancel");
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, @"ConsoleAsk\ConsoleAsk.pproj")));
            return h;
        }
        catch
        {
            await h.DisposeAsync();
            throw;
        }
    }

    private static string ProgramPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"ConsoleAsk\ConsoleAsk.pas");

    private static string ExePath(HeadlessHarness h) => Path.Combine(h.TempDir, @"ConsoleAsk\bin\Debug\ConsoleAsk.exe");

    /// <summary>
    /// Waits until the session has launched the program in the console host: the host's process (from the session's
    /// answer to <c>runInTerminal</c>) and the program's id. From then on the host has its console, and what is typed waits
    /// there until the program reads it (spec §8).
    /// </summary>
    private static async Task<(Process Host, int ProgramId)> RunningAsync(HeadlessHarness h)
    {
        await WaitAsync(h, () => h.Services.Debug!.Session is { State: DebugState.Running, ProcessId: not null }, "the launched program");
        var answer = Assert.Single(h.DebugSessions.SentResponses("runInTerminal"));
        using var json = JsonDocument.Parse(answer);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), answer);
        var hostId = json.RootElement.GetProperty("body").GetProperty("shellProcessId").GetInt32();
        var host = Open(hostId);
        Assert.Equal(Path.GetFileName(HeadlessHarness.ConsoleHostExecutable), host.ProcessName + ".exe", ignoreCase: true);
        return (host, h.Services.Debug!.Session!.ProcessId!.Value);
    }

    /// <summary>The ids of the debug adapters this test process runs.</summary>
    private static HashSet<int> Adapters() =>
        [.. ProcessSnapshot.Children(Environment.ProcessId).Where(c => c.Name.Equals(Adapter, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id)];

    /// <summary>The adapter the session started (the one that was not there before).</summary>
    private static Process AdapterOf(HashSet<int> before)
    {
        var started = Adapters().Except(before).ToList();
        Assert.True(started.Count == 1, "new debug adapters: " + string.Join(", ", started));
        return Open(started[0]);
    }

    /// <summary>A running process, with a handle kept so that its exit and exit code stay readable after it ended.</summary>
    private static Process Open(int id)
    {
        var process = Process.GetProcessById(id);
        _ = process.SafeHandle;
        return process;
    }

    private static async Task WaitAsync(HeadlessHarness h, Func<bool> condition, string step)
    {
        try
        {
            await h.WaitForAsync(condition, step);
        }
        catch (HarnessAssertionException e)
        {
            Assert.Fail(e.Message + " — " + Diagnostics(h));
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static string Diagnostics(HeadlessHarness h) =>
        $"state {h.Services.State.Current.State} at {h.Services.State.Current.PausedFile}({h.Services.State.Current.PausedLine}); messages: "
        + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
        + "; boxes: " + string.Join(" | ", h.Messages.Requests.Select(r => $"{r.MessageId}({string.Join(", ", r.Args)})"))
        + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => !l.StartsWith("INFO ", StringComparison.Ordinal)))
        + "; debugger: " + string.Join(" | ", h.Services.Output.GetLines(OutputChannels.Debugger).Select(l => l.Text))
        + "; program: " + string.Join(" | ", h.Services.Output.GetLines(OutputChannels.Program).Select(l => l.Text));
}
