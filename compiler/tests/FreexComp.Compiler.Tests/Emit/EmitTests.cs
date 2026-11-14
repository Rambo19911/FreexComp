using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-044: <see cref="Compilation.Emit"/> for a program and the implicit <c>System</c> (LANG-511, CMP-350, 355 without the
/// EH handler): bind, emit <c>.ll</c>, and (<c>Category=Toolchain</c>) compile, link and run the golden programs
/// <c>m2.1/HelloWorld</c> and <c>m2.1/EmptyProgram</c> (testing.md §2, sources inline, identical to the golden files).
/// </summary>
[Trait("Phase", "2")]
public sealed class EmitTests
{
    internal const string HelloWorldSource = """
        // covers: LANG-500, LANG-511, LANG-910, LANG-912
        program HelloWorld;
        begin
          Writeln('Hello, world!');
        end.

        """;

    internal const string EmptyProgramSource = """
        // covers: LANG-500, LANG-511
        program EmptyProgram;
        begin
        end.

        """;

    private const string Dir = @"C:\p";

    private static string MiniRtl => MiniRtlCompilesTests.MiniRtlDirectory;

    private static (Compilation Compilation, InMemoryFileSystem Fs) InMemory(string name, string source)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, name + ".pas"), source)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtl, "System.pas")));
        var options = new CompilationOptions { MainFile = Path.Combine(Dir, name + ".pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return (Compilation.Create(options, [], fs), fs);
    }

    private static string EmitProgramIr(string name, string source)
    {
        var (compilation, fs) = InMemory(name, source);
        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Null(result.OutputPath);
        Assert.Equal([name, "System"], result.CompiledUnits);
        Assert.True(fs.FileExists(@"C:\p\obj\System.ll"));
        return fs.ReadAllText(Path.Combine(@"C:\p\obj", name + ".ll"));
    }

    [Fact]
    [Trait("Kind", "Snapshot")]
    [Trait("Req", "CMP-355")]
    public void Start() => Snapshot.Verify("Start", EmitProgramIr("HelloWorld", HelloWorldSource));

    [Fact]
    [Trait("Req", "CMP-355")]
    [Trait("Req", "LANG-511")]
    public void ProgramModule_HasUnitTableMainAndStart_WithNullSystemEntry()
    {
        var text = EmitProgramIr("EmptyProgram", EmptyProgramSource);

        Assert.Contains("@\"$units\" = internal constant [1 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }], align 8\n", text);
        Assert.Contains(
            "define internal void @\"$main\"() #0 {\n" +
            "entry:\n" +
            "  call void @\"System._InitUnits\"()\n" +
            "  br label %exit\n" +
            "exit:\n" +
            "  ret void\n" +
            "}\n", text);
        Assert.Contains(
            "define void @_start() #0 {\n" +
            "entry:\n" +
            "  call void @\"System._Startup\"(ptr @\"$units\", i64 1)\n" +
            "  call void @\"$main\"()\n" +
            "  call void @\"System._Halt0\"() #1\n" +
            "  unreachable\n" +
            "}\n", text);
        Assert.Contains("declare void @\"System._Halt0\"() #1\n", text);
        Assert.DoesNotContain("$init", text);
    }

    [Fact]
    [Trait("Req", "CMP-410")]
    [Trait("Req", "LANG-511")]
    public void Writeln_UsesTheBoundSystemOutput()
    {
        var text = EmitProgramIr("HelloWorld", HelloWorldSource);

        Assert.Contains("@\"System.Output\" = external global [624 x i8], align 8\n", text);
        Assert.Contains("call void @\"System._WriteLn\"(ptr @\"System.Output\")\n", text);
    }

    [Fact]
    [Trait("Req", "LANG-511")]
    public void ProgramSeesSystemInterfaceSymbols_AndItsOwnDeclarationsHideThem()
    {
        var ir = EmitProgramIr("P", "program P; var ExitCode: Integer; begin ExitCode := 1 end.");
        Assert.Contains("store i32 1, ptr @\"P.ExitCode\"", ir);

        var system = EmitProgramIr("Q", "program Q; begin ExitCode := 2 end.");
        Assert.Contains("store i32 2, ptr @\"System.ExitCode\"", system);
        Assert.Contains("@\"System.ExitCode\" = external global i32, align 4\n", system);
    }

    [Fact]
    [Trait("Req", "CMP-350")]
    public void SystemModule_DefinesInitAndFinal()
    {
        var (compilation, fs) = InMemory("EmptyProgram", EmptyProgramSource);
        Assert.True(compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken).Success);
        var text = fs.ReadAllText(@"C:\p\obj\System.ll");

        Assert.Contains("define void @\"System.$init\"() #0 {\nentry:\n  br label %exit\nexit:\n  ret void\n}\n", text);
        Assert.Contains("define void @\"System.$final\"() #0 {\nentry:\n  br label %exit\nexit:\n  ret void\n}\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-202")]
    public void Check_WithErrors_FailsWithoutWritingFiles()
    {
        var (compilation, fs) = InMemory("Bad", "program Bad; begin Foo := 1 end.");

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("E2003", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(result.CompiledUnits);
        Assert.False(fs.FileExists(@"C:\p\obj\Bad.ll"));
    }

    [Fact]
    [Trait("Req", "CMP-200")]
    public void Check_Stage_BindsWithoutWritingFiles()
    {
        var (compilation, fs) = InMemory("HelloWorld", HelloWorldSource);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.False(fs.FileExists(@"C:\p\obj\HelloWorld.ll"));
    }

    [Fact]
    [Trait("Req", "LANG-510")]
    public void MissingSystem_IsE2202()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", "begin end.");
        var compilation = Compilation.Create(new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl" }, [], fs);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(@"C:\p\P.pas(0,0): error E2202: Unit 'System' not found", diagnostic.ToText());
    }

    [Fact]
    [Trait("Req", "CMP-200")]
    public void OpenFiles_WinOverTheFileSystem()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", "program P; begin Foo end.")
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtl, "System.pas")));
        var compilation = Compilation.Create(new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl" },
            [SourceText.From(@"C:\p\P.pas", "program P; begin end.")], fs);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.True(fs.FileExists(@"C:\p\obj\Debug\P.ll"));
    }

    // ------------------------------------------------------------------ real toolchain

    public static TheoryData<string, OptimizationLevel> Programs() => new()
    {
        { "HelloWorld", OptimizationLevel.O0 },
        { "HelloWorld", OptimizationLevel.O2 },
        { "EmptyProgram", OptimizationLevel.O0 },
        { "EmptyProgram", OptimizationLevel.O2 },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-355")]
    [Trait("Req", "CMP-620")]
    public async Task GoldenProgram_CompilesLinksAndRuns(string name, OptimizationLevel optimization)
    {
        var (source, expected) = name == "HelloWorld" ? (HelloWorldSource, new[] { "Hello, world!" }) : (EmptyProgramSource, []);

        var (exitCode, stdout, stderr) = await BuildAndRun(name, source, optimization);

        Assert.Equal(expected, stdout);
        Assert.Empty(stderr);
        Assert.Equal(0, exitCode);
    }

    private static async Task<(int ExitCode, List<string> StdOut, List<string> StdErr)> BuildAndRun(
        string name, string source, OptimizationLevel optimization)
    {
        var toolchain = Path.GetDirectoryName(Clang.Path)!;
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var fs = PhysicalFileSystem.Instance;
        var main = Path.Combine(dir, name + ".pas");
        fs.WriteAllBytes(main, Encoding.UTF8.GetBytes(source));
        var exe = Path.Combine(dir, "out", name + ".exe");
        var options = new CompilationOptions
        {
            MainFile = PathUtil.Normalize(main),
            RtlDirectory = MiniRtl,
            ToolchainDirectory = toolchain,
            ObjectDirectory = Path.Combine(dir, "obj"),
            Optimization = optimization,
        };
        var ct = TestContext.Current.CancellationToken;
        var commands = new List<string>();

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { OutputPath = exe, CommandLineSink = commands.Add }, ct);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText() + " " + string.Join(" | ", d.Related.Select(r => r.Message)))));
        Assert.Equal(PathUtil.Normalize(exe), result.OutputPath);
        Assert.Contains(commands, c => c.Contains("lld-link", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(dir, "obj", "imports", "kernel32.lib")));
        var stdout = new List<string>();
        var stderr = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var exitCode = await new ProcessRunner().RunAsync(exe, [], dir, stdout.Add, stderr.Add, timeout.Token);
        Directory.Delete(dir, recursive: true);
        return (exitCode, stdout, stderr);
    }
}
