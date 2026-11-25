using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// D-117: the write-back side of the action forms of the UI framework (its standard-actions.md UIF-1366 and the form of
/// its test <c>Act_StdForm</c>; the run-time library has no form writer, so the proof is the designer's): a form with
/// <c>SecondaryShortCuts</c> and with one action of every standard class is loaded with the shipped component
/// metadata and saved — the text equals the file; an empty list writes nothing; the list has the strings editor.
/// The files under <c>TestData/forms/actions</c> are the framework's two forms as the designer writes them: the
/// framework's own files were written by hand and lack the lines the designer always writes (the form's
/// <c>Left</c>, <c>Top</c> and <c>PixelsPerInch</c>, a tool button's <c>Width</c> — its default size, I-288); nothing else differs, which the
/// test checks against the framework's files.
/// </summary>
[Trait("Phase", "8")]
public sealed class ActionFormRoundTripTests
{
    private static readonly string[] Forms = ["Act_SecondaryShortCuts.form", "Act_StdForm.form"];

    /// <summary>The lines the designer always writes and the hand-written forms of the framework tests do not have.</summary>
    private static readonly string[] AlwaysWritten = ["  Left = 200", "  Top = 120", "  PixelsPerInch = 96", "      Width = 23"];

    /// <summary>A form of the framework tests (<c>ui-framework/tests/pascal</c>), written by hand.</summary>
    private static string FrameworkForm(string name) =>
        File.ReadAllText(Path.Combine(SpecTables.RepoRootPath(), "ui-framework", "tests", "pascal", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The same form as the designer writes it (<c>TestData/forms/actions</c>).</summary>
    private static string DesignerForm(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "forms", "actions", name));

    /// <summary>The form unit the designer needs beside <c>Act_StdForm.form</c>: the form class with one field per component.</summary>
    private static string StdUnit(string form)
    {
        var fields = form.Split('\n').Select(l => l.TrimStart()).Where(l => l.StartsWith("object ", StringComparison.Ordinal)).Skip(1)
            .Select(l => "    " + l["object ".Length..] + ";");
        return string.Join("\r\n",
        [
            "unit MainForm;", "", "interface", "", "uses",
            "  Classes, Controls, Forms, StdCtrls, ComCtrls, Menus, ActnList, StdActns;", "", "type",
            "  TStdForm = class(TForm)",
            .. fields,
            "    procedure FileOpen1Accept(Sender: TObject);",
            "  private", "  public", "  end;", "", "var", "  StdForm: TStdForm;", "", "implementation", "",
            "{$FORM MainForm.form}", "",
            "procedure TStdForm.FileOpen1Accept(Sender: TObject);", "begin", "", "end;", "",
            "end.", "",
        ]);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-049")]
    [Trait("Req", "DSG-SUR-215")]
    public Task SecondaryShortCutsAndStandardActions_SaveEqualsFile() => TestUiThread.RunAsync(SaveEqualsFileOnUiThread);

    private static async Task SaveEqualsFileOnUiThread()
    {
        var catalog = DataDesignTests.Catalog; // the shipped metadata, StdActns.components.json included

        foreach (var name in Forms)
        {
            // Read and written back, the text equals the file: no property is reordered, reformatted or dropped —
            // SecondaryShortCuts as a string list, the standard action classes, Control and OnAccept included.
            var text = DesignerForm(name);
            Assert.DoesNotContain('\r', text);
            var parsed = FormParser.Parse(text, name, catalog, "TForm"); // both forms derive from TForm
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal(text, FormWriter.Write(parsed.Document!));

            // It is the framework's form: that one reads without a diagnostic, the designer writes it as the file
            // above, and the two differ only by the lines the designer always writes.
            var framework = FrameworkForm(name);
            var read = FormParser.Parse(framework, name, catalog, "TForm");
            Assert.Empty(read.Diagnostics);
            Assert.Equal(text, FormWriter.Write(read.Document!));
            Assert.Equal(framework, string.Join("\n", text.Split('\n').Where(l => !AlwaysWritten.Contains(l))));
        }

        // The same through the designer: Act_StdForm opened as a document, changed, changed back and saved.
        var form = DesignerForm("Act_StdForm.form");
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        ((DocumentService)f.Services.Documents).DesignCatalog = catalog;
        var document = await f.OpenAsync(StdUnit(form), form);
        var designer = document.Designer;
        Assert.Equal(form, designer.CurrentFormText());
        var copy = DesignerFixture.Object(designer, "EditCopy1");
        Assert.Equal(["Ctrl+Ins"], Assert.IsType<StringListValue>(copy.FindProperty("SecondaryShortCuts")!.Value).Lines);
        Assert.All((string[])["EditCut1", "EditCopy1", "EditPaste1", "EditSelectAll1", "EditUndo1", "EditDelete1", "FileOpen1", "FileSaveAs1", "FileExit1"],
            n => Assert.False(DesignerFixture.Object(designer, n).IsPreserved, n));

        // The Child List Editor on the action list: the list row of SecondaryShortCuts has the strings editor, as the
        // other TStrings rows (Lines of a memo) have.
        ChildListEditorViewModel? editor = null;
        dialogs.Script(ChildListEditorViewModel.DialogName, async dialog =>
        {
            editor = Assert.IsType<ChildListEditorViewModel>(dialog);
            editor.Select("EditCopy1");
            editor.Grid.Page = InspectorPage.Properties;
            editor.Grid.SetCurrent("SecondaryShortCuts");
            Assert.True(PropertyGridViewModel.IsStringsRow(editor.Grid.CurrentRow!));

            // An empty list writes nothing: the property line is gone; the text of before comes back with the list.
            dialogs.Script(StringsEditorViewModel.DialogName, strings =>
            {
                var lines = Assert.IsType<StringsEditorViewModel>(strings);
                Assert.Equal("Ctrl+Ins", lines.Text);
                lines.Text = "";
                return lines.PressDefaultAsync();
            });
            await editor.Grid.EditInDialogAsync();
            Assert.Null(copy.FindProperty("SecondaryShortCuts"));
            var without = designer.CurrentFormText();
            Assert.Equal(form.Replace("      SecondaryShortCuts = (\n        'Ctrl+Ins'\n      )\n", "", StringComparison.Ordinal), without);
            Assert.DoesNotContain("SecondaryShortCuts", without, StringComparison.Ordinal);

            dialogs.Script(StringsEditorViewModel.DialogName, strings =>
            {
                var lines = Assert.IsType<StringsEditorViewModel>(strings);
                lines.Text = "Ctrl+Ins";
                return lines.PressDefaultAsync();
            });
            await editor.Grid.EditInDialogAsync();
            await editor.PressCancelAsync();
        });
        DesignerFixture.Select(designer, "ActionList1");
        await f.Services.Commands.ExecuteAsync(DesignCommandIds.EditComponent);
        Assert.NotNull(editor);

        // The memo's Lines row: the reference for "the other TStrings rows".
        Assert.Equal("strings", catalog.Find("TMemo")!.FindProperty("Lines")!.Editor);

        Assert.True(document.IsModified);
        Assert.True(await f.Services.Documents.SaveAsync(document));
        Assert.Equal(form, f.Disk(DesignerFixture.FormPath));
    }
}
