using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Rename Symbol dialog and the live command (I-380, F13-001; UI-DLG-380…386, IDE-REFAC-040, 044): F2 on a name opens
/// the dialog over the main window; the new name is validated 200 ms after the last change and checked by the language
/// service (the real RTL); <c>Rename</c> applies the plan through the engine — with the Preview dialog when other files change.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed partial class RenameDialogTests
{
    private const string Project = """
        { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
          "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Unit3.pas" } ] }
        """;

    /// <summary>
    /// <c>GetName</c> (lines 5, 14, 20) is used in Unit2 and Unit3; <c>GetCity</c> only here; <c>Run</c> is overridden in Unit3;
    /// <c>Log</c> has two overloads; the constant <c>Prefix</c> is used in two methods of <c>TCustomer</c>.
    /// </summary>
    internal const string Unit1 = "unit Unit1;\r\ninterface\r\ntype\r\n  TCustomer = class\r\n    function GetName: string;\r\n"
        + "    function GetCity: string;\r\n    procedure Run; virtual;\r\n  end;\r\nprocedure Log(A: Integer); overload;\r\n"
        + "procedure Log(const S: string); overload;\r\nimplementation\r\nconst\r\n  Prefix = 'Mr ';\r\n"
        + "function TCustomer.GetName: string;\r\nbegin\r\n  Result := Prefix;\r\nend;\r\n"
        + "function TCustomer.GetCity: string;\r\nbegin\r\n  Result := Prefix + GetName;\r\nend;\r\n"
        + "procedure TCustomer.Run;\r\nbegin\r\nend;\r\n"
        + "procedure Log(A: Integer);\r\nbegin\r\nend;\r\n"
        + "procedure Log(const S: string);\r\nbegin\r\n  Log(1);\r\nend;\r\nend.\r\n";

    internal const string Unit2 = "unit Unit2;\r\ninterface\r\nuses Unit1;\r\nprocedure Show(C: TCustomer);\r\nimplementation\r\n"
        + "procedure Show(C: TCustomer);\r\nvar\r\n  S: string;\r\nbegin\r\n  S := C.GetName;\r\n  Log(S);\r\nend;\r\nend.\r\n";

    internal const string Unit3 = "unit Unit3;\r\ninterface\r\nuses Unit1;\r\ntype\r\n  TVip = class(TCustomer)\r\n    procedure Run; override;\r\n  end;\r\n"
        + "implementation\r\nprocedure TVip.Run;\r\nbegin\r\n  Log(Self.GetName);\r\nend;\r\nend.\r\n";

    private const string Hello = "program Hello;\r\nuses Unit1, Unit2, Unit3;\r\nbegin\r\nend.\r\n";

    /// <summary>The Hello project with Unit1…Unit3 and the real system unit; Unit1 open and active, the editor focused.</summary>
    internal static async Task<(HeadlessHarness H, TextEditor Editor, TextDocumentViewModel Doc)> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Hello.pproj"] = Project;
            o.InitialFiles[@"Hello\Hello.pas"] = Hello;
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit1;
            o.InitialFiles[@"Hello\Unit2.pas"] = Unit2;
            o.InitialFiles[@"Hello\Unit3.pas"] = Unit3;
            configure?.Invoke(o);
        }, implementedPhase: null);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        return (h, h.Find<TextEditor>("Editor.Unit1.pas"), doc);
    }

    internal static string PathOf(HeadlessHarness h, string name) => Path.Combine(h.TempDir, "Hello", name);

    /// <summary>The caret inside the <paramref name="occurrence"/>-th <paramref name="text"/> of the document, the editor focused.</summary>
    internal static void CaretOn(TextEditor editor, TextDocumentViewModel doc, string text, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
            at = doc.Buffer.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        QuickActionTests.PlaceCaret(editor, doc, at + 1);
    }

    /// <summary>F2 in the editor: the dialog once it is open.</summary>
    internal static async Task<(DialogDriver Dialog, RenameSymbolViewModel Model)> F2Async(HeadlessHarness h)
    {
        h.Press("F2");
        return await OpenedAsync(h);
    }

    private static async Task<(DialogDriver Dialog, RenameSymbolViewModel Model)> OpenedAsync(HeadlessHarness h)
    {
        var dlg = await h.WaitForDialogAsync(RenameSymbolViewModel.DialogName);
        Assert.IsType<RenameSymbolWindow>(dlg.Window);
        return (dlg, Assert.IsType<RenameSymbolViewModel>(dlg.Window.Model));
    }

    /// <summary>The name field gets <paramref name="text"/> typed over all of its text.</summary>
    internal static void Replace(DialogDriver dlg, string text)
    {
        var box = dlg.Find<TextBox>("NewName");
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
        dlg.Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    internal static string Text(DialogDriver dlg, string field) => dlg.Find<TextBlock>(field).Text ?? "";

    internal static async Task SummaryAsync(HeadlessHarness h, DialogDriver dlg, string expected)
    {
        await h.WaitForAsync(() => Text(dlg, "Summary") == expected, "summary " + expected);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button Ok(DialogDriver dlg) => dlg.Find<Button>("Ok");

    /// <summary>The dialog's and the engine's late work ends before the test does (I-210).</summary>
    private static async Task SettleAsync(HeadlessHarness h, RenameSymbolViewModel model, RefactoringPreviewViewModel? preview = null)
    {
        await h.WaitForAsync(() => model.Checking.IsCompleted, "the dialog's check ended");
        await model.Checking;
        if (preview is not null)
        {
            await preview.DiffUpdate;
            if (preview.Diff is { } diff)
                await diff.Recomputing;
        }

        await h.WaitForAsync(() => !h.Services.Refactoring.IsBusy && h.Services.CodeModel.Worker.IsIdle, "the engine and the code model are idle");
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task CancelAsync(HeadlessHarness h, DialogDriver dlg, RenameSymbolViewModel model)
    {
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "the dialog closed");
        await SettleAsync(h, model);
    }

    /// <summary>
    /// UI-DLG-380, IDE-REFAC-044 (triage acceptance): F2 on a method used in three files, a new name, <c>Rename</c>; the
    /// Preview dialog lists the three files with the active one first; one edit unchecked; <c>Apply</c>: the open unit is
    /// edited in its buffer and not saved, the closed unit rewritten, the unchecked place unchanged; one undo entry.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-380")]
    [Trait("Req", "IDE-REFAC-044")]
    public async Task PreviewAndApply()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var (dlg, model) = await F2Async(h);
            Assert.Equal("GetName", dlg.Find<TextBox>("NewName").Text);
            Assert.False(Ok(dlg).IsEnabled);

            Replace(dlg, "FullName");
            await SummaryAsync(h, dlg, "5 changes in 3 files");
            Assert.True(Ok(dlg).IsEnabled);
            dlg.Click("Ok");

            var pd = await h.WaitForDialogAsync(RefactoringPreviewViewModel.DialogName);
            var preview = Assert.IsType<RefactoringPreviewViewModel>(pd.Window.Model);
            Assert.False(dlg.Window.IsVisible);
            await RefactoringPreviewTests.DiffAsync(h, preview);
            Assert.Equal("Preview — Rename 'GetName' to 'FullName'", pd.Window.Title);
            const string Prefix = "Dialog.RefactoringPreview.";
            Assert.Equal(
                [Prefix + "File.Unit1.pas", Prefix + "File.Unit2.pas", Prefix + "File.Unit3.pas"],
                pd.FindAll(Prefix + "File.").Select(c => AutomationIds.Get(c)!).Where(id => Regex.IsMatch(id, @"File\.Unit\d\.pas$")).Distinct());
            pd.Click("File.Unit3.pas.Edit.1.Check");
            await RefactoringPreviewTests.DiffAsync(h, preview);
            Assert.False(pd.Find<CheckBox>("File.Unit3.pas.Edit.1.Check").IsChecked);
            pd.Click("Ok");
            await h.WaitForAsync(() => !pd.Window.IsVisible && !h.Services.Refactoring.IsBusy, "the rename applied");

            Assert.Equal(Unit1.Replace("GetName", "FullName", StringComparison.Ordinal), doc.Buffer.Text);
            Assert.True(doc.IsModified);
            Assert.Equal(Unit1, File.ReadAllText(PathOf(h, "Unit1.pas")));
            Assert.Equal(Unit2.Replace("GetName", "FullName", StringComparison.Ordinal), File.ReadAllText(PathOf(h, "Unit2.pas")));
            Assert.Equal(Unit3, File.ReadAllText(PathOf(h, "Unit3.pas")));
            Assert.Equal("Rename 'GetName' to 'FullName'", h.Services.Refactoring.UndoTitle);
            await SettleAsync(h, model, preview);
        });
    }

    /// <summary>
    /// UI-DLG-382, 384: a reserved word and a name that is no identifier show their message after 200 ms and disable
    /// <c>Rename</c>; a name that would capture other references shows the conflict list instead of the summary.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-382")]
    [Trait("Req", "UI-DLG-384")]
    [Trait("Req", "UI-DLG-386")]
    public async Task LiveValidationAndConflictList()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var (dlg, model) = await F2Async(h);

            Replace(dlg, "begin");
            Assert.Null(dlg.ErrorText("NewName")); // validated after the pause, not at once
            Assert.False(Ok(dlg).IsEnabled);
            await h.WaitForAsync(() => dlg.ErrorText("NewName") is not null, "the reserved word reported");
            Assert.Equal("begin is a reserved word.", dlg.ErrorText("NewName"));
            Assert.Contains("invalid", dlg.Find<TextBox>("NewName").Classes);

            Replace(dlg, "1Name");
            await h.WaitForAsync(() => dlg.ErrorText("NewName") is not null, "the invalid name reported");
            Assert.Equal("A name starts with a letter or an underscore and continues with letters, digits and underscores.", dlg.ErrorText("NewName"));
            Assert.False(Ok(dlg).IsEnabled);

            // Letter case only is a rename; leading and trailing spaces are ignored.
            Replace(dlg, "  getName ");
            await SummaryAsync(h, dlg, "5 changes in 3 files");
            Assert.Null(dlg.ErrorText("NewName"));
            Assert.True(Ok(dlg).IsEnabled);
            Assert.False(dlg.Find<ListBox>("Conflicts").IsVisible);

            // Prefix: the two uses of the constant in TCustomer's methods would mean the method.
            Replace(dlg, "Prefix");
            await h.WaitForAsync(() => model.HasConflicts, "the conflicts listed");
            Dispatcher.UIThread.RunJobs();
            Assert.True(dlg.Find<TextBlock>("ConflictsLabel").IsVisible);
            Assert.Equal("The new name would change what these names refer to:", Text(dlg, "ConflictsLabel"));
            var list = dlg.Find<ListBox>("Conflicts");
            Assert.True(list.IsVisible);
            var rows = dlg.FindAll("Dialog.RenameSymbol.Conflicts.Row.").OfType<ListBoxItem>().ToList();
            Assert.Equal(["Unit1.pas(16,13): Prefix", "Unit1.pas(20,13): Prefix"], rows.Select(r => Avalonia.Automation.AutomationProperties.GetName(r)));
            Assert.Equal("Dialog.RenameSymbol.Conflicts.Row.1", AutomationIds.Get(rows[0]));
            Assert.StartsWith(PathOf(h, "Unit1.pas") + Environment.NewLine, (string)ToolTip.GetTip(rows[0])!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("", Text(dlg, "Summary"));
            Assert.Null(dlg.ErrorText("NewName"));
            Assert.Contains("invalid", dlg.Find<TextBox>("NewName").Classes);
            Assert.False(Ok(dlg).IsEnabled);

            // Enter on a name whose check failed does nothing.
            dlg.Press("Enter");
            Assert.True(dlg.Window.IsVisible);
            await CancelAsync(h, dlg, model);
            Assert.Equal(Unit1, doc.Buffer.Text);
        });
    }

    /// <summary>
    /// UI-DLG-385: <c>Enter</c> before the check of the typed name has ended starts it at once; the name, the check box and
    /// <c>Rename</c> are disabled until it returns; then the dialog closes and the rename (one file: no preview) is applied.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-385")]
    public async Task EnterWhileCheckingWaitsForTheCheck()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetCity");
            var (dlg, model) = await F2Async(h);
            await h.WaitForAsync(() => h.Services.CodeModel.Worker.IsIdle, "the target found");

            // The worker is held, so the check cannot return before the assertions.
            using var gate = new ManualResetEventSlim();
            var held = h.Services.CodeModel.Worker.RunAsync("test-gate", _ =>
            {
                gate.Wait();
                return true;
            });
            try
            {
                Replace(dlg, "GetTown");
                dlg.Press("Enter");
                Assert.True(dlg.Window.IsVisible);
                Assert.False(dlg.Find<TextBox>("NewName").IsEnabled);
                Assert.False(Ok(dlg).IsEnabled);
                Assert.True(dlg.Find<Button>("Cancel").IsEnabled);
                Assert.Equal("Checking…", Text(dlg, "Summary"));
            }
            finally
            {
                gate.Set();
            }

            await held;
            await h.WaitForAsync(() => !dlg.Window.IsVisible && !h.Services.Refactoring.IsBusy, "the dialog closed and the rename applied");
            Assert.Equal(Unit1.Replace("GetCity", "GetTown", StringComparison.Ordinal), doc.Buffer.Text);
            Assert.Null(h.TryFind<Window>(AutomationIds.Dialog(RefactoringPreviewViewModel.DialogName))); // one file: no preview
            await SettleAsync(h, model);
        });
    }

    /// <summary>
    /// UI-DLG-383: the check box is shown for an overloaded routine only, checked by default with the count; unchecked, only
    /// the overload at the caret is renamed (the summary follows); the choice is remembered.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-383")]
    public async Task OverloadsCheckBoxOnlyForOverloadedRoutines()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var (dlg, model) = await F2Async(h);
            Assert.Null(dlg.TryFind<CheckBox>("Overloads"));
            await CancelAsync(h, dlg, model);

            CaretOn(editor, doc, "Log");
            (dlg, model) = await F2Async(h);
            var box = dlg.Find<CheckBox>("Overloads");
            Assert.Equal("Rename all _overloads (2)", box.Content);
            Assert.True(box.IsChecked);
            Replace(dlg, "Trace");
            await SummaryAsync(h, dlg, "7 changes in 3 files");
            dlg.Click("Overloads");
            Assert.False(box.IsChecked);
            await SummaryAsync(h, dlg, "3 changes in 1 file");
            Assert.Equal("false", h.Services.Layout.GetDialogValue(RenameSymbolViewModel.DialogName, "overloads"));
            await CancelAsync(h, dlg, model);

            CaretOn(editor, doc, "Log");
            (dlg, model) = await F2Async(h);
            Assert.False(dlg.Find<CheckBox>("Overloads").IsChecked);
            await CancelAsync(h, dlg, model);
        });
    }

    /// <summary>UI-DLG-385: Cancel, Escape and × after a successful check change nothing and show no status text.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-385")]
    public async Task CancelChangesNothing()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var version = doc.Buffer.Version;
            var status = h.Services.Status.Message;
            foreach (var how in (string[])["Cancel", "Escape", "Close"])
            {
                var (dlg, model) = await F2Async(h);
                Replace(dlg, "FullName");
                await SummaryAsync(h, dlg, "5 changes in 3 files");
                if (how == "Cancel")
                    dlg.Click("Cancel");
                else if (how == "Escape")
                    dlg.Press("Escape");
                else
                    dlg.Window.Close();
                await h.WaitForAsync(() => !dlg.Window.IsVisible && !h.Services.Refactoring.IsBusy, how + ": closed");
                await SettleAsync(h, model);
                Assert.Null(model.Result);
                Assert.Equal(version, doc.Buffer.Version);
                Assert.Equal(status, h.Services.Status.Message);
                Assert.False(h.Services.Refactoring.CanUndo);
            }

            Assert.Equal(Unit2, File.ReadAllText(PathOf(h, "Unit2.pas")));
            Assert.Equal(Unit3, File.ReadAllText(PathOf(h, "Unit3.pas")));
        });
    }

    /// <summary>UI-DLG-381: kind, the name qualified by its container and the declaring file (and how many more).</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-381")]
    [Trait("Req", "UI-DLG-380")]
    public async Task SymbolLineNamesKindContainerAndFile()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            async Task ExpectAsync(string expected)
            {
                var (dlg, model) = await F2Async(h);
                Assert.Equal("Rename Symbol", dlg.Window.Title);
                var symbol = dlg.Find<TextBlock>("Symbol");
                Assert.Equal(expected, symbol.Text);
                Assert.Equal(expected, ToolTip.GetTip(symbol));
                Assert.Equal(dlg.FullId("NewName"), dlg.FocusedAutomationId);
                var box = dlg.Find<TextBox>("NewName");
                Assert.Equal((0, box.Text!.Length), (box.SelectionStart, box.SelectionEnd));
                await CancelAsync(h, dlg, model);
            }

            CaretOn(editor, doc, "GetName");
            await ExpectAsync("Method TCustomer.GetName — Unit1.pas");
            CaretOn(editor, doc, "Run");
            await ExpectAsync("Method TCustomer.Run — Unit1.pas (+1)");
            CaretOn(editor, doc, "Prefix");
            await ExpectAsync("Constant Unit1.Prefix — Unit1.pas");
            CaretOn(editor, doc, "TCustomer");
            await ExpectAsync("Class Unit1.TCustomer — Unit1.pas");
            CaretOn(editor, doc, "A: Integer", 2);
            await ExpectAsync("Parameter Log.A — Unit1.pas");
        });
    }

    /// <summary>UI-DLG-380, IDE-REFAC-047 c: while the dialog is open the engine is busy but the status bar shows no working text.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-380")]
    public async Task WorkingTextIsPausedWhileTheDialogIsOpen()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            var (dlg, model) = await F2Async(h);
            var shown = new List<string?>();
            void OnChanged(object? sender, EventArgs e) => shown.Add(h.Services.Status.Message);
            h.Services.Status.Changed += OnChanged;
            try
            {
                Replace(dlg, "FullName");
                await SummaryAsync(h, dlg, "5 changes in 3 files");
                h.Clock.Advance(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.RunJobs();
                Assert.True(h.Services.Refactoring.IsBusy);
                Assert.NotEqual("Rename Symbol…", h.Services.Status.Message);
                Assert.DoesNotContain("Rename Symbol…", shown);
            }
            finally
            {
                h.Services.Status.Changed -= OnChanged;
            }

            await CancelAsync(h, dlg, model);
        });
    }

    /// <summary>
    /// IDE-REFAC-040: F2, Edit › Refactor › Rename Symbol…, the editor context menu and the quick-action menu open the
    /// dialog on a name; off a name the command is disabled and the quick action is not offered.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-040")]
    public async Task F2MenuContextMenuAndQuickActionRunTheCommand()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            CaretOn(editor, doc, "GetName");
            Assert.True(h.Services.Commands.CanExecute(RenameRefactoring.RenameCommandId));

            // F2.
            var (dlg, model) = await F2Async(h);
            await CancelAsync(h, dlg, model);

            // Edit › Refactor › Rename Symbol… (F2 shown).
            CaretOn(editor, doc, "GetName");
            var item = MainMenuItem(h, RenameRefactoring.RenameCommandId);
            Assert.Equal("F2", Assert.IsType<FreexComp.Ide.Core.Commands.IdeCommand>(item.Command).GestureText);
            Assert.True(item.Command!.CanExecute(item.CommandParameter));
            item.Command.Execute(item.CommandParameter);
            (dlg, model) = await OpenedAsync(h);
            await CancelAsync(h, dlg, model);

            // The editor context menu.
            CaretOn(editor, doc, "GetName");
            var view = editor.TextArea.TextView;
            var caret = editor.TextArea.Caret.Position;
            var point = view.TranslatePoint(view.GetVisualPosition(caret, VisualYPosition.LineMiddle) - view.ScrollOffset, h.Window)!.Value;
            h.Window.MouseDown(point, MouseButton.Right);
            h.Window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<ContextMenu>(editor.TextArea.ContextMenu);
            Assert.True(menu.IsOpen);
            var context = menu.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == "Context." + RenameRefactoring.RenameCommandId);
            Assert.True(context.Command!.CanExecute(context.CommandParameter));
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            context.Command.Execute(context.CommandParameter);
            (dlg, model) = await OpenedAsync(h);
            await CancelAsync(h, dlg, model);

            // The quick-action menu: the refactoring is offered on a name.
            CaretOn(editor, doc, "GetName");
            h.Press("Ctrl+.");
            await h.WaitForAsync(() => editor.TextArea.ContextMenu is { IsOpen: true } open
                && open.Items.OfType<MenuItem>().Any(i => AutomationIds.Get(i) == "Context.quickAction.rename-symbol"), "the quick-action menu");
            var quick = editor.TextArea.ContextMenu!.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == "Context.quickAction.rename-symbol");
            Assert.Equal("Rename Symbol", quick.Header);
            quick.Focus();
            Dispatcher.UIThread.RunJobs();
            h.Press("Enter");
            (dlg, model) = await OpenedAsync(h);
            await CancelAsync(h, dlg, model);

            // Off a name (inside the keyword "begin"): disabled.
            QuickActionTests.PlaceCaret(editor, doc, doc.Buffer.Text.IndexOf("begin", StringComparison.Ordinal) + 1);
            Assert.False(h.Services.Commands.CanExecute(RenameRefactoring.RenameCommandId));
            Assert.Equal(Unit1, doc.Buffer.Text);
        });
    }

    /// <summary>The main-menu item of <paramref name="commandId"/>, its menus opened so that its state is refreshed.</summary>
    private static MenuItem MainMenuItem(HeadlessHarness h, string commandId)
    {
        var bar = h.Find<Menu>(AutomationIds.MenuBar);
        var item = ((ILogical)bar).GetLogicalDescendants().OfType<MenuItem>().Single(i => AutomationIds.Get(i) == AutomationIds.Menu(commandId));
        foreach (var parent in ((ILogical)item).GetLogicalAncestors().OfType<MenuItem>().Reverse())
        {
            parent.Open();
            Dispatcher.UIThread.RunJobs();
        }

        bar.Close();
        Dispatcher.UIThread.RunJobs();
        return item;
    }
}
