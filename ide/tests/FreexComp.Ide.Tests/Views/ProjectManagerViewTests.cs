using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Project Manager tool window (tool-windows.md §2, project-manager.md §6).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class ProjectManagerViewTests
{
    private const string Row = "Tool.ProjectManager.Row.";

    /// <summary>Hello/Hello.pproj with the files of the tool-windows.md §2 wireframe plus a nested and an external file.</summary>
    private static void HelloProject(HarnessOptions o, string extraFiles = "")
    {
        o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "notes.md" }, { "path": "Unit1.pas" }, { "path": "lib/StrUtils2.pas" }, { "path": "Hello.pas" },
                         { "path": "MainForm.pas", "form": "MainForm.form" }, { "path": "Missing.pas" },
                         { "path": "../Shared/Common.pas" }, { "path": "aaa/Zed.pas" }{{extraFiles}} ] }
            """;
        o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
        o.InitialFiles[@"Hello\Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
        o.InitialFiles[@"Hello\MainForm.pas"] = "unit MainForm;\ninterface\nimplementation\nend.\n";
        o.InitialFiles[@"Hello\MainForm.form"] = "object MainForm: TForm\nend\n";
        o.InitialFiles[@"Hello\lib\StrUtils2.pas"] = "unit StrUtils2;\ninterface\nimplementation\nend.\n";
        o.InitialFiles[@"Hello\aaa\Zed.pas"] = "unit Zed;\ninterface\nimplementation\nend.\n";
        o.InitialFiles[@"Hello\notes.md"] = "notes\n";
        o.InitialFiles[@"Shared\Common.pas"] = "unit Common;\ninterface\nimplementation\nend.\n";
    }

    private static ProjectManagerViewModel Pm(HeadlessHarness h) =>
        Assert.IsType<ProjectManagerViewModel>(h.Services.ToolWindows.Get("ProjectManager"));

    private static async Task<ProjectManagerViewModel> OpenHelloAsync(HeadlessHarness h)
    {
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
        var pm = Pm(h);
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        return pm;
    }

    private static IReadOnlyList<string> VisibleKeys(ProjectManagerViewModel pm) => [.. pm.VisibleRows().Select(n => n.Key)];

    private static string IconOf(HeadlessHarness h, string key) =>
        h.Find<IdeIcon>(Row + key + ".Icon").IconName!;

    /// <summary>
    /// tool-windows.md §2: with the Project Manager focused, <c>file.save</c>, <c>file.copyPath</c> and
    /// <c>file.showInExplorer</c> act on the selected node(s); elsewhere on the active document.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-014")]
    [Trait("Req", "UI-ED-079")]
    public async Task CommandsActOnSelectedNode()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        var dir = Path.Combine(h.TempDir, "Hello");
        var shell = (FreexComp.Ide.TestSupport.FakeShellService)h.Services.Shell;
        var clipboard = (FreexComp.Ide.TestSupport.FakeClipboard)h.Services.Clipboard;
        var commands = h.Services.Commands;
        var unit1 = Path.Combine(dir, "Unit1.pas");
        var strUtils = Path.Combine(dir, "lib", "StrUtils2.pas");

        // Outside the Project Manager: the active document.
        var unit = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(unit1)).Value);
        await h.ExecuteAsync("file.copyPath");
        Assert.Equal(unit1, clipboard.Text);
        await h.ExecuteAsync("file.showInExplorer");
        Assert.Equal(["RevealFile " + unit1], shell.Calls);

        // In the Project Manager: the selected file, even when another document is active.
        unit.Buffer.ApplyEdits([new Core.Text.TextEdit(0, 0, "{ x }")]);
        var other = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(strUtils)).Value);
        Assert.Same(other, h.Services.Documents.ActiveDocument);
        h.Press("Alt+1");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ProjectManagerViewModel.ToolName, h.Services.FocusTracker.Current.ToolWindowName);
        pm.Select(pm.Find("Unit1.pas")!);
        Assert.True(commands.CanExecute("file.save"));
        await h.ExecuteAsync("file.save");
        Assert.False(unit.IsModified);
        Assert.StartsWith("{ x }", File.ReadAllText(unit1), StringComparison.Ordinal);
        Assert.False(commands.CanExecute("file.save")); // nothing selected is modified
        await h.ExecuteAsync("file.copyPath");
        Assert.Equal(unit1, clipboard.Text);

        // A folder, the project root, a missing file.
        pm.Select(pm.Find("lib/")!);
        Assert.False(commands.CanExecute("file.save"));
        await h.ExecuteAsync("file.copyPath");
        Assert.Equal(Path.Combine(dir, "lib"), clipboard.Text);
        await h.ExecuteAsync("file.showInExplorer");
        Assert.Equal("RevealFile " + Path.Combine(dir, "lib"), shell.Calls[^1]);
        pm.Select(pm.Find("$project")!);
        await h.ExecuteAsync("file.copyPath");
        Assert.Equal(Path.Combine(dir, "Hello.pproj"), clipboard.Text);
        pm.Select(pm.Find("Missing.pas")!);
        Assert.True(commands.CanExecute("file.copyPath"));
        Assert.False(commands.CanExecute("file.showInExplorer")); // nothing on disk to show

        // Several files: Save saves the modified ones among them; path commands need one node.
        other.Buffer.ApplyEdits([new Core.Text.TextEdit(0, 0, "{ y }")]);
        unit.Buffer.ApplyEdits([new Core.Text.TextEdit(0, 0, "{ z }")]);
        pm.Select(pm.Find("Unit1.pas")!);
        pm.Select(pm.Find("notes.md")!, ProjectSelectMode.Toggle);
        Assert.False(commands.CanExecute("file.copyPath"));
        Assert.False(commands.CanExecute("file.showInExplorer"));
        await h.ExecuteAsync("file.save");
        Assert.False(unit.IsModified);
        Assert.True(other.IsModified); // not selected
        pm.ClearSelection();
        Assert.False(commands.CanExecute("file.save"));
        Assert.False(commands.CanExecute("file.copyPath"));

        // Back in the editor: the active document again.
        h.Press("Alt+0");
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(ProjectManagerViewModel.ToolName, h.Services.FocusTracker.Current.ToolWindowName);
        await h.ExecuteAsync("file.save");
        Assert.False(other.IsModified);
        await h.ExecuteAsync("file.copyPath");
        Assert.Equal(strUtils, clipboard.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-100")]
    [Trait("Req", "UI-TW-101")]
    [Trait("Req", "UI-TW-102")]
    [Trait("Req", "UI-TW-103")]
    [Trait("Req", "UI-TW-104")]
    [Trait("Req", "UI-TW-105")]
    [Trait("Req", "UI-TW-106")]
    [Trait("Req", "UI-TW-108")]
    [Trait("Req", "UI-TW-111")]
    [Trait("Req", "UI-TW-114")]
    public async Task TreeOrderAndFormChild()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        Assert.Equal(CommandScope.ProjectManager, h.Services.ToolWindows.Registered.Single(d => d.Name == "ProjectManager").Scope);
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();

        // Folders first, then files with the main source first; External last; the unit with a form is collapsed.
        Assert.Equal(
            ["$project", "aaa/", "aaa/Zed.pas", "lib/", "lib/StrUtils2.pas", "Hello.pas", "MainForm.pas", "Missing.pas", "notes.md",
             "Unit1.pas", "$external/", "../Shared/Common.pas"],
            VisibleKeys(pm));
        Assert.Equal(VisibleKeys(pm).Select(k => Row + k),
            h.FindAll(Row).OfType<TreeViewItem>().Where(i => i.IsEffectivelyVisible).Select(AutomationIds.Get));
        Assert.False(pm.Find("MainForm.pas")!.IsExpanded);
        Assert.NotNull(pm.Find("MainForm.form"));

        // Root: bold name, badge, tooltip = project file.
        var rootName = h.Find<TextBlock>(Row + "$project.Name");
        Assert.Equal("Hello", rootName.Text);
        Assert.Contains("bodyStrong", rootName.Classes);
        Assert.Equal("Debug · win64", h.Find<TextBlock>(Row + "$project.Badge").Text);
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "Hello.pproj"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + "$project")));
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "Unit1.pas"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + "Unit1.pas")));
        Assert.Equal("External", h.Find<TextBlock>(Row + "$external/.Name").Text);
        Assert.Equal("Common.pas", h.Find<TextBlock>(Row + "../Shared/Common.pas.Name").Text);

        // Icons (UI-TW-104).
        Assert.Equal("project", IconOf(h, "$project"));
        Assert.Equal("open-folder", IconOf(h, "lib/"));
        Assert.Equal("unit", IconOf(h, "Unit1.pas"));
        Assert.Equal("file", IconOf(h, "notes.md"));
        Assert.Equal("unit", IconOf(h, "Hello.pas"));
        Assert.True(h.Find<IdeIcon>(Row + "Hello.pas.Icon.MainSource").IsVisible);
        Assert.True(h.Find<IdeIcon>(Row + "Missing.pas.Icon.Missing").IsVisible);
        Assert.False(h.Find<IdeIcon>(Row + "Missing.pas.Icon").IsEnabled); // drawn in text.disabled
        pm.Find("lib/")!.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("folder", IconOf(h, "lib/"));

        // The form child is the unit's only child and never a sibling (UI-TW-103).
        pm.Find("MainForm.pas")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["MainForm.form"], pm.Find("MainForm.pas")!.Children.Select(c => c.Key));
        Assert.Equal("form", IconOf(h, "MainForm.form"));
        Assert.DoesNotContain(pm.Find("$project")!.Children, c => c.Key == "MainForm.form");

        // The main source is the active document after open: bold row (UI-TW-106), no auto-select.
        Assert.Contains("bodyStrong", h.Find<TextBlock>(Row + "Hello.pas.Name").Classes);
        Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(Row + "Unit1.pas.Name").Classes);
        Assert.Empty(pm.Selection);

        // Default action (Enter) on a file row opens it and focuses the editor (UI-TW-108).
        h.Press("Alt+1");
        pm.Select(pm.Find("Unit1.pas")!);
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath?.EndsWith("Unit1.pas", StringComparison.Ordinal) == true, "Unit1 opened");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        Assert.Contains("bodyStrong", h.Find<TextBlock>(Row + "Unit1.pas.Name").Classes);

        // Modified marker (UI-TW-105).
        Assert.Null(h.TryFind<Control>(Row + "Unit1.pas.Modified"));
        h.Type("x");
        Assert.NotNull(h.TryFind<Control>(Row + "Unit1.pas.Modified"));
        Assert.Equal("Unit1.pas, modified", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "Unit1.pas")));
        await h.ExecuteAsync("file.save");
        Assert.Null(h.TryFind<Control>(Row + "Unit1.pas.Modified"));
        Assert.Equal("Unit1.pas", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "Unit1.pas")));

        // Default action on a folder toggles it.
        pm.Select(pm.Find("aaa/")!);
        await h.ExecuteAsync("projectManager.open");
        Assert.False(pm.Find("aaa/")!.IsExpanded);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-107")]
    [Trait("Req", "IDE-PM-103")]
    public async Task MissingFileInfoBar()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o, """, { "path": "lib/Gone.pas" }"""));
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();

        var bar = h.Find<Control>("Tool.ProjectManager.InfoBar.MissingFiles");
        Assert.Equal("2 files listed in the project are missing on disk.", h.Find<TextBlock>("Tool.ProjectManager.InfoBar.MissingFiles.Text").Text);
        Assert.True(bar.IsVisible);
        Assert.True(pm.Find("Missing.pas")!.IsMissing);
        Assert.True(pm.Find("lib/Gone.pas")!.IsMissing);
        Assert.False(pm.Find("Unit1.pas")!.IsMissing);
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "lib", "Gone.pas"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + "lib/Gone.pas")));

        // Show selects the first missing file in tree order.
        h.Click("Tool.ProjectManager.InfoBar.MissingFiles.Action.Show");
        Assert.Equal(["lib/Gone.pas"], pm.Selection.Select(n => n.Key));

        // Remove from Project removes all missing entries after the confirmation and saves the project at once.
        h.Messages.Answer("Msg.Project.RemoveFilesConfirm", "Remove");
        h.Click("Tool.ProjectManager.InfoBar.MissingFiles.Action.RemoveFromProject");
        await h.WaitForAsync(() => pm.Find("Missing.pas") is null, "missing entries removed");
        var request = Assert.Single(h.Messages.Requests, r => r.MessageId == "Msg.Project.RemoveFilesConfirm");
        Assert.Equal(["2"], request.Args);
        var saved = File.ReadAllText(Path.Combine(h.TempDir, "Hello", "Hello.pproj"));
        Assert.DoesNotContain("Missing.pas", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("Gone.pas", saved, StringComparison.Ordinal);
        Assert.Contains("Unit1.pas", saved, StringComparison.Ordinal);
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "check after the removal");
        Assert.Null(pm.MissingFilesBar);
        Assert.Null(h.TryFind<Control>("Tool.ProjectManager.InfoBar.MissingFiles"));

        // A file that disappears later is found by the next check (window activation); one file uses the singular text.
        File.Delete(Path.Combine(h.TempDir, "Hello", "notes.md"));
        h.Window.Activate();
        await h.WaitForAsync(() => pm.MissingFilesBar is not null, "missing-file bar after activation");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 file listed in the project is missing on disk.", h.Find<TextBlock>("Tool.ProjectManager.InfoBar.MissingFiles.Text").Text);
        h.Messages.Answer("Msg.Project.RemoveFileConfirm", "Cancel");
        h.Click("Tool.ProjectManager.InfoBar.MissingFiles.Action.RemoveFromProject");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["notes.md"], Assert.Single(h.Messages.Requests, r => r.MessageId == "Msg.Project.RemoveFileConfirm").Args);
        Assert.NotNull(pm.Find("notes.md")); // canceled: kept
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-009")]
    [Trait("Req", "UI-TW-110")]
    public async Task FilterMatchesNamesAndRestoresExpansion()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        pm.Find("lib/")!.IsExpanded = false;

        pm.FilterText = "STR";
        Assert.Equal(11, VisibleKeys(pm).Count); // applied only after 150 ms
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["$project", "lib/", "lib/StrUtils2.pas"], VisibleKeys(pm));
        Assert.True(pm.Find("lib/")!.IsExpanded); // ancestors of matches are expanded while filtering
        Assert.Equal((0, 3), (pm.Find("lib/StrUtils2.pas")!.MatchStart, pm.Find("lib/StrUtils2.pas")!.MatchLength));
        Assert.False(h.Find<TreeViewItem>(Row + "Unit1.pas").IsVisible);

        pm.FilterText = "shared"; // paths are not matched (UI-TW-110)
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Empty(VisibleKeys(pm));

        pm.FilterText = "";
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Dispatcher.UIThread.RunJobs();
        Assert.False(pm.Find("lib/")!.IsExpanded); // the expansion from before filtering returns
        Assert.Equal(11, VisibleKeys(pm).Count);
        Assert.Equal(-1, pm.Find("lib/StrUtils2.pas")!.MatchStart);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-014")]
    [Trait("Req", "UI-TW-109")]
    [Trait("Req", "UI-MENU-042")]
    [Trait("Req", "UI-TW-013")]
    [Trait("Req", "IDE-PM-101")]
    public async Task ContextMenusAndSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 1; // the Phase 1 menus (the Phase 3 root menu: RootContextMenuHasBuildAndRun)
            HelloProject(o);
        });
        var pm = Pm(h);
        Dispatcher.UIThread.RunJobs();

        // Empty state without a project: message, links, tool bar disabled.
        Assert.True(h.Find<Control>("Tool.ProjectManager.Empty").IsVisible);
        Assert.Contains("No project is open", h.Find<Control>("Tool.ProjectManager.Empty").GetLogicalDescendantsTexts());
        Assert.True(h.Find<Button>("Tool.ProjectManager.Empty.Link.file.openProject").IsEffectivelyEnabled);
        Assert.False(h.Find<Button>("Tool.ProjectManager.ToolBar.projectManager.collapseAll").IsEffectivelyEnabled);
        Assert.False(h.Find<Button>("Tool.ProjectManager.ToolBar.projectManager.syncWithEditor").IsEffectivelyEnabled);

        await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>("Tool.ProjectManager.Empty").IsVisible);
        Assert.True(h.Find<Button>("Tool.ProjectManager.ToolBar.projectManager.collapseAll").IsEffectivelyEnabled);

        static string[] Ids(IReadOnlyList<Core.Menus.MenuItemModel> menu) => [.. menu.Select(m => m.Command?.Id ?? "-")];

        // Later-phase items are omitted (UI-MENU-042); "-" = separator.
        pm.Select(pm.Find("$project")!);
        Assert.Equal(["project.addNewUnit", "project.addExistingFile", "-", "project.viewSource", "file.showInExplorer", "file.copyPath", "-",
            "file.closeProject"], Ids(pm.ContextMenu()));
        pm.Select(pm.Find("lib/")!);
        Assert.Equal(["project.addNewUnit", "project.addExistingFile", "-", "file.showInExplorer", "file.copyPath"], Ids(pm.ContextMenu()));
        pm.Select(pm.Find("Unit1.pas")!);
        Assert.Equal(["projectManager.open", "-", "file.save", "-", "project.renameFile", "project.removeFile", "project.deleteFile", "-",
            "file.showInExplorer", "file.copyPath"], Ids(pm.ContextMenu()));
        pm.Select(pm.Find("notes.md")!, ProjectSelectMode.Toggle);
        Assert.Equal(["Unit1.pas", "notes.md"], pm.Selection.Select(n => n.Key));
        Assert.Equal(["projectManager.open", "-", "project.removeFile", "project.deleteFile"], Ids(pm.ContextMenu()));
        pm.ClearSelection();
        Assert.Equal(["project.addNewUnit", "project.addExistingFile", "-", "projectManager.collapseAll"], Ids(pm.ContextMenu()));

        // Folder and root rows cannot join a multi-selection (UI-TW-109).
        pm.Select(pm.Find("Unit1.pas")!);
        pm.Select(pm.Find("lib/")!, ProjectSelectMode.Toggle);
        Assert.Equal(["lib/"], pm.Selection.Select(n => n.Key));
        pm.Select(pm.Find("Unit1.pas")!, ProjectSelectMode.Toggle);
        Assert.Equal(["Unit1.pas"], pm.Selection.Select(n => n.Key));

        // Right-click on an unselected row selects it, then opens its menu (UI-TW-014).
        h.RightClick(Row + "notes.md");
        Assert.Equal(["notes.md"], pm.Selection.Select(n => n.Key));
        var menu = h.Find<TreeViewItem>(Row + "notes.md").ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
        Assert.NotNull(menu);
        Assert.Contains("Context.projectManager.open", menu!.Items.OfType<MenuItem>().Select(AutomationIds.Get));
        menu.Close();

        // Tool bar: collapse all, then select the active document again (sync expands its ancestors).
        await h.ExecuteAsync("projectManager.collapseAll");
        Assert.Equal(["$project"], VisibleKeys(pm));
        Assert.True(await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Hello", "lib", "StrUtils2.pas")) is { Success: true });
        Assert.True(h.Services.Commands.CanExecute("projectManager.syncWithEditor"));
        await h.ExecuteAsync("projectManager.syncWithEditor");
        Assert.Equal(["lib/StrUtils2.pas"], pm.Selection.Select(n => n.Key));
        Assert.True(pm.Find("lib/")!.IsExpanded);
        Assert.True(h.Find<TreeViewItem>(Row + "lib/StrUtils2.pas").IsSelected);

        // A loose document cannot be synchronised (IDE-PM-101).
        File.WriteAllText(Path.Combine(h.TempDir, "loose.txt"), "x");
        Assert.True(await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "loose.txt")) is { Success: true });
        Assert.False(h.Services.Commands.CanExecute("projectManager.syncWithEditor"));

        // Closing the project empties the tree.
        await h.ExecuteAsync("file.closeProject");
        Assert.Empty(pm.Roots);
        Assert.True(h.Find<Control>("Tool.ProjectManager.Empty").IsVisible);
    }

    /// <summary>
    /// I-193, I-207: the context menus of <see cref="ContextMenusAndSelection"/> at the product's phase 5 (tool-windows.md
    /// §Project Manager; P5 <c>project.addNewForm</c> and, for units with a form and <c>.form</c> rows,
    /// <c>projectManager.openForm</c>; the delivered P8+ items of card F09-054 — <c>project.newFolder</c>,
    /// <c>project.addNewFile</c>, <c>project.moveFile</c>, <c>file.openInTerminal</c>, Remove on folder rows
    /// (phase8-project-files.md §8); other P8+ items such as <c>project.setActive</c> omitted, UI-MENU-042).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-TW-014")]
    [Trait("Req", "UI-MENU-042")]
    [Trait("Req", "UI-TW-108")]
    public async Task ContextMenusAtDefaultPhase()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = Pm(h);
        await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        static string[] Ids(IReadOnlyList<Core.Menus.MenuItemModel> menu) => [.. menu.Select(m => m.Command?.Id ?? "-")];

        pm.Select(pm.Find("$project")!);
        Assert.Equal(["project.build", "project.rebuild", "project.clean", "-", "run.start", "run.startWithoutDebugging", "-",
            "project.addNewUnit", "project.addNewForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", "-",
            "project.viewSource", "file.showInExplorer", "file.openInTerminal", "project.openOutputFolder", "file.copyPath", "-",
            "file.closeProject", "-", "project.options"], Ids(pm.ContextMenu()));
        pm.Select(pm.Find("lib/")!);
        Assert.Equal(["project.addNewUnit", "project.addNewForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", "-",
            "project.removeFile", "-", "file.showInExplorer", "file.openInTerminal", "file.copyPath"], Ids(pm.ContextMenu()));
        pm.Select(pm.Find("MainForm.pas")!);
        Assert.Equal(["projectManager.open", "projectManager.openForm", "-", "file.save", "-", "project.renameFile", "project.moveFile",
            "project.removeFile", "project.deleteFile", "-", "file.showInExplorer", "file.openInTerminal", "file.copyPath"], Ids(pm.ContextMenu()));
        Assert.True(h.Services.Commands.CanExecute("projectManager.openForm"));
        pm.Select(pm.Find("Unit1.pas")!);
        Assert.Equal(["projectManager.open", "-", "file.save", "-", "project.renameFile", "project.moveFile", "project.removeFile",
            "project.deleteFile", "-", "file.showInExplorer", "file.openInTerminal", "file.copyPath"], Ids(pm.ContextMenu()));
        Assert.False(h.Services.Commands.CanExecute("projectManager.openForm"));
        pm.Select(pm.Find("notes.md")!, ProjectSelectMode.Toggle);
        Assert.Equal(["projectManager.open", "-", "project.moveFile", "project.removeFile", "project.deleteFile"], Ids(pm.ContextMenu()));
        pm.ClearSelection();
        Assert.Equal(["project.addNewUnit", "project.addNewForm", "project.addExistingFile", "-", "projectManager.collapseAll"],
            Ids(pm.ContextMenu()));

        h.RightClick(Row + "notes.md");
        var menu = h.Find<TreeViewItem>(Row + "notes.md").ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
        Assert.NotNull(menu);
        Assert.Contains("Context.projectManager.open", menu!.Items.OfType<MenuItem>().Select(AutomationIds.Get));
        menu.Close();

        // projectManager.openForm on a .form row: the form document in its Design view (UI-TW-108, P5).
        pm.Select(pm.Find("MainForm.form")!);
        await h.ExecuteAsync("projectManager.openForm");
        var form = Assert.IsType<FormDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "MainForm.pas"), form.FilePath);
        Assert.Equal(DocumentView.Design, form.ActiveView);
    }

    /// <summary>
    /// IDE-PM-101 with UI-TW-017 (I-180): in a 300-unit project the tree is virtualized, so the active document's row
    /// has no container until it is scrolled to; sync-with-editor brings it into view (scroll by item) and selects it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-101")]
    [Trait("Req", "UI-TW-017")]
    public async Task SyncScrollsToUnrealizedRow()
    {
        var units = new System.Text.StringBuilder();
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            for (var i = 0; i < 300; i++)
            {
                units.Append($$""", { "path": "Gen{{i:D3}}.pas" }""");
                o.InitialFiles[$@"Hello\Gen{i:D3}.pas"] = $"unit Gen{i:D3};\ninterface\nimplementation\nend.\n";
            }

            HelloProject(o, units.ToString());
        });
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        const string last = Row + "Gen299.pas";
        Assert.Null(h.TryFind<TreeViewItem>(last)); // not realized: far below the viewport

        Assert.True(await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Hello", "Gen299.pas")) is { Success: true });
        await h.ExecuteAsync("projectManager.syncWithEditor");
        Dispatcher.UIThread.RunJobs();
        AssertSelectedInView();

        // Collapsed to the root row (the tree scrolls back to the top), sync expands the root and scrolls once more,
        // although the selection did not change.
        await h.ExecuteAsync("projectManager.collapseAll");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["$project"], VisibleKeys(pm));
        await h.ExecuteAsync("projectManager.syncWithEditor");
        Dispatcher.UIThread.RunJobs();
        AssertSelectedInView();

        void AssertSelectedInView()
        {
            Assert.Equal(["Gen299.pas"], pm.Selection.Select(n => n.Key));
            var row = h.TryFind<TreeViewItem>(last);
            Assert.NotNull(row);
            Assert.True(row!.IsSelected);
            // The row's visible part (its header: icon and name; the item's 1 px layout padding aside) is inside the viewport.
            var header = row.FindDescendantOfType<FreexComp.Ide.Views.ProjectNodeView>()!;
            var viewport = h.Find<TreeView>("Tool.ProjectManager.Tree").FindDescendantOfType<ScrollContentPresenter>()!;
            var top = header.TranslatePoint(new Point(0, 0), viewport)!.Value.Y;
            Assert.True(top >= 0 && top + header.Bounds.Height <= viewport.Bounds.Height,
                $"row at {top:F1}..{top + header.Bounds.Height:F1} inside the viewport of height {viewport.Bounds.Height:F1}");
        }
    }

    /// <summary>
    /// UI-A11Y-021: a row carrying a status icon includes the status word in its automation name — the missing-file
    /// overlay (UI-TW-104) adds ", missing"; it goes away when the file is back.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-021")]
    public async Task StatusIconRowsNameTheirStatus()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Missing.pas" + Strings.ProjectManager_MissingSuffix, AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "Missing.pas")));
        Assert.Equal("Unit1.pas", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "Unit1.pas")));

        File.WriteAllText(Path.Combine(h.TempDir, "Hello", "Missing.pas"), "unit Missing;\ninterface\nimplementation\nend.\n");
        await pm.CheckMissingFilesAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Missing.pas", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "Missing.pas")));
    }

    /// <summary>
    /// IDE-PM-100: a project mutation rebuilds the tree from the model (new row objects) and keeps the expansion state by
    /// node key and the selection where its key still exists.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-100")]
    public async Task RebuildKeepsExpansionAndSelectionByKey()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        pm.Find("lib/")!.IsExpanded = false;
        pm.Find("MainForm.pas")!.IsExpanded = true;
        var oldUnit1 = pm.Find("Unit1.pas")!;
        pm.Select(oldUnit1);

        Assert.True(await h.Services.Projects.RemoveFromProjectAsync(["notes.md"]));
        await h.WaitForAsync(() => pm.Find("notes.md") is null, "tree rebuilt");
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(oldUnit1, pm.Find("Unit1.pas")); // rebuilt, not patched
        Assert.False(pm.Find("lib/")!.IsExpanded);
        Assert.True(pm.Find("MainForm.pas")!.IsExpanded);
        Assert.True(pm.Find("aaa/")!.IsExpanded);
        Assert.Equal(["Unit1.pas"], pm.Selection.Select(n => n.Key));
        Assert.True(h.Find<TreeViewItem>(Row + "Unit1.pas").IsSelected);

        // The selected row's key disappears: nothing is selected.
        Assert.True(await h.Services.Projects.RemoveFromProjectAsync(["Unit1.pas"]));
        await h.WaitForAsync(() => pm.Find("Unit1.pas") is null, "tree rebuilt again");
        Assert.Empty(pm.Selection);
        Assert.False(pm.Find("lib/")!.IsExpanded);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-108")]
    [Trait("Req", "UI-TW-112")]
    [Trait("Req", "IDE-PM-060")]
    [Trait("Req", "IDE-PM-061")]
    [Trait("Req", "IDE-PM-070")]
    public async Task KeyboardOpenRenameDelete()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        var dir = Path.Combine(h.TempDir, "Hello");
        const string Editor = Row + "Unit1.pas.Editor";

        // Enter opens the selected file and focuses it.
        h.Press("Alt+1");
        pm.Select(pm.Find("Unit1.pas")!);
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");

        // F2 in the tree: the label becomes an input with the base name selected; Escape cancels.
        h.Press("Alt+1");
        Dispatcher.UIThread.RunJobs();
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == Editor, "rename input focused");
        var input = h.Find<TextBox>(Editor);
        Assert.Equal("Unit1.pas", input.Text);
        Assert.Equal((0, 5), (input.SelectionStart, input.SelectionEnd));
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<TextBox>(Editor));
        Assert.True(File.Exists(Path.Combine(dir, "Unit1.pas")));
        Assert.Equal("Tool.ProjectManager.Row.Unit1.pas", h.FocusedAutomationId); // back in the tree

        // An invalid name shows the message under the input, which stays open; Enter with a valid name commits.
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == Editor, "rename input focused again");
        h.Type("1bad");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Resources.Strings.Msg_Project_RenameInvalid, h.Find<TextBlock>(Row + "Unit1.pas.Error").Text);
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(h.TryFind<TextBox>(Editor));
        h.Find<TextBox>(Editor).SelectAll();
        h.Type("Tools.pas");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<TextBlock>(Row + "Unit1.pas.Error"));
        h.Press("Enter");
        await h.WaitForAsync(() => pm.Find("Tools.pas") is not null, "row renamed");
        Assert.False(File.Exists(Path.Combine(dir, "Unit1.pas")));
        Assert.StartsWith("unit Tools;", File.ReadAllText(Path.Combine(dir, "Tools.pas")), StringComparison.Ordinal);
        Assert.Equal(Path.Combine(dir, "Tools.pas"), h.Services.Documents.ActiveDocument!.FilePath);
        Assert.Equal(["Tools.pas"], pm.Selection.Select(n => n.Key));
        await h.WaitForAsync(() => h.FocusedAutomationId == Row + "Tools.pas", "focus on the renamed row");

        // Delete removes the selection from the project after the confirmation; the file stays.
        h.Messages.Answer("Msg.Project.RemoveFileConfirm", "Remove");
        h.Press("Delete");
        await h.WaitForAsync(() => pm.Find("Tools.pas") is null, "removed");
        Assert.True(File.Exists(Path.Combine(dir, "Tools.pas")));
        Assert.DoesNotContain("Tools.pas", File.ReadAllText(Path.Combine(dir, "Hello.pproj")), StringComparison.Ordinal);
        await h.WaitForAsync(() => h.FocusedAutomationId?.StartsWith(Row, StringComparison.Ordinal) == true, "focus stays in the tree");

        // Shift+Delete moves the file to the Recycle Bin and removes it.
        pm.Select(pm.Find("notes.md")!);
        Dispatcher.UIThread.RunJobs();
        h.Messages.Answer("Msg.Project.DeleteFileConfirm", "Delete");
        h.Press("Shift+Delete");
        await h.WaitForAsync(() => pm.Find("notes.md") is null, "deleted");
        Assert.False(File.Exists(Path.Combine(dir, "notes.md")));
        Assert.DoesNotContain("notes.md", File.ReadAllText(Path.Combine(dir, "Hello.pproj")), StringComparison.Ordinal);

        // The main source cannot be removed: the command is disabled for it.
        pm.Select(pm.Find("Hello.pas")!);
        Assert.False(h.Services.Commands.CanExecute("project.removeFile"));

        // With focus outside the tree, F2 is not bound and the command opens the RenameFile dialog (UI-DLG-070).
        h.Services.Documents.Activate(h.Services.Documents.Documents.Single(d => d.FilePath == Path.Combine(dir, "Hello.pas")));
        h.Press("Escape"); // back to the document (UI-A11Y-004)
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Hello.pas", "editor focused");
        var running = h.Services.Commands.ExecuteAsync("project.renameFile");
        var dlg = await h.WaitForDialogAsync("RenameFile");
        Assert.Equal("Rename File", dlg.Window.Title);
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.RenameFile.NewName", "name focused");
        Assert.Equal("Hello", dlg.Find<TextBox>("NewName").SelectedText);
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        Assert.True(dlg.Find<CheckBox>("UpdateReferences").IsChecked);
        dlg.Type("NewName", "x");
        Assert.Equal(Core.Resources.Strings.Msg_Project_RenameInvalid, dlg.ErrorText("NewName"));
        dlg.Find<TextBox>("NewName").Text = "Start.pas";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dlg.ErrorText("NewName"));
        dlg.Press("Enter");
        await running;
        Assert.StartsWith("program Start;", File.ReadAllText(Path.Combine(dir, "Start.pas")), StringComparison.Ordinal);
        Assert.Contains("\"mainSource\": \"Start.pas\"", File.ReadAllText(Path.Combine(dir, "Hello.pproj")), StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-113")]
    [Trait("Req", "IDE-PM-033")]
    [Trait("Req", "UI-MSG-002")]
    public async Task DropAddsFiles()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            HelloProject(o);
            o.InitialFiles[@"Hello\Added.pas"] = "unit Added;\ninterface\nimplementation\n{$FORM Added.form}\nend.\n";
            o.InitialFiles[@"Hello\Added.form"] = "object Added: TForm\nend\n";
            o.InitialFiles[@"Hello\docs\readme.txt"] = "readme\n";
        });
        var pm = await OpenHelloAsync(h);
        string Abs(string relative) => Path.Combine(h.TempDir, "Hello", relative);
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        var restBackground = tree.Background;

        // Files dragged over the tree: the drop-target highlight (accent.subtle); dragged away: gone again.
        var data = await h.DragFilesOverAsync("Tool.ProjectManager.Tree", Abs("Added.pas"));
        Assert.Equal(Color.Parse("#E3F2EF"), Assert.IsAssignableFrom<ISolidColorBrush>(tree.Background).Color);
        h.DragLeave(data);
        Assert.Equal(restBackground, tree.Background);

        // Dropped: added as project.addExistingFile does (a unit with its {$FORM} companion), saved at once, the added
        // rows selected (IDE-PM-033); nothing opens.
        await h.DropFilesAsync("Tool.ProjectManager.Tree", Abs("Added.pas"), Abs(@"docs\readme.txt"));
        await h.WaitForAsync(() => h.Services.Projects.IsMember(Abs(@"docs\readme.txt")), "files added");
        Assert.Equal(restBackground, tree.Background);
        Assert.True(h.Services.Projects.IsMember(Abs("Added.pas")));
        Assert.Contains(h.Services.Projects.Project!.Files, f => f.Path == "Added.pas" && f.Form == "Added.form");
        Assert.Contains("Added.form", File.ReadAllText(Abs("Hello.pproj")), StringComparison.Ordinal); // saved at once
        Assert.Equal([Abs("Added.pas"), Abs(@"docs\readme.txt")], pm.Selection.Select(n => n.FullPath).Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(h.Services.Documents.Documents, d => d.FilePath == Abs("Added.pas"));

        // A member dropped again: the status bar says so, the project is unchanged.
        var before = File.ReadAllText(Abs("Hello.pproj"));
        await h.DropFilesAsync("Tool.ProjectManager.Tree", Abs("Added.pas"));
        await h.WaitForAsync(() => h.Services.Status.Message is not null, "status message");
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Project_FileAlreadyInProject, "Added.pas"), h.Services.Status.Message);
        Assert.Equal(before, File.ReadAllText(Abs("Hello.pproj")));
    }
}