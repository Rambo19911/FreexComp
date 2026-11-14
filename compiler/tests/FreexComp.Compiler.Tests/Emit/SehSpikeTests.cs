using System.Collections.Immutable;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-079 SEH spike: hand-written IR in <c>Seh/*.ll</c> uses exactly the funclet patterns of CMP-391 (try..except),
/// CMP-392 (outlined finally), CMP-393 (function-level managed cleanup) with personality <c>__C_specific_handler</c>
/// from ntdll (CMP-390) and a tiny IR filter/raise (<c>Seh/SehRuntime.ll</c>, ABI-120…124 reduced). Each scenario
/// is built with the pinned clang + lld-link (no C runtime) at <c>-O0</c> and <c>-O2</c> and run.
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class SehSpikeTests
{
    private static string SehDirectory => Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Seh");

    public static TheoryData<string> Optimisations() => ["-O0", "-O2"];

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-391")]
    public async Task RaiseIsCaughtByTheHandler(string optimisation) =>
        Assert.Equal(("AC1~Z", 0), await Run("RaiseCatch", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-392")]
    public async Task FinallyRunsOnNormalExitAndOnUnwind(string optimisation) =>
        Assert.Equal(("F2EF1H1~Z", 0), await Run("FinallyUnwind", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-391")]
    [Trait("Req", "CMP-392")]
    public async Task NestedScopesUnwindInnermostFirst(string optimisation) =>
        Assert.Equal(("NFO2~Z", 0), await Run("Nested", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-391")]
    [Trait("Req", "CMP-393")]
    public async Task RethrowReachesTheOuterHandlerWithTheSameObject(string optimisation) =>
        Assert.Equal(("RXH1~Z", 0), await Run("Rethrow", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-392")]
    [Trait("Req", "ABI-124")]
    public async Task UnhandledRunsFinallyThenTheStartHandler(string optimisation) =>
        Assert.Equal(("FU1", 217), await Run("Unhandled", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-391")]
    [Trait("Req", "ABI-121")]
    public async Task EndCatchRaises(string optimisation) =>
        Assert.Equal(("H~DS2~O3~Z", 0), await Run("EndCatchRaises", optimisation));

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Req", "CMP-392")]
    [Trait("Req", "ABI-121")]
    public async Task RaiseSpCheck(string optimisation) =>
        Assert.Equal(("I2~O1~Z", 0), await Run("RaiseSpCheck", optimisation));

    private static async Task<(string StdOut, int ExitCode)> Run(string scenario, string optimisation)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var bin = Path.GetDirectoryName(Clang.Path)!;
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fs = PhysicalFileSystem.Instance;
        var runner = new ToolRunner(new ProcessRunner(), new ToolRunOptions(Location.ForFile(Path.Combine(dir, scenario + ".pas"))));
        var ct = TestContext.Current.CancellationToken;
        var level = optimisation == "-O2" ? OptimizationLevel.O2 : OptimizationLevel.O0;
        var clang = new ClangInvoker(runner, Clang.Path);
        var objects = ImmutableArray.CreateBuilder<string>();
        foreach (var file in new[] { scenario, "SehRuntime" })
        {
            var obj = Path.Combine(dir, file + ".obj");
            var compiled = await clang.CompileAsync(Path.Combine(SehDirectory, file + ".ll"), obj, level, false, ct);
            Assert.True(compiled.Failure is null, string.Join(Environment.NewLine, compiled.Output));
            objects.Add(obj);
        }

        var libs = ImmutableArray.CreateBuilder<string>();
        var dllTool = new DllToolInvoker(runner, Path.Combine(bin, "llvm-dlltool.exe"), fs);
        foreach (var dll in new[] { "kernel32.dll", "ntdll.dll" })
        {
            var lib = await dllTool.BuildAsync(dll, Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, Path.ChangeExtension(dll, ".def")), dir, ct);
            Assert.Null(lib.Failure);
            libs.Add(lib.LibraryPath);
        }

        var exe = Path.Combine(dir, scenario + ".exe");
        var linked = await new LinkInvoker(runner, Path.Combine(bin, "lld-link.exe"), fs)
            .LinkAsync(new LinkRequest(exe, dir, objects.ToImmutable()) { ImportLibraries = libs.ToImmutable() }, ct);
        Assert.True(linked.Success, string.Join(Environment.NewLine, linked.Diagnostics.Select(d => d.Message)));

        var stdout = new List<string>();
        var stderr = new List<string>();
        var exitCode = await new ProcessRunner().RunAsync(exe, [], dir, stdout.Add, stderr.Add, ct);
        Assert.Empty(stderr);
        try
        {
            TestDirectories.Delete(dir);
        }
        catch (IOException)
        {
            // a just-exited exe can stay locked briefly; the temp folder is left behind
        }
        catch (UnauthorizedAccessException)
        {
        }

        return (string.Concat(stdout), exitCode);
    }
}
