using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// Quick actions in the running shell (I-351, F13-003): <c>edit.quickActions</c> (<c>Ctrl+.</c>, <c>Alt+Enter</c>, the
/// editor context menu) lists the fixes of the diagnostic at the caret and the applicable refactorings in a menu at the
/// caret; choosing one applies its changes through the refactoring engine. I-352: the quick-action glyph of the glyph
/// margin (the bulb) on the caret line when a fix is offered there.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class QuickActionTests
{
    internal const string Unit = "unit Unit1;\r\ninterface\r\nprocedure Run;\r\nimplementation\r\nprocedure Run;\r\nbegin\r\n  Count := 1;\r\nend;\r\nend.\r\n";
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

    internal static void PlaceCaret(TextEditor editor, TextDocumentViewModel doc, int offset)
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

    private const int ErrorLine = 7; // "  Count := 1;"
    private const string BulbTip = "Quick actions available (Ctrl+.)";

    private static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(500); // IDE-REFAC-035

    private static IEditorTestProbe Probe(TextEditor editor) => editor.FindAncestorOfType<CodeEditorView>()!;

    private static GlyphMargin Gutter(TextEditor editor) => editor.TextArea.LeftMargins.OfType<GlyphMargin>().Single();

    /// <summary>The window point of the middle of the glyph slot of <paramref name="line"/>.</summary>
    private static Point Slot(HeadlessHarness h, TextEditor editor, int line)
    {
        var gutter = Gutter(editor);
        return gutter.TranslatePoint(new Point(gutter.Bounds.Width / 2, (line - 0.5) * editor.TextArea.TextView.DefaultLineHeight), h.Window)!.Value;
    }

    /// <summary>The tooltip the glyph margin shows over <paramref name="line"/>.</summary>
    private static string? TipAt(HeadlessHarness h, TextEditor editor, int line)
    {
        h.Window.MouseMove(Slot(h, editor, line == 1 ? 2 : 1)); // the pointer comes from another line
        h.Window.MouseMove(Slot(h, editor, line));
        Dispatcher.UIThread.RunJobs();
        return ToolTip.GetTip(Gutter(editor)) as string;
    }

    /// <summary>The offset of the first character of <paramref name="line"/>.</summary>
    private static int LineStart(TextEditor editor, int line) => editor.Document.GetLineByNumber(line).Offset;

    /// <summary>
    /// The scenario with the error marker of the undeclared variable shown and the caret resting on a clean line; a
    /// provider without actions counts how often the quick actions are asked for.
    /// </summary>
    internal static async Task<(HeadlessHarness H, TextEditor Editor, TextDocumentViewModel Doc, IEditorTestProbe Probe, FakeQuickActionProvider Asked)> StartWithMarkerAsync(
        Action<HarnessOptions>? configure = null)
    {
        var (h, editor, doc) = await StartAsync(configure);
        var asked = new FakeQuickActionProvider();
        h.Services.Refactoring.Register(asked);
        PlaceCaret(editor, doc, LineStart(editor, 2));
        var probe = Probe(editor);
        await h.WaitForAsync(() => probe.Markers.Count > 0, "the error marker of the undeclared variable");
        return (h, editor, doc, probe, asked);
    }

    /// <summary>The caret has rested: the quick actions of its position are asked for and the answer has arrived.</summary>
    internal static async Task RestAsync(HeadlessHarness h, FakeQuickActionProvider asked)
    {
        var before = asked.Asked.Count;
        h.Clock.Advance(Rest);
        await h.WaitForAsync(() => asked.Asked.Count > before, "the quick actions of the caret position were asked for");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-035")]
    [Trait("Req", "UI-ED-150")]
    public async Task BulbAppearsOnTheFixLineAndFollowsTheCaret()
    {
        var (h, editor, doc, probe, asked) = await StartWithMarkerAsync();
        await using var _ = h;
        var engine = h.Services.Refactoring;
        var count = Unit.IndexOf("Count", StringComparison.Ordinal);
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));
        Assert.Null(probe.GlyphAt(2)); // the caret has rested on a line without a fix

        // The caret comes to the undeclared identifier: nothing is asked before it has rested for 500 ms …
        PlaceCaret(editor, doc, count + 2);
        var before = asked.Asked.Count;
        h.Clock.Advance(Rest - TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, asked.Asked.Count);
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));

        // … and a caret that moves on the way starts the 500 ms again: one question, for the position it rests at.
        PlaceCaret(editor, doc, count + 3);
        h.Clock.Advance(Rest - TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, asked.Asked.Count);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await h.WaitForAsync(() => probe.GlyphAt(ErrorLine) == "QuickAction", "the bulb on the line with the fix");
        Assert.Equal(before + 1, asked.Asked.Count);
        Assert.Equal(TextSelection.At(count + 3), asked.Asked[^1].Selection);
        // The bulb replaces the error marker of its line only; asking never makes the engine busy.
        Assert.Equal(["QuickAction"], Enumerable.Range(1, editor.Document.LineCount).Select(probe.GlyphAt).OfType<string>());
        Assert.Equal("QuickAction", Assert.Single(probe.VisualRows(ErrorLine)).Glyph);
        Assert.False(engine.IsBusy);
        Assert.True(h.Services.Commands.CanExecute("edit.quickActions"));
        Assert.Equal(Unit, doc.Buffer.Text);

        // A caret that stays on the line keeps the bulb until the answer for its new position says there is no fix there.
        PlaceCaret(editor, doc, Unit.IndexOf(":= 1;", StringComparison.Ordinal) + 5);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));
        await RestAsync(h, asked);
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));
        PlaceCaret(editor, doc, count);
        await RestAsync(h, asked);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));

        // Moving the caret to another line gives the error marker back at once; the bulb follows the caret only to a fix.
        PlaceCaret(editor, doc, LineStart(editor, 6));
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));
        await RestAsync(h, asked);
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));
        Assert.Null(probe.GlyphAt(6));
        PlaceCaret(editor, doc, count + 1);
        await RestAsync(h, asked);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));

        // Typing removes it at once; it is back when the caret has rested in the changed text (the name is still undeclared).
        h.Type("x");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Cxount := 1;", doc.Buffer.Text, StringComparison.Ordinal);
        Assert.NotEqual("QuickAction", probe.GlyphAt(ErrorLine));
        await RestAsync(h, asked);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));

        // A read-only document has no bulb and is not asked.
        doc.SetReadOnly(true);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("QuickAction", probe.GlyphAt(ErrorLine));
        before = asked.Asked.Count;
        PlaceCaret(editor, doc, count + 2);
        h.Clock.Advance(Rest + Rest);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, asked.Asked.Count);
        doc.SetReadOnly(false);
        Dispatcher.UIThread.RunJobs();
        await RestAsync(h, asked);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));

        // A stale result is dropped: the answer for a position the caret has left since shows nothing, there or here.
        PlaceCaret(editor, doc, LineStart(editor, 2));
        await RestAsync(h, asked);
        var answer = new TaskCompletionSource<IReadOnlyList<QuickAction>>();
        var slow = new FakeQuickActionProvider((_, _) => answer.Task);
        engine.Register(slow);
        var fix = new QuickAction("late-fix", "Late Fix", QuickActionKind.Fix, _ => Task.FromResult<WorkspaceEdit?>(null));
        PlaceCaret(editor, doc, LineStart(editor, 3));
        h.Clock.Advance(Rest);
        await h.WaitForAsync(() => slow.Asked.Count == 1, "the slow provider is asked");
        Assert.False(engine.IsBusy);
        PlaceCaret(editor, doc, LineStart(editor, 4));
        answer.SetResult([fix]);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(probe.GlyphAt(3));
        Assert.Null(probe.GlyphAt(4));
        // The answer for the position the caret rests at is shown.
        answer = new TaskCompletionSource<IReadOnlyList<QuickAction>>();
        h.Clock.Advance(Rest);
        await h.WaitForAsync(() => slow.Asked.Count == 2, "the slow provider is asked again");
        Assert.Null(probe.GlyphAt(4));
        answer.SetResult([fix]);
        await h.WaitForAsync(() => probe.GlyphAt(4) == "QuickAction", "the bulb of the late fix");
        Assert.Null(probe.GlyphAt(3));

        // Closing the document while an answer is awaited ends the request.
        doc.Buffer.Undo(); // the typed character: the document closes without a question
        Dispatcher.UIThread.RunJobs();
        Assert.False(doc.IsModified);
        Assert.Null(probe.GlyphAt(4));
        answer = new TaskCompletionSource<IReadOnlyList<QuickAction>>();
        PlaceCaret(editor, doc, LineStart(editor, 3));
        h.Clock.Advance(Rest);
        await h.WaitForAsync(() => slow.Asked.Count == 3, "the slow provider is asked a third time");
        Assert.True(await h.Services.Documents.CloseAsync(doc));
        answer.SetResult([fix]);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(doc.Decorations.GlyphsAt(3));
        Assert.False(engine.IsBusy);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-035")]
    [Trait("Req", "UI-ED-150")]
    [Trait("Req", "UI-ED-021")]
    public async Task BulbOnlyForFixesAndBelowBookmark()
    {
        var (h, editor, doc, probe, asked) = await StartWithMarkerAsync();
        await using var _ = h;
        var nl = Environment.NewLine;
        var wrap = new FakeRefactoring("wrap-in-region", "Wrap in Region");
        h.Services.Refactoring.Register(wrap);
        var error = TipAt(h, editor, ErrorLine);
        Assert.Contains("Count", error, StringComparison.Ordinal); // the message of E2003

        // A line where only a refactoring applies gets no glyph (it is offered by Ctrl+. all the same).
        PlaceCaret(editor, doc, LineStart(editor, 3));
        await RestAsync(h, asked);
        Assert.NotNull(wrap.AskedWith);
        Assert.Equal(["Error"], Enumerable.Range(1, editor.Document.LineCount).Select(probe.GlyphAt).OfType<string>());
        Assert.Null(TipAt(h, editor, 3));

        // A bookmark on the line with the fix keeps its glyph; the tooltip lists the bulb below it, then the error marker.
        PlaceCaret(editor, doc, Unit.IndexOf("Count", StringComparison.Ordinal) + 2);
        await h.ExecuteAsync("search.bookmark.toggle");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bookmark", probe.GlyphAt(ErrorLine));
        Assert.Equal("Bookmark" + nl + error, TipAt(h, editor, ErrorLine));
        await RestAsync(h, asked);
        Assert.Equal("Bookmark", probe.GlyphAt(ErrorLine));
        Assert.Equal("Bookmark" + nl + BulbTip + nl + error, TipAt(h, editor, ErrorLine));

        // Without the bookmark the bulb is drawn, in front of the error marker it hides.
        await h.ExecuteAsync("search.bookmark.toggle");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));
        Assert.Equal(BulbTip + nl + error, TipAt(h, editor, ErrorLine));

        // The tooltip names the gesture the command has now; without a gesture it names the command.
        h.Services.Keymap.SetGestures("edit.quickActions", [Core.Commands.KeyGesture.Parse("Alt+Enter")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Quick actions available (Alt+Enter)" + nl + error, TipAt(h, editor, ErrorLine));
        h.Services.Keymap.SetGestures("edit.quickActions", []);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Quick actions available (Quick Actions…)" + nl + error, TipAt(h, editor, ErrorLine));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-150")]
    [Trait("Req", "UI-ED-012")]
    [Trait("Req", "UI-ED-021")]
    public async Task ClickInGlyphMarginStillTogglesBreakpoint()
    {
        var (h, editor, doc, probe, asked) = await StartWithMarkerAsync();
        await using var _ = h;
        var breakpoints = h.Services.Breakpoints!;
        var caret = Unit.IndexOf("Count", StringComparison.Ordinal) + 2;
        PlaceCaret(editor, doc, caret);
        await RestAsync(h, asked);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));

        void Click()
        {
            h.Window.MouseDown(Slot(h, editor, ErrorLine), MouseButton.Left);
            h.Window.MouseUp(Slot(h, editor, ErrorLine), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        // A click on the slot that shows the bulb sets a breakpoint, as on every line: the bulb is an indicator, no button.
        var nl = Environment.NewLine;
        var tip = Assert.IsType<string>(TipAt(h, editor, ErrorLine)); // the pointer rests on the slot from here on
        Assert.StartsWith(BulbTip + nl, tip, StringComparison.Ordinal);
        var requests = h.Services.CodeModel.Worker.RequestCount;
        Click();
        var breakpoint = Assert.Single(breakpoints.All);
        Assert.Equal((BuildScenario.UnitPath(h), ErrorLine), (breakpoint.Path, breakpoint.Line));
        Assert.Equal("Breakpoint", probe.GlyphAt(ErrorLine));
        Assert.Equal("editor.breakpoint.line.bg", probe.LineBackgroundAt(ErrorLine));
        // No menu opened, nothing was asked or changed, the caret is where it was.
        Assert.False(editor.TextArea.ContextMenu?.IsOpen == true);
        Assert.False(Gutter(editor).ContextMenu?.IsOpen == true);
        Assert.Equal(requests, h.Services.CodeModel.Worker.RequestCount);
        Assert.False(h.Services.Refactoring.IsBusy);
        Assert.Equal(caret, editor.TextArea.Caret.Offset);
        Assert.Equal(Unit, doc.Buffer.Text);
        // The tooltip under the resting pointer follows the glyphs of its line (UI-ED-021).
        Assert.Equal("Breakpoint at line 7" + nl + tip, ToolTip.GetTip(Gutter(editor)));

        // A second click removes the breakpoint; the bulb of the caret line is drawn again.
        Click();
        Assert.Empty(breakpoints.All);
        Assert.Equal("QuickAction", probe.GlyphAt(ErrorLine));
        Assert.Equal(tip, ToolTip.GetTip(Gutter(editor)));

        // The caret leaves the line: the tooltip of the resting pointer is the error marker's alone.
        PlaceCaret(editor, doc, LineStart(editor, 2));
        Assert.Equal("Error", probe.GlyphAt(ErrorLine));
        var error = tip[(BulbTip + nl).Length..];
        Assert.Equal(error, ToolTip.GetTip(Gutter(editor)));
        Assert.False(editor.TextArea.ContextMenu?.IsOpen == true);

        // A pointer that comes back with a click (no move in between) gets the tooltip of the clicked line as it is then.
        var text = editor.TextArea.TextView;
        h.Window.MouseMove(text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), h.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Click();
        Assert.Equal("Breakpoint", probe.GlyphAt(ErrorLine));
        Assert.Equal("Breakpoint at line 7" + nl + error, ToolTip.GetTip(Gutter(editor)));
    }
}
