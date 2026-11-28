namespace FreexComp.LanguageService.Tests;

// CMP-1495 (C-446; phase8/helpers-completion.md §9, §15.9): go to definition from every form of reference to a helper member.
public sealed partial class GoToDefinitionTests
{
    private const string HelperForms = """
        program P;
        uses UH;
        type
          TGrand = class(TChild)
            procedure Go;
          end;
          TCounterMoreHelper = class helper(TCounterHelper) for TCounter
            procedure Show;
          end;
        procedure TGrand.Go;
        begin
          Bump;
        end;
        procedure TCounterMoreHelper.Show;
        begin
          inherited Show;
        end;
        var
          D: TChild;
          X: Integer;
        begin
          D.Bump;
          X := 5.Plus(1);
          X := 5.Plus(1, 2);
          D := TChild(TChild.Make);
          D := TChild(TCounterHelper.Make);
          with D do Bump;
          X := D.Size + TChild.Count;
        end.
        """;

    /// <summary>
    /// CMP-1495: from a value of a descendant, a literal, a type name, the helper's name, implicit <c>Self</c>, <c>with</c> and
    /// <c>inherited</c> in a descendant helper, the definition is the helper member — the implementation heading of a method (the
    /// overload the call binds to), the declaration of a property and of a class var.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1495")]
    public void HelperMember_FromEveryFormOfReference()
    {
        var program = HelperForms.ReplaceLineEndings("\n");
        var unit = HelpersFixture.Unit.ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", program), HelpersFixture.UnitFile);
        (string Path, int Start) From(string piece, string segment, int occurrence = 1) =>
            Single(NavigationService.GoToDefinition(doc, At(program, piece, occurrence) - 1 + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                TestContext.Current.CancellationToken));
        (string, int) InUH(string piece, string segment) =>
            (TestFiles.PathOf("UH.pas"), unit.IndexOf(piece, StringComparison.Ordinal) + piece.IndexOf(segment, StringComparison.Ordinal));

        var bump = InUH("procedure TCounterHelper.Bump; begin", "Bump");
        Assert.Equal(bump, From("D.Bump;", "Bump"));                                         // a value of a descendant
        Assert.Equal(bump, From("  Bump;\nend;", "Bump"));                                   // implicit Self in a method of a descendant
        Assert.Equal(bump, From("with D do Bump", "Bump"));
        Assert.Equal(InUH("function TIntH.Plus(A: Integer): Integer; begin", "Plus"), From("5.Plus(1);", "Plus"));            // a literal
        Assert.Equal(InUH("function TIntH.Plus(A, B: Integer): Integer; begin", "Plus"), From("5.Plus(1, 2);", "Plus"));
        var make = InUH("class function TCounterHelper.Make: TCounter; begin", "Make");
        Assert.Equal(make, From("TChild.Make", "Make"));                                     // a type name
        Assert.Equal(make, From("TCounterHelper.Make", "Make"));                             // the helper's name
        Assert.Equal(InUH("procedure TCounterHelper.Show; begin", "Show"), From("inherited Show", "Show"));   // inherited in a descendant helper
        Assert.Equal(InUH("property Size: Integer", "Size"), From("D.Size", "Size"));        // a property
        Assert.Equal(InUH("Count: Integer;", "Count"), From("TChild.Count", "Count"));       // a class var
    }
}
