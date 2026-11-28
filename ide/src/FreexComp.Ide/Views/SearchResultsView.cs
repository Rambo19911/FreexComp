using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Search Results tool window (tool-windows.md §8): the header strip <c>Tool.SearchResults.Header</c>, the
/// capped info bar, the multi-selection list of file and match rows (<c>Tool.SearchResults.Row.&lt;key&gt;</c>, match
/// cells <c>.Line</c> and <c>.Text</c> with the match span on <c>editor.findMatch.bg</c>) and the empty states. A
/// References result set (UI-TW-609…614) adds container rows between them and the kind badge (<c>.Kind</c>) on its
/// reference rows. Double-click is the default action (UI-TW-603); Right/Left expand and collapse file and container rows.
/// While a result set is pinned the set strip <c>Tool.SearchResults.Sets</c> lies above the header (UI-TW-615): one tab
/// per set (<c>Tool.SearchResults.Set.&lt;n&gt;</c> with its close button <c>.Close</c>); the strip is one tab stop —
/// Left/Right change the shown set, Delete closes it, Down goes on to the rows.
/// </summary>
public sealed class SearchResultsView : UserControl
{
    private const string Prefix = "Tool.SearchResults";

    /// <summary>UI-TW-610: the slot of a kind badge.</summary>
    public const double BadgeSize = 16;

    /// <summary>UI-TW-609: a container row starts one level in (under the file row's icon).</summary>
    private const double ContainerIndent = 18;

    /// <summary>UI-TW-609: a reference row starts two levels in (under the container's name).</summary>
    private const double ReferenceIndent = 32;

    /// <summary>UI-TW-615: the height of the set strip.</summary>
    public const double StripHeight = 26;

    /// <summary>UI-TW-615: the close button of a tab.</summary>
    public const double CloseSize = 16;

    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _header = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _headerStrip = new() { Height = 26 };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_SearchResults_Empty, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private readonly TextBlock _none = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private readonly InfoBarView _capped = new() { AutomationIdBase = Prefix + ".InfoBar" };
    private readonly Border _strip = new() { Height = StripHeight, Focusable = true, FocusAdorner = null, IsVisible = false };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Border _stripRing = new() { BorderThickness = new Thickness(2), IsVisible = false, IsHitTestVisible = false };
    private SearchResultsViewModel? _model;
    private bool _syncing;
    private bool _restoreScroll; // a set was shown: its scroll position is set once more after the list's posted calls
    private int _shownTab; // the position of the tab that was brought into view last (0: none)

    public SearchResultsView()
    {
        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_searchResults);
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<SearchResultRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += (_, _) =>
        {
            if (_model is not null && !_syncing)
                _model.SelectedRows = [.. _list.SelectedItems?.OfType<SearchResultRow>() ?? []];
        };
        _list.DoubleTapped += (_, e) =>
        {
            if (_model is null || RowAt(e.Source) is not { } row)
                return;
            e.Handled = true;
            _ = _model.GoToAsync(row);
        };
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Input.ListBehaviors.FocusOnRightPress(_list); // UI-TW-014 (I-283)
        _list.ContextRequested += (_, e) =>
        {
            if (_model is null)
                return;
            MenuBarBuilder.CreateContextMenu(_model.BuildContextMenu()).Open(_list);
            e.Handled = true;
        };

        AutomationProperties.SetAutomationId(_header, Prefix + ".Header");
        _header.Classes.Add("caption");
        _header.Bind(TextBlock.ForegroundProperty, _header.GetResourceObservable("FxcTextPrimaryBrush"));
        _headerStrip.Child = _header;
        _headerStrip.Bind(Border.BackgroundProperty, _headerStrip.GetResourceObservable("FxcBgHeaderBrush"));
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        _empty.Bind(TextBlock.ForegroundProperty, _empty.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(_none, Prefix + ".None");
        _none.Bind(TextBlock.ForegroundProperty, _none.GetResourceObservable("FxcTextSecondaryBrush"));

        // UI-TW-615: the set strip — a tab strip on bg.sunken, one tab stop, with its own focus ring (keyboard focus only).
        AutomationProperties.SetAutomationId(_strip, Prefix + ".Sets");
        AutomationProperties.SetName(_strip, Strings.SearchResults_Sets);
        _strip.Bind(Border.BackgroundProperty, _strip.GetResourceObservable("FxcBgSunkenBrush"));
        _stripRing.Bind(Border.BorderBrushProperty, _stripRing.GetResourceObservable("FxcBorderFocusBrush"));
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, // more tabs than fit: the shown one is brought into view
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tabs,
        };
        _strip.Child = new Panel { Children = { tabScroll, _stripRing } };
        _strip.KeyDown += OnStripKeyDown;
        _strip.GotFocus += (_, e) => _stripRing.IsVisible = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        _strip.LostFocus += (_, _) => _stripRing.IsVisible = false;
        _strip.PointerWheelChanged += (_, e) =>
        {
            tabScroll.Offset = new Vector(Math.Clamp(tabScroll.Offset.X - (e.Delta.Y * 48), 0, Math.Max(0, tabScroll.Extent.Width - tabScroll.Viewport.Width)), 0);
            e.Handled = true;
        };

        var top = new StackPanel { Children = { _strip, _headerStrip, _capped } };
        var panel = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new Panel { Children = { _list, _none } });
        Content = new Panel { Children = { panel, _empty } };
        DataContextChanged += (_, _) => Attach(DataContext as SearchResultsViewModel);
    }

    private void Attach(SearchResultsViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.ShownSetChanging -= OnShownSetChanging;
            _model.ShownSetChanged -= OnShownSetChanged;
        }

        _model = model;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
            model.ShownSetChanging += OnShownSetChanging;
            model.ShownSetChanged += OnShownSetChanged;
        }

        Update();
        UpdateStrip();
        // IDE-FREF-003: a result set shown before this view existed (the window was hidden) has its current row selected.
        if (_model?.CurrentRow is { } current && _list.SelectedItem is null)
        {
            _list.SelectedItem = current;
            _list.ScrollIntoView(current);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchResultsViewModel.CurrentRow) && _model?.CurrentRow is { } current)
        {
            _restoreScroll = false; // the current row is what must be in view now
            _list.SelectedItem = current;
            _list.ScrollIntoView(current);
        }
        else if (e.PropertyName is nameof(SearchResultsViewModel.Rows) or nameof(SearchResultsViewModel.Header) or nameof(SearchResultsViewModel.IsEmpty)
                 or nameof(SearchResultsViewModel.IsCapped))
        {
            Update();
        }
        else if (e.PropertyName == nameof(SearchResultsViewModel.Sets))
        {
            UpdateStrip();
        }
    }

    private ScrollViewer? ListScroll => _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>UI-TW-618: the scroll position belongs to the set — it is stored when another set is about to be shown.</summary>
    private void OnShownSetChanging(object? sender, EventArgs e)
    {
        if (_model is not null && ListScroll is { } scroll)
            _model.ScrollOffset = scroll.Offset.Y;
    }

    /// <summary>
    /// UI-TW-618: … and comes back with its set — now, and once more after the list's own posted call: the list scrolls
    /// to its selected row in a call it posts when the selection is set (it runs before the background priority), and
    /// the selection of the set came back just before.
    /// </summary>
    private void OnShownSetChanged(object? sender, EventArgs e)
    {
        RestoreScroll();
        _restoreScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_restoreScroll)
                RestoreScroll();
            _restoreScroll = false;
        }, DispatcherPriority.Background);
    }

    private void RestoreScroll()
    {
        if (_model is null || ListScroll is not { } scroll)
            return;
        var offset = _model.ScrollOffset;
        _list.UpdateLayout(); // the rows of the shown set are measured: the extent is theirs
        scroll.Offset = new Vector(scroll.Offset.X, offset);
    }

    /// <summary>UI-TW-615: the strip is shown while a set is pinned; one tab per set, the shown one selected and in view.</summary>
    private void UpdateStrip()
    {
        var sets = _model?.HasSetStrip == true ? _model.Sets : [];
        while (_tabs.Children.Count > sets.Count)
            _tabs.Children.RemoveAt(_tabs.Children.Count - 1);
        while (_tabs.Children.Count < sets.Count)
            _tabs.Children.Add(new SetTab(this, _tabs.Children.Count + 1));
        for (var i = 0; i < sets.Count; i++)
            ((SetTab)_tabs.Children[i]).Show(sets[i]);
        var hadFocus = _strip.IsKeyboardFocusWithin;
        _strip.IsVisible = sets.Count > 0;
        if (sets.Count == 0 && hadFocus)
        {
            // The strip went away under the focus (its last pinned set was closed): the focus goes on to the rows of the
            // set that is shown now — they are controls only after a layout.
            _list.UpdateLayout();
            this.FindAncestorOfType<ToolWindowFrame>()?.FocusPrimary();
        }
        var shown = _tabs.Children.OfType<SetTab>().FirstOrDefault(t => t.IsShown);
        var number = shown is null ? 0 : _tabs.Children.IndexOf(shown) + 1;
        if (shown is not null && number != _shownTab)
            Dispatcher.UIThread.Post(shown.BringIntoView, DispatcherPriority.Loaded); // after the layout of a new tab
        _shownTab = number;
    }

    /// <summary>UI-TW-615: the strip is a tab strip — Left / Right change the shown set, Delete closes it; Down goes on to the rows.</summary>
    private void OnStripKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                _model.ShowNeighbourSet(e.Key == Key.Right);
                break;
            case Key.Delete:
                _model.Clear();
                break;
            case Key.Down:
                FocusRows();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The keyboard focus goes to the rows: to the selected row when it is in view, else as <c>view.searchResults</c> does.</summary>
    private void FocusRows()
    {
        if (_list.SelectedItem is { } selected && _list.ContainerFromItem(selected) is { IsEffectivelyVisible: true } row)
            row.Focus(NavigationMethod.Directional);
        else
            this.FindAncestorOfType<ToolWindowFrame>()?.FocusPrimary();
    }

    /// <summary>
    /// A tab of the set strip (UI-TW-615): pin glyph when pinned, the icon of the set's kind, the title, and the close
    /// button — shown on the selected tab and on the tab under the pointer. The selected tab is drawn as the active tab
    /// of a dock (UI-TW-003): <c>bg.panel</c>, <c>text.primary</c>, a 2 px <c>accent.default</c> line at its bottom edge.
    /// </summary>
    private sealed class SetTab : Border
    {
        private readonly SearchResultsView _owner;
        private readonly int _number;
        private readonly IdeIcon _pin = new() { IconName = "pin", Width = 16, Height = 16, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly IdeIcon _icon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _close = new()
        {
            Width = CloseSize,
            Height = CloseSize,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false, // the strip is the tab stop (Delete closes the shown set)
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new IdeIcon { IconName = "close", Width = 16, Height = 16 },
        };

        private IDisposable? _background;
        private IDisposable? _foreground;
        private IDisposable? _line;

        public SetTab(SearchResultsView owner, int number)
        {
            _owner = owner;
            _number = number;
            var id = $"{Prefix}.Set.{number}";
            AutomationProperties.SetAutomationId(this, id);
            AutomationProperties.SetAutomationId(_close, id + ".Close");
            AutomationProperties.SetName(_close, Strings.SearchResults_CloseSet);
            ToolTip.SetTip(_close, Strings.SearchResults_CloseSet);
            _close.Click += (_, e) =>
            {
                e.Handled = true;
                _owner._model?.CloseSet(_number);
            };
            _title.Classes.Add("caption");
            var separator = new Rectangle { Width = 1 };
            separator.Bind(Shape.FillProperty, separator.GetResourceObservable("FxcBorderSubtleBrush"));
            DockPanel.SetDock(separator, Avalonia.Controls.Dock.Right);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 4, 0),
                Children = { _pin, _icon, _title, new Panel { Width = CloseSize, Height = CloseSize, Margin = new Thickness(6, 0, 0, 0), Children = { _close } } },
            };
            BorderThickness = new Thickness(0, 0, 0, 2); // the selected tab's line
            Child = new DockPanel { Children = { separator, row } };
        }

        public bool IsShown { get; private set; }

        public void Show(SearchResultSetTab tab)
        {
            IsShown = tab.IsShown;
            _pin.IsVisible = tab.IsPinned;
            _icon.IconName = tab.IconName;
            _title.Text = tab.Title;
            ToolTip.SetTip(this, tab.Tooltip);
            AutomationProperties.SetName(this, tab.IsPinned ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.SearchResults_SetKept, tab.Title) : tab.Title);
            _foreground?.Dispose();
            _foreground = _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable(tab.IsShown ? "FxcTextPrimaryBrush" : "FxcTextSecondaryBrush"));
            _line?.Dispose();
            _line = tab.IsShown ? this.Bind(BorderBrushProperty, this.GetResourceObservable("FxcAccentDefaultBrush")) : null;
            if (!tab.IsShown)
                BorderBrush = Brushes.Transparent;
            UpdateState();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsPointerOverProperty)
                UpdateState();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return; // a press on the close button is the button's
            e.Handled = true;
            _owner._model?.ShowSet(_number); // the keyboard focus is the strip's: Left / Right go on from here
        }

        /// <summary>The background of the selected, the hovered and any other tab; the close button on the first two.</summary>
        private void UpdateState()
        {
            _close.IsVisible = IsShown || IsPointerOver;
            _background?.Dispose();
            _background = IsShown ? this.Bind(BackgroundProperty, this.GetResourceObservable("FxcBgPanelBrush"))
                : IsPointerOver ? this.Bind(BackgroundProperty, this.GetResourceObservable("FxcBgHoverBrush"))
                : null;
            if (_background is null)
                Background = Brushes.Transparent; // hit-testable over its whole area
        }
    }

    private void Update()
    {
        var rows = _model?.Rows ?? [];
        if (!ReferenceEquals(_list.ItemsSource, rows))
        {
            var selected = _model?.SelectedRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal) ?? [];
            _syncing = true;
            _list.ItemsSource = rows;
            KeyboardNavigation.SetTabOnceActiveElement(_list, null); // the row Tab went to last belongs to the rows that left
            foreach (var row in rows.Where(r => selected.Contains(r.Key)))
                _list.SelectedItems?.Add(row);
            _syncing = false;
            if (_model is not null)
                _model.SelectedRows = [.. _list.SelectedItems?.OfType<SearchResultRow>() ?? []];
        }

        _header.Text = _model?.Header ?? "";
        var empty = _model?.IsEmpty ?? true;
        _empty.IsVisible = empty;
        _headerStrip.IsVisible = !empty;
        _list.IsVisible = !empty;
        _none.IsVisible = _model?.IsNoMatch == true;
        _none.Text = _model?.NoMatchText ?? "";
        // UI-TW-606 / IDE-FREF-006: the text names what was capped — matches or references.
        if (_model is not null && (_capped.DataContext as InfoBarViewModel)?.Text != _model.CappedText)
            _capped.DataContext = new InfoBarViewModel("Capped", InfoBarSeverity.Info, _model.CappedText, [], null);
        _capped.IsVisible = _model?.IsCapped == true;
    }

    private SearchResultRow? RowAt(object? source)
    {
        for (var element = source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is Control { Tag: SearchResultRow row })
                return row;
        }

        return _list.SelectedItem as SearchResultRow;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Right or Key.Left))
            return;
        if (_list.SelectedItem is SearchResultRow { Match: null } row && row.IsExpanded != (e.Key == Key.Right))
        {
            var key = row.Key;
            _model.SetExpanded(row, e.Key == Key.Right);
            _list.SelectedItem = _model.Rows.FirstOrDefault(r => r.Key == key);
            e.Handled = true;
        }
    }

    private static Control CreateRow(SearchResultRow row)
    {
        var id = $"{Prefix}.Row.{row.Key}";
        var panel = new DockPanel { MinHeight = 22, Background = Brushes.Transparent, Tag = row };
        AutomationProperties.SetAutomationId(panel, id);
        if (row.IsFile)
        {
            AutomationProperties.SetName(panel, row.FileName);
            panel.Margin = new Thickness(4, 0);
            var chevron = new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
            panel.Children.Add(chevron);
            var icon = new IdeIcon { IconName = "file", Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0) };
            DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
            panel.Children.Add(icon);
            var directory = new TextBlock
            {
                Text = row.Directory,
                TextTrimming = TextTrimming.LeadingCharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 320,
                Margin = new Thickness(8, 0, 0, 0), // never touches the name, its count and the library suffix
            };
            directory.Bind(TextBlock.ForegroundProperty, directory.GetResourceObservable("FxcTextTertiaryBrush"));
            DockPanel.SetDock(directory, Avalonia.Controls.Dock.Right);
            panel.Children.Add(directory);
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Run(row.FileName) { FontWeight = FontWeight.SemiBold });
            name.Inlines.Add(new Run($"  ({row.Count})"));
            if (row.IsLibrary)
            {
                // UI-TW-609: a library source says so after its count.
                var library = new Run("   " + Strings.Msg_References_Library);
                library.Bind(TextElement.ForegroundProperty, name.GetResourceObservable("FxcTextTertiaryBrush"));
                name.Inlines.Add(library);
            }

            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
            panel.Children.Add(name);
            return panel;
        }

        if (row.IsContainer)
        {
            // UI-TW-609: one level in — the container's name and the number of its references.
            AutomationProperties.SetName(panel, row.Container);
            panel.Margin = new Thickness(ContainerIndent, 0, 4, 0);
            var chevron = new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
            panel.Children.Add(chevron);
            var name = new TextBlock { Text = $"{row.Container}  ({row.Count})", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Classes.Add("body");
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
            panel.Children.Add(name);
            return panel;
        }

        AutomationProperties.SetName(panel, row.Text);
        panel.Margin = new Thickness(row.ReferenceKind is null ? 26 : ReferenceIndent, 0, 4, 0);
        if (row.ReferenceKind is { } kind)
        {
            // UI-TW-610: the kind as a letter badge in a 16 × 16 slot; the letter carries the meaning, the colour supports it.
            var letter = new TextBlock
            {
                Text = SearchResultsViewModel.KindLetter(kind),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            letter.Classes.Add("caption");
            letter.Bind(TextBlock.ForegroundProperty, letter.GetResourceObservable(IconCatalog.BrushKey(SearchResultsViewModel.KindToken(kind))));
            var badge = new Border { Width = BadgeSize, Height = BadgeSize, Child = letter, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            AutomationProperties.SetAutomationId(badge, id + ".Kind");
            AutomationProperties.SetName(badge, SearchResultsViewModel.KindName(kind));
            ToolTip.SetTip(badge, SearchResultsViewModel.KindName(kind));
            DockPanel.SetDock(badge, Avalonia.Controls.Dock.Left);
            panel.Children.Add(badge);
        }

        var line = new TextBlock { Text = row.LineText, Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        line.Classes.Add("codeSmall");
        line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(line, id + ".Line");
        var colon = new TextBlock { Text = ":  ", VerticalAlignment = VerticalAlignment.Center };
        colon.Bind(TextBlock.ForegroundProperty, colon.GetResourceObservable("FxcTextTertiaryBrush"));
        DockPanel.SetDock(line, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(colon, Avalonia.Controls.Dock.Left);
        panel.Children.Add(line);
        panel.Children.Add(colon);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("code");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(text, id + ".Text");
        var start = Math.Clamp(row.HighlightStart, 0, row.Text.Length);
        var length = Math.Clamp(row.HighlightLength, 0, row.Text.Length - start);
        if (start > 0)
            text.Inlines!.Add(new Run(row.Text[..start]));
        var match = new Run(row.Text.Substring(start, length));
        match.Bind(TextElement.BackgroundProperty, text.GetResourceObservable("FxcEditorFindMatchBgBrush"));
        text.Inlines!.Add(match);
        if (start + length < row.Text.Length)
            text.Inlines.Add(new Run(row.Text[(start + length)..]));
        panel.Children.Add(text);
        return panel;
    }
}
