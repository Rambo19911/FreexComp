using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the minimum height of the bottom dock (I-250).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 500 and 1920 × 1040, light and dark, with the bottom dock dragged to its minimum height
    /// after a build with messages: the Messages frame shows its tool bar, its column header and one full row above the
    /// tab strip (UI-WIN-005). PNGs under <c>main-window-bottom-dock-minimum</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-WIN-005")]
    public async Task MainWindow_BottomDockAtMinimum_ShowsToolBarHeaderAndOneRow()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["main-window-bottom-dock-minimum"];
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        if (Directory.Exists(VisualSnapshot.FolderOf(subjects[0])))
            Directory.Delete(VisualSnapshot.FolderOf(subjects[0]), recursive: true);

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await BuildScenario.StartAsync(o => o.Settings["appearance.theme"] = theme);
            await MessagesViewTests.BuildWithMessagesAsync(h);
            var dock = h.Find<Control>(AutomationIds.DockBottom);
            foreach (var size in sizes)
            {
                VisualSnapshot.Arrange(h.Window, size, 1.0);
                h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, 2000);
                var variant = $"{subjects[0]} {VisualSnapshot.FileName(theme, 1.0, size)}";
                Assert.Equal(size, h.Window.ClientSize);
                Assert.Equal((DockSizing.BottomDockMinHeight, DockSizing.BottomDockMinHeight), (dock.Bounds.Height, h.Services.Layout.Layout.BottomSize));
                foreach (var id in (string[])["Tool.Messages.ToolBar.messages.toggleErrors", "Tool.Messages.Column.Description", "Tool.Messages.Row.0",
                             "Tool.Messages.Row.0.Description", "ToolTab.Messages"])
                {
                    var control = h.Find<Control>(id);
                    Assert.True(control.IsEffectivelyVisible, $"{variant}: {id} is not shown");
                    if (VisualSnapshot.HiddenPart(control, h.Window) is { } hidden)
                        defects.Add((variant, $"cut: {id} — {hidden}"));
                }

                if (VisualSnapshot.DistinctColors(h.Window, h.Find<Control>("Tool.Messages.Row.0")) < 3)
                    defects.Add((variant, "the first row is not drawn"));
                // The list is one row high (24): its auto-hiding vertical scroll bar has no room for both arrow buttons
                // (2 × 16). Those two parts are the only ones allowed to be cut at the minimum; everything else must fit.
                defects.AddRange(VisualSnapshot.LayoutDefects(h.Window)
                    .Where(d => !(d.Contains("[Tool.Messages.List]", StringComparison.Ordinal)
                        && (d.Contains("#TrackRect", StringComparison.Ordinal) || d.Contains("#PART_LineDownButton", StringComparison.Ordinal))))
                    .Select(d => (variant, d)));
                files.Add((subjects[0], theme, size, VisualSnapshot.Capture(h.Window, subjects[0], theme)));
            }
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
