using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Emit;

// C-310 (inline-variables.md LANG-571, CMP-1185): goto does not change the debug scope of inline variables.
public sealed partial class PdiSidecarTests
{
    private const string GotoSource = """
        program P;

        procedure Run(N: Integer);
        label Again, Out;
        begin
        Again:
          N := N + 1;
          var Top: string; // top
          if N < 3 then goto Again;
          begin
            var Inner: string; // inner
            if N > 5 then goto Out;
            Inner := Top;
          end; // inner end
        Out:
        end;

        begin
          Run(1);
        end.
        """;

    /// <summary>
    /// A routine with labels finalises the managed variables of its outermost list in a region too (a backward goto executes the
    /// declaration again, LANG-562); the sidecar ranges stay those of CMP-1184 — the outermost variable has no <c>toLine</c> — and
    /// both gotos clear what they leave before the jump.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1185")]
    public void GotoKeepsInlineVariableLineRanges()
    {
        var (ir, unit, _) = Emit(GotoSource);
        int L(string marker) => LineOf(GotoSource, "// " + marker);
        string[] expected = ["N param -..-", $"Top local {L("top")}..-", $"Inner local {L("inner")}..{L("inner end")}"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Vars(Routine(unit, "Run")).Order(StringComparer.Ordinal));

        var run = ir[Regex.Match(ir, @"^define [^\n]*P\.Run", RegexOptions.Multiline).Index..];
        run = run[..run.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Equal(2, Regex.Matches(run, @"^label\.\d+:", RegexOptions.Multiline).Count);

        // goto Again leaves the scope of Top (the region of the outermost list); goto Out leaves only the scope of Inner
        Assert.Matches(@"_UStrClr""\(ptr %Top\.addr\)[^\n]*\n\s+br label %label\.1\b", run);
        Assert.Matches(@"_UStrClr""\(ptr %Inner\.addr\)[^\n]*\n\s+to label %(cont\.\d+) unwind[^\n]*\n\1:\n\s+br label %label\.6\b", run);
        Assert.Equal(2, Regex.Matches(run, @"br label %label\.6\b").Count);         // the goto and the end of the block before Out
    }
}
