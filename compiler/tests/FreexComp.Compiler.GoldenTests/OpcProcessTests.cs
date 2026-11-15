using System.Diagnostics;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.GoldenTests;

// C-191: starts the opc process, so it belongs to Phase 2 (IDE-P1-001: Phase 1 does not require opc).
[Trait("Phase", "2")]
public sealed class OpcProcessTests
{
    [Fact]
    public async Task VersionPrintsNameAndExitsZero()
    {
        var opc = Path.Combine(AppContext.BaseDirectory, "opc.exe");
        var psi = new ProcessStartInfo(opc, "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var limit = new RunLimit("opc.exe", NativeRun.CpuLimit, NativeRun.WallLimit, TestContext.Current.CancellationToken);   // C-258
        using var process = Process.Start(psi)!;
        limit.Attach(process.Id);
        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);

            Assert.Equal(0, process.ExitCode);
            Assert.StartsWith("opc ", stdout, StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (limit.Report is { } report)
        {
            Assert.Fail(report);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
