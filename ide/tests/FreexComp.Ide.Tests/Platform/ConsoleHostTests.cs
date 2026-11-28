using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.TestHelper;
using FreexComp.Ide.Tests.Fixtures;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>The console host (I-138, IDE-P3-087/088) with the real IDE executable, pipe and console.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ConsoleHostTests
{
    /// <summary>
    /// The part of the test's time kept for the failure snapshot. Each wait starts up to two .NET processes and a console
    /// (≈ 250 ms alone); under a loaded full-solution run one cold start can take seconds (I-194), so no wait gets a fixed
    /// slice: I-205, each is bounded by the test's own timeout less this reserve.
    /// </summary>
    private static readonly TimeSpan SnapshotReserve = TimeSpan.FromSeconds(5);

    /// <summary>Awaits <paramref name="task"/> until the test's time less <see cref="SnapshotReserve"/> is spent; a timeout names the step.</summary>
    private static async Task<T> Step<T>(Task<T> task, string step, TestDeadline deadline)
    {
        var left = deadline.Remaining - SnapshotReserve;
        try
        {
            return await task.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero, deadline.Token);
        }
        catch (TimeoutException e) when (!task.IsCompleted)
        {
            throw new TimeoutException($"step '{step}' still pending {deadline.Elapsed} after the start", e);
        }
    }

    private static async Task Step(Task task, string step, TestDeadline deadline) =>
        await Step(Completed(task), step, deadline);

    private static async Task<bool> Completed(Task task)
    {
        await task;
        return true;
    }

    /// <summary>What the host is doing when a wait fails (I-194): alive or not, its children, the text of its console.</summary>
    private static async Task<string> SnapshotAsync(Process host, TestDeadline deadline)
    {
        var token = deadline.Token;
        var text = new System.Text.StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"host {host.Id} exited: {host.HasExited}; children: ");
        text.AppendJoin(", ", ProcessSnapshot.Children(host.Id).Select(c => $"{c.Name} {c.Id}"));
        if (host.HasExited)
            return text.ToString();
        var screen = Path.Combine(Path.GetTempPath(), "freexcomp-console-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var (helper, arguments) = ProcessHelper.Command(string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.ConsoleKey} {host.Id} \"{screen}\""));
            using var key = Process.Start(new ProcessStartInfo(helper, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
            await deadline.WaitAsync(key.WaitForExitAsync(token), "the console snapshot helper");
            text.Append("; console: ").AppendJoin(" | ", File.Exists(screen) ? await File.ReadAllLinesAsync(screen, token) : ["(no screen file)"]);
        }
        catch (TimeoutException)
        {
            text.Append("; console: (helper timed out)");
        }
        finally
        {
            File.Delete(screen);
        }

        return text.ToString();
    }

    /// <summary>The IDE executable next to the test assembly (the project reference copies its apphost).</summary>
    private static string IdeExecutable => Path.ChangeExtension(typeof(ConsoleHost).Assembly.Location, ".exe");

    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Req", "IDE-P3-087")]
    [Trait("Req", "IDE-P3-088")]
    public async Task RunsChildReportsExitCodeAndWaitsForKey()
    {
        var deadline = CurrentTest.Deadline;
        var token = deadline.Token;
        var (file, arguments) = ProcessHelper.Command(HelperProgram.ExitWith + " 3");
        var launcher = new WindowsProcessLauncher(new ManualClock(), IdeExecutable);
        using var process = launcher.Start(new ProcessStartRequest(file, arguments, Path.GetTempPath()) { Capture = false, ConsoleHost = true },
            _ => throw new InvalidOperationException("Nothing is captured in host mode."));
        using var host = Process.GetProcessById(process.ProcessId);
        _ = host.SafeHandle; // keep a handle so the exit code stays readable

        // The program's exit code arrives over the pipe while the host still waits at its prompt.
        int exitCode;
        try
        {
            exitCode = await Step(process.Exited, "exited report", deadline);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException(e.Message + "; " + await SnapshotAsync(host, deadline), e);
        }

        Assert.Equal(3, exitCode);
        Assert.False(host.HasExited);

        // A key press in the host's console closes it; the host exits with the program's code.
        var screen = Path.Combine(Path.GetTempPath(), "freexcomp-console-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var (helper, helperArguments) = ProcessHelper.Command(
                string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.ConsoleKey} {process.ProcessId} \"{screen}\""));
            using (var key = Process.Start(new ProcessStartInfo(helper, helperArguments)
                   {
                       UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                   })!)
            {
                var errors = ChildProcessOutput.Read(key).StandardError; // I-262: a dedicated thread, not the pool
                await Step(key.WaitForExitAsync(token), "key helper", deadline);
                Assert.True(key.ExitCode == 0, $"key helper exit code {key.ExitCode}: {await errors}");
            }

            await Step(host.WaitForExitAsync(token), "host exit after key", deadline);
            Assert.Equal(3, host.ExitCode);

            var lines = await File.ReadAllLinesAsync(screen, token);
            Assert.Equal(ConsoleKeyHelper.TitlePrefix + Path.GetFileName(file), lines[0]); // the window title = the program's file name
            // The program wrote into the host's console; then an empty line and the host's prompt (the cursor waits after it).
            var prompt = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_PressAnyKey, "3");
            Assert.Equal([HelperProgram.ChildGreeting, "", prompt], lines[^3..]);
        }
        finally
        {
            File.Delete(screen);
        }
    }

    /// <summary>IDE-P3-098 (a): the window's title comes from <c>--title</c>; the joined arguments arrive one by one, verbatim.</summary>
    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P3-098")]
    public async Task TitleOption_SetsTheWindowTitle_ArgumentsArriveVerbatim()
    {
        var deadline = CurrentTest.Deadline;
        using var ready = ReadyEvent(out var name);
        using var process = StartHost(CommandLine.Join([HelperProgram.ReadLine, ConsoleInputHelper.SignalOption, name, "a b", "c\"d", ""]), "Demo.exe");
        using var host = Process.GetProcessById(process.ProcessId);
        _ = host.SafeHandle;
        await Step(Signaled(ready), "program ready", deadline); // the host has its console

        await TypeAsync(process.ProcessId, "\n", deadline); // the program reads one (empty) line and ends
        Assert.Equal(0, await ExitedAsync(process, host, deadline));
        var lines = await ScreenAndKeyAsync(process.ProcessId, host, deadline);

        Assert.Equal(ConsoleKeyHelper.TitlePrefix + "Demo.exe", lines[0]);
        Assert.Equal(["ready", "[a b]", "[c\"d]", "[]"], lines[1..5]);
        Assert.Equal(0, host.ExitCode);
    }

    /// <summary>F14-047: a line typed into the host's console (helper <c>--console-type</c>) is what the program reads.</summary>
    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P3-098")]
    public async Task TypedLineReachesTheProgram()
    {
        var deadline = CurrentTest.Deadline;
        using var ready = ReadyEvent(out var name);
        using var process = StartHost(CommandLine.Join([HelperProgram.ReadLine, ConsoleInputHelper.SignalOption, name]), title: null);
        using var host = Process.GetProcessById(process.ProcessId);
        _ = host.SafeHandle;
        await Step(Signaled(ready), "program ready", deadline);

        await TypeAsync(process.ProcessId, "Grüße ✓\n", deadline);
        Assert.Equal(0, await ExitedAsync(process, host, deadline));
        var lines = await ScreenAndKeyAsync(process.ProcessId, host, deadline);

        Assert.Equal(ConsoleKeyHelper.TitlePrefix + Path.GetFileName(HelperProgram.ExecutablePath), lines[0]); // no --title: the file name
        Assert.Contains("got: Grüße ✓", lines);
    }

    /// <summary>
    /// IDE-P3-098 (c): the IDE was started with Ctrl+C turned off (the test process turns it off while it starts the host, as
    /// a parent with <c>CREATE_NEW_PROCESS_GROUP</c> would); the host turns it back on for the program, so Ctrl+C in the
    /// console ends the program with the system's code while the host survives and waits at its prompt.
    /// </summary>
    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P3-098")]
    public async Task CtrlC_EndsTheProgram_HostStaysAtThePrompt()
    {
        const int controlCExit = -1073741510; // 0xC000013A
        var deadline = CurrentTest.Deadline;
        using var ready = ReadyEvent(out var name);
        IRunningProcess process;
        lock (WindowsProcessLauncher.CreateGate) // no other process start of this test run inherits the turned-off Ctrl+C
        {
            Assert.True(SetConsoleCtrlHandler(IntPtr.Zero, true));
            try
            {
                process = StartHost(CommandLine.Join([HelperProgram.ReadLine, ConsoleInputHelper.SignalOption, name]), title: null);
            }
            finally
            {
                SetConsoleCtrlHandler(IntPtr.Zero, false);
            }
        }

        using (process)
        {
            using var host = Process.GetProcessById(process.ProcessId);
            _ = host.SafeHandle;
            await Step(Signaled(ready), "program ready", deadline);

            await RunHelperAsync(string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.ConsoleCtrlC} {process.ProcessId}"), "Ctrl+C helper", deadline);
            Assert.Equal(controlCExit, await ExitedAsync(process, host, deadline));
            Assert.False(host.HasExited);

            var lines = await ScreenAndKeyAsync(process.ProcessId, host, deadline);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_PressAnyKey, RunService.FormatExitCode(controlCExit)), lines[^1]);
            Assert.Equal(controlCExit, host.ExitCode);
        }
    }

    /// <summary>IDE-P3-098 (a): the command line with <c>--title</c> parses back into the same options; without it the title is null.</summary>
    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-P3-098")]
    public async Task CommandLine_WithTitle_ParsesBack()
    {
        await Task.Yield();
        const string tail = "\"C:\\work\\Demo.exe\" \"a b\" c\\\"d \"\"";
        foreach (var title in new string?[] { "Demo (1).exe", @"odd\", null })
        {
            var line = ConsoleHost.CommandLine(@"C:\Program Files\FreexComp\FreexComp.exe", "", "FreexComp.Run.12.3", @"C:\work dir\", @"C:\work\Demo.exe",
                "\"a b\" c\\\"d \"\"", title);
            var args = DebugService.SplitArguments(line).Skip(1).ToArray(); // what the runtime gives Main: without the executable

            var options = ConsoleHost.Parse(args, line);

            Assert.Equal(new ConsoleHost.Options("FreexComp.Run.12.3", title, @"C:\work dir\", @"C:\work\Demo.exe", tail), options);
            Assert.Equal(title is not null, line.Contains(ConsoleHost.TitleOption, StringComparison.Ordinal));
        }

        Assert.Equal(ConsoleHost.CommandLine("h", "", "p", "c", "f", "x"), ConsoleHost.CommandLine("h", "", "p", "c", "f", "x", null)); // Run Without Debugging: unchanged
    }

    private static IRunningProcess StartHost(string helperArguments, string? title) =>
        new WindowsProcessLauncher(new ManualClock(), IdeExecutable).Start(
            new ProcessStartRequest(HelperProgram.ExecutablePath, helperArguments, Path.GetTempPath()) { Capture = false, ConsoleHost = true, ConsoleTitle = title },
            _ => throw new InvalidOperationException("Nothing is captured in host mode."));

    private static async Task<int> ExitedAsync(IRunningProcess process, Process host, TestDeadline deadline)
    {
        try
        {
            return await Step(process.Exited, "exited report", deadline);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException(e.Message + "; " + await SnapshotAsync(host, deadline), e);
        }
    }

    /// <summary>Types <paramref name="text"/> into the console of <paramref name="processId"/> (helper <c>--console-type</c>).</summary>
    internal static async Task TypeAsync(int processId, string text, TestDeadline deadline)
    {
        var file = Path.Combine(Path.GetTempPath(), "freexcomp-type-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(file, text, new System.Text.UTF8Encoding(false), deadline.Token);
            await RunHelperAsync(string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.ConsoleType} {processId} \"{file}\""), "type helper", deadline);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The title and screen of the host's console (helper <c>--console-key</c>), then a key: the host exits.</summary>
    internal static async Task<string[]> ScreenAndKeyAsync(int processId, Process host, TestDeadline deadline)
    {
        var screen = Path.Combine(Path.GetTempPath(), "freexcomp-console-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await RunHelperAsync(string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.ConsoleKey} {processId} \"{screen}\""), "key helper", deadline);
            await Step(host.WaitForExitAsync(deadline.Token), "host exit after key", deadline);
            return await File.ReadAllLinesAsync(screen, deadline.Token);
        }
        finally
        {
            File.Delete(screen);
        }
    }

    private static async Task RunHelperAsync(string mode, string step, TestDeadline deadline)
    {
        var (helper, arguments) = ProcessHelper.Command(mode);
        using var child = Process.Start(new ProcessStartInfo(helper, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!;
        var errors = ChildProcessOutput.Read(child).StandardError; // I-262: a dedicated thread, not the pool
        await Step(child.WaitForExitAsync(deadline.Token), step, deadline);
        Assert.True(child.ExitCode == 0, $"{step} exit code {child.ExitCode}: {await errors}");
    }

    /// <summary>The event a <c>--read-line --signal</c> program sets once it runs (and with it the host's console exists).</summary>
    private static EventWaitHandle ReadyEvent(out string name)
    {
        name = @"Local\FreexComp.Test.Ready." + Guid.NewGuid().ToString("N");
        return new EventWaitHandle(false, EventResetMode.ManualReset, name);
    }

    private static async Task Signaled(WaitHandle handle)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => done.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            await done.Task;
        }
        finally
        {
            registration.Unregister(null);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
