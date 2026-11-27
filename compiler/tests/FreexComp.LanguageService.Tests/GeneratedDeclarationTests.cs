using FreexComp.Compiler;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// CMP-1420 (C-397): <see cref="NavigationService.GetGeneratedDeclaration"/> — the generated read-only interface text of the unit of a
/// symbol without source (built-ins in <c>System</c>, a <c>.pcu</c> whose source is missing), with the symbol's name selected.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1420")]
public sealed class GeneratedDeclarationTests
{
    private const string Header = "unit System;\n{ Generated from compiled symbols; read only. }\ninterface\n";

    private const string Program = """
        program P;
        var
          X: Integer;
          Y: LongInt;
        procedure Show(N: Integer);
        begin
          Writeln(N);
        end;
        begin
          X := 1;
          Y := X;
          Show(Y);
        end.
        """;

    private static readonly string PText = Program.ReplaceLineEndings("\n");

    private const string Unit1 = """
        unit U1;
        interface
        type
          TBox = class
            Size: Integer;
            function Area: Integer;
          end;
        const
          Answer = 42;
        var
          Count: Integer;
        function Twice(A: Integer): Integer;
        function Describe(I: Integer): string; overload;
        function Describe(B: Boolean): string; overload;
        procedure Alpha;
        implementation
        function TBox.Area: Integer; begin Result := Size * Size end;
        function Twice(A: Integer): Integer; begin Result := A * 2 end;
        function Describe(I: Integer): string; begin Result := 'int' end;
        function Describe(B: Boolean): string; begin Result := 'bool' end;
        procedure Alpha; begin end;
        end.
        """;

    private const string UsesUnit1 = """
        program P;
        uses U1;
        var B: TBox;
        begin
          B := TBox.Create;
          Writeln(Twice(Answer), Describe(1), Describe(True), Count, B.Area);
          Alpha;
        end.
        """;

    private static readonly string U1Text = Unit1.ReplaceLineEndings("\n");

    private static readonly string QText = UsesUnit1.ReplaceLineEndings("\n");

    private static Document Doc() => MarkupTestFile.Open("P.pas", ("P.pas", PText));

    /// <summary>A program in <c>C:\q</c> that finds <c>U1</c> only as a <c>.pcu</c> in <c>C:\lib</c>; U1's source does not exist.</summary>
    private static Document PcuDoc()
    {
        var pcu = Compilation.Create(TestFiles.Options(), [], TestFiles.Files(("P.pas", QText), ("U1.pas", U1Text))).PcuTexts().Single(t => t.Unit == "U1").Text;
        var fs = TestFiles.Files();
        fs.Add(@"C:\lib\U1.pcu", pcu).Add(@"C:\lib\U1.obj", "").Add(@"C:\q\P.pas", QText);
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
        return workspace.GetDocument(@"C:\q\P.pas");
    }

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

    private static GeneratedDeclaration Generated(Document doc, int position) =>
        Assert.IsType<GeneratedDeclaration>(NavigationService.GetGeneratedDeclaration(doc, position, TestContext.Current.CancellationToken));

    /// <summary>The selected name and the whole line it is on.</summary>
    private static (string Name, string Line) Selected(GeneratedDeclaration generated)
    {
        var text = generated.Text;
        var start = text.LastIndexOf('\n', generated.NameSpan.Start) + 1;
        var end = text.IndexOf('\n', generated.NameSpan.Start);
        return (text.Substring(generated.NameSpan.Start, generated.NameSpan.Length), text[start..end]);
    }

    [Fact]
    public void WritelnInSystemView()
    {
        var doc = Doc();
        var position = At(PText, "Writeln");
        Assert.Empty(NavigationService.GoToDefinition(doc, position, TestContext.Current.CancellationToken));

        var generated = Generated(doc, position);

        Assert.Equal("System", generated.UnitName);
        Assert.StartsWith(Header, generated.Text, StringComparison.Ordinal);
        Assert.EndsWith("\nimplementation\n\nend.\n", generated.Text, StringComparison.Ordinal);
        Assert.Equal(("Writeln", "procedure Writeln([var F: Text;] Args);"), Selected(generated));
        Assert.Contains("\nfunction Length(", generated.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PredefinedTypeInSystemView()
    {
        var doc = Doc();
        var integer = Generated(doc, At(PText, "Integer;"));
        Assert.Equal(("Integer", "  Integer;"), Selected(integer));

        // an alias selects its own line; the groups come in the order type, const, var (none here), routines, each sorted
        var alias = Generated(doc, At(PText, "LongInt"));
        Assert.Equal(("LongInt", "  LongInt = Integer;"), Selected(alias));
        var text = alias.Text;
        Assert.True(text.IndexOf("\ntype\n", StringComparison.Ordinal) < text.IndexOf("\nconst\n", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\nconst\n", StringComparison.Ordinal) < text.IndexOf("\nprocedure ", StringComparison.Ordinal));
        Assert.Contains("\n  False = False;\n", text, StringComparison.Ordinal);
        var types = text[(text.IndexOf("\ntype\n", StringComparison.Ordinal) + 6)..text.IndexOf("\n\nconst\n", StringComparison.Ordinal)].Split('\n');
        Assert.Equal(types.Order(StringComparer.OrdinalIgnoreCase), types);
    }

    [Fact]
    public void OverloadSelectsResolvedName()
    {
        var doc = PcuDoc();
        var boolean = Generated(doc, At(QText, "Describe(True)"));
        Assert.Equal(("Describe", "function Describe(B: Boolean): string;"), Selected(boolean));

        var integer = Generated(doc, At(QText, "Describe(1)"));
        Assert.Equal(("Describe", "function Describe(I: Integer): string;"), Selected(integer));
        Assert.True(integer.NameSpan.Start < boolean.NameSpan.Start);                 // overloads in declaration order
        Assert.Equal(integer.Text, boolean.Text);
    }

    [Fact]
    public void PcuWithoutSourceGivesUnitInterface()
    {
        var doc = PcuDoc();
        Assert.Empty(NavigationService.GoToDefinition(doc, At(QText, "Twice"), TestContext.Current.CancellationToken));

        var generated = Generated(doc, At(QText, "Twice"));

        Assert.Equal("U1", generated.UnitName);
        Assert.Equal("""
            unit U1;
            { Generated from compiled symbols; read only. }
            interface

            type
              TBox = class(TObject)
                Size: Integer;
                function Area: Integer;
              end;

            const
              Answer = 42;

            var
              Count: Integer;

            procedure Alpha;
            function Describe(I: Integer): string;
            function Describe(B: Boolean): string;
            function Twice(A: Integer): Integer;

            implementation

            end.

            """.ReplaceLineEndings("\n"), generated.Text);
        Assert.Equal(("Twice", "function Twice(A: Integer): Integer;"), Selected(generated));
        Assert.Equal(("Area", "    function Area: Integer;"), Selected(Generated(doc, At(QText, "Area"))));
        Assert.Equal(("TBox", "  TBox = class(TObject)"), Selected(Generated(doc, At(QText, "TBox;"))));
        Assert.Equal(("Count", "  Count: Integer;"), Selected(Generated(doc, At(QText, "Count"))));
    }

    /// <summary>C-398: a pointer type read back from a <c>.pcu</c> without source is shown in its own unit's view, not in System's.</summary>
    [Fact]
    public void PcuPointerTypeInItsUnit()
    {
        const string unit2 = "unit U2;\ninterface\ntype\n  PCount = ^Integer;\nvar\n  Counter: PCount;\nimplementation\nend.\n";
        const string program = "program P;\nuses U2;\nvar C: PCount;\nbegin\n  C := Counter;\nend.\n";
        var pcu = Compilation.Create(TestFiles.Options(), [], TestFiles.Files(("P.pas", program), ("U2.pas", unit2))).PcuTexts().Single(t => t.Unit == "U2").Text;
        var fs = TestFiles.Files();
        fs.Add(@"C:\lib\U2.pcu", pcu).Add(@"C:\lib\U2.obj", "").Add(@"C:\q\P.pas", program);
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
        var doc = workspace.GetDocument(@"C:\q\P.pas");
        Assert.Empty(NavigationService.GoToDefinition(doc, At(program, "PCount;"), TestContext.Current.CancellationToken));

        var generated = Generated(doc, At(program, "PCount;"));

        Assert.Equal("U2", generated.UnitName);
        Assert.Equal(("PCount", "  PCount;"), Selected(generated));                    // the QuickInfo form of a type (CMP-1420)
        Assert.Equal(("Counter", "  Counter: PCount;"), Selected(Generated(doc, At(program, "Counter;"))));
    }

    [Fact]
    public void SymbolWithSourceGivesNull()
    {
        var doc = Doc();
        foreach (var position in new[] { At(PText, "Show(Y)"), At(PText, "N: Integer"), At(PText, "X := 1"), At(PText, "begin\n  X") - 1,
                                         PText.IndexOf("\n  X := 1", StringComparison.Ordinal), At(PText, "P;") })
        {
            Assert.Null(NavigationService.GetGeneratedDeclaration(doc, position, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void TextIsDeterministic()
    {
        var doc = Doc();
        var first = Generated(doc, At(PText, "Writeln"));
        Assert.Equal(first, Generated(doc, At(PText, "Writeln")));
        Assert.Equal(first.Text, Generated(doc, At(PText, "Integer;")).Text);
        Assert.Equal(first, Generated(Doc(), At(PText, "Writeln")));                    // another workspace, the same compilation input
        Assert.Equal(Generated(PcuDoc(), At(QText, "Twice")), Generated(PcuDoc(), At(QText, "Twice")));
    }
}
