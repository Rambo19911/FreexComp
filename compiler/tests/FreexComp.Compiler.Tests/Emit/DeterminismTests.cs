using System.Diagnostics;
using System.Text;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// testing.md §3 <c>DeterminismTests</c> (task C-048): compiling the same sources again gives byte-identical <c>.ll</c>.
/// Each .NET process draws its own random seed for string and <c>HashCode</c> hashing, so the runs in separate
/// <c>opc</c> processes are the "shuffled internal dictionary seeds" case; the in-process runs repeat the compilation
/// on another thread, in reverse order, after other work, so object hash codes and allocation order differ too.
/// </summary>
[Trait("Phase", "2")]
public sealed class DeterminismTests : IDisposable
{
    private static readonly string Corpus = Path.Combine(RepoPaths.Root, "tests", "compiler", "m2.1");
    private static readonly string MiniRtl = Path.Combine(RepoPaths.Root, "compiler", "tests", "minirtl");
    private const string ObjRoot = @"C:\det\obj";

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    /// <summary>The M2.1 golden programs that compile (those with an <c>.out</c>), by name.</summary>
    private static string[] Programs() =>
        [.. Directory.EnumerateFiles(Corpus, "*.pas").Where(p => File.Exists(Path.ChangeExtension(p, ".out")))
            .Select(p => Path.GetFileNameWithoutExtension(p)!).Order(StringComparer.Ordinal)];

    private static InMemoryFileSystem LoadSources()
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(MiniRtl, "System.pas"), File.ReadAllText(Path.Combine(MiniRtl, "System.pas")));
        foreach (var file in Directory.EnumerateFiles(Corpus).Where(f => f.EndsWith(".pas", StringComparison.Ordinal) || f.EndsWith(".inc", StringComparison.Ordinal)))
        {
            fs.Add(file, File.ReadAllText(file));
        }

        return fs;
    }

    /// <summary>Program and System <c>.ll</c> texts of one in-process compilation (stage Ll).</summary>
    private static (string Program, string System) EmitInProcess(InMemoryFileSystem fs, string name)
    {
        var obj = Path.Combine(ObjRoot, name);
        var options = new CompilationOptions { MainFile = Path.Combine(Corpus, name + ".pas"), RtlDirectory = MiniRtl, ObjectDirectory = obj };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, name + ": " + string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return (fs.ReadAllText(Path.Combine(obj, name + ".ll")), fs.ReadAllText(Path.Combine(obj, "System.ll")));
    }

    [Fact]
    public async Task GoldenPrograms_CompiledTwiceInProcess_GiveIdenticalLl()
    {
        var programs = Programs();
        Assert.True(programs.Length >= 50, $"only {programs.Length} M2.1 programs found in {Corpus}");

        var first = programs.ToDictionary(p => p, p => EmitInProcess(LoadSources(), p), StringComparer.Ordinal);

        _ = Enumerable.Range(0, 10_000).Select(i => new object()).ToList();       // shift allocation order and object hash codes
        var second = await Task.Run(() => programs.Reverse().ToDictionary(p => p, p => EmitInProcess(LoadSources(), p), StringComparer.Ordinal),
            TestContext.Current.CancellationToken);

        var differing = programs.Where(p => first[p] != second[p]).ToList();
        Assert.True(differing.Count == 0, "non-deterministic .ll: " + string.Join(", ", differing));
    }

    /// <summary>Programs that together use every M2.1 construct family: nesting/frames, case tables, constants, Write, externals, includes, conditionals, checks.</summary>
    public static TheoryData<string> CrossProcessPrograms() =>
        ["NestedProcs", "CaseBasic", "ConstExprTypes", "WriteWidthInt", "ExternalKernel32", "IncludeFile", "ConditionalCompilation", "RangeCheckOn"];

    [Theory]
    [MemberData(nameof(CrossProcessPrograms))]
    public async Task GoldenProgram_InTwoOpcProcesses_GivesTheInProcessLl(string name)
    {
        var expected = EmitInProcess(LoadSources(), name);

        var runs = await Task.WhenAll(RunOpcAsync(name, "a"), RunOpcAsync(name, "b"));

        foreach (var (program, system) in runs)
        {
            Assert.Equal(expected.Program, program);
            Assert.Equal(expected.System, system);
        }
    }

    /// <summary><c>opc &lt;name&gt;.pas --emit ll</c> in its own process (own hash seeds); returns the two <c>.ll</c> texts.</summary>
    private async Task<(string Program, string System)> RunOpcAsync(string name, string run)
    {
        var obj = Path.Combine(_temp, name, run);
        var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "opc.exe"))
        {
            WorkingDirectory = Corpus,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { Path.Combine(Corpus, name + ".pas"), "--emit", "ll", "--obj-dir", obj, "--rtl", MiniRtl, "-o", Path.Combine(obj, name + ".exe") })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, $"opc {name} exit {process.ExitCode}\n{await stdout}\n{await stderr}");

        // the in-process compilation reads its sources with the same UTF-8 decoding as opc's file system
        return (Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(obj, name + ".ll"))),
            Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(obj, "System.ll"))));
    }
}
