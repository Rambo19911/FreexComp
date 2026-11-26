using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Build;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-302: the active-editor commands and the status-bar editor fields follow the editor of the visible view of a form
/// document (Code: the unit, Text: the form text), not always the unit's.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class FormTextViewActiveEditorTests
{
    private const string Unit = "unit MainForm;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\n{$FORM MainForm.form}\r\n\r\nend.\r\n";

    /// <summary>A form text longer than its unit (40 property lines), so a line valid only in the form text exists.</summary>
    private static string FormText()
    {
        var text = new StringBuilder("object MainForm: TMainForm\r\n  Caption = 'MainForm'\r\n");
        for (var i = 1; i <= 40; i++)
            text.Append("  Tag = ").Append(i).Append("\r\n");
        return text.Append("end\r\n").ToString();
    }

    private static string Tab(string view) => AutomationIds.DocumentViewTab("MainForm.pas", view);

    private static CodeEditorView VisibleEditor(HeadlessHarness h) =>
        h.Window.GetVisualDescendants().OfType<CodeEditorView>().Single(e => e.IsEffectivelyVisible);

    private static void FocusVisibleEditor(HeadlessHarness h)
    {
        Assert.True(VisibleEditor(h).Editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-020")]
    [Trait("Req", "UI-WIN-040")]
    [Trait("Req", "UI-DLG-120")]
    [Trait("Req", "UI-MENU-010")]
    [Trait("Req", "IDE-ED-100")]
    [Trait("Req", "DBG-232")]
    public async Task GoToLineAndCaretFieldFollowTheVisibleView()
    {
        var formText = FormText();
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["MainForm.pas"] = Unit;
            o.InitialFiles["MainForm.form"] = formText;
            // A runnable project: Run to Cursor is then enabled wherever the visible editor holds Object Pascal.
            o.InitialFiles["P.pproj"] = ProjectSerializer.Save(ProjectSerializer.CreateDefault("P", "P.pas", "gui"));
            o.InitialFiles["P.pas"] = "program P;\r\n\r\nuses MainForm;\r\n\r\nbegin\r\nend.\r\n";
        });
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "P.pproj")));
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "MainForm.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        var unitLines = document.Buffer.LineCount;
        var formLines = document.FormText.Buffer.LineCount;
        Assert.True(formLines > unitLines + 20);

        // The Code view: the caret of the unit.
        FocusVisibleEditor(h);
        h.Press("Ctrl+End");
        Assert.Equal(new TextPosition(unitLines, 1), document.ViewState.Caret);
        Assert.Equal($"Ln {unitLines}, Col 1", h.Status.Field("CaretPosition"));
        Assert.True(h.Services.Commands.CanExecute("run.runToCursor"));

        // The Text view: the caret of the form text, and its moves.
        h.Click(Tab("Text"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.Equal("Ln 1, Col 1", h.Status.Field("CaretPosition"));
        FocusVisibleEditor(h);
        h.Press("Ctrl+End");
        Assert.Equal(new TextPosition(formLines, 1), document.FormText.ViewState.Caret);
        Assert.Equal($"Ln {formLines}, Col 1", h.Status.Field("CaretPosition"));
        Assert.Equal("Form", h.Status.Field("Language"));
        Assert.False(h.Services.Commands.CanExecute("run.runToCursor")); // the form text holds no Object Pascal (as breakpoints)

        // Go to Line offers and validates the form text's range and moves the form text's caret.
        var target = unitLines + 10; // beyond the unit, inside the form text
        var running = h.Services.Commands.ExecuteAsync("search.goToLine");
        var dialog = await h.WaitForDialogAsync("GoToLine");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains($"(1–{formLines})", dialog.Window.GetVisualDescendants().OfType<Label>().Single().Content as string, StringComparison.Ordinal);
        Assert.Equal(formLines.ToString(System.Globalization.CultureInfo.InvariantCulture), dialog.Find<TextBox>("Line").Text);
        dialog.Find<TextBox>("Line").Text = target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dialog.ErrorText("Line"));
        dialog.Press("Enter");
        await running;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.Equal(new TextPosition(target, 3), document.FormText.ViewState.Caret); // first non-blank of "  Tag = …"
        Assert.Equal(new TextPosition(unitLines, 1), document.ViewState.Caret);
        Assert.Equal($"Ln {target}, Col 3", h.Status.Field("CaretPosition"));

        // Back in the Code view: the unit caret's field again.
        h.Click(Tab("Code"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal($"Ln {unitLines}, Col 1", h.Status.Field("CaretPosition"));
        Assert.Equal("Object Pascal", h.Status.Field("Language"));

        // Navigate Back returns to the location Go to Line left: in the form text, shown in the Text view (IDE-ED-100).
        await h.ExecuteAsync("search.navigateBack");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.Equal(new TextPosition(formLines, 1), document.FormText.ViewState.Caret);
        Assert.Equal($"Ln {formLines}, Col 1", h.Status.Field("CaretPosition"));

        // The Design view shows no text editor: no caret field, and Go to Line ("text") is disabled.
        h.Click(Tab("Design"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.Status.Field("CaretPosition"));
        Assert.False(h.Services.Commands.CanExecute("search.goToLine"));
        Assert.False(h.Services.Commands.CanExecute("run.runToCursor"));
    }
}
