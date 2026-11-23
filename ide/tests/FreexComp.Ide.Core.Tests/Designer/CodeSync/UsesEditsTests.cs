using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §5: the interface <c>uses</c> clause (D-064).</summary>
[Trait("Phase", "5")]
public sealed class UsesEditsTests
{
    private static FormUnit Unit(string file, string formType, int rightMargin = 120) =>
        new(TestUnits.Buffer(file), formType) { RightMargin = rightMargin };

    [Fact, Trait("Req", "DSG-CS-052")]
    public void AddsUnitToExistingClause()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        var edits = UsesEdits.EnsureUnits(unit, ["ExtCtrls", "Dialogs"]);
        TestUnits.AssertGolden("AddsUnitToExistingClause", unit.Buffer.Text);
        Assert.Equal([", ExtCtrls", ", Dialogs"], edits.Select(e => e.InsertedText));

        unit.Buffer.Undo(); // one code-editor undo group
        Assert.Equal(TestUnits.Read("MainForm.pas"), unit.Buffer.Text);
    }

    [Fact, Trait("Req", "DSG-CS-052")]
    public void WrapsAtRightMargin()
    {
        // "  Classes, Controls, Forms, StdCtrls;" is 37 characters: ", ExtCtrls" would end at 47 > 40.
        var unit = Unit("MainForm.pas", "TMainForm", rightMargin: 40);
        UsesEdits.EnsureUnits(unit, ["ExtCtrls", "Menus"]);
        Assert.Contains("uses\r\n  Classes, Controls, Forms, StdCtrls,\r\n  ExtCtrls, Menus;\r\n", unit.Buffer.Text, StringComparison.Ordinal);

        // The wrapped line is indented to the column of the first unit name.
        var aligned = new FormUnit(new StringTextBuffer(TestUnits.Read("MainForm.pas")
            .Replace("uses\r\n  Classes, Controls, Forms, StdCtrls;", "uses Classes, Controls, Forms, StdCtrls;", StringComparison.Ordinal)), "TMainForm") { RightMargin = 40 };
        UsesEdits.EnsureUnits(aligned, ["ExtCtrls"]);
        Assert.Contains("uses Classes, Controls, Forms, StdCtrls,\r\n     ExtCtrls;\r\n", aligned.Buffer.Text, StringComparison.Ordinal);

        // 0 = no limit.
        var unlimited = Unit("MainForm.pas", "TMainForm", rightMargin: 0);
        UsesEdits.EnsureUnits(unlimited, ["ExtCtrls", "Dialogs", "Menus", "ComCtrls", "SomeVeryLongUnitNameThatGoesOnAndOn"]);
        Assert.Contains("  Classes, Controls, Forms, StdCtrls, ExtCtrls, Dialogs, Menus, ComCtrls, SomeVeryLongUnitNameThatGoesOnAndOn;\r\n",
            unlimited.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-053"), Trait("Req", "DSG-CS-051")]
    public void CreatesClauseWhenMissing()
    {
        // After the interface line; the following blank line is reused.
        var unit = Unit("NoUses.pas", "TNoUsesForm");
        UsesEdits.EnsureUnits(unit, ["Forms", "StdCtrls"]);
        TestUnits.AssertGolden("CreatesClauseWhenMissing", unit.Buffer.Text);
        unit.Buffer.Undo();
        Assert.Equal(TestUnits.Read("NoUses.pas"), unit.Buffer.Text);

        // No blank line before the next token: one is added. A unit used only in the implementation is still added to
        // the interface clause.
        var implOnly = Unit("ImplUsesOnly.pas", "TImplUsesForm");
        UsesEdits.EnsureUnits(implOnly, ["Dialogs"]);
        TestUnits.AssertGolden("CreatesClauseWhenMissingNoBlankLine", implOnly.Buffer.Text);

        // An LF unit gets LF line ends.
        var lf = new FormUnit(new StringTextBuffer(TestUnits.Read("NoUses.pas").Replace("\r\n", "\n", StringComparison.Ordinal)), "TNoUsesForm");
        UsesEdits.EnsureUnits(lf, ["Forms"]);
        Assert.Contains("interface\n\nuses\n  Forms;\n\ntype\n", lf.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-051"), Trait("Req", "DSG-CS-054")]
    public void DoesNothingWhenPresent()
    {
        var unit = Unit("MainForm.pas", "TMainForm");
        Assert.Empty(UsesEdits.EnsureUnits(unit, ["Forms", "stdctrls", "CLASSES"]));
        Assert.Equal(TestUnits.Read("MainForm.pas"), unit.Buffer.Text);
        Assert.False(unit.Buffer.CanUndo);

        // A dotted name must match in full.
        var edits = UsesEdits.EnsureUnits(unit, ["Sample.Forms"]);
        Assert.Equal(", Sample.Forms", Assert.Single(edits).InsertedText);
    }
}
