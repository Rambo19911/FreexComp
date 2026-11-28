using FreexComp.Compiler;

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
    /// C-421 (LANG-982): hover on a type parameter of a generic nothing instantiates shows its constraints — the keywords in the
    /// order <c>class</c>, <c>record</c>, <c>constructor</c>, then the constraint types as written; a shared list for each of its
    /// names; nothing after the name without constraints — in the type parameter list, in the declaration, in method headings and
    /// in bodies; the same for a type parameter of a generic method in its headings. Completion after a value of it offers the
    /// members its constraints give (<c>TObject</c>'s for <c>class</c>, none for <c>record</c>).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-982")]
    public void UninstantiatedGeneric_TypeParameterShowsConstraints()
    {
        var text = """
            unit UO;
            interface
            type
              TThing = class
              public
                Name: string;
              end;
              IShape = interface
                function Area: Integer;
              end;
              TMulti<T: IShape, constructor, TThing, class; R: record; K, L: class; U> = class
              public
                A: T;
                B: R;
                C: K;
                D: L;
                E: U;
                procedure Use(const V: T);
                class function Pick<X: class>(const P: X): X;
              end;
              TUtil = class
              public
                class function Larger<N: record; M>(const A, B: N; const C: M): N;
              end;
            implementation
            class function TUtil.Larger<N, M>(const A, B: N; const C: M): N;
            begin
              Result := A;
            end;
            class function TMulti<T, R, K, L, U>.Pick<X>(const P: X): X;
            begin
              Result := P;
            end;
            procedure TMulti<T, R, K, L, U>.Use(const V: T);
            var
              Held: T;
            begin
              A.Name := '';
              C.Free;
              B.Size;
            end;
            end.
            """.ReplaceLineEndings("\n");
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        string? At(string piece, string segment) =>
            QuickInfoService.GetQuickInfo(doc, GenericNamesFixture.Offset(text, piece) + piece.LastIndexOf(segment, StringComparison.Ordinal) + 1,
                                          TestContext.Current.CancellationToken)?.Signature;
        string[] After(string piece)
        {
            var caret = GenericNamesFixture.Offset(text, piece) + piece.Length;
            return [.. CompletionService.GetCompletions(doc, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), TestContext.Current.CancellationToken)
                .Items.Select(i => i.Label)];
        }

        Assert.Equal("type T: class, constructor, IShape, TThing", At("A: T", "T"));
        Assert.Equal("type R: record", At("B: R", "R"));
        Assert.Equal("type K: class", At("C: K", "K"));
        Assert.Equal("type L: class", At("D: L", "L"));
        Assert.Equal("type U", At("E: U", "U"));
        // in a method heading of the declaration and of the implementation, and in the body
        Assert.Equal("type T: class, constructor, IShape, TThing", At("procedure Use(const V: T)", "T"));
        Assert.Equal("type T: class, constructor, IShape, TThing", At(".Use(const V: T)", "T"));
        Assert.Equal("type T: class, constructor, IShape, TThing", At("Held: T", "T"));
        Assert.Equal("type T: class, constructor, IShape, TThing", At("TMulti<T: IShape", "T"));              // in the list itself
        Assert.Equal("type T: class, constructor, IShape, TThing", At("procedure TMulti<T, R", "T"));
        // a type parameter of a generic method, in its headings
        Assert.Equal("type X: class", At("Pick<X: class>(const P: X): X;", "X"));
        Assert.Equal("type X: class", At("Pick<X: class>(const P: X)", "X"));
        Assert.Equal("type X: class", At("Pick<X: class", "X"));
        Assert.Equal("type X: class", At(".Pick<X>(const P: X): X;", "X"));
        Assert.Equal("type X: class", At(".Pick<X", "X"));
        Assert.Equal("type N: record", At("Larger<N: record; M>(const A, B: N", "N"));          // of a type that is no generic
        Assert.Equal("type M", At("Larger<N: record; M>(const A, B: N; const C: M", "M"));
        Assert.Equal("type N: record", At("TUtil.Larger<N, M>(const A, B: N; const C: M): N", "N"));

        Assert.Contains("Name", After("  A."));
        Assert.Contains("Area", After("  A."));
        Assert.Contains("Free", After("  C."));
        Assert.DoesNotContain("Name", After("  C."));
        Assert.Empty(After("  B."));
    }

    /// <summary>
    /// C-422 (LANG-982): hover on a type parameter of a generic method inside that method's body shows its constraints — in a local
    /// declaration, in a statement, for every name of the list — for a method of a type that is no generic and for a method of a
    /// generic type (whose own type parameter shows its constraints there too): the body of a generic method nothing calls is
    /// bound on its open instance. Where a call in the file instantiates the method, its body says what that instance says, as
    /// the body of a generic type's method does (CMP-1476): the type argument, <c>type N = Integer</c>; a method of the same type
    /// that nothing calls keeps showing the constraints.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-982")]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericMethodBody_TypeParameterShowsConstraints(bool instantiated)
    {
        var text = """
            unit UO;
            interface
            type
              TThing = class
              public
                Name: string;
              end;
              IShape = interface
                function Area: Integer;
              end;
              TUtil = class
              public
                class function Larger<N: record; M>(const A, B: N; const C: M): N; static;
                function Keep<Q: TThing, IShape, constructor>(const Item: Q): Boolean;
              end;
              TMulti<T: class> = class
              public
                class function Pick<X: IShape; Y>(const P: X; const S: Y): T; static;
              end;
            procedure Run;
            implementation
            class function TUtil.Larger<N, M>(const A, B: N; const C: M): N;
            var
              Best: N;
              Other: M;
            begin
              Best := Default(N);
              Other := M(C);
              Result := A;
            end;
            function TUtil.Keep<Q>(const Item: Q): Boolean;
            var
              Kept: Q;
            begin
              Kept := Q.Create;
              Result := Kept <> nil;
            end;
            class function TMulti<T>.Pick<X, Y>(const P: X; const S: Y): T;
            var
              Shape: X;
              Spare: Y;
              Made: T;
            begin
              Shape := X(P);
              Spare := Default(Y);
              Made := Default(T);
              Result := Made;
            end;
            procedure Run;
            begin
              {CALLS}
            end;
            end.
            """.ReplaceLineEndings("\n").Replace("{CALLS}", instantiated ? "TUtil.Larger<Integer, string>(1, 2, 'x');\n  TMulti<TThing>.Pick<IShape, Byte>(nil, 0);" : "", StringComparison.Ordinal);
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        string? At(string piece, string segment) =>
            QuickInfoService.GetQuickInfo(doc, GenericNamesFixture.Offset(text, piece) + piece.LastIndexOf(segment, StringComparison.Ordinal) + 1,
                                          TestContext.Current.CancellationToken)?.Signature;
        string[] expected = instantiated
            ?
            [
                "Best: N -> type N = Integer",
                "Other: M -> type M = string",
                "Default(N) -> type N = Integer",
                "M(C) -> type M = string",
                "Kept: Q -> type Q: constructor, TThing, IShape",                // Keep<Q> is not called
                "Q.Create -> type Q: constructor, TThing, IShape",
                "Shape: X -> type X = IShape",
                "Spare: Y -> type Y = Byte",
                "Made: T -> type T = TThing",
                "X(P) -> type X = IShape",
                "Default(Y) -> type Y = Byte",
                "Default(T) -> type T = TThing",
            ]
            :
            [
                "Best: N -> type N: record",
                "Other: M -> type M",
                "Default(N) -> type N: record",
                "M(C) -> type M",
                "Kept: Q -> type Q: constructor, TThing, IShape",
                "Q.Create -> type Q: constructor, TThing, IShape",
                "Shape: X -> type X: IShape",
                "Spare: Y -> type Y",
                "Made: T -> type T: class",
                "X(P) -> type X: IShape",
                "Default(Y) -> type Y",
                "Default(T) -> type T: class",
            ];

        string[] shown =
        [
            // a generic method of a type that is no generic: local declarations and statements of its body
            $"Best: N -> {At("Best: N", "N")}",
            $"Other: M -> {At("Other: M", "M")}",
            $"Default(N) -> {At("Default(N)", "N")}",
            $"M(C) -> {At("M(C)", "M")}",
            $"Kept: Q -> {At("Kept: Q", "Q")}",
            $"Q.Create -> {At("Q.Create", "Q")}",
            // a generic method of a generic type: its own parameters and the type's
            $"Shape: X -> {At("Shape: X", "X")}",
            $"Spare: Y -> {At("Spare: Y", "Y")}",
            $"Made: T -> {At("Made: T", "T")}",
            $"X(P) -> {At("X(P)", "X")}",
            $"Default(Y) -> {At("Default(Y)", "Y")}",
            $"Default(T) -> {At("Default(T)", "T")}",
        ];

        Assert.True(expected.SequenceEqual(shown), string.Join("\n", shown));
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

    /// <summary>The quick info at the <c>$$</c> of <paramref name="marker"/> in the inline-inference fixture.</summary>
    private static QuickInfo? HoverInline(string marker)
    {
        var caret = marker.IndexOf("$$", StringComparison.Ordinal);
        var at = InlineInferenceFixture.Offset(marker.Remove(caret, 2)) + caret;
        return QuickInfoService.GetQuickInfo(InlineInferenceFixture.Open(), at, TestContext.Current.CancellationToken);
    }

    /// <summary>CMP-1413 (C-385): an inferred inline or <c>for var</c> variable shows its inferred type, at its declaration and its uses.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1413")]
    [InlineData("var $$N := 5", "var N: Integer")]
    [InlineData("N := N + $$I", "var I: Integer")]
    [InlineData("N := $$N + I", "var N: Integer")]
    [InlineData("var $$L := TBox", "var L: TBox<Integer>")]
    [InlineData("$$L.Free", "var L: TBox<Integer>")]
    [InlineData("for var $$I := 0 to High(U)", "var I: Integer")]
    [InlineData("for var $$Q := 0 to High(V)", "var Q: UInt64")]                         // LANG-575 (C-386): widened by the end bound
    [InlineData("for var $$B in Bytes", "var B: Byte")]
    [InlineData("W := W + $$B", "var B: Byte")]
    public void InlineVariable_ShowsItsInferredType(string marker, string signature)
    {
        var info = HoverInline(marker);

        Assert.NotNull(info);
        Assert.Equal(signature, info.Signature);
        Assert.Equal("P", info.DeclaredIn);
    }

    /// <summary>CMP-1413 (C-385): a declaration rejected with E2075 gives no quick info, at the declaration or a use.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1413")]
    [InlineData("var $$X := nil")]
    [InlineData("var $$A := [1, 2]")]
    [InlineData("$$X := A;")]
    [InlineData("X := $$A;")]
    public void InlineVariable_E2075_NoQuickInfo(string marker)
    {
        Assert.Null(HoverInline(marker));
    }

    /// <summary>The quick info at the <c>$$</c> of the program <paramref name="markup"/> (<c>P.pas</c>), with further files.</summary>
    private static QuickInfo? HoverAt(string markup, params (string Name, string Text)[] files)
    {
        var parsed = MarkupTestFile.Parse(markup);
        var doc = MarkupTestFile.Open("P.pas", [("P.pas", parsed.Text), .. files.Select(f => (f.Name, f.Text.ReplaceLineEndings("\n")))]);
        return QuickInfoService.GetQuickInfo(doc, parsed.Caret, TestContext.Current.CancellationToken);
    }

    /// <summary>CMP-732 (C-393): the XML tags of a doc comment rendered to plain text with fixed labels.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-732")]
    public void XmlDocComment_RenderedAsPlainText()
    {
        const string greet = """
            program P;
            /// <summary>Returns the greeting for <paramref name="AName"/>.</summary>
            /// <param name="AName">The name; <c>''</c> greets the world.</param>
            /// <returns>The text, never empty.</returns>
            function Greet(const AName: string): string;
            begin
              Result := AName;
            end;
            begin
              Gr$$eet('x');
            end.
            """;
        Assert.Equal("Returns the greeting for AName.\n\nParameters:\n  AName – The name; '' greets the world.\n\nReturns: The text, never empty.",
            HoverAt(greet)!.Documentation);

        const string rich = """
            program P;
            {** Lead text.
                <summary>First <b>bold</b> part
                  continues.<para>Second &lt;x&gt; &amp; <see cref="TObject"/>, <seealso cref="Exit"/>.</para></summary>
                <remarks>Line one<br/>line two; &quot;T&quot; is <typeparamref name="T"/>, &apos;x&apos;.</remarks>
                <param name="A">First.</param>
                <param name="B">Second
                  <para>more.</para></param> }
            procedure Doit(A, B: Integer);
            begin
            end;
            begin
              Do$$it(1, 2);
            end.
            """;
        Assert.Equal(
            "Lead text. First bold part continues.\n\nSecond <x> & TObject, Exit.\n\nParameters:\n  A – First.\n  B – Second more.\n\n"
            + "Remarks: Line one\nline two; \"T\" is T, 'x'.",
            HoverAt(rich)!.Documentation);

        const string onlyParams = """
            program P;
            /// <param name="A">The value.</param>
            procedure One(A: Integer);
            begin
            end;
            begin
              O$$ne(1);
            end.
            """;
        Assert.Equal("Parameters:\n  A – The value.", HoverAt(onlyParams)!.Documentation);
    }

    /// <summary>CMP-732: a comment that is not well-formed, or has none of the four top-level elements, is shown as before (CMP-730).</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-732")]
    [InlineData("/// <summary>Open <c>code</summary>", "<summary>Open <c>code</summary>")]
    [InlineData("/// <summary>Unclosed", "<summary>Unclosed")]
    [InlineData("/// <summary>a < b</summary>", "<summary>a < b</summary>")]
    [InlineData("/// Uses <c>X</c> only.", "Uses <c>X</c> only.")]
    [InlineData("/// Plain &amp; simple.", "Plain &amp; simple.")]
    public void XmlDocComment_NotWellFormed_ShownRaw(string comment, string documentation)
    {
        var markup = "program P;\n" + comment + "\nprocedure Doit;\nbegin\nend;\nbegin\n  Do$$it;\nend.";

        Assert.Equal(documentation, HoverAt(markup)!.Documentation);
    }

    /// <summary>CMP-734 (C-394): the plain comment directly above a source declaration is the fallback documentation.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-734")]
    public void PlainCommentAbove_IsTheFallbackDocumentation()
    {
        const string unit = """
            unit U2;
            interface
            type
              // The shape.
              //   Second line.
              TShape = class
              public
                { The area,
                  in square units. }
                function Area: Integer;
                (* <summary>The name.</summary> *)
                Name: string;
              end;
            // <summary>Sums.</summary>
            // <param name="A">Left.</param>
            function Sum(A, B: Integer): Integer;
            implementation
            function TShape.Area: Integer; begin Result := 0 end;
            function Sum(A, B: Integer): Integer; begin Result := A + B end;
            end.
            """;
        string? Doc(string body) => HoverAt("program P;\nuses U2;\nvar S: TShape;\nbegin\n  " + body + "\nend.", ("U2.pas", unit))!.Documentation;

        Assert.Equal("The shape.\n  Second line.", Doc("S := TSh$$ape.Create;"));
        Assert.Equal("The area,\nin square units.", Doc("S.Ar$$ea;"));
        Assert.Equal("The name.", Doc("S.Na$$me := '';"));
        Assert.Equal("Sums.\n\nParameters:\n  A – Left.", Doc("Su$$m(1, 2);"));

        // the same text in completion and signature help; LANG-142 keeps the symbol's own documentation empty
        var markup = MarkupTestFile.Parse("program P;\nuses U2;\nbegin\n  Sum($$\nend.");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", markup.Text), ("U2.pas", unit.ReplaceLineEndings("\n")));
        var help = SignatureHelpService.GetSignatureHelp(doc, markup.Caret, TestContext.Current.CancellationToken);
        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("Sums.", signature.Documentation);
        Assert.Equal(["Left.", null], signature.Parameters.Select(p => p.Documentation));
        var item = CompletionService.GetCompletions(doc, markup.Caret - "Sum(".Length, default, TestContext.Current.CancellationToken).Items
            .First(i => i.Label == "Sum");
        Assert.Equal("Sums.\n\nParameters:\n  A – Left.", CompletionService.GetDescription(doc, item, TestContext.Current.CancellationToken).Documentation);
        var sum = doc.SemanticModel.LookupSymbols(markup.Caret, "Sum").Single();
        Assert.Null(sum.Documentation);
    }

    /// <summary>
    /// CMP-734: no fallback across a blank line, for a comment after a token on its line, above a directive, for a parameter, or from a
    /// <c>.pcu</c> alone.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-734")]
    public void PlainComment_NotAdjacentOrTrailingOrDirective_NotDocumentation()
    {
        const string unit = """
            unit U3;
            interface
            // Not adjacent.

            procedure Spaced;
            procedure First; // Trails First.
            procedure Second;
            const C = 1; { Trails C,
              over two lines. }
            procedure Third;
            // Above a directive.
            {$IFNDEF NEVER_DEFINED}
            procedure Guarded;
            {$ENDIF}
            // Kept.
            procedure Kept(A: Integer);
            implementation
            procedure Spaced; begin end;
            procedure First; begin end;
            procedure Second; begin end;
            procedure Third; begin end;
            procedure Guarded; begin end;
            procedure Kept(A: Integer); begin end;
            end.
            """;
        string? Doc(string body) => HoverAt("program P;\nuses U3;\nbegin\n  " + body + "\nend.", ("U3.pas", unit))!.Documentation;

        Assert.Null(Doc("Spa$$ced;"));
        Assert.Null(Doc("Fi$$rst;"));
        Assert.Null(Doc("Sec$$ond;"));
        Assert.Null(Doc("Th$$ird;"));
        Assert.Null(Doc("Gua$$rded;"));
        Assert.Equal("Kept.", Doc("Ke$$pt(1);"));

        // a parameter is not the routine: no fallback from the routine's comment
        var parameter = HoverAt("program P;\n// Routine.\nprocedure Q(Arg: Integer);\nbegin\n  Writeln(A$$rg);\nend;\nbegin\nend.");
        Assert.Equal("param Arg: Integer", parameter!.Signature);
        Assert.Null(parameter.Documentation);

        // U3 is built to a .pcu; without its source the fallback has no text to read
        const string program = "program P;\nuses U3;\nbegin\n  Kept(1);\nend.";
        var pcu = Compilation.Create(TestFiles.Options(), [], TestFiles.Files(("P.pas", program), ("U3.pas", unit.ReplaceLineEndings("\n"))))
            .PcuTexts().Single(t => t.Unit == "U3").Text;
        var fs = TestFiles.Files();
        fs.Add(@"C:\lib\U3.pcu", pcu).Add(@"C:\lib\U3.obj", "").Add(@"C:\q\P.pas", program);
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
        var doc = workspace.GetDocument(@"C:\q\P.pas");
        var info = QuickInfoService.GetQuickInfo(doc, program.IndexOf("Kept", StringComparison.Ordinal) + 1, TestContext.Current.CancellationToken);
        Assert.Equal("procedure U3.Kept(A: Integer)", info!.Signature);
        Assert.Null(info.Documentation);
    }

    /// <summary>
    /// C-407: in a call on a class name (<c>TFoo.Create</c>) the member is the constructor and the qualifier the class — also an alias
    /// and a generic instance — and a variable inferred from the call has the class as its type.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-731")]
    [InlineData("Foo := TFoo.Cre$$ate;", "constructor TObject.Create")]
    [InlineData("Foo := TF$$oo.Create;", "type TFoo = class(TObject)")]
    [InlineData("Foo := TFoo.Ma$$ke(1);", "constructor TFoo.Make(N: Integer)")]
    [InlineData("Foo := TF$$oo.Make(1);", "type TFoo = class(TObject)")]
    [InlineData("N := TF$$oo.Count;", "type TFoo = class(TObject)")]
    [InlineData("N := TFoo.Co$$unt;", "class function TFoo.Count: Integer")]
    [InlineData("Foo := TAl$$ias.Create;", "type TAlias = TFoo")]
    [InlineData("B := TB$$ox<Integer>.Create;", "type TBox<Integer> = class(TObject)")]
    [InlineData("var Ma$$de := TFoo.Make(2);", "var Made: TFoo")]
    [InlineData("var Par$$ameterless := TFoo.Create;", "var Parameterless: TFoo")]
    public void ConstructorCallOnAClassName(string marker, string signature)
    {
        const string program = """
            program P;
            type
              TFoo = class
                constructor Make(N: Integer);
                class function Count: Integer;
              end;
              TAlias = TFoo;
              TBox<T> = class
                Value: T;
              end;
            constructor TFoo.Make(N: Integer); begin end;
            class function TFoo.Count: Integer; begin Result := 0; end;
            var Foo: TFoo; N: Integer; B: TBox<Integer>;
            begin
              Foo := TFoo.Create;
              Foo := TFoo.Make(1);
              N := TFoo.Count;
              Foo := TAlias.Create;
              B := TBox<Integer>.Create;
              var Made := TFoo.Make(2);
              var Parameterless := TFoo.Create;
            end.
            """;
        var text = program.ReplaceLineEndings("\n");
        var plain = marker.Replace("$$", "", StringComparison.Ordinal);
        Assert.Contains(plain, text, StringComparison.Ordinal);

        var info = HoverAt(text.Replace(plain, marker, StringComparison.Ordinal));

        Assert.Equal(signature, info?.Signature);
    }

    /// <summary>C-416 (CMP-1476): hovering a type query shows the signature text of LANG-972, like every intrinsic.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    [InlineData("B := IsManaged$$Type(string);", "function IsManagedType(T: Type): Boolean")]
    [InlineData("B := Has$$WeakRef(Integer);", "function HasWeakRef(T: Type): Boolean")]
    [InlineData("B := Get$$TypeKind(Integer) = tkInteger;", "function GetTypeKind(T: Type): TTypeKind")]
    [InlineData("B := IsConst$$Value(1 + 2);", "function IsConstValue(const X): Boolean")]
    public void TypeQueryIntrinsic_ShowsItsSignature(string statement, string signature)
    {
        var info = HoverAt("program P;\nvar B: Boolean;\nbegin\n  " + statement + "\nend.");

        Assert.Equal(signature, info?.Signature);
    }

    /// <summary>
    /// CMP-1478 (C-418): hover on the method name of a call whose type arguments are inferred through a <c>var</c>, open-array,
    /// dynamic-array or instance-typed parameter shows the instance the call binds to — its parameters with the inferred types, as for
    /// any instance of a generic method (before: nothing, the call was E2711).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1478")]
    [InlineData("TUtil.Sw$$ap(N, M);", "class procedure TUtil.Swap(var A: Integer; var B: Integer)")]
    [InlineData("N := TUtil.Index$$Of(Names, S);", "class function TUtil.IndexOf(const Values: array of string; const Item: string): Integer")]
    [InlineData("N := TUtil.Index$$Of([N, M], 7);", "class function TUtil.IndexOf(const Values: array of Integer; const Item: Integer): Integer")]
    [InlineData("TUtil.Fir$$st(Reals, 0.5);", "class function TUtil.First(const Values: TArray<Double>; const Fallback: Double): Double")]
    [InlineData("S := TUtil.Un$$box(Box, S);", "class function TUtil.Unbox(const Box: TBox<string>; const Fallback: string): string")]
    public void InferredGenericMethodCall_ShowsTheInstance(string statement, string signature)
    {
        var info = HoverAt(SignatureHelpTests.InferenceProgram.Replace("MAIN_BODY", statement, StringComparison.Ordinal));

        Assert.Equal(signature, info?.Signature);
    }

    /// <summary>
    /// CMP-1478 (C-419): hover on the method name of a call of a name with generic and non-generic methods shows the method LANG-980
    /// chooses — the non-generic one only as an equally good match, else the instance of the generic method.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1478")]
    [Trait("Req", "LANG-980")]
    [InlineData("TOut.Sh$$ow(N);", "class procedure TOut.Show(I: Integer)")]
    [InlineData("TOut.Sh$$ow(5);", "class procedure TOut.Show(I: Integer)")]
    [InlineData("TOut.Sh$$ow(S);", "class procedure TOut.Show(const V: string)")]
    [InlineData("TOut.Sh$$ow(B);", "class procedure TOut.Show(const V: Byte)")]
    [InlineData("TOut.Sh$$ow<Integer>(N);", "class procedure TOut.Show(const V: Integer)")]
    [InlineData("TOut.Sh$$ow(N, S);", "class procedure TOut.Show(const A: Integer; const B: string)")]
    public void MixedOverloads_ShowsTheChosenMethod(string statement, string signature)
    {
        var info = HoverAt(SignatureHelpTests.MixedProgram.Replace("MAIN_BODY", statement, StringComparison.Ordinal));

        Assert.Equal(signature, info?.Signature);
    }

    // ------------------------------------------------------------------ CMP-1473 (C-415): inside anonymous methods

    /// <summary>
    /// CMP-1473 (2): a variable used inside an anonymous method shows the CMP-731 text of its declaration — a captured local,
    /// parameter, inline variable and field of the enclosing method (also two anonymous methods deep, in a generic's method and in
    /// the main block), the anonymous method's own parameter (also one that hides a captured local), local and <c>Result</c>, and a
    /// method reference at a call through it (the variable, not the <c>Invoke</c> of its type).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    [InlineData("$$Count := 0", "var Count: Integer", "P")]                               // in the method itself
    [InlineData("Note := $$Count + Start + Before", "var Count: Integer", "P")]            // captured by the anonymous procedure
    [InlineData("$$Count := Count + Delta", "var Count: Integer", "P")]                    // … and by the anonymous function
    [InlineData("Count + $$Start + Before", "param Start: Integer", "P")]
    [InlineData("Local + $$Start", "param Start: Integer", "P")]                           // two anonymous methods deep
    [InlineData("Start + $$Before", "var Before: Integer", "P")]                           // an inline variable declared before the expression
    [InlineData("Count + $$After", "var After: Integer", "P")]
    [InlineData("Length($$FName)", "field TCounter.FName: string", "P.TCounter")]          // through the captured Self
    [InlineData("function($$Count: Integer)", "param Count: Integer", "P")]                // the parameter that hides the captured local
    [InlineData("Local := $$Count + Note", "param Count: Integer", "P")]
    [InlineData("Count + $$Delta", "param Delta: Integer", "P")]
    [InlineData("var $$Note: Integer", "var Note: Integer", "P")]
    [InlineData("Count + $$Note", "var Note: Integer", "P")]                               // the local of the outer anonymous method
    [InlineData("$$Result := Local + Start", "var Result: Integer", "P")]                  // the anonymous function's own Result
    [InlineData("$$Result := function", "var Result: TIntFunc", "P")]
    [InlineData("$$Log();\n      {@function}", "var Log: TProc", "P")]                     // a captured method reference, called
    [InlineData("$$Log();\n  Result", "var Log: TProc", "P")]
    [InlineData("Count := $$Pick(Note)", "var Pick: TIntFunc", "P")]                       // an inline variable of the anonymous procedure, called
    [InlineData("Mine := $$Seed", "param Seed: T", "P")]                                   // open body of a generic's method
    [InlineData("Keep := $$Mine", "var Mine: T", "P")]
    [InlineData("Result := $$Step + Outer", "param Step: Integer", "P")]                   // in the main block
    [InlineData("Step + $$Outer", "var Outer: Integer", null)]
    public void AnonymousBody_CapturedVariable(string marker, string signature, string? declaredIn)
    {
        var caret = marker.IndexOf("$$", StringComparison.Ordinal);
        var at = AnonymousBodyFixture.Offset(marker.Remove(caret, 2)) + caret;

        var info = QuickInfoService.GetQuickInfo(AnonymousBodyFixture.Open(), at, TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal(signature, info.Signature);
        Assert.Equal(at, info.Span.Start);
        if (declaredIn is not null)
        {
            Assert.Equal(declaredIn, info.DeclaredIn);
        }
    }

    /// <summary>
    /// CMP-1473 with CMP-731: a routine declared inside an anonymous method is shown under the routine (or program) the anonymous
    /// method is written in — the closure class and the method made for the anonymous method are no names of the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void AnonymousBody_NestedRoutine_IsNamedAfterTheEnclosingRoutine()
    {
        const string Source = """
            program P;
            type
              TIntFunc = reference to function(N: Integer): Integer;
              TProc = reference to procedure;
            procedure Work(Limit: Integer);
            var
              Total: Integer;
              Fact: TIntFunc;
            begin
              Total := 0;
              Fact := function(N: Integer): Integer
                function Bump(Step: Integer): Integer;
                begin
                  Total := Total + Step + N;
                  Result := Total;
                end;
                var Later: TProc;
                begin
                  Later := procedure
                    procedure Deep;
                    begin
                      Total := Bump(1);
                    end;
                    begin
                      Deep;
                    end;
                  Later();
                  Result := Bump(N) + Limit;
                end;
              WriteLn(Fact(2));
            end;
            var
              G: TProc;
            begin
              G := procedure
                procedure Top;
                begin
                  Work(1);
                end;
                begin
                  Top;
                end;
              G();
            end.
            """;
        var text = Source.ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        string? Signature(string piece)
        {
            var at = text.IndexOf(piece, StringComparison.Ordinal);
            Assert.True(at >= 0, piece);
            return QuickInfoService.GetQuickInfo(doc, at, TestContext.Current.CancellationToken)?.Signature;
        }

        Assert.Equal("function P.Work.Bump(Step: Integer): Integer", Signature("Bump(N) + Limit"));
        Assert.Equal("function P.Work.Bump(Step: Integer): Integer", Signature("Bump(Step: Integer)"));
        Assert.Equal("function P.Work.Bump(Step: Integer): Integer", Signature("Bump(1)"));                  // from the anonymous procedure nested in it
        Assert.Equal("procedure P.Work.Deep", Signature("Deep;\n        end;\n      Later"));
        Assert.Equal("procedure P.Top", Signature("Top;\n    end;\n  G()"));                                 // in the main block
        Assert.Equal("param Step: Integer", Signature("Step + N"));
        Assert.Equal("param N: Integer", Signature("N;\n      Result := Total"));                       // the anonymous function's, from the routine nested in it
        Assert.Equal("var Total: Integer", Signature("Total + Step + N"));
    }
}
