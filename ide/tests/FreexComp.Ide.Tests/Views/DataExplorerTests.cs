using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Data Explorer placeholder tool window (I-137; tool-windows.md §16): a static panel with a heading and one text,
/// registered from Phase 7, hidden until <c>view.dataExplorer</c> shows it.
/// </summary>
[Trait("Phase", "7")]
[Trait("Kind", "Ui")]
public sealed class DataExplorerTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1400")]
    [Trait("Req", "UI-TW-1401")]
    public async Task PlaceholderShown()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 7);
        var tools = h.Services.ToolWindows;
        var layout = h.Services.Layout.Layout;

        // UI-TW-1401: registered from P7, in the left dock, hidden by default.
        Assert.True(tools.IsRegistered(DataExplorerViewModel.ToolName));
        Assert.Equal(["ProjectManager", "Structure"], layout.Left.Tools);
        Assert.Null(h.TryFind<Control>("Tool.DataExplorer"));
        Assert.True(h.Services.Commands.CanExecute("view.dataExplorer"));

        // ... and shown by view.dataExplorer: third tab of the left dock, active, with keyboard focus (UI-TW-007).
        await h.ExecuteAsync("view.dataExplorer");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["ProjectManager", "Structure", "DataExplorer"], layout.Left.Tools);
        Assert.Equal(DataExplorerViewModel.ToolName, layout.Left.ActiveTool);
        var frame = h.Find<Control>("Tool.DataExplorer");
        Assert.True(frame.IsEffectivelyVisible);
        var model = Assert.IsType<DataExplorerViewModel>(tools.Get(DataExplorerViewModel.ToolName));
        Assert.Equal(Strings.Cmd_view_dataExplorer, model.Title);

        // UI-TW-1400: a static panel — no tool bar, no filter, no rows, no context menu.
        Assert.Empty(model.ToolBarCommands);
        Assert.False(model.HasFilter);
        Assert.Null(model.ToolBarChoice);
        Assert.False(model.HasCaptionMenu);
        Assert.Empty(frame.GetVisualDescendants().OfType<ItemsControl>());
        Assert.DoesNotContain(frame.GetVisualDescendants().OfType<Control>(), c => c.ContextMenu is not null || c.ContextFlyout is not null);

        // The empty-state block (UI-TW-013): heading over the text, centred, at one third of the content height.
        var empty = h.Find<Control>("Tool.DataExplorer.Empty");
        Assert.True(empty.IsEffectivelyVisible);
        var texts = empty.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Equal([Strings.Msg_DataExplorer_Heading, Strings.Msg_DataExplorer_Placeholder], texts.Select(t => t.Text));
        var (heading, text) = (texts[0], texts[1]);
        Assert.Equal("Data Explorer", heading.Text);
        Assert.Equal("Browsing database connections arrives in a later version. Use the data components on the Tool Palette to work with SQLite files.",
            text.Text);

        Assert.Contains("subtitle", heading.Classes); // type.subtitle, text.secondary
        Assert.Equal(14, heading.FontSize);
        Assert.Equal(Brush("FxcTextSecondaryBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(heading.Foreground).Color);
        Assert.Contains("body", text.Classes); // type.body, text.tertiary, centred, max width 220
        Assert.Equal(12, text.FontSize);
        Assert.Equal(Brush("FxcTextTertiaryBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal(TextAlignment.Center, text.TextAlignment);
        Assert.Equal(220, text.MaxWidth);
        Assert.InRange(text.Bounds.Width, 1, 220);
        Assert.True(text.Bounds.Height > 18, "the text wraps over several lines");
        Assert.Equal(HorizontalAlignment.Center, heading.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, text.HorizontalAlignment);

        var block = Assert.IsType<StackPanel>(heading.GetVisualParent());
        Assert.Same(block, text.GetVisualParent());
        Assert.Equal(4, block.Spacing); // space.2
        var top = block.TranslatePoint(default, empty)!.Value.Y;
        Assert.Equal((empty.Bounds.Height - block.Bounds.Height) / 3, top, 1.0);
        var centre = text.TranslatePoint(new Point(text.Bounds.Width / 2, 0), empty)!.Value.X;
        Assert.Equal(empty.Bounds.Width / 2, centre, 1.0);

        // The panel is focusable — the view.* command and F6 reach it — and Escape returns focus to the editor.
        Assert.True(model.IsActive);
        Assert.Equal("Tool.DataExplorer", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.False(model.IsActive);
        Assert.True(frame.IsEffectivelyVisible); // Escape does not hide
        h.Press("F6"); // the pane after the document area is the left dock (UI-A11Y-003)
        Assert.Equal("Tool.DataExplorer", h.FocusedAutomationId);
        Assert.True(model.IsActive);

        Color Brush(string key) =>
            h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
                ? brush.Color
                : throw new InvalidOperationException(key);
    }

    /// <summary>UI-TW-006: before Phase 7 the window is not registered and its command is disabled.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1401")]
    public async Task NotRegisteredBeforePhase7()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.ImplementedPhase = 6);

        Assert.False(h.Services.ToolWindows.IsRegistered(DataExplorerViewModel.ToolName));
        Assert.False(h.Services.Commands.CanExecute("view.dataExplorer"));
        Assert.Null(h.TryFind<Control>("Tool.DataExplorer"));
    }
}
