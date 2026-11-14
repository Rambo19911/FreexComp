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

    /// <summary>
    /// IDE-ARCH-082 / UI-CMD-013: an edit command on the UI thread completes within 50 ms even in a 1 MB unit — typing a
    /// character, the undo and redo commands, and reading the classification of the changed line. Work proportional
    /// to the document (whole-text lexing, folding, brackets) runs off the calling thread, so the median CPU time of
    /// the calling thread stays within the budget.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-082")]
    [Trait("Req", "UI-CMD-013")]
    public void EditCommandsOnLargeUnitStayWithinUiThreadBudget()
    {
        var language = new PascalLanguage();
        var doc = new FreexComp.Ide.Core.ViewModels.TextDocumentViewModel(new FreexComp.Ide.Core.Text.StringTextBuffer(LargeUnit()), @"C:\work\Large.pas", null,
            language, FreexComp.Ide.Core.Text.FileEncoding.Utf8, FreexComp.Ide.Core.Services.LineEndingKind.CrLf);
        var classifier = language.CreateClassifier();
        var target = doc.CommandTarget!;
        var line = doc.Buffer.LineCount / 2;
        doc.ViewState.Caret = new FreexComp.Ide.Core.Text.TextPosition(line, 1);
        var before = doc.Buffer.Length;

        var median = PerformanceBudget.MedianThreadCpu(() =>
        {
            Assert.True(doc.TypeText("x"));
            Assert.True(target.ExecuteAsync("edit.undo", null).GetAwaiter().GetResult());
            Assert.True(target.ExecuteAsync("edit.redo", null).GetAwaiter().GetResult());
            _ = classifier.Classify(doc.Buffer, new LineRange(line, line));
        });

        Assert.True(doc.Buffer.Length > before, "the typed characters are in the buffer");
        var budget = PerformanceBudget.Of(50, typeof(PascalClassifier).Assembly, typeof(FreexComp.Compiler.Syntax.Lexer).Assembly);
        Assert.True(budget.IsMetBy(median), budget.Describe("median UI-thread CPU time of type + undo + redo + classify", median));
    }
}
