using System.Globalization;
using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Emit;

// C-316 (inline-variables.md CMP-1184; debug-info-sidecar.md §5): inline variables of unit-level bodies in the debug scope.
public sealed partial class PdiSidecarTests
{
    private const string ProgramBlockSource = """
        program P;
        type EA = class end;
        var G: Integer;
        begin
          var Top := 1; // top
          G := Top;
          begin
            var Inner: string; // inner
            Inner := 'a';
            if Inner = '' then G := 2;
          end; // inner end
          begin
            var inner := 2.5; // shadow
            if inner > 2 then G := 3;
          end; // shadow end
          for var I := 1 to 2 do // loop
            G := G + I; // loop end
          try
            G := 4;
          except
            on E: EA do G := 5; // handler
          end;
        end.
        """;

    private const string UnitSectionsSource = """
        unit M;
        interface
        implementation
        var G: Integer;
        initialization
          var A := 1; // init a
          G := A;
          begin
            var B := 2; // init b
            G := G + B;
          end; // init b end
        finalization
          var A := 3; // final a
          G := A;
        end.
        """;

    /// <summary>
    /// The inline variables (and handler variables) of a program block and of <c>initialization</c>/<c>finalization</c> parts are
    /// internal globals, since those bodies have no frame record; each is a variable of the body's subprogram all the same —
    /// listed in its <c>vars[]</c> with the line range and DBG-142 name, and declared on a slot holding the global's address
    /// (<c>DW_OP_deref</c>). They are not unit globals.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1184")]
    [Trait("Req", "DBG-145")]
    public void ProgramBlockInlineVariablesListed()
    {
        var (ir, unit, _) = Emit(ProgramBlockSource);
        int L(string marker) => LineOf(ProgramBlockSource, "// " + marker);
        string[] expected =
        [
            $"Top local {L("top")}..-",
            $"Inner local {L("inner")}..{L("inner end")}",
            $"inner$2 local {L("shadow")}..{L("shadow end")}",
            $"I local {L("loop")}..{L("loop end")}",
            $"E local {L("handler")}..{L("handler")}",
        ];
        Assert.Equal(expected, Vars(Routine(unit, "P")));
        Assert.Equal(["G"], unit["globals"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()));

        // DBG-145: the same spellings in the metadata; every one is declared on a slot of the body that holds the global's address
        Assert.Equal(expected.Select(e => e.Split(' ')[0]).Order(StringComparer.Ordinal),
            LocalVariableRegex().Matches(ir).Select(m => m.Groups[1].Value).Where(n => !n.StartsWith('$')).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, GlobalDeclareRegex().Count(ir));
        Assert.Contains("""store ptr @"P.$main.Top.""", ir, StringComparison.Ordinal);

        (_, unit, _) = Emit(UnitSectionsSource);
        int U(string marker) => LineOf(UnitSectionsSource, "// " + marker);
        Assert.Equal([$"A local {U("init a")}..-", $"B local {U("init b")}..{U("init b end")}"], Vars(Routine(unit, "M.initialization")));
        Assert.Equal([$"A local {U("final a")}..-"], Vars(Routine(unit, "M.finalization")));
        Assert.Equal(["G"], unit["globals"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()));
    }

    private const string ProgramBlockCaptureSource = """
        program P;
        type TF = reference to function: Integer;
        var G: Integer; F: TF;
        begin
          var Plain := 1; // plain
          var N := 10; // captured
          F := function: Integer begin Result := N; Inc(N) end;
          G := Plain + N;
          begin
            var M := 2; // inner
            F := function: Integer begin Result := M + N end;
          end; // inner end
          begin
            var m := 3; // shadow
            F := function: Integer begin Result := m end;
          end; // shadow end
        end.
        """;

    private const string UnitSectionCaptureSource = """
        unit M;
        interface
        type TF = reference to function: Integer;
        var F: TF;
        implementation
        initialization
          var A := 1; // init a
          F := function: Integer begin Result := A end;
        finalization
          var B := 2; // final b
          F := function: Integer begin Result := B end;
        end.
        """;

    /// <summary>
    /// C-320 (CMP-1183, CMP-1184): an inline variable of a unit-level body that an anonymous method captures lives in a block
    /// frame whose hidden local <c>$block&lt;n&gt;</c> is an internal global of the body. It is a variable of the body's subprogram
    /// like the uncaptured ones: listed with its line range and DBG-142 name, and declared on a slot holding that global's
    /// address — the instance the global points to, plus the offset of the variable's field.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1183")]
    [Trait("Req", "CMP-1184")]
    public void ProgramBlockCapturedInlineVariableListed()
    {
        var (ir, unit, result) = Emit(ProgramBlockCaptureSource, ClosureSystemSource);
        int L(string marker) => LineOf(ProgramBlockCaptureSource, "// " + marker);
        string[] expected =
        [
            $"Plain local {L("plain")}..-",
            $"N local {L("captured")}..-",
            $"M local {L("inner")}..{L("inner end")}",
            $"m$2 local {L("shadow")}..{L("shadow end")}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Vars(Routine(unit, "P")).Order(StringComparer.Ordinal));
        Assert.Equal(["F", "G"], unit["globals"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()));

        var blocks = Assert.Single(result.Bodies).Closure!.Blocks;
        Assert.Equal(3, blocks.Count);
        foreach (var (name, debugName) in new[] { ("N", "N"), ("M", "M"), ("m", "m$2") })
        {
            var block = blocks.Single(b => b.Fields.Keys.Any(k => k.Name == name));
            var offset = block.Fields.Single(f => f.Key.Name == name).Value.Offset.ToString(CultureInfo.InvariantCulture);
            var node = Regex.Match(ir, $"""^!(\d+) = !DILocalVariable\(name: "{Regex.Escape(debugName)}",""", RegexOptions.Multiline);
            Assert.True(node.Success, "no DILocalVariable for " + debugName);
            var declare = Assert.Single(ir.Split('\n'), l => l.Contains($", !{node.Groups[1].Value}, !DIExpression(", StringComparison.Ordinal)).Trim();
            Assert.StartsWith($"#dbg_declare(ptr %{block.Local.Name}.ref, !{node.Groups[1].Value}, !DIExpression(DW_OP_deref, DW_OP_deref, DW_OP_plus_uconst, {offset}), ", declare, StringComparison.Ordinal);
            Assert.Contains($"store ptr @\"P.$main.{block.Local.Name}.", ir, StringComparison.Ordinal);
        }

        (_, unit, _) = Emit(UnitSectionCaptureSource, ClosureSystemSource);
        int U(string marker) => LineOf(UnitSectionCaptureSource, "// " + marker);
        Assert.Equal([$"A local {U("init a")}..-"], Vars(Routine(unit, "M.initialization")));
        Assert.Equal([$"B local {U("final b")}..-"], Vars(Routine(unit, "M.finalization")));
    }

    [GeneratedRegex("""#dbg_declare\(ptr %[^,]*\.ref[^,]*, ![0-9]+, !DIExpression\(DW_OP_deref\)""")]
    private static partial Regex GlobalDeclareRegex();
}
