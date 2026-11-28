using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// language-service.md §8 (CMP-750…753, C-139): what can be renamed, the edits of a rename across units, the conflict check on a
/// forked compilation, unit / program rename (heading, uses entries, qualified names, file rename) and the companion <c>.form</c>
/// edits of a form class's published field or method.
/// </summary>
[Trait("Phase", "8")]
public sealed class RenameTests
{
    private const string Unit1 = """
        unit Unit1;
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
        var Shared: Integer;
        procedure Hello;
        implementation
        procedure TBase.Run; begin Count := Count + 1 end;
        procedure TDerived.Run; begin inherited Run end;
        procedure Hello;
        var Total: Integer;
        begin
          Total := Shared;
          Shared := Total + 1;
        end;
        end.
        """;

    private const string Unit2 = """
        unit Unit2;
        interface
        uses Unit1;
        function Make: Unit1.TBase;
        implementation
        function Make: Unit1.TBase; begin Result := nil end;
        end.
        """;

    private const string Unit3 = """
        unit Unit3;
        interface
        procedure Call;
        implementation
        uses Unit1;
        procedure Call; begin Hello end;
        end.
        """;

    private const string App = """
        program App;
        uses Unit1, Unit2, Unit3;
        var B: TBase; D: TDerived;
        begin
          Unit1.Hello;
          Unit1.Shared := 2;
          B := Make;
          B.Run;
          D.Run;
          Call;
        end.
        """;

    private static (string Name, string Text)[] Fixture(params (string Name, string Text)[] replaced)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["P.pas"] = App, ["Unit1.pas"] = Unit1, ["Unit2.pas"] = Unit2, ["Unit3.pas"] = Unit3,
        };
        foreach (var (name, text) in replaced)
        {
            files[name] = text;
        }

        return [.. files.Select(f => (f.Key, f.Value.ReplaceLineEndings("\n")))];
    }

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

    private static string TextOf((string Name, string Text)[] files, string name) => files.Single(f => f.Name == name).Text;

    private static RenameResult Rename((string Name, string Text)[] files, string file, string piece, string newName, int occurrence = 1)
    {
        var doc = MarkupTestFile.Open(file, files);
        return RenameService.Rename(doc, At(TextOf(files, file), piece, occurrence), newName, TestContext.Current.CancellationToken);
    }

    private static RenameInfo Info((string Name, string Text)[] files, string file, string piece, int occurrence = 1)
    {
        var doc = MarkupTestFile.Open(file, files);
        return RenameService.GetRenameInfo(doc, At(TextOf(files, file), piece, occurrence), TestContext.Current.CancellationToken);
    }

    private static string Apply(string text, IEnumerable<TextChange> changes)
    {
        var builder = new System.Text.StringBuilder(text);
        foreach (var change in changes.OrderByDescending(c => c.Span.Start))
        {
            builder.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
        }

        return builder.ToString();
    }

    /// <summary>The files after the result's edits and file rename (as the IDE applies them).</summary>
    private static (string Name, string Text)[] Applied((string Name, string Text)[] files, RenameResult result)
    {
        Assert.True(result.Success, result.Error);
        var after = files.ToDictionary(f => TestFiles.PathOf(f.Name), f => f.Text, StringComparer.OrdinalIgnoreCase);
        foreach (var edit in result.Edits)
        {
            after[edit.Path] = Apply(after[edit.Path], edit.Changes);
        }

        if (result.FileRename is { } rename)
        {
            after[rename.NewPath] = after[rename.OldPath];
            after.Remove(rename.OldPath);
        }

        return [.. after.Select(f => (Path.GetFileName(f.Key), f.Value))];
    }

    /// <summary>"file: n" per edited file, ordered by file name.</summary>
    private static string[] Counts(RenameResult result) =>
        [.. result.Edits.Select(e => $"{Path.GetFileName(e.Path)}: {e.Changes.Length}").Order(StringComparer.Ordinal)];

    private static void AssertCompiles((string Name, string Text)[] files, string main = "P.pas")
    {
        var workspace = new Workspace(TestFiles.Files(files));
        workspace.SetProject(TestFiles.Options(main));
        Assert.Empty(workspace.CurrentCompilation.GetDiagnostics(null, TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));
    }

    // ------------------------------------------------------------------------------------------------ CMP-750

    [Fact]
    [Trait("Req", "CMP-750")]
    public void GetRenameInfo_OnASourceSymbol_GivesItsSpanAndName()
    {
        var files = Fixture();

        var info = Info(files, "Unit1.pas", "Total := Shared");

        Assert.True(info.CanRename, info.Reason);
        Assert.Null(info.Reason);
        Assert.Equal("Total", info.CurrentName);
        Assert.Equal(new TextSpan(At(TextOf(files, "Unit1.pas"), "Total := Shared"), 5), info.Span);
    }

    [Fact]
    [Trait("Req", "CMP-750")]
    public void GetRenameInfo_BuiltInsRtlSymbolsAndKeywords_AreNotRenamable()
    {
        var files = Fixture(("Unit3.pas", """
            unit Unit3;
            interface
            type TMine = class(TObject) destructor Destroy; override; end;
            procedure Call;
            implementation
            destructor TMine.Destroy; begin inherited end;
            procedure Call; var I: Integer; begin Writeln(I) end;
            end.
            """));

        var builtIn = Info(files, "Unit3.pas", "Integer");
        var intrinsic = Info(files, "Unit3.pas", "Writeln");
        var rtl = Info(files, "Unit3.pas", "TObject");
        var overrideOfRtl = Info(files, "Unit3.pas", "Destroy; override");
        var keyword = Info(files, "Unit3.pas", "implementation");

        Assert.All([builtIn, intrinsic, rtl, overrideOfRtl, keyword], i => Assert.False(i.CanRename));
        Assert.All([builtIn, intrinsic, rtl, overrideOfRtl, keyword], i => Assert.False(string.IsNullOrEmpty(i.Reason)));
        Assert.Equal("Only identifiers can be renamed.", keyword.Reason);
        Assert.Equal("'TObject' is declared in the run-time library and cannot be renamed.", rtl.Reason);
        Assert.Equal("'Destroy' is declared in the run-time library and cannot be renamed.", overrideOfRtl.Reason);
        Assert.Equal("'Writeln' is built in and cannot be renamed.", intrinsic.Reason);
    }

    [Fact]
    [Trait("Req", "CMP-750")]
    public void Rename_OfANonRenamableSymbol_FailsWithTheReason()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Integer", "Whole");

        Assert.False(result.Success);
        Assert.Equal("'Integer' is built in and cannot be renamed.", result.Error);
        Assert.Empty(result.Edits);
        Assert.Null(result.FileRename);
    }

    // ------------------------------------------------------------------------------------------------ CMP-751

    [Theory]
    [Trait("Req", "CMP-751")]
    [InlineData("")]
    [InlineData("begin")]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("A.B")]
    public void Rename_InvalidNewName_Fails(string newName)
    {
        var result = Rename(Fixture(), "Unit1.pas", "Total := Shared", newName);

        Assert.False(result.Success);
        Assert.Equal($"'{newName}' is not a valid identifier.", result.Error);
        Assert.Empty(result.Edits);
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_Local_EditsDeclarationAndEveryReference()
    {
        var files = Fixture();

        var result = Rename(files, "Unit1.pas", "Total := Shared", "Sum");

        Assert.Equal(["Unit1.pas: 3"], Counts(result));
        Assert.Null(result.FileRename);
        var text = TextOf(Applied(files, result), "Unit1.pas");
        Assert.Contains("var Sum: Integer;\nbegin\n  Sum := Shared;\n  Shared := Sum + 1;", text, StringComparison.Ordinal);
        AssertCompiles(Applied(files, result));
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_GlobalUsedInOtherUnits_EditsEveryFile_ChangesInTextOrder()
    {
        var files = Fixture();

        var result = Rename(files, "P.pas", "Shared := 2", "Common");

        Assert.Equal(["P.pas: 1", "Unit1.pas: 3"], Counts(result));
        Assert.All(result.Edits, e => Assert.Equal(e.Changes.OrderBy(c => c.Span.Start), e.Changes));
        Assert.Equal(result.Edits.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase), result.Edits);
        var after = Applied(files, result);
        Assert.Contains("Unit1.Common := 2;", TextOf(after, "P.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_VirtualMethod_RenamesTheWholeOverrideChain()
    {
        var files = Fixture();

        var result = Rename(files, "P.pas", "Run", "Execute", occurrence: 2);            // D.Run

        Assert.Equal(["P.pas: 2", "Unit1.pas: 5"], Counts(result));
        var after = Applied(files, result);
        Assert.DoesNotContain("Run", TextOf(after, "Unit1.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_SameNameInAnotherLetterCase_Succeeds()
    {
        var files = Fixture();

        var result = Rename(files, "Unit1.pas", "Total := Shared", "TOTAL");

        Assert.Equal(["Unit1.pas: 3"], Counts(result));
        AssertCompiles(Applied(files, result));
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_UnchangedName_SucceedsWithoutEdits()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Total := Shared", "Total");

        Assert.True(result.Success);
        Assert.Empty(result.Edits);
        Assert.Null(result.FileRename);
    }

    /// <summary>The renamed global's reference inside <c>Hello</c> would bind to the local of the same name.</summary>
    [Fact]
    [Trait("Req", "CMP-751")]
    public void Conflict_EditedReferenceWouldBindToAnotherSymbol_Fails()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Shared: Integer", "Total");

        Assert.False(result.Success);
        Assert.Equal("Renaming 'Shared' to 'Total' changes the meaning of 'Total' at Unit1.pas(20,12).", result.Error);
        Assert.Empty(result.Edits);
    }

    /// <summary>The local's new name hides the global the routine reads: an existing reference changes its target.</summary>
    [Fact]
    [Trait("Req", "CMP-751")]
    public void Conflict_OtherReferenceWouldChangeItsTarget_Fails()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Total := Shared", "Shared");

        Assert.False(result.Success);
        Assert.StartsWith("Renaming 'Total' to 'Shared' changes the meaning of 'Shared' at Unit1.pas(", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Edits);
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Conflict_NameAlreadyDeclaredInTheSameScope_Fails()
    {
        var files = Fixture(("Unit3.pas", """
            unit Unit3;
            interface
            procedure Call;
            implementation
            procedure Call;
            var First, Second: Integer;
            begin
              First := 1;
              Second := First;
            end;
            end.
            """));

        var result = Rename(files, "Unit3.pas", "First := 1", "Second");

        Assert.False(result.Success);
        Assert.StartsWith("Renaming 'First' to 'Second' changes the meaning of 'Second' at Unit3.pas(", result.Error, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ CMP-1505, CMP-1506 (C-449)

    private const string LateUser = """
        unit U3;
        interface
        procedure Run;
        implementation
        uses U2, U1;
        procedure Run;
        begin
          Bar;
        end;
        end.
        """;

    /// <summary><c>U1.Foo</c>, <c>U2.Bar</c>, and <c>U3</c> that uses <c>U2, U1</c> and names only <c>Bar</c>; the program does not use <c>U2</c>.</summary>
    private static (string Name, string Text)[] CaptureFixture() =>
    [
        ("P.pas", "program P;\nuses U1, U3;\nbegin\n  Foo;\n  Run;\nend.\n"),
        ("U1.pas", "unit U1;\ninterface\nprocedure Foo;\nimplementation\nprocedure Foo; begin end;\nend.\n"),
        ("U2.pas", "unit U2;\ninterface\nprocedure Bar;\nimplementation\nprocedure Bar; begin end;\nend.\n"),
        ("U3.pas", LateUser.ReplaceLineEndings("\n")),
    ];

    private static string Describe(RenameConflict conflict) =>
        $"{Path.GetFileName(conflict.Location.Path)}({conflict.Location.LineSpan.Start.Line + 1},{conflict.Location.LineSpan.Start.Column + 1}) {conflict.Name} [{conflict.Location.Span.Length}]";

    /// <summary>
    /// CMP-1506: <c>U3</c> is not edited by renaming <c>U1.Foo</c> to <c>Bar</c> — it never names <c>Foo</c> — but its <c>Bar</c>,
    /// which is <c>U2.Bar</c>, would become <c>U1.Bar</c> (the unit used last wins). The check binds every unit that spells the new name.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1506")]
    public void Conflict_UneditedUnitWouldBindToTheRenamedSymbol_Fails()
    {
        var files = CaptureFixture();
        AssertCompiles(files);

        var result = Rename(files, "U1.pas", "Foo;\nimplementation", "Bar");

        Assert.False(result.Success);
        Assert.Equal("Renaming 'Foo' to 'Bar' changes the meaning of 'Bar' at U3.pas(8,3).", result.Error);
        Assert.Empty(result.Edits);
        Assert.Equal(["U3.pas(8,3) Bar [3]"], result.Conflicts.Select(Describe));
        Assert.Equal(TestFiles.PathOf("U3.pas"), result.Conflicts[0].Location.Path);
        Assert.Equal(At(TextOf(files, "U3.pas"), "Bar;"), result.Conflicts[0].Location.Span.Start);
        Assert.Equal(result.Error, result.Conflicts[0].Message);

        // the same from a use in the program; a name nobody else spells is free
        Assert.Equal(["U3.pas(8,3) Bar [3]"], Rename(files, "P.pas", "Foo;", "bar").Conflicts.Select(Describe));
        var free = Rename(files, "U1.pas", "Foo;\nimplementation", "Baz");
        Assert.True(free.Success, free.Error);
        Assert.Empty(free.Conflicts);
        Assert.Equal(["P.pas: 1", "U1.pas: 2"], Counts(free));

        // U3 that names U2.Bar through its unit keeps its meaning: no conflict
        var qualified = CaptureFixture();
        qualified[3] = ("U3.pas", TextOf(files, "U3.pas").Replace("  Bar;", "  U2.Bar;", StringComparison.Ordinal));
        var kept = Rename(qualified, "U1.pas", "Foo;\nimplementation", "Bar");
        Assert.True(kept.Success, kept.Error);
        AssertCompiles(Applied(qualified, kept));
    }

    /// <summary>
    /// CMP-1506: a field renamed to the name of a unit-level variable that a method of a descendant class — in a unit the rename
    /// does not edit — reads and writes: there the name would become the inherited field.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1506")]
    public void Conflict_InheritedMemberWouldHideAGlobalInAnUneditedDescendant_Fails()
    {
        (string Name, string Text)[] files =
        [
            ("P.pas", "program P;\nuses UBase, UDesc;\nvar D: TDesc;\nbegin\n  D := TDesc.Create;\n  D.Size := 1;\n  D.Grow;\nend.\n"),
            ("UBase.pas", "unit UBase;\ninterface\ntype\n  TBase = class\n  public\n    Size: Integer;\n  end;\nimplementation\nend.\n"),
            ("UDesc.pas", "unit UDesc;\ninterface\nuses UBase;\ntype\n  TDesc = class(TBase)\n    procedure Grow;\n  end;\nvar Total: Integer;\nimplementation\n"
                          + "procedure TDesc.Grow;\nbegin\n  Total := Total + 1;\nend;\nend.\n"),
        ];
        AssertCompiles(files);

        var result = Rename(files, "UBase.pas", "Size: Integer", "Total");

        Assert.False(result.Success);
        Assert.Equal("Renaming 'Size' to 'Total' changes the meaning of 'Total' at UDesc.pas(12,3).", result.Error);
        Assert.Empty(result.Edits);
        Assert.Equal(["UDesc.pas(12,3) Total [5]", "UDesc.pas(12,12) Total [5]"], result.Conflicts.Select(Describe));   // the declaration of the variable keeps its meaning

        var other = Rename(files, "UBase.pas", "Size: Integer", "Extent");
        Assert.True(other.Success, other.Error);
        Assert.Equal(["P.pas: 1", "UBase.pas: 1"], Counts(other));
    }

    /// <summary>
    /// The fixture's <c>Shared</c> renamed to <c>Total</c>: two edited names in <c>Unit1</c> would become the local of <c>Hello</c>, and
    /// in <c>Unit3</c> — not edited — the <c>Total</c> of <c>Unit4</c> would become <c>Unit1</c>'s (and <paramref name="uses"/> more of them).
    /// </summary>
    private static (string Name, string Text)[] ManyConflictsFixture(int uses = 1) => Fixture(
        ("Unit4.pas", "unit Unit4;\ninterface\nvar Total: Integer;\nimplementation\nend.\n"),
        ("Unit3.pas", "unit Unit3;\ninterface\nprocedure Call;\nimplementation\nuses Unit4, Unit1;\nprocedure Call;\nbegin\n  Hello;\n"
                      + string.Concat(Enumerable.Repeat("  Total := 1;\n", uses)) + "end;\nend.\n"));

    /// <summary>CMP-1505: every conflict is listed — files in path order, then position; an edited name is given as the new name.</summary>
    [Fact]
    [Trait("Req", "CMP-1505")]
    [Trait("Req", "CMP-1506")]
    public void Conflict_AllConflictsAreListedInOrder()
    {
        var files = ManyConflictsFixture();
        AssertCompiles(files);

        var result = Rename(files, "Unit1.pas", "Shared: Integer", "Total");

        Assert.False(result.Success);
        Assert.Empty(result.Edits);
        Assert.Equal(["Unit1.pas(20,12) Total [6]", "Unit1.pas(21,3) Total [6]", "Unit3.pas(9,3) Total [5]"], result.Conflicts.Select(Describe));
        Assert.Equal(
            [
                "Renaming 'Shared' to 'Total' changes the meaning of 'Total' at Unit1.pas(20,12).",
                "Renaming 'Shared' to 'Total' changes the meaning of 'Total' at Unit1.pas(21,3).",
                "Renaming 'Shared' to 'Total' changes the meaning of 'Total' at Unit3.pas(9,3).",
            ],
            result.Conflicts.Select(c => c.Message));
        Assert.All(result.Conflicts, c => Assert.Equal(TestFiles.PathOf(Path.GetFileName(c.Location.Path)), c.Location.Path));

        // the same list from a use of the symbol in another file
        Assert.Equal(result.Conflicts.Select(Describe), Rename(files, "P.pas", "Shared := 2", "Total").Conflicts.Select(Describe));
    }

    /// <summary>
    /// CMP-1505: with conflicts <c>Error</c> is the first conflict's sentence — the text it has always been — and nothing is edited;
    /// a rename that succeeds and a refusal of another kind have no conflicts.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1505")]
    public void Conflict_ErrorIsTheFirstConflictsMessage()
    {
        var result = Rename(ManyConflictsFixture(), "Unit1.pas", "Shared: Integer", "Total");

        Assert.False(result.Success);
        Assert.Equal(3, result.Conflicts.Length);
        Assert.Equal("Renaming 'Shared' to 'Total' changes the meaning of 'Total' at Unit1.pas(20,12).", result.Error);
        Assert.Equal(result.Error, result.Conflicts[0].Message);
        Assert.Empty(result.Edits);
        Assert.Null(result.FileRename);

        // the fixture without the unit that is not edited: the conflicts of the edited file alone, the same first sentence
        var edited = Rename(Fixture(), "Unit1.pas", "Shared: Integer", "Total");
        Assert.Equal(result.Error, edited.Error);
        Assert.Equal(["Unit1.pas(20,12) Total [6]", "Unit1.pas(21,3) Total [6]"], edited.Conflicts.Select(Describe));

        Assert.Empty(Rename(Fixture(), "Unit1.pas", "Shared: Integer", "Common").Conflicts);                 // success
        Assert.Empty(Rename(Fixture(), "Unit1.pas", "Shared: Integer", "Shared").Conflicts);                 // unchanged name
        Assert.Empty(Rename(Fixture(), "Unit1.pas", "Shared: Integer", "not valid").Conflicts);              // invalid name
        Assert.Empty(Rename(Fixture(), "Unit1.pas", "Integer;\n    procedure Run", "Whole").Conflicts);       // not renamable
        var exists = Rename(Fixture(), "Unit1.pas", "Unit1;", "Unit2");
        Assert.Equal("A unit named 'Unit2' already exists.", exists.Error);
        Assert.Empty(exists.Conflicts);

        // a unit rename lists its conflicts too (LANG-311: the variable hides the unit, so the qualifier would stop naming it)
        var hidden = Rename(Fixture(("P.pas", "program App;\nuses Unit1;\nvar Customers: Integer;\nbegin\n  Unit1.Hello;\n  Customers := 1;\nend.\n")), "Unit1.pas", "Unit1;", "Customers");
        Assert.False(hidden.Success);
        Assert.Equal("Renaming 'Unit1' to 'Customers' changes the meaning of 'Customers' at P.pas(5,3).", hidden.Error);
        Assert.Equal(["P.pas(5,3) Customers [5]", "P.pas(5,9) Hello [5]"], hidden.Conflicts.Select(Describe));      // the member of the hidden unit binds to nothing
        Assert.Equal(hidden.Error, hidden.Conflicts[0].Message);
        Assert.Null(hidden.FileRename);
    }

    /// <summary>CMP-1505: at most <see cref="RenameService.MaxConflicts"/> conflicts are collected — the first ones in order.</summary>
    [Fact]
    [Trait("Req", "CMP-1505")]
    public void Conflict_ListIsCappedAtOneHundred()
    {
        Assert.Equal(100, RenameService.MaxConflicts);
        var files = ManyConflictsFixture(uses: 118);                                // 2 in Unit1 + 118 in Unit3 = 120
        AssertCompiles(files);

        var result = Rename(files, "Unit1.pas", "Shared: Integer", "Total");

        Assert.False(result.Success);
        Assert.Equal(100, result.Conflicts.Length);
        Assert.Equal("Unit1.pas(20,12) Total [6]", Describe(result.Conflicts[0]));
        Assert.Equal("Unit1.pas(21,3) Total [6]", Describe(result.Conflicts[1]));
        Assert.Equal(Enumerable.Range(9, 98).Select(line => $"Unit3.pas({line},3) Total [5]"), result.Conflicts.Skip(2).Select(Describe));
        Assert.Equal(result.Conflicts[0].Message, result.Error);

        var below = Rename(ManyConflictsFixture(uses: 97), "Unit1.pas", "Shared: Integer", "Total");
        Assert.Equal(99, below.Conflicts.Length);
    }

    // ------------------------------------------------------------------------------------------------ CMP-752

    /// <summary>The IDE's fixture (IDE-PM-138): heading, the uses entries of three files, the qualified names.</summary>
    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_EditsHeadingUsesEntriesAndQualifiedNames_AndReportsTheFileRename()
    {
        var files = Fixture();

        var result = Rename(files, "Unit1.pas", "Unit1;", "Customers");

        Assert.True(result.Success, result.Error);
        Assert.Equal(["P.pas: 3", "Unit1.pas: 1", "Unit2.pas: 3", "Unit3.pas: 1"], Counts(result));
        Assert.Equal(new FileRename(TestFiles.PathOf("Unit1.pas"), TestFiles.PathOf("Customers.pas")), result.FileRename);
        var after = Applied(files, result);
        Assert.StartsWith("unit Customers;\n", TextOf(after, "Customers.pas"), StringComparison.Ordinal);
        Assert.Contains("uses Customers, Unit2, Unit3;", TextOf(after, "P.pas"), StringComparison.Ordinal);
        Assert.Contains("  Customers.Hello;\n  Customers.Shared := 2;", TextOf(after, "P.pas"), StringComparison.Ordinal);
        Assert.Contains("uses Customers;\nfunction Make: Customers.TBase;", TextOf(after, "Unit2.pas"), StringComparison.Ordinal);
        Assert.Contains("uses Customers;", TextOf(after, "Unit3.pas"), StringComparison.Ordinal);
        Assert.All(after, f => Assert.DoesNotContain("Unit1", f.Text, StringComparison.Ordinal));
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_FromAUsesEntryOrAQualifier_IsTheSameRename()
    {
        var files = Fixture();
        var fromHeading = Rename(files, "Unit1.pas", "Unit1;", "Customers");

        var fromUses = Rename(files, "P.pas", "Unit1, Unit2", "Customers");
        var fromQualifier = Rename(files, "P.pas", "Unit1.Hello", "Customers");

        Assert.Equal(Counts(fromHeading), Counts(fromUses));
        Assert.Equal(Counts(fromHeading), Counts(fromQualifier));
        Assert.Equal(fromHeading.FileRename, fromUses.FileRename);
        Assert.Equal(fromHeading.FileRename, fromQualifier.FileRename);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_GetRenameInfoOnTheHeading_IsRenamable()
    {
        var files = Fixture();

        var info = Info(files, "Unit1.pas", "Unit1;");

        Assert.True(info.CanRename, info.Reason);
        Assert.Equal("Unit1", info.CurrentName);
        Assert.Equal(new TextSpan(5, 5), info.Span);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_InPathOfAUsesEntry_FollowsTheFile()
    {
        var files = Fixture(("P.pas", "program App;\nuses Unit1 in 'Unit1.pas', Unit2 in '.\\Unit2.pas';\nbegin\n  Unit1.Hello;\nend.\n"));

        var result = Rename(files, "Unit1.pas", "Unit1;", "Customers");

        var after = Applied(files, result);
        Assert.Contains("uses Customers in 'Customers.pas', Unit2 in '.\\Unit2.pas';", TextOf(after, "P.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_DottedNames_AreWholeNames()
    {
        var files = Fixture(
            ("Data.Core.pas", "unit Data.Core;\ninterface\nprocedure Ping;\nimplementation\nprocedure Ping; begin end;\nend.\n"),
            ("P.pas", "program App;\nuses Data.Core;\nbegin\n  Data.Core.Ping;\n  Ping;\nend.\n"));

        var result = Rename(files, "Data.Core.pas", "Data.Core;", "Store.Base");

        Assert.Equal(new FileRename(TestFiles.PathOf("Data.Core.pas"), TestFiles.PathOf("Store.Base.pas")), result.FileRename);
        var after = Applied(files, result);
        Assert.Equal("program App;\nuses Store.Base;\nbegin\n  Store.Base.Ping;\n  Ping;\nend.\n", TextOf(after, "P.pas"));
        Assert.StartsWith("unit Store.Base;", TextOf(after, "Store.Base.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_ToTheNameOfAnExistingUnit_IsAConflict()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Unit1;", "Unit2");

        Assert.False(result.Success);
        Assert.Equal("A unit named 'Unit2' already exists.", result.Error);
        Assert.Empty(result.Edits);
        Assert.Null(result.FileRename);
    }

    /// <summary>A file on the search path that the project does not use yet is a conflict as well: the renamed unit would shadow it or be shadowed.</summary>
    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_ToTheNameOfAnUnusedUnitFile_IsAConflict()
    {
        var files = Fixture(("Spare.pas", "unit Spare;\ninterface\nimplementation\nend.\n"));

        var result = Rename(files, "Unit1.pas", "Unit1;", "spare");

        Assert.False(result.Success);
        Assert.Equal("A unit named 'spare' already exists.", result.Error);
    }

    /// <summary>LANG-311: a nearer declaration of the new name hides the unit, so the qualified name would stop naming it.</summary>
    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_QualifierHiddenByADeclaration_IsAConflict()
    {
        var files = Fixture(("P.pas", "program App;\nuses Unit1;\nvar Customers: Integer;\nbegin\n  Unit1.Hello;\n  Customers := 1;\nend.\n"));

        var result = Rename(files, "Unit1.pas", "Unit1;", "Customers");

        Assert.False(result.Success);
        Assert.Equal("Renaming 'Unit1' to 'Customers' changes the meaning of 'Customers' at P.pas(5,3).", result.Error);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_InvalidUnitName_Fails()
    {
        var result = Rename(Fixture(), "Unit1.pas", "Unit1;", "My Unit");

        Assert.False(result.Success);
        Assert.Equal("'My Unit' is not a valid unit name.", result.Error);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_LetterCaseOnly_EditsTheNamesAndRenamesTheFile()
    {
        var files = Fixture();

        var result = Rename(files, "Unit1.pas", "Unit1;", "UNIT1");

        Assert.True(result.Success, result.Error);
        Assert.Equal(["P.pas: 3", "Unit1.pas: 1", "Unit2.pas: 3", "Unit3.pas: 1"], Counts(result));
        Assert.Equal(new FileRename(TestFiles.PathOf("Unit1.pas"), TestFiles.PathOf("UNIT1.pas")), result.FileRename);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void ProgramRename_EditsTheHeadingAndItsOwnQualifiers_FileRenameOnlyWhenTheFileIsNamedAfterIt()
    {
        var files = Fixture(("P.pas", "program P;\nuses Unit1;\nvar X: Integer;\nbegin\n  P.X := 1;\n  Hello;\nend.\n"));
        var named = Rename(files, "P.pas", "P;", "Shop");

        var other = Fixture(("P.pas", "program App;\nuses Unit1;\nvar X: Integer;\nbegin\n  App.X := 1;\n  Hello;\nend.\n"));
        var unnamed = Rename(other, "P.pas", "App;", "Shop");

        Assert.Equal(["P.pas: 2"], Counts(named));
        Assert.Equal(new FileRename(TestFiles.PathOf("P.pas"), TestFiles.PathOf("Shop.pas")), named.FileRename);
        Assert.Equal("program Shop;\nuses Unit1;\nvar X: Integer;\nbegin\n  Shop.X := 1;\n  Hello;\nend.\n", TextOf(Applied(files, named), "Shop.pas"));
        AssertCompiles(Applied(files, named), "Shop.pas");
        Assert.Equal(["P.pas: 2"], Counts(unnamed));
        Assert.Null(unnamed.FileRename);
        AssertCompiles(Applied(other, unnamed));
    }

    [Fact]
    [Trait("Req", "CMP-750")]
    public void UnitRename_OfAnRtlUnit_IsNotRenamable()
    {
        var files = Fixture(("Unit3.pas", "unit Unit3;\ninterface\nuses System;\nprocedure Call;\nimplementation\nprocedure Call; begin end;\nend.\n"));

        var info = Info(files, "Unit3.pas", "System;");

        Assert.False(info.CanRename);
        Assert.Equal("'System' is declared in the run-time library and cannot be renamed.", info.Reason);
    }

    /// <summary>IDE-PM-135: a file without a parsable heading has nothing to rename at the IDE's position.</summary>
    [Fact]
    [Trait("Req", "CMP-750")]
    public void GetRenameInfo_FileWithoutHeading_IsNotRenamable()
    {
        var files = Fixture(("Unit3.pas", "unit ;\ninterface\nimplementation\nend.\n"));

        var info = Info(files, "Unit3.pas", ";");
        var result = Rename(files, "Unit3.pas", ";", "Other");

        Assert.False(info.CanRename);
        Assert.Equal("Only identifiers can be renamed.", info.Reason);
        Assert.False(result.Success);
        Assert.Equal(info.Reason, result.Error);
    }

    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_PlainNameToDottedName_KeepsEveryQualifiedReference()
    {
        var files = Fixture();

        var result = Rename(files, "Unit1.pas", "Unit1;", "Shop.Customers");

        Assert.Equal(new FileRename(TestFiles.PathOf("Unit1.pas"), TestFiles.PathOf("Shop.Customers.pas")), result.FileRename);
        var after = Applied(files, result);
        Assert.Contains("  Shop.Customers.Hello;\n  Shop.Customers.Shared := 2;", TextOf(after, "P.pas"), StringComparison.Ordinal);
        Assert.Contains("function Make: Shop.Customers.TBase;", TextOf(after, "Unit2.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    /// <summary>
    /// IDE-PM-133: a file on a search path outside the project directory that uses the unit is part of the edit set, under its own
    /// path, so that the caller can refuse the rename.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-752")]
    public void UnitRename_UsedByAFileOnASearchPath_ThatFileIsInTheEdits()
    {
        const string lib = @"C:\lib\Extra.pas";
        var files = Fixture(("P.pas", "program App;\nuses Unit1, Extra;\nbegin\n  Hello;\n  More;\nend.\n"));
        var fs = TestFiles.Files(files);
        fs.Add(lib, "unit Extra;\ninterface\nprocedure More;\nimplementation\nuses Unit1;\nprocedure More; begin Unit1.Hello end;\nend.\n");
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options() with { SearchPaths = [@"C:\lib"] });

        var result = RenameService.Rename(workspace.GetDocument(TestFiles.PathOf("Unit1.pas")), 5, "Customers", TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal([lib, TestFiles.PathOf("P.pas"), TestFiles.PathOf("Unit1.pas")], result.Edits.Select(e => e.Path));
        Assert.Equal(2, result.Edits[0].Changes.Length);
    }

    /// <summary>An interface method, the methods implementing it and their overrides are one name.</summary>
    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_InterfaceMethod_RenamesItsImplementationsToo()
    {
        var files = Fixture(("Unit3.pas", """
            unit Unit3;
            interface
            type
              IRunner = interface
                procedure Go;
              end;
              TRunner = class(TInterfacedObject, IRunner)
                procedure Go; virtual;
              end;
              TFast = class(TRunner)
                procedure Go; override;
              end;
            procedure Call;
            implementation
            procedure TRunner.Go; begin end;
            procedure TFast.Go; begin inherited Go end;
            procedure Call;
            var R: IRunner;
            begin
              R := TFast.Create;
              R.Go;
            end;
            end.
            """));

        var fromInterface = Rename(files, "Unit3.pas", "Go;", "Start");
        var fromClass = Rename(files, "Unit3.pas", "Go; override", "Start");

        Assert.Equal(["Unit3.pas: 7"], Counts(fromInterface));
        Assert.Equal(["Unit3.pas: 7"], Counts(fromClass));
        var after = Applied(files, fromInterface);
        Assert.DoesNotContain("Go", TextOf(after, "Unit3.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-751")]
    public void Rename_Cancelled_Throws()
    {
        var files = Fixture();
        var doc = MarkupTestFile.Open("Unit1.pas", files);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => RenameService.Rename(doc, At(TextOf(files, "Unit1.pas"), "Shared: Integer"), "Common", cancelled.Token));
    }

    // ------------------------------------------------------------------------------------------------ CMP-753

    private const string FormUnit = """
        unit UMain;
        interface
        type
          TNotify = procedure(Sender: TObject) of object;
          TButton = class
          private
            FOnClick: TNotify;
          published
            property OnClick: TNotify read FOnClick write FOnClick;
          end;
          TForm1 = class
          published
            Button1: TButton;
            Button2: TButton;
            procedure Clicked(Sender: TObject);
          public
            Plain: Integer;
          end;
        implementation
        {$FORM UMain.form}
        procedure TForm1.Clicked(Sender: TObject);
        begin
          Button1.OnClick := Clicked;
          Plain := 1;
        end;
        end.
        """;

    private const string Form =
        "object Form1: TForm1\r\n  object Button1: TButton\r\n    OnClick = Clicked\r\n  end\r\n  object Button2: TButton\r\n    OnClick = Clicked\r\n  end\r\nend\r\n";

    private static (string Name, string Text)[] FormFixture() =>
        [("P.pas", "program P;\nuses UMain;\nbegin\nend.\n"), ("UMain.pas", FormUnit.ReplaceLineEndings("\n")), ("UMain.form", Form)];

    [Fact]
    [Trait("Req", "CMP-753")]
    public void FormField_Rename_AlsoEditsTheObjectNameInTheForm()
    {
        var files = FormFixture();

        var result = Rename(files, "UMain.pas", "Button1: TButton", "OkButton");

        Assert.Equal(["UMain.form: 1", "UMain.pas: 2"], Counts(result));
        var after = Applied(files, result);
        Assert.Equal(Form.Replace("Button1", "OkButton", StringComparison.Ordinal), TextOf(after, "UMain.form"));
        Assert.Contains("OkButton.OnClick := Clicked;", TextOf(after, "UMain.pas"), StringComparison.Ordinal);
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-753")]
    public void FormMethod_Rename_AlsoEditsEveryEventHandlerValueInTheForm()
    {
        var files = FormFixture();

        var result = Rename(files, "UMain.pas", "Clicked(Sender: TObject);", "ButtonClick");

        Assert.Equal(["UMain.form: 2", "UMain.pas: 3"], Counts(result));
        var after = Applied(files, result);
        Assert.Equal(Form.Replace("Clicked", "ButtonClick", StringComparison.Ordinal), TextOf(after, "UMain.form"));
        AssertCompiles(after);
    }

    [Fact]
    [Trait("Req", "CMP-753")]
    public void FormClass_NonPublishedMemberOrOtherClass_LeavesTheFormAlone()
    {
        var files = FormFixture();

        var plain = Rename(files, "UMain.pas", "Plain: Integer", "Simple");
        var other = Rename(files, "UMain.pas", "OnClick: TNotify read", "OnPress");
        var backing = Rename(files, "UMain.pas", "FOnClick: TNotify", "FPress");

        Assert.Equal(["UMain.pas: 2"], Counts(plain));
        Assert.True(other.Success, other.Error);
        Assert.DoesNotContain(other.Edits, e => e.Path.EndsWith(".form", StringComparison.OrdinalIgnoreCase));
        Assert.True(backing.Success, backing.Error);
        Assert.Equal(["UMain.pas: 3"], Counts(backing));
    }

    /// <summary>A form the scanner rejects cannot be edited by position: the rename is refused rather than left half done.</summary>
    [Fact]
    [Trait("Req", "CMP-753")]
    public void FormField_UnreadableForm_Fails()
    {
        var files = FormFixture();
        files[2] = ("UMain.form", "object Form1: TForm1\n  Left = = 1\nend\n");

        var result = Rename(files, "UMain.pas", "Button1: TButton", "OkButton");

        Assert.False(result.Success);
        Assert.Equal("The form file 'UMain.form' cannot be read, so its references cannot be updated.", result.Error);
    }

    /// <summary>
    /// LANG-952 / LANG-953 (C-342): the handlers of a frame instance and of its override blocks are methods of the root, so a method
    /// rename edits them; the name of an override block is a component of the frame, so a field rename of the root leaves it alone.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-753")]
    [Trait("Req", "LANG-953")]
    public void FormFrameInstance_MethodRenameEditsItsHandlers_FieldRenameLeavesOverrideBlocks()
    {
        const string form = "object Form1: TForm1\n  object Button1: TButton\n    OnClick = Clicked\n  end\n  inline Button2: TButton\n    OnClick = Clicked\n"
            + "    inherited Button1: TButton\n      OnClick = Clicked\n    end\n  end\nend\n";
        (string Name, string Text)[] files = [FormFixture()[0], FormFixture()[1], ("UMain.form", form)];

        var method = Rename(files, "UMain.pas", "Clicked(Sender: TObject);", "ButtonClick");
        var field = Rename(files, "UMain.pas", "Button1: TButton", "OkButton");

        Assert.Equal(["UMain.form: 3", "UMain.pas: 3"], Counts(method));
        Assert.Equal(form.Replace("Clicked", "ButtonClick", StringComparison.Ordinal), TextOf(Applied(files, method), "UMain.form"));
        Assert.Equal(["UMain.form: 1", "UMain.pas: 2"], Counts(field));
        Assert.Equal(form.Replace("object Button1", "object OkButton", StringComparison.Ordinal), TextOf(Applied(files, field), "UMain.form"));
        AssertCompiles(Applied(files, method));
        AssertCompiles(Applied(files, field));
    }

    // ------------------------------------------------------------------------------------------------ LANG-572 (C-307)

    private const string InlineScopes = """
        program P;
        var Value: string;
        begin
          Value := 'outer';
          begin
            var Value := 1;
            Value := Value + 1;
          end;
          begin
            var Value := 2.5;
            Value := Value * 2;
          end;
          Value := Value + '!';
        end.
        """;

    /// <summary>
    /// LANG-572: three entities named <c>Value</c> — a global and two inline variables of sibling blocks. A rename started on
    /// one of them (declaration or use) edits only the occurrences inside that entity's scope.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-572")]
    public void InlineVariable_RenamesOnlyItsScope()
    {
        (string Name, string Text)[] files = [("P.pas", InlineScopes.ReplaceLineEndings("\n"))];

        var first = Rename(files, "P.pas", "Value := 1", "Count");
        Assert.Equal(["P.pas: 3"], Counts(first));
        Assert.Equal(InlineScopes.ReplaceLineEndings("\n").Replace("    var Value := 1;\n    Value := Value + 1;", "    var Count := 1;\n    Count := Count + 1;", StringComparison.Ordinal),
            TextOf(Applied(files, first), "P.pas"));
        AssertCompiles(Applied(files, first));

        // from a use inside the second block
        var second = Rename(files, "P.pas", "Value * 2", "Ratio");
        Assert.Equal(["P.pas: 3"], Counts(second));
        Assert.Equal(InlineScopes.ReplaceLineEndings("\n").Replace("    var Value := 2.5;\n    Value := Value * 2;", "    var Ratio := 2.5;\n    Ratio := Ratio * 2;", StringComparison.Ordinal),
            TextOf(Applied(files, second), "P.pas"));
        AssertCompiles(Applied(files, second));

        // the global: its declaration and the uses outside the two blocks
        var global = Rename(files, "P.pas", "Value := 'outer'", "Text");
        Assert.Equal(["P.pas: 4"], Counts(global));
        var text = TextOf(Applied(files, global), "P.pas");
        Assert.Contains("var Text: string;\nbegin\n  Text := 'outer';", text, StringComparison.Ordinal);
        Assert.Contains("  Text := Text + '!';", text, StringComparison.Ordinal);
        Assert.Contains("    var Value := 1;\n    Value := Value + 1;", text, StringComparison.Ordinal);
        Assert.Contains("    var Value := 2.5;\n    Value := Value * 2;", text, StringComparison.Ordinal);
        AssertCompiles(Applied(files, global));

        // a new name that an inline variable would capture inside its scope is a conflict
        (string Name, string Text)[] hiding = [("P.pas", "program P;\nvar Outer: Integer;\nbegin\n  begin\n    var Inner := 1;\n    Outer := Inner;\n  end;\nend.\n")];
        Assert.False(Rename(hiding, "P.pas", "Outer: Integer", "Inner").Success);
        Assert.False(Rename(hiding, "P.pas", "Inner := 1", "Outer").Success);
    }

    // ------------------------------------------------------------------------------------------------ CMP-1473 (C-415)

    /// <summary>The fixture text with every <paramref name="edits"/> pair replaced (each old piece must occur).</summary>
    private static string Edited(params (string Old, string New)[] edits)
    {
        var text = AnonymousBodyFixture.Text;
        foreach (var (old, @new) in edits)
        {
            Assert.Contains(old, text, StringComparison.Ordinal);
            text = text.Replace(old, @new, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Renames the name at <paramref name="piece"/> in the anonymous-method fixture and gives the edit count and the new text.</summary>
    private static (int Edits, string Text) RenamedAnonymous(string piece, string newName)
    {
        var files = AnonymousBodyFixture.Files;
        var result = Rename(files, "P.pas", piece, newName);
        Assert.True(result.Success, result.Error);
        var applied = Applied(files, result);
        AssertCompiles(applied);
        return (Assert.Single(result.Edits).Changes.Length, TextOf(applied, "P.pas"));
    }

    /// <summary>
    /// CMP-1473 (2): a rename of a captured variable — started at its declaration or at a use inside an anonymous method — edits
    /// the declaration and every use in the enclosing body and in the anonymous methods, and nothing else: the parameter of the
    /// nested anonymous function that is spelt the same keeps its name. Also a captured parameter, a captured inline variable and
    /// a captured method reference with the calls through it.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void CapturedLocal_RenamesUsesInAnonymousBodies()
    {
        var expected = Edited(
            ("  Count: Integer;\n", "  Total: Integer;\n"), ("  Count := 0;", "  Total := 0;"), ("Note := Count + Start", "Note := Total + Start"),
            ("Count := Pick(Note)", "Total := Pick(Note)"), ("Count := Count + Delta", "Total := Total + Delta"), ("Count + After", "Total + After"));
        Assert.Contains("function(Count: Integer): Integer", expected, StringComparison.Ordinal);
        Assert.Contains("Local := Count + Note;", expected, StringComparison.Ordinal);

        Assert.Equal((7, expected), RenamedAnonymous("Count + After", "Total"));                    // from a use in the anonymous function
        Assert.Equal((7, expected), RenamedAnonymous("Count + Start + Before", "Total"));            // from a use in the anonymous procedure
        Assert.Equal((7, expected), RenamedAnonymous("Count: Integer;", "Total"));                   // from the declaration

        // a captured parameter: the two headings of the method and the uses in the anonymous procedure and in the function nested in it
        Assert.Equal((4, Edited(("Start", "First"))), RenamedAnonymous("Start;\n", "First"));
        // a captured inline variable
        Assert.Equal((2, Edited(("Before", "Early"))), RenamedAnonymous("Before;\n", "Early"));
        Assert.Equal((2, Edited(("After", "Next"))), RenamedAnonymous("After := 2", "Next"));
        // a captured method reference: the calls through it, in the method and in the anonymous function, are uses of the variable
        Assert.Equal((4, Edited(("Log", "Trace"))), RenamedAnonymous("Log();\n      {@function}", "Trace"));
        Assert.Equal((4, Edited(("Log", "Trace"))), RenamedAnonymous("Log: TProc;", "Trace"));
        // in the method of a generic that is never instantiated, and in the main block
        Assert.Equal((2, Edited(("Keep", "Held"))), RenamedAnonymous("Keep := Mine", "Held"));
        Assert.Equal((2, Edited(("Outer", "Base"))), RenamedAnonymous("Outer;\n", "Base"));
    }

    /// <summary>
    /// CMP-1473 (3): a rename of a parameter or local of an anonymous method edits that anonymous method (and the ones nested in
    /// it) only — the captured local spelt like the parameter and the parameter of the method-reference type keep their names.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void AnonymousParameter_StaysInsideItsBody()
    {
        // the parameter of the nested anonymous function, spelt like the captured local of the method
        var seen = Edited(("function(Count: Integer)", "function(Seen: Integer)"), ("Local := Count + Note", "Local := Seen + Note"));
        Assert.Equal((2, seen), RenamedAnonymous("Count + Note", "Seen"));
        Assert.Equal((2, seen), RenamedAnonymous("Count: Integer)", "Seen"));

        // the parameter of the second anonymous function; TIntFunc's parameter Delta is another symbol
        var amount = Edited(("function(Delta: Integer): Integer\n    begin", "function(Amount: Integer): Integer\n    begin"), ("Count + Delta", "Count + Amount"));
        Assert.Contains("TIntFunc = reference to function(Delta: Integer): Integer;", amount, StringComparison.Ordinal);
        Assert.Equal((2, amount), RenamedAnonymous("Delta;\n", "Amount"));

        // a local of the anonymous procedure: its body and the anonymous function nested in it
        Assert.Equal((4, Edited(("Note", "Memo"))), RenamedAnonymous("Note: Integer", "Memo"));
        Assert.Equal((4, Edited(("Note", "Memo"))), RenamedAnonymous("Note;\n", "Memo"));            // from the use in the nested function
        // a local of the nested function, and the inline variable of the procedure that holds it (declared and called)
        Assert.Equal((3, Edited(("Local", "Sum"))), RenamedAnonymous("Local: Integer", "Sum"));
        Assert.Equal((2, Edited(("Pick", "Choose"))), RenamedAnonymous("Pick(Note)", "Choose"));
        // in the method of a generic that is never instantiated, and in the main block
        Assert.Equal((3, Edited(("Mine", "Own"))), RenamedAnonymous("Mine := Seed", "Own"));
        Assert.Equal((2, Edited(("Step", "Pace"))), RenamedAnonymous("Step + Outer", "Pace"));

        // a name used elsewhere in the method but not in this body is no conflict: After is declared after the procedure
        Assert.Equal((4, Edited(("Note", "After"))), RenamedAnonymous("Note: Integer", "After"));
    }

    /// <summary>
    /// CMP-1473 (3) with CMP-751: a rename that makes a local or parameter of an anonymous method hide a captured name its body
    /// uses is refused, and so is the other direction (the captured variable renamed to a name an anonymous method declares and
    /// uses beside it); hiding a name the body does not use is accepted.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void AnonymousLocal_HidingACapturedName_IsRefused()
    {
        var files = AnonymousBodyFixture.Files;
        void Refused(string piece, string oldName, string newName)
        {
            var result = Rename(files, "P.pas", piece, newName);
            Assert.False(result.Success, $"{oldName} -> {newName}");
            Assert.StartsWith($"Renaming '{oldName}' to '{newName}' changes the meaning of '{newName}' at P.pas(", result.Error, StringComparison.Ordinal);
            Assert.Empty(result.Edits);
        }

        Refused("Note: Integer", "Note", "Count");                             // the procedure's local: a captured local,
        Refused("Note: Integer", "Note", "Start");                             // a captured parameter,
        Refused("Note: Integer", "Note", "Before");                            // a captured inline variable
        Refused("Delta;\n", "Delta", "Count");                                 // the function's parameter
        Refused("Delta;\n", "Delta", "After");
        Refused("Delta;\n", "Delta", "Log");                                   // the captured method reference the body calls
        Refused("Local: Integer", "Local", "Note");                            // the nested function's local: the local of the procedure around it
        Refused("Local: Integer", "Local", "Start");
        Refused("Step + Outer", "Step", "Outer");                              // in the main block (beside the function's own Result)

        Refused("Count: Integer;", "Count", "Note");                           // the other direction
        Refused("Count: Integer;", "Count", "Delta");
        Refused("Before := 1", "Before", "Note");

        // Before is not used in the second anonymous function: its parameter may hide it
        var hidden = RenamedAnonymous("Delta;\n", "Before");
        Assert.Equal((2, Edited(("function(Delta: Integer): Integer\n    begin", "function(Before: Integer): Integer\n    begin"), ("Count + Delta", "Count + Before"))), hidden);
    }

    /// <summary>
    /// CMP-751 inside and around anonymous methods (CMP-1473): <c>Result</c> and <c>Self</c> have no declaration site, so the
    /// conflict check knows them by the routine they belong to. Renaming that routine — a function, a method, the class of the
    /// method, a procedure whose anonymous function assigns its own <c>Result</c> — leaves them the same symbols and succeeds.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    [Trait("Req", "CMP-751")]
    public void RoutineUsingResult_AlsoInAnAnonymousFunction_IsRenamed()
    {
        const string Source = """
            program P;
            type
              TIntFunc = reference to function(N: Integer): Integer;
              TCounter = class
                FBase: Integer;
                function Make(Start: Integer): TIntFunc;
              end;

            function Twice(N: Integer): Integer;
            begin
              Result := N * 2;
            end;

            function TCounter.Make(Start: Integer): TIntFunc;
            begin
              Result := function(N: Integer): Integer
                begin
                  Result := N + Start + Self.FBase;
                end;
            end;

            procedure Work;
            var
              F: TIntFunc;
            begin
              F := function(N: Integer): Integer
                begin
                  Result := Twice(N);
                end;
              WriteLn(F(1));
            end;

            begin
              Work;
            end.
            """;
        (string Name, string Text)[] files = [("P.pas", Source.ReplaceLineEndings("\n"))];
        void Renamed(string piece, string newName, int edits)
        {
            var result = Rename(files, "P.pas", piece, newName);
            Assert.True(result.Success, result.Error);
            Assert.Equal([$"P.pas: {edits}"], Counts(result));
            AssertCompiles(Applied(files, result));
        }

        Renamed("Twice(N: Integer)", "Double", 2);                             // a function that assigns Result
        Renamed("Make(Start", "Build", 2);                                     // a method: its Result, the anonymous function's and Self
        Renamed("TCounter = class", "TTally", 2);                              // the class of that method
        Renamed("Work;\nvar", "Labour", 2);                                    // a procedure around an anonymous function with Result
        Renamed("Start + Self", "First", 3);                                   // a captured parameter beside Result and Self
    }

    // ------------------------------------------------------------------------------------------------ CMP-1500…1504 (C-448)

    private const string FactsUnit = """
        unit UFacts;
        interface
        uses Unit1;
        type
          TColor = (Red, Green);
          TCustomer = class(TBase)
          private
            FName: string;
          public
            procedure Run; override;
            procedure Load(const Source: string);
          published
            Owner: TBase;
            procedure Changed;
          end;
        procedure Put(Value: Integer); overload;
        implementation
        procedure Put(const Note: string); overload; forward;
        procedure TCustomer.Run; begin end;
        procedure TCustomer.Changed; begin end;
        procedure TCustomer.Load(const Source: string);
        label Done;
        var Line: string;
          procedure Reset; begin Line := '' end;
        begin
          Line := Source;
          FName := Line;
          Reset;
          goto Done;
          Done:
        end;
        procedure Put(Value: Integer); begin Put('') end;
        procedure Put(const Note: string); begin end;
        end.
        """;

    /// <summary>The four-unit fixture with <c>UFacts</c> (a class derived from <c>Unit1.TBase</c>) used by the program.</summary>
    private static (string Name, string Text)[] FactsFixture() =>
        Fixture(("UFacts.pas", FactsUnit), ("P.pas", App.Replace("Unit3;", "Unit3, UFacts;", StringComparison.Ordinal)));

    /// <summary>
    /// CMP-1500: what the query says about the target — its kind, the name of its container, whether every occurrence is in the
    /// document (scope), where the closure is declared, whether it is published and how many routines of its name its container declares.
    /// </summary>
    [Theory]
    [Trait("Req", "CMP-1500")]
    [InlineData("UFacts.pas", "Line: string", CompletionItemKind.Variable, "TCustomer.Load", RenameScope.Local, "UFacts.pas", false, 0)]      // local
    [InlineData("UFacts.pas", "Source;", CompletionItemKind.Parameter, "TCustomer.Load", RenameScope.Local, "UFacts.pas", false, 0)]          // parameter
    [InlineData("UFacts.pas", "FName: string", CompletionItemKind.Field, "TCustomer", RenameScope.Project, "UFacts.pas", false, 0)]           // field
    [InlineData("Unit1.pas", "Run; virtual", CompletionItemKind.Method, "TBase", RenameScope.Project, "UFacts.pas,Unit1.pas", false, 1)]      // override in another unit
    [InlineData("UFacts.pas", "Run; override", CompletionItemKind.Method, "TCustomer", RenameScope.Project, "UFacts.pas,Unit1.pas", false, 1)]
    [InlineData("Unit1.pas", "Hello;", CompletionItemKind.Routine, "Unit1", RenameScope.Project, "Unit1.pas", false, 1)]                      // unit-level routine
    [InlineData("Unit1.pas", "TBase = class", CompletionItemKind.Class, "Unit1", RenameScope.Project, "Unit1.pas", false, 0)]                 // class
    [InlineData("Unit1.pas", "Unit1;", CompletionItemKind.Unit, "", RenameScope.Project, "Unit1.pas", false, 0)]                              // unit, at its heading
    [InlineData("P.pas", "Unit1,", CompletionItemKind.Unit, "", RenameScope.Project, "Unit1.pas", false, 0)]                                  // unit, at a uses entry
    [InlineData("Unit1.pas", "Shared: Integer", CompletionItemKind.Variable, "Unit1", RenameScope.Project, "Unit1.pas", false, 0)]            // unit-level variable
    [InlineData("UFacts.pas", "Reset; begin", CompletionItemKind.Routine, "TCustomer.Load", RenameScope.Local, "UFacts.pas", false, 1)]       // nested routine
    [InlineData("UFacts.pas", "Done;", CompletionItemKind.Label, "TCustomer.Load", RenameScope.Local, "UFacts.pas", false, 0)]                // label
    [InlineData("UFacts.pas", "Red", CompletionItemKind.EnumMember, "TColor", RenameScope.Project, "UFacts.pas", false, 0)]                   // enumeration value
    [InlineData("UFacts.pas", "Owner: TBase", CompletionItemKind.Field, "TCustomer", RenameScope.Project, "UFacts.pas", true, 0)]             // published field
    [InlineData("UFacts.pas", "Changed;", CompletionItemKind.Method, "TCustomer", RenameScope.Project, "UFacts.pas", true, 1)]                // published method
    [InlineData("UFacts.pas", "Put(Value", CompletionItemKind.Routine, "UFacts", RenameScope.Project, "UFacts.pas", false, 2)]                // overloads in both sections
    [InlineData("UFacts.pas", "Put(const Note", CompletionItemKind.Routine, "UFacts", RenameScope.Project, "UFacts.pas", false, 2)]
    public void GetRenameInfo_Facts_KindContainerScopeAndDeclarationPaths(string file, string piece, CompletionItemKind kind, string container,
        RenameScope scope, string declaredIn, bool published, int overloads)
    {
        var files = FactsFixture();
        AssertCompiles(files);

        var info = Info(files, file, piece);

        Assert.True(info.CanRename, info.Reason);
        Assert.Equal(kind, info.Kind);
        Assert.Equal(container, info.ContainerName);
        Assert.Equal(scope, info.Scope);
        Assert.Equal(declaredIn.Split(',').Select(TestFiles.PathOf), info.DeclarationPaths);
        Assert.Equal(published, info.IsPublished);
        Assert.Equal(overloads, info.OverloadCount);
        Assert.Equal(scope == RenameScope.Local, !info.LocalSpans.IsEmpty);
    }

    /// <summary>CMP-1500: a name that cannot be renamed has the default facts.</summary>
    [Fact]
    [Trait("Req", "CMP-1500")]
    public void GetRenameInfo_NotRenamable_FactsAreTheDefaults()
    {
        var files = FactsFixture();

        Assert.All([Info(files, "UFacts.pas", "Integer"), Info(files, "UFacts.pas", "implementation"), Info(files, "UFacts.pas", "string")], info =>
        {
            Assert.False(info.CanRename);
            Assert.Equal(default, info.Kind);
            Assert.Equal("", info.ContainerName);
            Assert.Equal(RenameScope.Project, info.Scope);
            Assert.Empty(info.LocalSpans);
            Assert.Empty(info.DeclarationPaths);
            Assert.False(info.IsPublished);
            Assert.Equal(0, info.OverloadCount);
        });
    }

    /// <summary>
    /// CMP-1500: for a target of scope <c>Local</c>, <c>LocalSpans</c> are the spans <c>Rename</c> edits — all of them in the document, in
    /// text order, the span under the caret among them. A parameter has both headings; a field and a method have no local spans.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1500")]
    public void GetRenameInfo_LocalScope_SpansEqualTheEditsOfRename()
    {
        void Local((string Name, string Text)[] files, string file, string piece, int count)
        {
            var info = Info(files, file, piece);
            var result = Rename(files, file, piece, "Renamed");

            Assert.True(info.CanRename, info.Reason);
            Assert.Equal(RenameScope.Local, info.Scope);
            Assert.True(result.Success, result.Error);
            var edit = Assert.Single(result.Edits);
            Assert.Equal(TestFiles.PathOf(file), edit.Path);
            Assert.Equal(edit.Changes.Select(c => c.Span), info.LocalSpans);
            Assert.Equal(count, info.LocalSpans.Length);
            Assert.Contains(info.Span, info.LocalSpans);
            Assert.All(info.LocalSpans, s => Assert.Equal(info.CurrentName, TextOf(files, file).Substring(s.Start, s.Length), ignoreCase: true));
        }

        var facts = FactsFixture();
        Local(facts, "UFacts.pas", "Line: string", 4);                         // declaration, the nested routine's use, two uses
        Local(facts, "UFacts.pas", "Source;", 3);                              // both headings and the use
        Local(facts, "UFacts.pas", "Source: string);\n  published", 3);        // the same from the heading in the class
        Local(facts, "UFacts.pas", "Reset; begin", 2);
        Local(facts, "UFacts.pas", "Done;", 3);
        var anonymous = AnonymousBodyFixture.Files;
        Local(anonymous, "P.pas", "Count: Integer;", 7);                       // a captured local
        Local(anonymous, "P.pas", "Count: Integer): Integer", 2);              // the parameter of an anonymous function
        Local(anonymous, "P.pas", "Note: Integer;", 4);                        // the local of an anonymous procedure
        Local(anonymous, "P.pas", "Before := 1", 2);                           // an inline variable
        Local(anonymous, "P.pas", "Seed: T);\n  end", 3);                      // a parameter of a generic class's method
        Local(anonymous, "P.pas", "Step: Integer", 2);                         // the parameter of an anonymous function of the main block
        // inside an anonymous method the container is the routine the anonymous method is written in, or the unit
        Assert.Equal("TCounter.MakeAdder", Info(anonymous, "P.pas", "Note: Integer;").ContainerName);
        Assert.Equal("TBox<T>.Run", Info(anonymous, "P.pas", "Mine: T").ContainerName);
        Assert.Equal("P", Info(anonymous, "P.pas", "Step: Integer").ContainerName);

        void Project((string Name, string Text)[] files, string file, string piece, string container)
        {
            var info = Info(files, file, piece);
            Assert.True(info.CanRename, info.Reason);
            Assert.Equal(RenameScope.Project, info.Scope);
            Assert.Empty(info.LocalSpans);
            Assert.Equal(container, info.ContainerName);
        }

        Project(facts, "UFacts.pas", "FName: string", "TCustomer");            // a field
        Project(facts, "UFacts.pas", "Load(const", "TCustomer");               // a method
        Project(anonymous, "P.pas", "Outer := 5", "P");                        // an inline variable of the main block: declared in no routine

        // an occurrence, or a heading, in an include file is not in the document
        (string Name, string Text)[] included =
        [
            ("P.pas", "program P;\nuses UInc;\nbegin\n  Work(1);\n  Other(2);\nend.\n"),
            ("UInc.pas", "unit UInc;\ninterface\nprocedure Work(Count: Integer);\nprocedure Other(Value: Integer);\nimplementation\nprocedure Work(Count: Integer);\n"
                         + "var Total, Rest: Integer;\nbegin\n  Total := Count;\n  Rest := 2;\n  {$I body.inc}\nend;\n{$I other.inc}\nend.\n"),
            ("body.inc", "Total := Total + 1;\n"),
            ("other.inc", "procedure Other(Value: Integer);\nbegin\nend;\n"),
        ];
        AssertCompiles(included);
        Local(included, "UInc.pas", "Rest: Integer", 2);
        Local(included, "UInc.pas", "Count: Integer", 3);
        Project(included, "UInc.pas", "Total, Rest", "Work");                  // used in body.inc
        Project(included, "UInc.pas", "Value: Integer", "Other");              // the implementation heading is in other.inc
    }

    private static void AssertSame(RenameResult expected, RenameResult actual)
    {
        Assert.Equal(expected.Success, actual.Success);
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.FileRename, actual.FileRename);
        Assert.Equal(expected.Edits.Select(e => e.Path), actual.Edits.Select(e => e.Path));
        Assert.All(expected.Edits.Zip(actual.Edits), pair => Assert.Equal(pair.First.Changes.AsEnumerable(), pair.Second.Changes));
    }

    private static RenameResult Rename((string Name, string Text)[] files, string file, string piece, string newName, RenameOptions options)
    {
        var doc = MarkupTestFile.Open(file, files);
        return RenameService.Rename(doc, At(TextOf(files, file), piece), newName, options, TestContext.Current.CancellationToken);
    }

    /// <summary>CMP-1501: the default options are forms on, no form texts, one overload; with them the result is that of the four-parameter call.</summary>
    [Fact]
    [Trait("Req", "CMP-1501")]
    public void Options_Default_SameResultAsTheFourParameterRename()
    {
        Assert.True(RenameOptions.Default.Forms);
        Assert.Null(RenameOptions.Default.FormTexts);
        Assert.False(RenameOptions.Default.Overloads);
        Assert.Equal(new RenameOptions(), RenameOptions.Default);

        void Same((string Name, string Text)[] files, string file, string piece, string newName, bool success, int editedFiles)
        {
            var four = Rename(files, file, piece, newName);
            Assert.Equal(success, four.Success);
            Assert.Equal(editedFiles, four.Edits.Length);
            AssertSame(four, Rename(files, file, piece, newName, RenameOptions.Default));
            AssertSame(four, Rename(files, file, piece, newName, new RenameOptions()));
        }

        Same(Fixture(), "Unit1.pas", "Total := Shared", "Sum", true, 1);              // a local
        Same(Fixture(), "Unit1.pas", "Shared: Integer", "Common", true, 2);           // a global used in two files
        Same(Fixture(), "Unit1.pas", "Shared: Integer", "Shared", true, 0);           // unchanged: no edits
        Same(Fixture(), "Unit1.pas", "Run; virtual", "Execute", true, 2);             // an override chain
        Same(Fixture(), "Unit1.pas", "Shared: Integer", "Hello", false, 0);           // a conflict
        Same(Fixture(), "Unit1.pas", "Shared: Integer", "1abc", false, 0);            // an invalid name
        Same(Fixture(), "Unit1.pas", "Integer", "Whole", false, 0);                   // not renamable
        Same(Fixture(), "Unit1.pas", "Unit1;", "Core", true, 4);                      // a unit, with its file rename
        Same(FormFixture(), "UMain.pas", "Button1: TButton", "OkButton", true, 2);    // a form's component
        Same(FormFixture(), "UMain.pas", "Clicked(Sender: TObject);", "ButtonClick", true, 2);
        var unreadable = FormFixture();
        unreadable[2] = ("UMain.form", "object Form1: TForm1\n  Left = = 1\nend\n");
        Same(unreadable, "UMain.pas", "Button1: TButton", "OkButton", false, 0);      // a form that cannot be scanned
        Same(OverloadFixture(), "UOver.pas", "Draw(Scale: Integer); overload", "Zoom", true, 2);
    }

    private const string FormWithThreeHandlers =
        "object Form1: TForm1\n  OnShow = Clicked\n  object Button1: TButton\n    OnClick = Clicked\n  end\n  object Button2: TButton\n    OnClick = Clicked\n  end\nend\n";

    private static RenameOptions FormText(string path, string text) =>
        new() { FormTexts = new Dictionary<string, string> { [path] = text } };

    /// <summary>
    /// CMP-1502: the form on disk has CRLF line ends and two handler references; the text given for it (the designer's, unsaved) has
    /// LF line ends and a third one. The edits are computed against the given text — also when the key is spelled another way or
    /// the file does not exist; without the option against the disk text.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1502")]
    public void Options_FormTexts_EditsAreAgainstTheGivenText()
    {
        const string Piece = "Clicked(Sender: TObject);";
        var files = FormFixture();
        var formPath = TestFiles.PathOf("UMain.form");

        var disk = Rename(files, "UMain.pas", Piece, "ButtonClick");
        var given = Rename(files, "UMain.pas", Piece, "ButtonClick", FormText(formPath, FormWithThreeHandlers));
        var otherSpelling = Rename(files, "UMain.pas", Piece, "ButtonClick", FormText("c:/P/sub/../umain.FORM", FormWithThreeHandlers));
        var otherKey = Rename(files, "UMain.pas", Piece, "ButtonClick", FormText(TestFiles.PathOf("Other.form"), "not a form"));
        var relativeKey = Rename(files, "UMain.pas", Piece, "ButtonClick", FormText("UMain.form", "not a form"));
        var field = Rename(files, "UMain.pas", "Button2: TButton", "Cancel", FormText(formPath, FormWithThreeHandlers));
        (string Name, string Text)[] noFile = [files[0], files[1]];
        var withoutFile = Rename(noFile, "UMain.pas", Piece, "ButtonClick", FormText(formPath, FormWithThreeHandlers));

        Assert.Equal(["UMain.form: 2", "UMain.pas: 3"], Counts(disk));
        Assert.Equal(Form.Replace("Clicked", "ButtonClick", StringComparison.Ordinal), Apply(Form, disk.Edits.Single(e => e.Path == formPath).Changes));
        Assert.All([given, otherSpelling, withoutFile], result =>
        {
            Assert.True(result.Success, result.Error);
            Assert.Equal(["UMain.form: 3", "UMain.pas: 3"], Counts(result));
            var changes = result.Edits.Single(e => e.Path == formPath).Changes;
            Assert.Equal(FormWithThreeHandlers.Replace("Clicked", "ButtonClick", StringComparison.Ordinal), Apply(FormWithThreeHandlers, changes));
            Assert.All(changes, c => Assert.Equal("Clicked", FormWithThreeHandlers.Substring(c.Span.Start, c.Span.Length)));
            Assert.Equal(disk.Edits.Single(e => e.Path != formPath).Changes.AsEnumerable(), result.Edits.Single(e => e.Path != formPath).Changes);
        });
        AssertSame(disk, otherKey);                                             // a key that names no embedded form is ignored
        AssertSame(disk, relativeKey);
        Assert.Equal(["UMain.form: 1", "UMain.pas: 1"], Counts(field));
        Assert.Equal(FormWithThreeHandlers.Replace("Button2", "Cancel", StringComparison.Ordinal),
            Apply(FormWithThreeHandlers, field.Edits.Single(e => e.Path == formPath).Changes));
    }

    /// <summary>CMP-1502: a given text the scanner rejects fails the rename like an unreadable file (CMP-753), although the file on disk is fine.</summary>
    [Fact]
    [Trait("Req", "CMP-1502")]
    public void Options_FormTexts_TextThatCannotBeScannedFails()
    {
        var files = FormFixture();
        var broken = FormText(TestFiles.PathOf("UMain.form"), "object Form1: TForm1\n  Left = = 1\nend\n");

        var field = Rename(files, "UMain.pas", "Button1: TButton", "OkButton", broken);
        var method = Rename(files, "UMain.pas", "Clicked(Sender: TObject);", "ButtonClick", broken);
        var plain = Rename(files, "UMain.pas", "Plain: Integer", "Simple", broken);

        Assert.All([field, method], result =>
        {
            Assert.False(result.Success);
            Assert.Equal("The form file 'UMain.form' cannot be read, so its references cannot be updated.", result.Error);
            Assert.Empty(result.Edits);
        });
        Assert.True(Rename(files, "UMain.pas", "Button1: TButton", "OkButton").Success);   // the file on disk is fine
        Assert.True(plain.Success, plain.Error);                                // not a published member: no form is looked at
    }

    /// <summary>Counts the reads of <c>.form</c> files.</summary>
    private sealed class FormReads(IFileSystem inner) : IFileSystem
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        private void Note(string path)
        {
            if (path.EndsWith(".form", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _count);
            }
        }

        public bool FileExists(string path) => inner.FileExists(path);

        public string ReadAllText(string path)
        {
            Note(path);
            return inner.ReadAllText(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            Note(path);
            return inner.ReadAllBytes(path);
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

        public long GetLength(string path) => inner.GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

        public void Delete(string path) => inner.Delete(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);
    }

    /// <summary>
    /// CMP-1503: with <c>Forms = false</c> no form is read — one the scanner rejects does not fail the rename — and no edit of a form
    /// is returned, also when a text is given for it; the code edits and the conflict check are those of a rename with forms.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1503")]
    public void Options_FormsOff_NoFormIsReadAndNoFormEdit()
    {
        const string Method = "Clicked(Sender: TObject);";
        var off = new RenameOptions { Forms = false };
        var files = FormFixture();
        var unreadable = FormFixture();
        unreadable[2] = ("UMain.form", "object Form1: TForm1\n  Left = = 1\nend\n");
        int Reads(string piece, string newName, RenameOptions options, out RenameResult result)
        {
            var fs = new FormReads(TestFiles.Files(unreadable));
            var workspace = new Workspace(fs);
            workspace.SetProject(TestFiles.Options());
            var doc = workspace.GetDocument(TestFiles.PathOf("UMain.pas"));
            result = RenameService.Rename(doc, At(TextOf(unreadable, "UMain.pas"), piece), newName, options, TestContext.Current.CancellationToken);
            return fs.Count;
        }

        // the binder reads an embedded form when it binds the unit (LANG-670), for the document and for the conflict check: a rename
        // of a member that is not published reads it that often, and so does a rename of a component with Forms off
        var readsOfTheBinder = Reads("Plain: Integer", "Simple", RenameOptions.Default, out var plain);
        var readsOff = Reads("Button1: TButton", "OkButton", off, out var fieldOff);
        var readsOn = Reads("Button1: TButton", "OkButton", RenameOptions.Default, out var fieldOn);
        var methodOff = Rename(unreadable, "UMain.pas", Method, "ButtonClick", off);
        var withText = Rename(files, "UMain.pas", Method, "ButtonClick",
            new RenameOptions { Forms = false, FormTexts = new Dictionary<string, string> { [TestFiles.PathOf("UMain.form")] = FormWithThreeHandlers } });
        var withForms = Rename(files, "UMain.pas", Method, "ButtonClick");
        var conflictOff = Rename(files, "UMain.pas", "Button1: TButton", "Button2", off);
        var conflictOn = Rename(files, "UMain.pas", "Button1: TButton", "Button2");

        Assert.True(plain.Success, plain.Error);
        Assert.Equal(readsOfTheBinder, readsOff);
        Assert.Equal(readsOfTheBinder + 1, readsOn);
        Assert.False(fieldOn.Success);
        Assert.True(fieldOff.Success, fieldOff.Error);
        Assert.Equal(["UMain.pas: 2"], Counts(fieldOff));
        Assert.Equal(["UMain.pas: 3"], Counts(methodOff));
        Assert.Equal(["UMain.pas: 3"], Counts(withText));
        Assert.Equal(withForms.Edits.Single(e => e.Path.EndsWith(".pas", StringComparison.Ordinal)).Changes.AsEnumerable(), Assert.Single(methodOff.Edits).Changes);
        Assert.False(conflictOn.Success);
        AssertSame(conflictOn, conflictOff);
    }

    private const string OverloadUnit = """
        unit UOver;
        interface
        type
          TShape = class
            procedure Draw; overload; virtual;
            procedure Draw(Scale: Integer); overload;
            procedure Draw(const Title: string); overload;
          end;
          TCircle = class(TShape)
            procedure Draw; overload; override;
            procedure Draw(Radius: Double); overload;
          end;
        implementation
        procedure TShape.Draw; begin end;
        procedure TShape.Draw(Scale: Integer); begin Draw end;
        procedure TShape.Draw(const Title: string); begin Draw(1) end;
        procedure TCircle.Draw; begin inherited Draw; Draw(2); Draw('x') end;
        procedure TCircle.Draw(Radius: Double); begin Draw end;
        end.
        """;

    private static (string Name, string Text)[] OverloadFixture() =>
    [
        ("P.pas", "program P;\nuses UOver;\nvar C: TCircle;\nbegin\n  C.Draw;\n  C.Draw(3);\n  C.Draw('t');\n  C.Draw(1.5);\nend.\n"),
        ("UOver.pas", OverloadUnit.ReplaceLineEndings("\n")),
    ];

    /// <summary>
    /// CMP-1504: three overloads of a class, one of them virtual and overridden in a descendant that adds an overload of its own.
    /// With <c>Overloads</c> the three, the override and — through it — the descendant's own overload are renamed, from whichever of
    /// them the rename starts: no <c>Draw</c> is left and the project compiles.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1504")]
    [Trait("Req", "CMP-1500")]
    public void Options_Overloads_RenamesTheWholeGroup()
    {
        var group = new RenameOptions { Overloads = true };
        var files = OverloadFixture();
        AssertCompiles(files);
        Assert.Equal(3, Info(files, "UOver.pas", "Draw(Scale: Integer); overload").OverloadCount);
        Assert.Equal(3, Info(files, "UOver.pas", "Draw; overload; virtual").OverloadCount);
        Assert.Equal(2, Info(files, "UOver.pas", "Draw; overload; override").OverloadCount);
        Assert.Equal(3, Info(files, "P.pas", "Draw(3)").OverloadCount);

        (string File, string Piece)[] starts =
        [
            ("UOver.pas", "Draw(Scale: Integer); overload"), ("UOver.pas", "Draw(const Title"), ("UOver.pas", "Draw; overload; virtual"),
            ("UOver.pas", "Draw(Radius"), ("UOver.pas", "Draw(2)"), ("P.pas", "Draw('t')"),
        ];
        Assert.All(starts, start =>
        {
            var result = Rename(files, start.File, start.Piece, "Paint", group);

            Assert.Equal(["P.pas: 4", "UOver.pas: 16"], Counts(result));
            var after = Applied(files, result);
            Assert.Equal(TextOf(files, "UOver.pas").Replace("Draw", "Paint", StringComparison.Ordinal), TextOf(after, "UOver.pas"));
            Assert.Equal(TextOf(files, "P.pas").Replace("Draw", "Paint", StringComparison.Ordinal), TextOf(after, "P.pas"));
            AssertCompiles(after);
        });

        // unit-level routines, declared in both sections of the unit: the declarations, both implementation headings and the call
        var put = Rename(FactsFixture(), "UFacts.pas", "Put(Value", "Store", group);
        Assert.Equal(["UFacts.pas: 5"], Counts(put));
        AssertCompiles(Applied(FactsFixture(), put));
        Assert.Equal(["UFacts.pas: 2"], Counts(Rename(FactsFixture(), "UFacts.pas", "Put(Value", "Store")));

        // the overloads of an interface: with them their implementations, and with those the class's own overload
        (string Name, string Text)[] shapes =
        [
            ("P.pas", "program P;\ntype\n  IShape = interface\n    procedure Draw; overload;\n    procedure Draw(Scale: Integer); overload;\n  end;\n"
                      + "  TShape = class(TInterfacedObject, IShape)\n    procedure Draw; overload;\n    procedure Draw(Scale: Integer); overload;\n"
                      + "    procedure Draw(const Title: string); overload;\n  end;\nprocedure TShape.Draw; begin end;\nprocedure TShape.Draw(Scale: Integer); begin end;\n"
                      + "procedure TShape.Draw(const Title: string); begin end;\nvar S: IShape;\nbegin\n  S := TShape.Create;\n  S.Draw;\n  S.Draw(1);\nend.\n"),
        ];
        AssertCompiles(shapes);
        Assert.Equal(2, Info(shapes, "P.pas", "Draw; overload").OverloadCount);
        var all = Rename(shapes, "P.pas", "Draw; overload", "Paint", group);
        Assert.Equal(["P.pas: 10"], Counts(all));
        Assert.Equal(TextOf(shapes, "P.pas").Replace("Draw", "Paint", StringComparison.Ordinal), TextOf(Applied(shapes, all), "P.pas"));
        Assert.Equal(["P.pas: 4"], Counts(Rename(shapes, "P.pas", "Draw; overload", "Paint")));       // one overload: both declarations, the heading, the call

        // another kind, and a routine that has no overload: the option changes nothing
        AssertSame(Rename(files, "UOver.pas", "TShape = class", "TFigure"), Rename(files, "UOver.pas", "TShape = class", "TFigure", group));
        AssertSame(Rename(Fixture(), "Unit1.pas", "Hello;", "Greet"), Rename(Fixture(), "Unit1.pas", "Hello;", "Greet", group));
    }

    /// <summary>CMP-1504: without the option — and with the four-parameter call — only the overload at the position, with its override chain, is renamed.</summary>
    [Fact]
    [Trait("Req", "CMP-1504")]
    public void Options_OverloadsOff_RenamesOnlyTheOverloadAtThePosition()
    {
        var files = OverloadFixture();

        var one = Rename(files, "UOver.pas", "Draw(Scale: Integer); overload", "Zoom", new RenameOptions { Overloads = false });
        var chain = Rename(files, "UOver.pas", "Draw; overload; virtual", "Paint", RenameOptions.Default);

        Assert.Equal(["P.pas: 1", "UOver.pas: 4"], Counts(one));                   // declaration, implementation heading, Draw(1), Draw(2); C.Draw(3)
        var afterOne = Applied(files, one);
        Assert.Contains("procedure Zoom(Scale: Integer); overload;", TextOf(afterOne, "UOver.pas"), StringComparison.Ordinal);
        Assert.Contains("procedure Draw; overload; virtual;", TextOf(afterOne, "UOver.pas"), StringComparison.Ordinal);
        Assert.Contains("begin inherited Draw; Zoom(2); Draw('x') end;", TextOf(afterOne, "UOver.pas"), StringComparison.Ordinal);
        Assert.Contains("C.Draw;\n  C.Zoom(3);\n  C.Draw('t');\n  C.Draw(1.5);", TextOf(afterOne, "P.pas"), StringComparison.Ordinal);
        AssertCompiles(afterOne);
        AssertSame(Rename(files, "UOver.pas", "Draw(Scale: Integer); overload", "Zoom"), one);

        Assert.Equal(["P.pas: 1", "UOver.pas: 7"], Counts(chain));                 // two declarations, two headings, Draw in two bodies, inherited Draw; C.Draw
        var afterChain = Applied(files, chain);
        Assert.Contains("procedure Paint; overload; override;", TextOf(afterChain, "UOver.pas"), StringComparison.Ordinal);
        Assert.Contains("procedure Draw(Radius: Double); overload;", TextOf(afterChain, "UOver.pas"), StringComparison.Ordinal);
        AssertCompiles(afterChain);
    }
    private const string GenericOverloadUnit = """
        unit UGen;
        interface
        type
          PNode = ^TNode;
          TNode = record
            Next: PNode;
            procedure Init;
            function Same<T>(Item: T): T; overload;
            function Same(Item: Integer; Extra: Integer): Integer; overload;
          end;
          TUtil = class
            class function Max<T>(A, B: T): T; overload; static;
            class function Max(A: Integer): Integer; overload; static;
            function Keep<T>(Value: T): T;
          end;
        implementation
        procedure TNode.Init; begin Next := nil end;
        function TNode.Same<T>(Item: T): T; begin Result := Item end;
        function TNode.Same(Item: Integer; Extra: Integer): Integer; begin Result := Item + Extra end;
        class function TUtil.Max<T>(A, B: T): T; begin Result := A end;
        class function TUtil.Max(A: Integer): Integer; begin Result := A end;
        function TUtil.Keep<T>(Value: T): T; begin Result := Value end;
        end.
        """;

    private static (string Name, string Text)[] GenericOverloadFixture() =>
    [
        ("P.pas", "program P;\nuses UGen;\nvar I: Integer; N: TNode; U: TUtil;\nbegin\n  I := TUtil.Max<Integer>(1, 2);\n  I := TUtil.Max(1);\n  I := TUtil.Max(3, 4);\n"
                  + "  I := N.Same(5);\n  I := N.Same(5, 6);\n  U := TUtil.Create;\n  I := U.Keep(I);\nend.\n"),
        ("UGen.pas", GenericOverloadUnit.ReplaceLineEndings("\n")),
    ];

    /// <summary>A document of the fixture in a project bound on <paramref name="jobs"/> threads (0: the default, one per processor).</summary>
    private static Document OpenWithJobs((string Name, string Text)[] files, string file, int jobs)
    {
        var workspace = new Workspace(TestFiles.Files(files));
        workspace.SetProject(TestFiles.Options() with { MaxParallelism = jobs });
        return workspace.GetDocument(TestFiles.PathOf(file));
    }

    /// <summary>
    /// CMP-1504 on C-455: an overload group with a generic method — <c>Max&lt;T&gt;</c> and <c>Max</c> of a class, <c>Same&lt;T&gt;</c> and
    /// <c>Same</c> of a record — is renamed as a group from either member, and so is another symbol of that unit. The conflict check
    /// binds a fork in which both files changed; a parameter in the declaration of the generic method was a
    /// <c>ParameterSymbol</c> before and nothing in the fork whenever the program's bodies were bound before the unit's
    /// implementation part (with one thread always), so the rename was refused: "changes the meaning of 'A'".
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [Trait("Req", "CMP-1504")]
    [Trait("Req", "CMP-751")]
    public void Options_Overloads_GroupWithAGenericMethod_Renames(int jobs)
    {
        var group = new RenameOptions { Overloads = true };
        var files = GenericOverloadFixture();
        AssertCompiles(files);
        RenameResult Run(string file, string piece, string newName, RenameOptions options) =>
            RenameService.Rename(OpenWithJobs(files, file, jobs), At(TextOf(files, file), piece), newName, options, TestContext.Current.CancellationToken);

        (string File, string Piece)[] starts =
        [
            ("UGen.pas", "Max<T>(A, B: T): T; overload"), ("UGen.pas", "Max(A: Integer): Integer; overload"), ("P.pas", "Max<Integer>"), ("P.pas", "Max(1)"),
        ];
        Assert.All(starts, start =>
        {
            Assert.Equal(2, RenameService.GetRenameInfo(OpenWithJobs(files, start.File, jobs), At(TextOf(files, start.File), start.Piece), TestContext.Current.CancellationToken).OverloadCount);
            var result = Run(start.File, start.Piece, "Largest", group);

            Assert.Equal(["P.pas: 3", "UGen.pas: 4"], Counts(result));
            var after = Applied(files, result);
            Assert.Equal(TextOf(files, "UGen.pas").Replace("Max", "Largest", StringComparison.Ordinal), TextOf(after, "UGen.pas"));
            Assert.Equal(TextOf(files, "P.pas").Replace("Max", "Largest", StringComparison.Ordinal), TextOf(after, "P.pas"));
            AssertCompiles(after);
        });

        // the group of a record, from the generic member and from the plain one
        foreach (var piece in new[] { "Same<T>(Item: T): T; overload", "Same(Item: Integer; Extra" })
        {
            var same = Run("UGen.pas", piece, "Alike", group);
            Assert.Equal(["P.pas: 2", "UGen.pas: 4"], Counts(same));
            AssertCompiles(Applied(files, same));
        }

        // each overload alone, and other symbols of the unit: the generic methods' declarations do not stand in the way
        Assert.Equal(["P.pas: 2", "UGen.pas: 2"], Counts(Run("UGen.pas", "Max<T>(A, B: T): T; overload", "Largest", RenameOptions.Default)));
        Assert.Equal(["P.pas: 1", "UGen.pas: 2"], Counts(Run("UGen.pas", "Max(A: Integer): Integer; overload", "Largest", RenameOptions.Default)));
        Assert.Equal(["UGen.pas: 2"], Counts(Run("UGen.pas", "PNode = ^TNode", "PLink", RenameOptions.Default)));
        Assert.Equal(["UGen.pas: 2"], Counts(Run("UGen.pas", "Next: PNode", "Following", RenameOptions.Default)));
        Assert.Equal(["P.pas: 1", "UGen.pas: 2"], Counts(Run("UGen.pas", "Keep<T>(Value: T): T;", "Hold", group)));

        // a parameter of a generic method, from its declaration and from its implementation: both headings and the body
        foreach (var piece in new[] { "A, B: T): T; overload", "A, B: T): T; begin" })
        {
            var parameter = Run("UGen.pas", piece, "First", RenameOptions.Default);
            Assert.Equal(["UGen.pas: 3"], Counts(parameter));
            Assert.Contains("class function TUtil.Max<T>(First, B: T): T; begin Result := First end;", TextOf(Applied(files, parameter), "UGen.pas"), StringComparison.Ordinal);
        }
    }
}
