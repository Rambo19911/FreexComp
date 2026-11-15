using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §7 (CMP-740, 741, C-137): declaration ↔ implementation toggle, across units, from <c>.pcu</c>, uses entry.</summary>
[Trait("Phase", "3")]
public sealed class GoToDefinitionTests
{
    private const string Unit1 = """
        unit U1;
        interface
        type
          IShape = interface
            function Area: Integer;
          end;
          TBase = class(TInterfacedObject, IShape)
            function Area: Integer; virtual;
          end;
          TSquare = class(TBase)
            function Area: Integer; override;
          end;
          TCube = class(TSquare)
            function Area: Integer; override;
          end;
        function Twice(A: Integer): Integer;
        const Answer = 42;
        implementation
        function TBase.Area: Integer; begin Result := 0 end;
        function TSquare.Area: Integer; begin Result := 1 end;
        function TCube.Area: Integer; begin Result := 2 end;
        function Twice(A: Integer): Integer;
        begin
          Result := A * 2;
        end;
        end.
        """;

    private const string Program = """
        program P;
        uses U1;
        procedure Local; forward;
        procedure Local;
        begin
        end;
        var B: TBase; S: IShape;
        begin
          Local;
          Writeln(Twice(Answer), B.Area, S.Area);
        end.
        """;

    private static readonly string U1Text = Unit1.ReplaceLineEndings("\n");

    private static readonly string PText = Program.ReplaceLineEndings("\n");

    private static Document Doc(string path = "P.pas") => MarkupTestFile.Open(path, ("P.pas", PText), ("U1.pas", U1Text));

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="piece"/> in <paramref name="text"/>, plus 1 (inside the name).</summary>
    private static int At(string text, string piece, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
        {
            at = text.IndexOf(piece, at + 1, StringComparison.Ordinal);
        }

        Assert.True(at >= 0, piece);
        return at + 1;
    }

    private static (string Path, int Start) Single(IEnumerable<Location> locations)
    {
        var location = Assert.Single(locations);
        return (location.Path, location.Span.Start);
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void Reference_ToARoutine_GoesToItsImplementation_AcrossUnits()
    {
        var target = NavigationService.GoToDefinition(Doc(), At(PText, "Twice("), TestContext.Current.CancellationToken);

        Assert.Equal((TestFiles.PathOf("U1.pas"), U1Text.IndexOf("Twice", U1Text.IndexOf("implementation", StringComparison.Ordinal), StringComparison.Ordinal)),
            Single(target));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void InterfaceHeading_And_ImplementationHeading_Toggle()
    {
        var doc = Doc("U1.pas");
        var implementation = U1Text.IndexOf("Twice", U1Text.IndexOf("implementation", StringComparison.Ordinal), StringComparison.Ordinal);
        var declaration = U1Text.IndexOf("Twice", StringComparison.Ordinal);

        Assert.Equal((TestFiles.PathOf("U1.pas"), implementation), Single(NavigationService.GoToDefinition(doc, declaration + 1, TestContext.Current.CancellationToken)));
        Assert.Equal((TestFiles.PathOf("U1.pas"), declaration), Single(NavigationService.GoToDefinition(doc, implementation + 1, TestContext.Current.CancellationToken)));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void Method_ClassDeclaration_ToggleWithImplementation()
    {
        var doc = Doc("U1.pas");
        var declared = U1Text.IndexOf("Area: Integer; virtual", StringComparison.Ordinal);
        var implemented = U1Text.IndexOf("Area", U1Text.IndexOf("TBase.Area", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.Equal((TestFiles.PathOf("U1.pas"), implemented), Single(NavigationService.GoToDefinition(doc, declared + 1, TestContext.Current.CancellationToken)));
        Assert.Equal((TestFiles.PathOf("U1.pas"), declared), Single(NavigationService.GoToDefinition(doc, implemented + 1, TestContext.Current.CancellationToken)));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void ForwardRoutine_InTheSameFile()
    {
        var target = NavigationService.GoToDefinition(Doc(), At(PText, "Local;", 3), TestContext.Current.CancellationToken);

        Assert.Equal((TestFiles.PathOf("P.pas"), At(PText, "Local;", 2) - 1), Single(target));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void OtherSymbols_TheDeclaringIdentifier()
    {
        var constant = NavigationService.GoToDefinition(Doc(), At(PText, "Answer"), TestContext.Current.CancellationToken);
        Assert.Equal((TestFiles.PathOf("U1.pas"), U1Text.IndexOf("Answer", StringComparison.Ordinal)), Single(constant));

        var variable = NavigationService.GoToDefinition(Doc(), At(PText, "B.Area"), TestContext.Current.CancellationToken);
        Assert.Equal((TestFiles.PathOf("P.pas"), PText.IndexOf("B: TBase", StringComparison.Ordinal)), Single(variable));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void UsesEntry_TheStartOfTheUnitFile()
    {
        var target = Assert.Single(NavigationService.GoToDefinition(Doc(), At(PText, "U1;"), TestContext.Current.CancellationToken));

        Assert.Equal((TestFiles.PathOf("U1.pas"), 0, 0), (target.Path, target.Span.Start, target.LineSpan.Start.Line));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void BuiltIns_Empty()
    {
        Assert.Empty(NavigationService.GoToDefinition(Doc(), At(PText, "Writeln"), TestContext.Current.CancellationToken));
        Assert.Empty(NavigationService.GoToDefinition(Doc(), At(PText, "begin\n  Local") - 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Req", "CMP-740")]
    public void FromAPcu_TheRecordedDeclarationInTheSourceFile()
    {
        // U1 is built to a .pcu in C:\lib; its source stays in C:\p, the program lives in C:\q and finds only the .pcu
        var source = TestFiles.Files(("P.pas", PText), ("U1.pas", U1Text));
        var pcu = Compilation.Create(TestFiles.Options(), [], source).PcuTexts().Single(t => t.Unit == "U1").Text;
        var fs = TestFiles.Files(("U1.pas", U1Text));
        fs.Add(@"C:\lib\U1.pcu", pcu).Add(@"C:\lib\U1.obj", "").Add(@"C:\q\P.pas", PText);
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
        var doc = workspace.GetDocument(@"C:\q\P.pas");
        Assert.DoesNotContain(doc.Compilation.SourceUnitPaths, p => p.EndsWith("U1.pas", StringComparison.OrdinalIgnoreCase));   // imported from the .pcu

        var target = Assert.Single(NavigationService.GoToDefinition(doc, At(PText, "Twice("), TestContext.Current.CancellationToken));

        Assert.Equal((TestFiles.PathOf("U1.pas"), U1Text.IndexOf("Twice", StringComparison.Ordinal)), (target.Path, target.Span.Start));
        Assert.True(target.HasPosition);
        Assert.Equal(U1Text[..target.Span.Start].Count(c => c == '\n'), target.LineSpan.Start.Line);
    }

    [Fact]
    [Trait("Req", "CMP-741")]
    public void Implementation_OfAVirtualMethod_AllOverrides()
    {
        var targets = NavigationService.GoToImplementation(Doc(), At(PText, "B.Area") + 2, TestContext.Current.CancellationToken);

        Assert.Equal(
            [U1Text.IndexOf("Area", U1Text.IndexOf("TSquare.Area", StringComparison.Ordinal), StringComparison.Ordinal),
             U1Text.IndexOf("Area", U1Text.IndexOf("TCube.Area", StringComparison.Ordinal), StringComparison.Ordinal)],
            targets.Select(t => t.Span.Start));
    }

    [Fact]
    [Trait("Req", "CMP-741")]
    public void Implementation_OfAnInterfaceMethod_TheImplementingMethods()
    {
        var targets = NavigationService.GoToImplementation(Doc(), At(PText, "S.Area") + 2, TestContext.Current.CancellationToken);

        Assert.Equal([U1Text.IndexOf("Area", U1Text.IndexOf("TBase.Area", StringComparison.Ordinal), StringComparison.Ordinal)], targets.Select(t => t.Span.Start));
    }

    [Fact]
    [Trait("Req", "CMP-741")]
    public void Implementation_OfATypeOrInterface_DescendantsAndImplementers()
    {
        var doc = Doc("U1.pas");
        var square = U1Text.IndexOf("TSquare = ", StringComparison.Ordinal);
        var cube = U1Text.IndexOf("TCube = ", StringComparison.Ordinal);

        Assert.Equal([square, cube], NavigationService.GoToImplementation(doc, U1Text.IndexOf("TBase", StringComparison.Ordinal) + 1, TestContext.Current.CancellationToken)
            .Select(t => t.Span.Start));
        Assert.Equal([U1Text.IndexOf("TBase", StringComparison.Ordinal), square, cube],
            NavigationService.GoToImplementation(doc, U1Text.IndexOf("IShape", StringComparison.Ordinal) + 1, TestContext.Current.CancellationToken).Select(t => t.Span.Start));
    }
}
