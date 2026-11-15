using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The quick-pick popup of the main window (dialogs.md §13/§14, UI-DLG-130/140): drawn over the content 8 px below the
/// tool bar area, 560 wide, input row 34, up to 12 rows of 22; <c>bg.raised</c>, border, <c>radius.popup</c>,
/// <c>elevation.popup</c>. Light-dismiss: Escape, a click outside or focus leaving it closes it. The element focused
/// before opening gets the focus back when it closes (before an Enter action runs, UI-DLG-143).
/// </summary>
public sealed class QuickPickView : Border
{
    private readonly TextBox _input = new() { Height = 34, BorderThickness = new Thickness(0, 0, 0, 1), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single, MaxHeight = QuickPickViewModel.VisibleRows * 22 };
    private readonly TextBlock _noMatch = new() { Margin = new Thickness(8, 3), MinHeight = 16 };
    private QuickPickHost? _host;
    private QuickPickViewModel? _model;
    private IInputElement? _previous;
    private readonly DockPanel _panel = new();
    private bool _syncing;

    public QuickPickView()
    {
        Width = 560;
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 8, 0, 0);
        BorderThickness = new Thickness(1);
        ZIndex = 100;
        Bind(BackgroundProperty, this.GetResourceObservable("FxcBgRaisedBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        Bind(CornerRadiusProperty, this.GetResourceObservable("FxcRadiusPopup"));
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<QuickPickItem>((item, _) => item is null || _model is null ? new Border() : CreateRow(_model, item), supportsRecycling: false);
        _list.SelectionChanged += (_, _) =>
        {
            if (_model is not null && !_syncing && _list.SelectedIndex >= 0)
                _model.SelectedIndex = _list.SelectedIndex;
        };
        _list.DoubleTapped += (_, _) => _ = _model?.CommitAsync();
        _list.Focusable = false;
        _noMatch.Bind(TextBlock.ForegroundProperty, _noMatch.GetResourceObservable("FxcTextDisabledBrush"));
        _input.TextChanged += (_, _) =>
        {
            if (_model is not null && !_syncing)
                _model.Input = _input.Text ?? "";
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_model is not null && !IsKeyboardFocusWithin && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not null)
            {
                _previous = null; // focus went elsewhere on purpose
                _model.Close();
            }
        }), RoutingStrategies.Bubble, handledEventsToo: true);
        DockPanel.SetDock(_input, Avalonia.Controls.Dock.Top);
        _panel.Children.Add(_input);
        _panel.Children.Add(new Panel { Children = { _list, _noMatch } });
    }

    public bool IsOpen => _model is not null;

    /// <summary>Connects the popup to the host of the main window's view-model.</summary>
    public void Attach(QuickPickHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_host is not null)
            _host.PropertyChanged -= OnHostChanged;
        _host = host;
        host.PropertyChanged += OnHostChanged;
    }

    /// <summary>A pointer press in the main window outside the popup closes it.</summary>
    public void OnPointerPressedOutside(object? source)
    {
        if (_model is not null && (source is not Visual visual || (visual != this && !this.IsVisualAncestorOf(visual))))
            _model.Close();
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuickPickHost.Current))
            return;
        if (_model is not null)
            Detach();
        if (_host?.Current is { } model)
            Open(model);
    }

    private void Open(QuickPickViewModel model)
    {
        _model = model;
        _previous = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        model.PropertyChanged += OnModelChanged;
        AutomationProperties.SetAutomationId(this, model.Name);
        AutomationProperties.SetAutomationId(_input, model.Name + ".Input");
        AutomationProperties.SetAutomationId(_list, model.Name + ".List");
        AutomationProperties.SetAutomationId(_noMatch, model.Name + ".NoMatch");
        AutomationProperties.SetName(_input, model.AccessibleName);
        AutomationProperties.SetName(_list, model.AccessibleName);
        Child = _panel; // the controls exist only while the popup is open
        _syncing = true;
        _input.Text = model.Input;
        _syncing = false;
        ApplyElevation();
        IsVisible = true;
        Sync();
        _input.Focus(NavigationMethod.Tab);
    }

    /// <summary><c>elevation.popup</c> from the theme tokens.</summary>
    private void ApplyElevation()
    {
        var theme = ActualThemeVariant;
        if (this.TryFindResource("FxcShadowPopup", theme, out var color) && color is Color shadow)
        {
            var blur = this.TryFindResource("FxcElevationPopupBlur", theme, out var b) && b is double bv ? bv : 0;
            var offset = this.TryFindResource("FxcElevationPopupOffsetY", theme, out var o) && o is double ov ? ov : 0;
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = offset, Blur = blur, Color = shadow });
        }
    }

    /// <summary>Closed: hidden, and the previously focused element gets the focus back (synchronously).</summary>
    private void Detach()
    {
        var model = _model!;
        model.PropertyChanged -= OnModelChanged;
        _model = null;
        _list.ItemsSource = null;
        IsVisible = false;
        Child = null;
        AutomationProperties.SetAutomationId(_input, null);
        AutomationProperties.SetAutomationId(_list, null);
        AutomationProperties.SetAutomationId(_noMatch, null);
        AutomationProperties.SetAutomationId(this, null);
        if (_previous is InputElement previous && previous is Visual visual && visual.IsAttachedToVisualTree() && previous.IsEffectivelyVisible)
            previous.Focus(NavigationMethod.Tab);
        _previous = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuickPickViewModel.Items) or nameof(QuickPickViewModel.SelectedIndex))
            Sync();
    }

    private void Sync()
    {
        if (_model is not { } model)
            return;
        _syncing = true;
        if (!ReferenceEquals(_list.ItemsSource, model.Items))
            _list.ItemsSource = model.Items;
        _list.SelectedIndex = model.SelectedIndex;
        _syncing = false;
        if (model.SelectedItem is { } item)
            _list.ScrollIntoView(item);
        _noMatch.Text = model.NoMatchText;
        _noMatch.IsVisible = model.NoMatchText is not null;
        _list.IsVisible = !_noMatch.IsVisible;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model || e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Up:
                model.Move(-1);
                break;
            case Key.Down:
                model.Move(1);
                break;
            case Key.PageUp:
                model.Move(-QuickPickViewModel.VisibleRows);
                break;
            case Key.PageDown:
                model.Move(QuickPickViewModel.VisibleRows);
                break;
            case Key.Enter:
                _ = model.CommitAsync();
                break;
            case Key.Escape:
                model.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static Control CreateRow(QuickPickViewModel model, QuickPickItem item)
    {
        var id = $"{model.Name}.Item.{item.Id}";
        var (textCell, detailCell) = model.CellNames;
        var panel = new DockPanel { MinHeight = 22, Margin = new Thickness(8, 0), Background = Brushes.Transparent, IsEnabled = item.IsEnabled };
        AutomationProperties.SetAutomationId(panel, id);
        AutomationProperties.SetName(panel, item.Text);
        if (model.ShowsFileIcon)
        {
            var icon = new IdeIcon { IconName = "file", Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
            panel.Children.Add(icon);
        }

        var foreground = item.IsEnabled ? "FxcTextPrimaryBrush" : "FxcTextDisabledBrush";
        var detail = new TextBlock
        {
            Text = item.Detail,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = model.ShowsFileIcon ? TextTrimming.LeadingCharacterEllipsis : TextTrimming.None,
            MaxWidth = 260,
            Margin = new Thickness(12, 0, 0, 0),
        };
        detail.Classes.Add("caption");
        detail.Bind(TextBlock.ForegroundProperty, detail.GetResourceObservable(model.ShowsFileIcon ? "FxcTextTertiaryBrush" : "FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(detail, id + "." + detailCell);
        DockPanel.SetDock(detail, Avalonia.Controls.Dock.Right);
        panel.Children.Add(detail);

        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(foreground));
        AutomationProperties.SetAutomationId(text, id + "." + textCell);
        var marked = item.Highlights.ToHashSet();
        var start = 0;
        for (var i = 1; i <= item.Text.Length; i++)
        {
            if (i < item.Text.Length && marked.Contains(i) == marked.Contains(start))
                continue;
            var run = new Run(item.Text[start..i]);
            if (marked.Contains(start))
            {
                run.FontWeight = FontWeight.Bold; // UI-ED-115: matched characters
                run.Bind(TextElement.ForegroundProperty, text.GetResourceObservable(item.IsEnabled ? "FxcAccentDefaultBrush" : "FxcTextDisabledBrush"));
            }

            text.Inlines!.Add(run);
            start = i;
        }

        panel.Children.Add(text);
        return panel;
    }
}
