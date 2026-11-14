using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Tests.Infrastructure;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class MenuTests
{
    private static readonly (string Section, string Key, string Label)[] TopMenus =
    [
        ("2. File", "File", "&File"), ("3. Edit", "Edit", "&Edit"), ("4. Search", "Search", "&Search"),
        ("5. View", "View", "&View"), ("6. Project", "Project", "&Project"), ("7. Run", "Run", "&Run"),
        ("8. Component", "Component", "&Component"), ("9. Tools", "Tools", "&Tools"), ("10. Window", "Window", "&Window"),
        ("11. Help", "Help", "&Help"),
    ];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-MENU-001")]
    [Trait("Req", "UI-MENU-002")]
    [Trait("Req", "UI-MENU-003")]
    [Trait("Req", "UI-A11Y-033")]
    [Trait("Req", "UI-A11Y-034")]
    public async Task MenuMatchesSpec()
    {
        await using var h = await HeadlessHarness.StartAsync();

        var expected = Flatten(SpecTree(), 0);
        var actual = Flatten(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>(), 0);

        Assert.True(expected.Count > 200, $"spec not read: {expected.Count} rows");
        Assert.Equal(expected, actual);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-004")]
    [Trait("Req", "UI-MENU-001")]
    public async Task LaterPhaseItemsAreVisibleAndDisabled()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var items = AllItems(h).ToList();
        var later = items.Where(i => Definition(i)?.Phase > 1).ToList();

        Assert.NotEmpty(later);
        Assert.All(later, i =>
        {
            Assert.True(i.IsVisible, AutomationIds.Get(i));
            Assert.False(i.Command!.CanExecute(i.CommandParameter), AutomationIds.Get(i));
        });
        Assert.All(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<MenuItem>(), top => Assert.True(top.IsEnabled));
        Assert.False(Item(h, AutomationIds.SubMenu("Edit", "CodeInsight")).IsEnabled); // only P3+ children
        Assert.False(Item(h, AutomationIds.Menu("project.build")).Command!.CanExecute(null));
        Assert.True(Item(h, AutomationIds.Menu("view.theme.dark")).Command!.CanExecute(null)); // P1 with handler
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-005")]
    public async Task ShortcutTextFollowsKeymap()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var file = Item(h, AutomationIds.TopMenu("File"));
        file.Open();
        Dispatcher.UIThread.RunJobs();
        var save = Item(h, AutomationIds.Menu("file.save"));

        Assert.Equal("Ctrl+S", ShownGesture(save));
        Assert.Null(save.HotKey); // display only: key routing owns the gestures (IDE-ARCH-043)
        Assert.Null(save.InputGesture);
        Assert.Equal("", ShownGesture(Item(h, AutomationIds.Menu("file.saveAs"))));

        h.Services.Keymap.SetGestures("file.save", [CoreGesture.Parse("Ctrl+Shift+F2"), CoreGesture.Parse("F2")]);
        Assert.Equal("Ctrl+Shift+F2", ShownGesture(save)); // first of several
        file.Close();

        var view = Item(h, AutomationIds.TopMenu("View"));
        view.Open();
        Dispatcher.UIThread.RunJobs();
        var zoom = Item(h, AutomationIds.SubMenu("View", "Zoom"));
        zoom.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ctrl+=", ShownGesture(Item(h, AutomationIds.Menu("view.zoomIn")))); // UI-KEY-001 spelling, not the toolkit's
        Assert.Equal("Ctrl+-", ShownGesture(Item(h, AutomationIds.Menu("view.zoomOut"))));
        zoom.Close();
        view.Close();
    }

    /// <summary>The shortcut text the menu item's template shows.</summary>
    private static string ShownGesture(MenuItem item)
    {
        var text = item.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Name == "PART_InputGestureText");
        Assert.NotNull(text);
        Assert.True(string.IsNullOrEmpty(text.Text) || text.IsEffectivelyVisible, $"{AutomationIds.Get(item)}: shortcut text hidden");
        return text.Text ?? "";
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-007")]
    public async Task ExclusiveGroupsShowTheActiveEntry()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = "light");
        var light = Item(h, AutomationIds.Menu("view.theme.light"));
        var dark = Item(h, AutomationIds.Menu("view.theme.dark"));

        Assert.Equal(MenuItemToggleType.Radio, light.ToggleType);
        Assert.Equal(MenuItemToggleType.CheckBox, Item(h, AutomationIds.Menu("view.statusBar")).ToggleType);
        Assert.True(light.IsChecked);

        await h.ExecuteAsync("view.theme.dark");

        Assert.True(dark.IsChecked);
        Assert.False(light.IsChecked);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-009")]
    [Trait("Req", "IDE-P1-131")]
    public async Task RecentListsAreGenerated()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var recent = h.Services.RecentItems;
        var openRecent = Item(h, AutomationIds.SubMenu("File", "OpenRecent"));
        Assert.False(openRecent.IsEnabled); // no recent item yet

        const string longPath = @"C:\Users\someone\Documents\FreexComp Projects\A very long folder name\Hello\Hello.pproj";
        recent.Add(RecentKind.Project, @"C:\p\One\One.pproj");
        recent.Add(RecentKind.Project, longPath);
        for (var n = 1; n <= 12; n++)
            recent.Add(RecentKind.File, $@"C:\w\f{n}.pas");
        recent.SetPinned(RecentKind.File, @"C:\w\f1.pas", true);

        var file = Item(h, AutomationIds.TopMenu("File"));
        file.Open();
        Dispatcher.UIThread.RunJobs();
        openRecent = Item(h, AutomationIds.SubMenu("File", "OpenRecent"));
        Assert.True(openRecent.IsEnabled);
        var rows = Flatten(openRecent.Items.Cast<object?>(), 0);

        // Projects (pinned first, then newest), files (max 10), Clear; the first nine get digit mnemonics.
        string[] files = [@"C:\w\f1.pas", .. Enumerable.Range(4, 9).Reverse().Select(n => $@"C:\w\f{n}.pas")];
        Assert.Equal(
            [
                AutomationIds.Menu("file.openRecentProject", longPath) + @"|&1 C:\Users\…\A very long folder name\Hello\Hello.pproj",
                AutomationIds.Menu("file.openRecentProject", @"C:\p\One\One.pproj") + @"|&2 C:\p\One\One.pproj",
                "---",
                .. files.Select((f, i) => AutomationIds.Menu("file.openRecentFile", f) + "|" + (i < 7 ? $"&{i + 3} " : "") + f),
                "---",
                AutomationIds.Menu("file.clearRecent") + "|&Clear Recent Lists",
            ],
            rows);
        var longItem = Item(h, AutomationIds.Menu("file.openRecentProject", longPath));
        Assert.Equal(longPath, ToolTip.GetTip(longItem));
        Assert.True(((string)longItem.Header!).Length <= 60 + 3);
        Assert.True(longItem.IsEffectivelyEnabled);

        // The lists follow the service: clearing leaves the pinned file.
        await h.ExecuteAsync("file.clearRecent");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            [AutomationIds.Menu("file.openRecentFile", @"C:\w\f1.pas") + @"|&1 C:\w\f1.pas", "---", AutomationIds.Menu("file.clearRecent") + "|&Clear Recent Lists"],
            Flatten(openRecent.Items.Cast<object?>(), 0));
        Assert.True(openRecent.IsEnabled);
        openRecent.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Item(h, AutomationIds.Menu("file.clearRecent")).IsEffectivelyEnabled); // only a pinned entry left
        Assert.True(Item(h, AutomationIds.Menu("file.openRecentFile", @"C:\w\f1.pas")).IsEffectivelyEnabled);
        openRecent.Close();
        file.Close();
    }

    /// <summary>P1 menu commands whose "Enabled when" is "always" (P1 has no IDE state other than Idle, so "idle" too).</summary>
    private static readonly string[] AlwaysEnabled =
    [
        "file.newProject", "file.newFile", "file.newUnit", "file.open", "file.openProject",
        "view.projectManager", "view.welcomePage", "view.toolbar.standard", "view.statusBar",
        "view.theme.light", "view.theme.dark", "view.theme.system",
        "tools.options", "tools.keyMappings", "tools.openLogFolder", "tools.openSettingsFolder",
        "window.nextPane", "window.previousPane", "window.layout.edit", "window.resetLayout",
        "window.focusEditor", // menus.md says "doc"; UI-A11Y-004 / I-024: Alt+0 focuses the empty document area too
        "help.keyboardShortcuts", "help.licenses", "help.about",
        "view.showWhitespace", // I-055: the setting editor.showWhitespace
    ];

    /// <summary>P1 menu commands that still have no handler, with the task that adds it (and its condition here).</summary>
    private static readonly Dictionary<string, string> Pending = new()
    {
        ["file.exit"] = "I-086",
    };

    /// <summary>
    /// I-048 / I-049: enabled in every writable Object Pascal document ("text and writable"; the comment toggles need the
    /// language's comments; edit.indent without a multi-line selection inserts indentation like Tab). I-058: Replace
    /// ("text and writable").
    /// </summary>
    private static readonly string[] PascalTextCommands =
    [
        "edit.duplicateLine", "edit.deleteLine", "edit.trimTrailingWhitespace", "edit.toggleLineComment", "edit.toggleBlockComment",
        "edit.indent", "edit.outdent", "search.replace",
    ];

    /// <summary>I-057 / I-058: find works in every text document, the read-only generated ones too (UI-DLG-105).</summary>
    private static readonly string[] FindCommands = ["search.find", "search.findNext", "search.findPrevious", "search.findSelectionNext"];

    /// <summary>
    /// I-052, I-055: enabled in every text document, read-only too ("text"); zoom in/out while the font size is within
    /// 6…72 (Reset Zoom needs a zoom, see <see cref="Conditional"/>). I-057 / I-058: the find commands ("text"; Find Next /
    /// Previous without a search text open the find panel, UI-DLG-104). I-059: Go to Line ("text"; files and untitled
    /// documents, not the generated ones).
    /// </summary>
    private static readonly string[] TextCommands =
        ["search.goToMatchingBracket", "view.wordWrap", "view.zoomIn", "view.zoomOut", "search.goToLine", .. FindCommands];

    /// <summary>I-048: these also depend on the caret (first / last touched line, a selection or a word under the caret).</summary>
    private static readonly string[] CaretCommands = ["edit.moveLineUp", "edit.moveLineDown", "edit.upperCase", "edit.lowerCase"];

    /// <summary>P1 menu commands with a document / project / focus condition: the scenarios below list when they are enabled.</summary>
    private static readonly string[] Conditional =
    [
        "file.clearRecent", "file.save", "file.saveAs", "file.saveAll", "file.revert", "file.close", "file.closeAll",
        "file.closeProject", "edit.undo", "edit.redo", "edit.cut", "edit.copy", "edit.paste", "edit.delete", "edit.selectAll",
        "edit.convertLineEndings:crlf", "edit.convertLineEndings:lf", "edit.fold", "edit.unfold", "edit.foldAll", "edit.unfoldAll",
        "project.addNewUnit", "project.addExistingFile", "project.removeFile", "project.viewSource",
        "window.nextDocument", "window.previousDocument", "window.documents", "window.closeToolWindow", "view.zoomReset",
        "search.navigateBack", "search.navigateForward", // I-060: an earlier / a later navigation-history entry
        .. PascalTextCommands, .. CaretCommands, .. TextCommands,
    ];

    private static readonly string[] FoldCommands = ["edit.fold", "edit.unfold", "edit.foldAll", "edit.unfoldAll"];

    private static readonly string[] FoldAndCaretCommands = [.. FoldCommands, .. CaretCommands];

    /// <summary>Every P1 item of the main menu as <c>id</c> or <c>id:argument</c> (dynamic lists excluded).</summary>
    private static IEnumerable<string> P1MenuCommands(IEnumerable<Core.Menus.MenuNode> nodes) =>
        nodes.SelectMany(n => n switch
        {
            Core.Menus.SubmenuNode s => P1MenuCommands(s.Children),
            Core.Menus.CommandMenuNode c when CommandCatalog.All.Single(d => d.Id == c.CommandId).Phase == 1 =>
                [c.Argument is null ? c.CommandId : c.CommandId + ":" + c.Argument],
            _ => [],
        });

    private static bool CanExecute(HeadlessHarness h, string key)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? h.Services.Commands.CanExecute(key) : h.Services.Commands.CanExecute(key[..colon], key[(colon + 1)..]);
    }

    /// <summary>Exactly <paramref name="enabled"/> of the conditional commands are enabled (<paramref name="ignore"/> unchecked); "always" ones are.</summary>
    private static void AssertEnabled(HeadlessHarness h, string scenario, string[] enabled, params string[] ignore)
    {
        Dispatcher.UIThread.RunJobs();
        var actual = Conditional.Except(ignore).Where(k => CanExecute(h, k)).Order(StringComparer.Ordinal);
        Assert.Equal(scenario + ": " + string.Join(" ", enabled.Except(ignore).Order(StringComparer.Ordinal)), scenario + ": " + string.Join(" ", actual));
        Assert.All(AlwaysEnabled, k => Assert.True(CanExecute(h, k), scenario + ": " + k));
    }

    private static AvaloniaEdit.TextEditor FocusEditor(HeadlessHarness h, string fileName)
    {
        var editor = h.Find<AvaloniaEdit.TextEditor>("Editor." + fileName);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-010")]
    [Trait("Req", "UI-MENU-006")]
    public async Task EnabledStateFollowsConditions()
    {
        // Every P1 menu command is classified; pending ones really have no handler yet (their task must add a row here).
        var all = P1MenuCommands(Core.Menus.MainMenuDefinition.TopLevel).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(all, AlwaysEnabled.Concat(Pending.Keys).Concat(Conditional).Order(StringComparer.Ordinal));

        const string unit = "unit A;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nprocedure P;\r\nbegin\r\nend;\r\n\r\nend.\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = unit;
            o.InitialFiles["B.txt"] = "read only\r\n";
        });
        Assert.All(Pending, p => Assert.False(h.Services.Commands.IsHandled(p.Key), $"{p.Key} has a handler now ({p.Value}): classify it"));

        // No document, no project.
        AssertEnabled(h, "empty", []);

        // A clean file document with the caret inside the routine P.
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"))).Success);
        var a = FocusEditor(h, "A.pas");
        a.TextArea.Caret.Line = 8;
        // IDE-ED-038 (I-054): Cut and Copy without a selection take the caret line.
        string[] cleanA = ["file.clearRecent", "file.saveAs", "file.close", "file.closeAll", "edit.cut", "edit.copy", "edit.paste",
            "edit.delete", "edit.selectAll", "edit.convertLineEndings:lf", "edit.fold", "edit.foldAll", "window.documents",
            .. PascalTextCommands, .. TextCommands];
        AssertEnabled(h, "clean", [.. cleanA, .. CaretCommands]); // the caret is on "begin" of line 8

        // I-055: a zoomed document can reset its zoom.
        await h.ExecuteAsync("view.zoomIn");
        AssertEnabled(h, "zoomed", [.. cleanA, .. CaretCommands, "view.zoomReset"]);
        await h.ExecuteAsync("view.zoomReset");

        // I-058: Find Next / Previous do not depend on a search text (without one they open the panel).
        h.Services.Find.SearchText = "begin";
        AssertEnabled(h, "search text", [.. cleanA, .. CaretCommands]);
        h.Services.Find.SearchText = "";

        a.Select(0, 4);
        AssertEnabled(h, "selection", [.. cleanA, "edit.moveLineDown", "edit.upperCase", "edit.lowerCase"],
            FoldCommands); // the caret left the routine; line 1 cannot move up

        // Modified: Save, Save All, Revert, Undo.
        h.Type("x");
        string[] modifiedA = [.. cleanA, "file.save", "file.saveAll", "file.revert", "edit.undo"];
        AssertEnabled(h, "modified", modifiedA, FoldAndCaretCommands);
        await h.ExecuteAsync("edit.undo");
        a.TextArea.ClearSelection(); // undo selects the restored text
        AssertEnabled(h, "undone", [.. cleanA, "edit.redo"], FoldAndCaretCommands);

        // Delete removes the selection, else the character after the caret.
        a.Select(0, 4);
        await h.ExecuteAsync("edit.delete");
        Assert.StartsWith(" A;\r\n", a.Text, StringComparison.Ordinal);
        await h.ExecuteAsync("edit.delete");
        Assert.StartsWith("A;\r\n", a.Text, StringComparison.Ordinal);
        h.Type("x");

        // A read-only document: nothing that writes (Cut, Paste, Convert, Undo), Copy and Save As still work.
        var bPath = Path.Combine(h.TempDir, "B.txt");
        File.SetAttributes(bPath, FileAttributes.ReadOnly);
        try
        {
            Assert.True((await h.Services.Documents.OpenAsync(bPath)).Success);
            var b = FocusEditor(h, "B.txt");
            await h.ExecuteAsync("edit.delete"); // refused: the document is read-only
            Assert.Equal(("read only\r\n", Core.Resources.Strings.Msg_Editor_ReadOnlyEdit), (b.Text, h.Status.Message));
            b.Select(0, 4);
            AssertEnabled(h, "read-only",
            [
                "file.clearRecent", "file.saveAs", "file.saveAll", "file.close", "file.closeAll", "edit.copy", "edit.delete", "edit.selectAll",
                "window.nextDocument", "window.previousDocument", "window.documents", .. TextCommands,
                "search.navigateBack", // I-060: opening B.txt left A.pas behind in the navigation history
            ]);
        }
        finally
        {
            File.SetAttributes(bPath, FileAttributes.Normal);
        }

        // An untitled unit can be saved (never saved) but not reverted.
        await h.ExecuteAsync("file.newUnit");
        FocusEditor(h, "Unit1.pas");
        AssertEnabled(h, "untitled",
        [
            "file.clearRecent", "file.save", "file.saveAs", "file.saveAll", "file.close", "file.closeAll", "edit.cut", "edit.copy",
            "edit.paste", "edit.delete", "edit.selectAll", "edit.convertLineEndings:lf", "window.nextDocument", "window.previousDocument", "window.documents",
            "search.navigateBack", .. PascalTextCommands, .. TextCommands,
        ], FoldAndCaretCommands);

        // A generated document is no file: no Save / Save As / Revert / text commands; Select All and Copy work.
        await h.ExecuteAsync("help.keyboardShortcuts");
        var shortcuts = h.Find<AvaloniaEdit.TextEditor>("Document.KeyboardShortcuts");
        shortcuts.TextArea.Focus();
        string[] generated = ["file.clearRecent", "file.saveAll", "file.close", "file.closeAll", "edit.selectAll",
            "window.nextDocument", "window.previousDocument", "window.documents", "search.navigateBack", .. FindCommands];
        AssertEnabled(h, "generated", generated);
        shortcuts.Select(0, 8);
        AssertEnabled(h, "generated selection", [.. generated, "edit.copy"]);
        await h.ExecuteAsync("edit.copy");
        Assert.Equal(shortcuts.Text[..8], await TopLevel.GetTopLevel(h.Window)!.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-010")]
    public async Task EnabledStateFollowsProjectConditions()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"P\P.pproj"] = """{ "formatVersion": 1, "name": "P", "mainSource": "P.pas", "files": [ { "path": "P.pas" }, { "path": "U.pas" } ] }""";
            o.InitialFiles[@"P\P.pas"] = "program P;\r\nuses U;\r\nbegin\r\nend.\r\n";
            o.InitialFiles[@"P\U.pas"] = "unit U;\r\ninterface\r\nimplementation\r\nend.\r\n";
        });
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "P", "P.pproj")));
        FocusEditor(h, "P.pas");
        string[] project = ["file.clearRecent", "file.saveAs", "file.close", "file.closeAll", "file.closeProject", "edit.cut", "edit.copy",
            "edit.paste",
            "edit.delete", "edit.selectAll", "edit.convertLineEndings:lf", "project.addNewUnit", "project.addExistingFile", "project.viewSource",
            "window.documents", .. PascalTextCommands, .. TextCommands];

        // The main source cannot be removed from the project; another member can.
        AssertEnabled(h, "main source", project, FoldAndCaretCommands);
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "P", "U.pas"))).Success);
        FocusEditor(h, "U.pas");
        AssertEnabled(h, "member", [.. project, "project.removeFile", "window.nextDocument", "window.previousDocument", "search.navigateBack"],
            FoldAndCaretCommands); // I-060: opening U left P.pas behind in the navigation history

        // Hide Active Tool Window needs a tool window that had the focus.
        Assert.False(h.Services.Commands.CanExecute("window.closeToolWindow"));
        h.Press("Alt+1"); // view.projectManager focuses its tree
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Tool.ProjectManager", h.FocusedAutomationId, StringComparison.Ordinal);
        Assert.True(h.Services.Commands.CanExecute("window.closeToolWindow"));

        // Opening the folders: IDE-SET-093.
        await h.ExecuteAsync("tools.openLogFolder");
        await h.ExecuteAsync("tools.openSettingsFolder");
        var shell = (FreexComp.Ide.TestSupport.FakeShellService)h.Services.Shell;
        Assert.Equal(["OpenFolder " + h.Services.UserPaths.LogDirectory, "OpenFolder " + h.Services.UserPaths.RoamingDirectory], shell.Calls);
        Assert.True(Directory.Exists(h.Services.UserPaths.LogDirectory));
        Assert.True(h.Services.Commands.Get("window.layout.edit").IsChecked);
    }

    private static CommandDefinition? Definition(MenuItem item) =>
        item.Command is IdeCommand command ? command.Definition : null;

    private static MenuItem Item(HeadlessHarness h, string id) =>
        AllItems(h).FirstOrDefault(i => AutomationIds.Get(i) == id) ?? new MenuItem();

    private static IEnumerable<MenuItem> AllItems(HeadlessHarness h) => Descendants(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>());

    private static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items) =>
        items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Descendants(i.Items.Cast<object?>())));

    private static List<string> Flatten(IEnumerable<object?> items, int depth) =>
        [.. items.SelectMany(i => i switch
        {
            Separator => [Indent(depth) + "---"],
            MenuItem m => new[] { Indent(depth) + AutomationIds.Get(m) + "|" + ToSpecLabel((string)m.Header!) }
                .Concat(Flatten(m.Items.Cast<object?>(), depth + 1)),
            _ => [Indent(depth) + "?" + i],
        })];

    private static List<string> Flatten(IEnumerable<SpecNode> nodes, int depth) =>
        [.. nodes.SelectMany(n => new[] { Indent(depth) + n.Text }.Concat(Flatten(n.Children, depth + 1)))];

    private static string Indent(int depth) => new(' ', depth * 2);

    /// <summary>Avalonia access text (<c>_</c>) back to the spec's <c>&amp;</c> notation.</summary>
    private static string ToSpecLabel(string header) => header.Replace("__", "\0", StringComparison.Ordinal).Replace('_', '&').Replace('\0', '_');

    /// <summary>menus.md §2–§11 as a tree; dynamic lists are empty in Phase 1 and separators they leave dangling are dropped.</summary>
    private static List<SpecNode> SpecTree()
    {
        var path = SpecTables.DocPath("spec/ui/menus.md");
        var top = new List<SpecNode>();
        foreach (var (section, key, label) in TopMenus)
        {
            var menu = new SpecNode($"{AutomationIds.TopMenu(key)}|{label}", []);
            SpecNode? submenu = null;
            foreach (var row in SpecTables.Read(path, section))
            {
                var cell = row["Label"];
                var isChild = cell.StartsWith('├') || cell.StartsWith('└');
                var text = cell.TrimStart('├', '└', ' ').Trim();
                var command = row["Command id"];
                SpecNode? node = text switch
                {
                    "─" => new SpecNode("---", []),
                    _ when text.StartsWith("*(", StringComparison.Ordinal) => null, // dynamic list, empty in Phase 1
                    _ when command.StartsWith("submenu ", StringComparison.Ordinal) =>
                        new SpecNode($"{command["submenu ".Length..]}|{SubmenuLabel().Replace(text, "")}", []),
                    _ => new SpecNode($"Menu.{command}|{RunLabel().Replace(text, "")}", []),
                };

                if (isChild)
                {
                    if (node is not null)
                    {
                        submenu!.Children.Add(node);
                    }

                    continue;
                }

                submenu = node is not null && command.StartsWith("submenu ", StringComparison.Ordinal) ? node : null;
                if (node is not null)
                {
                    menu.Children.Add(node);
                }
            }

            top.Add(menu);
        }

        Normalize(top);
        return top;
    }

    /// <summary>Drops leading, trailing and doubled separators on every level.</summary>
    private static void Normalize(List<SpecNode> nodes)
    {
        var result = new List<SpecNode>();
        foreach (var node in nodes)
        {
            Normalize(node.Children);
            if (node.Text == "---" && (result.Count == 0 || result[^1].Text == "---"))
            {
                continue;
            }

            result.Add(node);
        }

        if (result.Count > 0 && result[^1].Text == "---")
        {
            result.RemoveAt(result.Count - 1);
        }

        nodes.Clear();
        nodes.AddRange(result);
    }

    [GeneratedRegex(@"\s*▸\s*$")]
    private static partial Regex SubmenuLabel();

    [GeneratedRegex(@"\s*\(label .*\)$")]
    private static partial Regex RunLabel();

    private sealed record SpecNode(string Text, List<SpecNode> Children);
}
