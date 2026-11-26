using System.Text;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Tests.Infrastructure;
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
    /// buffer version) in under 150 ms. Measured as the fastest CPU time of the test thread over seven runs after two
    /// warm-up runs, each after a full collection, so other load on the machine does not count (I-192).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-056")]
    public void TokenizeLargeUnitWithinBudget()
    {
        var text = LargeUnit();
        var classifier = new PascalClassifier();
        int spans = 0;

        var fastest = PerformanceBudget.MinThreadCpu(() => spans = classifier.LexText(text).Count);

        Assert.True(spans > 100_000, $"the unit is classified ({spans} spans)");
        var budget = PerformanceBudget.Of(150, typeof(PascalClassifier).Assembly, typeof(FreexComp.Compiler.Syntax.Lexer).Assembly);
        Assert.True(budget.IsMetBy(fastest), budget.Describe("fastest CPU time", fastest));
    }

    /// <summary>
    /// IDE-ARCH-082 / UI-CMD-013: an edit command on the UI thread completes within 50 ms even in a 1 MB unit — typing a
    /// character, the undo and redo commands, and reading the classification of the changed line. Work proportional
    /// to the document (whole-text lexing, folding, brackets) runs off the calling thread, so the CPU time of the calling
    /// thread stays within the budget. The test runs under a UI-like synchronization context and, before it ends, drains
    /// the background lexing, folding and token scans it started, so they cannot overlap the next performance test
    /// (I-179). I-205: measured like <see cref="TokenizeLargeUnitWithinBudget"/> (fastest CPU time of the calling thread,
    /// stopping after the time limit), so the background scans of a loaded machine cannot run it into its timeout; the
    /// drain waits for them up to the test's own timeout. I-300: the measurement keeps its own limit
    /// (<see cref="PerformanceBudget.DefaultTimeLimit"/>, 15 s) and the drain gets the rest of a 120 s safety-net timeout
    /// (like <c>LargeFileTests</c>): the last lexing, folding and token scans of the 1 MB unit run on the thread pool, and
    /// with every test project running at once they took longer than the 13 s a 30 s timeout left after the measurement.
    /// How long the drain takes is never measured; the UI-thread budget is unchanged.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-ARCH-082")]
    [Trait("Req", "UI-CMD-013")]
    public void EditCommandsOnLargeUnitStayWithinUiThreadBudget()
    {
        var text = LargeUnit();
        var deadline = CurrentTest.Deadline;
        var ui = new QueueSynchronizationContext();
        var fastest = TimeSpan.Zero;
        var grew = false;

        // Everything that may start background work runs under the context, so every result is posted to it.
        ui.Run(() =>
        {
            var language = new PascalLanguage();
            var doc = new FreexComp.Ide.Core.ViewModels.TextDocumentViewModel(new FreexComp.Ide.Core.Text.StringTextBuffer(text), @"C:\work\Large.pas", null,
                language, FreexComp.Ide.Core.Text.FileEncoding.Utf8, FreexComp.Ide.Core.Services.LineEndingKind.CrLf);
            var classifier = (WholeTextClassifier)language.CreateClassifier();
            try
            {
                var target = doc.CommandTarget!;
                var line = doc.Buffer.LineCount / 2;
                doc.ViewState.Caret = new FreexComp.Ide.Core.Text.TextPosition(line, 1);
                fastest = PerformanceBudget.MinThreadCpu(timeLimit: PerformanceBudget.DefaultTimeLimit, action: () =>
                {
                    Assert.True(doc.TypeText("x"));
                    Assert.True(target.ExecuteAsync("edit.undo", null).GetAwaiter().GetResult());
                    Assert.True(target.ExecuteAsync("edit.redo", null).GetAwaiter().GetResult());
                    _ = classifier.Classify(doc.Buffer, new LineRange(line, line));
                });
                grew = doc.Buffer.Length > text.Length;
            }
            finally
            {
                ui.RunUntil(() => !classifier.IsLexing && !doc.Folding.IsComputing && !doc.Tokens.IsScanning, deadline,
                    "the background lexing, folding and token scans to drain");
            }
        });

        Assert.True(grew, "the typed characters are in the buffer");
        var budget = PerformanceBudget.Of(50, typeof(PascalClassifier).Assembly, typeof(FreexComp.Compiler.Syntax.Lexer).Assembly);
        Assert.True(budget.IsMetBy(fastest), budget.Describe("fastest UI-thread CPU time of type + undo + redo + classify", fastest));
    }
}
