using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using BuildKind = FreexComp.Ide.Core.Build.BuildKind;
using IdeState = FreexComp.Ide.Core.Models.IdeState;
using IdeStateInfo = FreexComp.Ide.Core.Build.IdeStateInfo;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// The Project Manager node <c>Target Platforms</c> (platform-selector.md §3, IDE-PLAT-010…015; UI-TW-131…135; task
/// I-279): its place and rows, activation, <c>project.addPlatform</c> and <c>project.removePlatform</c>.
/// </summary>
[Trait("Phase", "8")]
public sealed class TargetPlatformsNodeTests
{
    private const string Second = TestPlatformCatalog.SecondId;
    private const string NodeKey = ProjectManagerViewModel.PlatformsKey;

    private static BuildTestBed Bed() => new(o => o.PlatformCatalog = new TestPlatformCatalog());

    private static ProjectManagerViewModel Pm(BuildTestBed bed) => bed.Services.ProjectManager;

    private static ProjectNodeViewModel Node(BuildTestBed bed) => Pm(bed).Find(NodeKey)!;

    private static ProjectNodeViewModel PlatformRow(BuildTestBed bed, string id) => Pm(bed).Find(NodeKey + id)!;

    private static string ProjectFile(BuildTestBed bed) => System.Text.Encoding.UTF8.GetString(bed.Fs.ReadAllBytes(BuildTestBed.ProjectPath));

    private static (string? Command, string Label, bool Enabled, string? Argument, string? Tip)[] Menu(BuildTestBed bed) =>
        [.. Pm(bed).ContextMenu().Select(i => (i.CommandId, i.Label, i.IsEnabled, i.Argument, i.ToolTip))];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-010")]
    [Trait("Req", "IDE-PLAT-011")]
    [Trait("Req", "IDE-PLAT-008")]
    [Trait("Req", "UI-TW-132")]
    [Trait("Req", "UI-TW-133")]
    public Task NodeIsLastChild_RowsLabelsActiveBold_NotInstalledGreyed() => TestUiThread.RunAsync(NodeOnUiThread);

    private static async Task NodeOnUiThread()
    {
        // One platform: the node is there all the same, after the files, collapsed.
        var bed = Bed();
        bed.File(@"C:\elsewhere\Shared.pas", "unit Shared;\ninterface\nimplementation\nend.\n");
        await bed.OpenHelloAsync();
        var root = Pm(bed).Roots[0];
        Assert.Equal(["Hello.pas", "Unit1.pas", NodeKey], root.Children.Select(c => c.Key));
        var node = Node(bed);
        Assert.Equal((ProjectNodeKind.Platforms, "Target Platforms", "platforms", false), (node.Kind, node.Name, node.IconName, node.IsExpanded));
        Assert.Null(node.ToolTipText);
        Assert.Null(node.Badge);
        var only = Assert.Single(node.Children);
        Assert.Equal((NodeKey + "win64", ProjectNodeKind.Platform, "Windows 64-bit (Win64)", "platform", "win64"), (only.Key, only.Kind, only.Name, only.IconName, only.PlatformId));
        Assert.True(only.IsActivePlatform);
        Assert.False(only.IsNotInstalled || only.IsModified || only.IsFile);
        Assert.Equal("Windows 64-bit (Win64), active platform", only.AutomationName);
        Assert.Null(only.ToolTipText);
        Assert.True(await bed.Services.Projects.CloseAsync());

        // Three listed platforms — the second active, the third not installed — and a file outside the project:
        // the node comes after External; rows in file order.
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "test32", "win32"], "activePlatform": "test32"
            """);
        Assert.Equal(1, await bed.Services.Projects.AddExistingFilesAsync([@"C:\elsewhere\Shared.pas"]));
        root = Pm(bed).Roots[0];
        Assert.Equal(ProjectManagerViewModel.ExternalKey, root.Children[^2].Key);
        Assert.Equal(NodeKey, root.Children[^1].Key);
        node = Node(bed);
        Assert.Equal([NodeKey + "win64", NodeKey + Second, NodeKey + "win32"], node.Children.Select(c => c.Key));
        Assert.Equal(["Windows 64-bit (Win64)", "Test 32-bit (Test32)", "Win32 (not installed)"], node.Children.Select(c => c.Name));
        Assert.Equal([false, true, false], node.Children.Select(c => c.IsActivePlatform));
        Assert.Equal([false, false, true], node.Children.Select(c => c.IsNotInstalled));
        Assert.Equal([null, null, Strings.Msg_Platform_NotInstalled], node.Children.Select(c => c.ToolTipText));
        Assert.Equal("This platform is not installed. The project cannot be built for it here.", Strings.Msg_Platform_NotInstalled);
        Assert.Equal("Test 32-bit (Test32), active platform", PlatformRow(bed, Second).AutomationName);
        Assert.Equal("Win32 (not installed)", PlatformRow(bed, "win32").AutomationName);

        // Expansion is remembered across a rebuild of the tree, like a folder's.
        node.IsExpanded = true;
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        Assert.NotSame(node, Node(bed));
        Assert.True(Node(bed).IsExpanded);
        Assert.Contains(NodeKey, Pm(bed).ExpandedKeys());

        // The node and its rows are single-selection rows: they never join a multi-selection (UI-TW-109).
        var file = Pm(bed).Find("Unit1.pas")!;
        Pm(bed).SetSelection([file, PlatformRow(bed, "win64")]);
        Assert.Equal([NodeKey + "win64"], Pm(bed).Selection.Select(n => n.Key));
        Pm(bed).Select(file);
        Pm(bed).Select(Node(bed), ProjectSelectMode.Toggle);
        Assert.Equal([NodeKey], Pm(bed).Selection.Select(n => n.Key));
        Assert.Empty(Pm(bed).SelectedNodePaths);
        Assert.Null(Pm(bed).SelectedNodeDirectory);
        Pm(bed).SelectAllVisible();
        Assert.DoesNotContain(Pm(bed).Selection, n => n.IsPlatformRow);

        // A project group: the node belongs to the loaded project only; a listed member shows its own active platform in
        // its badge (IDE-PLAT-008), and project.setPlatform changes the active project alone.
        var group = Bed();
        foreach (var (name, platforms) in ((string, string)[])[("App", "\"platforms\": [\"win64\", \"test32\"]"), ("Lib", "\"platforms\": [\"win64\", \"test32\"], \"activePlatform\": \"test32\"")])
        {
            group.File($@"C:\g\{name}\{name}.pproj", $$"""
                { "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas", {{platforms}},
                  "files": [ { "path": "{{name}}.pas" } ] }
                """);
            group.File($@"C:\g\{name}\{name}.pas", $"program {name};\nbegin\nend.\n");
        }

        group.File(@"C:\g\All.pgroup", """{ "formatVersion": 1, "projects": ["App/App.pproj", "Lib/Lib.pproj"], "activeProject": "App/App.pproj" }""");
        Assert.True(await group.Services.Projects.OpenAsync(@"C:\g\All.pgroup"));
        await Pm(group).MissingFileChecks;
        Assert.Equal(["$project", "$project:Lib"], Pm(group).Roots.Select(r => r.Key));
        Assert.Equal(["Debug · Win64", "Debug · Test32"], Pm(group).Roots.Select(r => r.Badge));
        Assert.Equal(NodeKey, Pm(group).Roots[0].Children[^1].Key);
        Assert.DoesNotContain(Pm(group).Roots[1].SelfAndDescendants(), n => n.IsPlatformRow);
        Assert.Single(Pm(group).Roots.SelectMany(r => r.SelfAndDescendants()), n => n.Kind == ProjectNodeKind.Platforms);
        var listed = System.Text.Encoding.UTF8.GetString(group.Fs.ReadAllBytes(@"C:\g\Lib\Lib.pproj"));
        await group.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        Assert.Equal(["Debug · Test32", "Debug · Test32"], Pm(group).Roots.Select(r => r.Badge));
        await group.Services.Commands.ExecuteAsync("project.setPlatform", "win64");
        Assert.Equal(["Debug · Win64", "Debug · Test32"], Pm(group).Roots.Select(r => r.Badge));
        Assert.Equal(listed, System.Text.Encoding.UTF8.GetString(group.Fs.ReadAllBytes(@"C:\g\Lib\Lib.pproj")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-012")]
    [Trait("Req", "UI-TW-134")]
    public Task ActivateByDefaultActionAndContextMenu() => TestUiThread.RunAsync(ActivateOnUiThread);

    private static async Task ActivateOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "test32"]
            """);
        var pm = Pm(bed);

        // Enter / double-click on a platform row: project.setPlatform:<id>.
        pm.Select(PlatformRow(bed, Second));
        await pm.OpenSelectionAsync();
        Assert.Equal(Second, bed.Services.Projects.Project!.ActivePlatform);
        Assert.True(PlatformRow(bed, Second).IsActivePlatform);
        Assert.False(PlatformRow(bed, "win64").IsActivePlatform);
        Assert.Equal([ProjectManagerViewModel.PlatformsKey + Second], pm.Selection.Select(n => n.Key)); // the selection survives the rebuild

        // The row's context menu: Set as Active Platform (disabled on the active row), —, Add Platform…, Remove Platform.
        Assert.Equal(
        [
            ("project.setPlatform", "Set as &Active Platform", false, Second, null),
            (null, "", false, null, null),
            ("project.addPlatform", "A&dd Platform…", false, null, Strings.Msg_Platform_NothingToAdd),
            ("project.removePlatform", "&Remove Platform", true, Second, null),
        ], Menu(bed));
        pm.Select(PlatformRow(bed, "win64"));
        var set = pm.ContextMenu()[0];
        Assert.Equal(("project.setPlatform", true, "win64", true), (set.CommandId, set.IsEnabled, set.Argument, set.ArgumentIsTarget));
        await bed.Services.Commands.ExecuteAsync(set.CommandId!, set.Argument);
        Assert.Equal("win64", bed.Services.Projects.Project!.ActivePlatform);
        Assert.True(PlatformRow(bed, "win64").IsActivePlatform);
        Assert.False(pm.ContextMenu()[0].IsEnabled); // now the active row

        // The node's own menu and default action: Add Platform…; Enter expands and collapses it.
        pm.Select(Node(bed));
        Assert.Equal(["project.addPlatform"], pm.ContextMenu().Select(i => i.CommandId));
        Assert.False(Node(bed).IsExpanded);
        await pm.OpenSelectionAsync();
        Assert.True(Node(bed).IsExpanded);
        await pm.OpenSelectionAsync();
        Assert.False(Node(bed).IsExpanded);

        // Not Idle: the row's commands are disabled.
        pm.Select(PlatformRow(bed, Second));
        Assert.True(bed.Services.State.TryTransition(new IdeStateInfo(IdeState.Building, BuildKind.Build, false, null, null)));
        Assert.All(pm.ContextMenu().Where(i => !i.IsSeparator), i => Assert.False(i.IsEnabled, i.CommandId));
        await pm.OpenSelectionAsync();
        Assert.Equal("win64", bed.Services.Projects.Project!.ActivePlatform);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-013")]
    [Trait("Req", "UI-TW-135")]
    public Task AddPlatform_OffersInstalledUnlistedOnly_AppendsWithoutActivating_DisabledWhenNone() => TestUiThread.RunAsync(AddOnUiThread);

    private static async Task AddOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win32", "win64"]
            """);
        var pm = Pm(bed);
        var commands = bed.Services.Commands;
        QuickPickViewModel? shown = null;
        pm.ShowQuickPick = popup => shown = popup;
        Assert.Equal("win32", bed.Services.Projects.Project!.ActivePlatform); // the first listed one; not installed

        // The quick pick: the installed platforms that are not listed — here the second test platform only.
        pm.Select(Node(bed));
        Assert.Equal([("project.addPlatform", "A&dd Platform…", true, null, null)], Menu(bed));
        Assert.True(commands.CanExecute("project.addPlatform"));
        await commands.ExecuteAsync("project.addPlatform");
        var popup = Assert.IsType<MoveToFolderViewModel>(shown);
        Assert.Equal(("Add Platform", "Add Platform"), (popup.Title, popup.AccessibleName));
        Assert.Equal([(Second, "Test 32-bit (Test32)")], popup.Items.Select(i => (i.Id, i.Text)));

        // Enter: appended to platforms; the active platform does not change; nothing else in the file changes.
        popup.SelectedIndex = 0;
        await popup.CommitAsync();
        var project = bed.Services.Projects.Project!;
        Assert.Equal(["win32", "win64", Second], project.Platforms);
        Assert.Equal("win32", project.ActivePlatform);
        Assert.Null(bed.Services.Projects.Definition!.ActivePlatform);
        Assert.False(project.IsModified);
        Assert.Contains("\"test32\"", ProjectFile(bed), StringComparison.Ordinal);
        Assert.DoesNotContain("activePlatform", ProjectFile(bed), StringComparison.Ordinal);
        Assert.Equal(["Win32 (not installed)", "Windows 64-bit (Win64)", "Test 32-bit (Test32)"], Node(bed).Children.Select(c => c.Name));
        Assert.Equal([true, false, false], Node(bed).Children.Select(c => c.IsActivePlatform));

        // Nothing left to add: the command is disabled and its item says why.
        shown = null;
        Assert.Empty(pm.AddablePlatforms());
        Assert.False(commands.CanExecute("project.addPlatform"));
        await commands.ExecuteAsync("project.addPlatform");
        Assert.Null(shown);
        pm.Select(Node(bed));
        Assert.Equal([("project.addPlatform", "A&dd Platform…", false, null, "Every installed platform is already in the project.")], Menu(bed));

        // A platform that is not installed cannot be added from the IDE; a listed one is not added twice.
        Assert.False(await bed.Services.Projects.AddPlatformAsync("arm64"));
        Assert.False(await bed.Services.Projects.AddPlatformAsync("win64"));
        Assert.Equal(["win32", "win64", Second], bed.Services.Projects.Project!.Platforms);

        // Without a project, and while not Idle, the command is disabled.
        Assert.True(await bed.Services.Projects.RemovePlatformAsync(Second));
        Assert.True(commands.CanExecute("project.addPlatform"));
        Assert.True(bed.Services.State.TryTransition(new IdeStateInfo(IdeState.Building, BuildKind.Build, false, null, null)));
        Assert.False(commands.CanExecute("project.addPlatform"));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.False(commands.CanExecute("project.addPlatform"));
    }

    /// <summary>
    /// I-298, IDE-PLAT-014: the question names a platform that is not installed by its name alone — the suffix
    /// <c>(not installed)</c> of the tree row and the combo does not stand in the middle of the sentence.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-014")]
    public Task RemovePlatform_TheQuestionNamesANotInstalledPlatformWithoutTheSuffix() => TestUiThread.RunAsync(async () =>
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "win32"], "platformOptions": { "win32": { "defines": ["LEGACY"] } }
            """);
        var pm = Pm(bed);
        pm.Select(PlatformRow(bed, "win32"));
        bed.Dialogs.Enqueue("Msg.Platform.RemoveConfirm", "Cancel");
        await bed.Services.Commands.ExecuteAsync("project.removePlatform");
        var request = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal(("Msg.Platform.RemoveConfirm", "Win32"), (request.MessageId, Assert.Single(request.Args)));
        Assert.Equal(["win64", "win32"], bed.Services.Projects.Project!.Platforms);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-014")]
    public Task RemovePlatform_LastIsDisabled_OptionsEntryAskedAndRemoved_ActiveMovesToFirst_NoFileDeleted() => TestUiThread.RunAsync(RemoveOnUiThread);

    private static async Task RemoveOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "test32", "win32"], "activePlatform": "test32",
              "platformOptions": { "test32": { "defines": ["LEGACY"], "outputDir": "out/$(Platform)/$(Config)" } }
            """);
        var pm = Pm(bed);
        var commands = bed.Services.Commands;
        // Outputs and intermediates of the platforms: they stay on disk whatever is removed.
        string[] built = [bed.File(BuildTestBed.Dir + @"\out\test32\Debug\Hello.exe", "MZ"), bed.File(BuildTestBed.Dir + @"\bin\win32\Debug\Hello.exe", "MZ"),
            bed.File(BuildTestBed.Dir + @"\obj\test32\Debug\Hello.o", "o")];

        // A platform without an options entry is removed without a question (here: one that is not installed).
        pm.Select(PlatformRow(bed, "win32"));
        var remove = pm.ContextMenu()[^1];
        Assert.Equal(("project.removePlatform", true, "win32"), (remove.CommandId, remove.IsEnabled, remove.Argument));
        await commands.ExecuteAsync(remove.CommandId!, remove.Argument);
        Assert.Empty(bed.Dialogs.Requests);
        Assert.Equal(["win64", Second], bed.Services.Projects.Project!.Platforms);
        Assert.Equal(Second, bed.Services.Projects.Project.ActivePlatform);
        Assert.DoesNotContain("win32", ProjectFile(bed), StringComparison.Ordinal);

        // One with an options entry: Msg.Platform.RemoveConfirm with its label; Cancel (the default button) keeps everything.
        var before = ProjectFile(bed);
        pm.Select(PlatformRow(bed, Second));
        bed.Dialogs.Enqueue("Msg.Platform.RemoveConfirm", "Cancel", "Remove");
        await commands.ExecuteAsync("project.removePlatform"); // the selected row (Delete has no binding; the command acts on the selection)
        var request = Assert.Single(bed.Dialogs.Requests);
        Assert.Equal(("Msg.Platform.RemoveConfirm", "Test32"), (request.MessageId, Assert.Single(request.Args)));
        Assert.Equal(["Cancel", "Remove"], Core.Dialogs.MessageDefinitions.TryGet("Msg.Platform.RemoveConfirm")!.Buttons);
        Assert.Equal(before, ProjectFile(bed));
        Assert.Equal(["win64", Second], bed.Services.Projects.Project!.Platforms);

        // Remove: the platform and its options entry go; it was the active platform, so the first remaining one is active.
        await commands.ExecuteAsync("project.removePlatform");
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        var project = bed.Services.Projects.Project!;
        Assert.Equal(["win64"], project.Platforms);
        Assert.Equal("win64", project.ActivePlatform);
        Assert.Empty(bed.Services.Projects.Definition!.PlatformOptions);
        Assert.DoesNotContain("platformOptions", ProjectFile(bed), StringComparison.Ordinal);
        Assert.DoesNotContain("test32", ProjectFile(bed), StringComparison.Ordinal);
        Assert.Contains("\"activePlatform\": \"win64\"", ProjectFile(bed), StringComparison.Ordinal);
        Assert.Equal(["Windows 64-bit (Win64)"], Node(bed).Children.Select(c => c.Name));
        Assert.True(Node(bed).Children[0].IsActivePlatform);

        // Nothing on disk was deleted.
        Assert.All(built, path => Assert.True(bed.Fs.FileExists(path), path));

        // The only listed platform cannot be removed: the command is disabled and does nothing.
        pm.Select(PlatformRow(bed, "win64"));
        Assert.False(pm.ContextMenu()[^1].IsEnabled);
        Assert.False(commands.CanExecute("project.removePlatform"));
        await commands.ExecuteAsync("project.removePlatform");
        Assert.False(await bed.Services.Projects.RemovePlatformAsync("win64"));
        Assert.Equal(["win64"], bed.Services.Projects.Project!.Platforms);

        // Another row selected (a file, the node): nothing to remove.
        pm.Select(Node(bed));
        Assert.False(commands.CanExecute("project.removePlatform"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-015")]
    public Task ProjectFileKeepsUnknownKeys_NodeHiddenByFilter_NotADropTarget() => TestUiThread.RunAsync(KeysFilterDropOnUiThread);

    private static async Task KeysFilterDropOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "win32"], "futureKey": { "kept": [1, 2] },
              "platformOptions": { "win32": { "defines": ["A"], "linkerFlavour": "later" } }
            """);
        var pm = Pm(bed);
        QuickPickViewModel? shown = null;
        pm.ShowQuickPick = popup => shown = popup;

        // Add, activate, remove: one write each through the project serializer; keys the IDE does not know survive.
        await bed.Services.Commands.ExecuteAsync("project.addPlatform");
        Assert.NotNull(shown);
        shown!.SelectedIndex = 0;
        await shown.CommitAsync();
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        var file = ProjectFile(bed).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("\"futureKey\": {\n    \"kept\": [\n      1,\n      2\n    ]\n  }", file, StringComparison.Ordinal);
        Assert.Contains("\"linkerFlavour\": \"later\"", file, StringComparison.Ordinal);
        Assert.Contains("\"activePlatform\": \"test32\"", file, StringComparison.Ordinal);
        Assert.True(await bed.Services.Projects.RemovePlatformAsync(Second));
        file = ProjectFile(bed);
        Assert.Contains("futureKey", file, StringComparison.Ordinal);
        Assert.Contains("linkerFlavour", file, StringComparison.Ordinal);
        Assert.Equal(["win64", "win32"], bed.Services.Projects.Project!.Platforms);
        Assert.Equal("win64", bed.Services.Projects.Project.ActivePlatform);

        // The tree filter hides the node and its rows while a filter text is set — also when the text matches their names.
        Node(bed).IsExpanded = true;
        Assert.Contains(NodeKey, pm.VisibleRows().Select(n => n.Key));
        pm.FilterText = "Win";
        bed.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.False(Node(bed).IsVisibleInTree);
        Assert.All(Node(bed).Children, c => Assert.False(c.IsVisibleInTree));
        Assert.DoesNotContain(pm.VisibleRows(), n => n.IsPlatformRow);
        pm.FilterText = "Unit";
        bed.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["$project", "Unit1.pas"], pm.VisibleRows().Select(n => n.Key));
        pm.FilterText = "";
        bed.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.True(Node(bed).IsVisibleInTree);
        Assert.All(Node(bed).Children, c => Assert.Equal(-1, c.MatchStart));

        // No drop target, no drag source, no rename, no file command.
        Assert.Null(pm.DropTargetFor(Node(bed)));
        Assert.Null(pm.DropTargetFor(PlatformRow(bed, "win64")));
        Assert.Same(pm.Roots[0], pm.DropTargetFor(pm.Find("Unit1.pas")));
        Assert.False(pm.CanStartDrag(PlatformRow(bed, "win64")));
        Assert.False(pm.CanStartDrag(Node(bed)));
        bed.Services.FocusTracker.Update(new FocusState(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false)); // the tree has the focus
        pm.Select(PlatformRow(bed, "win64"));
        Assert.All((string[])["project.renameFile", "project.removeFile", "project.deleteFile", "project.moveFile", "project.newFolder", "file.copyPath", "file.showInExplorer"],
            id => Assert.False(bed.Services.Commands.CanExecute(id), id));
        Assert.DoesNotContain(pm.ContextMenu(), i => i.CommandId is "project.renameFile" or "project.removeFile" or "projectManager.open");
    }
}
