using System.Diagnostics;
using System.Globalization;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.Tests.Fixtures;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>The console host (I-138, IDE-P3-087/088) with the real IDE executable, pipe and console.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ConsoleHostTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);

    /// <summary>The IDE executable next to the test assembly (the project reference copies its apphost).</summary>
    private static string IdeExecutable => Path.ChangeExtension(typeof(ConsoleHost).Assembly.Location, ".exe");

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-087")]
    [Trait("Req", "IDE-P3-088")]
    public async Task RunsChildReportsExitCodeAndWaitsForKey()
    {
        var token = TestContext.Current.CancellationToken;
        var (file, arguments) = ProcessHelper.Command(ProcessHelper.ExitWith + " 3");
        var launcher = new WindowsProcessLauncher(new ManualClock(), IdeExecutable);
        using var process = launcher.Start(new ProcessStartRequest(file, arguments, Path.GetTempPath()) { Capture = false, ConsoleHost = true },
            _ => throw new InvalidOperationException("Nothing is captured in host mode."));
        using var host = Process.GetProcessById(process.ProcessId);
        _ = host.SafeHandle; // keep a handle so the exit code stays readable

        // The program's exit code arrives over the pipe while the host still waits at its prompt.
        Assert.Equal(3, await process.Exited.WaitAsync(Limit, token));
        Assert.False(host.HasExited);

        // A key press in the host's console closes it; the host exits with the program's code.
        var screen = Path.Combine(Path.GetTempPath(), "freexcomp-console-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var (helper, helperArguments) = ProcessHelper.Command(
                string.Create(CultureInfo.InvariantCulture, $"{ProcessHelper.ConsoleKey} {process.ProcessId} \"{screen}\""));
            using (var key = Process.Start(new ProcessStartInfo(helper, helperArguments) { UseShellExecute = false, CreateNoWindow = true })!)
            {
                await key.WaitForExitAsync(token).WaitAsync(Limit, token);
                Assert.Equal(0, key.ExitCode);
            }

            await host.WaitForExitAsync(token).WaitAsync(Limit, token);
            Assert.Equal(3, host.ExitCode);

            var lines = await File.ReadAllLinesAsync(screen, token);
            Assert.Equal(ConsoleKeyHelper.TitlePrefix + Path.GetFileName(file), lines[0]); // the window title = the program's file name
            // The program wrote into the host's console; then an empty line and the host's prompt (the cursor waits after it).
            var prompt = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_PressAnyKey, "3");
            Assert.Equal([ProcessHelper.ChildGreeting, "", prompt], lines[^3..]);
        }
        finally
        {
            File.Delete(screen);
        }
    }
}
