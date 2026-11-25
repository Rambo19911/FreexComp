using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §2: goto labels are <see cref="ClassificationKind.Label"/>, also when the label is a number (C-322).</summary>
public sealed class ClassificationTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-710")]
    public void NumericLabel_IsLabel()
    {
        const string text = "program P;\nlabel 10, Done;\nvar X: Integer;\nbegin\n  X := 10;\n  goto 10;\n  10: X := 20;\n  goto Done;\n  Done: X := X + 10;\nend.";
        var workspace = new Workspace(TestFiles.Files(("P.pas", text)));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));

        var spans = Classifier.GetSemanticClassifications(document, new TextSpan(0, text.Length), TestContext.Current.CancellationToken);

        int[] labels = [text.IndexOf("10, Done", StringComparison.Ordinal), text.IndexOf("10;\n  10:", StringComparison.Ordinal), text.IndexOf("10: X", StringComparison.Ordinal)];
        foreach (var start in labels)
        {
            Assert.Contains(new ClassifiedSpan(new TextSpan(start, 2), ClassificationKind.Label), spans);
        }

        Assert.Equal(3, spans.Count(s => s.Kind == ClassificationKind.Label && text.Substring(s.Span.Start, s.Span.Length) == "Done"));

        // Ordinary numbers get no semantic span: they stay Number.
        Assert.Equal(3, spans.Count(s => text.Substring(s.Span.Start, s.Span.Length) == "10"));
        Assert.DoesNotContain(spans, s => text.Substring(s.Span.Start, s.Span.Length) == "20");
        var numbers = Classifier.GetSyntacticClassifications(document.SemanticModel.SyntaxTree, new TextSpan(0, text.Length)).Where(s => s.Kind == ClassificationKind.NumberLiteral);
        Assert.Contains(numbers, s => s.Span.Start == text.IndexOf("10;\n  goto", StringComparison.Ordinal));
        Assert.Contains(numbers, s => s.Span.Start == text.IndexOf("20", StringComparison.Ordinal));
    }
}
