using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §7 (CMP-740, 741, C-137): declaration ↔ implementation toggle, across units, from <c>.pcu</c>, uses entry.</summary>
[Trait("Phase", "3")]
public sealed partial class GoToDefinitionTests
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

    /// <summary>C-398: a pointer type read back from a <c>.pcu</c> keeps its declaration in the unit's source file.</summary>
    [Fact]
    [Trait("Req", "CMP-740")]
    public void PointerTypeFromPcu()
    {
        const string unit2 = "unit U2;\ninterface\ntype\n  PCount = ^Integer;\nvar\n  Counter: PCount;\nimplementation\nend.\n";
        const string program = "program P;\nuses U2;\nvar C: PCount;\nbegin\n  C := Counter;\nend.\n";
        var pcu = Compilation.Create(TestFiles.Options(), [], TestFiles.Files(("P.pas", program), ("U2.pas", unit2))).PcuTexts().Single(t => t.Unit == "U2").Text;
        var fs = TestFiles.Files(("U2.pas", unit2));
        fs.Add(@"C:\lib\U2.pcu", pcu).Add(@"C:\lib\U2.obj", "").Add(@"C:\q\P.pas", program);
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
        var doc = workspace.GetDocument(@"C:\q\P.pas");
        Assert.DoesNotContain(doc.Compilation.SourceUnitPaths, p => p.EndsWith("U2.pas", StringComparison.OrdinalIgnoreCase));   // imported from the .pcu

        var target = Assert.Single(NavigationService.GoToDefinition(doc, At(program, "PCount;"), TestContext.Current.CancellationToken));

        Assert.Equal((TestFiles.PathOf("U2.pas"), unit2.IndexOf("PCount", StringComparison.Ordinal), "PCount".Length),
            (target.Path, target.Span.Start, target.Span.Length));
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

    // ------------------------------------------------------------------ CMP-1394 (C-347): qualified generic and nested type names

    private static (string Path, int Start) DefinitionInP(string piece, string segment, int occurrence = 1)
    {
        var text = GenericNamesFixture.Program;
        var at = GenericNamesFixture.Offset(text, piece, occurrence) + piece.IndexOf(segment, StringComparison.Ordinal) + 1;
        return Single(NavigationService.GoToDefinition(GenericNamesFixture.Open(), at, TestContext.Current.CancellationToken));
    }

    private static (string Path, int Start) InUG(string piece, int occurrence = 1) =>
        (TestFiles.PathOf("UG.pas"), GenericNamesFixture.Offset(GenericNamesFixture.Unit, piece, occurrence));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void QualifiedGenericSegments()
    {
        Assert.Equal((TestFiles.PathOf("UG.pas"), 0), DefinitionInP("UG.TOuter.TBox<Integer>", "UG"));
        Assert.Equal(InUG("TOuter = class"), DefinitionInP("UG.TOuter.TBox<Integer>", "TOuter"));
        Assert.Equal(InUG("TBox<T> = class"), DefinitionInP("UG.TOuter.TBox<Integer>", "TBox"));
        Assert.Equal(InUG("TList<T> = class"), DefinitionInP("UG.TList<string>.TEnumerator", "TList"));
        Assert.Equal(InUG("TOuter = class"), DefinitionInP("TOuter.TBox<Integer>.Create", "TOuter"));
        Assert.Equal(InUG("TBox<T> = class"), DefinitionInP("TOuter.TBox<Integer>.Create", "TBox"));
        Assert.Equal(InUG("TInner = class"), DefinitionInP("TOuter.TInner", "TInner"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void NestedTypeOfInstance_GoesToTheGenericsNestedDeclaration()
    {
        Assert.Equal(InUG("TEnumerator = class"), DefinitionInP("TList<Integer>.TEnumerator;", "TEnumerator"));
        Assert.Equal(InUG("TEnumerator = class"), DefinitionInP("UG.TList<string>.TEnumerator", "TEnumerator"));
        Assert.Equal(InUG("TEnumerator = class"), DefinitionInP("TList<Integer>.TEnumerator.Create", "TEnumerator"));
        Assert.Equal(InUG("TList<T> = class"), DefinitionInP("TList<Integer>.TEnumerator;", "TList"));
        Assert.Equal(InUG("Count: Integer"), DefinitionInP("TList<Integer>.Count", "Count"));
        Assert.Equal(InUG("MoveNext", 2), DefinitionInP("E.MoveNext", "MoveNext"));        // a method: its implementation heading
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void NestedMethodImplementationHeading()
    {
        var doc = GenericNamesFixture.Open("UG.pas");
        var text = GenericNamesFixture.Unit;
        (string, int) Go(string piece, string segment) =>
            Single(NavigationService.GoToDefinition(doc, GenericNamesFixture.Offset(text, piece) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                                    TestContext.Current.CancellationToken));

        // the method segment toggles with the declaration in the nested class; the type segments go to their declarations
        Assert.Equal(InUG("MoveNext: Boolean;"), Go("TList<T>.TEnumerator.MoveNext", "MoveNext"));
        Assert.Equal(InUG("MoveNext", 2), Go("MoveNext: Boolean;", "MoveNext"));
        Assert.Equal(InUG("TEnumerator = class"), Go("TList<T>.TEnumerator.MoveNext", "TEnumerator"));
        Assert.Equal(InUG("TList<T> = class"), Go("TList<T>.TEnumerator.MoveNext", "TList"));
        Assert.Equal(InUG("Go;"), Go("TOuter.TInner.Go", "Go"));
        Assert.Equal(InUG("TInner = class"), Go("TOuter.TInner.Go", "TInner"));
        Assert.Equal(InUG("Make: TList<T>;"), Go("TList<T>.Make", "Make"));
    }

    /// <summary>C-355: in a generic nothing instantiates, method headings, nested types and members navigate like in an instantiated one.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_NestedMethodHeading()
    {
        var doc = GenericNamesFixture.OpenUninstantiated();
        var text = GenericNamesFixture.Uninstantiated;
        (string, int) In(string piece, int occurrence = 1) => (TestFiles.PathOf("UO.pas"), GenericNamesFixture.Offset(text, piece, occurrence));
        (string, int) Go(string piece, string segment) =>
            Single(NavigationService.GoToDefinition(doc, GenericNamesFixture.Offset(text, piece) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                                    TestContext.Current.CancellationToken));

        Assert.Equal(In("IsLast: Boolean;"), Go("TStack<T>.TNode.IsLast", "IsLast"));
        Assert.Equal(In("IsLast", 2), Go("IsLast: Boolean;", "IsLast"));
        Assert.Equal(In("TNode = class"), Go("TStack<T>.TNode.IsLast", "TNode"));
        Assert.Equal(In("TStack<T> = class"), Go("TStack<T>.TNode.IsLast", "TStack"));
        Assert.Equal(In("Push(const V: T);"), Go("TStack<T>.Push", "Push"));
        Assert.Equal(In("Push", 2), Go("Push(const V: T);", "Push"));
        Assert.Equal(In("TNode = class"), Go("Top: TNode", "TNode"));
        Assert.Equal(In("Count: Integer"), Go("Count + 1", "Count"));          // a member in a method body
        Assert.Equal(In("Value: T"), Go("Top.Value", "Value"));
    }

    /// <summary>
    /// C-358: in a generic nothing instantiates, a generic method called with the generic's type parameter (<c>TUtil.Max&lt;T&gt;(A, B)</c>)
    /// navigates like any method call (to its implementation), and members of constrained type parameters to those of the constraint.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_GenericMethodCall()
    {
        var text = GenericNamesFixture.Constrained;
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        (string, int) In(string piece, int occurrence = 1) => (TestFiles.PathOf("UO.pas"), GenericNamesFixture.Offset(text, piece, occurrence));
        (string, int) Go(string piece, string segment) =>
            Single(NavigationService.GoToDefinition(doc, GenericNamesFixture.Offset(text, piece) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                                    TestContext.Current.CancellationToken));

        Assert.Equal(In("Max<T>(const A, B: T): T;", 2), Go("TUtil.Max<T>(A, B)", "Max"));
        Assert.Equal(In("TUtil = class"), Go("TUtil.Max<T>(A, B)", "TUtil"));
        Assert.Equal(In("Rename(const S: string);", 2), Go("Item.Rename", "Rename"));
        Assert.Equal(In("Name: string"), Go("Item.Name)", "Name"));
        Assert.Equal(In("Area: Integer"), Go("Shape.Area", "Area"));
    }
    /// <summary>
    /// C-364: in a generic nothing instantiates, a generic method called with inferred type arguments (the generic's own type
    /// parameter) navigates like an explicit one — <c>TUtil.Max(A, B)</c> and the generic's own <c>Pick(A, B)</c> — and members of
    /// members of constrained type parameters navigate.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_InferredGenericMethodCall()
    {
        var text = GenericNamesFixture.Chained;
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        (string, int) In(string piece, int occurrence = 1) => (TestFiles.PathOf("UO.pas"), GenericNamesFixture.Offset(text, piece, occurrence));
        (string, int) Go(string piece, string segment) =>
            Single(NavigationService.GoToDefinition(doc, GenericNamesFixture.Offset(text, piece) + piece.IndexOf(segment, StringComparison.Ordinal) + 1,
                                                    TestContext.Current.CancellationToken));

        Assert.Equal(In("Max<T>(const A, B: T): T;", 2), Go("TUtil.Max(A, B)", "Max"));
        Assert.Equal(In("TUtil = class"), Go("TUtil.Max(A, B)", "TUtil"));
        Assert.Equal(In("Pick<U>(const A, B: U): U;", 2), Go("Pick(A, B)", "Pick"));
        Assert.Equal(In("Rename(const S: string);", 2), Go("Item.Owner.Rename", "Rename"));
        Assert.Equal(In("Title: string"), Go("Item.Parent.Title", "Title"));
        Assert.Equal(In("Owner: TOwner"), Go("Larger(Item, Item).Owner", "Owner"));
        Assert.Equal(In("Bounds: TOwner"), Go("Shape.Bounds", "Bounds"));
    }

    /// <summary>
    /// CMP-1400 (C-377): go-to-definition toggles between a class constructor's (destructor's) declaration heading and its
    /// implementation heading in both directions, as for methods — also in a nested type, a record and a generic; the ordinary constructor of
    /// the same name keeps its own pair, and a call <c>TA.Create(1)</c> goes to the ordinary constructor (LANG-787).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void ClassConstructorHeadings()
    {
        var unit = ClassConstructorFixture.Unit;
        var program = ClassConstructorFixture.Program;
        var doc = MarkupTestFile.Open("UC.pas", ("P.pas", program), ("UC.pas", unit));
        (string, int) In(string text, string file, string piece, string name) =>
            (TestFiles.PathOf(file), ClassConstructorFixture.IndexOf(text, piece) + piece.IndexOf(name, StringComparison.Ordinal));
        (string, int) Go(Document from, (string Path, int Start) at) =>
            Single(NavigationService.GoToDefinition(from, at.Start + 1, TestContext.Current.CancellationToken));

        (string Declaration, string Implementation, string Name)[] pairs =
        [
            ("class constructor Create;", "class constructor TA.Create;", "Create"),
            ("class destructor Teardown;", "class destructor TA.Teardown;", "Teardown"),
            ("class destructor Done;", "class destructor TA.TN.Done;", "Done"),
            ("class constructor Init;", "class constructor TR.Init;", "Init"),
            ("constructor Create(N: Integer);", "constructor TA.Create(N: Integer);", "Create"),
            ("class constructor Create;\n  end;", "class constructor TBox<T>.Create;", "Create"),
        ];
        foreach (var (declaration, implementation, name) in pairs)
        {
            var declared = In(unit, "UC.pas", declaration, name);
            var implemented = In(unit, "UC.pas", implementation, name);
            Assert.Equal(implemented, Go(doc, declared));
            Assert.Equal(declared, Go(doc, implemented));
        }

        var call = MarkupTestFile.Open("P.pas", ("P.pas", program), ("UC.pas", unit));
        Assert.Equal(In(unit, "UC.pas", "constructor TA.Create(N: Integer);", "Create"), Go(call, In(program, "P.pas", "TA.Create(1)", "Create")));
    }

    /// <summary>
    /// CMP-1477, CMP-1478 (C-418): a call whose type arguments are inferred through a <c>var</c> parameter, an open array, a dynamic
    /// array or an instance of a generic class resolves in the semantic model as in the compiler (LANG-979) — its method name
    /// navigates to the generic method (its implementation heading), not to nothing as for a call that does not bind.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1477")]
    [Trait("Req", "CMP-1478")]
    public void InferredGenericMethodCall_ThroughVarParameter()
    {
        var text = SignatureHelpTests.InferenceProgram.Replace("MAIN_BODY",
            "TUtil.Swap(N, M);\n  N := TUtil.IndexOf(Names, S);\n  N := TUtil.IndexOf([N, M], 7);\n  TUtil.First(Reals, 0.5);\n  S := TUtil.Unbox(Box, S);",
            StringComparison.Ordinal);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        Assert.DoesNotContain(doc.Compilation.GetDiagnostics(ct: TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        (string, int) Implementation(string heading) => (TestFiles.PathOf("P.pas"), text.IndexOf(heading, StringComparison.Ordinal) + "TUtil.".Length);
        (string, int) Go(string call) =>
            Single(NavigationService.GoToDefinition(doc, text.IndexOf(call, StringComparison.Ordinal) + "TUtil.".Length + 1, TestContext.Current.CancellationToken));

        Assert.Equal(Implementation("TUtil.Swap<T>(var A, B: T); begin"), Go("TUtil.Swap(N, M)"));
        Assert.Equal(Implementation("TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer; begin"), Go("TUtil.IndexOf(Names, S)"));
        Assert.Equal(Implementation("TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer; begin"), Go("TUtil.IndexOf([N, M], 7)"));
        Assert.Equal(Implementation("TUtil.First<T>(const Values: TArray<T>; const Fallback: T): T; begin"), Go("TUtil.First(Reals, 0.5)"));
        Assert.Equal(Implementation("TUtil.Unbox<T>(const Box: TBox<T>; const Fallback: T): T; begin"), Go("TUtil.Unbox(Box, S)"));
    }

    /// <summary>
    /// CMP-1478 (C-419): go-to-definition on the method name of a call of a name with generic and non-generic methods leads to the
    /// method LANG-980 chooses — the non-generic one only as an equally good match; for an instance, the generic method.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1478")]
    [Trait("Req", "LANG-980")]
    public void MixedOverloads_GoesToTheChosenMethod()
    {
        var text = SignatureHelpTests.MixedProgram.Replace("MAIN_BODY",
            "TOut.Show(N);\n  TOut.Show(5);\n  TOut.Show(S);\n  TOut.Show(B);\n  TOut.Show<Integer>(N);\n  TOut.Show(N, S);",
            StringComparison.Ordinal).ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        Assert.DoesNotContain(doc.Compilation.GetDiagnostics(ct: TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        (string, int) Implementation(string heading) => (TestFiles.PathOf("P.pas"), text.IndexOf(heading, StringComparison.Ordinal) + "TOut.".Length);
        (string, int) Go(string call) =>
            Single(NavigationService.GoToDefinition(doc, text.IndexOf(call, StringComparison.Ordinal) + "TOut.".Length + 1, TestContext.Current.CancellationToken));

        Assert.Equal(Implementation("TOut.Show(I: Integer); begin"), Go("TOut.Show(N);"));
        Assert.Equal(Implementation("TOut.Show(I: Integer); begin"), Go("TOut.Show(5);"));
        Assert.Equal(Implementation("TOut.Show<T>(const V: T); begin"), Go("TOut.Show(S);"));
        Assert.Equal(Implementation("TOut.Show<T>(const V: T); begin"), Go("TOut.Show(B);"));
        Assert.Equal(Implementation("TOut.Show<T>(const V: T); begin"), Go("TOut.Show<Integer>(N);"));
        Assert.Equal(Implementation("TOut.Show<K, V>(const A: K; const B: V); begin"), Go("TOut.Show(N, S);"));
    }

    // ------------------------------------------------------------------ CMP-1473 (C-415): inside anonymous methods

    /// <summary>
    /// CMP-1473 (2): from a use inside an anonymous method, a captured variable goes to its declaration in the enclosing body (a
    /// classic local, an inline variable, a method reference at a call through it); the anonymous method's own parameter and local
    /// go to their declaration in the anonymous method — also a parameter spelt like a captured local.
    /// <paramref name="target"/>: a piece of the fixture with <c>$$</c> at the declaring identifier.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    [InlineData("Note := $$Count + Start + Before", "$$Count: Integer;")]
    [InlineData("$$Count := Pick(Note)", "$$Count: Integer;")]
    [InlineData("$$Count := Count + Delta", "$$Count: Integer;")]
    [InlineData("Start + $$Before", "var $$Before := 1")]
    [InlineData("Count + $$After", "var $$After := 2")]
    [InlineData("$$Log();\n      {@function}", "$$Log: TProc;")]                         // a call through the captured method reference
    [InlineData("Local := $$Count + Note", "function($$Count: Integer)")]                // the parameter that hides the captured local
    [InlineData("Count + $$Delta", "function($$Delta: Integer): Integer\n    begin")]
    [InlineData("Count + $$Note", "var $$Note: Integer")]                                // from the nested anonymous function
    [InlineData("Result := $$Local + Start", "var $$Local: Integer")]
    [InlineData("Count := $$Pick(Note)", "var $$Pick: TIntFunc")]
    [InlineData("Keep := $$Mine", "var $$Mine: T")]                                      // open body of a generic's method
    [InlineData("$$Keep := Mine", "$$Keep: T;")]
    [InlineData("Result := $$Step + Outer", "function($$Step: Integer)")]                // in the main block
    [InlineData("Step + $$Outer", "var $$Outer := 5")]
    public void AnonymousBody_CapturedVariableAndOwnParameter(string marker, string target)
    {
        static int Caret(string piece)
        {
            var caret = piece.IndexOf("$$", StringComparison.Ordinal);
            return AnonymousBodyFixture.Offset(piece.Remove(caret, 2)) + caret;
        }

        var found = NavigationService.GoToDefinition(AnonymousBodyFixture.Open(), Caret(marker), TestContext.Current.CancellationToken);

        Assert.Equal((TestFiles.PathOf("P.pas"), Caret(target)), Single(found));
    }
}
