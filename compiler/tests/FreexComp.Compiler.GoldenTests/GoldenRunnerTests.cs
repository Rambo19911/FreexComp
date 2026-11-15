using System.Text;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// C-258: the golden runner stops <c>opc</c> and the program by the C-249 rule (<see cref="FreexComp.Tests.Shared.NativeRun"/>) —
/// the process's own CPU time, a wall-clock cap and the test's cancellation — not by a fixed wall clock that grows with the
/// load of a parallel full-solution run.
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class GoldenRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => GoldenRunner.TryDelete(_root);

    private GoldenCase Case(string name, string source, string expected)
    {
        var folder = Path.Combine(_root, "m2.1");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".pas"), Encoding.UTF8.GetBytes(source));
        File.WriteAllBytes(Path.Combine(folder, name + ".out"), Encoding.UTF8.GetBytes(expected));
        return Assert.Single(GoldenCorpus.Discover(_root));
    }

    /// <summary>A program whose wall time (here: waiting) exceeds the CPU limit is not stopped.</summary>
    [Fact]
    public async Task SlowWallTimeDoesNotStopAProgram()
    {
        var @case = Case("Slow",
            "program Slow;\nprocedure Sleep(Ms: Cardinal); stdcall; external 'kernel32.dll';\nbegin\n  Sleep(2000);\n  Writeln('done');\nend.\n", "done\n");

        var failure = await GoldenRunner.RunAsync(@case, o2: false, GoldenSetup.Default, TestContext.Current.CancellationToken,
            programCpuLimit: TimeSpan.FromSeconds(1), programWallLimit: TimeSpan.FromSeconds(60));

        Assert.True(failure is null, failure);
    }

    /// <summary>A program blocked without using CPU meets the wall-clock cap; the failure names the limit, the times and the state.</summary>
    [Fact]
    public async Task BlockedProgram_IsStoppedByTheWallCap_WithTheRunState()
    {
        var @case = Case("Blocked",
            "program Blocked;\nprocedure Sleep(Ms: Cardinal); stdcall; external 'kernel32.dll';\nbegin\n  Sleep($FFFFFFFF);\nend.\n", "");

        var failure = await GoldenRunner.RunAsync(@case, o2: false, GoldenSetup.Default, TestContext.Current.CancellationToken,
            programCpuLimit: TimeSpan.FromSeconds(20), programWallLimit: TimeSpan.FromSeconds(2));

        TestContext.Current.TestOutputHelper?.WriteLine(failure ?? "");
        Assert.NotNull(failure);
        Assert.Contains("program stopped: wall-clock limit 2.0 s reached: Blocked.exe stopped after ", failure);
        Assert.Contains("still running, threads [wait:", failure);
        const string kept = "(work directory: ";
        GoldenRunner.TryDelete(failure[(failure.LastIndexOf(kept, StringComparison.Ordinal) + kept.Length)..].TrimEnd(')'));
    }
}
