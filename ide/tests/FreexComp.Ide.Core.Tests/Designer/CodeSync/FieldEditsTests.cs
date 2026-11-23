using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §4: adding, removing and renaming fields; renaming the form class and variable (D-062, D-063).</summary>
[Trait("Phase", "5")]
public sealed class FieldEditsTests
{
    private static FormUnit Unit(string file, string formType) => new(TestUnits.Buffer(file), formType);

    private static FormUnit UnitFromText(string text, string formType, LineEndingKind defaultEnding = LineEndingKind.CrLf) =>
        new(new StringTextBuffer(text, defaultEnding), formType);

    [Fact, Trait("Req", "DSG-CS-036"), Trait("Req", "DSG-CS-037"), Trait("Req", "DSG-CS-002")]
    public void AddFieldAfterLastField()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var result = FieldEdits.AddFields(unit, [("OkButton", "TButton"), ("NoteLabel", "TLabel")]);

        TestUnits.AssertGolden("AddFieldAfterLastField", unit.Buffer.Text);
        Assert.Empty(result.Mismatches);
        Assert.Equal(2, result.Edits.Count);
        Assert.All(result.Edits, e => Assert.Equal("", e.RemovedText));
        Assert.Equal("\r\n    OkButton: TButton;", result.Edits[0].InsertedText);

        // One undo group in the code editor.
        unit.Buffer.Undo();
        Assert.Equal(TestUnits.Read("MainForm.pas"), unit.Buffer.Text);
        Assert.False(unit.Buffer.CanUndo);
    }

    [Fact, Trait("Req", "DSG-CS-036"), Trait("Req", "DSG-CS-037")]
    public void AddFirstFieldToEmptyClass()
    {
        // No members at all: after the class heading line, heading indentation + IU (two spaces).
        var unit = Unit("EmptyClass.pas", "TEmptyForm");
        FieldEdits.AddFields(unit, [("Button1", "TButton")]);
        TestUnits.AssertGolden("AddFirstFieldToEmptyClass", unit.Buffer.Text);

        // Only a method in the managed section: still directly after the heading, indented as the first member line.
        var withMethod = UnitFromText(
            "unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n      procedure FormCreate(Sender: TObject);\r\n  end;\r\nimplementation\r\nend.\r\n", "TF");
        FieldEdits.AddFields(withMethod, [("Edit1", "TEdit")]);
        Assert.Equal("unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n      Edit1: TEdit;\r\n      procedure FormCreate(Sender: TObject);\r\n  end;\r\n"
            + "implementation\r\nend.\r\n", withMethod.Buffer.Text);
    }

    [Fact, Trait("Req", "DSG-CS-005"), Trait("Req", "DSG-CS-006"), Trait("Req", "DSG-CS-037")]
    public void KeepsIndentationAndLineEndings()
    {
        // Tabs: the previous field's indentation is copied.
        var tabs = Unit("TabsIndent.pas", "TTabsForm");
        FieldEdits.AddFields(tabs, [("GreetButton", "TButton")]);
        Assert.Equal(TestUnits.Read("TabsIndent.pas").Replace("\t\tNameEdit: TEdit;\r\n", "\t\tNameEdit: TEdit;\r\n\t\tGreetButton: TButton;\r\n",
            StringComparison.Ordinal), tabs.Buffer.Text);

        // An LF file gets LF line ends.
        var lf = Unit("LfLineEndings.pas", "TLfForm");
        FieldEdits.AddFields(lf, [("OkButton", "TButton")]);
        Assert.DoesNotContain('\r', lf.Buffer.Text);
        Assert.Contains("    GreetButton: TButton;\n    OkButton: TButton;\n", lf.Buffer.Text, StringComparison.Ordinal);

        // A memberless class in a tab-indented file: heading indentation + a tab (IU keeps tabs).
        var tabEmpty = UnitFromText("unit U;\ninterface\ntype\n\tTF = class(TForm)\n\tend;\nimplementation\nend.\n", "TF", LineEndingKind.Lf);
        FieldEdits.AddFields(tabEmpty, [("Edit1", "TEdit")]);
        Assert.Equal("unit U;\ninterface\ntype\n\tTF = class(TForm)\n\t\tEdit1: TEdit;\n\tend;\nimplementation\nend.\n", tabEmpty.Buffer.Text);

        // IU from the class body: first member line minus the class line (here four spaces).
        var wide = UnitFromText("unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n  private\r\n      FX: Integer;\r\n  end;\r\nimplementation\r\nend.\r\n", "TF");
        Assert.Equal("    ", FormUnit.IndentUnit(wide.Locate().FormClass!));
        Assert.Equal("  ", FormUnit.IndentUnit(Unit("EmptyClass.pas", "TEmptyForm").Locate().FormClass!));
        Assert.Equal("\t", FormUnit.IndentUnit(Unit("TabsIndent.pas", "TTabsForm").Locate().FormClass!));
    }

    [Fact, Trait("Req", "DSG-CS-035"), Trait("Req", "DSG-CS-003")]
    public void ExistingFieldWithOtherTypeReportsMismatch()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var original = unit.Buffer.Text;

        var same = FieldEdits.AddFields(unit, [("nameedit", "TEdit")]);
        Assert.Empty(same.Edits);
        Assert.Empty(same.Mismatches);

        var other = FieldEdits.AddFields(unit, [("NameEdit", "TMemo")]);
        Assert.Empty(other.Edits);
        Assert.Equal(new FieldMismatch("NameEdit", "TEdit", "TMemo"), Assert.Single(other.Mismatches));
        Assert.Equal(original, unit.Buffer.Text);
        Assert.False(unit.Buffer.CanUndo);

        // A qualified declared type equals the unqualified type name; fields outside the managed section count too.
        var qualified = UnitFromText(original.Replace("NameEdit: TEdit;", "NameEdit: StdCtrls.TEdit;", StringComparison.Ordinal), "TMainForm");
        Assert.Empty(FieldEdits.AddFields(qualified, [("NameEdit", "TEdit")]).Mismatches);
        var published = Unit("ExplicitPublished.pas", "TPublishedForm");
        var inPublished = FieldEdits.AddFields(published, [("CancelButton", "TButton"), ("FCount", "TEdit")]);
        Assert.Empty(inPublished.Edits);
        Assert.Equal(new FieldMismatch("FCount", "Integer", "TEdit"), Assert.Single(inPublished.Mismatches));

        // "Fix Declaration" replaces the type identifier with the form's type name.
        FieldEdits.FixFieldType(unit, "NameEdit", "TMemo");
        Assert.Equal(original.Replace("NameEdit: TEdit;", "NameEdit: TMemo;", StringComparison.Ordinal), unit.Buffer.Text);

        // Edits need State == Ok (DSG-CS-003).
        var broken = Unit("SyntaxError.pas", "TBrokenForm");
        Assert.Throws<InvalidOperationException>(() => FieldEdits.AddFields(broken, [("X", "TButton")]));
    }

    [Fact, Trait("Req", "DSG-CS-040"), Trait("Req", "DSG-CS-041"), Trait("Req", "DSG-CS-042")]
    public void RemoveFieldSingleAndMultiName()
    {
        // Whole line with its trailing comment; a field in an explicit section too; a missing one is skipped.
        var published = Unit("ExplicitPublished.pas", "TPublishedForm");
        var edits = FieldEdits.RemoveFields(published, ["OkButton", "NoSuchField", "CancelButton"]);
        TestUnits.AssertGolden("RemoveFieldSingleAndMultiName", published.Buffer.Text);
        Assert.Equal(2, edits.Count);
        published.Buffer.Undo();
        Assert.Equal(TestUnits.Read("ExplicitPublished.pas"), published.Buffer.Text);

        // Multi-name declarations: the name and one adjacent comma.
        var original = TestUnits.Read("MultiNameFields.pas");
        string After(params string[] names)
        {
            var unit = Unit("MultiNameFields.pas", "TMultiForm");
            FieldEdits.RemoveFields(unit, names);
            return unit.Buffer.Text;
        }

        Assert.Equal(original.Replace("Button1, Button2, Button3: TButton;", "Button1, Button3: TButton;", StringComparison.Ordinal), After("Button2"));
        Assert.Equal(original.Replace("Button1, Button2, Button3: TButton;", "Button2, Button3: TButton;", StringComparison.Ordinal), After("button1"));
        Assert.Equal(original.Replace("Button1, Button2, Button3: TButton;", "Button1, Button2: TButton;", StringComparison.Ordinal), After("Button3"));
        Assert.Equal(original.Replace("    Button1, Button2, Button3: TButton;\r\n", "", StringComparison.Ordinal), After("Button1", "Button2", "Button3"));
    }

    [Fact, Trait("Req", "DSG-CS-045")]
    public void RenameFieldOnlyDeclaration()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var result = FieldEdits.RenameField(unit, "greetbutton", "OkButton");
        Assert.Equal(TestUnits.Read("MainForm.pas").Replace("    GreetButton: TButton;", "    OkButton: TButton;", StringComparison.Ordinal), unit.Buffer.Text);
        var edit = Assert.Single(result.Edits);
        Assert.Equal(("GreetButton", "OkButton"), (edit.RemovedText, edit.InsertedText));
        Assert.False(result.CodeUsesOldName); // GreetButtonClick is another identifier

        // References in bodies are not changed; the status note is due.
        var used = UnitFromText(TestUnits.Read("MainForm.pas").Replace("begin\r\n\r\nend;", "begin\r\n  NameEdit.Text := '';\r\nend;", StringComparison.Ordinal), "TMainForm");
        var renamed = FieldEdits.RenameField(used, "NameEdit", "FirstNameEdit");
        Assert.True(renamed.CodeUsesOldName);
        Assert.Contains("    FirstNameEdit: TEdit;", used.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  NameEdit.Text := '';", used.Buffer.Text, StringComparison.Ordinal);

        // A missing field: nothing.
        Assert.Empty(FieldEdits.RenameField(unit, "NoSuchField", "Other").Edits);
    }

    [Fact, Trait("Req", "DSG-CS-046")]
    public void RenameFormRenamesClassQualifiersAndVariable()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var result = FieldEdits.RenameFormClassAndVariable(unit, "MainForm", "StartForm");
        TestUnits.AssertGolden("RenameFormRenamesClassQualifiersAndVariable", unit.Buffer.Text);
        Assert.Equal("TStartForm", result.NewClassName);
        Assert.Equal("TStartForm", unit.FormTypeName);
        Assert.Equal(CodeSyncState.Ok, unit.Locate().State);
        unit.Buffer.Undo();
        Assert.Equal(TestUnits.Read("MainForm.pas"), unit.Buffer.Text);

        // The new class name is taken in the interface type section: only the variable is renamed.
        var taken = UnitFromText(TestUnits.Read("MainForm.pas").Replace("type\r\n", "type\r\n  TStartForm = class(TForm)\r\n  end;\r\n", StringComparison.Ordinal), "TMainForm");
        var partial = FieldEdits.RenameFormClassAndVariable(taken, "MainForm", "StartForm");
        Assert.Null(partial.NewClassName);
        Assert.Equal("TMainForm", taken.FormTypeName);
        Assert.Contains("  StartForm: TMainForm;", taken.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.GreetButtonClick", taken.Buffer.Text, StringComparison.Ordinal);

        // A class not named T + old name keeps its name; the variable of the form class is still renamed.
        var custom = UnitFromText(TestUnits.Read("MainForm.pas").Replace("TMainForm", "TGreeter", StringComparison.Ordinal), "TGreeter");
        var kept = FieldEdits.RenameFormClassAndVariable(custom, "MainForm", "StartForm");
        Assert.Null(kept.NewClassName);
        Assert.Contains("  StartForm: TGreeter;", custom.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  TGreeter = class(TForm)", custom.Buffer.Text, StringComparison.Ordinal);
    }
}
