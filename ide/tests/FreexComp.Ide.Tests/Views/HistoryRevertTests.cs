using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.History;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Revert to This Revision in the History window (I-333, F11-061; local-history.md IDE-HIST-020, 022): one undo step per
/// buffer, the <c>beforeRevert</c> record of an unsaved buffer, form documents with both buffers.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class HistoryRevertTests
{
    private const string FormUnit = "unit Unit1;\r\ninterface\r\nuses Classes, Controls, Forms, StdCtrls;\r\ntype\r\n  TForm1 = class(TForm)\r\n"
        + "    Button1: TButton;\r\n  end;\r\nvar\r\n  Form1: TForm1;\r\nimplementation\r\n{$R *.form}\r\nend.\r\n";

    private const string Form = "object Form1: TForm1\r\n  Caption = 'Form1'\r\n  object Button1: TButton\r\n    Caption = 'Go'\r\n  end\r\nend\r\n";

    private static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) =>
        BuildScenario.StartAsync(configure, implementedPhase: null);

    private static HistoryViewModel View(HeadlessHarness h) => Assert.IsType<HistoryViewModel>(h.Services.ToolWindows.Get(HistoryViewModel.ToolName));

    private static async Task SaveAsync(HeadlessHarness h, TextDocumentViewModel document, string? text = null)
    {
        if (text is not null)
            document.Buffer.ReplaceAll(text, resetUndo: false);
        Assert.True(await h.Services.Documents.SaveAsync(document));
        await h.Services.History.Pending;
        Dispatcher.UIThread.RunJobs();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Show(HeadlessHarness h)
    {
        h.Services.Layout.Layout.BottomSize = 300;
        Assert.True(h.Services.ToolWindows.Show(HistoryViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Selects the row <paramref name="key"/> and runs <c>history.revert</c> to its end.</summary>
    private static async Task RevertAsync(HeadlessHarness h, string key)
    {
        var view = View(h);
        view.SelectedKeys = [key];
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("history.revert"), "history.revert enabled for row " + key);
        await h.ExecuteAsync("history.revert");
        await view.Reverting;
        await h.Services.History.Pending;
        Dispatcher.UIThread.RunJobs();
    }

    private static string Key(HistoryRevision revision) => revision.Id.ToString(CultureInfo.InvariantCulture);

    private static string Entry(HeadlessHarness h, HistoryRevision revision, HistoryRole role) =>
        Encoding.UTF8.GetString(h.Services.History.ReadEntry(revision, role));

    private static string Caption(FormDocumentViewModel document) =>
        ((StringValue)PropertyAccess.GetEffective(document.Designer.Model!.Root, "Caption")).Value;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-020")]
    [Trait("Req", "IDE-HIST-022")]
    public async Task RevertSetsBufferAndUndoRestores()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var original = File.ReadAllText(unitPath);
        var unit = Assert.IsAssignableFrom<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();
        await SaveAsync(h, unit, original + "// one\r\n");
        await SaveAsync(h, unit, original + "// two\r\n// more\r\n");
        var view = View(h);
        Show(h);
        Assert.Equal([HistoryViewModel.CurrentKey, "3", "2", "1"], view.Rows.Select(r => r.Key));

        // Enabled for one revision row only: not the (current buffer) row, not two rows; the tool-bar button follows.
        view.SelectedKeys = [HistoryViewModel.CurrentKey];
        Assert.False(h.Services.Commands.CanExecute("history.revert"));
        view.SelectedKeys = ["3", "2"];
        Assert.False(h.Services.Commands.CanExecute("history.revert"));
        view.SelectedKeys = ["2"];
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("history.revert"));
        Assert.True(h.Find<Button>("Tool.History.ToolBar.history.revert").IsEffectivelyEnabled);

        // The caret is on the last text line; the revision is one line shorter: the caret keeps its line, clamped.
        var before = unit.Buffer.Text;
        var caretLine = unit.Buffer.LineCount - 1;
        unit.ViewState.Caret = new TextPosition(caretLine, 1);
        h.Click("Tool.History.ToolBar.history.revert");
        await view.Reverting;
        await h.Services.History.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(original + "// one\r\n", unit.Buffer.Text);
        Assert.True(unit.IsModified);
        Assert.Equal(Math.Min(caretLine, unit.Buffer.LineCount), unit.ViewState.Caret.Line);
        Assert.Equal(before, File.ReadAllText(unitPath)); // nothing written
        Assert.Equal(3, h.Services.History.GetRevisions(unitPath).Count); // a saved buffer needs no beforeRevert record

        // F11-061 acceptance 1: one undo step brings back the previous text exactly.
        unit.Buffer.Undo();
        Assert.Equal(before, unit.Buffer.Text);
        unit.Buffer.Redo();
        Assert.Equal(original + "// one\r\n", unit.Buffer.Text);

        // The original revision (row 1) the same way.
        await RevertAsync(h, "1");
        Assert.Equal(original, unit.Buffer.Text);
        unit.Buffer.Undo();
        Assert.Equal(original + "// one\r\n", unit.Buffer.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-020")]
    [Trait("Req", "IDE-HIST-022")]
    public async Task RevertThenSaveKeepsPreRevertInHistory()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var original = File.ReadAllText(unitPath);
        var unit = Assert.IsAssignableFrom<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();
        var one = original + "// one\r\n";
        var two = original + "// two\r\n";
        await SaveAsync(h, unit, one);
        await SaveAsync(h, unit, two);
        Show(h);

        // Saved pre-revert content: it is the earlier save revision; the save after the revert adds the reverted text.
        await RevertAsync(h, "2");
        await SaveAsync(h, unit);
        var revisions = h.Services.History.GetRevisions(unitPath);
        Assert.Equal([HistoryKind.Save, HistoryKind.Save, HistoryKind.Save, HistoryKind.Original], revisions.Select(r => r.Kind));
        Assert.Equal(one, Entry(h, revisions[0], HistoryRole.Text));
        Assert.Equal(two, Entry(h, revisions[1], HistoryRole.Text));

        // Unsaved pre-revert content: recorded as beforeRevert before the buffer changes, shown as "Before revert".
        var unsaved = original + "// unsaved\r\n";
        unit.Buffer.ReplaceAll(unsaved, resetUndo: false);
        Assert.True(unit.IsModified);
        await RevertAsync(h, Key(revisions[1])); // "two"
        Assert.Equal(two, unit.Buffer.Text);
        revisions = h.Services.History.GetRevisions(unitPath);
        Assert.Equal(HistoryKind.BeforeRevert, revisions[0].Kind);
        Assert.Equal(unsaved, Entry(h, revisions[0], HistoryRole.Text));
        Assert.Equal("Before revert", DebugScenario.Cell(h, "History", Key(revisions[0]), "Kind"));
        await SaveAsync(h, unit);
        revisions = h.Services.History.GetRevisions(unitPath);
        Assert.Equal([HistoryKind.Save, HistoryKind.BeforeRevert], revisions.Take(2).Select(r => r.Kind));
        Assert.Equal(two, Entry(h, revisions[0], HistoryRole.Text));
        Assert.Equal(unsaved, Entry(h, revisions[1], HistoryRole.Text)); // F11-061 acceptance 2
        Assert.Equal(two, File.ReadAllText(unitPath));

        // A modified buffer whose text equals the newest revision records nothing.
        var count = revisions.Count;
        unit.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        unit.Buffer.ApplyEdits([new TextEdit(0, 1, "")]);
        Assert.True(unit.IsModified);
        await RevertAsync(h, Key(revisions[^1]));
        Assert.Equal(original, unit.Buffer.Text);
        Assert.Equal(count, h.Services.History.GetRevisions(unitPath).Count);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-020")]
    public async Task RevertFormDocumentBothBuffers()
    {
        await using var h = await StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Unit1.pas"] = FormUnit;
            o.InitialFiles[@"Hello\Unit1.form"] = Form;
        });
        var unitPath = BuildScenario.UnitPath(h);
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();

        // A unit change, then a form change in the Text view: two saves after the original.
        var changedUnit = FormUnit.Replace("end.", "// unit change\r\nend.", StringComparison.Ordinal);
        await SaveAsync(h, document, changedUnit);
        Assert.True(document.ShowView(DocumentView.Text));
        Dispatcher.UIThread.RunJobs();
        document.FormBuffer.ReplaceAll(Form.Replace("'Form1'", "'Changed'", StringComparison.Ordinal), resetUndo: false);
        Assert.True(document.ShowView(DocumentView.Design));
        Dispatcher.UIThread.RunJobs();
        await SaveAsync(h, document);
        Show(h);
        Assert.Equal(3, h.Services.History.GetRevisions(unitPath).Count);
        Assert.Equal("Changed", Caption(document));

        // The Part choice is Form: the revert still replaces both buffers (row 1 = the original).
        h.Click("Tool.History.Part.form");
        var unitBefore = document.Buffer.Text;
        var formBefore = document.FormBuffer.Text; // the text the model was read from
        var onDisk = File.ReadAllText(unitPath); // the save repaired the unit (its form directive)
        await RevertAsync(h, "1");
        Assert.Equal(FormUnit, document.Buffer.Text);
        Assert.Equal(Form, document.FormBuffer.Text);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal("Form1", Caption(document)); // the designer read the reverted form text
        Assert.True(document.IsModified);
        Assert.Equal(onDisk, File.ReadAllText(unitPath)); // nothing written

        // One undo step in each buffer: the unit, and the form text (the Text view reads it back into the designer).
        document.Buffer.Undo();
        Assert.Equal(unitBefore, document.Buffer.Text);
        Assert.True(document.ShowView(DocumentView.Text));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Form, document.FormBuffer.Text);
        document.FormBuffer.Undo();
        Assert.Equal(formBefore, document.FormBuffer.Text);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Equal("Changed", Caption(document));
    }
}
