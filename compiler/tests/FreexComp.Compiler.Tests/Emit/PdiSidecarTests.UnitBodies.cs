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

    [GeneratedRegex("""#dbg_declare\(ptr %[^,]*\.ref[^,]*, ![0-9]+, !DIExpression\(DW_OP_deref\)""")]
    private static partial Regex GlobalDeclareRegex();
}
