using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Welcome page (welcome-page.md §1): product mark, start tiles, the recent-projects list and the start-up check
/// box. All state lives in <see cref="WelcomeDocumentViewModel"/>; the view maps keys and pointer gestures to it and
/// keeps keyboard focus on the nearest row after a change (IDE-WEL-006).
/// </summary>
public sealed class WelcomeDocumentView : UserControl
{
    internal const string RowPrefix = "Welcome.Recent.Row.";
    private const double StackBelowWidth = 700;

    private readonly ListBox _list;
    private readonly TextBox _filter;
    private readonly TextBlock _emptyHeading = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptySecondary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Control _empty;
    private readonly Grid _columns;
    private readonly Control _start;
    private readonly Control _recent;
    private WelcomeDocumentViewModel? _model;
    private Window? _window;

    public WelcomeDocumentView()
    {
        AutomationProperties.SetAutomationId(this, "Welcome");
        this.Bind(BackgroundProperty, this.GetResourceObservable("FxcBgEditorBrush"));

        _filter = new TextBox { Width = 220, PlaceholderText = Strings.Welcome_FilterPlaceholder, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(_filter, "Welcome.Recent.Filter");
        _filter.Bind(TextBox.TextProperty, new Binding(nameof(WelcomeDocumentViewModel.FilterText)) { Mode = BindingMode.TwoWay });
        _filter.Bind(IsVisibleProperty, new Binding(nameof(WelcomeDocumentViewModel.ShowFilter)));

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<RecentProjectRow>((row, _) => new RecentProjectRowView(this) { DataContext = row }),
            Background = Brushes.Transparent,
        };
        AutomationProperties.SetAutomationId(_list, "Welcome.Recent.List");
        _list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WelcomeDocumentViewModel.Projects)));
        _list.Bind(IsVisibleProperty, new Binding(nameof(WelcomeDocumentViewModel.HasRows)));
        _list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is RecentProjectRow row)
            {
                AutomationProperties.SetAutomationId(item, RowPrefix + row.Path);
                AutomationProperties.SetName(item, row.Name);
                item.MinHeight = 48;
            }
        };
        _list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel); // before the list's own keys
        _list.AddHandler(TappedEvent, OnListTapped);
        _list.ContextMenu = new ContextMenu();
        _list.AddHandler(ContextRequestedEvent, OnListContextRequested, RoutingStrategies.Tunnel); // fill before it opens

        _empty = CreateEmptyState();
        _start = CreateStartColumn();
        _recent = CreateRecentColumn();
        _columns = new Grid { Children = { _start, _recent } };
        _columns.SizeChanged += (_, e) => ArrangeColumns(e.NewSize.Width);
        ArrangeColumns(double.PositiveInfinity);

        var check = new CheckBox { Content = Strings.WelcomePage_ShowOnStartup };
        AutomationProperties.SetAutomationId(check, "Welcome.ShowOnStartup");
        check.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(WelcomeDocumentViewModel.ShowOnStartup)) { Mode = BindingMode.TwoWay });

        var column = new StackPanel
        {
            MaxWidth = 880,
            Spacing = 32,
            Margin = new Thickness(32, 32, 32, 32), // space.8
            Children = { CreateHeader(), _columns, check },
        };
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = column,
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model?.PropertyChanged -= OnModelChanged;
        _model = DataContext as WelcomeDocumentViewModel;
        _model?.PropertyChanged += OnModelChanged;
        UpdateEmptyState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        _window?.Activated += OnWindowActivated;
        _ = _model?.CheckMissingAsync(); // IDE-WEL-004: the page became visible
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.Activated -= OnWindowActivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>IDE-WEL-022: Ctrl+F focuses the filter box when it is shown.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && _filter.IsEffectivelyVisible)
        {
            _filter.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    internal void TogglePin(RecentProjectRow row)
    {
        if (_model?.TogglePin(row) is { } target)
            FocusRow(target);
    }

    internal void Remove(RecentProjectRow row)
    {
        if (_model is null)
            return;
        if (_model.Remove(row) is { } target)
            FocusRow(target);
        else
            _list.Focus();
    }

    private void OnWindowActivated(object? sender, EventArgs e) => _model?.OnWindowActivated();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WelcomeDocumentViewModel.EmptyText) or nameof(WelcomeDocumentViewModel.EmptySecondaryText))
            UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        _emptyHeading.Text = _model?.EmptyText;
        _emptySecondary.Text = _model?.EmptySecondaryText;
        _emptySecondary.IsVisible = _model?.EmptySecondaryText is not null;
        _empty.IsVisible = _model?.EmptyText is not null;
    }

    /// <summary>IDE-WEL-008: Enter opens, P toggles the pin, Delete removes the focused row.</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != Avalonia.Input.KeyModifiers.None
            || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not RecentProjectRow row)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _ = _model.OpenAsync(row);
                break;
            case Key.P:
                TogglePin(row);
                break;
            case Key.Delete:
                Remove(row);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>IDE-WEL-005: a click on a row opens it; clicks on its pin and remove buttons do not.</summary>
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null || e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not null
            || source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not RecentProjectRow row)
            return;
        _ = _model.OpenAsync(row);
    }

    /// <summary>IDE-WEL-008: the row under the pointer (or the focused row, keyboard) gets its context menu; elsewhere none.</summary>
    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not RecentProjectRow row)
        {
            e.Handled = true;
            return;
        }

        _list.SelectedItem = row;
        var menu = MenuBarBuilder.CreateContextMenu(_model.RowContextMenu(row, TogglePin, Remove));
        _list.ContextMenu!.Items.Clear();
        foreach (var item in menu.Items.Cast<Control>().ToList())
        {
            menu.Items.Remove(item);
            _list.ContextMenu.Items.Add(item);
        }
    }

    /// <summary>The row objects may be re-created by a re-sort: focus the entry once the list has its containers.</summary>
    private void FocusRow(RecentProjectRow target) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_model?.Projects.FirstOrDefault(p => p.Path == target.Path) is not { } row)
                return;
            _list.SelectedItem = row;
            _list.ContainerFromItem(row)?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);

    /// <summary>Two columns (280, gap space.8, rest ≥ 360); below 700 DIP they stack, Start above Recent.</summary>
    private void ArrangeColumns(double width)
    {
        var stacked = width < StackBelowWidth;
        _columns.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("280,32,*");
        _columns.RowDefinitions = stacked ? new RowDefinitions("Auto,32,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(_start, 0);
        Grid.SetRow(_start, 0);
        Grid.SetColumn(_recent, stacked ? 0 : 2);
        Grid.SetRow(_recent, stacked ? 2 : 0);
        _recent.MinWidth = stacked ? 0 : 360;
    }

    private Control CreateHeader()
    {
        var name = new TextBlock { Text = Strings.App_ProductName, Classes = { "title" } };
        var version = new TextBlock { Classes = { "caption" } };
        version.Bind(TextBlock.TextProperty, new Binding(nameof(WelcomeDocumentViewModel.VersionText)));
        version.Bind(TextBlock.ForegroundProperty, version.GetResourceObservable("FxcTextTertiaryBrush"));
        using var icon = AssetLoader.Open(new Uri("avares://FreexComp.Ide/Assets/app.ico"));
        var mark = new Image { Source = new Bitmap(icon), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top }; // icon.large
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { mark, new StackPanel { Children = { name, version } } },
        };
    }

    private static TextBlock SectionHead(string text)
    {
        var head = new TextBlock { Text = text, Classes = { "caption" }, LetterSpacing = 0.5, Margin = new Thickness(0, 0, 0, 8) };
        head.Bind(TextBlock.ForegroundProperty, head.GetResourceObservable("FxcTextSecondaryBrush"));
        return head;
    }

    private Control CreateStartColumn()
    {
        var panel = new StackPanel { Children = { SectionHead(Strings.WelcomePage_Start) } };
        foreach (var (id, icon, select) in new (string, string, Func<WelcomeDocumentViewModel, IdeCommand>)[]
        {
            ("Welcome.NewProject", "new-file", m => m.NewProject),
            ("Welcome.OpenProject", "open-folder", m => m.OpenProject),
            ("Welcome.OpenFile", "file", m => m.OpenFile),
        })
        {
            panel.Children.Add(CreateTile(id, icon, select));
        }

        return panel;
    }

    /// <summary>A flat 280 × 52 button: icon, command title, its current shortcut.</summary>
    private Button CreateTile(string automationId, string iconName, Func<WelcomeDocumentViewModel, IdeCommand> select)
    {
        var label = new TextBlock { Classes = { "body" } };
        var gesture = new TextBlock { Classes = { "caption" } };
        gesture.Bind(TextBlock.ForegroundProperty, gesture.GetResourceObservable("FxcTextTertiaryBrush"));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, gesture } };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { new IdeIcon { IconName = iconName, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, texts },
        };
        var tile = new Button
        {
            Width = 280,
            Height = 52,
            Content = content,
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(tile, automationId);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not WelcomeDocumentViewModel model)
                return;
            var command = select(model);
            tile.Command = command;
            label.Bind(TextBlock.TextProperty, new Binding(nameof(IdeCommand.Title)) { Source = command });
            gesture.Bind(TextBlock.TextProperty, new Binding(nameof(IdeCommand.GestureText)) { Source = command });
            AutomationProperties.SetName(tile, command.Title);
        };
        return tile;
    }

    private Control CreateRecentColumn()
    {
        var head = SectionHead(Strings.WelcomePage_RecentProjects);
        head.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8), Children = { head, _filter } };
        Grid.SetColumn(_filter, 1);
        return new StackPanel { Children = { top, _list, _empty } };
    }

    private Control CreateEmptyState()
    {
        _emptyHeading.Classes.Add("body");
        _emptyHeading.Bind(TextBlock.ForegroundProperty, _emptyHeading.GetResourceObservable("FxcTextSecondaryBrush"));
        _emptySecondary.Classes.Add("caption");
        _emptySecondary.Bind(TextBlock.ForegroundProperty, _emptySecondary.GetResourceObservable("FxcTextTertiaryBrush"));
        return new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0), Children = { _emptyHeading, _emptySecondary } };
    }
}

/// <summary>One recent-project row (IDE-WEL-002): pin toggle, name, path, date, remove button.</summary>
internal sealed class RecentProjectRowView : Grid
{
    private readonly WelcomeDocumentView _owner;
    private readonly Button _pin;
    private readonly TextBlock _pinGlyph = new();
    private readonly Button _remove;
    private readonly TextBlock _name = new() { Classes = { "bodyStrong" } };
    private readonly TextBlock _path = new() { Classes = { "caption" }, TextTrimming = TextTrimming.PrefixCharacterEllipsis };
    private readonly TextBlock _date = new() { Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Right };
    private RecentProjectRow? _row;
    private ListBoxItem? _item;

    public RecentProjectRowView(WelcomeDocumentView owner)
    {
        _owner = owner;
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        _pin = new Button { Content = _pinGlyph, ClickMode = ClickMode.Press, Focusable = false, Background = Brushes.Transparent, Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Top };
        _pin.Click += (_, e) =>
        {
            if (_row is not null)
                _owner.TogglePin(_row);
            e.Handled = true;
        };
        _remove = new Button
        {
            ClickMode = ClickMode.Press, // before the list item takes the press for its selection
            Focusable = false,
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            IsVisible = false,
            Content = new IdeIcon { IconName = "close", Width = 16, Height = 16 },
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(_remove, RecentProjectRow.RemoveToolTip);
        _remove.Click += (_, e) =>
        {
            if (_row is not null)
                _owner.Remove(_row);
            e.Handled = true;
        };
        _path.Bind(TextBlock.ForegroundProperty, _path.GetResourceObservable("FxcTextSecondaryBrush"));
        _date.Bind(TextBlock.ForegroundProperty, _date.GetResourceObservable("FxcTextTertiaryBrush"));
        _date.Margin = new Thickness(8, 0);
        SetColumn(_name, 1);
        SetColumn(_date, 2);
        SetColumn(_remove, 3);
        SetColumn(_path, 1);
        SetRow(_path, 1);
        SetColumnSpan(_path, 2);
        Children.Add(_pin);
        Children.Add(_name);
        Children.Add(_path);
        Children.Add(_date);
        Children.Add(_remove);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _row?.PropertyChanged -= OnRowChanged;
        _row = DataContext as RecentProjectRow;
        _row?.PropertyChanged += OnRowChanged;
        if (_row is not null)
        {
            AutomationProperties.SetAutomationId(_pin, WelcomeDocumentView.RowPrefix + _row.Path + ".Pin");
            AutomationProperties.SetAutomationId(_remove, WelcomeDocumentView.RowPrefix + _row.Path + ".Remove");
            AutomationProperties.SetAutomationId(_name, WelcomeDocumentView.RowPrefix + _row.Path + ".Name");
            _path.Text = _row.Path;
            ToolTip.SetTip(_path, _row.Path);
        }

        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _item = this.FindAncestorOfType<ListBoxItem>();
        _item?.PropertyChanged += OnItemChanged;
        UpdateRemoveVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _item?.PropertyChanged -= OnItemChanged;
        _item = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>IDE-WEL-002: the remove button shows while the row is hovered, selected or focused.</summary>
    private void OnItemChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty || e.Property == ListBoxItem.IsSelectedProperty || e.Property == IsKeyboardFocusWithinProperty)
            UpdateRemoveVisibility();
    }

    private void UpdateRemoveVisibility() =>
        _remove.IsVisible = IsPointerOver || _item is { } item && (item.IsPointerOver || item.IsSelected || item.IsKeyboardFocusWithin);

    private void Refresh()
    {
        if (_row is null)
            return;
        _pinGlyph.Text = _row.PinGlyph;
        ToolTip.SetTip(_pin, _row.PinToolTip);
        _name.Text = _row.DisplayName;
        _date.Text = _row.DateText;
        if (_row.IsMissing)
            _name.Bind(TextBlock.ForegroundProperty, _name.GetResourceObservable("FxcTextDisabledBrush"));
        else
            _name.ClearValue(TextBlock.ForegroundProperty);
    }
}
