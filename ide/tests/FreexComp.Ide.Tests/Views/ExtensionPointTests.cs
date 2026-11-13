using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ExtensionPointTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-100")]
    [Trait("Req", "UI-WIN-013")]
    [Trait("Req", "UI-TW-008")]
    [Trait("Req", "UI-TW-009")]
    public async Task NewToolWindowNeedsOnlyRegistration()
    {
        // "Sample" is in no profile: registering it is the only change; its home dock is the right upper group.
        await using var h = await HeadlessHarness.StartAsync(o => o.ToolWindows.Add(SampleToolViewModel.Definition("Sample", ToolGroupId.RightUpper)));
        var tools = h.Services.ToolWindows;
        Assert.True(tools.IsRegistered("Sample"));
        Assert.False(h.Find<Control>(AutomationIds.DockRight).IsVisible); // registered, not yet shown

        Assert.True(tools.Show("Sample", focus: true));
        Dispatcher.UIThread.RunJobs();

        Assert.True(h.Find<Control>(AutomationIds.DockRight).IsVisible);
        Assert.True(h.Find<Control>(AutomationIds.DockRightUpper).IsVisible);
        Assert.Equal("Messages", h.Find<Button>("ToolTab.Sample").Content is TextBlock t ? t.Text : null); // tab = title
        Assert.Equal("MESSAGES", h.Find<TextBlock>("Tool.Sample.Caption").Text);
        Assert.Equal("Tool.Sample.List", h.FocusedAutomationId);

        // Tool bar and filter box (UI-TW-008).
        var button = h.Find<Button>("Tool.Sample.ToolBar.messages.clear");
        Assert.Equal(24, button.Bounds.Width);
        Assert.Equal("Clear Messages", AutomationProperties.GetName(button));
        var filter = h.Find<TextBox>("Tool.Sample.Filter");
        var sample = Assert.IsType<SampleToolViewModel>(tools.Get("Sample"));
        Assert.True(filter.Focus());
        h.Type("ab");
        h.Clock.Advance(TimeSpan.FromMilliseconds(149));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sample.AppliedFilters); // UI-TW-009: applied 150 ms after the last keystroke
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["ab"], sample.AppliedFilters);
        h.Press("Escape"); // non-empty filter: cleared
        Assert.Equal("", filter.Text);
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["ab", ""], sample.AppliedFilters);
        h.Press("Escape"); // empty filter: back to the list
        Assert.Equal("Tool.Sample.List", h.FocusedAutomationId);

        // Hide button in the caption (UI-TW-002), then show again.
        h.Click("Tool.Sample.Hide");
        Assert.False(h.Find<Control>(AutomationIds.DockRight).IsVisible);
        Assert.True(tools.Show("Sample", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>(AutomationIds.DockRightUpper).IsVisible);
        Assert.Empty(AutomationIdChecks.ControlsWithoutId(h.Window));
        Assert.Empty(AutomationIdChecks.DuplicateIds(h.Window));
    }
}
