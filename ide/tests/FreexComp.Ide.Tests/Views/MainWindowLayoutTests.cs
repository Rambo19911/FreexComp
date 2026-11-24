using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Minimum sizes of the main-window layout (main-window.md §7).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class MainWindowLayoutTests
{
    /// <summary>
    /// I-250 (UI-WIN-005): at its minimum height the bottom dock shows the Messages frame with its tool bar, its column
    /// header and one full row; the minimum is the smallest multiple of 8 at which that holds.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-005")]
    public async Task BottomDockMinimum_ShowsHeaderAndOneRow()
    {
        await using var h = await BuildScenario.StartAsync();
        await MessagesViewTests.BuildWithMessagesAsync(h);
        var layout = h.Services.Layout.Layout;
        var dock = h.Find<Control>(AutomationIds.DockBottom);
        Assert.Equal(MessagesViewModel.ToolName, layout.Bottom.ActiveTool);

        Assert.Equal(128, DockSizing.BottomDockMinHeight);
        layout.BottomSize = DockSizing.BottomDockMinHeight;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DockSizing.BottomDockMinHeight, dock.Bounds.Height);

        Rect InDock(Control control) => new(control.TranslatePoint(default, dock)!.Value, control.Bounds.Size);
        var frame = InDock(h.Find<Control>("Tool.Messages"));
        var list = InDock(h.Find<ListBox>("Tool.Messages.List"));
        var toolBar = InDock(h.Find<Control>("Tool.Messages.ToolBar.messages.toggleErrors"));
        var header = InDock(h.Find<Control>("Tool.Messages.Column.Code"));
        var row = InDock(h.Find<Control>("Tool.Messages.Row.0"));

        // Top to bottom inside the frame: tool bar, column header, the first row — each whole.
        Assert.True(frame.Top >= 0 && frame.Bottom <= dock.Bounds.Height, $"frame {frame} in a dock of {dock.Bounds.Height}");
        Assert.True(toolBar.Top >= frame.Top && toolBar.Bottom <= header.Top, $"tool bar {toolBar}, header {header}");
        Assert.True(header.Bottom <= row.Top, $"header {header}, row {row}");
        Assert.Equal(22, row.Height);
        Assert.True(row.Bottom <= frame.Bottom && row.Bottom <= list.Bottom, $"row {row} in list {list}, frame {frame}");

        // The smallest multiple of 8: what the row needs, plus what lies below the frame (the tab strip).
        var needed = row.Bottom + (dock.Bounds.Height - frame.Bottom);
        Assert.Equal(DockSizing.BottomDockMinHeight, Math.Ceiling(needed / 8) * 8);

        // A smaller stored size is laid out at the minimum.
        layout.BottomSize = 80;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DockSizing.BottomDockMinHeight, dock.Bounds.Height);
        Assert.True(InDock(h.Find<Control>("Tool.Messages.Row.0")).Bottom <= InDock(h.Find<Control>("Tool.Messages")).Bottom);
    }
}
