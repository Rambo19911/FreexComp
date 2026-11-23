using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of a debug tool window (tool-windows.md §10–§15): the empty-state block <c>Tool.&lt;Name&gt;.Empty</c> of
/// UI-TW-013 — heading in <c>text.secondary</c>, optional secondary line in <c>type.caption</c> <c>text.tertiary</c>,
/// centred horizontally at one third of the height — shown instead of <see cref="ContentHost"/> while the view-model
/// says the window is empty. The panel takes keyboard focus (<c>view.*</c>, F6).
/// </summary>
public abstract class DebugToolWindowView : UserControl
{
    private readonly TextBlock _heading = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _secondary = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _empty = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0) };
    private DebugToolWindowViewModel? _model;

    protected DebugToolWindowView(string toolName)
    {
        AutomationProperties.SetAutomationId(_empty, AutomationIds.Tool(toolName) + ".Empty");
        _heading.Bind(TextBlock.ForegroundProperty, _heading.GetResourceObservable("FxcTextSecondaryBrush"));
        _secondary.Classes.Add("caption");
        _secondary.Bind(TextBlock.ForegroundProperty, _secondary.GetResourceObservable("FxcTextTertiaryBrush"));
        _empty.Children.Add(_heading);
        _empty.Children.Add(_secondary);

        // One third of the height above the block, two thirds below.
        var placed = new Grid { RowDefinitions = new RowDefinitions("*,Auto,2*") };
        Grid.SetRow(_empty, 1);
        placed.Children.Add(_empty);
        var root = new Panel { Focusable = true, Background = Brushes.Transparent, Children = { ContentHost, placed } };
        ToolWindowFrame.SetIsPrimary(root, true);
        Content = root;
        DataContextChanged += (_, _) => Attach(DataContext as DebugToolWindowViewModel);
        Update();
    }

    /// <summary>Where the window's rows go; hidden while the empty state is shown.</summary>
    protected Panel ContentHost { get; } = new();

    private void Attach(DebugToolWindowViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        Update();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        _heading.Text = _model?.EmptyHeading;
        _secondary.Text = _model?.EmptySecondary;
        _secondary.IsVisible = _model?.EmptySecondary is not null;
        _empty.IsVisible = _model?.IsEmpty == true;
        ContentHost.IsVisible = _model?.IsEmpty == false;
    }
}

/// <summary>Breakpoints (tool-windows.md §10).</summary>
public sealed class BreakpointsView() : DebugToolWindowView(BreakpointsViewModel.ToolName);

/// <summary>Call Stack (tool-windows.md §11).</summary>
public sealed class CallStackView() : DebugToolWindowView(CallStackViewModel.ToolName);

/// <summary>Locals (tool-windows.md §12).</summary>
public sealed class LocalsView() : DebugToolWindowView(LocalsViewModel.ToolName);

/// <summary>Watches (tool-windows.md §13).</summary>
public sealed class WatchesView() : DebugToolWindowView(WatchesViewModel.ToolName);

/// <summary>Threads (tool-windows.md §14).</summary>
public sealed class ThreadsView() : DebugToolWindowView(ThreadsViewModel.ToolName);

/// <summary>Event Log (tool-windows.md §15).</summary>
public sealed class EventLogView() : DebugToolWindowView(EventLogViewModel.ToolName);
