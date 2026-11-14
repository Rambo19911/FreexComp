using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class LayoutTests
{
    private const double Splitter = 5; // design-tokens.md §8: splitter hit area

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-WIN-010")]
    [Trait("Req", "UI-WIN-011")]
    public async Task EditProfileDefaultArrangement()
    {
        // The Edit column of main-window.md §2 is the built-in profile.
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/main-window.md"), "2. Default layout per profile");
        var profile = LayoutProfiles.Create(LayoutProfiles.Edit);
        foreach (var group in Enum.GetValues<ToolGroupId>())
        {
            Assert.Equal(ExpectedGroup(rows, group), Describe(profile.Group(group)));
        }

        Assert.Equal((260, 300, 200, 0.5), (profile.LeftSize, profile.RightSize, profile.BottomSize, profile.RightSplit));

        // Phase 1: only the P1 tool windows are registered; the profile skips the others (UI-WIN-011).
        var phase1 = rows.Where(r => r["First phase"] == "P1").Select(r => ToolName(r["Tool window"])).ToHashSet();
        Assert.Equal(["ProjectManager"], phase1);
        var (window, _, layout) = Show(phase1);
        try
        {
            Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
            var left = Find(window, "Dock.Left");
            Assert.True(left.IsVisible);
            Assert.Equal(260, left.Bounds.Width);
            Assert.Equal(["ToolTab.ProjectManager"], Ids(window, "ToolTab."));
            Assert.Equal("ProjectManager", layout.Left.ActiveTool);
            Assert.False(Find(window, "Dock.Bottom").IsVisible); // Messages, Output, Search Results are P3
            Assert.False(Find(window, "Dock.Right").IsVisible);  // UI-WIN-010: empty in Edit
            var documents = Find(window, AutomationIds.DocumentArea);
            Assert.Equal(1440 - 260 - Splitter, documents.Bounds.Width);
            Assert.Equal(900, documents.Bounds.Height);

        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-004")]
    [Trait("Req", "UI-TW-005")]
    public async Task EmptyDockCollapses()
    {
        // Phase 1 registers only the Project Manager (I-068): with it hidden, every dock is empty and collapsed.
        await using (var h = await HeadlessHarness.StartAsync())
        {
            Assert.True(h.Find<Control>("Dock.Left").IsVisible);
            Assert.True(h.Services.ToolWindows.Hide("ProjectManager"));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(h.Find<Control>("Dock.Left").IsVisible);
            Assert.False(h.Find<Control>("Dock.Right").IsVisible);
            Assert.False(h.Find<Control>("Dock.Bottom").IsVisible);
            Assert.Equal(1440, h.Find<Control>(AutomationIds.DocumentArea).Bounds.Width);
        }

        var (window, _, layout) = Show(new HashSet<string>(["ProjectManager", "Messages"], StringComparer.Ordinal));
        try
        {
            var documents = Find(window, AutomationIds.DocumentArea);
            Assert.Equal(200, Find(window, "Dock.Bottom").Bounds.Height);
            Assert.Equal(900 - 200 - Splitter, documents.Bounds.Height);

            Assert.True(layout.SetVisible("Messages", false)); // last visible tool of the bottom dock
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find(window, "Dock.Bottom").IsVisible);
            Assert.Equal(900, documents.Bounds.Height); // the document area takes the space, splitter included

            layout.LeftSize = 300;
            Assert.True(layout.SetVisible("ProjectManager", false));
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find(window, "Dock.Left").IsVisible);
            Assert.Equal(1440, documents.Bounds.Width);
            Assert.Null(layout.Left.ActiveTool);

            Assert.True(layout.SetVisible("ProjectManager", true));
            Assert.True(layout.SetVisible("Messages", true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, Find(window, "Dock.Left").Bounds.Width); // restored at its last size
            Assert.Equal(200, Find(window, "Dock.Bottom").Bounds.Height);
            Assert.Equal(1440 - 300 - Splitter, documents.Bounds.Width);
            Assert.Equal("ProjectManager", layout.Left.ActiveTool);
            Assert.False(layout.SetVisible("Watches", true)); // not in the profile: nothing to show (I-023 adds home docks)
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-005")]
    public async Task SplitterLimitsAreClamped()
    {
        await using var h = await HeadlessHarness.StartAsync(AllDocks);
        var layout = h.Services.Layout.Layout;
        ShowRightDock(h);
        var view = h.Find<Control>(AutomationIds.DockLeft).GetVisualAncestors().OfType<LayoutView>().Single();
        var width = view.Bounds.Width;
        var height = view.Bounds.Height;
        var left = h.Find<Control>(AutomationIds.DockLeft);
        var right = h.Find<Control>(AutomationIds.DockRight);
        var bottom = h.Find<Control>(AutomationIds.DockBottom);
        var documents = h.Find<Control>(AutomationIds.DocumentArea);

        // Left dock: at most window − 320 − right dock (− both splitters), at least 160.
        h.Drag(AutomationIds.Splitter(AutomationIds.DockLeft), 2000, 0);
        Assert.Equal(width - 320 - 300 - (2 * Splitter), left.Bounds.Width);
        Assert.Equal(320, documents.Bounds.Width);
        Assert.Equal(width - 320 - 300 - (2 * Splitter), layout.LeftSize);
        h.Drag(AutomationIds.Splitter(AutomationIds.DockLeft), -2000, 0);
        Assert.Equal(160, left.Bounds.Width);
        Assert.Equal(160, layout.LeftSize);

        // Right dock likewise, against the left dock.
        h.Drag(AutomationIds.Splitter(AutomationIds.DockRight), -2000, 0);
        Assert.Equal(width - 320 - 160 - (2 * Splitter), right.Bounds.Width);
        Assert.Equal(width - 320 - 160 - (2 * Splitter), layout.RightSize);
        h.Drag(AutomationIds.Splitter(AutomationIds.DockRight), 2000, 0);
        Assert.Equal(160, layout.RightSize);

        // Bottom dock: 80 … available height − 160.
        h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, -2000);
        Assert.Equal(height - 160 - Splitter, bottom.Bounds.Height);
        Assert.Equal(160, documents.Bounds.Height);
        Assert.Equal(height - 160 - Splitter, layout.BottomSize);
        h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, 2000);
        Assert.Equal(80, bottom.Bounds.Height);
        Assert.Equal(80, layout.BottomSize);

        // Right-dock groups: at least 80 high each.
        h.Drag(AutomationIds.Splitter(AutomationIds.DockRightUpper), 0, 2000);
        Assert.Equal(80, h.Find<Control>(AutomationIds.DockRightLower).Bounds.Height);
        Assert.Equal((height - Splitter - 80) / (height - Splitter), layout.RightSplit, 6);
        h.Drag(AutomationIds.Splitter(AutomationIds.DockRightUpper), 0, -2000);
        Assert.Equal(80, h.Find<Control>(AutomationIds.DockRightUpper).Bounds.Height);

        // Double-click on a splitter resets that dock to its default size.
        h.DoubleClick(AutomationIds.Splitter(AutomationIds.DockLeft));
        h.DoubleClick(AutomationIds.Splitter(AutomationIds.DockRight));
        h.DoubleClick(AutomationIds.Splitter(AutomationIds.DockBottom));
        h.DoubleClick(AutomationIds.Splitter(AutomationIds.DockRightUpper));
        Assert.Equal((260, 300, 200, 0.5), (layout.LeftSize, layout.RightSize, layout.BottomSize, layout.RightSplit));
        Assert.Equal(260, left.Bounds.Width);
        Assert.Equal(200, bottom.Bounds.Height);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-015")]
    public async Task ResetLayoutRestoresDefaults()
    {
        await using var h = await HeadlessHarness.StartAsync(AllDocks);
        var layout = h.Services.Layout.Layout;
        var tools = h.Services.ToolWindows;
        ShowRightDock(h);
        layout.LeftSize = 400;
        layout.BottomSize = 120;
        layout.RightSplit = 0.3;
        Assert.True(tools.Hide("ProjectManager"));
        Assert.True(tools.Show("Output", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Control>(AutomationIds.DockLeft).IsVisible);

        Assert.True(h.Services.Commands.CanExecute("window.resetLayout"));
        await h.ExecuteAsync("window.resetLayout"); // no confirmation: the harness fails on an unanswered message box

        var builtIn = LayoutProfiles.Create(LayoutProfiles.Edit);
        var profile = layout.State.Profiles[LayoutProfiles.Edit];
        foreach (var group in Enum.GetValues<ToolGroupId>())
        {
            Assert.Equal(Describe(builtIn.Group(group)), Describe(profile.Group(group)));
        }

        Assert.Equal((260, 300, 200, 0.5), (layout.LeftSize, layout.RightSize, layout.BottomSize, layout.RightSplit));
        Assert.Equal(260, h.Find<Control>(AutomationIds.DockLeft).Bounds.Width);
        Assert.Equal("ProjectManager", layout.Left.ActiveTool);
        Assert.Equal(["Messages", "Output"], layout.Bottom.Tools);
        Assert.Equal("Messages", layout.Bottom.ActiveTool);
        Assert.False(h.Find<Control>(AutomationIds.DockRight).IsVisible); // UI-WIN-010: empty in Edit

        // The reset is a change like any other: saved 2 s later.
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        var saved = File.ReadAllText(Path.Combine(h.UserDir, "roaming", "uistate.json"));
        Assert.Contains("\"size\": 260", saved, StringComparison.Ordinal);
    }

    private static void AllDocks(HarnessOptions o)
    {
        o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Output", ToolGroupId.Bottom, titleKey: "Cmd_view_output"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("ObjectInspector", ToolGroupId.RightUpper, titleKey: "Cmd_view_objectInspector"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("ToolPalette", ToolGroupId.RightLower, titleKey: "Cmd_view_toolPalette"));
    }

    /// <summary>The Edit profile has no right-dock tool windows; showing them places them in their home groups.</summary>
    private static void ShowRightDock(HeadlessHarness h)
    {
        Assert.True(h.Services.ToolWindows.Show("ObjectInspector", focus: false));
        Assert.True(h.Services.ToolWindows.Show("ToolPalette", focus: false));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The layout of a composition where exactly <paramref name="registered"/> tool windows exist (in their Edit-profile docks).</summary>
    private static (Window Window, LayoutView View, LayoutViewModel Layout) Show(IReadOnlySet<string> registered)
    {
        var services = TestComposition.Build(o => o.ToolWindows.AddRange(
            registered.Select(name => SampleToolViewModel.Definition(name, name == "ProjectManager" ? ToolGroupId.Left : ToolGroupId.Bottom))));
        var layout = services.Layout.Layout;
        layout.DocumentArea = new DocumentAreaViewModel(services.Commands, services.Keymap, services.Documents);
        var view = new LayoutView { DataContext = layout };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, layout);
    }

    private static Control Find(Window window, string id) =>
        window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);

    private static List<string> Ids(Window window, string prefix) =>
        [.. window.GetVisualDescendants().OfType<Control>().Select(c => AutomationIds.Get(c)).OfType<string>().Where(id => id.StartsWith(prefix, StringComparison.Ordinal))];

    private static string ToolName(string display) => display.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    private static string Describe(ToolGroupLayout group) =>
        $"tools=[{string.Join(",", group.Tools)}] hidden=[{string.Join(",", group.Hidden)}] active={group.Active}";

    /// <summary>Cells like "Left ①, active", "Bottom ④, hidden", "hidden" (= not placed in this profile).</summary>
    private static string ExpectedGroup(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, ToolGroupId group)
    {
        var prefix = group switch
        {
            ToolGroupId.Left => "Left",
            ToolGroupId.RightUpper => "Right-upper",
            ToolGroupId.RightLower => "Right-lower",
            _ => "Bottom",
        };
        var placed = rows
            .Select(r => (Name: ToolName(r["Tool window"]), Cell: r["Edit profile"]))
            .Where(t => t.Cell.StartsWith(prefix + " ", StringComparison.Ordinal))
            .Select(t => (t.Name, Order: "①②③④⑤⑥⑦⑧⑨".IndexOf(t.Cell[prefix.Length + 1], StringComparison.Ordinal), Hidden: t.Cell.EndsWith("hidden", StringComparison.Ordinal), Active: t.Cell.EndsWith("active", StringComparison.Ordinal)))
            .OrderBy(t => t.Order)
            .ToList();
        var tools = placed.Where(t => !t.Hidden).Select(t => t.Name);
        var hidden = placed.Where(t => t.Hidden).Select(t => t.Name);
        var active = placed.SingleOrDefault(t => t.Active).Name;
        return $"tools=[{string.Join(",", tools)}] hidden=[{string.Join(",", hidden)}] active={active}";
    }
}
