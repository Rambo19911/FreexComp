using System.Diagnostics;

namespace FreexComp.E2E.Tests;

/// <summary>
/// Runs tests/spikes/run.ps1, which proves on the pinned toolchain that the back-end mechanisms the compiler,
/// debugger and data areas rely on work: IR to exe without a C runtime, SEH exceptions, stack probes,
/// manifest embedding, a scripted lldb-dap debug session (CodeView and DWARF) and a SQLite DLL call.
/// Fails (never skips) when the toolchain is missing (TESTING.md rule 5). Phase 2: Phase 1 must not require the
/// LLVM toolchain (IDE-P1-001).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class ToolchainSpikeTests
{
    [Fact]
    public async Task AllToolchainSpikesPass()
    {
        var script = Path.Combine(RepoPaths.Root, "tests", "spikes", "run.ps1");
        Assert.True(File.Exists(script), "missing " + script);

        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var ct = TestContext.Current.CancellationToken;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail("toolchain spikes timed out after 5 minutes");
        }

        var output = await stdout + await stderr;
        Assert.True(p.ExitCode == 0, "tests/spikes/run.ps1 failed:\n" + output);
        Assert.Contains("All toolchain spikes passed.", output);
        Assert.DoesNotContain("[FAIL]", output);
    }
}
