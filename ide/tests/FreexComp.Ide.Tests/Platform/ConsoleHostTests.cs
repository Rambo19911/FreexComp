using System.Diagnostics;
using System.Globalization;
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
                var errors = key.StandardError.ReadToEndAsync(token);
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
}
