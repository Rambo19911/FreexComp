using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// C-318 (LANG-380): a label is a symbol of its block for the language service — hover, find references and document highlights
/// over its declaration, its definition and its <c>goto</c>s, and rename of all of them; a label of another block with the same
/// name is another symbol.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "LANG-380")]
public sealed class LabelNavigationTests
{
    private const string Program = """
        program P;
        procedure Work(N: Integer);
        label Again, Done;
        begin
        Again:
          N := N + 1;
          if N < 3 then goto Again;
          if N > 9 then goto Done;
          goto again;
        Done:
        end;
        label Again;
        begin
          Work(1);
        Again:
          goto Again;
        end.
        """;

    private static readonly string PText = Program.ReplaceLineEndings("\n");

    private static Document Doc(string text) => MarkupTestFile.Open("P.pas", ("P.pas", text));

    private static int At(string text, string piece, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
        {
            at = text.IndexOf(piece, at + 1, StringComparison.Ordinal);
        }

        Assert.True(at >= 0, piece);
        return at;
    }

    private static string[] Describe(IEnumerable<ReferenceLocation> references) =>
        [.. references.Select(r => $"{r.Location.LineSpan.Start.Line}:{r.Kind}:{PText.Substring(r.Location.Span.Start, r.Location.Span.Length)}")];

    [Fact]
    public void Hover_LabelAtDeclarationDefinitionAndGoto()
    {
        var doc = Doc(PText);
        foreach (var piece in new[] { "Again, Done", "Again:", "Again;", "again;" })
        {
            var info = QuickInfoService.GetQuickInfo(doc, At(PText, piece) + 1, TestContext.Current.CancellationToken);

            Assert.NotNull(info);
            Assert.Equal("label Again", info.Signature);
            Assert.Equal("P", info.DeclaredIn);
            Assert.Equal(new TextSpan(At(PText, piece), 5), info.Span);
        }
    }

    [Theory]
    [InlineData("Again, Done")]                                 // the declaration
    [InlineData("Again:")]                                      // the definition
    [InlineData("goto again")]                                  // a goto (the caret on the keyword's last character is not on the label)
    public void FindReferences_LabelAndGotos(string piece)
    {
        var position = At(PText, piece) + (piece.StartsWith("goto", StringComparison.Ordinal) ? 6 : 1);

        var references = NavigationService.FindReferences(Doc(PText), position, TestContext.Current.CancellationToken);

        Assert.Equal(["2:Declaration:Again", "4:Read:Again", "6:Read:Again", "8:Read:again"], Describe(references));
    }

    [Fact]
    public void FindReferences_LabelOfAnotherBlock_IsAnotherSymbol()
    {
        var doc = Doc(PText);

        var main = NavigationService.FindReferences(doc, At(PText, "label Again;") + 7, TestContext.Current.CancellationToken);
        var done = NavigationService.FindReferences(doc, At(PText, "Done:") + 1, TestContext.Current.CancellationToken);
        var highlights = NavigationService.GetDocumentHighlights(doc, At(PText, "label Again;") + 7, TestContext.Current.CancellationToken);

        Assert.Equal(["11:Declaration:Again", "14:Read:Again", "15:Read:Again"], Describe(main));
        Assert.Equal(["2:Declaration:Done", "7:Read:Done", "9:Read:Done"], Describe(done));
        Assert.Equal(main.Select(r => r.Location.Span), highlights.Select(h => h.Span));
    }

    [Fact]
    public void Rename_LabelUpdatesGotos()
    {
        var doc = Doc(PText);
        var position = At(PText, "goto Again") + 5;

        var info = RenameService.GetRenameInfo(doc, position, TestContext.Current.CancellationToken);
        var result = RenameService.Rename(doc, position, "Retry", TestContext.Current.CancellationToken);

        Assert.True(info.CanRename, info.Reason);
        Assert.Equal("Again", info.CurrentName);
        Assert.True(result.Success, result.Error);
        var edit = Assert.Single(result.Edits);
        var builder = new System.Text.StringBuilder(PText);
        foreach (var change in edit.Changes.OrderByDescending(c => c.Span.Start))
        {
            builder.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
        }

        var expected = PText
            .Replace("label Again, Done;", "label Retry, Done;", StringComparison.Ordinal)
            .Replace("begin\nAgain:\n  N", "begin\nRetry:\n  N", StringComparison.Ordinal)
            .Replace("then goto Again;", "then goto Retry;", StringComparison.Ordinal)
            .Replace("goto again;", "goto Retry;", StringComparison.Ordinal);
        Assert.Equal(expected, builder.ToString());
        Assert.Empty(Doc(expected).Compilation.GetDiagnostics(null, TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));
    }

    [Fact]
    public void Rename_ToAnotherLabelOfTheBlock_IsRefused()
    {
        var result = RenameService.Rename(Doc(PText), At(PText, "Again:") + 1, "Done", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.StartsWith("Renaming 'Again' to 'Done' changes the meaning", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Edits);
    }
}
