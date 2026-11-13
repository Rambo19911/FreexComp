using System.Diagnostics;

namespace FreexComp.Compiler.GoldenTests;

[Trait("Phase", "1")]
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

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.StartsWith("opc ", stdout, StringComparison.Ordinal);
    }
}
