using System.Diagnostics;
using System.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// testing.md #122 / CMP-611: golden <c>m2.2/StackProbeLargeFrame</c> runs a frame far above one page (its output proves the
/// probe works); this test checks the other half — the executable does not import <c>__chkstk</c> from any DLL (the name
/// would then appear in its import name table).
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class StackProbeImportTests
{
    [Fact]
    [Trait("Req", "CMP-611")]
    public async Task LargeFrameProgram_DoesNotImportChkstk()
    {
        var setup = GoldenSetup.Default;
        var source = Path.Combine(GoldenSetup.CorpusDirectory, "m2.2", "StackProbeLargeFrame.pas");
        var temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var exe = Path.Combine(temp, "StackProbeLargeFrame.exe");
        var psi = new ProcessStartInfo(setup.OpcPath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(source)!,
        };
        foreach (var argument in new[] { source, "-o", exe, "--obj-dir", Path.Combine(temp, "obj"), "--rtl", setup.RtlDirectory, "--toolchain", setup.ToolchainDirectory })
        {
            psi.ArgumentList.Add(argument);
        }

        using var limit = new RunLimit("opc.exe", NativeRun.CpuLimit, NativeRun.WallLimit, TestContext.Current.CancellationToken);   // C-258
        using var process = Process.Start(psi)!;
        limit.Attach(process.Id);
        try
        {
            var stdout = await PipePump.Text(process.StandardOutput, "stdout").WaitAsync(limit.Token);   // C-319
            await process.WaitForExitAsync(limit.Token);
            Assert.True(process.ExitCode == 0, stdout);

            var image = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("__chkstk", image, StringComparison.Ordinal);
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

        GoldenRunner.TryDelete(temp);
    }
}
