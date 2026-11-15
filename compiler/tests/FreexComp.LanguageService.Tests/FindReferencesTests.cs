using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// language-service.md §7 (CMP-742, 744, C-138): local, field, method with overrides, across units, read/write kinds; document
/// highlights; workspace symbols; references across a body-only edit snapshot (C-242).
/// </summary>
[Trait("Phase", "3")]
public sealed class FindReferencesTests
{
    private const string Unit1 = """
        unit U1;
        interface
        type
          TBase = class
          public
            Count: Integer;
            procedure Run; virtual;
          end;
          TDerived = class(TBase)
            procedure Run; override;
          end;
        function Twice(A: Integer): Integer;
        implementation
        procedure TBase.Run; begin Count := Count + 1 end;
        procedure TDerived.Run; begin inherited Run end;
        function Twice(A: Integer): Integer;
        begin
          Result := A * 2;
        end;
        end.
        """;

    private const string Program = """
        program P;
        uses U1;
        procedure Work;
        var Total: Integer;
        begin
          Total := 1;
          Total := Total + Twice(Total);
          Writeln(Total);
        end;
        var B: TBase; D: TDerived;
        begin
          B.Count := 2;
          Writeln(B.Count);
          B.Run;
          D.Run;
          Work;
        end.
        """;

    private static readonly string U1Text = Unit1.ReplaceLineEndings("\n");

    private static readonly string PText = Program.ReplaceLineEndings("\n");

    private static Document Doc(string path = "P.pas") => MarkupTestFile.Open(path, ("P.pas", PText), ("U1.pas", U1Text));

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

    /// <summary>"file:line:kind" per reference (line 0-based), in result order.</summary>
    private static string[] Describe(IEnumerable<ReferenceLocation> references) =>
        [.. references.Select(r => $"{Path.GetFileName(r.Location.Path)}:{r.Location.LineSpan.Start.Line}:{r.Kind}")];

    [Fact]
    [Trait("Req", "CMP-742")]
    public void Local_ReadWriteKinds_DeclarationFirst()
    {
        var references = NavigationService.FindReferences(Doc(), At(PText, "Total :=") + 1, TestContext.Current.CancellationToken);

        Assert.Equal(["P.pas:3:Declaration", "P.pas:5:Write", "P.pas:6:Write", "P.pas:6:Read", "P.pas:6:Read", "P.pas:7:Read"], Describe(references));
        Assert.All(references, r => Assert.Equal("Total", PText.Substring(r.Location.Span.Start, r.Location.Span.Length)));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void Field_AcrossUnits()
    {
        var references = NavigationService.FindReferences(Doc(), At(PText, "Count") + 1, TestContext.Current.CancellationToken);

        Assert.Equal(["U1.pas:5:Declaration", "P.pas:11:Write", "P.pas:12:Read", "U1.pas:13:Write", "U1.pas:13:Read"], Describe(references));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void Method_TheWholeOverrideChain()
    {
        var references = NavigationService.FindReferences(Doc(), At(PText, "D.Run") + 2, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["U1.pas:6:Declaration", "U1.pas:9:Declaration", "P.pas:13:Call", "P.pas:14:Call", "U1.pas:13:Implementation", "U1.pas:14:Implementation", "U1.pas:14:Call"],
            Describe(references));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void Routine_CallsAndBothHeadings()
    {
        var references = NavigationService.FindReferences(Doc("U1.pas"), At(U1Text, "Twice") + 1, TestContext.Current.CancellationToken);

        Assert.Equal(["U1.pas:11:Declaration", "P.pas:6:Call", "U1.pas:15:Implementation"], Describe(references));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void AcrossABodyOnlyEditSnapshot_TheTakenOverUnitStillMatches()
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", PText), ("U1.pas", U1Text)));
        workspace.SetProject(TestFiles.Options());
        _ = workspace.GetDocument(TestFiles.PathOf("P.pas")).SemanticModel;           // the baseline's analysis exists
        workspace.UpdateDocument(TestFiles.PathOf("U1.pas"), U1Text.Replace("A * 2", "A * 2 + 0", StringComparison.Ordinal));
        var doc = workspace.GetDocument(TestFiles.PathOf("U1.pas"));
        Assert.Equal(["U1"], doc.Compilation.BoundUnits);

        var references = NavigationService.FindReferences(doc, At(U1Text, "Twice") + 1, TestContext.Current.CancellationToken);

        Assert.Equal(["U1.pas:11:Declaration", "P.pas:6:Call", "U1.pas:15:Implementation"], Describe(references));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void BuiltIn_OnlyTheReferences()
    {
        var references = NavigationService.FindReferences(Doc(), At(PText, "Writeln") + 1, TestContext.Current.CancellationToken);

        Assert.Equal(["P.pas:7:Call", "P.pas:12:Call"], Describe(references).Where(r => r.StartsWith("P.pas", StringComparison.Ordinal)));
        Assert.DoesNotContain(references, r => r.Kind == ReferenceKind.Declaration);           // no declaration site: System uses it too
    }

    [Fact]
    [Trait("Req", "CMP-744")]
    public void DocumentHighlights_TheSymbolInThisDocument()
    {
        var highlights = NavigationService.GetDocumentHighlights(Doc(), At(PText, "B.Count") + 3, TestContext.Current.CancellationToken);

        Assert.Equal([(At(PText, "Count"), ReferenceKind.Write), (At(PText, "Count", 2), ReferenceKind.Read)], highlights.Select(h => (h.Span.Start, h.Kind)));
    }

    [Fact]
    [Trait("Req", "CMP-744")]
    public void DocumentHighlights_KeywordPair()
    {
        var begin = At(PText, "begin\n  Total");
        var end = At(PText, "end;");

        var highlights = NavigationService.GetDocumentHighlights(Doc(), begin + 1, TestContext.Current.CancellationToken);

        Assert.Equal([begin, end], highlights.Select(h => h.Span.Start));
    }

    [Fact]
    [Trait("Req", "CMP-742")]
    public void FindSymbols_ByNameAcrossTheWorkspace()
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", PText), ("U1.pas", U1Text)));
        workspace.SetProject(TestFiles.Options());

        var symbols = NavigationService.FindSymbols(workspace, "tba", 10, TestContext.Current.CancellationToken);

        var base_ = Assert.Single(symbols, s => s.Name == "TBase");
        Assert.Equal(("U1", CompletionItemKind.Class, TestFiles.PathOf("U1.pas"), At(U1Text, "TBase")),
            (base_.ContainerName, base_.Kind, base_.Location.Path, base_.Location.Span.Start));
        Assert.Contains(NavigationService.FindSymbols(workspace, "run", 10, TestContext.Current.CancellationToken), s => s is { Name: "Run", ContainerName: "U1.TDerived" });
        Assert.Single(NavigationService.FindSymbols(workspace, "o", 1, TestContext.Current.CancellationToken));
    }
}
