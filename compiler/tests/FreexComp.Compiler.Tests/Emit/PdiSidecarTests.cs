using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-309 (inline-variables.md CMP-1184, CMP-1186; debug-info-sidecar.md §5; debug-info.md DBG-142, DBG-145): the debug scope of
/// inline variables — <c>fromLine</c>/<c>toLine</c> in the sidecar, <c>Name$2</c>… for later declarations of a name in one routine
/// (sidecar and <c>DILocalVariable</c>), and captured inline variables described through the hidden local of their block frame.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
[Trait("Req", "CMP-1184")]
public sealed partial class PdiSidecarTests
{
    private const string Path = @"C:\p\M.pas";

    /// <summary>A <c>System</c> with what closures need (CMP-380): the root class, <c>IInterface</c> and <c>TInterfacedObject</c>.</summary>
    private const string ClosureSystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          IInterface = interface end;
          TInterfacedObject = class(TObject, IInterface) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private static (string Ir, JsonObject Unit, BindingResult Result) Emit(string source, string? systemSource = null)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, source));
        Assert.Empty(tree.Diagnostics);
        BindingResult result;
        if (systemSource is null)
        {
            result = BindWithStubSystem(tree);
        }
        else
        {
            var types = new BuiltInTypes();
            var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", systemSource)), types);
            Assert.Empty(Format(system.Diagnostics));
            result = Binder.BindCompilationUnit(tree, types, system);
        }

        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(DebugFormat.CodeView, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, Path) ? Encoding.UTF8.GetBytes(source) : null };
        var fragment = IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);
        Assert.NotNull(fragment);
        var pdi = JsonNode.Parse(fragment)!.AsObject();
        SidecarTests.ValidateSchema(pdi);
        return (writer.GetText(), pdi["units"]!.AsArray().Single()!.AsObject(), result);
    }

    /// <summary>The 1-based line of the only source line ending with <paramref name="text"/>.</summary>
    private static int LineOf(string source, string text) =>
        Assert.Single(source.Split('\n').Select((l, i) => (Line: l.TrimEnd(), Number: i + 1)), l => l.Line.EndsWith(text, StringComparison.Ordinal)).Number;

    private static JsonObject Routine(JsonObject unit, string displayName) =>
        unit["routines"]!.AsArray().Single(r => r!["displayName"]!.GetValue<string>() == displayName)!.AsObject();

    /// <summary><c>Name kind from..to</c> per variable of the routine, in sidecar order (<c>-</c>: the member is absent).</summary>
    private static string[] Vars(JsonObject routine) =>
        [.. routine["vars"]!.AsArray().Select(v => string.Create(CultureInfo.InvariantCulture,
            $"{v!["name"]!.GetValue<string>()} {v["kind"]!.GetValue<string>()} {Line(v["fromLine"])}..{Line(v["toLine"])}"))];

    private static string Line(JsonNode? node) => node is null ? "-" : node.GetValue<int>().ToString(CultureInfo.InvariantCulture);

    private const string RangesSource = """
        program P;

        procedure Run(N: Integer);
        var Total: Integer;
        begin
          var Top: Integer := N; // top
          Total := Top;
          if N > 0 then
          begin
            var Inner: Integer := 1; // inner
            const Limit: Integer = 5; // limit
            Total := Total + Inner + Limit;
          end; // inner end
          repeat
            var R: Integer := Total; // repeat var
            Total := R - 1;
          until Total < 0; // until
          try
            var T: Integer := 1; // try var
            Total := T;
          finally // finally
            var F: Integer := 2; // finally var
            Total := F;
          end; // try end
          case N of
            1: Total := 1;
          else
            var C: Integer := 3; // case var
            Total := C;
          end; // case end
          for var I := 1 to 3 do // for one
            Total := Total + I; // for one end
          for var J := 1 to 2 do // for two
          begin
            var A, B: Integer; // pair
            A := J;
            B := A;
            Total := Total + B;
          end; // for two end
        end;

        begin
          Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1184, CMP-1186: an inline variable or typed constant carries <c>fromLine</c> = the line of its declaration statement and
    /// <c>toLine</c> = the line of the token closing its statement list (<c>end</c>, <c>until</c>, <c>finally</c>); a
    /// <c>for var</c> variable the first and last line of the loop; one of the routine's outermost list <c>fromLine</c> only;
    /// parameters and classic locals neither.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1186")]
    public void InlineVariableLineRanges()
    {
        var (_, unit, _) = Emit(RangesSource);
        int L(string marker) => LineOf(RangesSource, "// " + marker);
        string[] expected =
        [
            "N param -..-",
            "Total local -..-",
            $"Top local {L("top")}..-",
            $"Inner local {L("inner")}..{L("inner end")}",
            $"Limit local {L("limit")}..{L("inner end")}",
            $"R local {L("repeat var")}..{L("until")}",
            $"T local {L("try var")}..{L("finally")}",
            $"F local {L("finally var")}..{L("try end")}",
            $"C local {L("case var")}..{L("case end")}",
            $"I local {L("for one")}..{L("for one end")}",
            $"J local {L("for two")}..{L("for two end")}",
            $"A local {L("pair")}..{L("for two end")}",
            $"B local {L("pair")}..{L("for two end")}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Vars(Routine(unit, "Run")).Order(StringComparer.Ordinal));
    }

    private const string ShadowSource = """
        program P;

        procedure Run(N: Integer);
        var Total: Integer;
        begin
          Total := 0;
          if N > 0 then
          begin
            var Inner := 1; // first
            Total := Inner;
          end; // first end
          if N > 1 then
          begin
            var Inner := 'text'; // second
            if Inner = '' then Total := 2;
          end; // second end
          for var I := 1 to 2 do // loop one
            Total := Total + I; // loop one end
          for var i := 1 to 2 do // loop two
          begin
            var inner: Integer := i; // third
            Total := Total + inner;
          end; // loop two end
        end;

        procedure Other;
        begin
          begin
            var Inner := 1; // other
            if Inner = 0 then Exit;
          end; // other end
        end;

        begin
          Run(1);
          Other;
        end.
        """;

    /// <summary>
    /// DBG-142: the first declaration of a name in a routine keeps it; later ones (compared without case, in source order) are
    /// <c>Name$2</c>, <c>Name$3</c>, … with the declared spelling — in the sidecar and in the <c>DILocalVariable</c> names
    /// (DBG-145). The numbering is per routine.
    /// </summary>
    [Fact]
    [Trait("Req", "DBG-142")]
    [Trait("Req", "DBG-145")]
    public void InlineShadowNamesAreNumbered()
    {
        var (ir, unit, _) = Emit(ShadowSource);
        int L(string marker) => LineOf(ShadowSource, "// " + marker);
        string[] expected =
        [
            "N param -..-",
            "Total local -..-",
            $"Inner local {L("first")}..{L("first end")}",
            $"Inner$2 local {L("second")}..{L("second end")}",
            $"I local {L("loop one")}..{L("loop one end")}",
            $"i$2 local {L("loop two")}..{L("loop two end")}",
            $"inner$3 local {L("third")}..{L("loop two end")}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Vars(Routine(unit, "Run")).Order(StringComparer.Ordinal));
        Assert.Equal([$"Inner local {L("other")}..{L("other end")}"], Vars(Routine(unit, "Other")));

        // the types follow the declarations: Integer, string, Integer
        var types = unit.Parent!.Parent!["types"]!.AsArray();
        string TypeOf(string name) => types.Single(t => t!["id"]!.GetValue<int>()
            == Routine(unit, "Run")["vars"]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == name)!["type"]!.GetValue<int>())!["kind"]!.GetValue<string>();
        Assert.Equal(["integer", "string", "integer"], [TypeOf("Inner"), TypeOf("Inner$2"), TypeOf("inner$3")]);

        // DBG-145: the same spellings in the metadata, and nothing else
        var emitted = LocalVariableRegex().Matches(ir).Select(m => m.Groups[1].Value).Where(n => !n.StartsWith('$')).Order(StringComparer.Ordinal);
        Assert.Equal(emitted, unit["routines"]!.AsArray().SelectMany(r => r!["vars"]!.AsArray()).Select(v => v!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Contains("""!DILocalVariable(name: "Inner$2",""", ir, StringComparison.Ordinal);
        Assert.Contains("""!DILocalVariable(name: "inner$3",""", ir, StringComparison.Ordinal);
    }

    private const string CaptureSource = """
        program P;
        type TF = reference to function: Integer;

        procedure Run(P: Integer);
        var Fs: array[0..2] of TF; I: Integer;
        begin
          for I := 0 to 2 do
          begin
            var Plain: Integer := I; // plain
            var N := I * 10; // captured
            Fs[I] := function: Integer begin Result := N + P; Inc(N) end;
            N := N + Plain;
          end; // block end
        end;

        begin
          Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1184: a captured inline variable (CMP-1183) is listed with its line range like any inline variable, and its
    /// <c>DILocalVariable</c> is declared on the hidden local <c>$block&lt;n&gt;</c> of its block frame: the instance that local
    /// points to, plus the offset of the variable's field. An uncaptured variable of the same list stays a slot of the routine.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1183")]
    public void CapturedInlineVariableThroughBlockFrame()
    {
        var (ir, unit, result) = Emit(CaptureSource, ClosureSystemSource);
        int L(string marker) => LineOf(CaptureSource, "// " + marker);
        var vars = Vars(Routine(unit, "Run"));
        Assert.Contains($"Plain local {L("plain")}..{L("block end")}", vars);
        Assert.Contains($"N local {L("captured")}..{L("block end")}", vars);
        Assert.Equal(["Fs", "I", "N", "P", "Plain"], vars.Select(v => v.Split(' ')[0]).Order(StringComparer.Ordinal));

        var block = Assert.Single(result.Routines.Single(r => r.Routine.Name == "Run").Routine.Closure!.Blocks);
        var field = block.Fields.Single(f => f.Key.Name == "N").Value;
        var node = Regex.Match(ir, """^!(\d+) = !DILocalVariable\(name: "N",""", RegexOptions.Multiline);
        Assert.True(node.Success, "no DILocalVariable for N");
        var offset = field.Offset.ToString(CultureInfo.InvariantCulture);
        var declare = Assert.Single(ir.Split('\n'), l => l.Contains($", !{node.Groups[1].Value}, !DIExpression(", StringComparison.Ordinal)).Trim();
        Assert.StartsWith($"#dbg_declare(ptr %{block.Local.Name}.addr, !{node.Groups[1].Value}, !DIExpression(DW_OP_deref, DW_OP_plus_uconst, {offset}), ", declare, StringComparison.Ordinal);

        // the frame's hidden local itself is an artificial variable of the routine (DBG-145: not in the sidecar)
        Assert.Contains($"!DILocalVariable(name: \"{block.Local.Name}\",", ir, StringComparison.Ordinal);
        var plain = Regex.Match(ir, """^!(\d+) = !DILocalVariable\(name: "Plain",""", RegexOptions.Multiline);
        Assert.Contains($"#dbg_declare(ptr %Plain.addr, !{plain.Groups[1].Value}, !DIExpression(), ", ir, StringComparison.Ordinal);
    }

    [GeneratedRegex("""!DILocalVariable\(name: "([^"]*)",""")]
    private static partial Regex LocalVariableRegex();
}
