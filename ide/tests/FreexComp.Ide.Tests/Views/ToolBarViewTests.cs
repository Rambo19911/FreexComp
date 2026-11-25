using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The platform combo of the Build tool bar (phase8-ui-additions.md §11, UI-TB-022, 023; platform-selector.md IDE-PLAT-004; task I-278).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ToolBarViewTests
{
    private const string NotInstalled = "Win32 (not installed)";

    private static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items) =>
        items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Descendants(i.Items.Cast<object?>())));

    private static MenuItem MenuItem(HeadlessHarness h, string id) =>
        Descendants(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object>()).Single(i => AutomationIds.Get(i) == id);

    /// <summary>
    /// UI-TB-022, 023: the combo is at least 88 wide and keeps its name; its items are the labels of the listed platforms —
    /// the id with an upper-case first letter, a platform that is not installed with its suffix — each elided at its end
    /// when it does not fit and shown in full as its tooltip; the active platform is selected and choosing another one
    /// runs <c>project.setPlatform</c> (the status bar and the Project menu bullet follow). Disabled while not Idle, and
    /// with a project that lists one platform — whose label stays visible.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-022")]
    [Trait("Req", "UI-TB-023")]
    [Trait("Req", "IDE-PLAT-004")]
    public async Task PlatformComboRendersItemsAndDisabledState()
    {
        await using var h = await BuildScenario.StartAsync(projectExtra: """
            , "platforms": ["win64", "win32"]
            """);
        var combo = h.Find<ComboBox>("ToolBar.Build.Platform");
        var limit = Assert.IsType<double>(h.Window.FindResource("FxcMetricDocumentTabMaxWidth"));
        Assert.Equal((88.0, "Platform"), (combo.MinWidth, AutomationProperties.GetName(combo)));
        Assert.InRange(combo.Bounds.Width, 88, limit); // I-287: as wide as the widest item needs, see PlatformCombo_ShowsTheLongestLabelOrHasATooltip
        Assert.Equal(["Win64", NotInstalled], combo.ItemsSource!.Cast<string>());
        Assert.Equal("Win64", combo.SelectedItem);
        Assert.True(combo.IsEnabled);
        Assert.Equal("Debug | Win64", h.Status.Field("Configuration"));

        // An item: its text ends in an ellipsis when it does not fit; the full label is its tooltip.
        var item = Assert.IsAssignableFrom<TextBlock>(combo.ItemTemplate!.Build(NotInstalled));
        Assert.Equal((NotInstalled, TextTrimming.CharacterEllipsis, NotInstalled), (item.Text, item.TextTrimming, ToolTip.GetTip(item)));

        // The Project menu lists the same labels; the bullet is on the active platform.
        Assert.Equal(("_1 Win64", true, MenuItemToggleType.Radio),
            (MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).Header, MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).IsChecked,
                MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).ToggleType));
        Assert.Equal(("_2 " + NotInstalled, false), (MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).Header, MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).IsChecked));

        // Choosing the other platform runs the command: the project file, the selection box, the status bar, the bullet.
        combo.SelectedItem = NotInstalled;
        await h.WaitForAsync(() => h.Services.Projects.Project?.ActivePlatform == "win32", "platform switched");
        Assert.Contains("\"activePlatform\": \"win32\"", File.ReadAllText(BuildScenario.ProjectPath(h)), StringComparison.Ordinal);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NotInstalled, combo.SelectedItem);
        Assert.Equal("Debug | " + NotInstalled, h.Status.Field("Configuration"));
        Assert.True(MenuItem(h, AutomationIds.Menu("project.setPlatform", "win32")).IsChecked);
        Assert.False(MenuItem(h, AutomationIds.Menu("project.setPlatform", "win64")).IsChecked);

        // The selection box: the label in full, or cut inside the combo at its widest with an ellipsis (I-287) — never
        // drawn over its neighbours.
        AssertShownInFullOrElidedAtTheLimit(combo, NotInstalled, limit);

        // Disabled while Building (UI-TB-020), enabled again afterwards.
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Assert.Equal("win32", held.Request!.Platform);
        Dispatcher.UIThread.RunJobs();
        Assert.False(combo.IsEnabled);
        Assert.Equal(NotInstalled, combo.SelectedItem);
        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsEnabled);

        // A project that lists a single platform: the label stays visible, the combo is disabled.
        await h.ExecuteAsync("project.setPlatform", "win64");
        var definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64"], ActivePlatform = null }));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Win64"], combo.ItemsSource!.Cast<string>());
        Assert.Equal("Win64", combo.SelectedItem);
        Assert.False(combo.IsEnabled);
        Assert.Contains(combo.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Win64" && t.IsEffectivelyVisible);
        Assert.Equal("Debug | Win64", h.Status.Field("Configuration"));
        Assert.Equal("Win64", ToolTip.GetTip(combo.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Win64")));
        Assert.Equal("Win64", ToolTip.GetTip(combo));
    }

    /// <summary>The width <paramref name="text"/> needs in the font of <paramref name="like"/>, measured without a limit.</summary>
    private static double NeededWidth(TextBlock like, string text)
    {
        var probe = new TextBlock
        {
            Text = text, FontFamily = like.FontFamily, FontSize = like.FontSize, FontWeight = like.FontWeight, FontStyle = like.FontStyle,
            FontStretch = like.FontStretch, UseLayoutRounding = false,
        };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }

    private static TextBlock Shown(ComboBox combo, string label) =>
        combo.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label && t.IsEffectivelyVisible);

    /// <summary>What the combo adds to the width of its text: padding, border and the drop-down glyph.</summary>
    private static double Chrome(ComboBox combo) => combo.Bounds.Width - Shown(combo, (string)combo.SelectedItem!).Bounds.Width;

    /// <summary>
    /// UI-TB-023 for the selected <paramref name="label"/>, the widest of the combo: shown in full when the combo may
    /// be as wide as that needs; otherwise the combo is as wide as the limit and the label ends in an ellipsis inside
    /// it. Either way the full label is the tooltip of the combo and of the item. Returns whether it is shown in full.
    /// </summary>
    private static bool AssertShownInFullOrElidedAtTheLimit(ComboBox combo, string label, double limit)
    {
        Assert.Equal(label, combo.SelectedItem);
        var shown = Shown(combo, label);
        var needed = NeededWidth(shown, label);
        var fits = needed + Chrome(combo) <= limit;
        Assert.Equal(TextTrimming.CharacterEllipsis, shown.TextTrimming);
        Assert.InRange(combo.Bounds.Width, 88, limit);
        var right = shown.TranslatePoint(new Point(shown.Bounds.Width, 0), combo)!.Value.X;
        Assert.True(right <= combo.Bounds.Width, $"'{label}' ends at {right} in a combo {combo.Bounds.Width} wide");
        if (fits)
        {
            Assert.False(shown.TextLayout.TextLines[0].HasCollapsed, $"'{label}' is elided in a combo {combo.Bounds.Width} wide (limit {limit}, the text needs {needed})");
            Assert.True(shown.Bounds.Width + 0.5 >= needed, $"'{label}': text block {shown.Bounds.Width} wide, the text needs {needed}");
        }
        else
        {
            Assert.Equal(limit, combo.Bounds.Width);
            Assert.True(shown.TextLayout.TextLines[0].HasCollapsed, $"'{label}' needs {needed}, does not fit and is elided");
        }

        Assert.Equal((label, label), (ToolTip.GetTip(combo), ToolTip.GetTip(shown)));
        return fits;
    }

    /// <summary>
    /// I-287, UI-TB-023: the platform combo is as wide as its widest item needs — at least 88, at most the limit of 240 —
    /// whichever item is selected, so the longest label is shown in full; a label that needs more than the limit ends in
    /// an ellipsis. The full label of the selected item is the combo's tooltip, also while the combo is disabled.
    /// (The headless font is about twice as wide as the product's: <c>Win32 (not installed)</c>, in full in the product,
    /// passes the limit here — both outcomes of the rule are asserted with labels measured in the font in use.)
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-023")]
    public async Task PlatformCombo_ShowsTheLongestLabelOrHasATooltip()
    {
        await using var h = await BuildScenario.StartAsync(o => o.PlatformCatalog = new TestPlatformCatalog(), projectExtra: """
            , "platforms": ["win64", "test32"], "activePlatform": "test32"
            """);
        var combo = h.Find<ComboBox>("ToolBar.Build.Platform");
        var limit = Assert.IsType<double>(h.Window.FindResource("FxcMetricDocumentTabMaxWidth"));
        Assert.Equal(240, limit);
        Assert.Equal((88.0, limit, true), (combo.MinWidth, combo.MaxWidth, double.IsNaN(combo.Width)));
        Assert.True(ToolTip.GetShowOnDisabled(combo));

        // Two labels that fit: the combo is as wide as the longer one needs, and the longer one is shown in full.
        Assert.Equal(["Win64", "Test32"], combo.ItemsSource!.Cast<string>());
        Assert.True(AssertShownInFullOrElidedAtTheLimit(combo, "Test32", limit), "Test32 fits below the limit");
        var width = combo.Bounds.Width;
        Assert.Equal(Math.Max(88, Math.Ceiling(NeededWidth(Shown(combo, "Test32"), "Test32")) + Chrome(combo)), width);
        var next = h.Find<Button>(AutomationIds.ToolBarButton("project.build"));
        var nextLeft = next.TranslatePoint(default, h.Window)!.Value.X;
        Assert.True(combo.TranslatePoint(new Point(width, 0), h.Window)!.Value.X <= nextLeft, "the combo ends before the Build button");

        // The width is that of the widest item, not of the selected one: selecting the shorter label moves nothing.
        await h.ExecuteAsync("project.setPlatform", "win64");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Win64", combo.SelectedItem);
        Assert.False(Shown(combo, "Win64").TextLayout.TextLines[0].HasCollapsed);
        Assert.Equal(width, combo.Bounds.Width);
        Assert.Equal(nextLeft, next.TranslatePoint(default, h.Window)!.Value.X);
        Assert.Equal("Win64", ToolTip.GetTip(combo));

        // The label of a platform that is not installed, active: in full, or — where the font makes it pass the limit —
        // elided at the limit; the tooltip gives it in full.
        var definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64", "win32"], ActivePlatform = "win32" }));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Win64", NotInstalled], combo.ItemsSource!.Cast<string>());
        AssertShownInFullOrElidedAtTheLimit(combo, NotInstalled, limit);
        Assert.True(combo.Bounds.Width > width, "the combo grew with its widest item");

        // A label that needs more than the limit in any font: the combo stops at the limit, the label is elided.
        const string LongId = "win32-with-a-rather-long-toolchain-name-that-no-combo-holds";
        const string LongLabel = "Win32-with-a-rather-long-toolchain-name-that-no-combo-holds (not installed)";
        definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64", LongId], ActivePlatform = LongId }));
        Dispatcher.UIThread.RunJobs();
        Assert.False(AssertShownInFullOrElidedAtTheLimit(combo, LongLabel, limit), "this label passes the limit");
        Assert.Equal(limit, combo.Bounds.Width);

        // One platform: the combo is disabled, its label and tooltip stay; no project: empty, 88 wide, no tooltip.
        definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64"], ActivePlatform = null }));
        Dispatcher.UIThread.RunJobs();
        Assert.False(combo.IsEnabled);
        Assert.True(AssertShownInFullOrElidedAtTheLimit(combo, "Win64", limit));
        await h.ExecuteAsync("file.closeProject");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(combo.SelectedItem);
        Assert.Null(ToolTip.GetTip(combo));
        Assert.Equal(88, combo.Bounds.Width);
    }

    /// <summary>
    /// I-293, UI-TB-006, UI-WIN-061: at the 800 px minimum width, a tool bar of the row is never cut — also after the
    /// platform combo grew to its longest label (<c>Win32 (not installed)</c> active) once the row was laid out: the
    /// tool bars that no longer fit move, whole and from the right, into the overflow.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-006")]
    [Trait("Req", "UI-WIN-061")]
    public async Task MinimumWidth_AGrownPlatformComboMovesToolbarsIntoTheOverflow()
    {
        await using var h = await BuildScenario.StartAsync(o => o.PlatformCatalog = new TestPlatformCatalog(), projectExtra: """
            , "platforms": ["win64", "test32"], "activePlatform": "win64"
            """);
        VisualSnapshot.Arrange(h.Window, new Size(800, 600), 1.0);
        var row = h.Find<Panel>("ToolBar.Standard").FindAncestorOfType<ToolBarRow>()!;
        var combo = h.Find<ComboBox>("ToolBar.Build.Platform");
        AssertNoToolbarCut(row);
        var width = combo.Bounds.Width;

        var definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64", "win32"], ActivePlatform = "win32" }));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NotInstalled, combo.SelectedItem);
        Assert.True(combo.Bounds.Width > width, "the combo grew with its widest item");
        AssertNoToolbarCut(row);
        Assert.NotEmpty(row.InOverflow);

        // Back to the short labels: the tool bars come back into the row as far as they fit.
        definition = h.Services.Projects.Definition!;
        Assert.True(await h.Services.Projects.ApplyOptionsAsync(definition with { Platforms = ["win64"], ActivePlatform = null }));
        Dispatcher.UIThread.RunJobs();
        AssertNoToolbarCut(row);
    }

    /// <summary>Every tool bar in the row ends before the overflow button (when shown) and inside the row.</summary>
    private static void AssertNoToolbarCut(ToolBarRow row)
    {
        Dispatcher.UIThread.RunJobs();
        var overflow = row.GetVisualDescendants().OfType<Button>().Single(b => AutomationIds.Get(b) == "ToolBar.Overflow");
        var end = overflow.IsVisible ? overflow.TranslatePoint(default, row)!.Value.X : row.Bounds.Width;
        foreach (var toolbar in row.InRow)
        {
            var right = toolbar.TranslatePoint(new Point(toolbar.Bounds.Width, 0), row)!.Value.X;
            Assert.True(right <= end + 0.5, $"{AutomationIds.Get(toolbar)} ends at {right}, the row has room up to {end} (in the overflow: {string.Join(", ", row.InOverflow.Select(AutomationIds.Get))})");
        }
    }
}
