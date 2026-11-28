using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Project Manager with a project group (phase8-ui-additions.md §9, project-groups.md; tasks I-274, I-275).</summary>
public sealed partial class ProjectManagerViewTests
{
    /// <summary><c>&lt;name&gt;/&lt;name&gt;.pproj</c> with its program file, one unit and <paramref name="extraFiles"/>.</summary>
    private static void GroupProject(HarnessOptions o, string name, string extraFiles = "")
    {
        o.InitialFiles[$@"{name}\{name}.pproj"] = $$"""
            { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas",
              "files": [ { "path": "{{name}}.pas" }, { "path": "{{name}}Unit.pas" }{{extraFiles}} ] }
            """;
        o.InitialFiles[$@"{name}\{name}.pas"] = $"program {name};\nbegin\nend.\n";
        o.InitialFiles[$@"{name}\{name}Unit.pas"] = $"unit {name}Unit;\ninterface\nimplementation\nend.\n";
    }

    /// <summary>The group of the §9 wireframe: App (active), CoreLib, Tools; <c>Tools.pproj</c> exists only with <paramref name="withTools"/>.</summary>
    private static void AppGroup(HarnessOptions o, bool withTools = false)
    {
        GroupProject(o, "App");
        GroupProject(o, "CoreLib", """, { "path": "lib/Strings2.pas" }""");
        o.InitialFiles[@"CoreLib\lib\Strings2.pas"] = "unit Strings2;\ninterface\nimplementation\nend.\n";
        if (withTools)
            GroupProject(o, "Tools");
        o.InitialFiles["All.pgroup"] =
            """{ "formatVersion": 1, "projects": ["App/App.pproj", "CoreLib/CoreLib.pproj", "Tools/Tools.pproj"], "activeProject": "App/App.pproj" }""";
    }

    private static async Task<ProjectManagerViewModel> OpenGroupAsync(HeadlessHarness h)
    {
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "All.pgroup")));
        var pm = Pm(h);
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();
        return pm;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-125")]
    [Trait("Req", "UI-TW-126")]
    [Trait("Req", "UI-TW-128")]
    public async Task Group_OneRootPerMember_ActiveBold_ListedCollapsed_UnavailableGreyed()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o));
        var pm = await OpenGroupAsync(h);
        var toolsPath = Path.Combine(h.TempDir, "Tools", "Tools.pproj");

        // One root per member in group order; the active root is expanded, the listed ones start collapsed.
        Assert.Equal(["$project", "$project:CoreLib", "$project:Tools"], pm.Roots.Select(r => r.Key));
        Assert.Equal(["$project", "App.pas", "AppUnit.pas", "$platforms/", "$project:CoreLib", "$project:Tools"], VisibleKeys(pm)); // IDE-PLAT-010: the node of the loaded project
        Assert.Equal(VisibleKeys(pm).Select(k => Row + k),
            h.FindAll(Row).OfType<TreeViewItem>().Where(i => i.IsEffectivelyVisible).Select(AutomationIds.Get));

        // The active project: bodyStrong, its badge, ", active project" for screen readers.
        var active = h.Find<TextBlock>(Row + "$project.Name");
        Assert.Equal("App", active.Text);
        Assert.Contains("bodyStrong", active.Classes);
        Assert.Equal("Debug · Win64", h.Find<TextBlock>(Row + "$project.Badge").Text);
        Assert.Equal("App, active project", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "$project")));

        // A listed member: body, its own badge, the project icon, the project file as the tooltip.
        var listed = h.Find<TextBlock>(Row + "$project:CoreLib.Name");
        Assert.Equal("CoreLib", listed.Text);
        Assert.Contains("body", listed.Classes);
        Assert.DoesNotContain("bodyStrong", listed.Classes);
        Assert.Equal(h.Window.FindResource(h.Window.ActualThemeVariant, "FxcTextPrimaryBrush"), listed.Foreground);
        Assert.Equal("Debug · Win64", h.Find<TextBlock>(Row + "$project:CoreLib.Badge").Text);
        Assert.Equal("project", IconOf(h, "$project:CoreLib"));
        Assert.False(h.Find<IdeIcon>(Row + "$project:CoreLib.Icon.Missing").IsVisible);
        Assert.Equal("CoreLib", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "$project:CoreLib")));
        Assert.Equal(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + "$project:CoreLib")));

        // An unavailable member: the file name in text.disabled, the warning overlay, no badge, no expander, the reason.
        var unavailable = pm.Find("$project:Tools")!;
        Assert.True(unavailable.IsUnavailable);
        Assert.Empty(unavailable.Children);
        var greyed = h.Find<TextBlock>(Row + "$project:Tools.Name");
        Assert.Equal("Tools", greyed.Text);
        Assert.DoesNotContain("bodyStrong", greyed.Classes);
        Assert.Equal(h.Window.FindResource(h.Window.ActualThemeVariant, "FxcTextDisabledBrush"), greyed.Foreground);
        Assert.True(h.Find<IdeIcon>(Row + "$project:Tools.Icon.Missing").IsVisible);
        Assert.Equal("warning-corner", h.Find<IdeIcon>(Row + "$project:Tools.Icon.Missing").IconName); // I-287: the corner glyph
        Assert.Null(h.TryFind<TextBlock>(Row + "$project:Tools.Badge"));
        var tip = Assert.IsType<string>(ToolTip.GetTip(h.Find<TreeViewItem>(Row + "$project:Tools")));
        Assert.StartsWith(toolsPath + "\n", tip, StringComparison.Ordinal);
        Assert.Contains("not found", tip, StringComparison.OrdinalIgnoreCase);

        // Rows below a listed root are prefixed "<name>:"; the keys of the active project's rows are unchanged.
        pm.Find("$project:CoreLib")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            ["$project", "App.pas", "AppUnit.pas", "$platforms/", "$project:CoreLib", "CoreLib:lib/", "CoreLib:lib/Strings2.pas", "CoreLib:CoreLib.pas",
             "CoreLib:CoreLibUnit.pas", "$project:Tools"],
            VisibleKeys(pm));
        Assert.Equal("Strings2.pas", h.Find<TextBlock>(Row + "CoreLib:lib/Strings2.pas.Name").Text);
        Assert.True(h.Find<IdeIcon>(Row + "CoreLib:CoreLib.pas.Icon.MainSource").IsVisible);
        Assert.Equal(Path.Combine(h.TempDir, "CoreLib", "lib", "Strings2.pas"), pm.Find("CoreLib:lib/Strings2.pas")!.FullPath);

        // Expansion is remembered while the session lasts: the member file appears, the tree is rebuilt.
        h.WriteFileExternally(@"Tools\Tools.pproj", """{ "formatVersion": 1, "name": "Tools", "mainSource": "Tools.pas" }""");
        h.Services.Groups.Refresh();
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();
        Assert.True(pm.Find("$project:CoreLib")!.IsExpanded);
        Assert.False(pm.Find("$project:Tools")!.IsUnavailable);
        Assert.False(pm.Find("$project:Tools")!.IsExpanded);
        Assert.Equal("Debug · Win64", h.Find<TextBlock>(Row + "$project:Tools.Badge").Text);
        Assert.False(h.Find<IdeIcon>(Row + "$project:Tools.Icon.Missing").IsVisible);

        // A project opened alone looks as before: one bold root, no suffix.
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj")));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["$project"], pm.Roots.Select(r => r.Key));
        Assert.Contains("bodyStrong", h.Find<TextBlock>(Row + "$project.Name").Classes);
        Assert.Equal("CoreLib", AutomationProperties.GetName(h.Find<TreeViewItem>(Row + "$project")));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-160")]
    public async Task Group_DoubleClickOnListedRootActivates()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o));
        var pm = await OpenGroupAsync(h);
        var coreLib = Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj");

        // An unavailable root does nothing.
        h.DoubleClick(Row + "$project:Tools");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("App", h.Services.Groups.ActiveMember?.Name);

        h.DoubleClick(Row + "$project:CoreLib");
        await h.WaitForAsync(() => h.Services.Projects.Project?.FilePath == coreLib && h.Services.Groups.ActiveMember?.Name == "CoreLib",
            "CoreLib activated");
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();

        // The order stays; the roles change: CoreLib is the bold, expanded root now, App a collapsed listed one.
        Assert.Equal(["$project:App", "$project", "$project:Tools"], pm.Roots.Select(r => r.Key));
        Assert.Equal("CoreLib", h.Find<TextBlock>(Row + "$project.Name").Text);
        Assert.Contains("bodyStrong", h.Find<TextBlock>(Row + "$project.Name").Classes);
        Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(Row + "$project:App.Name").Classes);
        Assert.True(pm.Find("$project")!.IsExpanded);
        Assert.False(pm.Find("$project:App")!.IsExpanded);
        Assert.Contains("CoreLib.pas", VisibleKeys(pm));
        Assert.EndsWith("CoreLib.pas", h.Services.Documents.ActiveDocument?.FilePath, StringComparison.Ordinal);
        Assert.Contains("\"activeProject\": \"CoreLib/CoreLib.pproj\"", File.ReadAllText(Path.Combine(h.TempDir, "All.pgroup")), StringComparison.Ordinal);
        Assert.StartsWith("CoreLib", h.Window.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// I-298, UI-TW-125, 126 (project-groups.md §8 state 1): a listed member is collapsed when it becomes available —
    /// also when its root was expanded before its file went away; while unavailable it remembers no expansion.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-125")]
    [Trait("Req", "UI-TW-126")]
    public async Task Group_AMemberThatBecomesAvailableAgainIsCollapsed()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o));
        var pm = await OpenGroupAsync(h);
        var file = Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj");
        pm.Find("$project:CoreLib")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        // The file goes away (renamed outside the IDE): the root is unavailable.
        File.Move(file, file + ".away");
        h.Services.Groups.Refresh();
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();
        Assert.True(pm.Find("$project:CoreLib")!.IsUnavailable);

        // … and comes back: the root is available again and collapsed, its rows not shown.
        File.Move(file + ".away", file);
        h.Services.Groups.Refresh();
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();
        var root = pm.Find("$project:CoreLib")!;
        Assert.False(root.IsUnavailable);
        Assert.NotEmpty(root.Children);
        Assert.False(root.IsExpanded);
        Assert.DoesNotContain("CoreLib:CoreLib.pas", VisibleKeys(pm));
        Assert.False(h.Find<TreeViewItem>(Row + "$project:CoreLib").IsExpanded);
    }

    /// <summary>
    /// I-295, IDE-PM-160: after a listed root is activated by a double-click — here through the unsaved-changes prompt of
    /// the outgoing project — the activated root is the selected and focused row of the Project Manager, not the root of
    /// the outgoing project; so the Project menu's Add items act on the active project and are enabled.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-160")]
    public async Task Group_ActivatedRootIsSelectedAndFocused_AfterTheUnsavedPrompt()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o));
        var pm = await OpenGroupAsync(h);
        var coreLib = Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj");
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "App", "App.pas"))).Success);
        var app = Assert.IsAssignableFrom<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        app.Buffer.ApplyEdits([new FreexComp.Ide.Core.Text.TextEdit(0, 0, "{ changed }")]);
        Dispatcher.UIThread.RunJobs();

        h.Click(Row + "$project:CoreLib");
        Assert.Equal(Row + "$project:CoreLib", h.FocusedAutomationId);
        h.DoubleClick(Row + "$project:CoreLib");
        var prompt = await h.WaitForDialogAsync("Message");
        prompt.Click("DontSave");
        await h.WaitForAsync(() => h.Services.Projects.Project?.FilePath == coreLib && h.Services.Groups.ActiveMember?.Name == "CoreLib",
            "CoreLib activated");
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["$project"], pm.Selection.Select(n => n.Key));
        Assert.Equal("CoreLib", pm.Selection[0].Name);
        Assert.Equal(Row + "$project", h.FocusedAutomationId);
        Assert.False(pm.IsListedSelection);
        Assert.True(h.Services.Commands.CanExecute("project.addNewUnit"));
        Assert.True(h.Services.Commands.CanExecute("project.addExistingFile"));
    }

    private const string InsertionLine = "Tool.ProjectManager.InsertionLine";

    private static string[] GroupProjects(HeadlessHarness h)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(h.TempDir, "All.pgroup")));
        return [.. json.RootElement.GetProperty("projects").EnumerateArray().Select(e => e.GetString()!)];
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-127")]
    [Trait("Req", "IDE-PM-168")]
    public async Task Group_DragRootBetweenRoots_Reorders_FileCannotBeDroppedOnAnotherProject()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            AppGroup(o, withTools: true);
        });
        var pm = await OpenGroupAsync(h);
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        pm.Find("$project:CoreLib")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["$project", "$project:CoreLib", "$project:Tools"], pm.Roots.Select(r => r.Key));

        // A root row is dragged like a file row: beyond 4 DIP the adorner shows the project icon and name.
        var start = RowPoint(h, "$project:CoreLib");
        Press(h, start);
        Move(h, start + new Point(3, 3));
        Assert.Null(h.TryFind<Control>(Adorner));
        Move(h, start + new Point(6, 2));
        var adorner = h.Find<Border>(Adorner);
        Assert.Equal("CoreLib", AutomationProperties.GetName(adorner));
        Assert.Equal("project", Assert.Single(adorner.GetVisualDescendants().OfType<IdeIcon>()).IconName);
        Assert.True(pm.IsRootDrag);
        Assert.Equal(["$project:CoreLib"], pm.DraggedRows.Select(n => n.Key));

        // The targets are the gaps between root rows: a 2 px accent line across the tree at the gap nearest to the pointer.
        Move(h, RowPoint(h, "$project") + new Point(0, -6));
        var line = h.Find<Border>(InsertionLine);
        Assert.Equal("before App", AutomationProperties.GetName(line));
        Assert.Equal(2, line.Bounds.Height);
        Assert.Equal(tree.Bounds.Width, line.Bounds.Width);
        Assert.Equal(Color.Parse("#0F7B6C"), Assert.IsAssignableFrom<ISolidColorBrush>(line.Background).Color); // accent
        Assert.Equal(new Cursor(StandardCursorType.DragMove).ToString(), tree.Cursor!.ToString());
        var appTop = h.Find<TreeViewItem>(Row + "$project").TranslatePoint(default, h.Window)!.Value.Y;
        Assert.InRange(line.TranslatePoint(default, h.Window)!.Value.Y, appTop - 1, appTop + 1);

        // Rows of files are no target while a root is dragged: over a file row the line stays at the nearest gap and no
        // row is highlighted.
        Move(h, RowPoint(h, "CoreLib:lib/"));
        Assert.Empty(DropTargets(h));
        Assert.Null(pm.DropTarget);
        Assert.Equal("before CoreLib", AutomationProperties.GetName(h.Find<Border>(InsertionLine)));
        Move(h, RowPoint(h, "CoreLib:CoreLibUnit.pas") + new Point(0, 8));
        Assert.Equal("before Tools", AutomationProperties.GetName(h.Find<Border>(InsertionLine)));
        Move(h, RowPoint(h, "$project:Tools") + new Point(0, 9));
        Assert.Equal("after Tools", AutomationProperties.GetName(h.Find<Border>(InsertionLine)));
        Assert.Single(h.FindAll(InsertionLine));

        // Dropped at its own position (the gap above or below it): nothing happens.
        Move(h, RowPoint(h, "CoreLib:lib/"));
        Assert.Equal("before CoreLib", AutomationProperties.GetName(h.Find<Border>(InsertionLine)));
        Release(h, RowPoint(h, "CoreLib:lib/"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(InsertionLine));
        Assert.Null(h.TryFind<Control>(Adorner));
        Assert.Null(tree.Cursor);
        Assert.False(pm.IsRootDrag);
        Assert.Equal(["App/App.pproj", "CoreLib/CoreLib.pproj", "Tools/Tools.pproj"], GroupProjects(h));

        // Dropped before the first root: the order of the group changes, in the tree and in the file.
        start = RowPoint(h, "$project:Tools");
        Press(h, start);
        Move(h, start + new Point(6, 2));
        var target = RowPoint(h, "$project") + new Point(0, -6);
        Move(h, target);
        Release(h, target);
        await h.WaitForAsync(() => pm.Roots[0].Key == "$project:Tools", "Tools moved to the front");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["$project:Tools", "$project", "$project:CoreLib"], pm.Roots.Select(r => r.Key));
        Assert.Equal(["Tools/Tools.pproj", "App/App.pproj", "CoreLib/CoreLib.pproj"], GroupProjects(h));
        Assert.Equal("App", h.Services.Groups.ActiveMember?.Name);
        Assert.True(pm.Find("$project:CoreLib")!.IsExpanded); // expansion is kept
        Assert.Null(h.TryFind<Control>(InsertionLine));

        // Escape cancels the drag of a root.
        start = RowPoint(h, "$project");
        Press(h, start);
        Move(h, start + new Point(6, 2));
        Move(h, RowPoint(h, "$project:Tools") + new Point(0, -6));
        Assert.NotNull(h.TryFind<Control>(InsertionLine));
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(InsertionLine));
        Release(h, RowPoint(h, "$project:Tools"));
        Assert.Equal(["$project:Tools", "$project", "$project:CoreLib"], pm.Roots.Select(r => r.Key));

        // A file row dragged over a root, folder or file of another project: the not-allowed cursor and no drop highlight;
        // released there, nothing moves.
        start = RowPoint(h, "AppUnit.pas");
        Press(h, start);
        Move(h, start + new Point(6, 2));
        Assert.False(pm.IsRootDrag);
        Assert.Null(h.TryFind<Control>(InsertionLine));
        foreach (var key in new[] { "$project:Tools", "$project:CoreLib", "CoreLib:lib/", "CoreLib:lib/Strings2.pas" })
        {
            Move(h, RowPoint(h, key));
            Assert.Empty(DropTargets(h));
            Assert.Equal(new Cursor(StandardCursorType.No).ToString(), tree.Cursor!.ToString());
        }

        Move(h, RowPoint(h, "$project")); // its own project: the root is a target (the control)
        Assert.Equal(["$project"], DropTargets(h));
        Release(h, RowPoint(h, "CoreLib:lib/"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(Path.Combine(h.TempDir, "App", "AppUnit.pas")));
        Assert.False(File.Exists(Path.Combine(h.TempDir, "CoreLib", "lib", "AppUnit.pas")));
        Assert.NotNull(pm.Find("AppUnit.pas"));

        // A project opened alone has nothing to reorder: its root is not dragged.
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj")));
        Assert.False(pm.CanStartDrag(pm.Roots[0]));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-129")]
    public async Task Group_ContextMenusOfActiveListedAndUnavailableRoots()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o));
        var pm = await OpenGroupAsync(h);
        static string[] Ids(IReadOnlyList<Core.Menus.MenuItemModel> menu) => [.. menu.Select(m => m.Command?.Id ?? "-")];
        pm.Find("$project:CoreLib")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        // The active root: the existing items with project.dependencies after run.startWithoutDebugging (own group) and
        // the group commands before file.closeProject. Its Build, Rebuild and Clean are the commands as before (no target).
        pm.Select(pm.Find("$project")!);
        Assert.All(pm.ContextMenu().Where(m => m.Command is not null), m => Assert.Null(m.Argument));
        Assert.Equal(["project.build", "project.rebuild", "project.clean", "-", "run.start", "run.startWithoutDebugging", "-",
            "project.dependencies", "-",
            "project.addNewUnit", "project.addNewForm", "project.addNewFrame", "project.addNewInheritedForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", "-",
            "project.viewSource", "file.showInExplorer", "file.openInTerminal", "project.openOutputFolder", "file.copyPath", "-",
            "project.addExistingProject", "project.addNewProject", "project.removeFromGroup", "project.moveProjectUp", "project.moveProjectDown", "-",
            "file.closeProject", "-", "project.options"], Ids(pm.ContextMenu()));

        // A listed root: Build, Rebuild and Clean act on that member (IDE-PM-170: they carry its project file), then Run
        // Without Debugging of that member in a group of its own (IDE-PM-190), then Dependencies.
        pm.Select(pm.Find("$project:CoreLib")!);
        Assert.Equal(["project.setActive", "-", "project.build", "project.rebuild", "project.clean", "-", "run.startWithoutDebugging", "-",
            "project.dependencies", "-",
            "project.removeFromGroup", "project.moveProjectUp", "project.moveProjectDown", "-",
            "file.showInExplorer", "file.openInTerminal", "file.copyPath"], Ids(pm.ContextMenu()));
        var coreLib = Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj");
        Assert.Equal([null, coreLib, coreLib, coreLib, coreLib, null, null, null, null, null, null, null],
            pm.ContextMenu().Where(m => m.Command is not null).Select(m => m.Argument));

        // An unavailable root: it can only be removed (and its place shown).
        pm.Select(pm.Find("$project:Tools")!);
        Assert.Equal(["project.removeFromGroup", "-", "file.showInExplorer", "file.copyPath"], Ids(pm.ContextMenu()));

        // A file of a listed member: open and the path commands; no file-management command.
        pm.Select(pm.Find("CoreLib:CoreLibUnit.pas")!);
        Assert.Equal(["projectManager.open", "-", "file.showInExplorer", "file.openInTerminal", "file.copyPath"], Ids(pm.ContextMenu()));

        // The empty area: the existing items, then the two commands that add a project.
        pm.ClearSelection();
        Assert.Equal(["project.addNewUnit", "project.addNewForm", "project.addExistingFile", "-", "projectManager.collapseAll", "-",
            "project.addExistingProject", "project.addNewProject"], Ids(pm.ContextMenu()));

        // The menu of a right-clicked listed root is the one shown, with its labels; its items act on that root.
        h.Press("Alt+1");
        Dispatcher.UIThread.RunJobs();
        h.RightClick(Row + "$project:CoreLib.Name");
        Assert.Equal(["$project:CoreLib"], pm.Selection.Select(n => n.Key));
        var menu = h.Find<TreeViewItem>(Row + "$project:CoreLib").ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
        Assert.NotNull(menu);
        var items = menu!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Context.project.setActive", "Context.project.build", "Context.project.rebuild", "Context.project.clean",
            "Context.run.startWithoutDebugging",
            "Context.project.dependencies", "Context.project.removeFromGroup", "Context.project.moveProjectUp", "Context.project.moveProjectDown",
            "Context.file.showInExplorer", "Context.file.openInTerminal", "Context.file.copyPath"], items.Select(AutomationIds.Get));
        Assert.Equal("Set as _Active Project", items[0].Header);
        Assert.Equal(["Run _Without Debugging", "De_pendencies…", "_Remove from Group", "Move Project _Up", "Move Project _Down"],
            items.Skip(4).Take(5).Select(i => i.Header));
        Assert.Equal(Enumerable.Repeat(true, 9), items.Take(9).Select(i => i.IsEffectivelyEnabled)); // CoreLib is in the middle
        // The three build items and the run item keep the ids of their commands and carry the row's project file as the command parameter.
        Assert.Equal([null, coreLib, coreLib, coreLib, coreLib, null], items.Take(6).Select(i => i.CommandParameter));
        menu.Close();

        // Enabled states follow the row: the unavailable last root cannot move down; the first root cannot move up.
        Assert.Equal(ProjectManagerViewModel.ToolName, h.Services.FocusTracker.Current.ToolWindowName);
        pm.Select(pm.Find("$project:Tools")!);
        Assert.True(h.Services.Commands.CanExecute("project.removeFromGroup"));
        Assert.True(h.Services.Commands.CanExecute("project.moveProjectUp"));
        Assert.False(h.Services.Commands.CanExecute("project.moveProjectDown"));
        Assert.False(h.Services.Commands.CanExecute("project.setActive"));
        pm.Select(pm.Find("$project")!);
        Assert.False(h.Services.Commands.CanExecute("project.moveProjectUp"));

        // Remove from Group on the unavailable root: the row is gone, the file of the group has two projects.
        pm.Select(pm.Find("$project:Tools")!);
        await h.ExecuteAsync("project.removeFromGroup");
        Assert.Equal(["$project", "$project:CoreLib"], pm.Roots.Select(r => r.Key));
        Assert.Equal(["App/App.pproj", "CoreLib/CoreLib.pproj"], GroupProjects(h));

        // A group without projects: the empty state says so and offers the two commands that add one (Msg.Group.Empty).
        h.WriteFileExternally("Empty.pgroup", """{ "formatVersion": 1, "projects": [] }""");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Empty.pgroup")));
        Dispatcher.UIThread.RunJobs();
        var empty = h.Find<Control>("Tool.ProjectManager.Empty");
        Assert.True(empty.IsVisible);
        Assert.Contains("This project group has no projects.", empty.GetLogicalDescendantsTexts());
        Assert.DoesNotContain("No project is open", empty.GetLogicalDescendantsTexts());
        Assert.True(h.Find<Button>("Tool.ProjectManager.Empty.Link.project.addExistingProject").IsEffectivelyEnabled);
        Assert.True(h.Find<Button>("Tool.ProjectManager.Empty.Link.project.addNewProject").IsEffectivelyEnabled);
        Assert.Null(h.TryFind<Button>("Tool.ProjectManager.Empty.Link.file.openProject"));
        await h.ExecuteAsync("file.closeProject");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("No project is open", empty.GetLogicalDescendantsTexts());
        Assert.NotNull(h.TryFind<Button>("Tool.ProjectManager.Empty.Link.file.openProject"));
    }

    /// <summary>
    /// Card F09-056, acceptance 1, as a UI test (group-member-run.md IDE-PM-190…192, 196). The menu of a right-clicked
    /// listed root has Run Without Debugging; an item that acts on the row shows no shortcut text (the key belongs to the
    /// command of the active project) but keeps its icon; a click builds that member and starts its program while the
    /// active project stays the active one; while the program runs the item is disabled.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-190")]
    [Trait("Req", "IDE-PM-191")]
    [Trait("Req", "IDE-PM-192")]
    [Trait("Req", "IDE-PM-196")]
    public async Task Group_ListedRootMenu_RunWithoutDebuggingStartsThatMember()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Root = root;
            foreach (var tool in (string[])[@"tc\llvm\clang.exe", @"tc\llvm\lld-link.exe", @"tc\llvm\llvm-dlltool.exe", @"tc\rtl\System.pas"])
                o.InitialFiles[tool] = "";
            o.Settings["toolchain.llvmBinDir"] = Path.Combine(root, "work", @"tc\llvm");
            o.Settings["toolchain.rtlDir"] = Path.Combine(root, "work", @"tc\rtl");
            AppGroup(o, withTools: true);
        });
        var pm = await OpenGroupAsync(h);
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        var toolsProject = Path.Combine(h.TempDir, "Tools", "Tools.pproj");
        var toolsExe = Path.Combine(h.TempDir, "Tools", "Tools.exe");
        var title = h.Window.Title;

        // The open context menu of the row: its command items by automation id.
        Dictionary<string, MenuItem> OpenMenu(string rowKey)
        {
            h.RightClick(Row + rowKey + ".Name");
            Dispatcher.UIThread.RunJobs();
            var menu = h.Find<TreeViewItem>(Row + rowKey).ContextMenu ?? tree.ContextMenu;
            Assert.True(menu is { IsOpen: true });
            return menu!.Items.OfType<MenuItem>().ToDictionary(i => AutomationIds.Get(i)!);
        }

        void CloseMenu()
        {
            (h.Find<TreeViewItem>(Row + "$project:Tools").ContextMenu ?? tree.ContextMenu)!.Close();
            Dispatcher.UIThread.RunJobs();
        }

        // The shortcut text the template of the item shows.
        static string ShownGesture(MenuItem item) =>
            item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_InputGestureText").Text ?? "";

        h.Press("Alt+1");
        Dispatcher.UIThread.RunJobs();

        // The control: the items of the active root are the commands themselves and show their keys.
        var active = OpenMenu("$project");
        Assert.Equal(("Ctrl+Shift+B", "Ctrl+F5"), (ShownGesture(active["Context.project.build"]), ShownGesture(active["Context.run.startWithoutDebugging"])));
        Assert.Null(active["Context.run.startWithoutDebugging"].CommandParameter);
        CloseMenu();

        // The listed root: the run item after the build items; target items show no key (IDE-PM-191) and keep their icon.
        var listed = OpenMenu("$project:Tools");
        Assert.Equal(["$project:Tools"], pm.Selection.Select(n => n.Key));
        string[] targetItems = ["Context.project.build", "Context.project.rebuild", "Context.project.clean", "Context.run.startWithoutDebugging"];
        Assert.Equal(["Context.project.setActive", .. targetItems, "Context.project.dependencies"], listed.Keys.Take(6));
        Assert.DoesNotContain("Context.run.start", listed.Keys); // the debugger half of the card: task I-361
        Assert.All(targetItems, id =>
        {
            Assert.Equal("", ShownGesture(listed[id]));
            Assert.Equal(toolsProject, listed[id].CommandParameter);
            Assert.True(listed[id].IsEffectivelyEnabled, id);
        });
        var runItem = listed["Context.run.startWithoutDebugging"];
        Assert.Equal("Run _Without Debugging", runItem.Header);
        Assert.Equal("run", Assert.IsType<IdeIcon>(runItem.Icon).IconName);
        Assert.Equal("build", Assert.IsType<IdeIcon>(listed["Context.project.build"].Icon).IconName);

        // A click builds Tools and starts its program; App stays the active project.
        runItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        CloseMenu();
        await h.WaitForAsync(() => h.Processes.Started.Count == 1 && h.Services.State.Current.State == Core.Models.IdeState.Running, "the program of the member started");
        Assert.Equal(toolsProject, Assert.Single(h.Build.Requests).ProjectPath);
        Assert.Equal(new Core.Platform.ProcessStartRequest(toolsExe, "", Path.GetDirectoryName(toolsExe)!), h.Processes.Last.Request);
        Assert.Equal(toolsExe, h.Services.State.Current.ProgramPath);
        Assert.Equal(Path.Combine(h.TempDir, "App", "App.pproj"), h.Services.Projects.Project!.FilePath);
        Assert.Contains("bodyStrong", h.Find<TextBlock>(Row + "$project.Name").Classes);
        Assert.Equal("App", h.Find<TextBlock>(Row + "$project.Name").Text);
        Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(Row + "$project:Tools.Name").Classes);
        Assert.Equal("App – FreexComp", title);
        Assert.Equal("App (Running) – FreexComp", h.Window.Title); // the title names the active project and the state, as for any run

        // While the program runs: the item is there, disabled (one program at a time, IDE-PM-192).
        var running = OpenMenu("$project:Tools");
        Assert.False(running["Context.run.startWithoutDebugging"].IsEffectivelyEnabled);
        Assert.False(running["Context.project.build"].IsEffectivelyEnabled);
        CloseMenu();
        Assert.Single(h.Processes.Started);

        h.Processes.Last.Exit(0);
        await h.WaitForAsync(() => h.Services.State.Current.State == Core.Models.IdeState.Idle, "the program ended");
        Assert.Equal(title, h.Window.Title);
        Assert.True(OpenMenu("$project:Tools")["Context.run.startWithoutDebugging"].IsEffectivelyEnabled);
        CloseMenu();
    }

    /// <summary>
    /// I-281: the context menu belongs to the right-clicked row whatever had the keyboard focus before (UI-TW-014,
    /// UI-TW-129). A fresh window with the focus in the editor: the group commands of a listed root, of the active root
    /// and of the empty area are enabled per the row, and the commands of a file row act on that row, not on the
    /// active document.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-014")]
    [Trait("Req", "UI-TW-129")]
    public async Task Group_ContextMenuOfListedRoot_IsEnabledWithoutPriorFocus()
    {
        await using var h = await HeadlessHarness.StartAsync(o => AppGroup(o, withTools: true));
        var pm = await OpenGroupAsync(h);
        var clipboard = (FreexComp.Ide.TestSupport.FakeClipboard)h.Services.Clipboard;
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        var appMain = Path.Combine(h.TempDir, "App", "App.pas");

        // The focus is in the editor (the main source of the active project); the Project Manager never had it.
        async Task FocusEditorAsync()
        {
            h.Click("Editor.App.pas");
            await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.App.pas", "editor focused");
            Assert.Equal(Core.Commands.CommandScope.Editor, h.Services.FocusTracker.Current.Scope);
            Assert.Null(h.Services.FocusTracker.Current.ToolWindowName);
        }

        // The open context menu of the tree: its command items by automation id.
        Dictionary<string, MenuItem> OpenMenu()
        {
            var menu = tree.ContextMenu!;
            Assert.True(menu.IsOpen);
            return menu.Items.OfType<MenuItem>().ToDictionary(i => AutomationIds.Get(i)!);
        }

        void CloseMenu()
        {
            tree.ContextMenu!.Close();
            Dispatcher.UIThread.RunJobs();
        }

        static void Click(MenuItem item) => item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(await h.Services.Documents.OpenAsync(appMain) is { Success: true });
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.App.pas", "editor focused");
        Assert.Equal(Core.Commands.CommandScope.Editor, h.Services.FocusTracker.Current.Scope);
        Assert.Empty(pm.Selection);

        // A listed root in the middle of a saved group, Idle: everything is enabled.
        h.RightClick(Row + "$project:CoreLib.Name");
        Assert.Equal(["$project:CoreLib"], pm.Selection.Select(n => n.Key));
        var items = OpenMenu();
        string[] group = ["Context.project.setActive", "Context.project.removeFromGroup", "Context.project.moveProjectUp", "Context.project.moveProjectDown"];
        Assert.Equal([true, true, true, true], group.Select(id => items[id].IsEffectivelyEnabled));
        Assert.All(items.Values, i => Assert.True(i.IsEffectivelyEnabled, AutomationIds.Get(i)));
        // The path commands act on the row, not on the active document.
        Click(items["Context.file.copyPath"]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Path.Combine(h.TempDir, "CoreLib", "CoreLib.pproj"), clipboard.Text);
        CloseMenu();

        // The last listed root cannot move down.
        await FocusEditorAsync();
        h.RightClick(Row + "$project:Tools.Name");
        items = OpenMenu();
        Assert.Equal([true, true, true, false], group.Select(id => items[id].IsEffectivelyEnabled));
        CloseMenu();

        // The active root (the first): it can be removed and moved down; the two commands that add a project.
        await FocusEditorAsync();
        h.RightClick(Row + "$project.Name");
        Assert.Equal(["$project"], pm.Selection.Select(n => n.Key));
        items = OpenMenu();
        Assert.Equal([true, false, true], group.Skip(1).Select(id => items[id].IsEffectivelyEnabled));
        Assert.True(items["Context.project.addExistingProject"].IsEffectivelyEnabled);
        Assert.True(items["Context.project.addNewProject"].IsEffectivelyEnabled);
        Click(items["Context.file.copyPath"]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Path.Combine(h.TempDir, "App", "App.pproj"), clipboard.Text);
        CloseMenu();

        // A file row: its commands act on the row (the active document is the main source, which cannot be removed).
        await FocusEditorAsync();
        h.RightClick(Row + "AppUnit.pas.Name");
        Assert.Equal(["AppUnit.pas"], pm.Selection.Select(n => n.Key));
        items = OpenMenu();
        Assert.True(items["Context.project.renameFile"].IsEffectivelyEnabled);
        Assert.True(items["Context.project.removeFile"].IsEffectivelyEnabled);
        Assert.True(items["Context.project.deleteFile"].IsEffectivelyEnabled);
        Click(items["Context.file.copyPath"]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Path.Combine(h.TempDir, "App", "AppUnit.pas"), clipboard.Text);
        CloseMenu();

        // The empty area below the rows: the selection is cleared; the two commands that add a project are enabled.
        await FocusEditorAsync();
        var lastRow = h.Find<TreeViewItem>(Row + "$project:Tools");
        var below = lastRow.TranslatePoint(new Point(lastRow.Bounds.Width / 2, lastRow.Bounds.Height + 11), h.Window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(h.Window.InputHitTest(below));
        Assert.Contains(tree, hit.GetSelfAndVisualAncestors());
        Assert.Null(hit.FindAncestorOfType<TreeViewItem>(includeSelf: true));
        h.Window.MouseDown(below, MouseButton.Right);
        h.Window.MouseUp(below, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(pm.Selection);
        items = OpenMenu();
        Assert.True(items["Context.project.addExistingProject"].IsEffectivelyEnabled);
        Assert.True(items["Context.project.addNewProject"].IsEffectivelyEnabled);
        Assert.True(items["Context.project.addNewUnit"].IsEffectivelyEnabled);
        CloseMenu();

        // A left click on a row, then the right click on another one.
        await FocusEditorAsync();
        h.Click(Row + "$project:Tools.Name");
        h.RightClick(Row + "$project:CoreLib.Name");
        items = OpenMenu();
        Assert.Equal([true, true, true, true], group.Select(id => items[id].IsEffectivelyEnabled));
        CloseMenu();
    }
}
