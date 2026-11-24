using System.Text.RegularExpressions;
using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;
using FreexComp.Opc;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>C-045: the <c>opc</c> command line (compiler-cli.md §1–2, build-engine.md CMP-850…853), run in-process.</summary>
[Trait("Phase", "2")]
public sealed partial class OpcCliTests
{
    private const string Cwd = @"C:\work";
    private const string Source = "program P; begin Writeln('hi') end.";

    /// <summary>C-149: the real RTL's <c>System.pas</c> and the include files it reads.</summary>
    private static readonly (string Name, string Text)[] RtlSystem =
        [.. Directory.GetFiles(Path.Combine(RepoPaths.Root, "rtl", "src"), "*.inc").Order(StringComparer.Ordinal)
            .Prepend(Path.Combine(RepoPaths.Root, "rtl", "src", "System.pas")).Select(p => (Path.GetFileName(p), File.ReadAllText(p)))];

    private static InMemoryFileSystem Fs(string program = Source)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\work\P.pas", program);
        foreach (var (name, text) in RtlSystem)
        {
            fs.Add(Path.Combine(@"C:\rtl", name), text);
        }

        return fs;
    }

    private static (int ExitCode, string[] Out, string Err) Run(IFileSystem fs, params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var code = OpcCommand.Run(args, new OpcEnvironment(Cwd, stdout, stderr, fs), TestContext.Current.CancellationToken);
        var lines = stdout.ToString().Split('\n');
        return (code, lines[^1].Length == 0 ? lines[..^1] : lines, stderr.ToString());
    }

    private static OpcArguments Parse(params string[] args) => OpcCommand.Parse(args, Cwd);

    // ------------------------------------------------------------------ --version / --help (CMP-852, CMP-241)

    [Fact]
    [Trait("Req", "CMP-241")]
    public void Version_PrintsCompilerAbiAndLlvmPin()
    {
        var (code, output, _) = Run(new InMemoryFileSystem(), "--version");

        Assert.Equal(0, code);
        Assert.Matches(@"^opc 0\.1\.0 \(abi 1, llvm \d+\.\d+\.\d+ pin\)$", Assert.Single(output));
        Assert.Equal("0.1.0", CompilerInfo.Version);
        Assert.Equal(1, AbiInfo.Version);
    }

    [Fact]
    [Trait("Req", "CMP-852")]
    public void Help_PrintsTheOptionTable_AndExitsZero()
    {
        var (code, output, _) = Run(new InMemoryFileSystem(), "--help");

        Assert.Equal(0, code);
        Assert.Equal("opc <main.pas> [options]", output[0]);
        Assert.Equal("  --version | --help", output[^1]);
    }

    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "CMP-852")]
    public void Help_NamesEveryOptionOfTheInterface_InItsOrder()
    {
        var cli = File.ReadAllText(Path.Combine(RepoPaths.RequireDocs(), "interfaces", "compiler-cli.md")).ReplaceLineEndings("\n");
        var table = cli[(cli.IndexOf("opc <main.pas> [options]", StringComparison.Ordinal))..];
        table = table[..table.IndexOf("```", StringComparison.Ordinal)];
        var specOptions = OptionPattern().Matches(table).Select(m => m.Value).ToList();
        var helpOptions = OptionPattern().Matches(OpcCommand.HelpText).Select(m => m.Value).ToList();

        Assert.NotEmpty(specOptions);
        Assert.Equal(specOptions, helpOptions);
    }

    [GeneratedRegex(@"(?<=^\s*|\| )--?[A-Za-z][-A-Za-z0-9]*", RegexOptions.Multiline)]
    private static partial Regex OptionPattern();

    // ------------------------------------------------------------------ argument errors (E0005…E0008, exit 1)

    [Theory]
    [Trait("Req", "CMP-850")]
    [InlineData(new string[0], "(0,0): error E0007: No input file")]
    [InlineData(new[] { "a.pas", "b.pas" }, @"(0,0): error E0008: More than one input file ('C:\work\a.pas' and 'C:\work\b.pas')")]
    [InlineData(new[] { "P.pas", "--frobnicate" }, "(0,0): error E0005: Invalid command-line option '--frobnicate'")]
    [InlineData(new[] { "P.pas", "-O3" }, "(0,0): error E0005: Invalid command-line option '-O3'")]
    [InlineData(new[] { "P.pas", "--emit", "asm" }, "(0,0): error E0005: Invalid command-line option '--emit asm'")]
    [InlineData(new[] { "P.pas", "-o" }, "(0,0): error E0006: Missing argument for option '-o'")]
    [InlineData(new[] { "P.pas", "--diagnostics", "xml" }, "(0,0): error E0005: Invalid command-line option '--diagnostics xml'")]
    public void ArgumentErrors_AreReportedOnStdout_WithExitCode1(string[] args, string expectedSuffix)
    {
        var (code, output, err) = Run(Fs(), args);

        Assert.Equal(1, code);
        var line = Assert.Single(output);
        Assert.EndsWith(expectedSuffix, line, StringComparison.Ordinal);
        Assert.Empty(err);
    }

    // ------------------------------------------------------------------ --jobs / -j (CMP-1130, C-272)

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1130")]
    public void JobsOption_ParsedAndValidated()
    {
        Assert.Equal(0, Parse("P.pas").Compilation!.MaxParallelism);                       // automatic
        Assert.Equal(4, Parse("P.pas", "--jobs", "4").Compilation!.MaxParallelism);
        Assert.Equal(4, Parse("P.pas", "-j", "4").Compilation!.MaxParallelism);
        Assert.Equal(0, Parse("P.pas", "--jobs", "0").Compilation!.MaxParallelism);
        Assert.Equal(256, Parse("P.pas", "--jobs", "256").Compilation!.MaxParallelism);

        foreach (var (option, value) in new[] { ("--jobs", "-1"), ("--jobs", "257"), ("--jobs", "x"), ("-j", "1.5"), ("-j", "+4"), ("--jobs", " 4") })
        {
            var (code, output, _) = Run(Fs(), "P.pas", option, value);
            Assert.Equal(1, code);
            Assert.EndsWith($"(0,0): error E0005: Invalid command-line option '{option} {value}'", Assert.Single(output), StringComparison.Ordinal);
        }

        foreach (var option in new[] { "--jobs", "-j" })
        {
            var (code, output, _) = Run(Fs(), "P.pas", option);
            Assert.Equal(1, code);
            Assert.EndsWith($"(0,0): error E0006: Missing argument for option '{option}'", Assert.Single(output), StringComparison.Ordinal);
        }

        Assert.Equal(1, Run(Fs(), "P.pas", "--jobs=4").ExitCode);                           // only the separated forms
        Assert.Equal(1, Run(Fs(), "P.pas", "-j4").ExitCode);

        var help = Run(new InMemoryFileSystem(), "--help").Out;
        var toolchain = Array.FindIndex(help, l => l.StartsWith("  --toolchain <dir>", StringComparison.Ordinal));
        Assert.Equal("  --jobs <N> | -j <N>   maximum number of parallel compile jobs (0 = automatic: one per processor; default 0)", help[toolchain + 1]);
    }

    // ------------------------------------------------------------------ option mapping (CMP-850)

    [Fact]
    [Trait("Req", "CMP-850")]
    public void Defaults_FollowTheInterface()
    {
        var a = Parse("src\\P.pas");

        Assert.Empty(a.Errors);
        var o = a.Compilation!;
        Assert.Equal(@"C:\work\src\P.pas", o.MainFile);
        Assert.Equal(@"C:\work\obj\Debug", o.ObjectDirectory);
        Assert.Equal(OptimizationLevel.O0, o.Optimization);
        Assert.Equal(AppType.Console, o.AppType);
        Assert.Equal("", o.RtlDirectory);
        Assert.Equal("", o.ToolchainDirectory);
        Assert.Equal("broad", o.Rtti);
        Assert.Equal("none", o.Manifest);
        Assert.Empty(o.UnitScopeNames);
        Assert.Equal(EmitStage.Exe, a.Emit!.Stage);
        Assert.Equal(@"C:\work\P.exe", a.Emit.OutputPath);
        Assert.False(a.Json);
        Assert.False(a.Verbose);
    }

    [Fact]
    [Trait("Req", "CMP-850")]
    public void EveryOption_MapsToTheCompilationAndEmitOptions()
    {
        var a = Parse("P.pas", "-o", @"out\app.exe", "-U", "lib", "-U", @"D:\units", "--obj-dir", "tmp", "-D", "FOO", "-D", "Bar",
            "-O2", "-g", "--app-type", "gui", "--manifest", "app.manifest", "--rtti", "published", "--unit-scope", "Winapi",
            "--unit-scope", "System", "--emit", "obj", "--diagnostics", "json", "--rtl", @"C:\rtl", "--toolchain", "llvm", "--verbose");

        Assert.Empty(a.Errors);
        var o = a.Compilation!;
        Assert.Equal([@"C:\work\lib", @"D:\units"], o.SearchPaths);
        Assert.Equal(@"C:\work\tmp", o.ObjectDirectory);
        Assert.Equal(["FOO", "Bar"], o.Defines);
        Assert.Equal(OptimizationLevel.O2, o.Optimization);
        Assert.True(o.DebugInfo);
        Assert.Equal(AppType.Gui, o.AppType);
        Assert.Equal(@"C:\work\app.manifest", o.Manifest);
        Assert.Equal("published", o.Rtti);
        Assert.Equal(["Winapi", "System"], o.UnitScopeNames);
        Assert.Equal(@"C:\rtl", o.RtlDirectory);
        Assert.Equal(@"C:\work\llvm", o.ToolchainDirectory);
        Assert.Equal(EmitStage.Obj, a.Emit!.Stage);
        Assert.Equal(@"C:\work\out\app.exe", a.Emit.OutputPath);
        Assert.True(a.Json);
        Assert.True(a.Verbose);
    }

    [Theory]
    [Trait("Req", "CMP-850")]
    [InlineData("-O1", OptimizationLevel.O1, @"C:\work\obj\Release")]
    [InlineData("-O2", OptimizationLevel.O2, @"C:\work\obj\Release")]
    [InlineData("-O0", OptimizationLevel.O0, @"C:\work\obj\Debug")]
    public void Optimisation_SelectsTheDefaultObjectDirectory(string option, OptimizationLevel level, string objDir)
    {
        var o = Parse("P.pas", option).Compilation!;

        Assert.Equal(level, o.Optimization);
        Assert.Equal(objDir, o.ObjectDirectory);
    }

    [Theory]
    [Trait("Req", "CMP-850")]
    [InlineData("gui", "default")]
    [InlineData("console", "none")]
    public void Manifest_DefaultsByAppType(string appType, string manifest) =>
        Assert.Equal(manifest, Parse("P.pas", "--app-type", appType).Compilation!.Manifest);

    [Fact]
    [Trait("Req", "CMP-850")]
    public void Check_SelectsTheCheckStage_AndEmitLlTheLlStage()
    {
        Assert.Equal(EmitStage.Check, Parse("P.pas", "--check").Emit!.Stage);
        Assert.Equal(EmitStage.Ll, Parse("P.pas", "--emit", "ll").Emit!.Stage);
        Assert.Equal(EmitStage.Exe, Parse("P.pas", "--emit", "exe").Emit!.Stage);
    }

    // ------------------------------------------------------------------ diagnostics and exit codes (compiler-cli.md §2)

    [Fact]
    [Trait("Req", "CMP-851")]
    public void CompileErrors_TextForm_OnStdout_ExitCode1_NoFiles()
    {
        var fs = Fs("program P; begin Foo := 1 end.");

        var (code, output, err) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--emit", "ll");

        Assert.Equal(1, code);
        Assert.Equal([@"C:\work\P.pas(1,18): error E2003: Undeclared identifier 'Foo'"], output);
        Assert.Empty(err);
        Assert.False(fs.FileExists(@"C:\work\obj\Debug\P.ll"));
    }

    [Fact]
    [Trait("Req", "CMP-851")]
    public void CompileErrors_JsonForm_EndWithTheSummaryLine()
    {
        var (code, output, _) = Run(Fs("program P; begin Foo := 1 end."), "P.pas", "--rtl", @"C:\rtl", "--diagnostics", "json", "--check");

        Assert.Equal(1, code);
        Assert.Equal(2, output.Length);
        Assert.StartsWith("{\"file\":\"C:\\\\work\\\\P.pas\",\"line\":1,\"col\":18,", output[0], StringComparison.Ordinal);
        Assert.Contains("\"code\":\"E2003\"", output[0], StringComparison.Ordinal);
        Assert.Matches("""^\{"summary":\{"errors":1,"warnings":0,"hints":0,"output":null,"ms":\d+\}\}$""", output[1]);
    }

    [Fact]
    [Trait("Req", "CMP-851")]
    public void ArgumentErrors_JsonForm_AlsoEndWithTheSummaryLine()
    {
        var (code, output, _) = Run(Fs(), "--diagnostics", "json");

        Assert.Equal(1, code);
        Assert.Contains("\"code\":\"E0007\"", output[0], StringComparison.Ordinal);
        Assert.Matches("""^\{"summary":\{"errors":1,"warnings":0,"hints":0,"output":null,"ms":\d+\}\}$""", output[1]);
    }

    [Fact]
    [Trait("Req", "CMP-850")]
    public void Check_WritesNothing_AndExitsZero()
    {
        var fs = Fs();

        var (code, output, _) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--check");

        Assert.Equal(0, code);
        Assert.Empty(output);
        Assert.False(fs.FileExists(@"C:\work\obj\Debug\P.ll"));
    }

    [Fact]
    [Trait("Req", "CMP-850")]
    public void EmitLl_WritesIntoTheDefaultObjectDirectoryOfTheCurrentDirectory()
    {
        var fs = Fs();

        var (code, _, _) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--emit", "ll", "-O2");

        Assert.Equal(0, code);
        Assert.True(fs.FileExists(@"C:\work\obj\Release\P.ll"));
        Assert.True(fs.FileExists(@"C:\work\obj\Release\System.ll"));
    }

    [Fact]
    [Trait("Req", "CMP-850")]
    public void RtlWithoutSystem_IsE0009()
    {
        var (code, output, _) = Run(Fs(), "P.pas", "--rtl", @"C:\empty", "--check");

        Assert.Equal(1, code);
        Assert.Equal([@"C:\work\P.pas(0,0): error E0009: RTL not found: 'C:\empty' does not contain System.pas"], output);
    }

    [Fact]
    [Trait("Req", "CMP-850")]
    public void MissingToolchain_ExitsWithCode2()
    {
        var (code, output, _) = Run(Fs(), "P.pas", "--rtl", @"C:\rtl", "--toolchain", @"C:\nollvm", "--emit", "obj");

        Assert.Equal(2, code);
        Assert.Contains(" error E4001: ", Assert.Single(output), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-851")]
    public void InternalError_IsE0001_OnStderr_WithExitCode2()
    {
        var (code, output, err) = Run(new ThrowingFileSystem(Fs()), "P.pas", "--rtl", @"C:\rtl", "--check");

        Assert.Equal(2, code);
        Assert.Empty(output);
        Assert.StartsWith(@"C:\work\P.pas(0,0): error E0001: Internal compiler error: boom", err, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", err, StringComparison.Ordinal);

        var (_, _, verbose) = Run(new ThrowingFileSystem(Fs()), "P.pas", "--rtl", @"C:\rtl", "--check", "--verbose");
        Assert.Contains(" at ", verbose, StringComparison.Ordinal);                  // stack trace with --verbose
    }

    // ------------------------------------------------------------------ real toolchain

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-850")]
    public async Task HelloWorld_DefaultOutputInTheCurrentDirectory_RunsAndVerboseEchoesCommands()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        File.WriteAllText(Path.Combine(dir, "src", "Hello.pas"), "program Hello; begin Writeln('Hello, world!') end.");
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter();
        var ct = TestContext.Current.CancellationToken;

        var code = OpcCommand.Run(
            [@"src\Hello.pas", "--rtl", Path.Combine(RepoPaths.Root, "rtl", "src"), "--toolchain", bin, "--verbose"],
            new OpcEnvironment(dir, stdout, stderr, PhysicalFileSystem.Instance), ct);

        Assert.True(code == 0, stdout + stderr.ToString());
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains("lld-link", StringComparison.OrdinalIgnoreCase));
        Assert.True(Directory.Exists(Path.Combine(dir, "obj", "Debug")));
        var exe = Path.Combine(dir, "Hello.exe");
        Assert.True(File.Exists(exe));
        var output = new List<string>();
        var exit = await NativeRun.RunAsync(exe, dir, output.Add, _ => { });                  // C-258: CPU limit, wall cap
        TempDirectories.Delete(dir);
        Assert.Equal(0, exit);
        Assert.Equal(["Hello, world!"], output);
    }

    /// <summary>
    /// C-273 (CMP-1139): with <c>--verbose --diagnostics json</c> the command line of every back-end job names its unit or DLL,
    /// in job-key order (the units in initialisation order, the program last, then the import libraries by DLL name); the link's
    /// command line names neither. Text mode stays the bare command after <c>&gt; </c>, the same lines for every job limit.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1139")]
    public void Verbose_Json_JobCommandLinesNameTheirUnitOrDll()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), "program Hello; uses Greet; begin Writeln(Text) end.");
            File.WriteAllText(Path.Combine(dir, "Greet.pas"), "unit Greet; interface const Text = 'hi'; implementation end.");
            string[] Opc(params string[] extra)
            {
                var stdout = new StringWriter { NewLine = "\n" };
                var stderr = new StringWriter();
                var code = OpcCommand.Run(
                    ["Hello.pas", "--rtl", Path.Combine(RepoPaths.Root, "rtl", "src"), "--toolchain", bin, "--verbose", .. extra],
                    new OpcEnvironment(dir, stdout, stderr, PhysicalFileSystem.Instance), TestContext.Current.CancellationToken);
                Assert.True(code == 0, stdout + stderr.ToString());
                TempDirectories.Delete(Path.Combine(dir, "obj"));                       // the next run compiles everything again
                return stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }

            var json = Opc("--diagnostics", "json", "--jobs", "4").Where(l => l.StartsWith("{\"event\":\"commandLine\"", StringComparison.Ordinal)).ToList();
            var serial = Opc("--jobs", "1");
            var parallel = Opc("--jobs", "8");

            static string Tail(string line) => line[line.LastIndexOf(",\"", StringComparison.Ordinal)..];
            Assert.Equal(
                [",\"unit\":\"System\"}", ",\"unit\":\"Greet\"}", ",\"unit\":\"Hello\"}", ",\"dll\":\"kernel32.dll\"}", ",\"dll\":\"ntdll.dll\"}"],
                json.Take(5).Select(Tail));
            Assert.Equal(6, json.Count);
            Assert.Contains("lld-link", json[5], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"unit\"", json[5], StringComparison.Ordinal);
            Assert.DoesNotContain("\"dll\"", json[5], StringComparison.Ordinal);
            Assert.Equal(6, serial.Count(l => l.StartsWith("> ", StringComparison.Ordinal)));
            Assert.Equal(serial, parallel);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    /// <summary>Reading any source throws a non-I/O exception: an internal error.</summary>
    private sealed class ThrowingFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public string ReadAllText(string path) => throw new InvalidOperationException("boom");

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

        public long GetLength(string path) => inner.GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

        public void Delete(string path) => inner.Delete(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);
    }
}

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    /// <summary>The repository's <c>docs</c> folder; skips the calling <c>Category=Spec</c> test when it is absent (TESTING.md).</summary>
    public static string RequireDocs()
    {
        var docs = Path.Combine(Root, "docs");
        if (!Directory.Exists(docs))
        {
            Assert.Skip("docs/ not present (product-only checkout)");
        }

        return docs;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
