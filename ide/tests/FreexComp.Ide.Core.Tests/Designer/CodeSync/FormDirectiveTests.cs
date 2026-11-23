using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md Ā§3: the <c>{$FORM}</c> directive (D-069).</summary>
[Trait("Phase", "5")]
public sealed class FormDirectiveTests
{
    private static FormUnit UnitFromText(string text, string formType, LineEndingKind ending = LineEndingKind.CrLf) =>
        new(new StringTextBuffer(text, ending), formType);

    private static string Ensure(string text, string formType, string fileName, out IReadOnlyList<FreexComp.Designer.Editing.TextEditRecord> edits)
    {
        var unit = UnitFromText(text, formType);
        edits = FormDirectiveEdits.EnsureFormDirective(unit, fileName);
        return unit.Buffer.Text;
    }

    [Fact, Trait("Req", "DSG-CS-020"), Trait("Req", "DSG-CS-021"), Trait("Req", "DSG-CS-022")]
    public void InsertsAndFixesDirective()
    {
        var main = TestUnits.Read("MainForm.pas");

        // DSG-CS-020: present with the form's file name (case-insensitive) ā†’ nothing to do.
        Assert.Equal(main, Ensure(main, "TMainForm", "MainForm.form", out var none));
        Assert.Empty(none);
        Assert.Equal(main, Ensure(main, "TMainForm", "MAINFORM.FORM", out none));
        Assert.Empty(none);

        // DSG-CS-022: a wrong argument is replaced, only the argument text.
        var renamed = main.Replace("{$FORM MainForm.form}", "{$form Old.form}", StringComparison.Ordinal);
        Assert.Equal(main.Replace("{$FORM MainForm.form}", "{$form MainForm.form}", StringComparison.Ordinal),
            Ensure(renamed, "TMainForm", "MainForm.form", out var fixedArgument));
        Assert.Equal(new FreexComp.Designer.Editing.TextEditRecord(renamed.IndexOf("Old.form", StringComparison.Ordinal), "Old.form", "MainForm.form"),
            Assert.Single(fixedArgument));
        var paren = main.Replace("{$FORM MainForm.form}", "(*$FORM  Old.form *)", StringComparison.Ordinal);
        Assert.Equal(main.Replace("{$FORM MainForm.form}", "(*$FORM  MainForm.form *)", StringComparison.Ordinal),
            Ensure(paren, "TMainForm", "MainForm.form", out _));

        // Two or more directives: not touched.
        var twice = main.Replace("{$FORM MainForm.form}", "{$FORM A.form}\r\n{$FORM B.form}", StringComparison.Ordinal);
        Assert.Equal(twice, Ensure(twice, "TMainForm", "MainForm.form", out none));
        Assert.Empty(none);

        // DSG-CS-021: missing ā†’ own line after the implementation line, one blank line before and after (blank lines reused).
        var missing = main.Replace("{$FORM MainForm.form}\r\n\r\n", "", StringComparison.Ordinal);
        Assert.Equal(main, Ensure(missing, "TMainForm", "MainForm.form", out var inserted));
        Assert.Single(inserted);

        const string Bare = "unit U;\r\ninterface\r\ntype\r\n  TF = class(TForm)\r\n  end;\r\nimplementation\r\nend.\r\n";
        Assert.Equal(Bare.Replace("implementation\r\n", "implementation\r\n\r\n{$FORM U.form}\r\n\r\n", StringComparison.Ordinal),
            Ensure(Bare, "TF", "U.form", out _));
        var blankAfter = Bare.Replace("implementation\r\n", "implementation\r\n\r\n", StringComparison.Ordinal);
        Assert.Equal(Bare.Replace("implementation\r\n", "implementation\r\n\r\n{$FORM U.form}\r\n\r\n", StringComparison.Ordinal),
            Ensure(blankAfter, "TF", "U.form", out _));

        // After an implementation uses clause.
        var implUses = TestUnits.Read("ImplUsesOnly.pas");
        var withoutDirective = implUses.Replace("{$FORM ImplUsesOnly.form}\r\n\r\n", "", StringComparison.Ordinal);
        Assert.NotEqual(implUses, withoutDirective);
        Assert.Equal(implUses, Ensure(withoutDirective, "TImplUsesForm", "ImplUsesOnly.form", out _));

        // LF units get LF line ends.
        var lf = UnitFromText(Bare.Replace("\r\n", "\n", StringComparison.Ordinal), "TF", LineEndingKind.Lf);
        FormDirectiveEdits.EnsureFormDirective(lf, "U.form");
        Assert.Equal("unit U;\ninterface\ntype\n  TF = class(TForm)\n  end;\nimplementation\n\n{$FORM U.form}\n\nend.\n", lf.Buffer.Text);

        // One code-editor undo group; edits need State == Ok.
        var undo = UnitFromText(missing, "TMainForm");
        FormDirectiveEdits.EnsureFormDirective(undo, "MainForm.form");
        undo.Buffer.Undo();
        Assert.Equal(missing, undo.Buffer.Text);
        Assert.Throws<InvalidOperationException>(() => FormDirectiveEdits.EnsureFormDirective(UnitFromText(Bare, "TOther"), "U.form"));
    }
}
