using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class LayoutPersistenceTests
{
    private const string UiState = "roaming/uistate.json";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-055")]
    [Trait("Req", "UI-WIN-051")]
    [Trait("Req", "UI-WIN-050")]
    [Trait("Req", "IDE-P1-023")]
    public async Task RoundTripIsStable()
    {
        byte[] saved;
        await using (var h = await HeadlessHarness.StartAsync(WithTools))
        {
            var layout = h.Services.Layout.Layout;
            layout.LeftSize = 310;
            layout.BottomSize = 240.5;
            Assert.True(h.Services.ToolWindows.Show("Output", focus: false));
            Assert.True(h.Services.ToolWindows.Hide("SearchResults"));
            h.Services.ToolWindows.Get("Messages")!.SetColumnWidth("Code", 96);
            Dispatcher.UIThread.RunJobs();

            h.Window.Close(); // UI-WIN-051 (a): the UI state is saved when the main window closes
            Dispatcher.UIThread.RunJobs();
            saved = File.ReadAllBytes(Path.Combine(h.UserDir, UiState));
        }

        var text = System.Text.Encoding.UTF8.GetString(saved);
        Assert.StartsWith("{\n  \"formatVersion\": 1,\n  \"window\": {", text, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"normal\"", text, StringComparison.Ordinal);
        Assert.Contains("\"activeProfile\": \"Edit\"", text, StringComparison.Ordinal);

        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            WithTools(o);
            o.UserFiles[UiState] = saved;
        }))
        {
            var layout = h.Services.Layout.Layout;
            Assert.Equal((310, 240.5), (layout.LeftSize, layout.BottomSize));
            Assert.Equal(["Messages", "Output"], layout.Bottom.Tools);
            Assert.Equal("Output", layout.Bottom.ActiveTool);
            Assert.Equal(96, h.Services.ToolWindows.Get("Messages")!.ColumnWidth("Code"));
            Assert.Equal(310, h.Find<Avalonia.Controls.Control>(AutomationIds.DockLeft).Bounds.Width);

            // Saving right after restoring writes the same bytes.
            h.Services.Layout.Save();
            Assert.Equal(saved, File.ReadAllBytes(Path.Combine(h.UserDir, UiState)));
        }
    }

    /// <summary>
    /// IDE-ARCH-060: <c>window.state</c> is <c>normal</c> or <c>maximized</c>, and the saved bounds are the normal bounds
    /// in DIPs — a maximized window saves the size it had before maximizing, and a minimized one is saved as normal.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-060")]
    public async Task MaximizedWindowSavesItsNormalBounds()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.WindowSize = new Avalonia.Size(1000, 700));
        Assert.Equal(new Avalonia.Size(1000, 700), h.Window.ClientSize);

        h.Window.WindowState = Avalonia.Controls.WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        h.Window.Close();
        Dispatcher.UIThread.RunJobs();
        var window = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(h.UserDir, UiState)))!["window"]!;
        Assert.Equal("maximized", (string)window["state"]!);
        Assert.Equal((1000, 700), ((int)window["width"]!, (int)window["height"]!));

        await using var minimized = await HeadlessHarness.StartAsync(o => o.WindowSize = new Avalonia.Size(900, 600));
        minimized.Window.WindowState = Avalonia.Controls.WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        minimized.Window.Close();
        Dispatcher.UIThread.RunJobs();
        window = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(minimized.UserDir, UiState)))!["window"]!;
        Assert.Equal("normal", (string)window["state"]!);
        Assert.Equal((900, 600), ((int)window["width"]!, (int)window["height"]!));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-054")]
    public async Task CorruptFileFallsBackToDefault()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            WithTools(o);
            o.UserFiles[UiState] = "{ \"formatVersion\": 1, \"profiles\": "u8.ToArray();
        });
        var roaming = Path.Combine(h.UserDir, "roaming");

        var corrupt = Assert.Single(Directory.GetFiles(roaming, "uistate.corrupt-*.json"));
        Assert.Matches(@"uistate\.corrupt-\d{8}-\d{6}\.json$", corrupt);
        Assert.Equal("{ \"formatVersion\": 1, \"profiles\": ", File.ReadAllText(corrupt));
        Assert.False(File.Exists(Path.Combine(roaming, "uistate.json")));

        // Defaults are used, and the status bar says so.
        var layout = h.Services.Layout.Layout;
        Assert.Equal((LayoutProfiles.DefaultLeftSize, LayoutProfiles.DefaultBottomSize), (layout.LeftSize, layout.BottomSize));
        Assert.Equal(["Messages", "Output", "SearchResults"], layout.Bottom.Tools);
        Assert.Null(h.Services.Layout.WindowPlacement);
        Assert.Equal(Strings.Msg_App_LayoutCorrupt, h.Services.Status.Message);

        // The next save writes a valid file again.
        h.Services.Layout.Save();
        Assert.Contains("\"activeProfile\": \"Edit\"", File.ReadAllText(Path.Combine(roaming, "uistate.json")), StringComparison.Ordinal);
    }

    private static void WithTools(HarnessOptions o)
    {
        o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Output", ToolGroupId.Bottom, titleKey: "Cmd_view_output"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("SearchResults", ToolGroupId.Bottom, titleKey: "Cmd_view_searchResults"));
    }
}
