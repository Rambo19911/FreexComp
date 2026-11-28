using System.Diagnostics;
using System.Text;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.IntegrationTests.TestSupport;

/// <summary>
/// The FakeAdapter's console helper modes (DBG-559 b), each in a process of its own: the test process never attaches
/// to another console. A helper that fails or does not finish within the safety limit fails the test.
/// </summary>
internal static class ConsoleHelper
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Types <paramref name="text"/> into the console of <paramref name="processId"/> (a line feed is Enter).</summary>
    public static void Type(int processId, string text)
    {
        var file = TempFile();
        try
        {
            File.WriteAllText(file, text, Utf8);
            RunHelper("--console-type", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The screen of the console of <paramref name="processId"/>: its rows up to the cursor's, right-trimmed.</summary>
    public static IReadOnlyList<string> Screen(int processId)
    {
        var file = TempFile();
        try
        {
            RunHelper("--console-screen", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), file);
            return File.ReadAllText(file, Utf8).Split('\n')[..^1];
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Sends Ctrl+C to every process of the console of <paramref name="processId"/>.</summary>
    public static void CtrlC(int processId) =>
        RunHelper("--console-ctrl-c", processId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Sends Ctrl+Break to every process of the console of <paramref name="processId"/>.</summary>
    public static void CtrlBreak(int processId) =>
        RunHelper("--console-ctrl-break", processId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "FreexComp.console-" + Guid.NewGuid().ToString("N") + ".txt");

    private static void RunHelper(params string[] arguments)
    {
        var info = new ProcessStartInfo(TestTerminal.HelperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The console helper could not be started.");
        ProcessLeakFixture.Registry.Track(process.Id, "console helper " + arguments[0]);
        // The pipe is drained on a thread of its own while the helper runs (a child's pipe only blocks).
        var stderr = "";
        var read = DedicatedThread.Run("FreexComp.Tests.ConsoleHelperStderr", () => stderr = process.StandardError.ReadToEnd());
        if (!process.WaitForExit(IntegrationFixture.Safety))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"The console helper {arguments[0]} did not finish within {IntegrationFixture.Safety.TotalSeconds} s.");
        }

        read.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"The console helper {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {stderr}");
    }
}
