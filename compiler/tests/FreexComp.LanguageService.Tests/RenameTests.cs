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
}
