using System.Diagnostics;
using System.Text;

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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(GoldenRunner.Timeout);
        using var process = Process.Start(psi)!;
        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, stdout);

            var image = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(exe, timeout.Token));
            Assert.DoesNotContain("__chkstk", image, StringComparison.Ordinal);
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
