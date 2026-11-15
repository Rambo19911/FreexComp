using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// api.md §4.2, the five basic edits of C-141: <c>InsertMember</c>, <c>InsertMethodImplementation</c> (CMP-254),
/// <c>RenameMethod</c> (CMP-255), <c>RemoveMember</c>, <c>AddUsesEntry</c> (CMP-257). Every case applies the changes,
/// re-parses (no new syntax diagnostics, round trip, CMP-259) and compares the exact resulting text.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class SyntaxEditsTests
{
    private const string Path = @"C:\p\Unit1.pas";

    private const string FormUnit = """
        unit Unit1;

        interface

        uses
          Classes, Forms;

        type
          TForm1 = class(TForm)
            Button1: TButton;
            procedure Button1Click(Sender: TObject);
          private
            FCount: Integer;
          public
            procedure Show;
          end;

        var
          Form1: TForm1;

        implementation

        {$FORM *.form}

        procedure TForm1.Button1Click(Sender: TObject);
        begin
        end;

        procedure TForm1.Show;
        begin
        end;

        end.
        """;

    private static SyntaxTree Parse(string text) => SyntaxTree.Parse(SourceText.From(Path, text.ReplaceLineEndings("\r\n")));

    /// <summary>Applies the changes in descending offset order, re-parses and checks CMP-259 and the round trip.</summary>
    private static string Apply(SyntaxTree tree, params TextChange[] changes)
    {
        var ordered = changes.Where(c => !c.IsEmpty).OrderBy(c => c.Span.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i - 1].Span.End <= ordered[i].Span.Start, "changes overlap");
        }

        var text = tree.Text.Text;
        foreach (var change in Enumerable.Reverse(ordered))
        {
            text = text.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
        }

        var reparsed = SyntaxTree.Parse(SourceText.From(Path, text));
        Assert.Equal(text, reparsed.Root.ToFullString());
        Assert.Equal(tree.Diagnostics.Select(d => d.Code), reparsed.Diagnostics.Select(d => d.Code));
        return text;
    }

    private static string Expected(string text) => text.ReplaceLineEndings("\r\n");

    private static TypeDeclarationSyntax Type(SyntaxTree tree, string name) =>
        tree.Root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(t => t.Identifier.Text == name);

    private static UnitSyntax Unit(SyntaxTree tree) => (UnitSyntax)tree.Root;

    // ------------------------------------------------------------------ InsertMember

    [Fact]
    public void InsertMember_IntoAnExistingSection_GoesAfterItsLastMember()
    {
        var tree = Parse(FormUnit);

        var text = Apply(tree, SyntaxEdits.InsertMember(Type(tree, "TForm1"), "procedure Increment;", Visibility.Private));

        Assert.Equal(Expected(FormUnit).Replace("    FCount: Integer;\r\n", "    FCount: Integer;\r\n    procedure Increment;\r\n"), text);
    }

    [Fact]
    public void InsertMember_IntoAnEmptySection_GoesAfterTheVisibilityLine()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class
              private
              public
              end;
            implementation
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.InsertMember(Type(tree, "TFoo"), "FName: string;", Visibility.Private));

        Assert.Equal(Expected(source).Replace("  private\r\n", "  private\r\n    FName: string;\r\n"), text);
    }

    [Fact]
    public void InsertMember_MissingSection_IsCreatedBeforeTheEnd()
    {
        var tree = Parse(FormUnit);

        var text = Apply(tree, SyntaxEdits.InsertMember(Type(tree, "TForm1"), "procedure Paint; virtual;", Visibility.StrictProtected));

        Assert.Equal(
            Expected(FormUnit).Replace("    procedure Show;\r\n  end;", "    procedure Show;\r\n  strict protected\r\n    procedure Paint; virtual;\r\n  end;"),
            text);
    }

    [Fact]
    public void InsertMember_ClassEndOnTheHeadingLine_SplitsTheLine()
    {
        var source = """
            unit U;
            interface
            type
              TFoo = class end;
            implementation
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.InsertMember(Type(tree, "TFoo"), "X: Integer;", Visibility.Public));

        Assert.Equal(Expected(source).Replace("  TFoo = class end;", "  TFoo = class\r\n  public\r\n    X: Integer;\r\n  end;"), text);
    }

    [Fact]
    public void InsertMember_NotAClass_IsEmpty()
    {
        var tree = Parse("unit U; interface type TA = array of Integer; implementation end.");

        Assert.True(SyntaxEdits.InsertMember(Type(tree, "TA"), "X: Integer;", Visibility.Public).IsEmpty);
    }

    // ------------------------------------------------------------------ InsertMethodImplementation (CMP-254)

    private const string Hide = "procedure TForm1.Hide;\r\nbegin\r\nend;\r\n";

    [Fact]
    public void InsertMethodImplementation_AfterTheLastMethodOfTheClass()
    {
        var tree = Parse(FormUnit);

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TForm1", Hide));

        Assert.Equal(Expected(FormUnit).Replace("procedure TForm1.Show;\r\nbegin\r\nend;\r\n\r\n",
            "procedure TForm1.Show;\r\nbegin\r\nend;\r\n\r\n" + Hide + "\r\n"), text);
    }

    [Fact]
    public void InsertMethodImplementation_NoMethodOfTheClass_AfterTheFormDirective()
    {
        var source = """
            unit Unit1;
            interface
            uses Forms;
            type
              TForm1 = class(TForm)
              end;
            implementation
            uses Other;
            {$FORM *.form}
            procedure Helper;
            begin
            end;

            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TForm1", Hide));

        Assert.Equal(Expected(source).Replace("{$FORM *.form}\r\n", "{$FORM *.form}\r\n\r\n" + Hide + "\r\n"), text);
    }

    [Fact]
    public void InsertMethodImplementation_NoDirective_AfterTheImplementationUses()
    {
        var source = """
            unit Unit1;
            interface
            type
              TForm1 = class
              end;
            implementation
            uses
              Other;


            procedure Helper;
            begin
            end;

            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TForm1", Hide));

        // the two blank lines after the clause become one, one more blank line separates it from Helper
        Assert.Equal(Expected(source).Replace("  Other;\r\n\r\n\r\n", "  Other;\r\n\r\n" + Hide + "\r\n"), text);
    }

    [Fact]
    public void InsertMethodImplementation_NothingElse_AfterTheImplementationKeywordAndBeforeInitialization()
    {
        var source = """
            unit Unit1;
            interface
            type
              TForm1 = class
              end;
            implementation
            initialization
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.InsertMethodImplementation(Unit(tree), "TForm1", Hide.TrimEnd()));

        Assert.Equal(Expected(source).Replace("implementation\r\n", "implementation\r\n\r\n" + Hide + "\r\n"), text);
    }

    [Fact]
    public void InsertMethodImplementation_ClassComparisonIsCaseInsensitive()
    {
        var tree = Parse(FormUnit);

        var change = SyntaxEdits.InsertMethodImplementation(Unit(tree), "tform1", Hide);

        Assert.Equal(SyntaxEdits.InsertMethodImplementation(Unit(tree), "TForm1", Hide), change);
        Assert.Equal(Expected(FormUnit).IndexOf("\r\nend.", StringComparison.Ordinal) + 2, change.Span.End);
    }

    // ------------------------------------------------------------------ RenameMethod (CMP-255)

    [Fact]
    public void RenameMethod_DeclarationAndImplementationHeading()
    {
        var tree = Parse(FormUnit);

        var changes = SyntaxEdits.RenameMethod(Unit(tree), "TForm1", "button1click", "OkClick");

        Assert.Equal(2, changes.Length);
        var text = Apply(tree, [.. changes]);
        Assert.Equal(Expected(FormUnit).Replace("procedure Button1Click(", "procedure OkClick(")
            .Replace("TForm1.Button1Click(", "TForm1.OkClick("), text);
    }

    [Fact]
    public void RenameMethod_WithoutImplementation_IsOneChange_AndUnknownIsNone()
    {
        var tree = Parse("unit U; interface type TFoo = class procedure A; end; implementation end.");

        Assert.Single(SyntaxEdits.RenameMethod(Unit(tree), "TFoo", "A", "B"));
        Assert.Empty(SyntaxEdits.RenameMethod(Unit(tree), "TFoo", "Missing", "B"));
        Assert.Empty(SyntaxEdits.RenameMethod(Unit(tree), "TBar", "A", "B"));
    }

    [Fact]
    public void RenameMethod_CallsAreNotTouched()
    {
        var source = """
            unit U;
            interface
            type TFoo = class procedure A; procedure B; end;
            implementation
            procedure TFoo.A; begin end;
            procedure TFoo.B; begin A; end;
            end.
            """;
        var tree = Parse(source);

        var text = Apply(tree, [.. SyntaxEdits.RenameMethod(Unit(tree), "TFoo", "A", "C")]);

        Assert.Equal(Expected(source).Replace("procedure A;", "procedure C;").Replace("TFoo.A;", "TFoo.C;"), text);
    }

    // ------------------------------------------------------------------ RemoveMember

    [Fact]
    public void RemoveMember_WholeLine()
    {
        var tree = Parse(FormUnit);
        var field = Type(tree, "TForm1").DescendantNodes().OfType<FieldDeclarationSyntax>().First(f => f.ToString().StartsWith("FCount", StringComparison.Ordinal));

        var text = Apply(tree, SyntaxEdits.RemoveMember(field));

        Assert.Equal(Expected(FormUnit).Replace("    FCount: Integer;\r\n", ""), text);
    }

    [Fact]
    public void RemoveMember_WithTrailingComment_RemovesTheComment()
    {
        var source = "unit U;\r\ninterface\r\ntype\r\n  TFoo = class\r\n    A: Integer; // the count\r\n    B: Byte;\r\n  end;\r\nimplementation\r\nend.\r\n";
        var tree = Parse(source);
        var member = Type(tree, "TFoo").DescendantNodes().OfType<FieldDeclarationSyntax>().First();

        Assert.Equal(source.Replace("    A: Integer; // the count\r\n", ""), Apply(tree, SyntaxEdits.RemoveMember(member)));
    }

    [Fact]
    public void RemoveMember_SharingALine_RemovesOnlyTheMember()
    {
        var source = "unit U;\r\ninterface\r\ntype\r\n  TFoo = class A: Integer; B: Byte; end;\r\nimplementation\r\nend.\r\n";
        var tree = Parse(source);
        var fields = Type(tree, "TFoo").DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();

        Assert.Equal(source.Replace("A: Integer; ", ""), Apply(tree, SyntaxEdits.RemoveMember(fields[0])));
        Assert.Equal(source.Replace(" B: Byte;", ""), Apply(tree, SyntaxEdits.RemoveMember(fields[1])));
    }

    [Fact]
    public void RemoveMember_RoutineImplementation_RemovesItsLines()
    {
        var tree = Parse(FormUnit);
        var routine = Unit(tree).ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>().Last();

        var text = Apply(tree, SyntaxEdits.RemoveMember(routine));

        Assert.Equal(Expected(FormUnit).Replace("procedure TForm1.Show;\r\nbegin\r\nend;\r\n", ""), text);
    }

    // ------------------------------------------------------------------ AddUsesEntry (CMP-257)

    [Fact]
    public void AddUsesEntry_ExistingClause_AppendsAfterTheLastUnit()
    {
        var tree = Parse(FormUnit);

        var text = Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "StdCtrls", interfaceSection: true));

        Assert.Equal(Expected(FormUnit).Replace("Classes, Forms;", "Classes, Forms, StdCtrls;"), text);
    }

    [Fact]
    public void AddUsesEntry_BeyondTheRightMargin_WrapsToTheFirstUnitColumn()
    {
        var source = "unit U;\r\ninterface\r\nuses Classes, Forms;\r\nimplementation\r\nend.\r\n";
        var tree = Parse(source);
        var options = new EditOptions(RightMargin: 25);

        var text = Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "StdCtrls", interfaceSection: true, options));

        Assert.Equal(source.Replace("uses Classes, Forms;", "uses Classes, Forms,\r\n     StdCtrls;"), text);
        // within the margin: same line
        var wide = Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "StdCtrls", interfaceSection: true, new EditOptions(RightMargin: 80)));
        Assert.Equal(source.Replace("Forms;", "Forms, StdCtrls;"), wide);
    }

    [Fact]
    public void AddUsesEntry_NoClause_CreatesOneAfterTheSectionKeywordLine()
    {
        var tree = Parse(FormUnit);

        var text = Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "Dialogs", interfaceSection: false, new EditOptions(IndentUnit: "    ")));

        Assert.Equal(Expected(FormUnit).Replace("implementation\r\n", "implementation\r\n\r\nuses\r\n    Dialogs;\r\n"), text);
    }

    [Fact]
    public void AddUsesEntry_AlreadyPresent_IsEmpty()
    {
        var tree = Parse("unit U; interface uses System.Classes, Forms; implementation uses Other; end.");

        Assert.True(SyntaxEdits.AddUsesEntry(tree.Root, "forms", interfaceSection: true).IsEmpty);
        Assert.True(SyntaxEdits.AddUsesEntry(tree.Root, "SYSTEM.CLASSES", interfaceSection: true).IsEmpty);
        Assert.False(SyntaxEdits.AddUsesEntry(tree.Root, "Classes", interfaceSection: true).IsEmpty);
        Assert.True(SyntaxEdits.AddUsesEntry(tree.Root, "Other", interfaceSection: false).IsEmpty);
        Assert.False(SyntaxEdits.AddUsesEntry(tree.Root, "Other", interfaceSection: true).IsEmpty);
    }

    [Fact]
    public void AddUsesEntry_Program_UsesItsOnlyClause()
    {
        var source = "program P;\r\n\r\nbegin\r\nend.\r\n";
        var tree = Parse(source);

        var text = Apply(tree, SyntaxEdits.AddUsesEntry(tree.Root, "SysUtils", interfaceSection: true));
        Assert.Equal("program P;\r\n\r\nuses\r\n  SysUtils;\r\n\r\nbegin\r\nend.\r\n", text);

        var again = Parse(text);
        Assert.Equal("program P;\r\n\r\nuses\r\n  SysUtils, Math;\r\n\r\nbegin\r\nend.\r\n",
            Apply(again, SyntaxEdits.AddUsesEntry(again.Root, "Math", interfaceSection: true)));
    }

    // ------------------------------------------------------------------ CMP-259 over a sequence

    [Fact]
    public void EditSequence_KeepsTheUnitFreeOfSyntaxErrors()
    {
        var tree = Parse(FormUnit);
        var steps = new Func<SyntaxTree, ImmutableArray<TextChange>>[]
        {
            t => [SyntaxEdits.InsertMember(Type(t, "TForm1"), "procedure Edit1Change(Sender: TObject);", Visibility.Published)],
            t => [SyntaxEdits.InsertMethodImplementation(Unit(t), "TForm1", "procedure TForm1.Edit1Change(Sender: TObject);\r\nbegin\r\nend;")],
            t => SyntaxEdits.RenameMethod(Unit(t), "TForm1", "Edit1Change", "NameChange"),
            t => [SyntaxEdits.AddUsesEntry(t.Root, "StdCtrls", interfaceSection: true)],
            t => [SyntaxEdits.RemoveMember(Type(t, "TForm1").DescendantNodes().OfType<FieldDeclarationSyntax>().First())],
        };

        foreach (var step in steps)
        {
            tree = SyntaxTree.Parse(SourceText.From(Path, Apply(tree, [.. step(tree)])));
        }

        Assert.Empty(tree.Diagnostics);
        Assert.Contains("procedure TForm1.NameChange(Sender: TObject);", tree.Text.Text, StringComparison.Ordinal);
        Assert.Contains("  published\r\n    procedure NameChange(Sender: TObject);\r\n  end;", tree.Text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Button1: TButton;", tree.Text.Text, StringComparison.Ordinal);
    }
}
