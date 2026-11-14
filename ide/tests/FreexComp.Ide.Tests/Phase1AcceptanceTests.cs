using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>
/// The Phase 1 acceptance scenario (phase1-shell.md §14, ROADMAP §3 Phase 1 exit check): create, edit, save, close and
/// reopen a project through the headless shell; the reopen is done three ways (File › Open Recent, the Welcome page,
/// File › Open Project) and each restores the documents, the active one and the carets.
/// </summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class Phase1AcceptanceTests
{
    private const string WelcomeRow = "Welcome.Recent.Row.";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-071")]
    [Trait("Req", "IDE-PM-023")]
    [Trait("Req", "IDE-PM-040")]
    [Trait("Req", "IDE-P1-085")]
    [Trait("Req", "IDE-P1-113")]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-WEL-005")]
    [Trait("Req", "UI-WIN-072")]
    public async Task CreateEditSaveReopenProject()
    {
        // 1. An empty user directory (no settings, no recent items): the Welcome page is active; Build is disabled.
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings.Clear());
        Assert.IsType<WelcomeDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.True(h.Find<Control>("Welcome").IsEffectivelyVisible);
        Assert.False(MenuEnabled(h, "Project", "project.build"));
        Assert.False(h.Services.Commands.CanExecute("project.build"));

        // 2. New Project: name Hello in a temporary location, Enter.
        h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);
        var creating = h.Services.Commands.ExecuteAsync("file.newProject");
        var dlg = await h.WaitForDialogAsync("NewProject");
        Assert.Equal("Dialog.NewProject.Name", dlg.FocusedAutomationId); // its default text is selected: typing replaces it
        Assert.Equal(h.TempDir, dlg.Find<TextBox>("Location").Text);
        dlg.Window.KeyTextInput("Hello");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Hello", dlg.Find<TextBox>("Name").Text);
        await h.WaitForAsync(() => !((Core.Dialogs.NewProjectViewModel)dlg.Window.Model).IsChecking, "New Project check done");
        dlg.Press("Enter");
        await creating;
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath is { } p && p.EndsWith("Hello.pas", StringComparison.Ordinal),
            "Hello.pas open");

        var dir = Path.Combine(h.TempDir, "Hello");
        var pproj = Path.Combine(dir, "Hello.pproj");
        var helloPath = Path.Combine(dir, "Hello.pas");
        var unitPath = Path.Combine(dir, "Unit1.pas");
        Assert.Equal(["Hello.pas", "Hello.pproj"], Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var pm = Assert.IsType<ProjectManagerViewModel>(h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName));
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Assert.Equal("Hello", pm.Find("$project")?.Name);
        Assert.NotNull(pm.Find("Hello.pas"));
        Assert.True(h.Find<Control>("Tool.ProjectManager.Row.Hello.pas").IsEffectivelyVisible);
        var hello = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(helloPath, hello.FilePath);
        Assert.Equal("Hello – FreexComp", h.Window.Title);

        // 3. Highlighting, through the editor probe.
        var helloEditor = h.Find<TextEditor>(AutomationIds.Editor("Hello.pas"));
        var probe = Probe(helloEditor);
        Assert.Equal("syntax.keyword", probe.TokenClassAt(hello.Buffer.Text.IndexOf("program", StringComparison.Ordinal)));
        Assert.Equal("syntax.string", probe.TokenClassAt(hello.Buffer.Text.IndexOf("'Hello, world'", StringComparison.Ordinal)));

        // 4. Type a new line "  WriteLn('x');" before "end." (Enter at the end of line 7 auto-indents by two spaces).
        Focus(helloEditor);
        h.Press("Ctrl+Home");
        for (var i = 1; i < 7; i++)
            h.Press("Down");
        h.Press("End");
        h.Press("Enter");
        h.Type("WriteLn('x');");
        Assert.Equal("  WriteLn('x');", Line(hello.Buffer, 8));
        Assert.Equal("end.", Line(hello.Buffer, 9));
        Assert.NotNull(h.TryFind<Control>("DocumentTab.Hello.pas.Modified"));
        Assert.True(MenuEnabled(h, "File", "file.save"));

        // 5. Ctrl+Z until unmodified, then Ctrl+Y as many times.
        var undos = 0;
        while (hello.IsModified)
        {
            Assert.True(++undos <= 20, "undo never reached the save point");
            h.Press("Ctrl+Z");
        }

        Assert.Null(h.TryFind<Control>("DocumentTab.Hello.pas.Modified"));
        for (var i = 0; i < undos; i++)
            h.Press("Ctrl+Y");
        Assert.NotNull(h.TryFind<Control>("DocumentTab.Hello.pas.Modified"));
        Assert.Equal("  WriteLn('x');", Line(hello.Buffer, 8));

        // 6. Add a new unit; Save All writes both units and the project file.
        await h.ExecuteAsync("project.addNewUnit");
        var unit = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(unitPath, unit.FilePath);
        Assert.NotNull(pm.Find("Unit1.pas"));
        Assert.Contains(h.Services.Projects.Project!.Files, f => f.Path == "Unit1.pas");
        h.Press("Ctrl+Shift+S");
        await h.WaitForAsync(() => !hello.IsModified && !unit.IsModified, "everything saved");
        Assert.DoesNotContain(h.FindAll("DocumentTab."), c => AutomationIds.Get(c)!.EndsWith(".Modified", StringComparison.Ordinal));
        AssertSource(helloPath,
            "program Hello;\r\n\r\nuses\r\n  SysUtils;\r\n\r\nbegin\r\n  WriteLn('Hello, world');\r\n  WriteLn('x');\r\nend.\r\n");
        AssertSource(unitPath, "unit Unit1;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n");
        var projectBytes = File.ReadAllBytes(pproj);
        Assert.False(HasBom(projectBytes));
        var projectText = Encoding.UTF8.GetString(projectBytes);
        Assert.DoesNotContain('\r', projectText);
        Assert.Contains("\"Unit1.pas\"", projectText, StringComparison.Ordinal);
        Assert.Contains("\"Hello.pas\"", projectText, StringComparison.Ordinal);

        // 7. Find in Hello.pas: "WriteLn", Enter → the first match is selected, the counter says 1 of 2.
        h.Click(AutomationIds.DocumentTab("Hello.pas"));
        Assert.Same(hello, h.Services.Documents.ActiveDocument);
        helloEditor = h.Find<TextEditor>(AutomationIds.Editor("Hello.pas")); // the document area shows one editor at a time
        Focus(helloEditor);
        h.Press("Ctrl+Home");
        h.Press("Ctrl+F");
        Assert.Equal("Editor.Hello.pas.FindPanel.SearchText", h.FocusedAutomationId);
        h.Type("WriteLn");
        h.Press("Enter");
        h.Clock.Advance(FindSession.Delay);
        Dispatcher.UIThread.RunJobs();
        var first = hello.Buffer.Text.IndexOf("WriteLn", StringComparison.Ordinal);
        Assert.Equal((first, 7), (helloEditor.SelectionStart, helloEditor.SelectionLength));
        Assert.Equal("1 of 2", h.Find<TextBlock>("Editor.Hello.pas.FindPanel.MatchCount").Text);
        h.Press("Escape"); // closes the panel; the match stays selected
        Assert.Equal(AutomationIds.Editor("Hello.pas"), h.FocusedAutomationId);

        // 8. Unit1.pas changes on disk: the unmodified document reloads silently.
        const string changed = "unit Unit1;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\n// changed outside\r\n\r\nend.\r\n";
        h.WriteFileExternally(Path.Combine("Hello", "Unit1.pas"), changed);
        await h.WaitForAsync(() => unit.Buffer.Text == changed, "Unit1.pas reloaded");
        await h.Services.FileChanges.WhenIdle();
        Assert.False(unit.IsModified);
        Assert.Empty(unit.InfoBars);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_ReloadedSilently, "Unit1.pas"), h.Status.Message);

        // 9. Close the project and reopen it from File › Open Recent: same documents, same active one, same carets.
        var before = Snapshot(h);
        Assert.Equal(helloPath, before.Active);
        Assert.Equal(new TextPosition(7, 10), before.Carets[helloPath]); // the end of the selected match
        await ReopenAsync(h, () => h.ExecuteAsync("file.openRecentProject", pproj), before, "File › Open Recent");

        // … from the Welcome page (keyboard: the recent row, Enter), after moving the caret in Hello.pas.
        Focus(h.Find<TextEditor>(AutomationIds.Editor("Hello.pas")));
        h.Press("Ctrl+End");
        h.Press("Up");
        before = Snapshot(h);
        Assert.Equal(new TextPosition(9, 1), before.Carets[helloPath]);
        await ReopenAsync(h, async () =>
        {
            await h.ExecuteAsync("view.welcomePage");
            var page = Assert.IsType<WelcomeDocumentViewModel>(h.Services.Documents.ActiveDocument);
            await h.WaitForAsync(() => !page.IsCheckingMissing, "missing-project check done");
            h.Find<Button>("Welcome.NewProject").Focus(NavigationMethod.Tab);
            h.Press("Tab");
            h.Press("Tab");
            h.Press("Tab");
            Assert.Equal(WelcomeRow + pproj, h.FocusedAutomationId);
            h.Press("Enter");
        }, before, "Welcome page");

        // … from File › Open Project (Ctrl+Shift+O and the picker), with Unit1.pas active and its caret moved.
        h.Click(AutomationIds.DocumentTab("Unit1.pas"));
        Focus(h.Find<TextEditor>(AutomationIds.Editor("Unit1.pas")));
        h.Press("Ctrl+Home");
        h.Press("Down");
        h.Press("Down");
        h.Press("End");
        before = Snapshot(h);
        Assert.Equal((unitPath, new TextPosition(3, 10)), (before.Active, before.Carets[unitPath]));
        await ReopenAsync(h, () =>
        {
            h.FileDialogs.NextOpen(pproj);
            h.Press("Ctrl+Shift+O");
            return Task.CompletedTask;
        }, before, "File › Open Project");

        // 10. Exit: UI state and session written, no recovery folder left (binding errors are checked at dispose).
        await h.ExecuteAsync("file.exit");
        await h.WaitForAsync(() => !h.Window.IsVisible, "main window closed");
        Assert.Empty(h.Messages.Requests);
        Assert.True(File.Exists(Path.Combine(h.UserDir, "roaming", "uistate.json")));
        Assert.True(File.Exists(Path.Combine(dir, ".freexcomp", "session.json")));
        Assert.False(Directory.Exists(h.Services.Recovery.InstanceDirectory));
        var recoveryRoot = Path.Combine(h.UserDir, "local", "recovery");
        Assert.True(!Directory.Exists(recoveryRoot) || Directory.GetFileSystemEntries(recoveryRoot).Length == 0);
    }

    private sealed record SessionSnapshot(string? Active, IReadOnlyList<string> Documents, IReadOnlyDictionary<string, TextPosition> Carets);

    /// <summary>The open text documents (in tab order), the active one and every caret.</summary>
    private static SessionSnapshot Snapshot(HeadlessHarness h)
    {
        var documents = h.Services.Documents.Documents.OfType<TextDocumentViewModel>().ToList();
        return new SessionSnapshot(
            h.Services.Documents.ActiveDocument?.FilePath,
            [.. documents.Select(d => d.FilePath!)],
            documents.ToDictionary(d => d.FilePath!, d => d.ViewState.Caret, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Closes the project (its documents close, the Welcome page stays), reopens it and compares the session.</summary>
    private static async Task ReopenAsync(HeadlessHarness h, Func<Task> reopen, SessionSnapshot before, string how)
    {
        await h.ExecuteAsync("file.closeProject");
        Assert.Null(h.Services.Projects.Project);
        Assert.Empty(h.Services.Documents.Documents.OfType<TextDocumentViewModel>());
        Assert.Empty(h.Messages.Requests); // everything was saved: no prompt

        await reopen();
        await h.WaitForAsync(() => h.Services.Projects.Project?.Name == "Hello"
            && h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Count() == before.Documents.Count
            && h.Services.Documents.ActiveDocument is TextDocumentViewModel, $"project reopened ({how})");

        var after = Snapshot(h);
        Assert.Equal(before.Documents.Order(StringComparer.OrdinalIgnoreCase), after.Documents.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(before.Active, after.Active);
        foreach (var (path, caret) in before.Carets)
            Assert.Equal((how, path, caret), (how, path, after.Carets[path]));

        // The view shows the restored caret of the active document.
        var active = (TextDocumentViewModel)h.Services.Documents.ActiveDocument!;
        var editor = h.Find<TextEditor>(AutomationIds.Editor(Path.GetFileName(active.FilePath!)));
        Assert.Equal((how, before.Carets[active.FilePath!]), (how, new TextPosition(editor.TextArea.Caret.Line, editor.TextArea.Caret.Column)));
        Assert.Equal("Hello – FreexComp", h.Window.Title);
    }

    private static void AssertSource(string path, string expected)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.False(HasBom(bytes), path + " has a BOM");
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    private static bool HasBom(byte[] bytes) => bytes is [0xEF, 0xBB, 0xBF, ..];

    private static string Line(ITextBuffer buffer, int line)
    {
        var (offset, length, _) = buffer.GetLine(line);
        return buffer.GetText(offset, length);
    }

    private static IEditorTestProbe Probe(TextEditor editor) => editor.FindAncestorOfType<CodeEditorView>()!;

    private static void Focus(TextEditor editor)
    {
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Opens the top-level menu <paramref name="top"/> and reads whether the item of <paramref name="commandId"/> is enabled.</summary>
    private static bool MenuEnabled(HeadlessHarness h, string top, string commandId)
    {
        static IEnumerable<MenuItem> All(IEnumerable<object?> items) =>
            items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(All(i.Items.Cast<object?>())));
        var items = All(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object?>()).ToList();
        var menu = items.Single(i => AutomationIds.Get(i) == AutomationIds.TopMenu(top));
        menu.Open();
        Dispatcher.UIThread.RunJobs();
        try
        {
            return items.Single(i => AutomationIds.Get(i) == AutomationIds.Menu(commandId)).IsEffectivelyEnabled;
        }
        finally
        {
            menu.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
