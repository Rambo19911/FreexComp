using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Project Manager tool window (tool-windows.md §2, project-manager.md §6).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ProjectManagerViewTests
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
    public async Task ContextMenusAndSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
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
}
