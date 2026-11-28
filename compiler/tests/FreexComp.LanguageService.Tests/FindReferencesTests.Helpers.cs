namespace FreexComp.LanguageService.Tests;

// CMP-1495 (C-446; phase8/helpers-completion.md §9, §15.9): references of helper members.
public sealed partial class FindReferencesTests
{
    private const string HelperUser = """
        unit U2;
        interface
        uses UH;
        procedure Call(D: TChild);
        implementation
        procedure Call(D: TChild);
        begin
          D.Show;
        end;
        end.
        """;

    private const string HelperProgram = """
        program P;
        uses UH, U2;
        type
          TGrand = class(TChild)
            procedure Go;
          end;
        procedure TGrand.Go;
        begin
          Show;
        end;
        var
          D: TChild;
        begin
          D.Show;
          with D do
            Show;
          Call(D);
        end.
        """;

    /// <summary>
    /// CMP-1495: the references of a helper member are its declaration, its implementation heading and every reference bound to it — in
    /// the helper's unit (implicit <c>Self</c> in a method of the helped class's descendant), in another unit, through a value, through
    /// <c>with</c> and implicit <c>Self</c> in the program — from any of them.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1495")]
    public void HelperMember_AllFormsAcrossUnits()
    {
        var program = HelperProgram.ReplaceLineEndings("\n");
        var unit = HelpersFixture.Unit.ReplaceLineEndings("\n");
        (string, string)[] files = [("P.pas", program), HelpersFixture.UnitFile, ("U2.pas", HelperUser.ReplaceLineEndings("\n"))];
        string[] From(string file, string text, string piece) =>
            Describe(NavigationService.FindReferences(MarkupTestFile.Open(file, files), At(text, piece) + piece.IndexOf("Show", StringComparison.Ordinal) + 1,
                TestContext.Current.CancellationToken)).Order(StringComparer.Ordinal).ToArray();

        string[] expected =
        [
            "P.pas:13:Call", "P.pas:15:Call", "P.pas:8:Call",
            "U2.pas:7:Call",
            "UH.pas:25:Declaration", "UH.pas:59:Call", "UH.pas:63:Implementation",
        ];
        Assert.Equal(expected, From("P.pas", program, "D.Show;"));
        Assert.Equal(expected, From("P.pas", program, "    Show;"));
        Assert.Equal(expected, From("UH.pas", unit, "procedure Show;"));
        Assert.Equal(expected, From("UH.pas", unit, "procedure TCounterHelper.Show;"));
    }

    /// <summary>
    /// CMP-1495: a member of the helped type that a helper member hides is another symbol — its references are the calls made where
    /// the helper is not active (a program that does not use the helper's unit), never those that bind to the helper member.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1495")]
    public void HiddenMemberOfTheHelpedType_IsAnotherSymbol()
    {
        const string ua = "unit UA;\ninterface\ntype\n  TFoo = class\n    procedure Bump;\n  end;\nimplementation\nprocedure TFoo.Bump; begin end;\nend.\n";
        const string ub = "unit UB;\ninterface\nuses UA;\ntype\n  TFooHelper = class helper for TFoo\n    procedure Bump;\n  end;\nimplementation\nprocedure TFooHelper.Bump; begin end;\nend.\n";
        const string u2 = "unit U2;\ninterface\nuses UA, UB;\nprocedure Call(F: TFoo);\nimplementation\nprocedure Call(F: TFoo);\nbegin\n  F.Bump;\nend;\nend.\n";
        const string p = "program P;\nuses UA, U2;\nvar\n  F: TFoo;\nbegin\n  F.Bump;\n  Call(F);\nend.\n";
        (string, string)[] files = [("P.pas", p), ("UA.pas", ua), ("UB.pas", ub), ("U2.pas", u2)];
        string[] From(string file, string text, string piece) =>
            Describe(NavigationService.FindReferences(MarkupTestFile.Open(file, files), At(text, piece) + piece.IndexOf("Bump", StringComparison.Ordinal) + 1,
                TestContext.Current.CancellationToken)).Order(StringComparer.Ordinal).ToArray();

        string[] own = ["P.pas:5:Call", "UA.pas:4:Declaration", "UA.pas:7:Implementation"];
        string[] helper = ["U2.pas:7:Call", "UB.pas:5:Declaration", "UB.pas:8:Implementation"];
        Assert.Equal(own, From("P.pas", p, "F.Bump"));
        Assert.Equal(own, From("UA.pas", ua, "procedure Bump"));
        Assert.Equal(helper, From("U2.pas", u2, "F.Bump"));
        Assert.Equal(helper, From("UB.pas", ub, "procedure Bump"));
    }

    /// <summary>
    /// CMP-1495 (C-446; finding (a) of C-442): a read of a helper property whose accessor is a field or a method of the helped type
    /// (LANG-990 (d)) is a reference of the property, not of the accessor.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1495")]
    public void HelperProperty_OverAMemberOfTheHelpedType()
    {
        const string text = """
            program P;
            type
              TFoo = class
                FValue: Integer;
                function GetDouble: Integer;
              end;
              TFooHelper = class helper for TFoo
                property Value: Integer read FValue;
                property Doubled: Integer read GetDouble;
              end;
            function TFoo.GetDouble: Integer; begin Result := FValue * 2 end;
            var
              F: TFoo;
            begin
              Writeln(F.Value, F.Doubled);
            end.
            """;
        var program = text.ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", program));
        string[] From(string piece) => Describe(NavigationService.FindReferences(doc, At(program, piece) + 1, TestContext.Current.CancellationToken));

        Assert.Equal(["P.pas:7:Declaration", "P.pas:14:Read"], From("Value: Integer read"));
        Assert.Equal(["P.pas:8:Declaration", "P.pas:14:Read"], From("Doubled:"));
        Assert.Equal(["P.pas:3:Declaration", "P.pas:7:Read", "P.pas:10:Read"], From("FValue:"));
    }
}
