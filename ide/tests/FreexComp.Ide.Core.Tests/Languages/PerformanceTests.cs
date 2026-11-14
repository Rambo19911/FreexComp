using System.Text;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Languages;

/// <summary>Time budgets of Phase 1 (testing.md §5.1, category P).</summary>
[Trait("Phase", "1")]
[Trait("Category", "Performance")]
public sealed class PerformanceTests
{
    /// <summary>One routine with every token class the classifier produces (keywords, types, strings, numbers, comments, directives).</summary>
    private const string Routine = """
        {$IFDEF DEBUG} {$R+} {$ENDIF}
        // Computes a checksum over the items of a list.
        function TWorker.Checksum{0}(const Items: array of Integer; Seed: Cardinal = $FFFF): Int64;
        var
          I: Integer;
          Name: string;
          Ratio: Double;
        begin
          (* block comment with 'quotes' inside *)
          Result := Seed;
          Name := 'item''s name'#13#10 + 'tail';
          Ratio := 1.5E-3;
          for I := Low(Items) to High(Items) do
          begin
            if (Items[I] mod 2 = 0) and not Assigned(FOwner) then
              Result := Result xor (Items[I] shl 3)
            else
              Inc(Result, Items[I] * 31 + Ord(Name[1]));
          end;
          case Result of
            0..9: Result := -1;
          else
            Result := Trunc(Ratio * Result);
          end;
        end;


        """;

    /// <summary>A unit of at least 1 MB (IDE-P1-056: "1 MB of Pascal source").</summary>
    private static string LargeUnit()
    {
        var text = new StringBuilder("unit Large;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\n");
        for (int i = 0; text.Length < 1024 * 1024; i++)
        {
            text.Append(Routine.Replace("{0}", i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ReplaceLineEndings("\r\n"));
        }

        return text.Append("end.\r\n").ToString();
    }

    /// <summary>
    /// IDE-P1-056: 1 MB of Pascal source is lexed and classified (the work of <see cref="PascalClassifier"/> for one
    /// buffer version) in under 150 ms. Measured as the median CPU time of the test thread over five runs after a
    /// warm-up run, so other load on the machine does not count.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-056")]
    public void TokenizeLargeUnitWithinBudget()
    {
        var text = LargeUnit();
        var classifier = new PascalClassifier();
        int spans = 0;

        var median = PerformanceBudget.MedianThreadCpu(() => spans = classifier.LexText(text).Count);

        Assert.True(spans > 100_000, $"the unit is classified ({spans} spans)");
        var budget = PerformanceBudget.Of(150, typeof(PascalClassifier).Assembly, typeof(FreexComp.Compiler.Syntax.Lexer).Assembly);
        Assert.True(budget.IsMetBy(median), budget.Describe("median CPU time", median));
    }
}
