using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Welcome page (welcome-page.md, UI-WIN-071).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class WelcomePageTests
{
    private const string Row = "Welcome.Recent.Row.";
    private const string FileRow = "Welcome.RecentFiles.Row.";

    /// <summary>The harness presets the setting to false so other tests start with an empty document area.</summary>
    private static void ShowAtStartup(HarnessOptions o) => o.Settings.Remove(SettingsCatalog.EnvironmentShowWelcomeOnStartup.Name);

    /// <summary><c>roaming/recent.json</c> with these projects (<c>lastOpened</c> as UTC stamps).</summary>
    private static void Recent(HarnessOptions o, params (string Path, string LastOpened, bool Pinned)[] projects)
    {
        var root = new JsonObject
        {
            ["formatVersion"] = 1,
            ["projects"] = new JsonArray([.. projects.Select(p => (JsonNode)new JsonObject
            {
                ["path"] = p.Path,
                ["lastOpened"] = p.LastOpened,
                ["pinned"] = p.Pinned,
            })]),
            ["files"] = new JsonArray(),
        };
        o.UserFiles[@"roaming\recent.json"] = Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static void Project(HarnessOptions o, string name)
    {
        o.InitialFiles[$@"{name}\{name}.pproj"] = $$"""{ "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas" }""";
        o.InitialFiles[$@"{name}\{name}.pas"] = $"program {name};\nbegin\nend.\n";
    }

    private static WelcomeDocumentViewModel Page(HeadlessHarness h) =>
        Assert.Single(h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>());

    private static async Task<WelcomeDocumentViewModel> CheckedPageAsync(HeadlessHarness h)
    {
        var page = Page(h);
        await h.WaitForAsync(() => !page.IsCheckingMissing, "missing-project check done");
        return page;
    }

    /// <summary>The visible rows' paths, top to bottom on screen.</summary>
    private static IReadOnlyList<string> RowPaths(HeadlessHarness h, string prefix = Row) =>
    [
        .. h.FindAll(prefix).OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible)
            .OrderBy(i => Avalonia.VisualExtensions.TranslatePoint(i, default, h.Window)!.Value.Y)
            .Select(i => AutomationIds.Get(i)![prefix.Length..]),
    ];

    /// <summary>The visible recent-file rows' paths, top to bottom on screen (IDE-WEL-024).</summary>
    private static IReadOnlyList<string> FileRowPaths(HeadlessHarness h) => RowPaths(h, FileRow);

    private static JsonArray RecentFilesOnDisk(HeadlessHarness h) =>
        (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(h.UserDir, "roaming", "recent.json")))!["files"]!;

    private static JsonArray RecentProjectsOnDisk(HeadlessHarness h) =>
        (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(h.UserDir, "roaming", "recent.json")))!["projects"]!;

    private static string Pinned(JsonNode entry) => $"{entry["path"]}|{entry["pinned"]}";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-071")]
    [Trait("Req", "UI-WIN-103")]
    [Trait("Req", "IDE-WEL-020")]
    [Trait("Req", "IDE-WEL-021")]
    [Trait("Req", "IDE-WEL-022")]
    [Trait("Req", "IDE-P1-015")]
    public async Task ShownOnFirstStart()
    {
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Project(o, "Hello");
        }))
        {
            // An empty user directory: the Welcome page is the active document, a tab titled Welcome.
            var page = Assert.IsType<WelcomeDocumentViewModel>(h.Services.Documents.ActiveDocument);
            Assert.Equal("Welcome", page.Title);
            Assert.Null(page.FilePath);
            Assert.False(page.IsModified);
            Assert.True(h.Find<Control>(AutomationIds.DocumentTab("Welcome")).IsEffectivelyVisible);
            Assert.True(h.Find<Control>("Welcome").IsEffectivelyVisible);
            Assert.False(h.Find<Control>(AutomationIds.DocumentAreaEmpty).IsVisible);
            Assert.Equal("FreexComp", h.Window.Title); // not a file: no subject (main-window.md §4)

            // Header, start tiles with their commands' current shortcuts, the empty recent list, the check box.
            var texts = h.Find<Control>("Welcome").GetLogicalDescendantsTexts();
            Assert.Contains("FreexComp", texts);
            Assert.Contains(page.VersionText, texts);
            Assert.StartsWith("Version ", page.VersionText, StringComparison.Ordinal);
            Assert.Contains("START", texts);
            Assert.Contains("RECENT PROJECTS", texts);
            Assert.Equal(["New Project…", "Ctrl+Shift+N"], h.Find<Button>("Welcome.NewProject").GetLogicalDescendantsTexts());
            Assert.Equal(["Open Project…", "Ctrl+Shift+O"], h.Find<Button>("Welcome.OpenProject").GetLogicalDescendantsTexts());
            Assert.Equal(["Open File…", "Ctrl+O"], h.Find<Button>("Welcome.OpenFile").GetLogicalDescendantsTexts());
            Assert.Contains("No recent projects", texts);
            Assert.Contains("Projects you open appear here.", texts);
            Assert.False(h.Find<Control>("Welcome.Recent.List").IsVisible);
            Assert.False(h.Find<Control>("Welcome.Recent.Filter").IsVisible);
            Assert.True(h.Find<CheckBox>("Welcome.ShowOnStartup").IsChecked);

            // UI-WIN-040: no editor fields; edit.* have no target.
            Assert.False(h.Services.Commands.CanExecute("edit.undo"));
            Assert.False(h.Services.Commands.CanExecute("file.save"));

            // The Open File tile runs file.open.
            h.FileDialogs.NextCancel();
            h.Click("Welcome.OpenFile");
            await h.WaitForAsync(() => h.FileDialogs.Requests.Count == 1, "Open File picker");

            // Opening a project keeps the Welcome tab open but inactive; it is not part of the session.
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Hello", "Hello.pproj")));
            Assert.Contains(page, h.Services.Documents.Documents);
            Assert.NotSame(page, h.Services.Documents.ActiveDocument);

            // view.welcomePage activates the open tab instead of opening a second one.
            await h.ExecuteAsync("view.welcomePage");
            Assert.Same(page, h.Services.Documents.ActiveDocument);
            Assert.Single(h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>());

            // It closes like any tab, without a prompt; view.welcomePage opens it again.
            await h.ExecuteAsync("file.close");
            Assert.Empty(h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>());
            Assert.Empty(h.Messages.Requests);
            await h.ExecuteAsync("view.welcomePage");
            Assert.IsType<WelcomeDocumentViewModel>(h.Services.Documents.ActiveDocument);
            await h.Services.Projects.CloseAsync();
            Assert.IsType<WelcomeDocumentViewModel>(Assert.Single(h.Services.Documents.Documents));
        }

        // environment.showWelcomeOnStartup = false: the empty document area.
        await using (var h = await HeadlessHarness.StartAsync(o => o.Settings[SettingsCatalog.EnvironmentShowWelcomeOnStartup.Name] = false))
        {
            Assert.Empty(h.Services.Documents.Documents);
            Assert.True(h.Find<Control>(AutomationIds.DocumentAreaEmpty).IsVisible);
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-001")]
    [Trait("Req", "IDE-WEL-002")]
    [Trait("Req", "IDE-WEL-003")]
    [Trait("Req", "IDE-WEL-009")]
    public async Task RecentListOrdersPinnedFirst()
    {
        string a = @"C:\w\A\A.pproj", b = @"C:\w\B\B.pproj", c = @"C:\w\C\C.pproj", d = @"D:\x\D\D.pproj";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Recent(o, (a, "2020-11-10T10:00:00Z", false), (c, "2020-11-01T10:00:00Z", true), (b, "2020-11-12T10:00:00Z", false),
                (d, "2020-10-01T10:00:00Z", true));
        });
        var page = await CheckedPageAsync(h);

        // Pinned first, then unpinned; each group newest first.
        Assert.Equal([c, d, b, a], RowPaths(h));
        Assert.Equal([c, d, b, a], page.Projects.Select(p => p.Path));

        // Row content: pin state, name, path; tooltips of the pin toggle.
        var rowC = page.Projects[0];
        Assert.Equal(("C", true), (rowC.Name, rowC.IsPinned));
        Assert.Equal(["★", "C (missing)", c], h.Find<ListBoxItem>(Row + c).GetLogicalDescendantsTexts().Take(3)); // C:\w does not exist
        Assert.Equal("Unpin", ToolTip.GetTip(h.Find<Button>(Row + c + ".Pin")));
        Assert.Equal("Pin to top", ToolTip.GetTip(h.Find<Button>(Row + a + ".Pin")));
        Assert.Equal("Remove from list", ToolTip.GetTip(h.Find<Button>(Row + a + ".Remove")));
        Assert.Equal("☆", page.Projects[2].PinGlyph);

        // The list follows the recent items service: a project opened now leads the unpinned group, dated today.
        var e = Path.Combine(h.TempDir, "E", "E.pproj");
        h.Services.RecentItems.Add(RecentKind.Project, e);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([c, d, e, b, a], RowPaths(h));
        Assert.Equal("today " + h.Clock.Now.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            page.Projects[2].DateText);
        Assert.Contains(page.Projects[2].DateText, h.Find<ListBoxItem>(Row + e).GetLogicalDescendantsTexts());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-005")]
    [Trait("Req", "IDE-WEL-008")]
    public async Task OpenRecentByKeyboard()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Project(o, "Hello");
            Project(o, "Other");
        });
        // The real paths are only known once the harness has its temp folder.
        var hello = Path.Combine(h.TempDir, "Hello", "Hello.pproj");
        var other = Path.Combine(h.TempDir, "Other", "Other.pproj");
        h.Services.RecentItems.Add(RecentKind.Project, hello);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.Project, other);
        await CheckedPageAsync(h);
        Assert.Equal([other, hello], RowPaths(h));

        // Tab order: the three tiles, (no filter with ≤ 5 entries) the list as one stop, the check box.
        h.Find<Button>("Welcome.NewProject").Focus(NavigationMethod.Tab);
        Assert.Equal("Welcome.NewProject", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Welcome.OpenProject", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Welcome.OpenFile", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal(Row + other, h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Welcome.ShowOnStartup", h.FocusedAutomationId);
        h.Press("Shift+Tab");
        Assert.Equal(Row + other, h.FocusedAutomationId);

        // Down moves to the next row, Enter opens it; the Welcome tab stays open behind the project.
        h.Press("Down");
        Assert.Equal(Row + hello, h.FocusedAutomationId);
        h.Press("Enter");
        // The project is set before its main source has been read (in the background): wait for the document too.
        await h.WaitForAsync(() => h.Services.Projects.Project?.Name == "Hello" && h.Services.Documents.ActiveDocument is TextDocumentViewModel,
            "Hello and its main source opened");
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "Hello.pas"), h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Single(h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>());

        // A click on a row opens it too.
        await h.ExecuteAsync("view.welcomePage");
        await h.WaitForAsync(() => RowPaths(h).FirstOrDefault() == hello, "Hello leads the list");
        h.Click(Row + other);
        await h.WaitForAsync(() => h.Services.Projects.Project?.Name == "Other" &&
            h.Services.Documents.ActiveDocument?.FilePath == Path.Combine(h.TempDir, "Other", "Other.pas"), "Other and its main source opened");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-006")]
    [Trait("Req", "IDE-WEL-008")]
    public async Task PinAndRemoveUpdateRecentJson()
    {
        string a = @"C:\w\A\A.pproj", b = @"C:\w\B\B.pproj", c = @"C:\w\C\C.pproj";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Recent(o, (a, "2026-11-12T10:00:00Z", false), (b, "2026-11-11T10:00:00Z", false), (c, "2026-11-10T10:00:00Z", false));
        });
        await CheckedPageAsync(h);
        Assert.Equal([a, b, c], RowPaths(h));

        // Clicking the pin toggle pins and re-sorts; recent.json is written at once.
       h.Click(Row + c + ".Pin");
        Assert.Equal([c, a, b], RowPaths(h));
        Assert.Equal([$"{c}|true", $"{a}|false", $"{b}|false"], RecentProjectsOnDisk(h).Select(e => Pinned(e!)));
        Assert.Equal("Unpin", ToolTip.GetTip(h.Find<Button>(Row + c + ".Pin")));
        Assert.Equal(Row + c, h.FocusedAutomationId); // focus stays on the toggled row

        // Keyboard: P toggles the pin of the focused row, Delete removes it; focus goes to the nearest remaining row.
        h.Press("Down");
        Assert.Equal(Row + a, h.FocusedAutomationId);
        h.Press("P");
        Assert.Equal([a, c, b], RowPaths(h));
        Assert.Equal(Row + a, h.FocusedAutomationId);
        Assert.Equal([$"{a}|true", $"{c}|true", $"{b}|false"], RecentProjectsOnDisk(h).Select(e => Pinned(e!)));
        h.Press("Delete");
        Assert.Equal([c, b], RowPaths(h));
        Assert.Equal(Row + c, h.FocusedAutomationId);
        Assert.Equal([$"{c}|true", $"{b}|false"], RecentProjectsOnDisk(h).Select(e => Pinned(e!)));
        Assert.Empty(h.Messages.Requests); // no confirmation

        // The remove button shows on hover and removes without asking.
        var remove = h.Find<Button>(Row + b + ".Remove");
        Assert.False(remove.IsVisible);
        h.Hover(Row + b);
        Assert.True(remove.IsVisible);
        h.Click(Row + b + ".Remove");
        Assert.Equal([c], RowPaths(h));
        Assert.Equal([$"{c}|true"], RecentProjectsOnDisk(h).Select(e => Pinned(e!)));
        Assert.Equal(Row + c, h.FocusedAutomationId);
        Assert.Null(h.Services.Projects.Project); // neither the pin toggle nor remove opened anything
    }

    /// <summary>
    /// IDE-WEL-008: the row context menu — Open (<c>file.openRecentProject</c>), Open Containing Folder
    /// (<c>file.showInExplorer</c> with the path), Pin/Unpin and Remove from List (actions, not commands).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-008")]
    public async Task RowContextMenu()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Project(o, "Hello");
        });
        var hello = Path.Combine(h.TempDir, "Hello", "Hello.pproj");
        var gone = Path.Combine(h.TempDir, "Gone", "Gone.pproj");
        h.Services.RecentItems.Add(RecentKind.Project, gone);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.Project, hello);
        var page = await CheckedPageAsync(h);
        var shell = (FreexComp.Ide.TestSupport.FakeShellService)h.Services.Shell;

        List<MenuItem> MenuOf(string path)
        {
            h.RightClick(Row + path);
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<ContextMenu>(h.Find<ListBox>("Welcome.Recent.List").ContextMenu);
            var items = menu.Items.OfType<MenuItem>().ToList();
            menu.Close();
            return items;
        }

        static void Choose(MenuItem item)
        {
            Assert.True(item.IsEffectivelyEnabled, AutomationIds.Get(item) + " is disabled");
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var items = MenuOf(hello);
        Assert.Equal(["Context.file.openRecentProject:" + hello, "Context.file.showInExplorer:" + hello, "Context.welcome.pin", "Context.welcome.remove"],
            items.Select(AutomationIds.Get));
        Assert.Equal(["_Open", "Open Containing _Folder", "_Pin to Top", "_Remove from List"], items.Select(i => (string)i.Header!));

        // Open Containing Folder shows the project file in the file manager.
        Choose(items[1]);
        Assert.Equal(["RevealFile " + hello], shell.Calls);

        // Pin / Unpin toggles the pin; the label follows.
        Choose(items[2]);
        Assert.True(page.Projects.Single(p => p.Path == hello).IsPinned);
        items = MenuOf(hello);
        Assert.Equal("Context.welcome.unpin", AutomationIds.Get(items[2]));
        Assert.Equal("Un_pin", items[2].Header);
        Choose(items[2]);
        Assert.False(page.Projects.Single(p => p.Path == hello).IsPinned);

        // A missing project has no folder to show; Remove from List removes it without asking.
        items = MenuOf(gone);
        Assert.False(items[1].IsEffectivelyEnabled);
        Choose(items[3]);
        Assert.Equal([hello], RowPaths(h));
        Assert.Empty(h.Messages.Requests);

        // Open opens the project.
        Choose(MenuOf(hello)[0]);
        await h.WaitForAsync(() => h.Services.Projects.Project is not null, "project opened");
        Assert.Equal(hello, h.Services.Projects.Project!.FilePath);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-004")]
    [Trait("Req", "IDE-WEL-005")]
    [Trait("Req", "IDE-SET-073")]
    public async Task MissingProjectIsMarkedAndPrompts()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Project(o, "Hello");
        });
        var hello = Path.Combine(h.TempDir, "Hello", "Hello.pproj");
        var gone = Path.Combine(h.TempDir, "Gone", "Gone.pproj");
        h.Services.RecentItems.Add(RecentKind.Project, hello);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.Project, gone);
        var page = await CheckedPageAsync(h);

        // The missing project is marked: " (missing)" after the name, name in text.disabled.
        var goneRow = page.Projects.Single(p => p.Path == gone);
        Assert.True(goneRow.IsMissing);
        Assert.False(page.Projects.Single(p => p.Path == hello).IsMissing);
        var name = h.Find<TextBlock>(Row + gone + ".Name");
        Assert.Equal("Gone (missing)", name.Text);
        Assert.True(h.Window.TryFindResource("FxcTextDisabledBrush", h.Window.ActualThemeVariant, out var disabled));
        Assert.Same(disabled, name.Foreground);
        Assert.Equal("Hello", h.Find<TextBlock>(Row + hello + ".Name").Text);

        // Activating it asks Msg.File.RecentNotFound; Remove deletes the entry.
        h.Messages.Answer("Msg.File.RecentNotFound", "Remove");
        h.Find<ListBoxItem>(Row + gone).Focus(NavigationMethod.Tab);
        h.Press("Enter");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 1, "RecentNotFound asked");
        Assert.Equal([gone], h.Messages.Requests[0].Args);
        await h.WaitForAsync(() => RowPaths(h).Count == 1, "entry removed");
        Assert.Equal([hello], RowPaths(h));
        Assert.Null(h.Services.Projects.Project);

        // The check runs again when the main window is activated.
        File.Delete(hello);
        page.OnWindowActivated();
        await h.WaitForAsync(() => page.Projects.Single().IsMissing, "Hello now missing");
        Assert.Equal("Hello (missing)", h.Find<TextBlock>(Row + hello + ".Name").Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-007")]
    [Trait("Req", "IDE-WEL-022")]
    public async Task FilterShowsEmptyState()
    {
        string[] names = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta"];
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            Recent(o, [.. names.Select((n, i) => ($@"C:\p\{n}\{n}.pproj", $"2026-11-{10 + i:00}T10:00:00Z", false))]);
        });
        var page = await CheckedPageAsync(h);
        Assert.Equal(7, RowPaths(h).Count);

        // More than five entries: the filter box is shown; Ctrl+F focuses it.
        var filter = h.Find<TextBox>("Welcome.Recent.Filter");
        Assert.True(filter.IsEffectivelyVisible);
        Assert.Equal("Filter…", filter.PlaceholderText);
        h.Find<ListBoxItem>(Row + @"C:\p\Eta\Eta.pproj").Focus(NavigationMethod.Tab);
        h.Press("Ctrl+F");
        Assert.Equal("Welcome.Recent.Filter", h.FocusedAutomationId);

        // Case-insensitive substring of name or path.
        h.Type("ETA");
        Assert.Equal([@"C:\p\Eta\Eta.pproj", @"C:\p\Zeta\Zeta.pproj", @"C:\p\Beta\Beta.pproj"], RowPaths(h));
        filter.Text = @"p\gam";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([@"C:\p\Gamma\Gamma.pproj"], RowPaths(h));

        // No match: Msg.Welcome.FilterEmpty in place of the list.
        filter.Text = "zzz";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(RowPaths(h));
        Assert.False(h.Find<Control>("Welcome.Recent.List").IsVisible);
        Assert.Contains("No recent project matches zzz", h.Find<Control>("Welcome").GetLogicalDescendantsTexts());
        Assert.DoesNotContain("No recent projects", h.Find<Control>("Welcome").GetLogicalDescendantsTexts());

        // Five entries or fewer: no filter box, and its text no longer applies.
        foreach (var n in names.Take(2))
            h.Services.RecentItems.Remove(RecentKind.Project, $@"C:\p\{n}\{n}.pproj");
        Dispatcher.UIThread.RunJobs();
        Assert.False(filter.IsEffectivelyVisible);
        Assert.Equal("", page.FilterText);
        Assert.Equal(5, RowPaths(h).Count);

        // No recent projects at all: Msg.Welcome.NoRecent.
        foreach (var n in names.Skip(2))
            h.Services.RecentItems.Remove(RecentKind.Project, $@"C:\p\{n}\{n}.pproj");
        Dispatcher.UIThread.RunJobs();
        var texts = h.Find<Control>("Welcome").GetLogicalDescendantsTexts();
        Assert.Contains("No recent projects", texts);
        Assert.DoesNotContain("No recent project matches zzz", texts);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-020")]
    [Trait("Req", "IDE-SET-001")]
    public async Task ShowOnStartupCheckBoxWritesSetting()
    {
        await using var h = await HeadlessHarness.StartAsync(ShowAtStartup);
        var box = h.Find<CheckBox>("Welcome.ShowOnStartup");
        Assert.Equal("Show this page at start-up", box.Content);
        Assert.True(box.IsChecked);

        // Unchecking applies at once and is saved to settings.json.
        h.Click("Welcome.ShowOnStartup");
        Assert.False(h.Services.Settings.Get(SettingsCatalog.EnvironmentShowWelcomeOnStartup));
        await h.Services.Settings.FlushAsync();
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(h.UserDir, "roaming", "settings.json")))!;
        Assert.False(saved["environment.showWelcomeOnStartup"]!.GetValue<bool>());

        // A change made elsewhere (Options dialog) shows in the check box.
        h.Services.Settings.Set(SettingsCatalog.EnvironmentShowWelcomeOnStartup, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsChecked);
    }

    /// <summary>
    /// IDE-WEL-024: a second list "Recent files" under the recent projects, fed by the recent files of
    /// <c>recent.json</c> (the store of File › Open Recent): pinned first, then most recent first; the same row look
    /// (pin, name with extension, folder, date, remove); Enter or a click executes <c>file.openRecentFile</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-024")]
    public async Task RecentFilesListed_MostRecentFirst()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            o.InitialFiles[@"Notes\a.txt"] = "alpha";
            o.InitialFiles[@"Notes\b.pas"] = "unit b;";
            o.InitialFiles[@"Other\c.inc"] = "{ c }";
        });
        var page = await CheckedPageAsync(h);

        // No recent files: the section is not shown at all.
        Assert.Empty(page.Files);
        Assert.False(h.Find<Control>("Welcome.RecentFiles").IsVisible);

        string a = Path.Combine(h.TempDir, "Notes", "a.txt"), b = Path.Combine(h.TempDir, "Notes", "b.pas"),
            c = Path.Combine(h.TempDir, "Other", "c.inc");
        h.Services.RecentItems.Add(RecentKind.File, a);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.File, b);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.File, c);
        await h.WaitForAsync(() => FileRowPaths(h).Count == 3, "three recent files shown");

        // Most recent first, below the recent projects (whose list is still empty).
        Assert.Equal([c, b, a], FileRowPaths(h));
        Assert.True(h.Find<Control>("Welcome.RecentFiles").IsEffectivelyVisible);
        var texts = h.Find<Control>("Welcome").GetLogicalDescendantsTexts();
        Assert.Contains("RECENT FILES", texts);
        Assert.Contains("No recent projects", texts);
        Assert.True(Avalonia.VisualExtensions.TranslatePoint(h.Find<Control>("Welcome.RecentFiles"), default, h.Window)!.Value.Y
            > Avalonia.VisualExtensions.TranslatePoint(h.Find<Control>("Welcome.Recent.List"), default, h.Window)!.Value.Y);

        // Row look: pin, file name with extension, its folder, the date; tooltips as for projects.
        var today = "today " + h.Clock.Now.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Equal(today, page.Files[0].DateText);
        Assert.Equal(["☆", "b.pas", Path.Combine(h.TempDir, "Notes"), page.Files[1].DateText],
            h.Find<ListBoxItem>(FileRow + b).GetLogicalDescendantsTexts().Take(4));
        Assert.StartsWith("today ", page.Files[1].DateText, StringComparison.Ordinal);
        Assert.Equal("b.pas", h.Find<TextBlock>(FileRow + b + ".Name").Text);
        Assert.Equal("Pin to top", ToolTip.GetTip(h.Find<Button>(FileRow + b + ".Pin")));
        Assert.Equal("Remove from list", ToolTip.GetTip(h.Find<Button>(FileRow + b + ".Remove")));

        // Pinning moves the file to the top; recent.json is written at once; focus stays on the row.
        h.Click(FileRow + a + ".Pin");
        Assert.Equal([a, c, b], FileRowPaths(h));
        Assert.Equal([$"{a}|true", $"{c}|false", $"{b}|false"], RecentFilesOnDisk(h).Select(e => Pinned(e!)));
        Assert.Equal(FileRow + a, h.FocusedAutomationId);

        // Tab order: the start tiles, the file list as one stop (the empty project list is hidden), the check box.
        h.Find<Button>("Welcome.OpenFile").Focus(NavigationMethod.Tab);
        h.Press("Tab");
        Assert.Equal(FileRow + a, h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Welcome.ShowOnStartup", h.FocusedAutomationId);
        h.Press("Shift+Tab");
        Assert.Equal(FileRow + a, h.FocusedAutomationId);

        // Delete removes the focused row without asking; focus goes to the nearest remaining row.
        h.Press("Down");
        Assert.Equal(FileRow + c, h.FocusedAutomationId);
        h.Press("Delete");
        Assert.Equal([a, b], FileRowPaths(h));
        Assert.Equal(FileRow + b, h.FocusedAutomationId);
        Assert.Equal([$"{a}|true", $"{b}|false"], RecentFilesOnDisk(h).Select(e => Pinned(e!)));
        Assert.Empty(h.Messages.Requests);

        // The context menu: Open runs file.openRecentFile; the other items as for projects.
        var row = page.Files.Single(f => f.Path == a);
        Assert.Equal(["Context.file.openRecentFile:" + a, "Context.file.showInExplorer:" + a, "Context.welcome.unpin", "Context.welcome.remove"],
            FreexComp.Ide.Views.MenuBarBuilder.CreateContextMenu(page.RowContextMenu(row, _ => { }, _ => { })).Items.OfType<MenuItem>().Select(AutomationIds.Get));

        // Enter opens the file in an editor (file.openRecentFile); the Welcome tab stays open behind it.
        h.Press("Enter");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == b, "b.pas opened");
        Assert.Single(h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>());

        // A click on a row opens it too.
        await h.ExecuteAsync("view.welcomePage");
        await h.WaitForAsync(() => FileRowPaths(h).Count == 2, "file rows shown again");
        h.Click(FileRow + a);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == a, "a.txt opened");
    }

    /// <summary>IDE-WEL-024 with IDE-WEL-004/005: a missing recent file is flagged and asks <c>Msg.File.RecentNotFound</c>.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-024")]
    [Trait("Req", "IDE-P1-132")]
    public async Task RecentFileMissing_FlaggedAndPrompts()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            o.InitialFiles[@"Notes\here.pas"] = "unit here;";
        });
        var here = Path.Combine(h.TempDir, "Notes", "here.pas");
        var gone = Path.Combine(h.TempDir, "Notes", "gone.pas");
        h.Services.RecentItems.Add(RecentKind.File, here);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Services.RecentItems.Add(RecentKind.File, gone);
        var page = await CheckedPageAsync(h);

        // " (missing)" after the name, name in text.disabled; the existing file is not flagged.
        Assert.Equal([gone, here], FileRowPaths(h));
        Assert.True(page.Files.Single(f => f.Path == gone).IsMissing);
        Assert.False(page.Files.Single(f => f.Path == here).IsMissing);
        var name = h.Find<TextBlock>(FileRow + gone + ".Name");
        Assert.Equal("gone.pas (missing)", name.Text);
        Assert.True(h.Window.TryFindResource("FxcTextDisabledBrush", h.Window.ActualThemeVariant, out var disabled));
        Assert.Same(disabled, name.Foreground);
        Assert.Equal("here.pas", h.Find<TextBlock>(FileRow + here + ".Name").Text);

        // Keep leaves the entry; Remove deletes it from recent.json; nothing is opened.
        h.Messages.Answer("Msg.File.RecentNotFound", "Keep");
        h.Find<ListBoxItem>(FileRow + gone).Focus(NavigationMethod.Tab);
        h.Press("Enter");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 1, "RecentNotFound asked");
        Assert.Equal([gone], h.Messages.Requests[0].Args);
        Assert.Equal([gone, here], FileRowPaths(h));

        h.Messages.Answer("Msg.File.RecentNotFound", "Remove");
        h.Press("Enter");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 2, "RecentNotFound asked again");
        await h.WaitForAsync(() => FileRowPaths(h).Count == 1, "entry removed");
        Assert.Equal([here], FileRowPaths(h));
        Assert.Equal([here], RecentFilesOnDisk(h).Select(e => (string)e!["path"]!));
        Assert.DoesNotContain(h.Services.Documents.Documents, d => d.FilePath == gone);

        // The check runs again when the main window is activated.
        File.Delete(here);
        page.OnWindowActivated();
        await h.WaitForAsync(() => page.Files.Single().IsMissing, "here.pas now missing");
        Assert.Equal("here.pas (missing)", h.Find<TextBlock>(FileRow + here + ".Name").Text);
    }

    /// <summary>
    /// IDE-WEL-024 with IDE-WEL-007: the filter box shows when both lists together hold more than five entries and
    /// filters both; the projects show <c>Msg.Welcome.FilterEmpty</c> without a match, the recent-files section hides.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-024")]
    [Trait("Req", "IDE-WEL-007")]
    public async Task FilterAppliesToBothLists()
    {
        string[] projects = ["Alpha", "Beta", "Gamma"];
        string[] files = [@"C:\f\zeta.pas", @"C:\f\theta.txt", @"C:\g\omega.inc"];
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ShowAtStartup(o);
            var root = new JsonObject
            {
                ["formatVersion"] = 1,
                ["projects"] = new JsonArray([.. projects.Select((n, i) => (JsonNode)new JsonObject
                {
                    ["path"] = $@"C:\p\{n}\{n}.pproj", ["lastOpened"] = $"2026-11-{10 + i:00}T10:00:00Z", ["pinned"] = false,
                })]),
                ["files"] = new JsonArray([.. files.Select((f, i) => (JsonNode)new JsonObject
                {
                    ["path"] = f, ["lastOpened"] = $"2026-11-{10 + i:00}T10:00:00Z", ["pinned"] = false,
                })]),
            };
            o.UserFiles[@"roaming\recent.json"] = Encoding.UTF8.GetBytes(root.ToJsonString());
        });
        var page = await CheckedPageAsync(h);
        Assert.Equal(3, RowPaths(h).Count);
        Assert.Equal([@"C:\g\omega.inc", @"C:\f\theta.txt", @"C:\f\zeta.pas"], FileRowPaths(h));

        // Three projects + three files: more than five entries, so the filter box is shown.
        var filter = h.Find<TextBox>("Welcome.Recent.Filter");
        Assert.True(filter.IsEffectivelyVisible);

        // Ctrl+F from a file row; "eta" matches Beta (project) and zeta.pas, theta.txt (files).
        h.Find<ListBoxItem>(FileRow + @"C:\f\zeta.pas").Focus(NavigationMethod.Tab);
        h.Press("Ctrl+F");
        Assert.Equal("Welcome.Recent.Filter", h.FocusedAutomationId);
        h.Type("ETA");
        Assert.Equal([@"C:\p\Beta\Beta.pproj"], RowPaths(h));
        Assert.Equal([@"C:\f\theta.txt", @"C:\f\zeta.pas"], page.Files.Select(f => f.Path));
        Assert.Equal([@"C:\f\theta.txt", @"C:\f\zeta.pas"], FileRowPaths(h));

        // A path part matches too: no project, only the file under C:\g.
        filter.Text = @"c:\G\";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(RowPaths(h));
        Assert.Equal([@"C:\g\omega.inc"], FileRowPaths(h));
        Assert.Contains(@"No recent project matches c:\G\", h.Find<Control>("Welcome").GetLogicalDescendantsTexts());
        Assert.True(h.Find<Control>("Welcome.RecentFiles").IsEffectivelyVisible);

        // No file matches: the recent-files section hides; the matching project still shows.
        filter.Text = "gam";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([@"C:\p\Gamma\Gamma.pproj"], RowPaths(h));
        Assert.Empty(FileRowPaths(h));
        Assert.False(h.Find<Control>("Welcome.RecentFiles").IsVisible);

        // Clearing the filter shows both lists in full again.
        filter.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, RowPaths(h).Count);
        Assert.Equal(3, FileRowPaths(h).Count);
        Assert.True(h.Find<Control>("Welcome.RecentFiles").IsEffectivelyVisible);

        // Five entries or fewer in both lists together: no filter box, and its text no longer applies.
        filter.Text = "a";
        Dispatcher.UIThread.RunJobs();
        h.Services.RecentItems.Remove(RecentKind.File, @"C:\g\omega.inc");
        Dispatcher.UIThread.RunJobs();
        Assert.False(filter.IsEffectivelyVisible);
        Assert.Equal("", page.FilterText);
        Assert.Equal(2, FileRowPaths(h).Count);
    }
}
