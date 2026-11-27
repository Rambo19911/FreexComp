namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §6 (CMP-730, 731, C-136): one case per row of CMP-731.</summary>
[Trait("Phase", "3")]
public sealed class QuickInfoTests
{
    private const string Unit1 = """
        unit U1;
        interface
        type
          /// The base class.
          TBar = class
          end;
          TFoo = class(TBar)
          public
            Count: Integer;
            procedure Bar; virtual;
            property Name: Integer read Count write Count;
          end;
          TRec = record
            A, B: Int64;
          end;
          TColor = (Red, Green, Blue);
          TMany = (M1, M2, M3, M4, M5, M6, M7, M8, M9);
          TInt = Integer;
        const
          Answer = 42;
          Typed: Integer = 7;
        var
          Global: Integer;
        {** Doubles A.
            Ignores B. }
        function Twice(A: Integer; const B: string = ''): Boolean;
        implementation
        procedure TFoo.Bar; begin end;
        function Twice(A: Integer; const B: string): Boolean; begin Result := A > 0 end;
        end.
        """;

    private const string Program = """
        program P;
        uses U1;
        var F: TFoo; R: TRec; C: TColor; M: TMany; I: TInt;
        procedure Local(var X: Integer);
        var L: Integer;
        begin
          L := X;
        end;
        begin
          F.Bar;
          F.Count := 1;
          F.Name := 2;
          Twice(Answer, 'x');
          Global := Typed;
          C := Red;
        end.
        """;

    /// <summary>Hover at the <c>$$</c> of <paramref name="marker"/>, a piece of the program text with the caret inside.</summary>
    private static QuickInfo Hover(string marker)
    {
        var program = Program.ReplaceLineEndings("\n");
        var caret = marker.IndexOf("$$", StringComparison.Ordinal);
        var at = program.IndexOf(marker.Remove(caret, 2), StringComparison.Ordinal);
        Assert.True(at >= 0, marker);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", program), ("U1.pas", Unit1.ReplaceLineEndings("\n")));
        var info = QuickInfoService.GetQuickInfo(doc, at + caret, TestContext.Current.CancellationToken);
        Assert.NotNull(info);
        return info;
    }

    [Theory]
    [Trait("Req", "CMP-731")]
    [InlineData("L$$ := X", "var L: Integer", "P")]
    [InlineData("Glo$$bal :=", "var Global: Integer", "U1")]
    [InlineData("F.Co$$unt", "field TFoo.Count: Integer", "U1.TFoo")]
    [InlineData("L := $$X", "param var X: Integer", "P")]
    public void VariableFieldParameter(string marker, string signature, string declaredIn)
    {
        var info = Hover(marker);

        Assert.Equal(signature, info.Signature);
        Assert.Equal(declaredIn, info.DeclaredIn);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Constant_ValueOrType()
    {
        var answer = Hover("Twice(Ans$$wer");
        Assert.Equal("const Answer = 42", answer.Signature);
        Assert.Equal("42", answer.ConstantValue);

        Assert.Equal("const Typed: Integer = …", Hover("Global := Ty$$ped").Signature);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Routine_QualifiedWithDefaultsAndDocumentation()
    {
        var info = Hover("Tw$$ice(");

        Assert.Equal("function U1.Twice(A: Integer; const B: string = ''): Boolean", info.Signature);
        Assert.Equal("Doubles A.\nIgnores B.", info.Documentation);
        Assert.Equal("U1", info.DeclaredIn);
        Assert.Equal(TextSpan(Program, "Twice("), info.Span.Start);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Method_WithItsDirective()
    {
        Assert.Equal("procedure TFoo.Bar; virtual", Hover("F.B$$ar").Signature);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Property_ListsItsAccessors()
    {
        var info = Hover("F.Na$$me");

        Assert.Equal("property TFoo.Name: Integer read write", info.Signature);
        Assert.Equal("U1.TFoo", info.DeclaredIn);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Types_ClassRecordEnumAlias()
    {
        var foo = Hover("F: TF$$oo");
        Assert.Equal("type TFoo = class(TBar)", foo.Signature);

        var record = Hover("R: TR$$ec");
        Assert.Equal("type TRec = record", record.Signature);
        Assert.Equal("SizeOf = 16", record.ConstantValue);

        Assert.Equal("type TColor = (Red, Green, Blue)", Hover("C: TCo$$lor").Signature);
        Assert.Equal("type TMany = (M1, M2, M3, M4, M5, M6, M7, M8, …)", Hover("M: TMa$$ny").Signature);
        Assert.Equal("type TInt = Integer", Hover("I: TI$$nt").Signature);
    }

    [Fact]
    [Trait("Req", "CMP-730")]
    public void Type_Documentation()
    {
        Assert.Null(Hover("F: TF$$oo").Documentation);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", Program.ReplaceLineEndings("\n").Replace("TFoo; R", "TBar; R", StringComparison.Ordinal)),
            ("U1.pas", Unit1.ReplaceLineEndings("\n")));
        var text = doc.Text.Text;
        var info = QuickInfoService.GetQuickInfo(doc, text.IndexOf("TBar", StringComparison.Ordinal) + 1, TestContext.Current.CancellationToken);
        Assert.Equal("The base class.", info!.Documentation);
        Assert.Equal("type TBar = class(TObject)", info.Signature);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void EnumMember()
    {
        Assert.Equal("TColor.Red = 0", Hover("C := R$$ed").Signature);
    }

    [Fact]
    [Trait("Req", "CMP-731")]
    public void Unit_WithItsPath()
    {
        var info = Hover("uses U$$1");

        Assert.Equal("unit U1", info.Signature);
        Assert.Equal(TestFiles.PathOf("U1.pas"), info.Documentation);
    }

    [Theory]
    [Trait("Req", "CMP-731")]
    [InlineData("be$$gin\n  L")]
    [InlineData("F.Bar;$$")]
    public void NothingToShow_Null(string marker)
    {
        var program = Program.ReplaceLineEndings("\n");
        var caret = marker.IndexOf("$$", StringComparison.Ordinal);
        var at = program.IndexOf(marker.Remove(caret, 2), StringComparison.Ordinal);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", program), ("U1.pas", Unit1.ReplaceLineEndings("\n")));

        Assert.Null(QuickInfoService.GetQuickInfo(doc, at + caret, TestContext.Current.CancellationToken));
    }

    private static int TextSpan(string text, string piece) => text.ReplaceLineEndings("\n").IndexOf(piece, StringComparison.Ordinal);

    /// <summary>CMP-1394 (C-347): hover on each segment of a qualified generic name shows that segment's symbol with its display name.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void NestedTypeOfInstance_DisplayName()
    {
        var doc = GenericNamesFixture.Open();
        var text = GenericNamesFixture.Program;
        QuickInfo At(string piece, string segment, int occurrence = 1)
        {
            var info = QuickInfoService.GetQuickInfo(doc, GenericNamesFixture.Offset(text, piece, occurrence) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                                     TestContext.Current.CancellationToken);
            Assert.NotNull(info);
            return info;
        }

        var nested = At("TList<Integer>.TEnumerator;", "TEnumerator");
        Assert.Equal("type TList<Integer>.TEnumerator = class(TObject)", nested.Signature);
        Assert.Equal("UG.TList<Integer>", nested.DeclaredIn);
        Assert.Equal("type TList<string>.TEnumerator = class(TObject)", At("UG.TList<string>.TEnumerator", "TEnumerator").Signature);
        Assert.Equal("type TList<Integer> = class(TObject)", At("TList<Integer>.TEnumerator;", "TList").Signature);
        Assert.Equal("unit UG", At("UG.TOuter.TBox<Integer>", "UG").Signature);
        Assert.Equal("type TOuter = class(TObject)", At("UG.TOuter.TBox<Integer>", "TOuter").Signature);
        Assert.Equal("type TOuter.TBox<Integer> = class(TObject)", At("UG.TOuter.TBox<Integer>", "TBox").Signature);
        Assert.Equal("type TOuter.TInner = class(TObject)", At("I: TOuter.TInner", "TInner").Signature);

        // at its declaration inside the generic, the nested type is shown as the generic's
        var unit = GenericNamesFixture.Open("UG.pas");
        var declared = QuickInfoService.GetQuickInfo(unit, GenericNamesFixture.Offset(GenericNamesFixture.Unit, "TEnumerator = class") + 1, TestContext.Current.CancellationToken);
        Assert.Equal("type TList<T>.TEnumerator = class(TObject)", declared?.Signature);
    }

    /// <summary>C-355: a nested type of a generic nothing instantiates is shown with the generic's name, at its declaration and its uses.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_NestedType()
    {
        var doc = GenericNamesFixture.OpenUninstantiated();
        var text = GenericNamesFixture.Uninstantiated;
        string? At(string piece, string segment) =>
            QuickInfoService.GetQuickInfo(doc, GenericNamesFixture.Offset(text, piece) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                          TestContext.Current.CancellationToken)?.Signature;

        Assert.Equal("type TStack<T>.TNode = class(TObject)", At("TNode = class", "TNode"));
        Assert.Equal("type TStack<T>.TNode = class(TObject)", At("Top: TNode", "TNode"));
        Assert.Equal("type TStack<T>.TNode = class(TObject)", At("TStack<T>.TNode.IsLast", "TNode"));
        Assert.Null(At("Value: T", "Value"));                                    // C-347: no hover for a member at its declaration
    }

    /// <summary>
    /// CMP-1400 (C-377): hover on a class constructor's (destructor's) declaration or implementation heading shows
    /// <c>class constructor TA.Create</c>; hover on a call <c>TA.Create(1)</c> shows the ordinary constructor (LANG-787).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void ClassConstructorHeading()
    {
        var unit = ClassConstructorFixture.Unit;
        var program = ClassConstructorFixture.Program;
        QuickInfo? At(string file, string text, string piece, string name)
        {
            var doc = MarkupTestFile.Open(file, ("P.pas", program), ("UC.pas", unit));
            var at = ClassConstructorFixture.IndexOf(text, piece) + piece.IndexOf(name, StringComparison.Ordinal) + 1;
            return QuickInfoService.GetQuickInfo(doc, at, TestContext.Current.CancellationToken);
        }

        string? Signature(string piece, string name) => At("UC.pas", unit, piece, name)?.Signature;

        Assert.Equal("class constructor TA.Create", Signature("class constructor Create;", "Create"));
        Assert.Equal("class constructor TA.Create", Signature("class constructor TA.Create;", "Create"));
        Assert.Equal("class destructor TA.Teardown", Signature("class destructor Teardown;", "Teardown"));
        Assert.Equal("class destructor TA.Teardown", Signature("class destructor TA.Teardown;", "Teardown"));
        Assert.Equal("class constructor TR.Init", Signature("class constructor Init;", "Init"));
        Assert.Equal("class constructor TR.Init", Signature("class constructor TR.Init;", "Init"));
        Assert.Equal("constructor TA.Create(N: Integer)", Signature("constructor Create(N: Integer);", "Create"));
        Assert.Equal("UC.TA", At("UC.pas", unit, "class constructor Create;", "Create")?.DeclaredIn);

        Assert.Equal("constructor TA.Create(N: Integer)", At("P.pas", program, "TA.Create(1)", "Create")?.Signature);
    }
}
