using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Keyboard, pointer and drawing rules shared by lists and trees (accessibility.md §2, tool-windows.md §1).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ListKeyboardTests
{
    private const string Row = "Tool.ProjectManager.Row.";

    private static void Project(HarnessOptions o, int extraUnits = 0)
    {
        var files = new StringBuilder("""{ "path": "Hello.pas" }, { "path": "MainForm.pas" }, { "path": "Missing.pas" }, { "path": "notes.md" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "lib/StrUtils2.pas" }""");
        for (var i = 0; i < extraUnits; i++)
        {
            files.Append($$""", { "path": "Gen{{i:D3}}.pas" }""");
            o.InitialFiles[$@"Hello\Gen{i:D3}.pas"] = "unit Gen;\n";
        }

        o.InitialFiles[@"Hello\Hello.pproj"] = $$"""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "files": [ {{files}} ] }""";
        foreach (var name in new[] { "Hello.pas", "MainForm.pas", "notes.md", "Unit1.pas", "Unit2.pas", @"lib\StrUtils2.pas" })
            o.InitialFiles[@"Hello\" + name] = "unit X;\n";
    }

    private static async Task<(HeadlessHarness H, ProjectManagerViewModel Pm)> OpenAsync(int extraUnits = 0)
    {
        var h = await HeadlessHarness.StartAsync(o => Project(o, extraUnits));
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
        var pm = Assert.IsType<ProjectManagerViewModel>(h.Services.ToolWindows.Get("ProjectManager"));
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        h.Press("Alt+1");
        Dispatcher.UIThread.RunJobs();
        return (h, pm);
    }

    private static string[] Selected(ProjectManagerViewModel pm) => [.. pm.Selection.Select(n => n.Key)];

    private static void ClickRow(HeadlessHarness h, string key, KeyModifiers modifiers = KeyModifiers.None, int clicks = 1)
    {
        var row = h.Find<TreeViewItem>(Row + key).GetVisualDescendants().OfType<TextBlock>().First(t => AutomationIds.Get(t) == Row + key + ".Name");
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), h.Window)!.Value;
        var raw = (modifiers.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
                  | (modifiers.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None);
        for (var i = 0; i < clicks; i++)
        {
            h.Window.MouseDown(point, MouseButton.Left, raw);
            h.Window.MouseUp(point, MouseButton.Left, raw);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// UI-A11Y-007: Up/Down move, Left/Right collapse/expand, Home/End first/last, typed characters search by prefix
    /// (reset after a 1 s pause), Shift+F10 opens the context menu of the focused row.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-007")]
    public async Task TreeKeysAndTypeAhead()
    {
        var (h, pm) = await OpenAsync();
        await using var _ = h;
        pm.Select(pm.Find("$project")!);
        Dispatcher.UIThread.RunJobs();
        h.Find<TreeViewItem>(Row + "$project").Focus(NavigationMethod.Tab);

        h.Press("Down");
        Assert.Equal(["lib/"], Selected(pm));
        h.Press("Left"); // collapse
        Assert.False(pm.Find("lib/")!.IsExpanded);
        h.Press("Right"); // expand
        Assert.True(pm.Find("lib/")!.IsExpanded);
        h.Press("End");
        Assert.Equal(["Unit2.pas"], Selected(pm));
        h.Press("Home");
        Assert.Equal(["$project"], Selected(pm));

        h.Type("u");
        Assert.Equal(["Unit1.pas"], Selected(pm));
        Assert.Equal(Row + "Unit1.pas", h.FocusedAutomationId);
        h.Type("u"); // the same character again: the next row starting with it
        Assert.Equal(["Unit2.pas"], Selected(pm));
        h.Clock.Advance(TypeAheadSearch.ResetAfter);
        h.Type("m");
        Assert.Equal(["MainForm.pas"], Selected(pm));
        h.Type("i"); // "mi"
        Assert.Equal(["Missing.pas"], Selected(pm));
        h.Clock.Advance(TypeAheadSearch.ResetAfter);
        h.Type("N"); // case-insensitive, a new prefix after the pause
        Assert.Equal(["notes.md"], Selected(pm));

        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        h.Press("Shift+F10");
        Assert.True(tree.ContextMenu!.IsOpen);
        Assert.Contains("Context.projectManager.open", tree.ContextMenu.Items.OfType<MenuItem>().Select(AutomationIds.Get));
        tree.ContextMenu.Close();
    }

    /// <summary>UI-A11Y-006: tool bar (one stop, arrows between its buttons) → filter → tree; Tab never leaves; F6 does.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-006")]
    public async Task TabOrderStaysInsideTheToolWindow()
    {
        var (h, pm) = await OpenAsync();
        await using var _ = h;
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Hello", "Unit1.pas")); // syncWithEditor enabled
        pm.Select(pm.Find("notes.md")!);
        Dispatcher.UIThread.RunJobs();
        h.Find<TreeViewItem>(Row + "notes.md").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith(Row, h.FocusedAutomationId, StringComparison.Ordinal);

        h.Press("Tab"); // wraps to the first stop: the tool bar, at its first button
        var first = h.FocusedAutomationId;
        Assert.Equal("Tool.ProjectManager.ToolBar.project.addNewUnit", first);
        h.Press("Right");
        Assert.StartsWith("Tool.ProjectManager.ToolBar.", h.FocusedAutomationId, StringComparison.Ordinal);
        Assert.NotEqual(first, h.FocusedAutomationId);
        h.Press("Left");
        Assert.Equal(first, h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Tool.ProjectManager.Filter", h.FocusedAutomationId);
        var stops = new List<string?>();
        for (var i = 0; i < 6 && h.FocusedAutomationId?.StartsWith(Row, StringComparison.Ordinal) != true; i++)
        {
            h.Press("Tab"); // the missing-files info bar (UI-TW-001: between tool bar and content), then the tree
            stops.Add(h.FocusedAutomationId);
        }

        Assert.All(stops, s => Assert.StartsWith("Tool.ProjectManager.", s, StringComparison.Ordinal));
        Assert.StartsWith(Row, h.FocusedAutomationId, StringComparison.Ordinal);
        h.Press("Tab"); // never leaves: back to the tool bar
        Assert.Equal(first, h.FocusedAutomationId);
        h.Press("Shift+Tab"); // and backwards to the tree
        Assert.StartsWith(Row, h.FocusedAutomationId, StringComparison.Ordinal);

        h.Press("F6"); // F6 leaves the tool window
        Assert.False(h.FocusedAutomationId?.StartsWith("Tool.ProjectManager", StringComparison.Ordinal) ?? false);
    }

    /// <summary>UI-TW-015: Ctrl+click toggles, Shift+click and Shift+arrows extend, Ctrl+A selects all visible (file) rows.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-015")]
    public async Task MultiSelectionFollowsThePlatform()
    {
        var (h, pm) = await OpenAsync();
        await using var _ = h;

        ClickRow(h, "MainForm.pas");
        Assert.Equal(["MainForm.pas"], Selected(pm));
        ClickRow(h, "notes.md", KeyModifiers.Control);
        Assert.Equal(["MainForm.pas", "notes.md"], Selected(pm));
        ClickRow(h, "MainForm.pas", KeyModifiers.Control); // toggled off
        Assert.Equal(["notes.md"], Selected(pm));
        ClickRow(h, "Unit2.pas", KeyModifiers.Shift); // extends from the anchor
        Assert.Equal(["notes.md", "Unit1.pas", "Unit2.pas"], Selected(pm).Order(StringComparer.OrdinalIgnoreCase));
        ClickRow(h, "Hello.pas");
        h.Press("Shift+Down");
        Assert.Equal(["Hello.pas", "MainForm.pas"], Selected(pm).Order(StringComparer.Ordinal));

        h.Press("Ctrl+A");
        Assert.Equal(pm.VisibleRows().Where(n => n.IsFile).Select(n => n.Key).Order(StringComparer.Ordinal), Selected(pm).Order(StringComparer.Ordinal));
    }

    /// <summary>UI-TW-018: a single click only selects; double-click and Enter run the same default action (open).</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-018")]
    public async Task ClickSelectsDoubleClickAndEnterOpen()
    {
        var (h, pm) = await OpenAsync();
        await using var _ = h;
        var unit1 = Path.Combine(h.TempDir, "Hello", "Unit1.pas");
        var unit2 = Path.Combine(h.TempDir, "Hello", "Unit2.pas");

        ClickRow(h, "Unit1.pas");
        Assert.Equal(["Unit1.pas"], Selected(pm));
        Assert.DoesNotContain(h.Services.Documents.Documents, d => d.FilePath == unit1);

        ClickRow(h, "Unit1.pas", clicks: 2);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unit1, "Unit1 opened by double-click");
        Dispatcher.UIThread.RunJobs();

        pm.Select(pm.Find("Unit2.pas")!);
        Dispatcher.UIThread.RunJobs();
        h.Find<TreeViewItem>(Row + "Unit2.pas").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unit2, "Unit2 opened by Enter");
    }

    /// <summary>UI-TW-017: a tree with more than 200 rows is virtualized; the test checks the view-model rows and only a few containers.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-017")]
    public async Task LongTreeIsVirtualized()
    {
        var (h, pm) = await OpenAsync(extraUnits: 300);
        await using var _ = h;
        Dispatcher.UIThread.RunJobs();

        Assert.True(pm.VisibleRows().Count() > 300);
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        Assert.IsType<VirtualizingStackPanel>(tree.ItemsPanelRoot);
        var realized = tree.GetVisualDescendants().OfType<TreeViewItem>().Count();
        Assert.True(realized < 100, $"{realized} row containers realized for {pm.VisibleRows().Count()} rows");
        Assert.NotNull(h.TryFind<TreeViewItem>(Row + "Gen000.pas")); // the first rows are realized
    }

    /// <summary>
    /// UI-TW-010: rows 22 high; the selected row bg.selected while the list has focus, bg.selected.inactive otherwise;
    /// no alternating row colours.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-010")]
    public async Task RowsAndSelectionColours()
    {
        var (h, pm) = await OpenAsync();
        await using var _ = h;
        pm.Select(pm.Find("Unit1.pas")!);
        Dispatcher.UIThread.RunJobs();
        h.Find<TreeViewItem>(Row + "Unit1.pas").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Color Background(string key) => Assert.IsAssignableFrom<ISolidColorBrush>(
            h.Find<TreeViewItem>(Row + key).GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot").Background ?? Brushes.Transparent).Color;
        Color Token(string brush) => Assert.IsAssignableFrom<ISolidColorBrush>(
            Application.Current!.TryGetResource(brush, h.Window.ActualThemeVariant, out var value) ? value : null).Color;

        var rows = h.FindAll(Row).OfType<TreeViewItem>().Where(r => r.IsEffectivelyVisible).ToList();
        Assert.All(rows, r => Assert.True(r.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot").Bounds.Height >= 22));
        Assert.Equal(Token("FxcBgSelectedBrush"), Background("Unit1.pas"));
        Assert.Equal(Background("notes.md"), Background("Unit2.pas")); // no alternating colours
        Assert.Equal(0, Background("notes.md").A);

        h.Press("Alt+0"); // focus leaves the tree
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Token("FxcBgSelectedInactiveBrush"), Background("Unit1.pas"));
    }

    /// <summary>UI-TW-016: a trimmed cell shows its full text as tooltip (the Documents list's name column); an untrimmed one has none.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-016")]
    public async Task TrimmedCellShowsFullTextAsToolTip()
    {
        const string LongName = "AVeryLongFileNameThatCannotFitIntoTheNameColumnOfTheDocumentsList.pas";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;\n";
            o.InitialFiles[LongName] = "unit B;\n";
        });
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, LongName));
        var running = h.Services.Commands.ExecuteAsync("window.documents");
        var dialog = await h.WaitForDialogAsync("Documents");
        Dispatcher.UIThread.RunJobs();

        var names = dialog.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "A.pas" or LongName).ToList();
        Assert.Equal(LongName, ToolTip.GetTip(names.Single(t => t.Text == LongName)));
        Assert.Null(ToolTip.GetTip(names.Single(t => t.Text == "A.pas")));
        Assert.Contains(dialog.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.TextTrimming == TextTrimming.PrefixCharacterEllipsis); // the folder column trims at the start

        dialog.Press("Escape");
        await running;
    }
}
