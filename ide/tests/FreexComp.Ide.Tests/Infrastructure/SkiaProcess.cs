using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// I-240: a visual snapshot test needs the real Skia renderer, the other headless tests the default headless drawing
/// backend (IDE-TEST-010), and one process cannot use both. Such a test therefore starts the test program again for
/// itself alone, with <see cref="Variable"/> set — there <c>TestAppBuilder</c> sets the application up with Skia and
/// the body of the test runs — and passes when that run passed:
/// <code>if (!SkiaProcess.IsCurrent) { await SkiaProcess.RunThisTestAsync(typeof(MyTests)); return; }</code>
/// </summary>
public static class SkiaProcess
{
    public const string Variable = "FREEXCOMP_TEST_RENDERER";

    /// <summary>True in the child process that renders with Skia.</summary>
    public static bool IsCurrent { get; } = Environment.GetEnvironmentVariable(Variable) == "skia";

    /// <summary>
    /// Runs the calling test method alone in a child process that renders with Skia, bounded by the calling test's own
    /// cancellation; fails with the child's output unless exactly that one test ran and passed.
    /// </summary>
    public static async Task RunThisTestAsync(Type testClass, [CallerMemberName] string method = "")
    {
        ArgumentNullException.ThrowIfNull(testClass);
        var program = Path.Combine(AppContext.BaseDirectory, typeof(SkiaProcess).Assembly.GetName().Name + ".exe");
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-method", $"{testClass.FullName}.{method}", "-noLogo", "-noColor" })
            start.ArgumentList.Add(argument);
        start.Environment[Variable] = "skia";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + program);
        try
        {
            var cancel = TestContext.Current.CancellationToken;
            var output = ChildProcessOutput.Read(process); // I-262: dedicated threads, not the pool
            await process.WaitForExitAsync(cancel);
            var text = await output.StandardOutput.WaitAsync(cancel) + await output.StandardError.WaitAsync(cancel);
            Assert.True(process.ExitCode == 0 && text.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 0", StringComparison.Ordinal),
                $"The Skia run of {method} failed (exit code {process.ExitCode}):{Environment.NewLine}{text}");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
