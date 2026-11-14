using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-043: the mini RTL <c>compiler/tests/minirtl/System.pas</c> (CMP-630, 631) binds without diagnostics, emits
/// the System-only parts of the module (CMP-610, 611, <c>System.Output</c>) and assembles with the pinned clang.
/// </summary>
[Trait("Phase", "2")]
public sealed class MiniRtlCompilesTests
{
    internal static string MiniRtlDirectory => Path.Combine(RepoPaths.Root, "compiler", "tests", "minirtl");

    internal static string EmitSystem()
    {
        var path = PathUtil.Normalize(Path.Combine(MiniRtlDirectory, "System.pas"));
        var tree = SyntaxTree.Parse(SourceText.From(path, File.ReadAllText(path)));
        Assert.Empty(Format(tree.Diagnostics));
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }

    [Fact]
    [Trait("Req", "CMP-611")]
    public void System_GetsTheStackProbeModuleAsmVerbatim()
    {
        var text = EmitSystem();

        Assert.Contains(
            """
            module asm "\09.text"
            module asm "\09.globl\09__chkstk"
            module asm "\09.p2align\094"
            module asm "__chkstk:"
            module asm "\09pushq\09%rcx"
            module asm "\09pushq\09%rax"
            module asm "\09cmpq\09$4096, %rax"
            module asm "\09leaq\0924(%rsp), %rcx"
            module asm "\09jb\091f"
            module asm "2:"
            module asm "\09subq\09$4096, %rcx"
            module asm "\09testq\09%rcx, (%rcx)"
            module asm "\09subq\09$4096, %rax"
            module asm "\09cmpq\09$4096, %rax"
            module asm "\09ja\092b"
            module asm "1:"
            module asm "\09subq\09%rax, %rcx"
            module asm "\09testq\09%rcx, (%rcx)"
            module asm "\09popq\09%rax"
            module asm "\09popq\09%rcx"
            module asm "\09retq"

            """.ReplaceLineEndings("\n"),
            text);
        Assert.DoesNotContain("module asm", IrSnapshotTests.Emit(IrSnapshotTests.AddSource));
    }

    [Fact]
    [Trait("Req", "CMP-610")]
    public void MemRoutines_HaveNoBuiltins_AndNoMemIntrinsics()
    {
        var text = EmitSystem();

        foreach (var name in new[] { "memcpy", "memmove", "memset", "memcmp" })
        {
            Assert.Matches($@"\ndefine (i32|ptr) @{name}\([^)]*\) #2 {{", text);
        }

        Assert.Contains("attributes #2 = { uwtable \"no-builtins\" }\n", text);
        Assert.DoesNotContain("@llvm.mem", text);
        Assert.DoesNotContain(" #2 ", IrSnapshotTests.Emit(IrSnapshotTests.AddSource));
    }

    [Fact]
    [Trait("Req", "CMP-630")]
    [Trait("Req", "CMP-410")]
    public void System_DefinesOutput_AndCallsItsOwnHelpersWithoutDeclaringThem()
    {
        var text = EmitSystem();

        Assert.Contains("@\"System.Output\" = global [624 x i8] zeroinitializer, align 8\n", text);
        Assert.Contains("@_fltused = global i32 39029, align 4\n", text);
        Assert.DoesNotContain("external global", text);
        Assert.DoesNotContain("declare void @\"System.", text);
        Assert.Contains("call void @\"System._WriteUStr\"(ptr @\"System.ErrOutput\", ", text);
        // C-056: memory is accessed through pointers, no longer through ntdll's RtlMoveMemory/RtlFillMemory
        Assert.DoesNotContain("@RtlMoveMemory", text);
        Assert.DoesNotContain("@RtlFillMemory", text);
        Assert.Contains("declare dllimport ptr @HeapReAlloc(i64, i32, ptr, i64)\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-631")]
    public void MiniRtl_HasTheDefinitionFilesItImports()
    {
        var kernel32 = File.ReadAllLines(Path.Combine(MiniRtlDirectory, "kernel32.def"));
        var ntdll = File.ReadAllLines(Path.Combine(MiniRtlDirectory, "ntdll.def"));

        Assert.Equal(["LIBRARY kernel32.dll", "EXPORTS"], kernel32[..2]);
        Assert.Equal(["LIBRARY ntdll.dll", "EXPORTS"], ntdll[..2]);
        Assert.Contains("__C_specific_handler", ntdll);
        var text = EmitSystem();
        foreach (var import in text.Split('\n').Where(l => l.StartsWith("declare dllimport ", StringComparison.Ordinal)))
        {
            var name = import.Split('@')[1].Split('(')[0];
            Assert.True(kernel32.Contains(name) || ntdll.Contains(name), $"'{name}' is in neither .def file");
        }
    }

    public static TheoryData<string> Optimisations() => ["-O0", "-O2"];

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-630")]
    public void MiniRtl_AssemblesWithPinnedClang(string optimisation)
    {
        var (exitCode, output) = Clang.CompileToObject(EmitSystem(), optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }

    /// <summary>A hand-written <c>_start</c> (C-044 emits the real one) drives the helpers; stdout, stderr and exit code are checked.</summary>
    private const string DriverIr = """
        target triple = "x86_64-pc-windows-msvc"
        @"System.Output" = external global [624 x i8], align 8
        @.str.0 = private unnamed_addr constant { i32, i32, i16, i16, i32, [4 x i16] } { i32 -1, i32 3, i16 1200, i16 2, i32 0, [4 x i16] [i16 120, i16 252, i16 61, i16 0] }, align 16
        @a = private constant [3 x i8] c"abc"
        @b = private constant [3 x i8] c"abd"
        declare void @"System._Startup"(ptr, i64)
        declare void @"System._InitUnits"()
        declare void @"System._WriteUStr"(ptr, ptr, i32)
        declare void @"System._WriteInt"(ptr, i64, i32)
        declare void @"System._WriteUInt"(ptr, i64, i32)
        declare void @"System._WriteBool"(ptr, i8, i32)
        declare void @"System._WriteWChar"(ptr, i16, i32)
        declare void @"System._WriteAChar"(ptr, i8, i32)
        declare void @"System._WriteLn"(ptr)
        declare void @"System._WriteEnd"(ptr)
        declare i32 @memcmp(ptr, ptr, i64)
        declare void @"System._Halt"(i32) #1
        declare void @"System._DivZeroError"() #1
        define void @_start() {
          call void @"System._Startup"(ptr null, i64 0)
          call void @"System._InitUnits"()
          call void @"System._WriteUStr"(ptr @"System.Output", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [4 x i16] }, ptr @.str.0, i32 0, i32 5), i32 5)
          call void @"System._WriteInt"(ptr @"System.Output", i64 -9223372036854775808, i32 0)
          call void @"System._WriteAChar"(ptr @"System.Output", i8 32, i32 0)
          call void @"System._WriteUInt"(ptr @"System.Output", i64 -1, i32 22)
          call void @"System._WriteBool"(ptr @"System.Output", i8 1, i32 5)
          call void @"System._WriteBool"(ptr @"System.Output", i8 0, i32 0)
          call void @"System._WriteWChar"(ptr @"System.Output", i16 8364, i32 2)
          call void @"System._WriteInt"(ptr @"System.Output", i64 0, i32 0)
          call void @"System._WriteLn"(ptr @"System.Output")
          %c = call i32 @memcmp(ptr @a, ptr @b, i64 3)
          %d = call i32 @memcmp(ptr @a, ptr @a, i64 3)
          %e = sext i32 %c to i64
          call void @"System._WriteInt"(ptr @"System.Output", i64 %e, i32 0)
          %f = sext i32 %d to i64
          call void @"System._WriteInt"(ptr @"System.Output", i64 %f, i32 3)
          call void @"System._WriteLn"(ptr @"System.Output")
          call void @"System._WriteEnd"(ptr @"System.Output")
          %z = icmp eq i32 %d, 0
          br i1 %z, label %ok, label %err
        err:
          call void @"System._DivZeroError"() #1
          unreachable
        ok:
          call void @"System._Halt"(i32 3) #1
          unreachable
        }
        attributes #1 = { noreturn }

        """;

    private const string RunErrorIr = """
        target triple = "x86_64-pc-windows-msvc"
        declare void @"System._Startup"(ptr, i64)
        declare void @"System._DivZeroError"() #1
        define void @_start() {
          call void @"System._Startup"(ptr null, i64 0)
          call void @"System._DivZeroError"() #1
          unreachable
        }
        attributes #1 = { noreturn }

        """;

    /// <summary>
    /// C-089: the CMP-391 pattern written by hand around the mini RTL's ABI §9.7 helpers. Scenario 1 raises a TObject and
    /// catches it; scenario 2 re-raises it from an inner handler (ABI-122: kept) to an outer one; scenario 3 raises nil
    /// (ABI-123: error 204, which without ErrorProc ends the program). Each line: object caught = the raised one (1/0), the
    /// live block count inside the handler, the live block count and <c>_CurrentExcept</c> (as 0/1) after <c>_EndCatch</c>.
    /// </summary>
    private const string SehDriverIr = """
        target triple = "x86_64-pc-windows-msvc"
        @"System.Output" = external global [624 x i8], align 8
        @"System.TObject.$vmt" = external global ptr, align 8
        declare i32 @__C_specific_handler(...)
        declare void @"System._Startup"(ptr, i64)
        declare void @"System._WriteInt"(ptr, i64, i32)
        declare void @"System._WriteWChar"(ptr, i16, i32)
        declare void @"System._WriteLn"(ptr)
        declare void @"System._WriteEnd"(ptr)
        declare void @"System._Halt"(i32) #1
        declare i64 @"System._LiveAllocCount"()
        declare ptr @"System.TObject.Create"(ptr, i8)
        declare void @"System._Raise"(ptr, ptr) #1
        declare void @"System._ReRaise"() #1
        declare i32 @"System._SehFilter"(ptr, ptr)
        declare ptr @"System._BeginCatch"()
        declare void @"System._EndCatch"()
        declare ptr @"System._CurrentExcept"()

        define void @int(i64 %v) {
          call void @"System._WriteInt"(ptr @"System.Output", i64 %v, i32 0)
          call void @"System._WriteWChar"(ptr @"System.Output", i16 32, i32 0)
          ret void
        }

        define void @line(ptr %raised, ptr %caught) {
          %same = icmp eq ptr %raised, %caught
          %s = zext i1 %same to i64
          call void @int(i64 %s)
          %n = call i64 @"System._LiveAllocCount"()
          call void @int(i64 %n)
          ret void
        }

        define void @after() {
          %n = call i64 @"System._LiveAllocCount"()
          call void @int(i64 %n)
          %c = call ptr @"System._CurrentExcept"()
          %has = icmp ne ptr %c, null
          %h = zext i1 %has to i64
          call void @"System._WriteInt"(ptr @"System.Output", i64 %h, i32 0)
          call void @"System._WriteLn"(ptr @"System.Output")
          ret void
        }

        define void @catchOne() personality ptr @__C_specific_handler {
        entry:
          %o = call ptr @"System.TObject.Create"(ptr @"System.TObject.$vmt", i8 1)
          invoke void @"System._Raise"(ptr %o, ptr null) #1
                  to label %u unwind label %d
        u:
          unreachable
        d:
          %cs = catchswitch within none [label %p] unwind to caller
        p:
          %cp = catchpad within %cs [ptr @"System._SehFilter"]
          catchret from %cp to label %b
        b:
          %e = call ptr @"System._BeginCatch"()
          call void @line(ptr %o, ptr %e)
          call void @"System._EndCatch"()
          call void @after()
          ret void
        }

        define void @rethrow() personality ptr @__C_specific_handler {
        entry:
          %o = call ptr @"System.TObject.Create"(ptr @"System.TObject.$vmt", i8 1)
          invoke void @"System._Raise"(ptr %o, ptr null) #1
                  to label %u unwind label %d1
        u:
          unreachable
        d1:
          %cs1 = catchswitch within none [label %p1] unwind label %d2
        p1:
          %cp1 = catchpad within %cs1 [ptr @"System._SehFilter"]
          catchret from %cp1 to label %b1
        b1:
          %e1 = call ptr @"System._BeginCatch"()
          invoke void @"System._ReRaise"() #1
                  to label %u unwind label %c1
        c1:
          %cl1 = cleanuppad within none []
          invoke void @"System._EndCatch"() [ "funclet"(token %cl1) ]
                  to label %r1 unwind label %d2
        r1:
          cleanupret from %cl1 unwind label %d2
        d2:
          %cs2 = catchswitch within none [label %p2] unwind to caller
        p2:
          %cp2 = catchpad within %cs2 [ptr @"System._SehFilter"]
          catchret from %cp2 to label %b2
        b2:
          %e2 = call ptr @"System._BeginCatch"()
          call void @line(ptr %o, ptr %e2)
          call void @"System._EndCatch"()
          call void @after()
          ret void
        }

        define void @raiseNil() personality ptr @__C_specific_handler {
        entry:
          invoke void @"System._Raise"(ptr null, ptr null) #1
                  to label %u unwind label %d
        u:
          unreachable
        d:
          %cs = catchswitch within none [label %p] unwind to caller
        p:
          %cp = catchpad within %cs [ptr @"System._SehFilter"]
          catchret from %cp to label %b
        b:
          %e = call ptr @"System._BeginCatch"()
          call void @"System._EndCatch"()
          ret void
        }

        define void @_start() {
          call void @"System._Startup"(ptr null, i64 0)
          call void @catchOne()
          call void @rethrow()
          call void @"System._WriteEnd"(ptr @"System.Output")
          call void @raiseNil()
          call void @"System._Halt"(i32 0) #1
          unreachable
        }
        attributes #1 = { noreturn }

        """;

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "ABI-121")]
    [Trait("Req", "ABI-122")]
    [Trait("Req", "ABI-123")]
    public async Task MiniRtl_SehHelpers_CatchRethrowAndRaiseNil(string optimisation)
    {
        var (exitCode, stdout, stderr) = await BuildAndRun(SehDriverIr, optimisation);

        Assert.Equal(["1 1 0 0", "1 1 0 0"], stdout);
        Assert.Equal(["Runtime error 204 at $0000000000000000"], stderr);
        Assert.Equal(204, exitCode);
    }

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-630")]
    [Trait("Req", "LANG-915")]
    [Trait("Req", "LANG-920")]
    [Trait("Req", "LANG-921")]
    public async Task MiniRtl_LinksAndRunsWithAHandWrittenEntry(string optimisation)
    {
        var (exitCode, stdout, stderr) = await BuildAndRun(DriverIr, optimisation);

        Assert.Equal(["  xü=-9223372036854775808   18446744073709551615 TRUEFALSE €0", "-1  0"], stdout);
        Assert.Empty(stderr);
        Assert.Equal(3, exitCode);
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-630")]
    public async Task MiniRtl_RuntimeError_WritesTheMessageToStderrAndExitsWithTheCode()
    {
        var (exitCode, stdout, stderr) = await BuildAndRun(RunErrorIr, "-O0");

        Assert.Empty(stdout);
        Assert.Equal(["Runtime error 200 at $0000000000000000"], stderr);
        Assert.Equal(200, exitCode);
    }

    private static async Task<(int ExitCode, List<string> StdOut, List<string> StdErr)> BuildAndRun(string mainIr, string optimisation)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var bin = Path.GetDirectoryName(Clang.Path)!;
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var fs = PhysicalFileSystem.Instance;
        fs.WriteAllBytes(Path.Combine(dir, "System.ll"), Encoding.UTF8.GetBytes(EmitSystem()));
        fs.WriteAllBytes(Path.Combine(dir, "Main.ll"), Encoding.UTF8.GetBytes(mainIr));
        var runner = new ToolRunner(new ProcessRunner(), new ToolRunOptions(Location.ForFile(Path.Combine(dir, "Main.pas"))));
        var ct = TestContext.Current.CancellationToken;
        var level = optimisation == "-O2" ? OptimizationLevel.O2 : OptimizationLevel.O0;
        var clang = new ClangInvoker(runner, Clang.Path);
        foreach (var unit in new[] { "Main", "System" })
        {
            var compiled = await clang.CompileAsync(Path.Combine(dir, unit + ".ll"), Path.Combine(dir, unit + ".obj"), level, false, ct);
            Assert.True(compiled.Failure is null, string.Join(Environment.NewLine, compiled.Output));
        }

        var libs = ImmutableArray.CreateBuilder<string>();
        var dllTool = new DllToolInvoker(runner, Path.Combine(bin, "llvm-dlltool.exe"), fs);
        foreach (var dll in new[] { "kernel32.dll", "ntdll.dll" })
        {
            var lib = await dllTool.BuildAsync(dll, Path.Combine(MiniRtlDirectory, Path.ChangeExtension(dll, ".def")), dir, ct);
            Assert.Null(lib.Failure);
            libs.Add(lib.LibraryPath);
        }

        var exe = Path.Combine(dir, "Main.exe");
        var linked = await new LinkInvoker(runner, Path.Combine(bin, "lld-link.exe"), fs)
            .LinkAsync(new LinkRequest(exe, dir, [Path.Combine(dir, "Main.obj"), Path.Combine(dir, "System.obj")]) { ImportLibraries = libs.ToImmutable() }, ct);
        Assert.True(linked.Success, string.Join(Environment.NewLine, linked.Diagnostics.Select(d => d.Message + " " + string.Join(" | ", d.Related.Select(r => r.Message)))));

        var stdout = new List<string>();
        var stderr = new List<string>();
        var exitCode = await new ProcessRunner().RunAsync(exe, [], dir, stdout.Add, stderr.Add, ct);
        Directory.Delete(dir, recursive: true);
        return (exitCode, stdout, stderr);
    }
}
