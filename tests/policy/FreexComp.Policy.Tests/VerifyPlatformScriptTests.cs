using System.Globalization;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// PLAT-012, PLAT-026: <c>scripts/verify-platform.ps1</c>, the manual check of a second system. The script runs the
/// platform tests of the current tree and writes a report; here it runs one of them, so that this test does not start
/// the suite it belongs to a second time.
/// </summary>
[Trait("Phase", "8")]
[Trait("Category", "Platform")]
[Trait("Category", "Toolchain")]
[Collection(PlatformCollection.Name)]
public sealed partial class VerifyPlatformScriptTests
{
    [GeneratedRegex(@"^Host: Windows 1[01] 10\.0\.(\d+) x64$")]
    private static partial Regex HostLine();

    [Fact]
    [Trait("Req", "PLAT-012")]
    [Trait("Req", "PLAT-026")]
    public async Task WritesReportAndExitCode()
    {
        var script = RepoPaths.Combine("scripts", "verify-platform.ps1");
        Assert.True(File.Exists(script), "scripts/verify-platform.ps1 is missing");
        var directory = Path.Combine(Path.GetTempPath(), "freexcomp-platform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var report = Path.Combine(directory, "report.txt");
            string[] arguments =
            [
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-Output", report,
                "-Filter", "FullyQualifiedName=" + typeof(PlatformTests).FullName + "." + nameof(PlatformTests.HostIsInTheSupportMatrix),
                "-NoBuild",
            ];

            var (exit, output) = await PlatformPrograms.RunAsync(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                arguments,
                TimeSpan.FromMinutes(10),
                TestContext.Current.CancellationToken);

            Assert.True(exit == 0, $"exit code {exit}\n{output}");
            Assert.True(File.Exists(report), "no report was written\n" + output);
            var lines = (await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken)).TrimEnd('\n').Split('\n');
            Assert.Equal(3, lines.Length);
            var host = HostLine().Match(lines[0]);
            Assert.True(host.Success, "host line: " + lines[0]);
            Assert.Equal(Environment.OSVersion.Version.Build, int.Parse(host.Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.Equal("Tests: total 1, passed 1, failed 0, skipped 0", lines[1]);
            Assert.Equal("Exit code: 0", lines[2]);

            // the same lines on the standard output
            Assert.Contains(lines[0], output, StringComparison.Ordinal);
            Assert.Contains(lines[1], output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
