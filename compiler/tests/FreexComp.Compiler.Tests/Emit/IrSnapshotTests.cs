using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// Source → bind → lower → <see cref="IrEmitter"/> → <see cref="LlvmTextWriter"/>, compared with the verified <c>.ll</c>
/// files under <c>Snapshots/</c> (testing.md §3; regenerate deliberately with <c>UPDATE_SNAPSHOTS=1</c>).
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Snapshot")]
public sealed class IrSnapshotTests
{
    /// <summary>CMP-310 verbatim (the example of codegen.md §3).</summary>
    private const string Cmp310Add =
        "define i32 @\"M.Add\"(i32 %a, i32 %b) #0 {\n" +
        "entry:\n" +
        "  %a.addr = alloca i32, align 4\n" +
        "  %b.addr = alloca i32, align 4\n" +
        "  %Result.addr = alloca i32, align 4\n" +
        "  store i32 %a, ptr %a.addr, align 4\n" +
        "  store i32 %b, ptr %b.addr, align 4\n" +
        "  %t1 = load i32, ptr %a.addr, align 4\n" +
        "  %t2 = load i32, ptr %b.addr, align 4\n" +
        "  %t3 = add i32 %t1, %t2\n" +
        "  store i32 %t3, ptr %Result.addr, align 4\n" +
        "  br label %exit\n" +
        "exit:\n" +
        "  %t4 = load i32, ptr %Result.addr, align 4\n" +
        "  ret i32 %t4\n" +
        "}\n";

    internal const string AddSource = """
        unit M;
        interface
        function Add(a, b: Integer): Integer;
        implementation
        function Add(a, b: Integer): Integer;
        begin
          Result := a + b;
        end;
        end.
        """;

    internal const string ShortCircuitSource = """
        unit M;
        interface
        function Both(a, b: Boolean): Boolean;
        function Either(a: Boolean; x: Integer): Boolean;
        implementation
        function Both(a, b: Boolean): Boolean;
        begin
          Result := a and b;
        end;
        function Either(a: Boolean; x: Integer): Boolean;
        begin
          Result := a or (x > 0);
        end;
        function Mixed(a, b, c: Boolean): Boolean;
        begin
          Result := (a and b) or not c;
        end;
        {$B+}
        function Complete(a, b: Boolean): Boolean;
        begin
          Result := (a and b) or (a xor b);
        end;
        end.
        """;

    internal const string DivisionSource = """
        unit M;
        interface
        function Quot(a, b: Integer): Integer;
        function Rem(a, b: Int64): Int64;
        function UQuot(a, b: Cardinal): Cardinal;
        function ByConstants(a: Integer): Integer;
        implementation
        function Quot(a, b: Integer): Integer;
        begin
          Result := a div b;
        end;
        function Rem(a, b: Int64): Int64;
        begin
          Result := a mod b;
        end;
        function UQuot(a, b: Cardinal): Cardinal;
        begin
          Result := a div b;
        end;
        function ByConstants(a: Integer): Integer;
        begin
          Result := a div 7 + a mod 3 + a div -1 + a mod -1;
        end;
        end.
        """;

    internal const string ConversionsSource = """
        unit M;
        interface
        var
          Total: Int64;
          Flag: Boolean = True;
        procedure Mix(b: Byte; var w: Word; c: Char; s: Single);
        function Less(i: Int64; u: UInt64): Boolean;
        implementation
        var
          Count: Integer = 42;
        procedure Mix(b: Byte; var w: Word; c: Char; s: Single);
        var
          i: Integer;
          d: Double;
          ok: Boolean;
          lb: LongBool;
        begin
          i := b + w;
          w := i;
          Total := i * Count;
          i := Ord(c) shl 3;
          i := -(i shr b) xor not i;
          d := s + i;
          s := d;
          ok := (i < 0) = Flag;
          lb := ok;
          Flag := lb and not ok;
          Count := Ord(Flag) + Abs(i);
        end;
        function Less(i: Int64; u: UInt64): Boolean;
        begin
          Result := (i < u) and (u >= i) or (i = u);
        end;
        end.
        """;

    internal const string ForSource = """
        unit M;
        interface
        function Sum(n: Integer): Integer;
        procedure Down(var b: Byte);
        implementation
        function Sum(n: Integer): Integer;
        var
          i: Integer;
        begin
          Result := 0;
          for i := 1 to n do
          begin
            if i = 3 then Continue;
            if i > 10 then Break;
            Result := Result + i;
          end;
        end;
        procedure Down(var b: Byte);
        var
          c: Byte;
        begin
          for c := 255 downto b do
            b := c;
        end;
        end.
        """;

    internal const string CaseSource = """
        unit M;
        interface
        function Classify(x: Integer): Integer;
        function Letter(c: Char): Boolean;
        procedure NoElse(var x: Byte);
        implementation
        function Classify(x: Integer): Integer;
        begin
          case x of
            1: Result := 10;
            2, 4: Result := 20;
            5..7: Result := 30;
            1000..100000, -3: Result := 40;
          else
            Result := 0;
          end;
        end;
        function Letter(c: Char): Boolean;
        begin
          Result := False;
          case c of
            'a'..'c', 'x': Result := True;
          end;
        end;
        procedure NoElse(var x: Byte);
        begin
          case x of
            0: x := 1;
            200..255: Exit;
          end;
          x := 2;
        end;
        end.
        """;

    internal const string LoopsSource = """
        unit M;
        interface
        function Count(n: Integer): Integer;
        function Digits(n: Cardinal): Integer;
        implementation
        function Count(n: Integer): Integer;
        begin
          Result := 0;
          while n > 0 do
          begin
            n := n - 1;
            if n = 5 then Continue;
            if n = 2 then Break;
            Result := Result + 1;
          end;
        end;
        function Digits(n: Cardinal): Integer;
        begin
          Result := 0;
          repeat
            Result := Result + 1;
            n := n div 10;
            if Result > 20 then Continue;
          until n = 0;
        end;
        end.
        """;

    internal const string IfExitSource = """
        unit M;
        interface
        function Sign(x: Integer): Integer;
        procedure Check(x: Integer);
        implementation
        function Sign(x: Integer): Integer;
        begin
          if x < 0 then
            Exit(-1)
          else if x = 0 then
            Result := 0
          else
            Result := 1;
        end;
        procedure Check(x: Integer);
        begin
          if x = 0 then
            Halt(3);
          if x > 0 then
          begin
            Exit;
            x := 1;
          end;
          RunError(201);
        end;
        end.
        """;

    internal static readonly IReadOnlyDictionary<string, string> Sources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["Add"] = AddSource,
        ["ShortCircuit"] = ShortCircuitSource,
        ["Division"] = DivisionSource,
        ["Conversions"] = ConversionsSource,
        ["For"] = ForSource,
        ["Case"] = CaseSource,
        ["Loops"] = LoopsSource,
        ["IfExit"] = IfExitSource,
    };

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For() => Snapshot.Verify("For", Emit(ForSource));

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case() => Snapshot.Verify("Case", Emit(CaseSource));

    [Fact]
    [Trait("Req", "CMP-319")]
    public void Loops() => Snapshot.Verify("Loops", Emit(LoopsSource));

    [Fact]
    [Trait("Req", "CMP-318")]
    public void IfExit() => Snapshot.Verify("IfExit", Emit(IfExitSource));

    [Fact]
    [Trait("Req", "CMP-310")]
    public void EveryRoutine_HasExactlyOneRet()
    {
        foreach (var source in Sources.Values)
        {
            var text = Emit(source);
            Assert.Equal(text.Split("define ").Length - 1, text.Split("\n  ret ").Length - 1);
        }
    }

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Add()
    {
        var text = Emit(AddSource);

        Assert.Contains(Cmp310Add, text);
        Snapshot.Verify("Add", text);
    }

    [Fact]
    [Trait("Req", "CMP-317")]
    public void ShortCircuit() => Snapshot.Verify("ShortCircuit", Emit(ShortCircuitSource));

    [Fact]
    [Trait("Req", "CMP-312")]
    public void Division() => Snapshot.Verify("Division", Emit(DivisionSource));

    [Fact]
    [Trait("Req", "CMP-313")]
    public void Conversions() => Snapshot.Verify("Conversions", Emit(ConversionsSource));

    [Fact]
    [Trait("Req", "CMP-311")]
    public void ImplementationOnlyRoutine_HasInternalLinkage()
    {
        var text = Emit(ShortCircuitSource);

        Assert.Contains("define i8 @\"M.Both\"(i8 %a, i8 %b) #0 {", text);
        Assert.Contains("define internal i8 @\"M.Mixed\"(i8 %a, i8 %b, i8 %c) #0 {", text);
    }

    [Fact]
    [Trait("Req", "CMP-303")]
    public void ParameterNamedLikeAGeneratedName_GetsASuffix()
    {
        var text = Emit("""
            unit M;
            interface
            function F(t1, exit: Integer): Integer;
            implementation
            function F(t1, exit: Integer): Integer;
            begin
              Result := t1 - exit;
            end;
            end.
            """);

        Assert.Contains("define i32 @\"M.F\"(i32 %t1.1, i32 %exit.1) #0 {", text);
        Assert.Contains("  %t1 = load i32, ptr %t1.1.addr, align 4\n", text);
    }

    internal static string Emit(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", source));
        Assert.Empty(tree.Diagnostics);
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        return writer.GetText();
    }
}

/// <summary>The modules of <see cref="IrSnapshotTests"/> are accepted by the pinned clang; fails when the toolchain is missing.</summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class IrSnapshotToolchainTests
{
    public static TheoryData<string, string> Modules()
    {
        var data = new TheoryData<string, string>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in IrSnapshotTests.Sources.Keys)
            {
                data.Add(name, optimisation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-125")]
    public void EmittedModule_CompilesWithPinnedClang(string name, string optimisation)
    {
        var (exitCode, output) = Clang.CompileToObject(IrSnapshotTests.Emit(IrSnapshotTests.Sources[name]), optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}

/// <summary>Compares text with <c>Snapshots/&lt;name&gt;.ll</c> of the source tree; <c>UPDATE_SNAPSHOTS=1</c> rewrites the file.</summary>
internal static class Snapshot
{
    public static string Directory => Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Snapshots");

    public static void Verify(string name, string actual)
    {
        var path = Path.Combine(Directory, name + ".ll");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot {path} is missing (create it deliberately with UPDATE_SNAPSHOTS=1). Actual:\n{actual}");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
