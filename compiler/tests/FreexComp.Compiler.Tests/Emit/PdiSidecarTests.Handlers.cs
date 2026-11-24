namespace FreexComp.Compiler.Tests.Emit;

// C-315 (debug-info.md DBG-142; debug-info-sidecar.md §5): exception handler variables in the debug scope.
public sealed partial class PdiSidecarTests
{
    private const string HandlerSource = """
        program P;
        type
          EA = class end;
          EB = class end;

        procedure Run;
        var Total: Integer;
        begin
          Total := 0;
          try
            Total := 1;
          except
            on E: EA do // first
              Total := 2; // first end
            on e: EB do // second
            begin
              Total := 3;
            end; // second end
          end;
          try
            Total := 4;
          except
            on E: EA do Total := 5; // third
          end;
          begin
            var E := 1; // inline
            Total := E;
          end; // inline end
        end;

        procedure Other;
        begin
          try
          except
            on E: EA do // other
              if E = nil then Exit; // other end
          end;
        end;

        begin
          Run;
          Other;
        end.
        """;

    /// <summary>
    /// The variable of an <c>on E: T do</c> handler is in scope from the handler's first line to its last; the first declaration of
    /// a name in the routine keeps it, later handler variables and inline variables — one numbering, in source order, compared
    /// without case — are <c>E$2</c>, <c>E$3</c>, … in the sidecar and in the <c>DILocalVariable</c> names.
    /// </summary>
    [Fact]
    [Trait("Req", "DBG-142")]
    public void HandlerVariablesAreNumbered()
    {
        var (ir, unit, _) = Emit(HandlerSource);
        int L(string marker) => LineOf(HandlerSource, "// " + marker);
        string[] expected =
        [
            "Total local -..-",
            $"E local {L("first")}..{L("first end")}",
            $"e$2 local {L("second")}..{L("second end")}",
            $"E$3 local {L("third")}..{L("third")}",
            $"E$4 local {L("inline")}..{L("inline end")}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Vars(Routine(unit, "Run")).Order(StringComparer.Ordinal));
        Assert.Equal([$"E local {L("other")}..{L("other end")}"], Vars(Routine(unit, "Other")));

        // the same spellings in the metadata, and nothing else
        var emitted = LocalVariableRegex().Matches(ir).Select(m => m.Groups[1].Value).Where(n => !n.StartsWith('$')).Order(StringComparer.Ordinal);
        Assert.Equal(emitted, unit["routines"]!.AsArray().SelectMany(r => r!["vars"]!.AsArray()).Select(v => v!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Contains("""!DILocalVariable(name: "e$2",""", ir, StringComparison.Ordinal);
        Assert.Contains("""!DILocalVariable(name: "E$3",""", ir, StringComparison.Ordinal);
        Assert.Contains("""!DILocalVariable(name: "E$4",""", ir, StringComparison.Ordinal);
    }
}
