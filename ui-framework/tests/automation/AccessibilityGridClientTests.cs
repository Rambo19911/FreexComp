using System.Runtime.InteropServices;
using FreexComp.UiFramework.Tests;

namespace FreexComp.UiFramework.Automation.Tests;

/// <summary>
/// U-157 (accessibility.md §7, §9): the string grid of <c>tests/gui/AccessibilityProbe.pas</c> as the operating
/// system's COM client sees it. The probe sets the focus inside its own thread (its form becomes the active window of
/// that thread, no foreground change); like every test of this project (U-161: no parallel test runs in the
/// assembly) it runs alone. Keys reach the grid as messages the probe sends to its own window: no input is injected
/// and nothing outside the probe is touched.
/// </summary>
[Collection(DesktopCollection.Name)]
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Foreground")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class AccessibilityGridClientTests
{
    private const uint WM_APP = 0x8000;
    private const uint WM_PROBE_GRID = WM_APP + 8;
    private const uint WM_PROBE_GRID_FOCUS = WM_APP + 9;
    private const uint WM_PROBE_GRID_KEY = WM_APP + 10;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    /// <summary>
    /// UIF-1435…1439, UIF-1444: <c>GridPattern.GetItem(2, 1)</c> is the cell with the text 'Beta' as its Name and Value
    /// (a DataItem); <c>SelectionItemPattern.Select</c> makes it the current cell (the probe prints <c>Col</c>,
    /// <c>Row</c>); with the grid focused, arrow keys sent to the grid raise focus-changed events on the new current
    /// cells, which the client receives in order.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1435")]
    [Trait("Req", "UIF-1436")]
    [Trait("Req", "UIF-1437")]
    [Trait("Req", "UIF-1438")]
    [Trait("Req", "UIF-1439")]
    [Trait("Req", "UIF-1444")]
    public void Uia_GridItemsAndSelection()
    {
        Harness.RequireDesktop();
        using var probe = AccessibilityClientTests.Probe.Start();
        var grid = probe.Find("DataGrid");
        Assert.NotNull(grid);
        var gridPattern = (IUIAutomationGridPattern?)grid.GetCurrentPattern(UiaIds.GridPattern);
        Assert.NotNull(gridPattern);
        Assert.Equal(4, gridPattern.CurrentRowCount);
        Assert.Equal(4, gridPattern.CurrentColumnCount);

        var cell = gridPattern.GetItem(2, 1);
        Assert.Equal("Beta", cell.CurrentName);
        Assert.Equal(UiaIds.DataItem, cell.CurrentControlType);
        Assert.Equal("FreexComp", cell.CurrentFrameworkId);
        Assert.Equal(UiaIds.HeaderItem, gridPattern.GetItem(0, 1).CurrentControlType);
        var value = (IUIAutomationValuePattern?)cell.GetCurrentPattern(UiaIds.ValuePattern);
        Assert.NotNull(value);
        Assert.Equal("Beta", value.CurrentValue);

        var selection = (IUIAutomationSelectionItemPattern?)cell.GetCurrentPattern(UiaIds.SelectionItemPattern);
        Assert.NotNull(selection);
        Assert.Equal(0, selection.CurrentIsSelected);
        Assert.Equal("grid 1 1", probe.Request(WM_PROBE_GRID, "grid "));
        selection.Select();
        Assert.Equal("grid 1 2", probe.Request(WM_PROBE_GRID, "grid "));
        Assert.NotEqual(0, selection.CurrentIsSelected);

        Assert.Equal("focused grid True", probe.Request(WM_PROBE_GRID_FOCUS, "focused grid "));
        var sink = new FocusSink(probe.App.Process.Id);
        probe.Client.AddFocusChangedEventHandler(FocusSink.Prefetch(probe.Client), sink);
        Assert.Equal("grid 1 3", probe.Request(WM_PROBE_GRID_KEY, "grid ", VK_DOWN));
        Assert.Equal("grid 2 3", probe.Request(WM_PROBE_GRID_KEY, "grid ", VK_RIGHT));
        Assert.True(
            probe.WaitUntil(() => sink.Cells().Contains("Delta")),
            "no focus-changed event on cell (2, 3); seen: " + sink);
        Assert.Equal("Gamma Delta", string.Join(" ", sink.Cells()));
        probe.RemoveHandlers();
        probe.Close();
    }

    /// <summary>
    /// The client's focus-changed handler (a COM callable wrapper the system calls on its own threads). Focus-changed
    /// events come from the whole desktop (other programs, the user's activity); U-161: the handler keeps only those
    /// whose sender belongs to the probe's process, and reads the sender's process id, control type and name from the
    /// cache the system filled before the call (<see cref="Prefetch"/>), so it never calls into another process.
    /// </summary>
    [ComVisible(true)]
    private sealed class FocusSink(int processId) : IUIAutomationFocusChangedEventHandler
    {
        private readonly List<(string Name, int Type)> events = [];
        private int others;

        /// <summary>The cache request of the handler: process id, control type and name of the sender itself.</summary>
        public static IUIAutomationCacheRequest Prefetch(IUIAutomation client)
        {
            var request = client.CreateCacheRequest();
            request.AddProperty(UiaIds.ProcessIdProperty);
            request.AddProperty(UiaIds.ControlTypeProperty);
            request.AddProperty(UiaIds.NameProperty);
            request.TreeFilter = client.CreateTrueCondition();              // every sender, control view or not
            return request;
        }

        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
            (string, int)? e;
            try
            {
                e = sender.GetCachedPropertyValue(UiaIds.ProcessIdProperty) is int pid && pid == processId
                    ? (sender.GetCachedPropertyValue(UiaIds.NameProperty) as string ?? "", sender.GetCachedPropertyValue(UiaIds.ControlTypeProperty) is int type ? type : 0)
                    : null;
            }
            catch (COMException)
            {
                e = null;                                                   // nothing cached: not one of the probe's
            }

            lock (events)
            {
                if (e is { } own)
                {
                    events.Add(own);
                }
                else
                {
                    others++;
                }
            }
        }

        /// <summary>The names of the probe's grid cells (DataItem senders) that received the focus, in order.</summary>
        public string[] Cells()
        {
            lock (events)
            {
                return [.. events.Where(e => e.Type == UiaIds.DataItem).Select(e => e.Name)];
            }
        }

        public override string ToString()
        {
            lock (events)
            {
                return string.Join("; ", events.Select(e => $"{UiaIds.Name(e.Type)} '{e.Name}'"))
                    + $" (and {others} of other processes)";
            }
        }
    }
}
