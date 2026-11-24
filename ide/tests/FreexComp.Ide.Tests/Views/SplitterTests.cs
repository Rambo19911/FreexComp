using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The dock splitters (main-window.md UI-WIN-005).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class SplitterTests
{
    private const string UiState = "roaming/uistate.json";

    private static void Docks(HarnessOptions o)
    {
        o.ImplementedPhase = 1;
        o.ToolWindows.Add(SampleToolViewModel.Definition("ProjectManager", ToolGroupId.Left, titleKey: "Cmd_view_projectManager"));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Messages", ToolGroupId.Bottom));
        o.ToolWindows.Add(SampleToolViewModel.Definition("Output", ToolGroupId.Bottom, titleKey: "Cmd_view_output"));
    }

    /// <summary>
    /// I-250: a splitter drag, a keyboard resize and a stored <c>uistate.json</c> value all stop at the minimum height of
    /// the bottom dock (the layout too low for both minimums: <c>DockSizingTests</c>).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-005")]
    [Trait("Req", "UI-WIN-054")]
    public async Task BottomDock_ClampsToMinimum()
    {
        const double Min = DockSizing.BottomDockMinHeight;
        Assert.Equal(128, Min);
        byte[] saved;
        await using (var h = await HeadlessHarness.StartAsync(Docks))
        {
            var layout = h.Services.Layout.Layout;
            var bottom = h.Find<Control>(AutomationIds.DockBottom);
            var documents = h.Find<Control>(AutomationIds.DocumentArea);
            var splitter = h.Find<GridSplitter>(AutomationIds.Splitter(AutomationIds.DockBottom));
            var view = bottom.GetVisualAncestors().OfType<LayoutView>().Single();
            Assert.Equal(200, bottom.Bounds.Height);

            // Pointer: far below the minimum.
            h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, 2000);
            Assert.Equal((Min, Min), (bottom.Bounds.Height, layout.BottomSize));

            // Keyboard: the arrow keys on the splitter never pass the minimum either.
            h.DoubleClick(AutomationIds.Splitter(AutomationIds.DockBottom));
            Assert.Equal(200, bottom.Bounds.Height);
            splitter.Focus(NavigationMethod.Tab);
            for (var i = 0; i < 40; i++)
                h.Press("Down");
            Dispatcher.UIThread.RunJobs();
            Assert.True(bottom.Bounds.Height < 200, "the arrow key did not move the splitter");
            Assert.Equal(Min, bottom.Bounds.Height);

            // The upper limit is unchanged: available height − 160.
            h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, -2000);
            Assert.Equal(160, documents.Bounds.Height);
            Assert.Equal(view.Bounds.Height - 160 - 5, layout.BottomSize);

            layout.BottomSize = 333.5;
            Dispatcher.UIThread.RunJobs();
            h.Window.Close();
            Dispatcher.UIThread.RunJobs();
            saved = File.ReadAllBytes(Path.Combine(h.UserDir, UiState));
        }

        // A stored height below the minimum is raised when the file is read.
        var text = System.Text.Encoding.UTF8.GetString(saved);
        Assert.Contains("333.5", text, StringComparison.Ordinal);
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            Docks(o);
            o.UserFiles[UiState] = System.Text.Encoding.UTF8.GetBytes(text.Replace("333.5", "40", StringComparison.Ordinal));
        }))
        {
            Assert.Equal(Min, h.Services.Layout.Layout.BottomSize);
            Assert.Equal(Min, h.Find<Control>(AutomationIds.DockBottom).Bounds.Height);
        }
    }
}
