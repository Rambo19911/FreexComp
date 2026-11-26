using System.Diagnostics;
using System.Text;
using FreexComp.Tests.Shared;

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
    private static readonly string Rtl = TestRtl.Directory;
    private const string ObjRoot = @"C:\det\obj";

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            TestDirectories.Delete(_temp);
        }
    }

    /// <summary>The M2.1 golden programs that compile (those with an <c>.out</c>), by name.</summary>
    private static string[] Programs() =>
        [.. Directory.EnumerateFiles(Corpus, "*.pas").Where(p => File.Exists(Path.ChangeExtension(p, ".out")))
            .Select(p => Path.GetFileNameWithoutExtension(p)!).Order(StringComparer.Ordinal)];

    private static InMemoryFileSystem LoadSources()
    {
        var fs = new InMemoryFileSystem().AddSystem(Rtl);
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
        var options = new CompilationOptions { MainFile = Path.Combine(Corpus, name + ".pas"), RtlDirectory = Rtl, ObjectDirectory = obj };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, name + ": " + string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return (fs.ReadAllText(Path.Combine(obj, name + ".ll")), fs.ReadAllText(Path.Combine(obj, "System.ll")));
    }

    [Fact]
    public async Task GoldenPrograms_CompiledTwiceInProcess_GiveIdenticalLl()
    {
        var programs = Programs();
        Assert.True(programs.Length >= 50, $"only {programs.Length} M2.1 programs found in {Corpus}");

        // C-237 (CMP-911): each pass compiles the programs on several threads (every compilation is still independent, with its own
        // file system); the second pass runs in reverse order on other threads, after shifting allocation order and hash codes
        var first = EmitAll(programs);

        _ = Enumerable.Range(0, 10_000).Select(i => new object()).ToList();       // shift allocation order and object hash codes
        var second = await Task.Run(() => EmitAll([.. programs.Reverse()]), TestContext.Current.CancellationToken);

        var differing = programs.Where(p => first[p] != second[p]).ToList();
        Assert.True(differing.Count == 0, "non-deterministic .ll: " + string.Join(", ", differing));
    }

    private static Dictionary<string, (string Program, string System)> EmitAll(string[] programs)
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Program, string System)>(StringComparer.Ordinal);
        Parallel.ForEach(programs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
            p => results[p] = EmitInProcess(LoadSources(), p));
        return new Dictionary<string, (string Program, string System)>(results, StringComparer.Ordinal);
    }

    /// <summary>Programs that together use every M2.1 construct family: nesting/frames, case tables, constants, Write, externals, includes, conditionals, checks.</summary>
    public static TheoryData<string> CrossProcessPrograms() =>
        ["NestedProcs", "CaseBasic", "ConstExprTypes", "WriteWidthInt", "ExternalKernel32", "IncludeFile", "ConditionalCompilation", "RangeCheckOn"];

    [Theory]
    [MemberData(nameof(CrossProcessPrograms))]
    [Trait("Kind", "Native")]
    [Trait("Category", "Integration")]
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

    /// <summary>
    /// C-236: the same proofs for golden cases of M2.3 to M2.6, every <c>.ll</c> of the compilation compared. A nested class, so
    /// that xUnit runs it as its own collection in parallel with the M2.1 programs above.
    /// </summary>
    [Trait("Phase", "2")]
    public sealed class LaterMilestones : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_temp))
            {
                TestDirectories.Delete(_temp);
            }
        }

        /// <summary>
        /// C-236: golden cases of M2.3…M2.6 — multi-unit programs (folders), classes and exceptions, threadvars across units, the
        /// SysUtils unit of the RTL, interfaces, RTTI, generics (also instantiated across units) and anonymous methods.
        /// </summary>
        public static TheoryData<string> LaterMilestoneCases() =>
        [
            "m2.3/UnitInitFinalOrder", "m2.3/UnitTypesAcrossUnits",
            "m2.4/ClassVirtual", "m2.4/TryExceptOnOrder", "m2.4/ExceptionSysUtils", "m2.4/ThreadVarUnits",
            "m2.5/IntfRefCount", "m2.5/IntfMultiple", "m2.5/IntfMethodResolution", "m2.5/RttiPublished",
            "m2.6/GenericClass", "m2.6/GenericInterface", "m2.6/GenericMethod", "m2.6/GenericConstraints",
            "m2.6/GenericAcrossUnits", "m2.6/GenericClassVarManaged",
            "m2.6/AnonCapture", "m2.6/AnonNested", "m2.6/AnonLoopCapture", "m2.6/AnonSelfTwoLevels", "m2.6/AnonLifetime",
            "generics/GenericQualifiedAcrossUnits",
        ];

        /// <summary>
        /// C-345 (CMP-385, C-228 rule; LANG-775): the nested types of an instance go with it ā€” the unit that uses
        /// <c>TList&lt;Integer&gt;.TEnumerator</c> and <c>TOuter.TCell&lt;string&gt;</c> defines them <c>linkonce_odr</c>, the declaring unit,
        /// which uses no instance, defines none (also when a method heading of the nested type names the generic itself).
        /// </summary>
        [Fact]
        [Trait("Phase", "8")]
        [Trait("Req", "CMP-1391")]
        public void NestedTypesOfInstances_AreDefinedByTheUsingUnitsOnly()
        {
            var files = EmitCase("generics/GenericQualifiedAcrossUnits", maxParallelism: 0);
            Assert.DoesNotContain("define linkonce_odr", files["UQGen.ll"], StringComparison.Ordinal);
            Assert.Contains("define linkonce_odr ptr @\"UQGen.TList<Integer>.TEnumerator.Create\"", files["UQUser.ll"], StringComparison.Ordinal);
            Assert.Contains("define linkonce_odr void @\"UQGen.TOuter.TCell<string>.Show\"", files["UQUser.ll"], StringComparison.Ordinal);
        }

        /// <summary>
        /// C-236 (CMP-001, CMP-156): every <c>.ll</c> of the compilation (program, its units, the RTL units) is byte-identical when
        /// compiled again — sequentially (<c>MaxParallelism</c> 1) and then, on another thread after other allocations, with 8 and
        /// 3 threads (the opc-process test below uses the default, the processor count).
        /// </summary>
        [Theory]
        [MemberData(nameof(LaterMilestoneCases))]
        public async Task CorpusCase_CompiledAgainWithOtherThreadCounts_GivesIdenticalLl(string id)
        {
            var first = EmitCase(id, maxParallelism: 1);
            Assert.Contains(first.Keys, k => k.Equals("System.ll", StringComparison.Ordinal));

            _ = Enumerable.Range(0, 10_000).Select(i => new object()).ToList();       // shift allocation order and object hash codes
            foreach (var threads in new[] { 8, 3 })
            {
                var again = await Task.Run(() => EmitCase(id, threads), TestContext.Current.CancellationToken);
                AssertSameFiles(first, again, $"{id}, MaxParallelism = {threads}");
            }
        }

        /// <summary>C-236: one case of each family above in two <c>opc</c> processes (own hash seeds) gives the in-process files.</summary>
        public static TheoryData<string> LaterMilestoneCrossProcessCases() =>
            ["m2.3/UnitInitFinalOrder", "m2.4/ExceptionSysUtils", "m2.5/IntfRefCount", "m2.6/GenericAcrossUnits", "m2.6/AnonCapture"];

        [Theory]
        [MemberData(nameof(LaterMilestoneCrossProcessCases))]
        [Trait("Kind", "Native")]
        [Trait("Category", "Integration")]
        public async Task CorpusCase_InTwoOpcProcesses_GivesTheInProcessLl(string id)
        {
            var expected = EmitCase(id, maxParallelism: 0);
            var (main, _) = CaseSource(id);

            var runs = await Task.WhenAll(RunOpcFilesAsync(main, id.Replace('/', '_') + "_a"), RunOpcFilesAsync(main, id.Replace('/', '_') + "_b"));

            foreach (var run in runs)
            {
                AssertSameFiles(expected, run, id + " (opc process)");
            }
        }

        /// <summary>The main file of a golden case and the directory whose sources belong to it (CMP-900: a file, or a folder).</summary>
        private static (string Main, string Directory) CaseSource(string id)
        {
            var path = Path.Combine(RepoPaths.Root, "tests", "compiler", id.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(path)
                ? (Path.Combine(path, Path.GetFileName(path) + ".pas"), path)
                : (path + ".pas", Path.GetDirectoryName(path)!);
        }

        /// <summary>The sources of <c>rtl/src</c> and <c>rtl/winapi/src</c> (Windows for SysUtils) and the unit alias table.</summary>
        private static readonly Lazy<(string Path, string Text)[]> RtlFiles = new(() =>
            [.. new[] { Rtl, Path.Combine(RepoPaths.Root, "rtl", "winapi", "src") }.SelectMany(Directory.EnumerateFiles)
                .Where(f => f.EndsWith(".pas", StringComparison.Ordinal) || f.EndsWith(".inc", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(f => (f, File.ReadAllText(f)))]);

        /// <summary>
        /// Every <c>.ll</c> (file name → text) of one in-process compilation (stage Ll) of a case, with the whole RTL and the case's
        /// sources (a folder: all its files; a single-file case: the file and the include files beside it) in a fresh file system.
        /// </summary>
        private static SortedDictionary<string, string> EmitCase(string id, int maxParallelism)
        {
            var (main, directory) = CaseSource(id);
            var fs = new InMemoryFileSystem();
            foreach (var (path, text) in RtlFiles.Value)
            {
                fs.Add(path, text);
            }

            var single = !Path.GetFileNameWithoutExtension(main).Equals(Path.GetFileName(directory), StringComparison.Ordinal);
            foreach (var file in Directory.EnumerateFiles(directory).Where(f => single
                ? f.Equals(main, StringComparison.Ordinal) || f.EndsWith(".inc", StringComparison.Ordinal)
                : f.EndsWith(".pas", StringComparison.Ordinal) || f.EndsWith(".inc", StringComparison.Ordinal)))
            {
                fs.Add(file, File.ReadAllText(file));
            }

            var obj = Path.Combine(ObjRoot, id.Replace('/', '_'), maxParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var options = new CompilationOptions { MainFile = main, RtlDirectory = Rtl, ObjectDirectory = obj, MaxParallelism = maxParallelism };
            var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, id + ": " + string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            return new SortedDictionary<string, string>(
                fs.EnumerateFiles(obj, "*.ll").ToDictionary(f => Path.GetFileName(f), f => fs.ReadAllText(f), StringComparer.Ordinal), StringComparer.Ordinal);
        }

        private static void AssertSameFiles(SortedDictionary<string, string> expected, SortedDictionary<string, string> actual, string what)
        {
            Assert.Equal(expected.Keys, actual.Keys);
            var differing = expected.Keys.Where(k => !string.Equals(expected[k], actual[k], StringComparison.Ordinal)).ToList();
            Assert.True(differing.Count == 0, $"{what}: non-deterministic .ll: " + string.Join(", ", differing));
        }

        /// <summary><c>opc &lt;main&gt; --emit ll</c> in its own process; every <c>.ll</c> it wrote (file name → text).</summary>
        private async Task<SortedDictionary<string, string>> RunOpcFilesAsync(string main, string run)
        {
            var obj = Path.Combine(_temp, run);
            await RunOpcProcessAsync(main, obj);
            return new SortedDictionary<string, string>(
                Directory.EnumerateFiles(obj, "*.ll").ToDictionary(f => Path.GetFileName(f), f => Encoding.UTF8.GetString(File.ReadAllBytes(f)), StringComparer.Ordinal),
                StringComparer.Ordinal);
        }
    }

    /// <summary><c>opc &lt;name&gt;.pas --emit ll</c> in its own process (own hash seeds); returns the two <c>.ll</c> texts.</summary>
    private async Task<(string Program, string System)> RunOpcAsync(string name, string run)
    {
        var obj = Path.Combine(_temp, name, run);
        await RunOpcProcessAsync(Path.Combine(Corpus, name + ".pas"), obj);

        // the in-process compilation reads its sources with the same UTF-8 decoding as opc's file system
        return (Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(obj, name + ".ll"))),
            Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(obj, "System.ll"))));
    }

    private static async Task RunOpcProcessAsync(string main, string obj)
    {
        var name = Path.GetFileNameWithoutExtension(main);
        var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "opc.exe"))
        {
            WorkingDirectory = Path.GetDirectoryName(main)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { main, "--emit", "ll", "--obj-dir", obj, "--rtl", Rtl, "-o", Path.Combine(obj, name + ".exe") })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = PipePump.Text(process.StandardOutput, "stdout");           // C-319: each pipe on its own thread
        var stderr = PipePump.Text(process.StandardError, "stderr");
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, $"opc {name} exit {process.ExitCode}\n{await stdout}\n{await stderr}");
    }
}
