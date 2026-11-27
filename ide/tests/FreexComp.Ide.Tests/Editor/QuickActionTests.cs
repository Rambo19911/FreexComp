using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// Quick actions in the running shell (I-351, F13-003): <c>edit.quickActions</c> (<c>Ctrl+.</c>, <c>Alt+Enter</c>, the
/// editor context menu) lists the fixes of the diagnostic at the caret and the applicable refactorings in a menu at the
/// caret; choosing one applies its changes through the refactoring engine.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class QuickActionTests
{
    private const string Unit = "unit Unit1;\r\ninterface\r\nprocedure Run;\r\nimplementation\r\nprocedure Run;\r\nbegin\r\n  Count := 1;\r\nend;\r\nend.\r\n";
    private const string Declared = "unit Unit1;\r\ninterface\r\nprocedure Run;\r\nimplementation\r\nprocedure Run;\r\nvar\r\n  Count: Integer;\r\nbegin\r\n  Count := 1;\r\nend;\r\nend.\r\n";
    private const string DeclareId = "Context.quickAction.declare-variable";
    private const string DeclareTitle = "Declare variable 'Count: Integer'";

    /// <summary>The project of <see cref="BuildScenario"/> with the real system unit; Unit1 has one undeclared variable (E2003).</summary>
    private static async Task<(HeadlessHarness H, TextEditor Editor, TextDocumentViewModel Doc)> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit;
            configure?.Invoke(o);
        }, implementedPhase: null);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        PlaceCaret(editor, doc, Unit.IndexOf("Count", StringComparison.Ordinal) + 2);
        return (h, editor, doc);
    }

    private static void PlaceCaret(TextEditor editor, TextDocumentViewModel doc, int offset)
    {
        doc.Selection = TextSelection.At(offset);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(offset, editor.TextArea.Caret.Offset);
    }

    private static string? Focused(HeadlessHarness h) => h.Window.FocusManager?.GetFocusedElement() is Control c ? AutomationIds.Get(c) ?? c.GetType().Name : null;

    private static ContextMenu? OpenMenu(TextEditor editor, string idPrefix) =>
        editor.TextArea.ContextMenu is { IsOpen: true } menu
        && menu.Items.OfType<MenuItem>().Any(i => AutomationIds.Get(i)?.StartsWith(idPrefix, StringComparison.Ordinal) == true)
            ? menu
            : null;

    /// <summary>The open quick-action menu, once the actions were collected.</summary>
    private static async Task<ContextMenu> QuickMenuAsync(HeadlessHarness h, TextEditor editor)
    {
        await h.WaitForAsync(() => OpenMenu(editor, "Context.quickAction.") is not null, "the quick-action menu is open");
        Dispatcher.UIThread.RunJobs();
        return OpenMenu(editor, "Context.quickAction.")!;
    }

    private static List<string> Rows(ContextMenu menu) =>
        [.. menu.Items.Select(i => i is MenuItem item ? AutomationIds.Get(item)! : i is Separator ? "—" : i!.GetType().Name)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    [Trait("Req", "IDE-REFAC-033")]
    public async Task ListsCodeActionsOfTheDiagnosticAtTheCaret()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        Assert.True(h.Services.Commands.CanExecute("edit.quickActions"));

        h.Press("Ctrl+.");
        var menu = await QuickMenuAsync(h, editor);

        // One item per action: the service's title as it is, no shortcut; the first item has the focus.
        var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
        Assert.Equal(DeclareId, AutomationIds.Get(item));
        Assert.Equal(DeclareTitle, item.Header);
        Assert.Null(item.InputGesture);
        Assert.Equal(DeclareId, Focused(h));

        // Below the caret line, its left edge at the caret (UI-MENU-043).
        Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, menu.Placement);
        Assert.Same(editor.TextArea.TextView, menu.PlacementTarget);
        var caret = editor.TextArea.Caret.CalculateCaretRectangle();
        Assert.Equal(caret.X - editor.TextArea.TextView.ScrollOffset.X, menu.PlacementRect!.Value.X, 0.5);
        Assert.Equal(caret.Y - editor.TextArea.TextView.ScrollOffset.Y, menu.PlacementRect!.Value.Y, 0.5);

        // Escape closes it and changes nothing.
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.IsOpen);
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);
        Assert.Equal(Unit, doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.False(h.Services.Refactoring.CanUndo);
        Assert.False(h.Services.Refactoring.IsBusy);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    [Trait("Req", "IDE-REFAC-010")]
    public async Task EnterAppliesTheFixAsOneUndoStep()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        var engine = h.Services.Refactoring;

        h.Press("Ctrl+.");
        var menu = await QuickMenuAsync(h, editor);
        h.Press("Enter");
        await RefactoringUndoViewTests.WhenIdleAsync(engine);

        // The declaration is in the text: one change in the active buffer, so no preview; the document is not saved.
        Assert.False(menu.IsOpen);
        Assert.Equal(Declared, doc.Buffer.Text);
        Assert.Equal(Declared, editor.Document.Text);
        Assert.True(doc.IsModified);
        Assert.Equal(Unit, File.ReadAllText(BuildScenario.UnitPath(h)));
        Assert.Null(h.TryFind<Window>(AutomationIds.Dialog("RefactoringPreview")));
        Assert.Equal(DeclareTitle + ": 1 change", h.Services.Status.Message);
        Assert.Equal(DeclareTitle, engine.UndoTitle);
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);

        // The engine's undo entry names it (Edit › Refactor).
        var undo = h.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.Menu("refactor.undo"));
        Assert.Equal("_Undo " + DeclareTitle, undo.Header);

        // Ctrl+Z takes it back as one step.
        h.Press("Ctrl+Z");
        await RefactoringUndoViewTests.WhenIdleAsync(engine);
        Assert.Equal(Unit, doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.False(doc.Buffer.CanUndo);
        Assert.False(engine.CanUndo);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    [Trait("Req", "UI-MENU-040")]
    public async Task AltEnterAndContextMenuOpenTheSameMenu()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;

        // The second gesture.
        h.Press("Alt+Enter");
        var menu = await QuickMenuAsync(h, editor);
        Assert.Equal([DeclareId], Rows(menu));
        Assert.Equal(DeclareId, Focused(h));
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.IsOpen);
        Assert.Equal(Unit, doc.Buffer.Text); // Enter was not typed

        // The editor context menu lists the command as the first item of its second group, in front of Go to Declaration,
        // with the main menu's label and shortcut.
        h.Press("Shift+F10");
        Dispatcher.UIThread.RunJobs();
        var context = Assert.IsType<ContextMenu>(OpenMenu(editor, "Context.edit."));
        var rows = Rows(context);
        var at = rows.IndexOf("Context.edit.quickActions");
        Assert.True(at > 0, string.Join(' ', rows));
        Assert.Equal(("—", "Context.search.goToDeclaration"), (rows[at - 1], rows[at + 1]));
        Assert.Equal(4, at - 1); // Cut, Copy, Paste, Select All come first
        var entry = (MenuItem)context.Items[at]!;
        Assert.Equal("_Quick Actions…", entry.Header);
        Assert.Equal("Ctrl+.", Assert.IsType<Core.Commands.IdeCommand>(entry.Command).GestureText); // the first gesture is the one shown (UI-KEY-005)
        var main = h.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.Menu("edit.quickActions"));
        Assert.Equal(entry.Header, main.Header);
        Assert.Same(entry.Command, main.Command);
        Assert.Equal(AutomationIds.SubMenu("Edit", "CodeAssist"), AutomationIds.Get((MenuItem)main.Parent!));

        // Choosing it opens the same menu at the caret.
        for (var i = 0; i < rows.Count && Focused(h) != "Context.edit.quickActions"; i++)
        {
            h.Press("Down");
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("Context.edit.quickActions", Focused(h));
        h.Press("Enter");
        menu = await QuickMenuAsync(h, editor);
        Assert.False(context.IsOpen);
        Assert.Equal([DeclareId], Rows(menu));
        Assert.Equal(DeclareId, Focused(h));
        Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, menu.Placement);
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    public async Task NoActionsShowsStatus()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        const string None = "No quick actions here";

        // A clean line: the status bar says so and no menu opens.
        PlaceCaret(editor, doc, Unit.IndexOf("interface", StringComparison.Ordinal) + 3);
        h.Press("Ctrl+.");
        await h.WaitForAsync(() => h.Services.Status.Message == None, "the status text of an empty result");
        Assert.Null(OpenMenu(editor, "Context."));
        Assert.False(h.Services.Refactoring.IsBusy);

        // A read-only document: the command is disabled and its key does nothing.
        PlaceCaret(editor, doc, Unit.IndexOf("Count", StringComparison.Ordinal) + 2);
        h.Clock.Advance(TimeSpan.FromSeconds(6)); // the status text is gone
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("edit.quickActions"));
        doc.SetReadOnly(true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Commands.CanExecute("edit.quickActions"));
        var requests = h.Services.CodeModel.Worker.RequestCount;
        h.Press("Ctrl+.");
        h.Press("Alt+Enter");
        Assert.Equal(requests, h.Services.CodeModel.Worker.RequestCount);
        Assert.Null(OpenMenu(editor, "Context."));
        Assert.NotEqual(None, h.Services.Status.Message);
        Assert.Equal(Unit, doc.Buffer.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    [Trait("Req", "IDE-REFAC-032")]
    public async Task FixesAndRefactoringsAreSeparated()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        var engine = h.Services.Refactoring;
        var wrap = new FakeRefactoring("wrap-in-region", "Wrap in Region")
        {
            Compute = (c, _) => Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit("Wrap in Region", [new FileEdit(c.Document.FilePath!, c.Document.Buffer.Text, [new TextEdit(0, 0, "{x}")])])),
        };
        engine.Register(wrap);
        // A second action with the id of the service's fix, and a title with the characters a menu label gives a meaning to.
        engine.Register(new FakeQuickActionProvider(new QuickAction("declare-variable", "Declare field F_Count & more", QuickActionKind.Fix, _ => Task.FromResult<WorkspaceEdit?>(null))));

        h.Press("Ctrl+.");
        var menu = await QuickMenuAsync(h, editor);

        // Fixes, a separator, refactorings; a repeated id is numbered.
        Assert.Equal([DeclareId, DeclareId + "#2", "—", "Context.quickAction.wrap-in-region"], Rows(menu));
        Assert.Equal([DeclareTitle, "Declare field F__Count & more", "Wrap in Region"], menu.Items.OfType<MenuItem>().Select(i => i.Header));
        Assert.Equal(DeclareId, Focused(h));

        // Down moves through the menu as in every context menu; Enter runs the refactoring.
        h.Press("Down");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DeclareId + "#2", Focused(h));
        h.Press("Down");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Context.quickAction.wrap-in-region", Focused(h));
        h.Press("Enter");
        await RefactoringUndoViewTests.WhenIdleAsync(engine);
        Assert.Equal("{x}" + Unit, doc.Buffer.Text);
        Assert.Equal("Wrap in Region: 1 change", h.Services.Status.Message);
        Assert.Same(doc, Assert.Single(wrap.Computed).Document);
        Assert.NotNull(wrap.Computed[0].Source);
    }

    /// <summary>
    /// An action that changes more than the active buffer goes through the Preview dialog (IDE-REFAC-007), and its
    /// operation is taken back as a whole by <c>refactor.undo</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-034")]
    [Trait("Req", "IDE-REFAC-007")]
    [Trait("Req", "IDE-REFAC-021")]
    public async Task ActionOverTwoFilesGoesThroughThePreview()
    {
        var (h, editor, doc) = await StartAsync();
        await using var _ = h;
        await Views.TrustDialogTests.Guarded(h, async () =>
        {
            var engine = h.Services.Refactoring;
            var program = Path.Combine(h.TempDir, @"Hello\Hello.pas");
            Assert.True(await h.Services.Documents.CloseAllAsync(except: doc)); // the main source opened with the project: a closed file now
            PlaceCaret(editor, doc, Unit.IndexOf("Count", StringComparison.Ordinal) + 2);
            var programBytes = File.ReadAllBytes(program);
            var programText = File.ReadAllText(program);
            engine.Register(new FakeRefactoring("mark-both", "Mark Both Files")
            {
                Compute = (c, _) => Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit("Mark Both Files",
                [
                    new FileEdit(c.Document.FilePath!, c.Document.Buffer.Text, [new TextEdit(0, 0, "{x}")]),
                    new FileEdit(program, programText, [new TextEdit(0, 0, "{y}")]),
                ])),
            });

            h.Press("Ctrl+.");
            var menu = await QuickMenuAsync(h, editor);
            Assert.Equal([DeclareId, "—", "Context.quickAction.mark-both"], Rows(menu));
            h.Press("Down");
            Dispatcher.UIThread.RunJobs();
            h.Press("Enter");

            // The closed file is not in view: the changes are shown first, and nothing has changed yet.
            var dlg = await h.WaitForDialogAsync(Core.Dialogs.RefactoringPreviewViewModel.DialogName);
            await Views.RefactoringPreviewTests.DiffAsync(h, Assert.IsType<Core.Dialogs.RefactoringPreviewViewModel>(dlg.Window.Model));
            Assert.Equal("Preview — Mark Both Files", dlg.Window.Title);
            Assert.Equal("Selected changes: 2 of 2, files: 2", dlg.Find<TextBlock>("Summary").Text);
            Assert.True(engine.IsBusy);
            Assert.False(h.Services.Commands.CanExecute("edit.quickActions"));
            Assert.Equal((Unit, programText), (doc.Buffer.Text, File.ReadAllText(program)));

            dlg.Click("Ok");
            await RefactoringUndoViewTests.WhenIdleAsync(engine);
            Assert.Equal(("{x}" + Unit, "{y}" + programText), (doc.Buffer.Text, File.ReadAllText(program)));
            Assert.Equal("Mark Both Files: 2 changes in 2 files", h.Services.Status.Message);

            // Edit › Refactor › Undo takes both files back; the closed one byte for byte.
            await h.ExecuteAsync("refactor.undo");
            await RefactoringUndoViewTests.WhenIdleAsync(engine);
            Assert.Equal(Unit, doc.Buffer.Text);
            Assert.False(doc.IsModified);
            Assert.Equal(programBytes, File.ReadAllBytes(program));
            Assert.False(engine.CanUndo);
        });
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-031")]
    [Trait("Req", "IDE-REFAC-003")]
    public async Task UnreadableFormIsReportedNotThrown()
    {
        var (h, editor, doc) = await StartAsync(o => o.MessageAnswers["Msg.Refactoring.ApplyFailed"] = "OK");
        await using var _ = h;
        var engine = h.Services.Refactoring;
        var form = Path.Combine(h.TempDir, @"Hello\Gone.form");
        // An action whose changes include a form the language service read and that is gone when the edit is built.
        engine.Register(new FakeQuickActionProvider(new QuickAction("rename-handler", "Rename Handler", QuickActionKind.Fix, ct =>
            WorkspaceEdit.FromLanguageService("Rename Handler", [new DocumentEdit(form, [new TextChange(new TextSpan(0, 0), "x")])],
                h.Services.CodeModel.Synchronize()!, h.Services.FileSystem, ct)!)));

        h.Press("Ctrl+.");
        var menu = await QuickMenuAsync(h, editor);
        Assert.Equal([DeclareId, "Context.quickAction.rename-handler"], Rows(menu));
        h.Press("Down");
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => h.Messages.Requests.Count > 0 && !engine.IsBusy, "the failure is reported");

        // A failure message that names the operation, the file and the system's reason — not an internal error.
        var request = Assert.Single(h.Messages.Requests);
        Assert.Equal("Msg.Refactoring.ApplyFailed", request.MessageId);
        Assert.Equal(3, request.Args.Count);
        Assert.Equal(("Rename Handler", form), (request.Args[0], request.Args[1]));
        Assert.False(string.IsNullOrWhiteSpace(request.Args[2]));
        Assert.Equal(Unit, doc.Buffer.Text);
        Assert.False(engine.CanUndo);
        Assert.True(h.Services.Commands.CanExecute("edit.quickActions"));
    }
}
