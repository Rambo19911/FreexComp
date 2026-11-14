using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Editor;

/// <summary>
/// The find / replace panel of one editor (dialogs.md §10, editor.md §10): an overlay at the top-right of the text
/// area over the document's <see cref="FindSession"/> (UI-ED-102: not AvaloniaEdit's search panel). The state lives in
/// the session and the shared <see cref="Core.Services.IFindService"/>; this view mirrors it and maps the panel's keys
/// (UI-DLG-101). AutomationIds <c>&lt;editor id&gt;.FindPanel</c> and <c>.FindPanel.&lt;Element&gt;</c>.
/// </summary>
public sealed class FindPanelView : Border
{
    private readonly Func<bool> _focusText;
    private readonly ToggleButton _toggleReplace = Toggle("›", Strings.FindPanel_ToggleReplace);
    private readonly TextBox _searchText = Field(Strings.FindPanel_SearchText);
    private readonly TextBlock _matchCount = new() { Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "caption" } };
    private readonly ToggleButton _matchCase = Toggle("Aa", Strings.FindPanel_MatchCase);
    private readonly ToggleButton _wholeWord = Toggle(new TextBlock { Text = "W", TextDecorations = TextDecorations.Underline }, Strings.FindPanel_WholeWord);
    private readonly ToggleButton _regex = Toggle(".*", Strings.FindPanel_Regex);
    private readonly ToggleButton _inSelection = Toggle("≡", Strings.FindPanel_InSelection);
    private readonly Button _previous = Small("↑", Strings.FindPanel_Previous);
    private readonly Button _next = Small("↓", Strings.FindPanel_Next);
    private readonly Button _close = Small("×", Strings.FindPanel_Close);
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, IsVisible = false };
    private readonly TextBox _replaceText = Field(Strings.FindPanel_ReplaceText);
    private readonly Button _replace = new() { Content = Strings.FindPanel_Replace };
    private readonly Button _replaceAll = new() { Content = Strings.FindPanel_ReplaceAll };
    private readonly StackPanel _replaceButtons;
    private readonly History _searchHistory;
    private readonly History _replaceHistory;
    private FindSession? _session;
    private bool _syncing;

    /// <param name="focusText">Returns the focus to the editor's text (Close, Escape).</param>
    public FindPanelView(Func<bool> focusText)
    {
        _focusText = focusText;
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        MinWidth = 420; // UI-ED-100
        BorderThickness = new Thickness(1, 0, 1, 1);
        Padding = new Thickness(4, 4, 4, 3); // with the 1 px bottom border: 34 / 66 high (UI-ED-100)
        Bind(this, BackgroundProperty, "FxcBgRaisedBrush");
        Bind(this, BorderBrushProperty, "FxcBorderDefaultBrush");
        Bind(this, CornerRadiusProperty, "FxcRadiusPopup");
        Bind(_matchCount, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
        Bind(_error, TextBlock.ForegroundProperty, "FxcStatusErrorBrush");
        AutomationProperties.SetName(this, Strings.FindPanel_Name);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        // Like a text field: only Global commands reach the keymap from here; the editor's keys (Tab, Enter, F3…) are the panel's.
        Input.CommandRouting.SetScope(this, Core.Commands.CommandScope.Global);
        _toggleReplace.IsTabStop = false; // reached with Ctrl+H (UI-DLG-101 lists no tab stop for it)
        ToolTip.SetTip(_replace, Strings.FindPanel_ReplaceTip);
        ToolTip.SetTip(_replaceAll, Strings.FindPanel_ReplaceAllTip);
        AutomationProperties.SetName(_replace, Strings.FindPanel_Replace);
        AutomationProperties.SetName(_replaceAll, Strings.FindPanel_ReplaceAll);
        _replaceButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Children = { _replace, _replaceAll } };

        // The children are in tab order (UI-DLG-101); the grid places them.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,200,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 2,
        };
        // UI-ED-100: 34 high with the find row (4 + 26 + 4), 66 with the replace row (+ 6 + 26); gaps as margins so that
        // the hidden rows take no space.
        _replaceText.Margin = new Thickness(0, 6, 0, 0);
        _replaceButtons.Margin = new Thickness(0, 6, 0, 0);
        _replace.Height = _replaceAll.Height = 26;
        _error.Margin = new Thickness(0, 4, 0, 0);
        Place(grid, _searchText, 0, 1);
        Place(grid, _replaceText, 2, 1);
        Place(grid, _matchCase, 0, 3);
        Place(grid, _wholeWord, 0, 4);
        Place(grid, _regex, 0, 5);
        Place(grid, _inSelection, 0, 6);
        Place(grid, _previous, 0, 7);
        Place(grid, _next, 0, 8);
        Place(grid, _close, 0, 9);
        Place(grid, _replaceButtons, 2, 2, span: 8);
        Place(grid, _toggleReplace, 0, 0);
        Place(grid, _matchCount, 0, 2);
        Place(grid, _error, 1, 1, span: 9);
        Child = grid;

        _searchHistory = new History(_searchText, () => _session?.Service.SearchHistory ?? []);
        _replaceHistory = new History(_replaceText, () => _session?.Service.ReplaceHistory ?? []);
        _searchText.TextChanged += (_, _) =>
        {
            if (!_syncing && _session is { } s)
                s.Service.SearchText = _searchText.Text ?? "";
        };
        _replaceText.TextChanged += (_, _) =>
        {
            if (!_syncing && _session is { } s)
                s.Service.ReplaceText = _replaceText.Text ?? "";
        };
        _toggleReplace.Click += (_, _) => _session?.SetReplaceMode(_toggleReplace.IsChecked == true);
        _matchCase.Click += (_, _) => Toggle(s => s.Service.MatchCase = !s.Service.MatchCase);
        _wholeWord.Click += (_, _) => Toggle(s => s.Service.WholeWord = !s.Service.WholeWord);
        _regex.Click += (_, _) => Toggle(s => s.Service.Regex = !s.Service.Regex);
        _inSelection.Click += (_, _) => Toggle(s => s.InSelection = !s.InSelection);
        _previous.Click += async (_, _) => await FindNextAsync(forward: false);
        _next.Click += async (_, _) => await FindNextAsync(forward: true);
        _close.Click += (_, _) => Close();
        _replace.Click += async (_, _) => await ReplaceAsync();
        _replaceAll.Click += async (_, _) => await ReplaceAllAsync();
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => UpdateShadow();
        AttachedToVisualTree += (_, _) => UpdateShadow();
    }

    /// <summary>Sets the AutomationIds from the editor's id (<c>Editor.&lt;fileName&gt;</c> or <c>Document.&lt;Kind&gt;</c>).</summary>
    public void SetAutomationIds(string editorId)
    {
        var id = editorId + ".FindPanel";
        AutomationProperties.SetAutomationId(this, id);
        foreach (var (control, name) in new (Control, string)[]
                 {
                     (_toggleReplace, "ToggleReplace"), (_searchText, "SearchText"), (_matchCount, "MatchCount"), (_matchCase, "MatchCase"),
                     (_wholeWord, "WholeWord"), (_regex, "Regex"), (_inSelection, "InSelection"), (_previous, "Previous"), (_next, "Next"),
                     (_close, "Close"), (_error, "Error"), (_replaceText, "ReplaceText"), (_replace, "Replace"), (_replaceAll, "ReplaceAll"),
                 })
            AutomationProperties.SetAutomationId(control, id + "." + name);
    }

    /// <summary>The document's find state; null unbinds (the panel hides).</summary>
    public FindSession? Session
    {
        get => _session;
        set
        {
            if (_session == value)
                return;
            if (_session is { } old)
            {
                old.Changed -= OnSessionChanged;
                old.FocusRequested -= OnFocusRequested;
                old.Service.QueryChanged -= OnSessionChanged;
            }

            _session = value;
            if (value is not null)
            {
                value.Changed += OnSessionChanged;
                value.FocusRequested += OnFocusRequested;
                value.Service.QueryChanged += OnSessionChanged;
            }

            Sync();
        }
    }

    private static void Bind(Control target, AvaloniaProperty property, string resourceKey) =>
        target.Bind(property, target.GetResourceObservable(resourceKey));

    private static void Place(Grid grid, Control control, int row, int column, int span = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, span);
        grid.Children.Add(control);
    }

    private static TextBox Field(string name)
    {
        var box = new TextBox { Width = 200, Height = 26, MinHeight = 26, AcceptsReturn = false, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static ToggleButton Toggle(object content, string tip)
    {
        var button = new ToggleButton { Content = content, MinWidth = 20, Padding = new Thickness(4, 1) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    private static Button Small(string content, string tip)
    {
        var button = new Button { Content = content, MinWidth = 20, Padding = new Thickness(4, 1) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    /// <summary>UI-ED-100: <c>elevation.popup</c> (shadow colour of the theme, blur 8, offset (0, 2)).</summary>
    private void UpdateShadow()
    {
        var color = this.TryFindResource("FxcShadowPopup", ActualThemeVariant, out var value) && value is Color c ? c : Colors.Transparent;
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 8, Color = color });
    }

    private void Toggle(Action<FindSession> change)
    {
        if (_session is { } s)
            change(s);
        Sync(); // a refused change (In selection without a range) springs back
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Sync();

    /// <summary>The controls follow the session and the shared query.</summary>
    private void Sync()
    {
        var s = _session;
        IsVisible = s is { IsOpen: true };
        if (s is null)
            return;
        _syncing = true;
        try
        {
            var service = s.Service;
            if (_searchText.Text != service.SearchText)
                _searchText.Text = service.SearchText;
            if (_replaceText.Text != service.ReplaceText)
                _replaceText.Text = service.ReplaceText;
            _toggleReplace.IsChecked = s.IsReplaceMode;
            _toggleReplace.Content = s.IsReplaceMode ? "⌄" : "›";
            _matchCase.IsChecked = service.MatchCase;
            _wholeWord.IsChecked = service.WholeWord;
            _regex.IsChecked = service.Regex;
            _inSelection.IsEnabled = s.CanUseInSelection;
            _inSelection.IsChecked = s.InSelection;
            _replaceText.IsVisible = _replaceButtons.IsVisible = s.IsReplaceMode;
            _replace.IsEnabled = _replaceAll.IsEnabled = s.CanReplace;
            var counter = s.CounterText;
            _matchCount.Text = counter;
            if (counter.Length > 0 && s.Matches.Count == 0)
                Bind(_matchCount, TextBlock.ForegroundProperty, "FxcStatusErrorBrush"); // Msg.Find.NoMatches
            else
                Bind(_matchCount, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
            _error.Text = s.Error;
            _error.IsVisible = s.Error is not null;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        Sync();
        if (!FocusSearch())
            Dispatcher.UIThread.Post(() => FocusSearch(), DispatcherPriority.Loaded); // shown in this turn, not laid out yet
    }

    private bool FocusSearch()
    {
        if (!_searchText.Focus(NavigationMethod.Tab))
            return false;
        _searchText.SelectAll();
        return true;
    }

    private void Close()
    {
        _session?.Close();
        _focusText();
    }

    private async Task FindNextAsync(bool forward)
    {
        if (_session is { } s)
            await s.FindNextAsync(forward);
    }

    private async Task ReplaceAsync()
    {
        if (_session is { IsReplaceMode: true, CanReplace: true } s)
            await s.ReplaceAsync();
    }

    private async Task ReplaceAllAsync()
    {
        if (_session is { IsReplaceMode: true, CanReplace: true } s)
            await s.ReplaceAllAsync();
    }

    /// <summary>UI-DLG-101: the panel's keys, before the text fields see them.</summary>
    private async void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_session is not { } s)
            return;
        var mods = e.KeyModifiers;
        var inSearch = e.Source == _searchText || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _searchText;
        var inReplace = !inSearch && (e.Source == _replaceText || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _replaceText);
        Func<Task>? action = (e.Key, mods) switch
        {
            (Key.Escape, KeyModifiers.None) => () => { Close(); return Task.CompletedTask; },
            (Key.Enter, KeyModifiers.None) when inSearch => () => FindNextAsync(forward: true),
            (Key.Enter, KeyModifiers.Shift) when inSearch => () => FindNextAsync(forward: false),
            (Key.Enter, KeyModifiers.None) when inReplace => ReplaceAsync,
            (Key.F3, KeyModifiers.None) => () => FindNextAsync(forward: true),
            (Key.F3, KeyModifiers.Shift) => () => FindNextAsync(forward: false),
            (Key.A, KeyModifiers.Alt) => ReplaceAllAsync,
            (Key.C, KeyModifiers.Alt) => () => Run(() => Toggle(x => x.Service.MatchCase = !x.Service.MatchCase)),
            (Key.W, KeyModifiers.Alt) => () => Run(() => Toggle(x => x.Service.WholeWord = !x.Service.WholeWord)),
            (Key.R, KeyModifiers.Alt) => () => Run(() => Toggle(x => x.Service.Regex = !x.Service.Regex)),
            (Key.L, KeyModifiers.Alt) => () => Run(() => Toggle(x => x.InSelection = !x.InSelection)),
            (Key.F, KeyModifiers.Control) => () => Run(() => s.Open(replace: false)),
            (Key.H, KeyModifiers.Control) => () => Run(() => s.SetReplaceMode(!s.IsReplaceMode)),
            (Key.Up, KeyModifiers.None) when inSearch => () => Run(() => _searchHistory.Step(older: true)),
            (Key.Down, KeyModifiers.None) when inSearch => () => Run(() => _searchHistory.Step(older: false)),
            (Key.Up, KeyModifiers.None) when inReplace => () => Run(() => _replaceHistory.Step(older: true)),
            (Key.Down, KeyModifiers.None) when inReplace => () => Run(() => _replaceHistory.Step(older: false)),
            _ => null,
        };
        if (action is null)
            return;
        e.Handled = true;
        await action();

        static Task Run(Action a)
        {
            a();
            return Task.CompletedTask;
        }
    }

    /// <summary>Up / Down in a text field step through its history (UI-DLG-101); typing starts over.</summary>
    private sealed class History
    {
        private readonly TextBox _box;
        private readonly Func<IReadOnlyList<string>> _entries;
        private int _index = -1;
        private string _typed = "";
        private string? _stepped;

        public History(TextBox box, Func<IReadOnlyList<string>> entries)
        {
            _box = box;
            _entries = entries;
            box.TextChanged += (_, _) =>
            {
                if (_box.Text != _stepped)
                    _index = -1; // edited: the next step starts from the newest entry
            };
        }

        public void Step(bool older)
        {
            var entries = _entries();
            if (_index < 0)
                _typed = _box.Text ?? "";
            var index = older ? Math.Min(_index + 1, entries.Count - 1) : _index - 1;
            if (index == _index || index < -1)
                return;
            _index = index;
            _stepped = index < 0 ? _typed : entries[index];
            _box.Text = _stepped;
            _box.CaretIndex = _stepped.Length;
        }
    }
}
