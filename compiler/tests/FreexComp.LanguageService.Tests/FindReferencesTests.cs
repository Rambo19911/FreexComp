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

    /// <summary>
    /// CMP-1400 (C-377): the references of a class constructor are its two headings — never the ordinary constructor of the same
    /// name, its implementation or its calls (LANG-787); those of the ordinary constructor never include the class constructor.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void ClassConstructor_ItsTwoHeadingsOnly()
    {
        var unit = ClassConstructorFixture.Unit;
        var doc = MarkupTestFile.Open("UC.pas", ("P.pas", ClassConstructorFixture.Program), ("UC.pas", unit));
        int NameIn(string piece) => ClassConstructorFixture.IndexOf(unit, piece) + piece.IndexOf("Create", StringComparison.Ordinal);
        int[] References(string piece) =>
            [.. NavigationService.FindReferences(doc, NameIn(piece) + 1, TestContext.Current.CancellationToken)
                .Where(r => r.Location.Path == TestFiles.PathOf("UC.pas")).Select(r => r.Location.Span.Start).Order()];

        Assert.Equal([NameIn("class constructor Create;"), NameIn("class constructor TA.Create;")], References("class constructor Create;"));
        Assert.Equal([NameIn("class constructor Create;"), NameIn("class constructor TA.Create;")], References("class constructor TA.Create;"));
        Assert.DoesNotContain(NameIn("class constructor Create;"), References("constructor Create(N: Integer);"));
        Assert.DoesNotContain(NameIn("class constructor TA.Create;"), References("constructor TA.Create(N: Integer);"));
    }

    /// <summary>
    /// CMP-1476 (C-417): a statement that a type-query condition prunes from the instance in this file (LANG-977 rule 3) is bound on
    /// the open instance for code insight; its names are references to the symbols the kept code refers to — a parameter and a
    /// field named in both parts have one set of references, from the declaration, from the kept use and from the pruned use, and
    /// the same set as with an ordinary condition (the control).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    [InlineData("if IsManagedType(T) then")]
    [InlineData("if Count > 100 then")]
    public void PrunedBranchOfAnInstance_SameSymbolsAsTheKeptCode(string condition)
    {
        var text = """
            program P;
            type
              TItem = class
                Name: string;
              end;
              TStore<T> = class
              public
                Left: TItem;
                procedure Put(const V: T; Count: Integer);
              end;
            procedure TStore<T>.Put(const V: T; Count: Integer);
            begin
              {IF}
              begin
                Left.Name := 'managed';
                Count := Count + 1;
              end
              else
              begin
                Left.Name := 'plain';
                Count := Count - 1;
              end;
            end;
            var
              S: TStore<Integer>;
            begin
              S := TStore<Integer>.Create;
              S.Put(1, 2);
              S.Left := nil;
              S.Free;
            end.
            """.ReplaceLineEndings("\n").Replace("{IF}", condition, StringComparison.Ordinal);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        int[] Lines(string piece) =>
            [.. NavigationService.FindReferences(doc, text.IndexOf(piece, StringComparison.Ordinal), TestContext.Current.CancellationToken)
                .Select(r => r.Location.LineSpan.Start.Line + 1).Where(line => line != 13)];        // line 13: the condition itself

        int[] parameter = [11, 16, 16, 21, 21];                                    // the implementation's heading, both parts
        Assert.Equal(parameter, Lines("Count: Integer);\nbegin"));
        Assert.Equal(parameter, Lines("Count - 1"));                               // the part Integer keeps
        Assert.Equal(parameter, Lines("Count + 1"));                               // the part Integer prunes
        int[] field = [15, 20, 29];                                                // the instance's field: both parts and the program
        Assert.Equal(field, Lines("Left.Name := 'plain'"));
        Assert.Equal(field, Lines("Left.Name := 'managed'"));
        Assert.Equal([4, 15, 20], Lines("Name := 'managed'"));
    }

    /// <summary>
    /// CMP-1478 (C-419): the references of a non-generic method that shares its name with generic methods are the calls LANG-980
    /// binds to it — not the calls that bind to an instance of a generic method of the name, and the other way round.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1478")]
    [Trait("Req", "LANG-980")]
    public void MixedOverloads_ReferencesFollowTheChosenMethod()
    {
        var text = SignatureHelpTests.MixedProgram.Replace("MAIN_BODY",
            "TOut.Show(N);\n  TOut.Show(5);\n  TOut.Show(S);\n  TOut.Show(B);\n  TOut.Show<Integer>(N);\n  TOut.Show(N, S);",
            StringComparison.Ordinal).ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        string Lines(string piece) =>
            string.Join(" ", NavigationService.FindReferences(doc, text.IndexOf(piece, StringComparison.Ordinal) + piece.IndexOf("Show", StringComparison.Ordinal) + 1,
                TestContext.Current.CancellationToken).Select(r => r.Location.LineSpan.Start.Line + 1).Order());

        // lines: 5, 6, 7 the declarations; 9, 10, 11 the implementations; 17…22 the calls
        Assert.Equal(
            ["5 9 17 18", "5 9 17 18", "6 10 19 20 21", "6 10 19 20 21", "7 11 22", "7 11 22"],
            [
                Lines("procedure Show(I: Integer); overload;"), Lines("TOut.Show(5);"),
                Lines("procedure Show<T>(const V: T); overload;"), Lines("TOut.Show(B);"),
                Lines("procedure Show<K, V>(const A: K; const B: V); overload;"), Lines("TOut.Show(N, S);"),
            ]);
    }

    // ------------------------------------------------------------------ CMP-1473 (C-415): inside anonymous methods

    /// <summary>
    /// CMP-1473 (2): a captured variable is one symbol — its references are the declaration and every use in the enclosing body
    /// and in the anonymous methods (also nested ones), whichever of them the search starts from; a parameter of an anonymous
    /// method spelt the same is another symbol. A call through a captured method reference is a use of that variable.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void CapturedLocal_IncludesUsesInAnonymousBodies()
    {
        var doc = AnonymousBodyFixture.Open();
        var text = AnonymousBodyFixture.Text;
        static (int Start, ReferenceKind Kind) Use(string piece, ReferenceKind kind, int delta = 0) => (AnonymousBodyFixture.Offset(piece) + delta, kind);
        void AssertReferences(string name, params (int Start, ReferenceKind Kind)[] expected)
        {
            foreach (var (start, _) in expected)                                // from the declaration and from every use
            {
                var references = NavigationService.FindReferences(doc, start + 1, TestContext.Current.CancellationToken);
                Assert.Equal(expected, references.Select(r => (r.Location.Span.Start, r.Kind)));
                Assert.All(references, r => Assert.Equal(name, text.Substring(r.Location.Span.Start, r.Location.Span.Length)));
                Assert.Equal(expected.Select(e => e.Start), NavigationService.GetDocumentHighlights(doc, start + 1, TestContext.Current.CancellationToken).Select(h => h.Span.Start));
            }
        }

        AssertReferences("Count",
            Use("Count: Integer;", ReferenceKind.Declaration), Use("Count := 0", ReferenceKind.Write),
            Use("Count + Start + Before", ReferenceKind.Read),                                                      // the anonymous procedure
            Use("Count := Pick(Note)", ReferenceKind.Write),
            Use("Count := Count + Delta", ReferenceKind.Write), Use("Count + Delta", ReferenceKind.Read),          // the anonymous function
            Use("Count + After", ReferenceKind.Read));
        AssertReferences("Count",                                                                                   // the nested function's parameter
            Use("function(Count: Integer)", ReferenceKind.Declaration, 9), Use("Count + Note", ReferenceKind.Read));
        AssertReferences("Before", Use("Before := 1", ReferenceKind.Declaration), Use("Start + Before", ReferenceKind.Read, 8));
        AssertReferences("After", Use("After := 2", ReferenceKind.Declaration), Use("Count + After", ReferenceKind.Read, 8));
        AssertReferences("Note",
            Use("Note: Integer", ReferenceKind.Declaration), Use("Note := Count", ReferenceKind.Write), Use("Count + Note", ReferenceKind.Read, 8),
            Use("Pick(Note)", ReferenceKind.Read, 5));
        AssertReferences("Log",
            Use("Log: TProc;", ReferenceKind.Declaration), Use("Log := procedure", ReferenceKind.Write),
            Use("Log();\n  Result", ReferenceKind.Read), Use("Log();\n      {@function}", ReferenceKind.Read));       // called in the method and in the function
        AssertReferences("Pick", Use("Pick: TIntFunc", ReferenceKind.Declaration), Use("Pick(Note)", ReferenceKind.Read));
        AssertReferences("Mine", Use("Mine: T", ReferenceKind.Declaration), Use("Mine := Seed", ReferenceKind.Write), Use("Keep := Mine", ReferenceKind.Read, 8));
        AssertReferences("Outer", Use("Outer := 5", ReferenceKind.Declaration), Use("Step + Outer", ReferenceKind.Read, 7));
    }
    /// <summary>
    /// C-455: the parameter of a generic method — of a class and of a record — is one symbol from its declaration in the type, from
    /// the implementation heading and from the body: the same references and the same definition whichever model is made first and
    /// however many threads bind the units (with one, the program's call that infers the type arguments is bound before the
    /// unit's implementation part).
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-742")]
    public void GenericMethodParameter_SameReferencesAndDefinitionInEveryBindingOrder(int jobs, bool programFirst)
    {
        const string Unit = "unit UGen;\ninterface\ntype\n  TRec = record\n    function Same<T>(Item: T): T; overload;\n    function Same(Item: Integer; Extra: Integer): Integer; overload;\n  end;\n"
            + "  TUtil = class\n    class function Max<T>(A, B: T): T; overload; static;\n    class function Max(A: Integer): Integer; overload; static;\n  end;\n"
            + "implementation\nfunction TRec.Same<T>(Item: T): T; begin Result := Item end;\nfunction TRec.Same(Item: Integer; Extra: Integer): Integer; begin Result := Item + Extra end;\n"
            + "class function TUtil.Max<T>(A, B: T): T; begin Result := A end;\nclass function TUtil.Max(A: Integer): Integer; begin Result := A end;\nend.\n";
        const string Main = "program P;\nuses UGen;\nvar I: Integer; R: TRec;\nbegin\n  I := TUtil.Max(1, 2);\n  I := TUtil.Max(3);\n  I := R.Same(4);\nend.\n";
        var workspace = new Workspace(TestFiles.Files(("P.pas", Main), ("UGen.pas", Unit)));
        workspace.SetProject(TestFiles.Options() with { MaxParallelism = jobs });
        var ct = TestContext.Current.CancellationToken;
        if (programFirst)
        {
            Assert.DoesNotContain(workspace.GetDocument(TestFiles.PathOf("P.pas")).SemanticModel.GetDiagnostics(null, ct), d => d.Severity == DiagnosticSeverity.Error);
        }

        var doc = workspace.GetDocument(TestFiles.PathOf("UGen.pas"));
        void AssertParameter(string declared, string heading, string use)
        {
            int[] sites = [Unit.IndexOf(declared, StringComparison.Ordinal), Unit.IndexOf(heading, StringComparison.Ordinal), Unit.IndexOf(use, StringComparison.Ordinal)];
            Assert.All(sites, site => Assert.True(site >= 0));
            sites[2] += "Result := ".Length;
            foreach (var site in sites)
            {
                var references = NavigationService.FindReferences(doc, site, ct);
                Assert.Equal(sites, references.Select(r => r.Location.Span.Start));
                Assert.Equal([ReferenceKind.Declaration, ReferenceKind.Read, ReferenceKind.Read], references.Select(r => r.Kind));    // the heading repeats the name
                Assert.Equal([sites[0]], NavigationService.GoToDefinition(doc, site, ct).Select(l => l.Span.Start));
            }
        }

        AssertParameter("A, B: T): T; overload", "A, B: T): T; begin", "Result := A end;\nclass function TUtil.Max(A");
        AssertParameter("Item: T): T; overload", "Item: T): T; begin", "Result := Item end");
        AssertParameter("A: Integer): Integer; overload", "A: Integer): Integer; begin", "Result := A end;\nend.");     // as for a method that is not generic
    }
}
