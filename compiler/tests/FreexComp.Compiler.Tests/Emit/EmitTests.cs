using System.Text;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Tests.Binding;
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

    /// <summary>
    /// C-213: a construct the emitter does not handle yet (here a Double → Pointer conversion planted into the lowered tree, as no
    /// bound tree produces one) fails with <see cref="EmitNotSupportedException"/> at the source location the Lowerer stamped on the
    /// innermost statement being emitted; <c>Compilation.Emit</c> reports it as E2900 there instead of at (0,0).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-500")]
    public void EmitterErrorHasTheNodeLocation()
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", "unit M;\ninterface\nvar X: Double; Q: Pointer;\nimplementation\nprocedure P;\nbegin\n  X := 1.5;\n  Q := nil;\nend;\nend.\n"));
        var lowered = Lowerer.Lower(BinderTestHelper.BindWithStubSystem(tree));
        var body = lowered.Routines.Single(r => r.Routine!.Name == "P");
        var assignments = Assignments(body.Body).ToList();
        var (x, q) = (assignments[0], assignments[1]);
        Assert.Equal(6, x.Location!.Value.LineSpan.Start.Line);
        var planted = new LAssign(q.Target, new LConvert(x.Target, q.Target.Type)) { SourceSyntax = x.SourceSyntax };
        var broken = lowered with { Routines = [body with { Body = new LBlock([new LBlock([planted]) { SourceSyntax = q.SourceSyntax }]) }] };

        var error = Assert.Throws<EmitNotSupportedException>(() => IrEmitter.EmitUnit(broken, new LlvmTextWriter()));

        Assert.Equal((@"C:\p\M.pas", 6, 2), (error.Location.Path, error.Location.LineSpan.Start.Line, error.Location.LineSpan.Start.Column));
        Assert.Contains("is not emitted yet", error.Message, StringComparison.Ordinal);

        static IEnumerable<LAssign> Assignments(LStatement statement) => statement switch
        {
            LAssign assign => [assign],
            LBlock block => block.Statements.SelectMany(Assignments),
            _ => [],
        };
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
        // CMP-355 with the top-level handler (C-095, LANG-832/833)
        Assert.Contains(
            "define void @_start() #0 personality ptr @__C_specific_handler {\n" +
            "entry:\n" +
            "  call void @\"System._Startup\"(ptr @\"$units\", i64 1)\n" +
            "  invoke void @\"$main\"()\n" +
            "          to label %done unwind label %catch.dispatch\n" +
            "done:\n" +
            "  call void @\"System._Halt0\"() #1\n" +
            "  unreachable\n" +
            "catch.dispatch:\n" +
            "  %cs = catchswitch within none [label %catch] unwind to caller\n" +
            "catch:\n" +
            "  %cp = catchpad within %cs [ptr @\"System._SehFilter\"]\n" +
            "  catchret from %cp to label %unhandled\n" +
            "unhandled:\n" +
            "  %t1 = call ptr @\"System._BeginCatch\"()\n" +
            "  call void @\"System._UnhandledException\"() #1\n" +
            "  unreachable\n" +
            "}\n", text);
        Assert.Contains("declare void @\"System._Halt0\"() #1\n", text);
        Assert.Contains("declare void @\"System._UnhandledException\"() #1\n", text);
        Assert.Contains("declare i32 @__C_specific_handler(...)\n", text);
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

    /// <summary>C-175: a routine defined in another unit (here System's <c>memcmp</c>, <c>_GetMem</c>) gets a <c>declare</c> (CMP-340).</summary>
    [Fact]
    [Trait("Req", "CMP-340")]
    [Trait("Req", "LANG-511")]
    public void CallToSystemRoutine_IsDeclared()
    {
        var ir = EmitProgramIr("Repro", """
            program Repro;
            var A, B: array[0..3] of Byte; P: Pointer;
            begin
              Writeln(memcmp(@A[0], @B[0], 4));
              P := _GetMem(8);
            end.
            """);

        Assert.Contains("call i32 @memcmp(ptr ", ir);
        Assert.Contains("declare i32 @memcmp(ptr, ptr, i64)\n", ir);
        Assert.Contains("declare ptr @\"System._GetMem\"(i64)\n", ir);
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
    [Trait("Req", "CMP-115")]
    public void Check_RunsFlowAnalysisAfterBinding()
    {
        var (compilation, _) = InMemory("P", "program P;\nprocedure Q;\nvar X: Integer;\nbegin\nend;\nbegin\n  Q\nend.\n");

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal([@"C:\p\P.pas(3,5): hint H2001: Variable 'X' is declared but never used"], result.Diagnostics.Select(d => d.ToText()));
    }

    [Theory]
    [Trait("Req", "LANG-500")]
    [InlineData("program Other;\nbegin end.", new[] { @"C:\p\Main.pas(1,9): hint H1002: Program name 'Other' differs from file name 'Main'" })]
    [InlineData("program MAIN;\nbegin end.", new string[0])]
    [InlineData("begin end.", new string[0])]
    public void Check_ProgramNameDiffersFromFileName_IsH1002(string source, string[] expected)
    {
        var (compilation, _) = InMemory("Main", source);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Diagnostics.Select(d => d.ToText()));
    }

    [Fact]
    [Trait("Req", "CMP-115")]
    public void Check_EmptyProgram_ReportsNothing()
    {
        var (compilation, _) = InMemory("EmptyProgram", EmptyProgramSource);

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    [Trait("Req", "LANG-511")]
    public void IfDirective_SeesTheSystemInterface()
    {
        var (compilation, _) = InMemory("P", "program P;\nbegin\n{$IF not Declared(ExitCode)}\n  NotDeclaredAnywhere := 1;\n{$ELSE}\n  ExitCode := 7;\n{$ENDIF}\nend.\n");

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    public void IfDirective_SizeOfASystemVariable_Folds()
    {
        var (compilation, _) = InMemory("P", "program P;\nbegin\n{$IF SizeOf(ExitCode) <> 4}\n  NotDeclaredAnywhere := 1;\n{$ELSE}\n  ExitCode := 7;\n{$ENDIF}\nend.\n");

        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Empty(result.Diagnostics);
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

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-627")]
    public void DefaultOutputPath_IsTheMainFileNameNextToTheMainFile()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var main = PathUtil.Normalize(Path.Combine(dir, "EmptyProgram.pas"));
        PhysicalFileSystem.Instance.WriteAllBytes(main, Encoding.UTF8.GetBytes(EmptyProgramSource));
        var options = new CompilationOptions { MainFile = main, RtlDirectory = MiniRtl, ToolchainDirectory = Path.GetDirectoryName(Clang.Path)! };

        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance).Emit(new EmitOptions(), TestContext.Current.CancellationToken);

        var exists = File.Exists(Path.Combine(dir, "EmptyProgram.exe"));
        TestDirectories.Delete(dir);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(Path.Combine(main[..main.LastIndexOf('\\')], "EmptyProgram.exe"), result.OutputPath);
        Assert.True(exists);
    }

    // ------------------------------------------------------------------ incremental builds (C-075, CMP-150…155)

    /// <summary>P uses U and W; U's implementation uses V. Prints <c>UValue WValue</c> = <c>VValue + 1</c> and a constant.</summary>
    private sealed class IncrementalProject : IDisposable
    {
        private readonly string _dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));

        public IncrementalProject()
        {
            Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
            Put("P", "program P;\nuses U, W;\nbegin\n  Writeln(UValue, ' ', WValue);\nend.\n");
            Put("U", "unit U;\ninterface\nfunction UValue: Integer;\nimplementation\nuses V;\nfunction UValue: Integer;\nbegin\n  Result := VValue + 1;\nend;\nend.\n");
            Put("V", "unit V;\ninterface\nfunction VValue: Integer;\nimplementation\nfunction VValue: Integer;\nbegin\n  Result := 10;\nend;\nend.\n");
            Put("W", "unit W;\ninterface\nconst WValue = 5;\nimplementation\nend.\n");
        }

        public string Exe => Path.Combine(_dir, "P.exe");

        public List<string> Commands { get; } = [];

        public void Put(string unit, string text) => PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(_dir, unit + ".pas"), Encoding.UTF8.GetBytes(text));

        public void Edit(string unit, string from, string to)
        {
            var path = Path.Combine(_dir, unit + ".pas");
            var text = File.ReadAllText(path);
            Assert.Contains(from, text, StringComparison.Ordinal);
            Put(unit, text.Replace(from, to, StringComparison.Ordinal));
        }

        public EmitResult Build()
        {
            Commands.Clear();
            var options = new CompilationOptions
            {
                MainFile = Path.Combine(_dir, "P.pas"),
                RtlDirectory = MiniRtl,
                ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
                ObjectDirectory = Path.Combine(_dir, "obj"),
            };
            var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
                .Emit(new EmitOptions { OutputPath = Exe, CommandLineSink = Commands.Add }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            Assert.Equal(Exe, result.OutputPath);
            return result;
        }

        public bool Linked => Commands.Any(c => c.Contains("lld-link", StringComparison.OrdinalIgnoreCase));

        public async Task<string> RunAsync()
        {
            var stdout = new List<string>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            Assert.Equal(0, await new ProcessRunner().RunAsync(Exe, [], _dir, stdout.Add, _ => { }, timeout.Token));
            return string.Join("\n", stdout);
        }

        public void Dispose()
        {
            try
            {
                TestDirectories.Delete(_dir);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// CMP-153, CMP-155: a second build with nothing changed starts no process and keeps the exe; an implementation edit recompiles
    /// only that unit and relinks; a deleted exe is relinked without recompiling anything.
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-150")]
    [Trait("Req", "CMP-153")]
    [Trait("Req", "CMP-155")]
    public async Task IncrementalBodyChange()
    {
        using var project = new IncrementalProject();
        var first = project.Build();
        Assert.Equal(["P", "System", "V", "U", "W"], first.CompiledUnits);
        Assert.Empty(first.UpToDateUnits);
        Assert.Equal("11 5", await project.RunAsync());
        var linkedAt = File.GetLastWriteTimeUtc(project.Exe);

        var unchanged = project.Build();
        Assert.Empty(unchanged.CompiledUnits);
        Assert.Equal(["P", "System", "V", "U", "W"], unchanged.UpToDateUnits);
        Assert.Empty(project.Commands);
        Assert.Equal(linkedAt, File.GetLastWriteTimeUtc(project.Exe));

        project.Edit("U", "Result := VValue + 1;", "Result := VValue + 22;");
        var body = project.Build();
        Assert.Equal(["U"], body.CompiledUnits);
        Assert.Equal(["P", "System", "V", "W"], body.UpToDateUnits);
        Assert.True(project.Linked);
        Assert.Equal("32 5", await project.RunAsync());

        File.Delete(project.Exe);
        var relink = project.Build();
        Assert.Empty(relink.CompiledUnits);
        Assert.True(project.Linked);
        Assert.Equal("32 5", await project.RunAsync());
    }

    /// <summary>
    /// build-engine.md §5: an interface edit recompiles the unit and every unit that recorded its old interface hash, transitively
    /// only while their own interface hash changes — V's interface reaches U (implementation uses) but not P, whose recorded hash of
    /// U is unchanged; W's interface reaches P.
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-150")]
    [Trait("Req", "CMP-151")]
    [Trait("Req", "CMP-152")]
    public async Task IncrementalInterfaceChange()
    {
        using var project = new IncrementalProject();
        project.Build();

        project.Edit("V", "function VValue: Integer;\nimplementation", "function VValue: Integer;\nconst VExtra = 1;\nimplementation");
        var throughImplementationUses = project.Build();
        Assert.Equal(["V", "U"], throughImplementationUses.CompiledUnits);
        Assert.Equal(["P", "System", "W"], throughImplementationUses.UpToDateUnits);
        Assert.Equal("11 5", await project.RunAsync());

        project.Edit("W", "WValue = 5;", "WValue = 66;");
        var throughInterfaceUses = project.Build();
        Assert.Equal(["P", "W"], throughInterfaceUses.CompiledUnits);
        Assert.Equal("11 66", await project.RunAsync());
    }

    internal static async Task<(int ExitCode, List<string> StdOut, List<string> StdErr)> BuildAndRun(
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
        TestDirectories.Delete(dir);
        return (exitCode, stdout, stderr);
    }
}
