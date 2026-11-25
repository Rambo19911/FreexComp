using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Structure tool window (tool-windows.md §3): a flat list of the visible tree rows (<c>Tool.Structure.Row.&lt;key&gt;</c>,
/// error rows with the cells <c>.Message</c> and <c>.Position</c>), indented by depth with a chevron for expandable rows;
/// Left/Right collapse/expand, Enter and double-click run <c>structure.goToSource</c>; the tracked row is selected without
/// taking the focus. Empty state <c>Tool.Structure.Empty</c> (<c>Msg.Structure.Empty</c>). In the Design view
/// (UI-TW-207) the list allows several selected rows, kept equal to the surface selection; non-visual components carry
/// a gear overlay, unknown or read-only ones are drawn in <c>text.disabled</c> with a warning overlay.
/// </summary>
public sealed class StructureView : UserControl
{
    private const string Prefix = "Tool.Structure";
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_Structure_Empty, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private StructureViewModel? _model;
    private bool _syncing;

    public StructureView()
    {
        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_structure);
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<StructureRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += (_, e) =>
        {
            if (_model is null || _syncing)
                return;
            if (_model.IsDesign)
            {
                // The row selected last becomes the primary selection on the surface.
                var added = e.AddedItems.OfType<StructureRow>().ToList();
                var rows = (_list.SelectedItems?.OfType<StructureRow>() ?? []).Except(added).Concat(added).ToList();
                _model.SelectRows(rows);
            }
            else
            {
                _model.SelectedRow = _list.SelectedItem as StructureRow;
            }
        };
        _list.DoubleTapped += async (_, _) =>
        {
            if (_model?.SelectedRow is { } row)
                await _model.GoToSourceAsync(row);
        };
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.ContextRequested += OnContextRequested;
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        _empty.Bind(TextBlock.ForegroundProperty, _empty.GetResourceObservable("FxcTextSecondaryBrush"));
        Content = new Panel { Children = { _list, _empty } };
        DataContextChanged += (_, _) => Attach(DataContext as StructureViewModel);
    }

    private void Attach(StructureViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        _list.ItemsSource = model?.Rows;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        Sync();
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StructureViewModel.SelectedRow) or nameof(StructureViewModel.SelectedRows)
            or nameof(StructureViewModel.IsEmpty) or nameof(StructureViewModel.IsDesign))
            Sync();
    }

    private void Sync()
    {
        _empty.IsVisible = _model?.IsEmpty != false;
        _list.IsVisible = !_empty.IsVisible;
        _syncing = true;
        if (_model is { IsDesign: true } design)
        {
            _list.SelectionMode = SelectionMode.Multiple;
            if (_list.SelectedItems is { } items)
            {
                items.Clear();
                foreach (var selected in design.SelectedRows)
                    items.Add(selected);
            }
        }
        else
        {
            _list.SelectionMode = SelectionMode.Single;
            _list.SelectedItem = _model?.SelectedRow;
        }

        _syncing = false;
        if (_model?.SelectedRow is { } row)
            _list.ScrollIntoView(row);
    }

    /// <summary>Design view rows use the designer context menu, verb group first (tool-windows.md §3, DSG-SUR-191).</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model?.BuildDesignContextMenu() is not { } items)
            return;
        _list.ContextMenu?.Close();
        var menu = MenuBarBuilder.CreateContextMenu(items);
        _list.ContextMenu = menu;
        menu.Open(_list);
        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model?.SelectedRow is not { } row || e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _model.GoToSourceAsync(row);
                break;
            case Key.Right when row.HasChildren && !row.IsExpanded:
            case Key.Left when row.HasChildren && row.IsExpanded:
                e.Handled = true;
                _model.SetExpanded(row, e.Key == Key.Right);
                _list.Focus();
                break;
        }
    }

    private Control CreateRow(StructureRow row)
    {
        var id = $"{Prefix}.Row.{row.Node.Key}";
        var panel = new DockPanel { MinHeight = 22, Margin = new Thickness(4 + 16 * row.Depth, 0, 4, 0), Background = Avalonia.Media.Brushes.Transparent };
        AutomationProperties.SetAutomationId(panel, id);
        // UI-DSG-136: an inherited object's screen-reader name names the frame it comes from.
        AutomationProperties.SetName(panel, row.Node.InheritedFrom is { } frame
            ? row.Node.Text + string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Designer_InheritedSuffix, frame)
            : row.Node.Text);
        var chevron = new Panel { Width = 14 };
        if (row.HasChildren)
        {
            chevron.Children.Add(new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8 });
            chevron.PointerPressed += (_, e) =>
            {
                _model?.SetExpanded(row, !row.IsExpanded);
                e.Handled = true;
            };
        }

        DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
        panel.Children.Add(chevron);
        if (row.Node.Kind == StructureKind.Error)
        {
            var position = new TextBlock { Text = row.Node.Position, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            position.Bind(TextBlock.ForegroundProperty, position.GetResourceObservable("FxcTextTertiaryBrush"));
            AutomationProperties.SetAutomationId(position, id + ".Position");
            DockPanel.SetDock(position, Avalonia.Controls.Dock.Right);
            panel.Children.Add(position);
        }

        // UI-TW-203: the symbol-kind icon (16).
        var kind = new IdeIcon
        {
            IconName = SymbolIcons.For(row.Node.Kind), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0), Classes = { "symbol" },
        };
        DockPanel.SetDock(kind, Avalonia.Controls.Dock.Left);
        panel.Children.Add(kind);

        // UI-TW-201: the visibility overlay of a member; UI-TW-207: the gear overlay of a non-visual component, the warning
        // overlay of an unknown or read-only one.
        var lockIcon = SymbolIcons.VisibilityOverlay(row.Node.Visibility);
        foreach (var overlay in (ReadOnlySpan<(bool Shown, string? Icon, string Class)>)[(lockIcon is not null, lockIcon, "visibility"),
                     (row.Node.IsNonVisual, "settings", "overlay"), (row.Node.IsUnavailable, "warning", "overlay")])
        {
            if (!overlay.Shown)
                continue;
            var glyph = new IdeIcon { IconName = overlay.Icon, Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            glyph.Classes.Add(overlay.Class);
            DockPanel.SetDock(glyph, Avalonia.Controls.Dock.Left);
            panel.Children.Add(glyph);
        }

        var text = new TextBlock { Text = row.Node.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(row.Node.Kind == StructureKind.Error ? "FxcStatusErrorBrush"
            : row.Node.IsUnavailable ? "FxcTextDisabledBrush" : row.Node.InheritedFrom is not null ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush"));
        if (row.Node.Kind == StructureKind.Error)
            AutomationProperties.SetAutomationId(text, id + ".Message");
        if (row.Node.Visibility is { } visibility)
            ToolTip.SetTip(text, visibility);
        panel.Children.Add(text);
        return panel;
    }
}
