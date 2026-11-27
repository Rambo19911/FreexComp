using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-128 (CMP-420…424, debug-info.md §1–§6, §8): the debug metadata <c>-g</c> adds — module flags, compile unit, files with MD5,
/// subprograms, the line rules of §3, <c>#dbg_declare</c> records and the type descriptions — as golden <c>.ll</c> files
/// (<c>Snapshots/DebugInfo_*.ll</c>, <c>UPDATE_SNAPSHOTS=1</c>), and the same modules accepted by the pinned clang.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Snapshot")]
public sealed class DebugInfoTests
{
    /// <summary>The program of the D6-0 spike (debug-info.md §9; line numbers are significant).</summary>
    internal const string HelloSource = """
        program hello;

        function Twice(X: Integer): Integer;
        begin
          Result := X * 2;
        end;

        var
          A, B: Integer;
        begin
          A := 21;
          B := Twice(A);
          if B > 40 then
            A := 0;
        end.
        """;

    /// <summary>Parameters (value, var, const record, open array), locals, a function result and a global of each kind (DBG-136…140).</summary>
    internal const string VariablesSource = """
        unit M;
        interface
        type
          TColor = (clRed, clGreen, clBlue);
          TPoint = record
            X, Y: Integer;
          end;
          PNode = ^TNode;
          TNode = record
            Value: Int64;
            Next: PNode;
          end;
        var
          Counter: Integer;
        function Sum(const P: TPoint; var Total: Int64; const Items: array of Integer): Integer;
        implementation
        var
          Hidden: TColor = clGreen;
          Grid: array[1..3, 0..1] of Byte;
          Flags: set of TColor;
          Name: string;
          Short: string[10];
          Head: PNode;
          Ok: Boolean;
          Letter: Char;
          Ratio: Double;
          Words: array of Word;
        function Sum(const P: TPoint; var Total: Int64; const Items: array of Integer): Integer;
        var
          I: Integer;
        begin
          Result := P.X + P.Y;
          for I := 0 to High(Items) do
            Result := Result + Items[I];
          Total := Total + Result;
          Counter := Counter + 1;
        end;
        end.
        """;

    /// <summary>The control-flow rules of debug-info.md §3 (DBG-120…125) in one unit.</summary>
    internal const string StatementsSource = """
        unit M;
        interface
        function Flow(N: Integer): Integer;
        implementation
        function Flow(N: Integer): Integer;
        var
          I, K: Integer;
        begin
          K := 0;
          if N > 0 then
            K := 1
          else
          begin
            K := 2;
            K := K + 1;
          end;
          while K < 10 do
            K := K * 2;
          repeat
            Dec(K);
          until K < 5;
          case N of
            1: K := 10;
            2, 3: K := 20;
          else
            K := 30;
          end;
          for I := 1 to N do
          begin
            if I = 3 then
              Continue;
            K := K + I;
          end;
          if K = 0 then
            Exit;
          Result := K;
        end;
        end.
        """;

    /// <summary>A nested routine capturing a variable and a <c>var</c> parameter of its parent (frame record, CMP-345/422).</summary>
    internal const string NestedSource = """
        unit M;
        interface
        function Outer(var X: Integer): Integer;
        implementation
        function Outer(var X: Integer): Integer;
        var
          Count: Integer;

          procedure Bump;
          begin
            Count := Count + 1;
            X := X + Count;
          end;

        begin
          Count := 0;
          Bump;
          Result := Count;
        end;
        end.
        """;

    /// <summary>A class with a field and a method (Self, DBG-138; class layout with $vmt, §5).</summary>
    internal const string ClassSource = """
        unit M;
        interface
        type
          TCounter = class
            Value: Integer;
            procedure Add(N: Integer);
          end;
        implementation
        procedure TCounter.Add(N: Integer);
        begin
          Value := Value + N;
        end;
        end.
        """;

    /// <summary>DBG-127: <c>try … finally</c> (the outlined <c>$fin</c> routine has its own subprogram) and <c>try … except</c> with a handler.</summary>
    internal const string ExceptionsSource = """
        unit M;
        interface
        type
          EA = class
          end;
        procedure Step(N: Integer);
        function Run(N: Integer): Integer;
        implementation
        procedure Step(N: Integer); begin end;
        function Run(N: Integer): Integer;
        var
          X: EA;
        begin
          Result := 0;
          try
            Step(N);
          finally
            Result := Result + 1;
          end;
          try
            Step(Result);
          except
            on E: EA do
              X := E;
          end;
        end;
        end.
        """;

    internal static readonly IReadOnlyDictionary<string, string> Sources = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["Class"] = ClassSource,
        ["Exceptions"] = ExceptionsSource,
        ["Hello"] = HelloSource,
        ["Nested"] = NestedSource,
        ["Statements"] = StatementsSource,
        ["Variables"] = VariablesSource,
    };

    /// <summary>Emits <paramref name="source"/> (<c>C:\p\M.pas</c>) against the stub System with debug information.</summary>
    internal static string EmitWithDebugInfo(string source, DebugFormat format = DebugFormat.CodeView, string path = @"C:\p\M.pas")
    {
        var tree = SyntaxTree.Parse(SourceText.From(path, source));
        Assert.Empty(tree.Diagnostics);
        var result = BindWithStubSystem(tree);
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(format, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, path) ? Encoding.UTF8.GetBytes(source) : null };
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);
        return writer.GetText();
    }

    [Fact]
    [Trait("Req", "CMP-420")]
    [Trait("Req", "CMP-421")]
    [Trait("Req", "DBG-110")]
    [Trait("Req", "DBG-117")]
    [Trait("Req", "DBG-118")]
    public void DebugInfo_IrGolden_Hello() => Snapshot.Verify("DebugInfo_Hello", EmitWithDebugInfo(HelloSource, path: @"C:\p\hello.pas"));

    [Fact]
    [Trait("Req", "CMP-422")]
    [Trait("Req", "CMP-423")]
    [Trait("Req", "DBG-137")]
    [Trait("Req", "DBG-140")]
    public void DebugInfo_IrGolden_Variables() => Snapshot.Verify("DebugInfo_Variables", EmitWithDebugInfo(VariablesSource));

    [Fact]
    [Trait("Req", "DBG-120")]
    [Trait("Req", "DBG-121")]
    [Trait("Req", "DBG-123")]
    [Trait("Req", "DBG-124")]
    [Trait("Req", "DBG-125")]
    public void DebugInfo_IrGolden_Statements() => Snapshot.Verify("DebugInfo_Statements", EmitWithDebugInfo(StatementsSource));

    [Fact]
    [Trait("Req", "CMP-422")]
    [Trait("Req", "DBG-141")]
    public void DebugInfo_IrGolden_Nested() => Snapshot.Verify("DebugInfo_Nested", EmitWithDebugInfo(NestedSource));

    [Fact]
    [Trait("Req", "DBG-138")]
    [Trait("Req", "CMP-423")]
    public void DebugInfo_IrGolden_Class() => Snapshot.Verify("DebugInfo_Class", EmitWithDebugInfo(ClassSource));

    [Fact]
    [Trait("Req", "DBG-127")]
    public void DebugInfo_IrGolden_Exceptions() => Snapshot.Verify("DebugInfo_Exceptions", EmitWithDebugInfo(ExceptionsSource));

    /// <summary>CMP-420: the DWARF container replaces only the container flag.</summary>
    [Fact]
    [Trait("Req", "CMP-420")]
    [Trait("Req", "DBG-104")]
    public void DebugInfo_IrGolden_Dwarf()
    {
        var codeView = EmitWithDebugInfo(HelloSource, DebugFormat.CodeView, @"C:\p\hello.pas");
        var dwarf = EmitWithDebugInfo(HelloSource, DebugFormat.Dwarf, @"C:\p\hello.pas");

        Assert.Contains("!2 = !{i32 2, !\"CodeView\", i32 1}\n", codeView);
        Assert.Equal(codeView.Replace("!2 = !{i32 2, !\"CodeView\", i32 1}\n", "!2 = !{i32 7, !\"Dwarf Version\", i32 5}\n", StringComparison.Ordinal), dwarf);
    }

    /// <summary>DBG-115: in a function with a subprogram every call carries a location; without one (DBG-114) no instruction does.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    [Trait("Req", "DBG-114")]
    [Trait("Req", "DBG-115")]
    public void DebugInfo_EveryInstructionOfASubprogramHasALocation(string name)
    {
        var text = EmitWithDebugInfo(Sources[name], path: name == "Hello" ? @"C:\p\hello.pas" : @"C:\p\M.pas");
        // one line per instruction: an invoke's "to label" line and a switch's case lines belong to it
        text = Regex.Replace(text.Replace("\n          to label", " to label", StringComparison.Ordinal), @"\[\n(    .*\n)*  \]", "[ ]");
        var inSubprogram = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("define ", StringComparison.Ordinal))
            {
                inSubprogram = line.Contains(" !dbg !", StringComparison.Ordinal);
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("    ", StringComparison.Ordinal))
            {
                Assert.True(inSubprogram == line.Contains(", !dbg !", StringComparison.Ordinal), "Unexpected location state: " + line);
            }
        }
    }

    /// <summary>CMP-424: without <c>-g</c> no metadata at all, but the module flag of a module with try regions (CMP-398).</summary>
    [Fact]
    [Trait("Req", "CMP-424")]
    public void WithoutDebugInfo_NoMetadataIsEmitted()
    {
        foreach (var source in Sources.Values)
        {
            var text = IrSnapshotTests.Emit(source);
            Assert.DoesNotContain("!dbg", text);
            Assert.DoesNotContain("#dbg_", text);
            Assert.DoesNotContain("\n!", text.Replace("\n!llvm.module.flags = !{!0}\n!0 = " + IrEmitter.AsyncEhFlag + "\n", "\n", StringComparison.Ordinal));
        }
    }

    /// <summary>DBG-106: the same source gives the same metadata.</summary>
    [Fact]
    [Trait("Req", "DBG-106")]
    public void DebugInfo_Deterministic()
    {
        foreach (var source in Sources.Values)
        {
            Assert.Equal(EmitWithDebugInfo(source), EmitWithDebugInfo(source));
        }
    }

    public static TheoryData<string> Names() => [.. Sources.Keys];

    /// <summary>Captured <c>for var</c> variables of both loop forms (C-387; line markers are significant).</summary>
    private const string ForVarCapturedSource = """
        program P;
        type TF = reference to function: Integer;

        procedure Run(const Values: array of Integer);
        var Fs: array[0..2] of TF;
        begin
          for var I := 0 to 2 do // counted
          begin
            Fs[I] := function: Integer begin Result := I * 10 end;
          end; // counted end
          for var X in Values do // in
            Fs[0] := function: Integer begin Result := X end; // in end
        end;

        begin
          Run([1, 2]);
        end.
        """;

    /// <summary>
    /// CMP-1412 (C-387): a captured <c>for var</c> variable is described like a captured inline variable (CMP-1184) — its
    /// <c>DILocalVariable</c> is declared on the hidden local <c>$block&lt;m&gt;</c> of its loop's per-iteration frame with
    /// <c>DW_OP_deref, DW_OP_plus_uconst &lt;field offset&gt;</c>, and its sidecar range is the first and last line of the loop.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1412")]
    [Trait("Req", "LANG-577")]
    public void ForVarCaptured_DescribedThroughItsBlockFrame()
    {
        const string path = @"C:\p\P.pas";
        var tree = SyntaxTree.Parse(SourceText.From(path, ForVarCapturedSource));
        Assert.Empty(tree.Diagnostics);
        var types = new FreexComp.Compiler.Types.BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", IrSnapshotTests.ClosureSystemSource)), types);
        Assert.Empty(Format(system.Diagnostics));
        var result = Binder.BindCompilationUnit(tree, types, system);
        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(DebugFormat.CodeView, IsOptimized: false)
        {
            ReadFile = p => PathUtil.Comparer.Equals(p, path) ? Encoding.UTF8.GetBytes(ForVarCapturedSource) : null,
        };
        var fragment = IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);
        var ir = writer.GetText();
        Assert.NotNull(fragment);
        var pdi = System.Text.Json.Nodes.JsonNode.Parse(fragment)!.AsObject();
        SidecarTests.ValidateSchema(pdi);
        var unit = pdi["units"]!.AsArray().Single()!.AsObject();
        var run = unit["routines"]!.AsArray().Single(r => r!["displayName"]!.GetValue<string>() == "Run")!.AsObject();
        int Line(string marker) =>
            Assert.Single(ForVarCapturedSource.Split('\n').Select((l, i) => (Text: l.TrimEnd(), Number: i + 1)), l => l.Text.EndsWith("// " + marker, StringComparison.Ordinal)).Number;
        string Range(string name)
        {
            var entry = run["vars"]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == name)!;
            return $"{entry["kind"]!.GetValue<string>()} {entry["fromLine"]!.GetValue<int>()}..{entry["toLine"]!.GetValue<int>()}";
        }

        Assert.Equal($"local {Line("counted")}..{Line("counted end")}", Range("I"));
        Assert.Equal($"local {Line("in")}..{Line("in end")}", Range("X"));

        var blocks = result.Routines.Single(r => r.Routine.Name == "Run").Routine.Closure!.Blocks;
        Assert.Equal(2, blocks.Count);
        foreach (var name in new[] { "I", "X" })
        {
            var block = blocks.Single(b => b.Fields.Keys.Any(k => k.Name == name));
            var offset = block.Fields.Single(f => f.Key.Name == name).Value.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var node = Regex.Match(ir, $"""^!(\d+) = !DILocalVariable\(name: "{name}",""", RegexOptions.Multiline);
            Assert.True(node.Success, "no DILocalVariable for " + name);
            var declare = Assert.Single(ir.Split('\n'), l => l.Contains($", !{node.Groups[1].Value}, !DIExpression(", StringComparison.Ordinal)).Trim();
            Assert.StartsWith($"#dbg_declare(ptr %{block.Local.Name}.addr, !{node.Groups[1].Value}, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, {offset}), ",
                declare, StringComparison.Ordinal);
        }

        // the counted loop's driver is a hidden local like the index of a for … in loop: artificial, not in the sidecar (DBG-145)
        Assert.DoesNotContain(run["vars"]!.AsArray(), v => v!["name"]!.GetValue<string>().StartsWith('$'));
        var counted = blocks.Single(b => b.Fields.Keys.Any(k => k.Name == "I"));
        var driver = Assert.Single(ir.Split('\n'), l => l.Contains($"!DILocalVariable(name: \"$for{counted.Local.Name["$block".Length..]}\",", StringComparison.Ordinal));
        Assert.Contains("flags: DIFlagArtificial", driver, StringComparison.Ordinal);
    }
}

/// <summary>debug-info.md §8 <c>DebugInfo_VerifierClean</c>: the pinned clang (with its verifier) accepts every module emitted with <c>-g</c>.</summary>
[Trait("Phase", "6")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class DebugInfoToolchainTests
{
    public static TheoryData<string, string, DebugFormat> Modules()
    {
        var data = new TheoryData<string, string, DebugFormat>();
        foreach (var optimisation in new[] { "-O0", "-O2" })
        {
            foreach (var name in DebugInfoTests.Sources.Keys)
            {
                data.Add(name, optimisation, DebugFormat.CodeView);
                data.Add(name, optimisation, DebugFormat.Dwarf);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Modules))]
    [Trait("Req", "CMP-420")]
    public async Task DebugInfo_VerifierClean(string name, string optimisation, DebugFormat format)
    {
        var text = DebugInfoTests.EmitWithDebugInfo(DebugInfoTests.Sources[name], format, name == "Hello" ? @"C:\p\hello.pas" : @"C:\p\M.pas");

        var (exitCode, output) = await Clang.CompileToObjectAsync(text, optimisation);

        Assert.True(exitCode == 0 && !output.Contains("warning", StringComparison.OrdinalIgnoreCase), "clang:\n" + output);
    }
}

/// <summary>
/// debug-info.md §8 <c>DebugInfoSmokeTests</c> (C-128): a program built with <c>-g</c> against the real RTL (System with debug
/// information too) compiles — every <c>.ll</c> passes <c>clang -c</c> —, links with <c>/debug</c>, runs, and the <c>.pdb</c> exists.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class DebugInfoSmokeTests
{
    private const string Source = """
        program DbgDemo;

        type
          TPoint = record
            X, Y: Integer;
          end;

        function Twice(X: Integer): Integer;
        begin
          Result := X * 2;
        end;

        procedure Show(const S: string; var P: TPoint);
        var
          I: Integer;
        begin
          for I := 1 to 2 do
            P.X := P.X + I;
          try
            WriteLn(S, ' ', Twice(P.X), ' ', P.Y);
          finally
            P.Y := 0;
          end;
        end;

        var
          A: TPoint;
        begin
          A.X := 18;
          A.Y := 7;
          Show('value', A);
          WriteLn(A.X + A.Y);
        end.
        """;

    [Theory]
    [InlineData(OptimizationLevel.O0)]
    [InlineData(OptimizationLevel.O2)]
    [Trait("Req", "CMP-420")]
    [Trait("Req", "DBG-105")]
    public async Task ProgramBuiltWithDebugInfo_LinksRunsAndHasAPdb(OptimizationLevel optimization)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var fs = PhysicalFileSystem.Instance;
        var main = Path.Combine(dir, "DbgDemo.pas");
        fs.WriteAllBytes(main, Encoding.UTF8.GetBytes(Source));
        var exe = Path.Combine(dir, "out", "DbgDemo.exe");
        var options = new CompilationOptions
        {
            MainFile = PathUtil.Normalize(main),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = Path.Combine(dir, "obj"),
            Optimization = optimization,
            DebugInfo = true,
        };
        var commands = new List<string>();

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { OutputPath = exe, CommandLineSink = commands.Add }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var programIr = File.ReadAllText(Path.Combine(dir, "obj", "DbgDemo.ll"));
        var systemIr = File.ReadAllText(Path.Combine(dir, "obj", "System.ll"));
        var pdb = File.Exists(Path.Combine(dir, "out", "DbgDemo.pdb"));
        var stdout = new List<string>();
        var stderr = new List<string>();
        var exitCode = await FreexComp.Tests.Shared.NativeRun.RunAsync(exe, dir, stdout.Add, stderr.Add);
        TestDirectories.Delete(dir);

        Assert.Contains(commands, c => c.Contains("clang", StringComparison.OrdinalIgnoreCase) && c.EndsWith(" -g", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Contains("lld-link", StringComparison.OrdinalIgnoreCase));
        Assert.True(pdb, "DbgDemo.pdb was not written");
        Assert.Contains("!2 = !{i32 2, !\"CodeView\", i32 1}", programIr);
        Assert.Contains("distinct !DISubprogram(name: \"Twice\", linkageName: \"DbgDemo.Twice\"", programIr);
        Assert.Contains("distinct !DISubprogram(name: \"DbgDemo\", linkageName: \"$main\"", programIr);
        Assert.Contains("!DICompileUnit(language: DW_LANG_Pascal83", systemIr);
        Assert.Equal(["value 42 7", "21"], stdout);
        Assert.Empty(stderr);
        Assert.Equal(0, exitCode);
    }
}
