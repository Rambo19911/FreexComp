using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>Language-service behaviour of language additions that touch several services at once.</summary>
public sealed class LanguageServiceTests
{
    private const string DispatchProgram = """
        program P;
        type
          IInventoryDisp = dispinterface ['{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D01}']
            procedure Add(const Key: string; Value: Integer); dispid 1;
            function Exists(const Key: string): Boolean; dispid 2;
            property Count: Integer readonly dispid 3;
            property Item[const Key: string]: Integer dispid 0; default;
          end;
        var
          D: IInventoryDisp;
        begin
          D.Add('a', D.Count);
        end.
        """;

    private static Document Open(string text)
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", text)));
        workspace.SetProject(TestFiles.Options());
        return workspace.GetDocument(TestFiles.PathOf("P.pas"));
    }

    /// <summary>
    /// com-interfaces.md CMP-1211 (C-303): <c>dispinterface</c> is classified as a keyword and <c>dispid</c> / <c>readonly</c> as
    /// contextual keywords in member position; the members of a dispatch interface complete after <c>D.</c> like interface
    /// members; hover shows the member with its <c>dispid</c>; the type folds and appears in the outline.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1211")]
    public void DispInterface_KeywordClassifiedAndMembersCompleted()
    {
        var text = DispatchProgram.ReplaceLineEndings("\n");
        var ct = TestContext.Current.CancellationToken;

        // classification: lexical (the word alone) and syntactic
        var lexical = Classifier.GetLexicalClassifications(Lexer.Tokenize(SourceText.From(TestFiles.PathOf("P.pas"), text)), new TextSpan(0, text.Length));
        Assert.Equal(ClassificationKind.Keyword, lexical.Single(s => text.Substring(s.Span.Start, s.Span.Length) == "dispinterface").Kind);
        var document = Open(text);
        var syntactic = Classifier.GetSyntacticClassifications(document.SyntaxTree, new TextSpan(0, text.Length));
        ClassificationKind[] KindsOf(string word) => [.. syntactic.Where(s => text.Substring(s.Span.Start, s.Span.Length) == word).Select(s => s.Kind).Distinct()];
        Assert.Equal([ClassificationKind.Keyword], KindsOf("dispinterface"));
        Assert.Equal([ClassificationKind.ContextualKeyword], KindsOf("dispid"));
        Assert.Equal([ClassificationKind.ContextualKeyword], KindsOf("readonly"));

        // completion after "D."
        var caret = text.IndexOf("D.Add", StringComparison.Ordinal) + 2;
        var completions = CompletionService.GetCompletions(document, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), ct);
        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Contains("Add", labels);
        Assert.Contains("Exists", labels);
        Assert.Contains("Count", labels);
        Assert.Contains("Item", labels);

        // hover: the member with its dispid
        var method = QuickInfoService.GetQuickInfo(document, caret + 1, ct);
        Assert.NotNull(method);
        Assert.Contains("procedure IInventoryDisp.Add(const Key: string; Value: Integer); dispid 1", method.Signature, StringComparison.Ordinal);
        var property = QuickInfoService.GetQuickInfo(document, text.IndexOf("D.Count", StringComparison.Ordinal) + 3, ct);
        Assert.NotNull(property);
        Assert.Contains("property IInventoryDisp.Count: Integer readonly dispid 3", property.Signature, StringComparison.Ordinal);
        var type = QuickInfoService.GetQuickInfo(document, text.IndexOf("D: IInventoryDisp", StringComparison.Ordinal) + 4, ct);
        Assert.NotNull(type);
        Assert.Contains("type IInventoryDisp = dispinterface", type.Signature, StringComparison.Ordinal);

        // no diagnostics, and the type body folds
        Assert.Empty(DiagnosticService.GetSyntaxDiagnostics(document).Concat(DiagnosticService.GetSemanticDiagnostics(document, ct)).Select(d => d.ToText()));
        Assert.Contains(FoldingService.GetFoldingRanges(document.SyntaxTree), r => r.Kind == FoldingKind.Type);
    }
}
