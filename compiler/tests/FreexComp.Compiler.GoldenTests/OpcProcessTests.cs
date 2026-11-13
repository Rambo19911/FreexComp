using System.Diagnostics;

namespace FreexComp.Compiler.GoldenTests;

[Trait("Phase", "1")]
public sealed class OpcProcessTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task VersionPrintsNameAndExitsZero()
    {
        var opc = Path.Combine(AppContext.BaseDirectory, "opc.exe");
        var psi = new ProcessStartInfo(opc, "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(psi)!;
        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            Assert.Equal(0, process.ExitCode);
            Assert.StartsWith("opc ", stdout, StringComparison.Ordinal);
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
