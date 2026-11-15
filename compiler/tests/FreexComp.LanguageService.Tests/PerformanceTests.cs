using System.Diagnostics;
using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// language-service.md CMP-702 perf fixtures on a generated 5 000-line unit (C-150): syntax classification of a visible
/// range (60 lines) &lt; 5 ms, completion list &lt; 100 ms after a single-character edit, diagnostics of the edited file
/// &lt; 500 ms. TESTING.md performance rule: budgets are for optimized builds, x3 for an unoptimized assembly under test,
/// x3 again with <c>CI_SLOW=1</c>; single-thread work is the calling thread's CPU time, work that may fan out to other
/// threads is wall-clock; median of 5 after a warm-up. CMP-702 budgets are measured, not enforced in CI: the medians and
/// the verdict against the budget are written to the test output (the perf fixture report); the tests fail only when
/// the fixture itself does not work. The class runs
/// in its own non-parallel collection so other tests do not share the machine while it measures.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
[Trait("Category", "Performance")]
[Trait("Req", "CMP-702")]
[Collection(nameof(PerformanceTests))]
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private const int Routines = 225;

    private const string Typed = "  T := S";

    private static readonly Lazy<string> BigUnit = new(() =>
    {
        var sb = new StringBuilder();
        sb.Append("unit Big;\n\ninterface\n\ntype\n  TItem = class\n  public\n    Value: Integer;\n    Name: string;\n")
          .Append("    function Twice(A: Integer): Integer;\n  end;\n\n");
        for (var i = 0; i < Routines; i++)
        {
            sb.Append("function Work").Append(i).Append("(A: Integer; const S: string): Integer;\n");
        }

        sb.Append("\nimplementation\n\nfunction TItem.Twice(A: Integer): Integer;\nbegin\n  Result := A * 2;\nend;\n");
        for (var i = 0; i < Routines; i++)
        {
            sb.Append($$"""

                function Work{{i}}(A: Integer; const S: string): Integer;
                var
                  I, Sum: Integer;
                  T: string;
                  Item: TItem;
                begin
                  Sum := 0;
                  for I := 1 to A do
                  begin
                    if I mod 3 = 0 then
                      Sum := Sum + I
                    else
                      Sum := Sum - 1;
                  end;
                  T := S + 'x';
                  if Length(T) > 2 then
                    Sum := Sum + Length(T);
                  while Sum > 100 do
                    Sum := Sum div 2;
                  Item := TItem.Create;
                  Sum := Sum + Item.Twice(Sum);
                  Item.Free;
                  Result := Sum;
                end;

                """.ReplaceLineEndings("\n"));
        }

        sb.Append("\nend.\n");
        return sb.ToString();
    });

    private static double Factor
    {
        get
        {
            var debuggable = typeof(Workspace).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
                .OfType<DebuggableAttribute>().FirstOrDefault();
            return (debuggable is { IsJITOptimizerDisabled: true } ? 3 : 1) * (Environment.GetEnvironmentVariable("CI_SLOW") == "1" ? 3 : 1);
        }
    }

    private static Workspace OpenBig()
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", "program P;\nuses Big;\nbegin\n  Writeln(Work0(1, 'a'));\nend."), ("Big.pas", BigUnit.Value)));
        workspace.SetProject(TestFiles.Options());
        return workspace;
    }

    [Fact]
    public void Fixture_IsAFiveThousandLineUnitWithoutErrors()
    {
        var workspace = OpenBig();
        var document = workspace.GetDocument(TestFiles.PathOf("Big.pas"));

        Assert.InRange(document.Text.Lines.LineCount, 5_000, 6_000);
        Assert.DoesNotContain(DiagnosticService.GetSyntaxDiagnostics(document)
            .Concat(DiagnosticService.GetSemanticDiagnostics(document, TestContext.Current.CancellationToken)),
            d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SyntaxClassification_OfTheVisibleRange_AgainstFiveMilliseconds()
    {
        var workspace = OpenBig();
        var tree = workspace.GetDocument(TestFiles.PathOf("Big.pas")).SyntaxTree;
        var firstLine = tree.Text.Lines.LineCount / 2;
        var range = TextSpan.FromBounds(tree.Text.Lines.GetLineSpan(firstLine).Start, tree.Text.Lines.GetLineSpan(firstLine + 59).End);
        Assert.NotEmpty(Classifier.GetSyntacticClassifications(tree, range));     // warm-up

        // one call is below the CPU-clock resolution: each sample is the mean of 50 calls
        var median = Median(() => ThreadCpuTime(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                Classifier.GetSyntacticClassifications(tree, range);
            }
        }) / 50);

        Check("syntax classification (60 lines)", median, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void Completion_AfterASingleCharacterEdit_AgainstOneHundredMilliseconds()
    {
        var (workspace, path, position) = EditTarget();
        var ct = TestContext.Current.CancellationToken;
        var round = 0;
        TimeSpan Sample() => WallClock(() =>
        {
            var caret = TypeOrErase(workspace, path, position, round++);
            var list = CompletionService.GetCompletions(workspace.GetDocument(path), caret,
                new CompletionTrigger(CompletionTriggerKind.Character, 'S'), ct);
            Assert.NotEmpty(list.Items);
        });

        Sample();                                                                  // warm-up
        Check("completion after a one-character edit", Median(Sample), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Diagnostics_OfTheEditedFile_AgainstFiveHundredMilliseconds()
    {
        var (workspace, path, position) = EditTarget();
        var ct = TestContext.Current.CancellationToken;
        var round = 0;
        TimeSpan Sample() => WallClock(() =>
        {
            TypeOrErase(workspace, path, position, round++);
            var document = workspace.GetDocument(path);
            _ = DiagnosticService.GetSyntaxDiagnostics(document).Length + DiagnosticService.GetSemanticDiagnostics(document, ct).Length;
        });

        Sample();                                                                  // warm-up
        Check("diagnostics of the edited file", Median(Sample), TimeSpan.FromMilliseconds(500));
    }

    /// <summary>The workspace, Big.pas and the offset just after <c>T := S</c> in the middle routine.</summary>
    private static (Workspace Workspace, string Path, int Position) EditTarget()
    {
        var workspace = OpenBig();
        var path = TestFiles.PathOf("Big.pas");
        var text = workspace.GetDocument(path).Text.Text;
        var routine = text.IndexOf($"function Work{Routines / 2}(A: Integer; const S: string): Integer;\nvar", StringComparison.Ordinal);
        var position = text.IndexOf(Typed, routine, StringComparison.Ordinal) + Typed.Length;
        return (workspace, path, position);
    }

    /// <summary>Even rounds type one <c>S</c> at <paramref name="position"/>, odd rounds erase it; returns the caret.</summary>
    private static int TypeOrErase(Workspace workspace, string path, int position, int round)
    {
        if (round % 2 == 0)
        {
            workspace.UpdateDocument(path, [new FreexComp.Compiler.Syntax.TextChange(new TextSpan(position, 0), "S")]);
            return position + 1;
        }

        workspace.UpdateDocument(path, [new FreexComp.Compiler.Syntax.TextChange(new TextSpan(position, 1), "")]);
        return position;
    }

    private void Check(string what, TimeSpan median, TimeSpan budget)
    {
        var factor = Factor;
        var limit = budget * factor;
        output.WriteLine($"CMP-702 {what}: median {median.TotalMilliseconds:F2} ms, budget {budget.TotalMilliseconds} ms x{factor} = "
                         + $"{limit.TotalMilliseconds} ms{(median < limit ? "" : " OVER BUDGET")}");
    }

    private static TimeSpan Median(Func<TimeSpan> sample)
    {
        var samples = Enumerable.Range(0, 5).Select(_ => sample()).Order().ToList();
        return samples[2];
    }

    private static TimeSpan WallClock(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed;
    }

    private static TimeSpan ThreadCpuTime(Action action)
    {
        var before = CurrentThreadCpuTime();
        action();
        return CurrentThreadCpuTime() - before;
    }

    private static TimeSpan CurrentThreadCpuTime()
    {
        var id = GetCurrentThreadId();
        foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
        {
            if (thread.Id == id)
            {
                return thread.TotalProcessorTime;
            }
        }

        throw new InvalidOperationException("current thread not found");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}

/// <summary>Runs <see cref="PerformanceTests"/> alone, after the parallel collections.</summary>
[CollectionDefinition(nameof(PerformanceTests), DisableParallelization = true)]
public sealed class PerformanceTestsCollection;
