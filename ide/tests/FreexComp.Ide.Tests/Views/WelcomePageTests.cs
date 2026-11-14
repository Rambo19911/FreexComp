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
    private static IReadOnlyList<string> RowPaths(HeadlessHarness h) =>
    [
        .. h.FindAll(Row).OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible)
            .OrderBy(i => Avalonia.VisualExtensions.TranslatePoint(i, default, h.Window)!.Value.Y)
            .Select(i => AutomationIds.Get(i)![Row.Length..]),
    ];

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
}
