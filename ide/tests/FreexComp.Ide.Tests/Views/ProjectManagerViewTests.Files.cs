using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>File operations of Phase 8 in the Project Manager (phase8-project-files.md, phase8-ui-additions.md §1).</summary>
public sealed partial class ProjectManagerViewTests
{
    private const string Adorner = "Tool.ProjectManager.DragAdorner";

    /// <summary>The centre of a row's name in window coordinates.</summary>
    private static Point RowPoint(HeadlessHarness h, string key)
    {
        var name = h.Find<TextBlock>(Row + key + ".Name");
        return name.TranslatePoint(new Point(Math.Min(name.Bounds.Width, 40) / 2, name.Bounds.Height / 2), h.Window)
            ?? throw new InvalidOperationException("Row " + key + " is not in the window.");
    }

    private static void Move(HeadlessHarness h, Point point)
    {
        h.Window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(HeadlessHarness h, Point point, MouseButton button = MouseButton.Left)
    {
        h.Window.MouseDown(point, button);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Release(HeadlessHarness h, Point point, MouseButton button = MouseButton.Left)
    {
        h.Window.MouseUp(point, button);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The keys of the rows whose automation name says "drop target" (UI-TW-122).</summary>
    private static string[] DropTargets(HeadlessHarness h) =>
        [.. h.Find<TreeView>("Tool.ProjectManager.Tree").GetVisualDescendants().OfType<TreeViewItem>()
            .Where(i => AutomationProperties.GetName(i)?.EndsWith(Strings.ProjectManager_DropTargetSuffix, StringComparison.Ordinal) == true)
            .Select(i => ((ProjectNodeViewModel)i.DataContext!).Key)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-120")]
    [Trait("Req", "IDE-PM-121")]
    [Trait("Req", "UI-TW-119")]
    [Trait("Req", "UI-TW-120")]
    [Trait("Req", "UI-TW-121")]
    [Trait("Req", "UI-TW-122")]
    public async Task DragRowOntoFolderMovesFile()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            HelloProject(o);
        });
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        string Abs(string relative) => Path.Combine(h.TempDir, "Hello", relative);
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");

        // UI-TW-119: a press and a move of at most 4 DIP is no drag; beyond that the drag starts.
        var start = RowPoint(h, "Unit1.pas");
        Press(h, start);
        Assert.Equal(["Unit1.pas"], pm.Selection.Select(n => n.Key)); // a press on an unselected row selects it first
        Move(h, start + new Point(3, 3));
        Assert.Null(h.TryFind<Control>(Adorner));
        Assert.Empty(pm.DraggedRows);
        Move(h, start + new Point(6, 2));

        // UI-TW-120: the dragged row at 50 % opacity, the adorner with icon and name at pointer offset (12, 16).
        var adorner = h.Find<Border>(Adorner);
        Assert.Equal("Unit1.pas", AutomationProperties.GetName(adorner));
        Assert.Equal("unit", Assert.Single(adorner.GetVisualDescendants().OfType<Icons.IdeIcon>()).IconName);
        Assert.Equal(Color.Parse("#FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(adorner.Background).Color); // bg.raised
        Assert.Equal(new Thickness(1), adorner.BorderThickness);
        Assert.Equal(start + new Point(6 + 12, 2 + 16), adorner.TranslatePoint(default, h.Window));
        Assert.Equal(["Unit1.pas"], pm.DraggedRows.Select(n => n.Key));
        Assert.Equal(0.5, h.Find<TextBlock>(Row + "Unit1.pas.Name").FindAncestorOfType<ProjectNodeView>()!.Opacity);
        Assert.Equal(1, h.Find<TextBlock>(Row + "notes.md.Name").FindAncestorOfType<ProjectNodeView>()!.Opacity);

        // UI-TW-121 / UI-TW-122: over a file row the feedback is on that file's folder row — exactly one row; the cursor
        // is the move cursor over a valid target and "not allowed" elsewhere.
        Move(h, RowPoint(h, "lib/StrUtils2.pas"));
        Assert.Equal(["lib/"], DropTargets(h));
        var lib = h.Find<TreeViewItem>(Row + "lib/");
        Assert.Equal("lib, drop target", AutomationProperties.GetName(lib));
        var header = lib.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
        Assert.Equal(Color.Parse("#E3F2EF"), Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color); // accent.subtle
        Assert.Equal(Color.Parse("#0F7B6C"), Assert.IsAssignableFrom<ISolidColorBrush>(header.BorderBrush).Color); // accent.default
        Assert.Equal(new Thickness(1), header.BorderThickness);
        Assert.Equal(new Cursor(StandardCursorType.DragMove).ToString(), tree.Cursor!.ToString());
        Move(h, RowPoint(h, "MainForm.pas"));
        Assert.Equal(["$project"], DropTargets(h)); // a file directly under the root: the root row
        Move(h, RowPoint(h, "../Shared/Common.pas"));
        Assert.Empty(DropTargets(h)); // the External group is no target
        Assert.Equal(new Cursor(StandardCursorType.No).ToString(), tree.Cursor!.ToString());
        Move(h, RowPoint(h, "aaa/"));
        Assert.Equal(["aaa/"], DropTargets(h));

        // Released on the folder row: the file moves there (IDE-PM-121), the visuals end.
        Release(h, RowPoint(h, "aaa/"));
        await h.WaitForAsync(() => pm.Find("aaa/Unit1.pas") is not null, "Unit1.pas moved");
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(Abs(@"aaa\Unit1.pas")));
        Assert.False(File.Exists(Abs("Unit1.pas")));
        Assert.Contains("\"path\": \"aaa/Unit1.pas\"", File.ReadAllText(Abs("Hello.pproj")), StringComparison.Ordinal);
        Assert.Null(h.TryFind<Control>(Adorner));
        Assert.Empty(DropTargets(h));
        Assert.Empty(pm.DraggedRows);
        Assert.Null(tree.Cursor);
        Assert.Equal(["aaa/Unit1.pas"], pm.Selection.Select(n => n.Key));
        Assert.Equal(1, h.Find<TextBlock>(Row + "aaa/Unit1.pas.Name").FindAncestorOfType<ProjectNodeView>()!.Opacity);

        // A press on a row of a multi-selection drags the whole selection: the adorner counts the files.
        pm.Select(pm.Find("aaa/Unit1.pas")!);
        pm.Select(pm.Find("aaa/Zed.pas")!, ProjectSelectMode.Toggle);
        Dispatcher.UIThread.RunJobs();
        start = RowPoint(h, "aaa/Zed.pas");
        Press(h, start);
        Assert.Equal(2, pm.Selection.Count);
        Move(h, start + new Point(0, 9));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Drag_FileCount, 2), AutomationProperties.GetName(h.Find<Border>(Adorner)));
        Assert.Empty(h.Find<Border>(Adorner).GetVisualDescendants().OfType<Icons.IdeIcon>());
        Assert.Equal(["aaa/Unit1.pas", "aaa/Zed.pas"], pm.DraggedRows.Select(n => n.Key));

        // Escape cancels the drag: nothing moves.
        Move(h, RowPoint(h, "lib/"));
        Assert.Equal(["lib/"], DropTargets(h));
        h.Press("Escape");
        Assert.Null(h.TryFind<Control>(Adorner));
        Assert.Empty(DropTargets(h));
        Release(h, RowPoint(h, "lib/"));
        Assert.NotNull(pm.Find("aaa/Zed.pas"));

        // Again, released on the root row: both files move into the project directory; aaa/ stays as an empty folder.
        pm.Select(pm.Find("aaa/Unit1.pas")!);
        pm.Select(pm.Find("aaa/Zed.pas")!, ProjectSelectMode.Toggle);
        Dispatcher.UIThread.RunJobs();
        start = RowPoint(h, "aaa/Unit1.pas");
        Press(h, start);
        Move(h, start + new Point(8, 0));
        Move(h, RowPoint(h, "$project"));
        Assert.Equal(["$project"], DropTargets(h));
        Release(h, RowPoint(h, "$project"));
        await h.WaitForAsync(() => pm.Find("Zed.pas") is not null, "both files moved");
        Assert.True(File.Exists(Abs("Unit1.pas")));
        Assert.True(File.Exists(Abs("Zed.pas")));
        Assert.Equal(["aaa/"], pm.EmptyFolders);
        Assert.NotNull(pm.Find("aaa/"));

        // Released outside a valid target: canceled. Nothing is dragged with the right button.
        start = RowPoint(h, "notes.md");
        Press(h, start);
        Move(h, start + new Point(0, 7));
        Assert.NotNull(h.TryFind<Control>(Adorner));
        Move(h, RowPoint(h, "../Shared/Common.pas"));
        Release(h, RowPoint(h, "../Shared/Common.pas"));
        Assert.Null(h.TryFind<Control>(Adorner));
        Assert.NotNull(pm.Find("notes.md"));
        Press(h, start, MouseButton.Right);
        Move(h, start + new Point(0, 9));
        Assert.Null(h.TryFind<Control>(Adorner));
        Release(h, start + new Point(0, 9), MouseButton.Right);
        Assert.Empty(h.Messages.Requests);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-120")]
    [Trait("Req", "UI-TW-121")]
    public async Task DragDisabledWhileFilteredOrBusy()
    {
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\lib\Unit2.pas"] = "unit Unit2;\ninterface\nimplementation\nend.\n";
            o.InitialFiles[@"Hello\Main.pas"] = "unit Main;\ninterface\n{$FORM Main.form}\nimplementation\nend.\n";
            o.InitialFiles[@"Hello\Main.form"] = "object Main: TForm\nend\n";
            o.InitialFiles[@"Shared\Common.pas"] = "unit Common;\ninterface\nimplementation\nend.\n";
        }, projectExtra: "", implementedPhase: null);
        var pm = Pm(h);
        Assert.Equal(4, await h.Services.Projects.AddExistingFilesAsync([
            Path.Combine(h.TempDir, @"Hello\lib\Unit2.pas"), Path.Combine(h.TempDir, @"Hello\Main.pas"), Path.Combine(h.TempDir, @"Shared\Common.pas"),
            Path.Combine(h.TempDir, @"Hello\Gone.pas")]));
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles && pm.Find("Gone.pas")!.IsMissing, "missing-file check done");
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        pm.Find("Main.pas")!.IsExpanded = true; // its form child row
        Dispatcher.UIThread.RunJobs();

        bool StartsDrag(string key)
        {
            var start = RowPoint(h, key);
            Press(h, start);
            Move(h, start + new Point(0, 8));
            var dragging = h.TryFind<Control>(Adorner) is not null;
            Move(h, RowPoint(h, "lib/"));
            var target = DropTargets(h);
            h.Press("Escape");
            Release(h, RowPoint(h, "lib/"));
            Assert.Null(h.TryFind<Control>(Adorner));
            Assert.True(dragging == (target.Length == 1), key);
            return dragging;
        }

        // Rows that cannot be dragged: the root, folder rows, form child rows, files under External, missing files.
        Assert.True(StartsDrag("Unit1.pas"));
        Assert.True(StartsDrag("Hello.pas"));
        foreach (var key in new[] { "$project", "lib/", "Main.form", "../Shared/Common.pas", "Gone.pas" })
            Assert.False(StartsDrag(key), key);

        // While the filter box is non-empty the tree is not the folder structure: no drag.
        pm.FilterText = "unit";
        await h.WaitForAsync(() => pm.Find("Hello.pas")!.IsVisibleInTree == false, "filter applied");
        Assert.False(StartsDrag("Unit1.pas"));
        pm.FilterText = "";
        await h.WaitForAsync(() => pm.Find("Hello.pas")!.IsVisibleInTree, "filter cleared");
        Assert.True(StartsDrag("Unit1.pas"));

        // While the IDE is not Idle (a build runs): no drag, and project.moveFile is disabled.
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        var building = h.Services.Commands.ExecuteAsync("project.build"); // completes when the held build is released
        await h.WaitForAsync(() => held.Request is not null && h.Services.State.Current.State == IdeState.Building, "build started");
        pm.Select(pm.Find("Unit1.pas")!);
        Assert.False(h.Services.Commands.CanExecute("project.moveFile"));
        Assert.False(StartsDrag("Unit1.pas"));
        held.Release();
        await building;
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        pm.Select(pm.Find("Unit1.pas")!);
        Assert.True(h.Services.Commands.CanExecute("project.moveFile"));
        Assert.True(StartsDrag("Hello.pas")); // another row first: a second press on the same spot would be a double-click
        Assert.True(StartsDrag("Unit1.pas"));

        // UI-TW-121: a collapsed folder under the pointer for 700 ms expands.
        pm.Find("lib/")!.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        var from = RowPoint(h, "Unit1.pas");
        Press(h, from);
        Move(h, from + new Point(0, 8));
        Move(h, RowPoint(h, "lib/"));
        h.Clock.Advance(ProjectManagerViewModel.DragExpandDelay - TimeSpan.FromMilliseconds(1));
        Assert.False(pm.Find("lib/")!.IsExpanded);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(pm.Find("lib/")!.IsExpanded);
        h.Press("Escape");
        Release(h, RowPoint(h, "lib/"));
        Assert.Equal(["Hello.pas", "Unit1.pas"], h.Services.Projects.Project!.Files.Take(2).Select(f => f.Path)); // nothing moved
    }

    /// <summary>
    /// UI-TW-116: the in-place naming of a new folder in the tree — the editor (<c>.NameEditor</c>) pre-filled with all
    /// text selected, inline errors in the popup of UI-TW-112, Enter commits, Escape keeps the provisional name, and an
    /// invalid name on focus loss cancels instead of trapping focus. Delete then removes the empty folder (IDE-PM-115).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-116")]
    [Trait("Req", "IDE-PM-112")]
    [Trait("Req", "IDE-PM-115")]
    public async Task NewFolderNamingInPlace()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        string Abs(string relative) => Path.Combine(h.TempDir, "Hello", relative);
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();

        // The command from the tree: a new row under lib/, its editor focused with the whole name selected.
        pm.Select(pm.Find("lib/")!);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("project.newFolder");
        Dispatcher.UIThread.RunJobs();
        const string NewRow = Row + "lib/NewFolder1/";
        var editor = h.Find<TextBox>(NewRow + ".NameEditor");
        Assert.True(Directory.Exists(Abs(@"lib\NewFolder1")));
        Assert.True(editor.IsFocused);
        Assert.Equal("NewFolder1", editor.Text);
        Assert.Equal((0, 10), (Math.Min(editor.SelectionStart, editor.SelectionEnd), Math.Max(editor.SelectionStart, editor.SelectionEnd)));
        Assert.Equal(IconOf(h, "lib/"), IconOf(h, "lib/NewFolder1/")); // UI-TW-115: looks exactly like a derived folder row
        Assert.Equal(Abs(@"lib\NewFolder1"), ToolTip.GetTip(h.Find<TreeViewItem>(NewRow)));

        // An invalid name: the message in the popup, Enter keeps the editor open.
        h.Type("a/b"); // replaces the selection
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Project_FolderNameInvalid, h.Find<TextBlock>(NewRow + ".Error").Text);
        h.Press("Enter");
        Assert.Same(editor, h.TryFind<TextBox>(NewRow + ".NameEditor"));
        Assert.True(Directory.Exists(Abs(@"lib\NewFolder1")));

        // A name that exists: the other message; a valid one commits with Enter and focus returns to the renamed row.
        editor.Text = "strutils2.pas";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Project_FolderExists, "strutils2.pas"),
            h.Find<TextBlock>(NewRow + ".Error").Text);
        editor.Text = "data";
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => pm.Find("lib/data/") is not null, "folder renamed");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Directory.Exists(Abs(@"lib\data")));
        Assert.False(Directory.Exists(Abs(@"lib\NewFolder1")));
        Assert.Null(h.TryFind<TextBox>(Row + "lib/data/.NameEditor"));
        Assert.Equal(["lib/data/"], pm.Selection.Select(n => n.Key));
        Assert.Equal(Row + "lib/data/", h.FocusedAutomationId);

        // Escape keeps the provisional name.
        await h.ExecuteAsync("project.newFolder"); // inside lib/data/
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(h.TryFind<TextBox>(Row + "lib/data/NewFolder1/.NameEditor"));
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<TextBox>(Row + "lib/data/NewFolder1/.NameEditor"));
        Assert.True(Directory.Exists(Abs(@"lib\data\NewFolder1")));

        // Focus loss with an invalid name cancels the edit (the provisional name stays); with a valid one it commits.
        pm.Select(pm.Find("$project")!);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("project.newFolder");
        Dispatcher.UIThread.RunJobs();
        var second = h.Find<TextBox>(Row + "NewFolder1/.NameEditor");
        second.Text = "con";
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("window.focusEditor");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<TextBox>(Row + "NewFolder1/.NameEditor"));
        Assert.True(Directory.Exists(Abs("NewFolder1")));
        Assert.Contains("NewFolder1/", pm.EmptyFolders);

        // Delete on the empty folder row removes it without a question; on a folder with files it is disabled.
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        pm.Select(pm.Find("NewFolder1/")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["project.addNewUnit", "project.addNewForm", "project.addNewFrame", "project.addNewInheritedForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", "-",
            "project.removeFile", "-", "file.showInExplorer", "file.openInTerminal", "file.copyPath"],
            pm.ContextMenu().Select(m => m.Command?.Id ?? "-"));
        Assert.True(h.Services.Commands.CanExecute("project.removeFile"));
        await h.ExecuteAsync("project.removeFile");
        Assert.Null(pm.Find("NewFolder1/"));
        Assert.False(Directory.Exists(Abs("NewFolder1")));
        pm.Select(pm.Find("aaa/")!);
        Assert.False(h.Services.Commands.CanExecute("project.removeFile"));
        Assert.Empty(h.Messages.Requests);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-PM-128")]
    [Trait("Req", "UI-TW-113")]
    public async Task OsDropOnFolderStillAddsByReference()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            HelloProject(o);
            o.InitialFiles[@"Hello\Added.pas"] = "unit Added;\ninterface\nimplementation\nend.\n";
            o.InitialFiles[@"Elsewhere\Far.pas"] = "unit Far;\ninterface\nimplementation\nend.\n";
        });
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        string Abs(string relative) => Path.Combine(h.TempDir, relative);

        // Dropped on a folder row: added by reference where the files are — never copied or moved into the folder.
        await h.DropFilesAsync(Row + "lib/", Abs(@"Hello\Added.pas"), Abs(@"Elsewhere\Far.pas"));
        await h.WaitForAsync(() => h.Services.Projects.IsMember(Abs(@"Elsewhere\Far.pas")), "files added");

        var paths = h.Services.Projects.Project!.Files.Select(f => f.Path).ToList();
        Assert.Contains("Added.pas", paths);
        Assert.Contains("../Elsewhere/Far.pas", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("lib/", StringComparison.Ordinal) && p != "lib/StrUtils2.pas");
        Assert.True(File.Exists(Abs(@"Hello\Added.pas")));
        Assert.True(File.Exists(Abs(@"Elsewhere\Far.pas")));
        Assert.Equal(["StrUtils2.pas"], Directory.EnumerateFiles(Abs(@"Hello\lib")).Select(Path.GetFileName));
        Assert.NotNull(pm.Find("Added.pas"));
        Assert.Empty(pm.DraggedRows); // no in-tree drag was involved
        Assert.Null(h.TryFind<Control>(Adorner));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-118")]
    [Trait("Req", "IDE-PM-134")]
    public async Task BusyRowShowsRingAndTypedName()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            HelloProject(o);
        });
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        const string Id = Row + "Unit1.pas";
        var node = pm.Find("Unit1.pas")!;
        pm.Select(node);
        Assert.True(pm.BeginRename("Unit1.pas"));
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextBox>(Id + ".Editor");
        editor.Text = "Customers.pas";
        Assert.Null(h.TryFind<Control>(Id + ".Busy"));

        // The rename waits for the language service: the editor is closed, the row shows the typed name in
        // text.secondary and the 12 px ring where the modified marker is.
        node.IsModified = true;
        node.BusyName = "Customers.pas";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<TextBox>(Id + ".Editor"));
        var name = h.Find<TextBlock>(Id + ".Name");
        Assert.True(name.IsEffectivelyVisible);
        Assert.Equal("Customers.pas", name.Text);
        Assert.True(h.Window.TryFindResource("FxcTextSecondaryBrush", h.Window.ActualThemeVariant, out var secondary));
        Assert.Equal(((ISolidColorBrush)secondary!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(name.Foreground).Color);
        var ring = h.Find<Control>(Id + ".Busy");
        Assert.True(ring.IsEffectivelyVisible);
        Assert.Equal(new Size(12, 12), ring.Bounds.Size);
        Assert.Null(h.TryFind<Control>(Id + ".Modified"));
        Assert.NotNull(node.Rename); // the model of the editor waits for the answer

        // Refused (IDE-PM-134): the editor is open again with the typed name, its base name selected.
        node.BusyName = null;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(Id + ".Busy"));
        Assert.NotNull(h.TryFind<Control>(Id + ".Modified"));
        var reopened = h.Find<TextBox>(Id + ".Editor");
        Assert.Equal("Customers.pas", reopened.Text);
        Assert.True(reopened.IsFocused);
        Assert.Equal((0, "Customers".Length), (Math.Min(reopened.SelectionStart, reopened.SelectionEnd), Math.Max(reopened.SelectionStart, reopened.SelectionEnd)));
        node.Rename!.Cancel();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Unit1.pas", h.Find<TextBlock>(Id + ".Name").Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-123")]
    [Trait("Req", "IDE-PM-146")]
    public async Task UnlistedFilesBarRenders()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            HelloProject(o);
            o.InitialFiles[@"Hello\Extra.pas"] = "unit Extra;\ninterface\nimplementation\nend.\n";
            o.InitialFiles[@"Hello\lib\More.pas"] = "unit More;\ninterface\nimplementation\nend.\n";
        });
        var pm = await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        const string Bar = "Tool.ProjectManager.InfoBar.UnlistedFiles";

        // Kind UnlistedFiles, severity info (accent.subtle), the text and the three actions in order, no dismiss button.
        var bar = h.Find<InfoBarView>(Bar);
        Assert.True(bar.IsVisible);
        Assert.Equal(Color.Parse("#E3F2EF"), Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background).Color);
        Assert.Equal("2 Pascal files in the project folders are not part of the project.", h.Find<TextBlock>(Bar + ".Text").Text);
        var actions = bar.GetVisualDescendants().OfType<Button>().Select(b => AutomationProperties.GetAutomationId(b)).ToList();
        Assert.Equal([Bar + ".Action.AddToProject", Bar + ".Action.Show", Bar + ".Action.Ignore"], actions);
        Assert.Equal(["Add to Project", "Show", "Ignore"], bar.GetVisualDescendants().OfType<Button>().Select(b => AutomationProperties.GetName(b)));
        Assert.Null(h.TryFind<Control>(Bar + ".Dismiss"));

        // Placement: under the tool bar, below the missing-files bar when both are shown, above the tree.
        var missing = h.Find<InfoBarView>("Tool.ProjectManager.InfoBar.MissingFiles");
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        double Top(Visual v) => v.TranslatePoint(default, h.Window)!.Value.Y;
        Assert.Equal(Top(missing) + missing.Bounds.Height, Top(bar));
        Assert.Equal(Top(bar) + bar.Bounds.Height, Top(tree));
        Assert.Equal(missing.Bounds.Width, bar.Bounds.Width);

        // Show: the OS file manager with the first unlisted file selected; the bar goes.
        var shell = (FakeShellService)h.Services.Shell;
        h.Click(Bar + ".Action.Show");
        Assert.Equal("RevealFile " + Path.Combine(h.TempDir, "Hello", "Extra.pas"), shell.Calls[^1]);
        Assert.Null(h.TryFind<Control>(Bar));
        Assert.NotNull(h.TryFind<Control>("Tool.ProjectManager.InfoBar.MissingFiles"));

        // A new file found at the next check (window activation) brings it back; Ignore is the dismissal.
        h.WriteFileExternally(@"Hello\Third.pas", "unit Third;\ninterface\nimplementation\nend.\n");
        pm.OnWindowActivated();
        await h.WaitForAsync(() => pm.UnlistedFilesBar is not null, "unlisted files found");
        Assert.Equal("3 Pascal files in the project folders are not part of the project.", h.Find<TextBlock>(Bar + ".Text").Text);
        h.Click(Bar + ".Action.Ignore");
        Assert.Null(h.TryFind<Control>(Bar));
        Assert.Equal(["Extra.pas", "Third.pas", "lib/More.pas"], pm.IgnoredFiles);

        // Add to Project (one file left): the singular text, then the file is a project member.
        h.WriteFileExternally(@"Hello\aaa\Fourth.pas", "unit Fourth;\ninterface\nimplementation\nend.\n");
        pm.OnWindowActivated();
        await h.WaitForAsync(() => pm.UnlistedFilesBar is not null, "the fourth file found");
        Assert.Equal("1 Pascal file in the project folders is not part of the project.", h.Find<TextBlock>(Bar + ".Text").Text);
        h.Click(Bar + ".Action.AddToProject");
        await h.WaitForAsync(() => pm.Find("aaa/Fourth.pas") is not null, "the file added");
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "check after the add");
        Assert.Null(h.TryFind<Control>(Bar));
    }

    /// <summary>
    /// IDE-TGT-017: the root of a library project has the icon variant <c>project.library</c> and its tooltip is the DLL
    /// path of the active configuration; other projects keep the project icon and the project file's path.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TGT-017")]
    [Trait("Req", "IDE-TGT-011")]
    public async Task LibraryProjectIconAndTooltip()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"Mathlib\Mathlib.pproj"] = """
                { "formatVersion": 1, "name": "Mathlib", "appType": "library", "mainSource": "Mathlib.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                      "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "out" } },
                  "files": [ { "path": "Mathlib.pas" } ] }
                """;
            o.InitialFiles[@"Mathlib\Mathlib.pas"] = "library Mathlib;\nbegin\nend.\n";
            HelloProject(o);
        });
        var dir = Path.Combine(h.TempDir, "Mathlib");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(dir, "Mathlib.pproj")));
        var pm = Pm(h);
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("project.library", IconOf(h, ProjectManagerViewModel.RootKey));
        Assert.Contains("project.library", FreexComp.Ide.Icons.IconCatalog.Names);
        var icon = h.Find<FreexComp.Ide.Icons.IdeIcon>(Row + ProjectManagerViewModel.RootKey + ".Icon");
        Assert.True(icon.IsEffectivelyVisible);
        Assert.Equal(new Size(16, 16), icon.Bounds.Size);
        Assert.Equal(Path.Combine(dir, @"bin\Debug\Mathlib.dll"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + ProjectManagerViewModel.RootKey)));
        Assert.Equal(Path.Combine(dir, "Mathlib.pas"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + "Mathlib.pas"))); // file rows: the path
        Assert.Equal("unit", IconOf(h, "Mathlib.pas"));

        // The tooltip follows the active configuration.
        await h.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Path.Combine(dir, @"out\Mathlib.dll"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + ProjectManagerViewModel.RootKey)));

        // Another kind of project: the project icon and the project file.
        await OpenHelloAsync(h);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("project", IconOf(h, ProjectManagerViewModel.RootKey));
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "Hello.pproj"), ToolTip.GetTip(h.Find<TreeViewItem>(Row + ProjectManagerViewModel.RootKey)));
    }
}
